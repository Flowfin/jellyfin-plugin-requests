using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Time;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.FullDiskProbe;

/// <summary>
/// Asks the shipped store what a caller sees when the volume it writes to runs out of room.
///
/// The third condition of issue #46 is the one of the three where the wrong outcome looks exactly
/// like the right one: a request that was never written, on a call that returned normally. The
/// other two conditions are met by tests that truncate and interrupt, and both of those run in the
/// ordinary suite. This one cannot, because a suite that needs a volume it may fill is a suite that
/// needs a container engine, which the headless rule in docs/testing.md refuses.
///
/// So the measurement is made here, against the store this repository ships, on a filesystem whose
/// size is fixed by whoever starts the container. Nothing outside that mount is touched.
///
/// What it reports:
///
///   - a write eventually failed, rather than the filesystem turning out to be large enough that
///     nothing was measured
///   - the caller was told, and what it was told
///   - the store still answers with the set it held before the failed write
///   - a store opened fresh over the same directory loads, and holds that same set
///
/// Exit codes: 0 all four hold, 1 one of them does not, 2 no write ever failed.
/// </summary>
internal static class Program
{
    /// <summary>
    /// How many additions are attempted before the run is abandoned. It is a bound rather than a
    /// loop that never ends, and it is what turns "the mount was not size limited" into a refusal
    /// instead of a green run that measured nothing. A missing or mistyped size argument is the
    /// one-character mistake this exists against.
    /// </summary>
    private const int Bound = 200;

    /// <summary>
    /// Characters of display title per request. Large so the document outgrows a small mount in a
    /// few writes: the store serialises the whole set on every write, so a record small enough to
    /// need thousands of them turns this into a long run rather than a better one.
    /// </summary>
    private const int TitleLength = 8192;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            await Console.Error.WriteLineAsync("usage: Jellyfin.Plugin.Requests.FullDiskProbe <directory on a size limited filesystem>").ConfigureAwait(false);
            return 1;
        }

        var directory = args[0];
        var logger = new ConsoleLogger();
        var clock = new SystemClock();

        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "== the store writes into {0}", directory));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "== running on {0}", Environment.Version));

        var store = new FileRequestStore(directory, logger, clock);
        var accepted = new List<Guid>();
        Exception? refusal = null;

        for (var ordinal = 0; ordinal < Bound; ordinal++)
        {
            var request = ARequest(ordinal);

            try
            {
                await store.AddAsync(request, CancellationToken.None).ConfigureAwait(false);
                accepted.Add(request.Id);
            }
            catch (Exception reason) when (reason is not OutOfMemoryException)
            {
                refusal = reason;
                break;
            }
        }

        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "== {0} of at most {1} additions were accepted", accepted.Count, Bound));

        if (refusal is null)
        {
            await Console.Error.WriteLineAsync(string.Format(
                CultureInfo.InvariantCulture,
                "NOTHING WAS MEASURED. {0} additions all succeeded, so the filesystem under {1} never ran out of room. This run says nothing about what a caller sees on a full disk. Check that the mount is size limited.",
                Bound,
                directory)).ConfigureAwait(false);
            return 2;
        }

        Console.WriteLine("== what the caller was told");
        Console.WriteLine(refusal.GetType().FullName);
        Console.WriteLine(refusal.Message);

        var wrong = new List<string>();

        if (refusal is not IOException)
        {
            wrong.Add(string.Format(
                CultureInfo.InvariantCulture,
                "the caller was told {0}, which is not the IOException a filesystem raises when it is out of room",
                refusal.GetType().FullName));
        }

        var stillHeld = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "== the store that saw the failure now reports {0}", stillHeld.Count));

        if (!SameSet(stillHeld, accepted))
        {
            wrong.Add("the store that saw the failure does not report the set it held before the write that failed");
        }

        IReadOnlyList<StoredRequest> reopened;

        try
        {
            reopened = await new FileRequestStore(directory, logger, clock).GetAllAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (RequestStoreLoadException reason)
        {
            Console.WriteLine("== a store opened fresh over the same directory refused to load");
            Console.WriteLine(reason.Message);
            wrong.Add("a store opened fresh over the same directory does not load");
            reopened = Array.Empty<StoredRequest>();
        }

        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "== a store opened fresh over the same directory reports {0}", reopened.Count));

        if (!SameSet(reopened, accepted))
        {
            wrong.Add("a store opened fresh over the same directory does not hold the set that was accepted before the write that failed");
        }

        Console.WriteLine("== what is left on the mount");

        foreach (var file in new DirectoryInfo(directory).EnumerateFiles().OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}\t{1}", file.Length, file.Name));
        }

        if (wrong.Count > 0)
        {
            foreach (var line in wrong)
            {
                await Console.Error.WriteLineAsync(line).ConfigureAwait(false);
            }

            return 1;
        }

        Console.WriteLine("== the caller was told, and nothing that was accepted was lost");
        return 0;
    }

    private static bool SameSet(IReadOnlyList<StoredRequest> held, IReadOnlyList<Guid> accepted)
        => held.Select(stored => stored.Request.Id).OrderBy(id => id).SequenceEqual(accepted.OrderBy(id => id));

    private static MediaRequest ARequest(int ordinal)
    {
        var asked = new DateTimeOffset(2026, 8, 8, 8, 0, 0, TimeSpan.Zero);

        return new MediaRequest
        {
            Id = new Guid(string.Format(CultureInfo.InvariantCulture, "00000000-0000-0000-0000-{0:D12}", ordinal)),
            RequestedByUserId = new Guid("b31d0f9a-5c2e-4a71-8f6b-0d4c3e2a1b58"),
            RequestedAt = asked,
            StateChangedAt = asked,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = new string('t', TitleLength)
        };
    }

    /// <summary>
    /// The store writes to a log before it throws, and on this run that log is evidence rather than
    /// noise, so it goes to the output the job records instead of being dropped.
    /// </summary>
    private sealed class ConsoleLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "[{0}] {1}", logLevel, formatter(state, exception)));
        }
    }
}

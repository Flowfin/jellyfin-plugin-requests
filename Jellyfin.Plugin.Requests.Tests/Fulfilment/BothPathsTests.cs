using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Fulfilment;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using MediaBrowser.Model.Tasks;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Fulfilment;

/// <summary>
/// The fourth condition on #42: the match runs on a library event and on a schedule, and neither
/// path is the only one that works.
/// <para>
/// Each of the two is exercised with the other one absent, which is what "neither is the only one"
/// means. A test that started both and asserted the outcome would pass with either of them removed,
/// and the failure this condition exists against is exactly that: a server that was off when the
/// file arrived hears no event, and a server that is running should not wait hours to notice.
/// </para>
/// </summary>
public class BothPathsTests
{
    private static readonly DateTimeOffset Asked = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The scheduled task, with nothing subscribed to the library at all, moves a request whose
    /// title arrived while nobody was listening.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheScheduledRunFulfilsWithNothingListeningToTheLibrary()
    {
        var store = new InMemoryRequestStore();
        var library = new FakeLibrary();
        var task = new FulfilmentTask(Sweep(store, library));

        await store.AddAsync(Request(), CancellationToken.None);
        library.Put(RequestedItemKind.Movie, "Tmdb", "603");

        var progress = new RecordedProgress();
        await task.ExecuteAsync(progress, CancellationToken.None);

        Assert.Equal(RequestState.Fulfilled, (await Only(store)).State);
        Assert.Equal([0d, 100d], progress.Reported);
    }

    /// <summary>
    /// The library event, with the scheduled task never run, moves the same request. Stopping the
    /// watcher is what says the queued change has been handled, so nothing here waits for time to
    /// pass.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheLibraryEventFulfilsWithTheScheduledRunNeverExecuted()
    {
        var store = new InMemoryRequestStore();
        var library = new FakeLibrary();

        using var watcher = new LibraryWatcher(library, Sweep(store, library), new RecordingLogger());

        await store.AddAsync(Request(), CancellationToken.None);
        library.Put(RequestedItemKind.Movie, "Tmdb", "603");

        await watcher.StartAsync(CancellationToken.None);
        library.Raise(RequestedItemKind.Movie, Identifiers());
        await watcher.StopAsync(CancellationToken.None);

        Assert.Equal(RequestState.Fulfilled, (await Only(store)).State);
    }

    /// <summary>
    /// A change raised before the watcher started, or after it stopped, is not handled by it. That
    /// is the gap the scheduled run closes, and it is asserted rather than argued so the two paths
    /// are known to be needed rather than believed to be.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AChangeRaisedWhileNothingIsListeningIsCaughtByTheScheduledRunInstead()
    {
        var store = new InMemoryRequestStore();
        var library = new FakeLibrary();
        var sweep = Sweep(store, library);

        using var watcher = new LibraryWatcher(library, sweep, new RecordingLogger());

        await store.AddAsync(Request(), CancellationToken.None);
        library.Put(RequestedItemKind.Movie, "Tmdb", "603");

        // The server was not running when the file arrived, which is the whole case.
        library.Raise(RequestedItemKind.Movie, Identifiers());

        await watcher.StartAsync(CancellationToken.None);
        await watcher.StopAsync(CancellationToken.None);

        Assert.Equal(RequestState.Open, (await Only(store)).State);

        await new FulfilmentTask(sweep).ExecuteAsync(new RecordedProgress(), CancellationToken.None);

        Assert.Equal(RequestState.Fulfilled, (await Only(store)).State);
    }

    /// <summary>
    /// One library item that throws does not stop the ones behind it in the queue. A scan raises
    /// thousands of these and the loop handling them is the only thing handling any of them.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task OneChangeThatFailsDoesNotStopTheNextOne()
    {
        var store = new InMemoryRequestStore();
        var library = new FakeLibrary();
        var log = new RecordingLogger();
        var refusing = new StoreThatRefusesTheFirstLookup(store);

        using var watcher = new LibraryWatcher(
            library,
            new FulfilmentSweep(refusing, library, new TestClock(Asked), new RecordingJournal(), log),
            log);

        await store.AddAsync(Request(), CancellationToken.None);
        library.Put(RequestedItemKind.Movie, "Tmdb", "603");

        await watcher.StartAsync(CancellationToken.None);
        library.Raise(RequestedItemKind.Movie, Identifiers());
        library.Raise(RequestedItemKind.Movie, Identifiers());
        await watcher.StopAsync(CancellationToken.None);

        Assert.Equal(RequestState.Fulfilled, (await Only(store)).State);
        Assert.NotEmpty(log.At(Microsoft.Extensions.Logging.LogLevel.Error));
    }

    /// <summary>
    /// The task tells the server to run it at startup as well as on an interval, because the gap it
    /// exists to close is the one a server that was off has just been through.
    /// </summary>
    [Fact]
    public void TheScheduledRunHappensAtStartupAndOnAnInterval()
    {
        var triggers = new FulfilmentTask(
            Sweep(new InMemoryRequestStore(), new FakeLibrary())).GetDefaultTriggers().ToList();

        Assert.Contains(triggers, trigger => trigger.Type == TaskTriggerInfoType.StartupTrigger);
        Assert.Contains(
            triggers,
            trigger => trigger.Type == TaskTriggerInfoType.IntervalTrigger && trigger.IntervalTicks > 0);
    }

    private static FulfilmentSweep Sweep(IRequestStore store, ILibrary library)
        => new FulfilmentSweep(store, library, new TestClock(Asked), new RecordingJournal(), new RecordingLogger());

    private static async Task<MediaRequest> Only(InMemoryRequestStore store)
        => (await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true)).Single().Request;

    private static Dictionary<string, string> Identifiers()
        => new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "603" };

    private static MediaRequest Request()
        => new MediaRequest
        {
            Id = new Guid("2b8f4c61-0d75-4a39-9e26-3f5a8c1d7b04"),
            RequestedByUserId = new Guid("7a4e2f18-6b0c-4d39-95e7-1f8a3c6d2b40"),
            RequestedAt = Asked,
            StateChangedAt = Asked,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = "The one that was asked for",
            ProviderIds = Identifiers()
        };
}

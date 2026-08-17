using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Storage;

/// <summary>
/// What the three query paths cost at a size worth caring about, as a bound the suite holds rather
/// than as a sentence in a document. A store that is fine at fifty records and unusable at ten
/// thousand is a decision nobody made, and this is where the decision is kept honest.
/// <para>
/// Each leg measures the workload the path actually carries rather than one call of it. A single
/// call answered by walking ten thousand records takes under a millisecond, so a bound over one call
/// would pass whatever shape the store had underneath and would prove nothing. One call per user and
/// one call per library item is what the surfaces and the fulfilment sweep do, and at that workload
/// the difference between a lookup and a walk is the difference between milliseconds and minutes.
/// </para>
/// <para>
/// <b>What these bounds catch and what they do not.</b> They catch a query path that grows with how
/// much the store holds where it should not: an index dropped, a lookup replaced by a walk, a read
/// that goes back to the file on every call. They do not catch a path that is merely slower than it
/// could be, and they are not a benchmark. The headroom is deliberately wide, because a shared
/// machine under load is the ordinary case for a suite and a bound that fails there teaches people
/// to delete it. The numbers below say what was measured and what the bound is, so the distance
/// between the two is visible rather than implied.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class FileRequestStoreQueryCostTests : IDisposable
{
    /// <summary>
    /// The size the bounds are stated at. Ten thousand is well past what a household request queue
    /// reaches and is the number the plan asks these paths to be answerable at.
    /// </summary>
    private const int Records = 10_000;

    /// <summary>
    /// How many people the requests are spread over, so each of them is waiting for ten and a
    /// lookup has something to return.
    /// </summary>
    private const int Users = 1_000;

    /// <summary>
    /// How many rows one page of the queue holds.
    /// </summary>
    private const int PageSize = 50;

    /// <summary>
    /// How many pages the queue leg turns. An operator working through a queue turns a handful, and
    /// a hundred is enough that the walk behind each one is measured rather than lost in the noise
    /// of a single call.
    /// </summary>
    private const int PagesTurned = 100;

    /// <summary>
    /// One state in five, so the filtered leg matches a fifth of the store and every page it asks
    /// for is a full one.
    /// </summary>
    private const int StatesUsed = 5;

    /// <summary>
    /// How many times the user leg asks. A thousand people opening their page ten times each, which
    /// is the workload rather than one call of it.
    /// </summary>
    private const int UserLookups = 10_000;

    /// <summary>
    /// The bound on answering one person's own requests, ten thousand times, over ten thousand
    /// records.
    /// <para>
    /// The window this sits in was measured in both directions. The store as it ships took 5 ms; the
    /// same leg with the lookup replaced by a walk of the set took 2,610 ms. The bound is far above
    /// the first and far below the second, so a machine many times slower than this one still passes
    /// and a store that lost the lookup still fails.
    /// </para>
    /// </summary>
    private const int UserLookupsBoundMilliseconds = 400;

    /// <summary>
    /// The bound on answering one external identifier per record, over ten thousand records. This
    /// is the shape of the fulfilment sweep in #42: one question per library item, and the leg where
    /// a walk becomes ten thousand walks.
    /// <para>
    /// Measured the same way, on the same run. As it ships, 10 ms; with the lookup replaced by a
    /// walk, 3,070 ms.
    /// </para>
    /// </summary>
    private const int IdentifierLookupsBoundMilliseconds = 600;

    /// <summary>
    /// The bound on turning a hundred filtered, ordered pages of the queue.
    /// <para>
    /// Measured at 153 ms. This leg is a walk by construction and the bound is not about avoiding
    /// one: a filter and an order chosen at the call cannot be served by a lookup built before it.
    /// The run that turned the other two lookups into walks measured 98 ms here, which is the same
    /// leg unmoved, so this bound does not separate a walk from a lookup and is not claimed to. What
    /// it catches is a page that stops being one walk, which is what a read going back to the file,
    /// or a count taken by a second pass over the set, would make it.
    /// </para>
    /// </summary>
    private const int QueuePagesBoundMilliseconds = 8_000;

    private readonly List<FileRequestStore> _stores = [];
    private readonly List<string> _directories = [];

    /// <summary>
    /// One person's own requests, answered ten thousand times against a store of ten thousand,
    /// inside the stated bound.
    /// <para>
    /// The answers are counted as well as timed. A store that returned nothing would be the fastest
    /// one there is, and a bound on its own cannot tell a lookup from a broken lookup.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EveryUsersOwnRequestsAreAnsweredWithinTheBound()
    {
        var store = await LoadedStore().ConfigureAwait(true);

        var found = 0;
        var clock = Stopwatch.StartNew();

        for (var lookup = 0; lookup < UserLookups; lookup++)
        {
            var theirs = await store.FindForUserAsync(UserId(lookup % Users), CancellationToken.None).ConfigureAwait(true);
            found += theirs.Count;
        }

        clock.Stop();

        Assert.Equal(UserLookups * (Records / Users), found);
        AssertWithin(UserLookupsBoundMilliseconds, clock, "ten thousand user lookups");
    }

    /// <summary>
    /// One external identifier per record, which is the shape of a fulfilment sweep walking the
    /// library, inside the stated bound.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EveryIdentifierIsAnsweredWithinTheBound()
    {
        var store = await LoadedStore().ConfigureAwait(true);

        var found = 0;
        var clock = Stopwatch.StartNew();

        for (var record = 0; record < Records; record++)
        {
            var carrying = await store.FindByProviderIdentifierAsync(
                KindOf(record),
                "Tmdb",
                IdentifierOf(record),
                CancellationToken.None).ConfigureAwait(true);

            found += carrying.Count;
        }

        clock.Stop();

        Assert.Equal(Records, found);
        AssertWithin(IdentifierLookupsBoundMilliseconds, clock, "one identifier lookup for each of ten thousand records");
    }

    /// <summary>
    /// A hundred filtered, ordered pages of the queue over ten thousand records, inside the stated
    /// bound. The count that comes back with each page is asserted too, because a page that stopped
    /// counting the matches would be the cheapest way to make this leg faster and the queue wrong.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TurningTheQueuePagesStaysWithinTheBound()
    {
        var store = await LoadedStore().ConfigureAwait(true);
        var matches = Records / StatesUsed;
        var pagesInTheQueue = matches / PageSize;

        var rows = 0;
        var clock = Stopwatch.StartNew();

        for (var turn = 0; turn < PagesTurned; turn++)
        {
            var page = await store.PageAsync(
                new RequestQuery
                {
                    States = [RequestState.Open],
                    Order = RequestQueryOrder.DisplayTitle,
                    Skip = (turn % pagesInTheQueue) * PageSize,
                    Take = PageSize
                },
                CancellationToken.None).ConfigureAwait(true);

            Assert.Equal(matches, page.MatchCount);
            rows += page.Requests.Count;
        }

        clock.Stop();

        Assert.Equal(PagesTurned * PageSize, rows);
        AssertWithin(QueuePagesBoundMilliseconds, clock, "a hundred filtered and ordered pages");
    }

    /// <summary>
    /// Removes every store this test made and the directory each one wrote in.
    /// </summary>
    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        foreach (var directory in _directories)
        {
            TestRunDirectory.Remove(directory);
        }
    }

    /// <summary>
    /// Fails with the measurement in the message rather than with a bare assertion, so a run that
    /// went over says by how much and the next reader has the number without re-running anything.
    /// </summary>
    /// <param name="boundMilliseconds">The bound this leg is held to.</param>
    /// <param name="clock">The stopped clock.</param>
    /// <param name="workload">What was measured, for the message.</param>
    private static void AssertWithin(int boundMilliseconds, Stopwatch clock, string workload)
        => Assert.True(
            clock.ElapsedMilliseconds <= boundMilliseconds,
            string.Format(
                CultureInfo.InvariantCulture,
                "{0} over {1} records took {2} ms, past the bound of {3} ms.",
                workload,
                Records,
                clock.ElapsedMilliseconds,
                boundMilliseconds));

    /// <summary>
    /// The user a record belongs to. Spread evenly, so every one of them is waiting for the same
    /// number and no lookup is answered by a bucket that happens to be empty.
    /// </summary>
    /// <param name="user">Which user.</param>
    /// <returns>Their identifier.</returns>
    private static Guid UserId(int user)
        => new Guid(user, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);

    /// <summary>
    /// What sort of thing a record names. Alternating, so the kind is part of every identifier
    /// question rather than a constant the store could ignore.
    /// </summary>
    /// <param name="record">Which record.</param>
    /// <returns>The kind.</returns>
    private static RequestedItemKind KindOf(int record)
        => record % 2 == 0 ? RequestedItemKind.Movie : RequestedItemKind.Series;

    /// <summary>
    /// The external identifier a record carries. One per record, so a lookup that returned a bucket
    /// instead of a match would be caught by the count.
    /// </summary>
    /// <param name="record">Which record.</param>
    /// <returns>The identifier under the provider.</returns>
    private static string IdentifierOf(int record)
        => record.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Ten thousand requests, spread over a thousand people, five states and two kinds, each
    /// carrying one external identifier of its own.
    /// </summary>
    /// <returns>The requests.</returns>
    private static IReadOnlyList<MediaRequest> Requests()
    {
        var asked = new DateTimeOffset(2026, 8, 8, 8, 0, 0, TimeSpan.Zero);

        return [.. Enumerable.Range(0, Records).Select(record => new MediaRequest
        {
            Id = new Guid(record, 0, 0, [0, 0, 0, 0, 0, 0, 0, 2]),
            RequestedByUserId = UserId(record % Users),
            RequestedAt = asked.AddSeconds(record),
            StateChangedAt = asked.AddSeconds(record),
            Kind = KindOf(record),

            // Titles that do not arrive in order, so the ordered leg has a sort to do rather than a
            // list that is already in the order it asked for.
            DisplayTitle = string.Create(CultureInfo.InvariantCulture, $"Request {(record * 7919) % Records:D5}"),
            State = (RequestState)(record % StatesUsed),
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Tmdb"] = IdentifierOf(record)
            }
        })];
    }

    /// <summary>
    /// A store holding ten thousand records, and the load already done.
    /// <para>
    /// The file is written directly rather than through ten thousand calls of the store's own add,
    /// because each of those rewrites the whole set and the arrangement would cost more than
    /// everything being measured. That is also the honest shape of the case: a server restarting
    /// reads a file somebody else's process wrote. What it costs is a coupling to the on-disk shape,
    /// and the count asserted here is what turns a change to that shape into a failure with a
    /// reason rather than a leg that quietly measures an empty store.
    /// </para>
    /// </summary>
    /// <returns>The store, with its file already read.</returns>
    private async Task<FileRequestStore> LoadedStore()
    {
        var directory = TestRunDirectory.CreateSubdirectory();
        _directories.Add(directory);

        var persisted = new
        {
            Version = FileRequestStore.OnDiskVersion,
            Requests = Requests().Select(request => new { Revision = 1L, Request = request }).ToArray()
        };

        await File.WriteAllTextAsync(
            Path.Combine(directory, FileRequestStore.FileName),
            JsonSerializer.Serialize(persisted)).ConfigureAwait(true);

        var store = new FileRequestStore(directory, new RecordingLogger(), TestClock.AtAFixedMoment());
        _stores.Add(store);

        // The first read is what parses the file, so it is made here and not inside a measured
        // region. It is also the assertion that the store read what was written.
        var all = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(Records, all.Count);

        return store;
    }
}

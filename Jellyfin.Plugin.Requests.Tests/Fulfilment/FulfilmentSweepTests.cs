using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Fulfilment;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Fulfilment;

/// <summary>
/// Fulfilment detected from the library rather than ticked by hand, which is #42.
/// <para>
/// The moves are asserted per media kind, because a film is one library item and a series is a set
/// of them and the two are the same code only if nothing about them differs. The removal case is
/// here as well: it is a decision this repository took rather than an oversight, and the assertion
/// is that a title leaving does not move a request back.
/// </para>
/// </summary>
public class FulfilmentSweepTests
{
    private static readonly DateTimeOffset Asked = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Requester = new Guid("7a4e2f18-6b0c-4d39-95e7-1f8a3c6d2b40");

    /// <summary>The two seasons the series requests below ask for.</summary>
    private static readonly int[] TwoSeasons = [1, 2];

    /// <summary>
    /// A film that has arrived moves an open request to fulfilled, with the plugin as the actor and
    /// no person recorded, because nobody decided anything.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AFilmThatArrivedFulfilsTheOpenRequestForIt()
    {
        var store = new InMemoryRequestStore();
        var library = new FakeLibrary();
        var clock = new TestClock(Asked);
        var sweep = Sweep(store, library, clock);

        await store.AddAsync(Request(RequestedItemKind.Movie, "Tmdb", "603"), CancellationToken.None);
        library.Put(RequestedItemKind.Movie, "Tmdb", "603");
        clock.Advance(TimeSpan.FromHours(3));

        Assert.Equal(1, await sweep.SweepAsync(CancellationToken.None));

        var after = await Only(store);

        Assert.Equal(RequestState.Fulfilled, after.State);
        Assert.Equal(LibraryAvailability.Present, after.Availability);
        Assert.Equal(clock.UtcNow, after.AvailabilityCheckedAt);
        Assert.Null(after.StateChangedByUserId);
        Assert.Equal(RequestState.Fulfilled, Assert.Single(after.History).To);
    }

    /// <summary>
    /// A series whose seasons have all arrived moves an approved request to fulfilled. The same code
    /// as the film above and a different shape of answer from the library, which is why it is
    /// asserted rather than assumed to follow.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ASeriesWhoseSeasonsArrivedFulfilsTheApprovedRequestForIt()
    {
        var store = new InMemoryRequestStore();
        var library = new FakeLibrary();
        var clock = new TestClock(Asked);
        var sweep = Sweep(store, library, clock);

        var approved = RequestLifecycle.Move(
            Request(RequestedItemKind.Series, "Tvdb", "76648") with { Seasons = TwoSeasons },
            RequestState.Approved,
            Asked,
            RequestCaller.Administrator(Requester));

        await store.AddAsync(approved, CancellationToken.None);
        library.Put(RequestedItemKind.Series, "Tvdb", "76648", 1, 2);

        Assert.Equal(1, await sweep.SweepAsync(CancellationToken.None));
        Assert.Equal(RequestState.Fulfilled, (await Only(store)).State);
    }

    /// <summary>
    /// A series with one of the two seasons it asked for is partial, and partial is not a move. The
    /// request stays where it was and the queue says something true about it, which is the whole
    /// point of having a third value rather than rounding to arrived or missing.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ASeriesWithSomeOfItsSeasonsIsPartialAndDoesNotMove()
    {
        var store = new InMemoryRequestStore();
        var library = new FakeLibrary();
        var sweep = Sweep(store, library, new TestClock(Asked));

        await store.AddAsync(
            Request(RequestedItemKind.Series, "Tvdb", "76648") with { Seasons = TwoSeasons },
            CancellationToken.None);

        library.Put(RequestedItemKind.Series, "Tvdb", "76648", 1);

        Assert.Equal(0, await sweep.SweepAsync(CancellationToken.None));

        var after = await Only(store);

        Assert.Equal(RequestState.Open, after.State);
        Assert.Equal(LibraryAvailability.Partial, after.Availability);
    }

    /// <summary>
    /// The item being removed again does not take the request back out of fulfilled. A library that
    /// stops holding something is an observation and not a decision being undone, so what changes is
    /// the availability and the request stays where it is with a row that says both.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task RemovingTheItemLeavesTheRequestFulfilledAndSaysTheServerNoLongerHoldsIt()
    {
        var store = new InMemoryRequestStore();
        var library = new FakeLibrary();
        var clock = new TestClock(Asked);
        var sweep = Sweep(store, library, clock);

        await store.AddAsync(Request(RequestedItemKind.Movie, "Tmdb", "603"), CancellationToken.None);
        library.Put(RequestedItemKind.Movie, "Tmdb", "603");
        await sweep.SweepAsync(CancellationToken.None);

        library.Remove(RequestedItemKind.Movie, "Tmdb", "603");
        clock.Advance(TimeSpan.FromDays(2));

        Assert.Equal(0, await sweep.SweepAsync(CancellationToken.None));

        var after = await Only(store);

        Assert.Equal(RequestState.Fulfilled, after.State);
        Assert.Equal(LibraryAvailability.Absent, after.Availability);
        Assert.Equal(clock.UtcNow, after.AvailabilityCheckedAt);
        Assert.Single(after.History);
    }

    /// <summary>
    /// A declined request whose title has since arrived records that the server holds it and stays
    /// declined. The table refuses that move, and this is the sweep asking the table rather than
    /// carrying its own list of states.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ADeclinedRequestRecordsTheArrivalAndStaysDeclined()
    {
        var store = new InMemoryRequestStore();
        var library = new FakeLibrary();
        var sweep = Sweep(store, library, new TestClock(Asked));

        var declined = RequestLifecycle.Decline(
            Request(RequestedItemKind.Movie, "Tmdb", "603"),
            DeclineReason.NotWanted,
            note: null,
            Asked,
            RequestCaller.Administrator(Requester));

        await store.AddAsync(declined, CancellationToken.None);
        library.Put(RequestedItemKind.Movie, "Tmdb", "603");

        Assert.Equal(0, await sweep.SweepAsync(CancellationToken.None));

        var after = await Only(store);

        Assert.Equal(RequestState.Declined, after.State);
        Assert.Equal(LibraryAvailability.Present, after.Availability);
    }

    /// <summary>
    /// A request nobody identified is not looked up and is not written. There is nothing to look it
    /// up by, so calling it absent would be a claim nothing checked.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ARequestWithNoProviderIdentifierIsLeftUnknownRatherThanCalledAbsent()
    {
        var store = new InMemoryRequestStore();
        var library = new FakeLibrary();
        var sweep = Sweep(store, library, new TestClock(Asked));

        await store.AddAsync(
            Request(RequestedItemKind.Movie, "Tmdb", "603") with
            {
                ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal)
            },
            CancellationToken.None);

        Assert.Equal(0, await sweep.SweepAsync(CancellationToken.None));
        Assert.Equal(0, library.Lookups);

        var after = await Only(store);

        Assert.Equal(LibraryAvailability.Unknown, after.Availability);
        Assert.Null(after.AvailabilityCheckedAt);
    }

    /// <summary>
    /// A second sweep that sees the same thing writes nothing. The store is one document, so a run
    /// that rewrote every request for no new fact would rewrite the whole of it on a schedule.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ASecondSweepSeeingTheSameThingWritesNothing()
    {
        var store = new InMemoryRequestStore();
        var library = new FakeLibrary();
        var clock = new TestClock(Asked);
        var sweep = Sweep(store, library, clock);

        await store.AddAsync(Request(RequestedItemKind.Movie, "Tmdb", "603"), CancellationToken.None);
        await sweep.SweepAsync(CancellationToken.None);

        var revision = (await store.GetAllAsync(CancellationToken.None)).Single().Revision;

        clock.Advance(TimeSpan.FromDays(1));
        await sweep.SweepAsync(CancellationToken.None);

        var again = (await store.GetAllAsync(CancellationToken.None)).Single();

        Assert.Equal(revision, again.Revision);
        Assert.Equal(2, library.Lookups);
    }

    /// <summary>
    /// The event path finds the request by the identifier the library item carries, and moves it the
    /// same way the sweep does. A request matching two of the item's identifiers is looked at once.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AChangedItemFulfilsTheRequestsNamingItAndLooksAtEachOnlyOnce()
    {
        var store = new InMemoryRequestStore();
        var library = new FakeLibrary();
        var sweep = Sweep(store, library, new TestClock(Asked));

        await store.AddAsync(
            Request(RequestedItemKind.Movie, "Tmdb", "603") with
            {
                ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Tmdb"] = "603",
                    ["Imdb"] = "tt0133093"
                }
            },
            CancellationToken.None);

        library.Put(RequestedItemKind.Movie, "Tmdb", "603");

        var changed = new LibraryChangeEventArgs(
            RequestedItemKind.Movie,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Tmdb"] = "603",
                ["Imdb"] = "tt0133093"
            });

        Assert.Equal(1, await sweep.ItemChangedAsync(changed, CancellationToken.None));
        Assert.Equal(1, library.Lookups);
        Assert.Equal(RequestState.Fulfilled, (await Only(store)).State);
    }

    /// <summary>
    /// A library event about something nobody asked for touches nothing. Most of what a scan raises
    /// is this case.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AChangedItemNobodyAskedForTouchesNothing()
    {
        var store = new InMemoryRequestStore();
        var library = new FakeLibrary();
        var sweep = Sweep(store, library, new TestClock(Asked));

        await store.AddAsync(Request(RequestedItemKind.Movie, "Tmdb", "603"), CancellationToken.None);

        var changed = new LibraryChangeEventArgs(
            RequestedItemKind.Movie,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "999" });

        Assert.Equal(0, await sweep.ItemChangedAsync(changed, CancellationToken.None));
        Assert.Equal(0, library.Lookups);
        Assert.Equal(RequestState.Open, (await Only(store)).State);
    }

    /// <summary>
    /// A request that somebody else moved between the read and the write keeps their move. The
    /// observation is dropped rather than retried, because a sweep that retried would write over the
    /// decision an operator has just made.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AnObservationLosesToADecisionMadeWhileItWasBeingMade()
    {
        var store = new InMemoryRequestStore();
        var library = new FakeLibrary();
        var moving = new StoreThatMovesARequestUnderTheWrite(
            store,
            held => RequestLifecycle.Decline(
                held,
                DeclineReason.NoRoomForIt,
                note: null,
                Asked,
                RequestCaller.Administrator(Requester)));

        var sweep = new FulfilmentSweep(moving, library, new TestClock(Asked), new RecordingJournal(), new RecordingSink(), new RecordingLogger());

        await store.AddAsync(Request(RequestedItemKind.Movie, "Tmdb", "603"), CancellationToken.None);
        library.Put(RequestedItemKind.Movie, "Tmdb", "603");

        Assert.Equal(0, await sweep.SweepAsync(CancellationToken.None));

        var after = await Only(store);

        Assert.Equal(RequestState.Declined, after.State);
        Assert.Equal(LibraryAvailability.Unknown, after.Availability);
    }

    private static FulfilmentSweep Sweep(IRequestStore store, ILibrary library, TestClock clock)
        => new FulfilmentSweep(store, library, clock, new RecordingJournal(), new RecordingSink(), new RecordingLogger());

    private static async Task<MediaRequest> Only(InMemoryRequestStore store)
        => (await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true)).Single().Request;

    private static MediaRequest Request(RequestedItemKind kind, string provider, string value)
        => new MediaRequest
        {
            Id = new Guid("2b8f4c61-0d75-4a39-9e26-3f5a8c1d7b04"),
            RequestedByUserId = Requester,
            RequestedAt = Asked,
            StateChangedAt = Asked,
            Kind = kind,
            DisplayTitle = "The one that was asked for",
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { [provider] = value }
        };
}

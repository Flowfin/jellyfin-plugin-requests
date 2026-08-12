using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Storage;

/// <summary>
/// Everything <see cref="IRequestStore"/> promises, as tests rather than as prose. Any store this
/// plugin ever ships derives from this class and is judged by the same set, so a second
/// implementation cannot quietly keep a weaker promise than the first.
/// <para>
/// The concurrency tests assert the documented outcome and not merely that nothing threw. A store
/// that wrote last-writer-wins would never crash under any of them; what it would do is fail the
/// count of accepted writes, which is the assertion these are built around.
/// </para>
/// </summary>
public abstract class RequestStoreContract
{
    /// <summary>
    /// How many callers the concurrent tests run at once. Large enough that they genuinely overlap
    /// on an ordinary machine and small enough that the suite stays quick.
    /// </summary>
    private const int Writers = 32;

    /// <summary>
    /// A store with nothing in it. Each test gets its own.
    /// </summary>
    /// <returns>An empty store of the implementation under test.</returns>
    protected abstract IRequestStore NewStore();

    /// <summary>
    /// The ordinary case, and the one every other test starts from. A request that has been added
    /// reads back as itself, at revision 1, which is the number a caller hands back on its first
    /// write.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnAddedRequestReadsBackAtRevisionOne()
    {
        var store = NewStore();
        var request = ARequest();

        var added = await store.AddAsync(request, CancellationToken.None).ConfigureAwait(true);
        var read = await store.GetAsync(request.Id, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, added.Revision);
        Assert.Equal(request, added.Request);
        Assert.NotNull(read);
        Assert.Equal(added, read.Value);
    }

    /// <summary>
    /// Asking about a request the store does not hold is an answer rather than a failure. The
    /// failure this stands for is a store that throws here, which would make every caller holding
    /// an identifier from an earlier read wrap its reads in a catch.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ReadingSomethingTheStoreDoesNotHoldIsNotAnError()
    {
        var store = NewStore();

        var read = await store.GetAsync(Guid.NewGuid(), CancellationToken.None).ConfigureAwait(true);

        Assert.Null(read);
    }

    /// <summary>
    /// Two requests with one identifier is a state the store never reaches. An identifier source
    /// that repeats itself, or an add replayed by a retry, is refused rather than overwriting what
    /// is already there.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AddingAnIdentifierTheStoreAlreadyHoldsIsRefused()
    {
        var store = NewStore();
        var request = ARequest();
        await store.AddAsync(request, CancellationToken.None).ConfigureAwait(true);

        var refused = await Assert.ThrowsAsync<DuplicateRequestException>(
            () => store.AddAsync(request with { DisplayTitle = "Something else" }, CancellationToken.None))
            .ConfigureAwait(true);

        var read = await store.GetAsync(request.Id, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(request.Id, refused.RequestId);
        Assert.Equal(request.DisplayTitle, read!.Value.Request.DisplayTitle);
    }

    /// <summary>
    /// A write against the revision the caller read is accepted, and the revision moves by exactly
    /// one. The exactness is the point: callers hand the number back and a store that moved it by
    /// anything else would make every second write of a chain fail for no reason a caller could see.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AWriteAgainstTheRevisionThatWasReadIsAccepted()
    {
        var store = NewStore();
        var added = await store.AddAsync(ARequest(), CancellationToken.None).ConfigureAwait(true);

        var approved = added.Request with { State = RequestState.Approved };
        var written = await store.ReplaceAsync(approved, added.Revision, CancellationToken.None).ConfigureAwait(true);
        var again = await store.ReplaceAsync(
            written.Request with { Availability = LibraryAvailability.Present },
            written.Revision,
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, written.Revision);
        Assert.Equal(3, again.Revision);
        Assert.Equal(RequestState.Approved, again.Request.State);
    }

    /// <summary>
    /// The case the whole contract exists for, made deterministic. An operator read the request and
    /// went away to decide; something else moved it meanwhile; the operator's decision arrives
    /// against a revision that is no longer there. It is refused, and the refusal carries what the
    /// store holds now, so the surface can show the operator what actually happened rather than a
    /// generic failure.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AWriteAgainstAnOvertakenRevisionIsRefusedAndSaysWhatTheStoreHolds()
    {
        var store = NewStore();
        var added = await store.AddAsync(ARequest(), CancellationToken.None).ConfigureAwait(true);
        var overtaking = await store.ReplaceAsync(
            added.Request with { State = RequestState.Fulfilled },
            added.Revision,
            CancellationToken.None).ConfigureAwait(true);

        var refused = await Assert.ThrowsAsync<RequestConcurrencyException>(
            () => store.ReplaceAsync(
                added.Request with { State = RequestState.Declined },
                added.Revision,
                CancellationToken.None))
            .ConfigureAwait(true);

        Assert.Equal(added.Request.Id, refused.RequestId);
        Assert.Equal(added.Revision, refused.ExpectedRevision);
        Assert.NotNull(refused.Current);
        Assert.Equal(overtaking, refused.Current!.Value);

        var read = await store.GetAsync(added.Request.Id, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(RequestState.Fulfilled, read!.Value.Request.State);
        Assert.Equal(overtaking.Revision, read.Value.Revision);
    }

    /// <summary>
    /// A write against a request that has been removed says so rather than reporting a revision
    /// nobody can act on. A caller told "gone" re-reads and finds nothing, which is a different
    /// repair from "somebody moved it".
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AWriteAgainstARemovedRequestSaysItIsGone()
    {
        var store = NewStore();
        var added = await store.AddAsync(ARequest(), CancellationToken.None).ConfigureAwait(true);
        await store.RemoveAsync(added.Request.Id, added.Revision, CancellationToken.None).ConfigureAwait(true);

        var refused = await Assert.ThrowsAsync<RequestConcurrencyException>(
            () => store.ReplaceAsync(added.Request, added.Revision, CancellationToken.None))
            .ConfigureAwait(true);

        Assert.Null(refused.Current);
    }

    /// <summary>
    /// A removal carries a revision for the same reason a write does. Deleting something on the
    /// strength of a read that has since been overtaken is how a request an operator just approved
    /// disappears.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task RemovingAgainstAnOvertakenRevisionIsRefused()
    {
        var store = NewStore();
        var added = await store.AddAsync(ARequest(), CancellationToken.None).ConfigureAwait(true);
        await store.ReplaceAsync(
            added.Request with { State = RequestState.Approved },
            added.Revision,
            CancellationToken.None).ConfigureAwait(true);

        await Assert.ThrowsAsync<RequestConcurrencyException>(
            () => store.RemoveAsync(added.Request.Id, added.Revision, CancellationToken.None))
            .ConfigureAwait(true);

        var read = await store.GetAsync(added.Request.Id, CancellationToken.None).ConfigureAwait(true);
        Assert.NotNull(read);
    }

    /// <summary>
    /// Removing something already gone is not an error. A retention sweep and a person deleting the
    /// same finished request is a race whose two outcomes are the same, and making the loser throw
    /// would put a failure in a log for a thing that went right.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task RemovingSomethingTheStoreDoesNotHoldIsNotAnError()
    {
        var store = NewStore();

        var removed = await store.RemoveAsync(Guid.NewGuid(), 1, CancellationToken.None).ConfigureAwait(true);

        Assert.False(removed);
    }

    /// <summary>
    /// The whole-store read returns every request, each at the revision it is held at.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheWholeStoreReadReturnsEveryRequestAtItsOwnRevision()
    {
        var store = NewStore();
        var first = await store.AddAsync(ARequest(), CancellationToken.None).ConfigureAwait(true);
        var second = await store.AddAsync(ARequest(), CancellationToken.None).ConfigureAwait(true);
        var moved = await store.ReplaceAsync(
            second.Request with { State = RequestState.Approved },
            second.Revision,
            CancellationToken.None).ConfigureAwait(true);

        var all = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, all.Count);
        Assert.Contains(first, all);
        Assert.Contains(moved, all);
    }

    /// <summary>
    /// One person's requests are the ones they asked for and the ones they joined, and a store that
    /// answered only the first would show somebody nothing for the request they are most likely to
    /// be looking for. Somebody else's request is not theirs, which is the other half and the one a
    /// store returning everything would still pass without.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task OneUsersRequestsAreTheOnesTheyAskedForAndTheOnesTheyJoined()
    {
        var store = NewStore();
        var asker = Guid.NewGuid();
        var joiner = Guid.NewGuid();
        var stranger = Guid.NewGuid();

        var theirs = await store.AddAsync(
            ARequest() with { RequestedByUserId = asker },
            CancellationToken.None).ConfigureAwait(true);
        var joined = await store.AddAsync(
            ARequest() with { RequestedByUserId = stranger, JoinedByUserIds = [joiner, asker] },
            CancellationToken.None).ConfigureAwait(true);
        await store.AddAsync(
            ARequest() with { RequestedByUserId = stranger },
            CancellationToken.None).ConfigureAwait(true);

        var forAsker = await store.FindForUserAsync(asker, CancellationToken.None).ConfigureAwait(true);
        var forJoiner = await store.FindForUserAsync(joiner, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, forAsker.Count);
        Assert.Contains(theirs, forAsker);
        Assert.Contains(joined, forAsker);
        Assert.Equal(new[] { joined }, forJoiner);
    }

    /// <summary>
    /// Somebody who has asked for nothing gets an empty answer rather than a failure. Every user on
    /// the server reaches this call the first time they open the page, so the ordinary case must not
    /// be an exception a surface has to catch.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SomebodyWhoHasAskedForNothingGetsAnEmptyAnswer()
    {
        var store = NewStore();
        await store.AddAsync(ARequest(), CancellationToken.None).ConfigureAwait(true);

        var theirs = await store.FindForUserAsync(Guid.NewGuid(), CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(theirs);
    }

    /// <summary>
    /// The identifier lookup answers the comparison <see cref="RequestIdentity"/> makes: the
    /// provider name without case, because two callers spell one provider two ways and neither is
    /// wrong.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnIdentifierIsFoundHoweverTheProviderNameIsSpelled()
    {
        var store = NewStore();
        var carrying = await store.AddAsync(
            ARequest() with { ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" } },
            CancellationToken.None).ConfigureAwait(true);
        await store.AddAsync(
            ARequest() with { ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "604" } },
            CancellationToken.None).ConfigureAwait(true);

        var found = await store.FindByProviderIdentifierAsync(
            RequestedItemKind.Movie,
            "tmdb",
            "603",
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(new[] { carrying }, found);
    }

    /// <summary>
    /// The other half of the same comparison, and the half a store written with one case-insensitive
    /// dictionary would fail. The value is somebody else's identifier, so two that differ are two
    /// things, and the kind is part of the identity because a film and a series can carry the same
    /// number under one provider.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnIdentifierOfAnotherKindOrAnotherValueIsAnotherThing()
    {
        var store = NewStore();
        await store.AddAsync(
            ARequest() with
            {
                Kind = RequestedItemKind.Series,
                ProviderIds = new Dictionary<string, string> { ["Tvdb"] = "tt42" }
            },
            CancellationToken.None).ConfigureAwait(true);

        var wrongKind = await store.FindByProviderIdentifierAsync(
            RequestedItemKind.Movie,
            "Tvdb",
            "tt42",
            CancellationToken.None).ConfigureAwait(true);
        var wrongCase = await store.FindByProviderIdentifierAsync(
            RequestedItemKind.Series,
            "Tvdb",
            "TT42",
            CancellationToken.None).ConfigureAwait(true);
        var rightBoth = await store.FindByProviderIdentifierAsync(
            RequestedItemKind.Series,
            "Tvdb",
            "tt42",
            CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(wrongKind);
        Assert.Empty(wrongCase);
        Assert.Single(rightBoth);
    }

    /// <summary>
    /// A query naming no state and no kind is a queue nobody has narrowed, and it matches
    /// everything. The opposite reading, that an empty list matches nothing, is the mistake a filter
    /// written as a membership test without an emptiness test makes, and it turns an unfiltered
    /// queue into an empty page.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AQueryNamingNoFilterMatchesEverything()
    {
        var store = NewStore();
        await store.AddAsync(ARequest(), CancellationToken.None).ConfigureAwait(true);
        await store.AddAsync(
            ARequest() with { Kind = RequestedItemKind.Series, State = RequestState.Approved },
            CancellationToken.None).ConfigureAwait(true);

        var page = await store.PageAsync(new RequestQuery { Take = 10 }, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, page.MatchCount);
        Assert.Equal(2, page.Requests.Count);
    }

    /// <summary>
    /// Both filters have to hold, and the count is over the filter rather than over the store. A
    /// store applying either filter alone would return the wrong rows; one counting the whole store
    /// would return the right rows under a pager that says there are more.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EveryNamedFilterHasToHoldAndTheCountIsOverThem()
    {
        var store = NewStore();
        var wanted = await store.AddAsync(
            ARequest() with { Kind = RequestedItemKind.Series, State = RequestState.Approved },
            CancellationToken.None).ConfigureAwait(true);
        await store.AddAsync(
            ARequest() with { Kind = RequestedItemKind.Movie, State = RequestState.Approved },
            CancellationToken.None).ConfigureAwait(true);
        await store.AddAsync(
            ARequest() with { Kind = RequestedItemKind.Series, State = RequestState.Open },
            CancellationToken.None).ConfigureAwait(true);

        var page = await store.PageAsync(
            new RequestQuery
            {
                States = [RequestState.Approved],
                Kinds = [RequestedItemKind.Series],
                Take = 10
            },
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, page.MatchCount);
        Assert.Equal(new[] { wanted }, page.Requests);
    }

    /// <summary>
    /// The count is how many matched and not how many the page carries. It is what a pager is drawn
    /// from, and a store answering the page length would tell an operator on the first page of six
    /// that there is one page.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ThePageSaysHowManyMatchedRatherThanHowManyItCarries()
    {
        var store = NewStore();

        for (var made = 0; made < 6; made++)
        {
            await store.AddAsync(ARequest(), CancellationToken.None).ConfigureAwait(true);
        }

        var page = await store.PageAsync(new RequestQuery { Take = 2 }, CancellationToken.None).ConfigureAwait(true);
        var counted = await store.PageAsync(new RequestQuery { Take = 0 }, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(6, page.MatchCount);
        Assert.Equal(2, page.Requests.Count);
        Assert.Equal(6, counted.MatchCount);
        Assert.Empty(counted.Requests);
    }

    /// <summary>
    /// Walking the pages sees every match exactly once, over requests that were all made in the same
    /// moment so nothing but the tiebreak separates them.
    /// <para>
    /// What this stands for is the page arithmetic: a boundary off by one shows a request twice or
    /// skips it, and neither throws. It is not the proof that the order is total. Removing the
    /// tiebreak from the store leaves this leg green, because the sort behind it is stable and the
    /// set it reads is enumerated the same way each time; what goes red then is
    /// <see cref="DescendingIsTheAscendingOrderReadBackwards"/>, which is where that proof is.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task WalkingThePagesSeesEveryMatchExactlyOnce()
    {
        var store = NewStore();
        var added = new List<Guid>();

        for (var made = 0; made < 7; made++)
        {
            var stored = await store.AddAsync(ARequest(), CancellationToken.None).ConfigureAwait(true);
            added.Add(stored.Request.Id);
        }

        var walked = new List<Guid>();

        for (var skip = 0; skip < 7; skip += 2)
        {
            var page = await store.PageAsync(
                new RequestQuery { Skip = skip, Take = 2 },
                CancellationToken.None).ConfigureAwait(true);

            walked.AddRange(page.Requests.Select(stored => stored.Request.Id));
        }

        Assert.Equal(added.Order(), walked.Order());
        Assert.Equal(7, walked.Distinct().Count());
    }

    /// <summary>
    /// Descending is the ascending order read backwards, tiebreak included. A store that reversed
    /// the chosen key and left the tiebreak alone would answer a third order, and two requests made
    /// in the same tick would sit one way going down the queue and the other going up it.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task DescendingIsTheAscendingOrderReadBackwards()
    {
        var store = NewStore();
        var asked = new DateTimeOffset(2026, 8, 8, 8, 0, 0, TimeSpan.Zero);

        for (var made = 0; made < 5; made++)
        {
            // Two of the five share a moment, so the tiebreak is what decides their order and the
            // reversal has something to get wrong.
            await store.AddAsync(
                ARequest() with { RequestedAt = asked.AddMinutes(made / 2) },
                CancellationToken.None).ConfigureAwait(true);
        }

        var up = await store.PageAsync(new RequestQuery { Take = 5 }, CancellationToken.None).ConfigureAwait(true);
        var down = await store.PageAsync(
            new RequestQuery { Take = 5, Descending = true },
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(up.Requests.Reverse(), down.Requests);
    }

    /// <summary>
    /// Many callers write against one revision at the same moment. Exactly one is accepted, every
    /// other is refused, and the store ends one revision on rather than <see cref="Writers"/> of
    /// them.
    /// <para>
    /// This is the assertion a last-writer-wins store fails without ever throwing: it would accept
    /// all of them, end at a revision nobody wrote against, and hold whichever value happened to
    /// land last.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task OfManyWritersAgainstOneRevisionExactlyOneIsAccepted()
    {
        var store = NewStore();
        var added = await store.AddAsync(ARequest(), CancellationToken.None).ConfigureAwait(true);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, Writers).Select(async writer =>
        {
            await start.Task.ConfigureAwait(true);

            try
            {
                var written = await store.ReplaceAsync(
                    added.Request with { DisplayYear = writer },
                    added.Revision,
                    CancellationToken.None).ConfigureAwait(true);
                return (Accepted: true, written.Revision, Writer: writer);
            }
            catch (RequestConcurrencyException)
            {
                return (Accepted: false, Revision: 0L, Writer: writer);
            }
        }).ToArray();

        start.SetResult();
        var outcomes = await Task.WhenAll(attempts).ConfigureAwait(true);

        var accepted = outcomes.Where(outcome => outcome.Accepted).ToArray();
        Assert.Single(accepted);
        Assert.Equal(2, accepted[0].Revision);

        var read = await store.GetAsync(added.Request.Id, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(2, read!.Value.Revision);
        Assert.Equal(accepted[0].Writer, read.Value.Request.DisplayYear);
    }

    /// <summary>
    /// The same race with the callers doing what a real caller does, which is to read again and
    /// decide again. Every writer eventually lands, each accepted write gets its own revision, and
    /// no writer's change is overwritten by one that never saw it.
    /// <para>
    /// The set of returned revisions is the assertion that matters. Consecutive with no repeat is
    /// what "exactly one accepted write per revision" means, and a store that lost an update would
    /// still end at the right final number while the titles said otherwise, which is why both are
    /// checked.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EveryAcceptedWriteGetsItsOwnRevisionAndNoneIsLost()
    {
        var store = NewStore();
        var added = await store.AddAsync(ARequest() with { DisplayTitle = "seed" }, CancellationToken.None).ConfigureAwait(true);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, Writers).Select(async writer =>
        {
            await start.Task.ConfigureAwait(true);

            while (true)
            {
                var read = await store.GetAsync(added.Request.Id, CancellationToken.None).ConfigureAwait(true);
                var appended = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} {1}",
                    read!.Value.Request.DisplayTitle,
                    writer);

                try
                {
                    var written = await store.ReplaceAsync(
                        read.Value.Request with { DisplayTitle = appended },
                        read.Value.Revision,
                        CancellationToken.None).ConfigureAwait(true);
                    return written.Revision;
                }
                catch (RequestConcurrencyException)
                {
                    // Exactly what a caller is meant to do with one: read again, decide against
                    // what is there now, write again.
                }
            }
        }).ToArray();

        start.SetResult();
        var revisions = await Task.WhenAll(attempts).ConfigureAwait(true);

        Assert.Equal(Enumerable.Range(2, Writers).Select(revision => (long)revision), revisions.OrderBy(revision => revision));

        var read = await store.GetAsync(added.Request.Id, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(Writers + 1, read!.Value.Revision);

        var words = read.Value.Request.DisplayTitle.Split(' ');
        Assert.Equal("seed", words[0]);
        Assert.Equal(
            Enumerable.Range(0, Writers).Select(writer => writer.ToString(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal),
            words.Skip(1).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The same rule on the way in. Several callers adding one identifier at the same moment leave
    /// the store holding one request, and every caller but one is told why it was refused.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task OfManyAddsOfOneIdentifierExactlyOneIsAccepted()
    {
        var store = NewStore();
        var request = ARequest();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, Writers).Select(async writer =>
        {
            await start.Task.ConfigureAwait(true);

            try
            {
                await store.AddAsync(request with { DisplayYear = writer }, CancellationToken.None).ConfigureAwait(true);
                return true;
            }
            catch (DuplicateRequestException)
            {
                return false;
            }
        }).ToArray();

        start.SetResult();
        var outcomes = await Task.WhenAll(attempts).ConfigureAwait(true);

        Assert.Single(outcomes, accepted => accepted);

        var all = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Single(all);
    }

    /// <summary>
    /// A reader running beside writers sees whole requests. Every read returns a value some write
    /// completed and never a mixture of two, which is the promise that lets a queue be rendered
    /// from a read taken while the plugin is working.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AReaderRunningBesideWritersOnlySeesValuesSomeWriteWrote()
    {
        var store = NewStore();
        var added = await store.AddAsync(ARequest() with { DisplayTitle = "0", DisplayYear = 0 }, CancellationToken.None).ConfigureAwait(true);
        var stop = new CancellationTokenSource();

        var writing = Task.Run(async () =>
        {
            var held = added;

            for (var write = 1; write <= 200; write++)
            {
                var next = held.Request with
                {
                    DisplayTitle = write.ToString(CultureInfo.InvariantCulture),
                    DisplayYear = write
                };

                held = await store.ReplaceAsync(next, held.Revision, CancellationToken.None).ConfigureAwait(true);
            }

            await stop.CancelAsync().ConfigureAwait(true);
        });

        var mixtures = new List<string>();

        while (!stop.IsCancellationRequested)
        {
            var read = await store.GetAsync(added.Request.Id, CancellationToken.None).ConfigureAwait(true);
            var seen = read!.Value.Request;

            if (seen.DisplayTitle != seen.DisplayYear?.ToString(CultureInfo.InvariantCulture))
            {
                mixtures.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "title {0} with year {1}",
                    seen.DisplayTitle,
                    seen.DisplayYear));
            }
        }

        await writing.ConfigureAwait(true);
        stop.Dispose();

        Assert.Empty(mixtures);
    }

    /// <summary>
    /// A cancelled call throws rather than returning something the caller reads as an answer. The
    /// contract says nothing about whether a cancelled write took effect, so nothing here asserts
    /// that it did or did not.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ACancelledCallThrows()
    {
        var store = NewStore();
        var added = await store.AddAsync(ARequest(), CancellationToken.None).ConfigureAwait(true);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync().ConfigureAwait(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.GetAsync(added.Request.Id, cancelled.Token)).ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.ReplaceAsync(added.Request, added.Revision, cancelled.Token)).ConfigureAwait(true);
    }

    /// <summary>
    /// A want the sibling handed over is found again by its own identifier. This is the lookup that
    /// makes a repeat recognisable as one, and without it every refresh that recreated an item on the
    /// browsing side is another acquisition here.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARequestIsFoundByAWantItAbsorbed()
    {
        var store = NewStore();
        var want = Guid.NewGuid();
        var request = ARequest() with { WantIds = [Guid.NewGuid(), want] };

        var added = await store.AddAsync(request, CancellationToken.None).ConfigureAwait(true);
        var found = await store.FindByWantAsync(want, CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(found);
        Assert.Equal(added, found.Value);
    }

    /// <summary>
    /// A want nothing has absorbed is an answer rather than a failure, in the same way an unknown
    /// request identifier is. The seam asks this of every handover, and most of them are the first
    /// time that want has been seen.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AWantNobodyHasHandedOverIsAnsweredWithNothing()
    {
        var store = NewStore();

        await store.AddAsync(ARequest() with { WantIds = [Guid.NewGuid()] }, CancellationToken.None).ConfigureAwait(true);

        Assert.Null(await store.FindByWantAsync(Guid.NewGuid(), CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>
    /// The lookup answers whatever state the request is in. A want whose request was declined has
    /// still been taken, and a store that answered only for the open ones would make a refusal the
    /// one thing that lets a repeat through.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AWantIsStillFoundAfterItsRequestWasAnswered()
    {
        var store = NewStore();
        var want = Guid.NewGuid();

        var added = await store
            .AddAsync(ARequest() with { WantIds = [want] }, CancellationToken.None)
            .ConfigureAwait(true);

        await store
            .ReplaceAsync(added.Request with { State = RequestState.Declined }, added.Revision, CancellationToken.None)
            .ConfigureAwait(true);

        var found = await store.FindByWantAsync(want, CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(found);
        Assert.Equal(RequestState.Declined, found.Value.Request.State);
    }

    /// <summary>
    /// A want identifier that names nothing is refused rather than answered. It is not an identifier
    /// any request can have absorbed, so an answer of nothing found would read as a fact about the
    /// store instead of as a caller's mistake.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AWantIdentifierThatNamesNothingIsRefused()
    {
        var store = NewStore();

        await Assert
            .ThrowsAsync<ArgumentException>(() => store.FindByWantAsync(Guid.Empty, CancellationToken.None))
            .ConfigureAwait(true);
    }

    /// <summary>
    /// A request with only the fields that have no default, and its own identifier each time so a
    /// test can add two without arranging anything.
    /// </summary>
    /// <returns>A newly asked-for request.</returns>
    private static MediaRequest ARequest()
    {
        var asked = new DateTimeOffset(2026, 8, 8, 8, 0, 0, TimeSpan.Zero);

        return new MediaRequest
        {
            Id = Guid.NewGuid(),
            RequestedByUserId = new Guid("b31d0f9a-5c2e-4a71-8f6b-0d4c3e2a1b58"),
            RequestedAt = asked,
            StateChangedAt = asked,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = "The Conversation"
        };
    }
}

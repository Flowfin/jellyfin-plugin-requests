using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Api;

/// <summary>
/// Deciding on several requests in one action.
/// <para>
/// The actions are called directly rather than through a server, for the reason the other API tests
/// give: the headless rule refuses a running Jellyfin, and what these judge is what an endpoint does
/// with a body, an identity and a store.
/// </para>
/// <para>
/// The failure these exist against is an operator told an action was done when part of it was
/// refused. Every leg below asks the store what it holds afterwards rather than believing the
/// answer the endpoint gave.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class ActOnManyRequestsTests
{
    private static readonly Guid Asker = new Guid("b6000000-0000-0000-0000-000000000001");
    private static readonly Guid Operator = new Guid("b6000000-0000-0000-0000-000000000002");
    private static readonly Guid Watcher = new Guid("b6000000-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset Started = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// Three requests approved in one action move, each entry carries the row at its new revision,
    /// and each request holds exactly one history entry naming the person who decided.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ApprovingSeveralMovesEachOneAndReportsItsNewRow()
    {
        var store = new InMemoryRequestStore();
        var open = await ThreeOpenRequestsAsync(store).ConfigureAwait(true);

        var answered = await ControllerFor(store, Operator)
            .ApproveManyAsync(new ApproveManyBody { Requests = Choosing(open) }, CancellationToken.None)
            .ConfigureAwait(true);

        var entries = Decided(answered);

        Assert.Equal(open.Select(one => one.Request.Id), entries.Select(entry => entry.Id));

        foreach (var (entry, before) in entries.Zip(open))
        {
            var row = Assert.IsType<QueuedRequest>(entry.Request);
            Assert.Null(entry.Failure);
            Assert.Equal(RequestState.Approved, row.State);
            Assert.Equal(before.Revision + 1, row.Revision);

            var held = await Held(store, before.Request.Id).ConfigureAwait(true);
            Assert.Equal(RequestState.Approved, held.Request.State);
            Assert.Equal(Operator, held.Request.StateChangedByUserId);

            var history = Assert.Single(held.Request.History);
            Assert.Equal(RequestState.Open, history.From);
            Assert.Equal(RequestState.Approved, history.To);
            Assert.Equal(Operator, history.ByUserId);
        }
    }

    /// <summary>
    /// A decline of several carries the one reason and the one note onto every request in the
    /// action, and every one of them keeps it in its history.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task DecliningSeveralCarriesOneReasonOntoEveryOneOfThem()
    {
        var store = new InMemoryRequestStore();
        var open = await ThreeOpenRequestsAsync(store).ConfigureAwait(true);

        var answered = await ControllerFor(store, Operator)
            .DeclineManyAsync(
                new DeclineManyBody
                {
                    Requests = Choosing(open),
                    Reason = DeclineReason.NoRoomForIt,
                    Note = "The disk this would go on is full until the new one arrives."
                },
                CancellationToken.None)
            .ConfigureAwait(true);

        foreach (var entry in Decided(answered))
        {
            var row = Assert.IsType<QueuedRequest>(entry.Request);
            Assert.Equal(RequestState.Declined, row.State);
            Assert.Equal(DeclineReason.NoRoomForIt, row.DeclineReason);

            var held = await Held(store, entry.Id).ConfigureAwait(true);
            var history = Assert.Single(held.Request.History);
            Assert.Equal(DeclineReason.NoRoomForIt, history.Reason);
            Assert.Equal(
                "The disk this would go on is full until the new one arrives.",
                history.Note,
                StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The whole point of the shape. One request moved underneath the operator and one was never
    /// there; the action says which two and why, and the other two are decided rather than rolled
    /// back.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task OneRefusalLeavesTheOthersDecidedAndSaysWhichAndWhy()
    {
        var store = new InMemoryRequestStore();
        var open = await ThreeOpenRequestsAsync(store).ConfigureAwait(true);

        // The second one is decided by somebody else between the operator reading the queue and
        // acting on it, which is the ordinary way a revision goes stale.
        await ControllerFor(store, Watcher)
            .ApproveAsync(
                open[1].Request.Id,
                new ApproveRequestBody { Revision = open[1].Revision },
                CancellationToken.None)
            .ConfigureAwait(true);

        var missing = new Guid("b6000000-0000-0000-0000-0000000000ff");

        var chosen = Choosing(open).Append(new RequestToDecide { Id = missing, Revision = 1 }).ToArray();

        var answered = await ControllerFor(store, Operator)
            .ApproveManyAsync(new ApproveManyBody { Requests = chosen }, CancellationToken.None)
            .ConfigureAwait(true);

        var entries = Decided(answered);

        Assert.Equal(4, entries.Count);
        Assert.Equal(chosen.Select(one => one.Id!.Value), entries.Select(entry => entry.Id));

        Assert.NotNull(entries[0].Request);
        Assert.NotNull(entries[2].Request);

        var stale = Assert.IsType<RequestFailure>(entries[1].Failure);
        Assert.Equal(RequestFailureCode.MovedSinceItWasRead, stale.Code);
        Assert.Null(entries[1].Request);

        // What the store holds now comes back with the refusal, so the operator decides again
        // against what is there rather than reading the queue a second time.
        var current = Assert.IsType<QueuedRequest>(stale.Current);
        Assert.Equal(RequestState.Approved, current.State);
        Assert.Equal(Watcher, current.StateChangedByUserId);

        var absent = Assert.IsType<RequestFailure>(entries[3].Failure);
        Assert.Equal(RequestFailureCode.NoSuchRequest, absent.Code);

        // The two that were not refused are done, and the refused one was not moved by this action.
        Assert.Equal(RequestState.Approved, (await Held(store, open[0].Request.Id).ConfigureAwait(true)).Request.State);
        Assert.Equal(RequestState.Approved, (await Held(store, open[2].Request.Id).ConfigureAwait(true)).Request.State);
        Assert.Single((await Held(store, open[1].Request.Id).ConfigureAwait(true)).Request.History);
    }

    /// <summary>
    /// The action and the single decision agree on every state a request can be in, for both moves.
    /// <para>
    /// This is the leg that says no transition rule is reachable one way and not the other. It is a
    /// comparison of two runs over identical stores rather than a reading of the source, because
    /// what the two paths share is a fact about the code today and the property has to survive
    /// somebody changing it.
    /// </para>
    /// </summary>
    /// <param name="from">The state the request is in when the decision arrives.</param>
    /// <param name="approving">Whether the move is an approval or a decline.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData(RequestState.Open, true)]
    [InlineData(RequestState.Approved, true)]
    [InlineData(RequestState.Declined, true)]
    [InlineData(RequestState.Fulfilled, true)]
    [InlineData(RequestState.Failed, true)]
    [InlineData(RequestState.Open, false)]
    [InlineData(RequestState.Approved, false)]
    [InlineData(RequestState.Declined, false)]
    [InlineData(RequestState.Fulfilled, false)]
    [InlineData(RequestState.Failed, false)]
    public async Task TheActionAndTheSingleDecisionAgreeOnEveryStateARequestCanBeIn(RequestState from, bool approving)
    {
        var alone = new InMemoryRequestStore();
        var together = new InMemoryRequestStore();

        var one = await InStateAsync(alone, from).ConfigureAwait(true);
        var other = await InStateAsync(together, from).ConfigureAwait(true);

        var single = approving
            ? await ControllerFor(alone, Operator)
                .ApproveAsync(one.Request.Id, new ApproveRequestBody { Revision = one.Revision }, CancellationToken.None)
                .ConfigureAwait(true)
            : await ControllerFor(alone, Operator)
                .DeclineAsync(one.Request.Id, ADecline(one.Revision), CancellationToken.None)
                .ConfigureAwait(true);

        var chosen = new[] { new RequestToDecide { Id = other.Request.Id, Revision = other.Revision } };

        var many = approving
            ? await ControllerFor(together, Operator)
                .ApproveManyAsync(new ApproveManyBody { Requests = chosen }, CancellationToken.None)
                .ConfigureAwait(true)
            : await ControllerFor(together, Operator)
                .DeclineManyAsync(ADeclineOfMany(chosen), CancellationToken.None)
                .ConfigureAwait(true);

        var entry = Assert.Single(Decided(many));
        var result = Assert.IsAssignableFrom<ObjectResult>(single.Result);

        if (result.Value is RequestFailure refused)
        {
            var failure = Assert.IsType<RequestFailure>(entry.Failure);
            Assert.Equal(refused.Code, failure.Code);
            Assert.Equal(refused.Message, failure.Message, StringComparer.Ordinal);
            Assert.Equal(refused.Current?.State, failure.Current?.State);
        }
        else
        {
            var moved = Assert.IsType<QueuedRequest>(result.Value);
            var row = Assert.IsType<QueuedRequest>(entry.Request);
            Assert.Equal(moved.State, row.State);
            Assert.Equal(moved.Revision, row.Revision);
        }

        // The stores are what the endpoints left behind, and they have to agree too: an answer that
        // matched while the writes differed would be the interesting failure.
        var here = await Held(alone, one.Request.Id).ConfigureAwait(true);
        var there = await Held(together, other.Request.Id).ConfigureAwait(true);

        Assert.Equal(here.Request.State, there.Request.State);
        Assert.Equal(here.Revision, there.Revision);
        Assert.Equal(here.Request.History.Count, there.Request.History.Count);
    }

    /// <summary>
    /// An action carrying no requests is refused. Answered rather than refused, it would report that
    /// it had decided everything it was asked to, which is true and is what a surface draws as done.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnActionOnNothingIsRefusedRatherThanAnsweredAsDone()
    {
        var store = new InMemoryRequestStore();

        Assert.Equal(
            nameof(ApproveManyBody.Requests),
            Field(await ControllerFor(store, Operator)
                .ApproveManyAsync(new ApproveManyBody { Requests = [] }, CancellationToken.None)
                .ConfigureAwait(true)));

        Assert.Equal(
            nameof(ApproveManyBody.Requests),
            Field(await ControllerFor(store, Operator)
                .ApproveManyAsync(new ApproveManyBody(), CancellationToken.None)
                .ConfigureAwait(true)));
    }

    /// <summary>
    /// An entry with no request in it, or none with a revision, is refused before anything is
    /// written, and the message says which position of the body is wrong.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnEntryThatNamesNoRequestOrNoRevisionIsRefusedBeforeAnythingIsWritten()
    {
        var store = new InMemoryRequestStore();
        var open = await ThreeOpenRequestsAsync(store).ConfigureAwait(true);

        var nameless = Choosing(open).ToArray();
        nameless[2] = new RequestToDecide { Revision = 1 };

        var refusal = Refusal(await ControllerFor(store, Operator)
            .ApproveManyAsync(new ApproveManyBody { Requests = nameless }, CancellationToken.None)
            .ConfigureAwait(true));

        Assert.Equal(RequestFailureCode.InvalidBody, refusal.Code);
        Assert.Contains("position 3", refusal.Message, StringComparison.Ordinal);

        var revisionless = Choosing(open).ToArray();
        revisionless[0] = new RequestToDecide { Id = open[0].Request.Id };

        Assert.Contains(
            "position 1",
            Refusal(await ControllerFor(store, Operator)
                .ApproveManyAsync(new ApproveManyBody { Requests = revisionless }, CancellationToken.None)
                .ConfigureAwait(true)).Message,
            StringComparison.Ordinal);

        // Nothing in the body was acted on, which is what "before anything is written" means.
        foreach (var one in open)
        {
            Assert.Equal(RequestState.Open, (await Held(store, one.Request.Id).ConfigureAwait(true)).Request.State);
        }
    }

    /// <summary>
    /// The same request twice in one action is refused rather than answered with a conflict the
    /// action created itself. The second entry would report that the request had moved since it was
    /// read, against a move made one line earlier by the same call.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheSameRequestTwiceInOneActionIsRefusedBeforeAnythingIsWritten()
    {
        var store = new InMemoryRequestStore();
        var open = await ThreeOpenRequestsAsync(store).ConfigureAwait(true);

        var twice = Choosing(open).Append(Choosing(open)[1]).ToArray();

        var refusal = Refusal(await ControllerFor(store, Operator)
            .ApproveManyAsync(new ApproveManyBody { Requests = twice }, CancellationToken.None)
            .ConfigureAwait(true));

        Assert.Equal(RequestFailureCode.InvalidBody, refusal.Code);
        Assert.Contains("position 2 and position 4", refusal.Message, StringComparison.Ordinal);

        foreach (var one in open)
        {
            Assert.Equal(RequestState.Open, (await Held(store, one.Request.Id).ConfigureAwait(true)).Request.State);
        }
    }

    /// <summary>
    /// An action carrying more requests than a page holds is refused rather than acted on as far as
    /// the cap. A caller given the first two hundred and not told has just decided the rest were
    /// done.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task MoreRequestsThanAPageHoldsIsRefusedRatherThanPartlyDone()
    {
        var store = new InMemoryRequestStore();
        var open = await ThreeOpenRequestsAsync(store).ConfigureAwait(true);

        var overflowing = Enumerable
            .Range(0, RequestsController.MaximumPageSize + 1)
            .Select(index => new RequestToDecide
            {
                // Identifiers a test made up rather than minted, so the same body is sent every
                // run: what is under test is the count, and none of these is ever looked up.
                Id = index < open.Count
                    ? open[index].Request.Id
                    : new Guid(string.Format(CultureInfo.InvariantCulture, "b6000000-0000-0000-0000-{0:D12}", index)),
                Revision = 1
            })
            .ToArray();

        var refusal = Refusal(await ControllerFor(store, Operator)
            .ApproveManyAsync(new ApproveManyBody { Requests = overflowing }, CancellationToken.None)
            .ConfigureAwait(true));

        Assert.Equal(RequestFailureCode.InvalidBody, refusal.Code);
        Assert.Contains(
            RequestsController.MaximumPageSize.ToString(CultureInfo.InvariantCulture),
            refusal.Message,
            StringComparison.Ordinal);

        foreach (var one in open)
        {
            Assert.Equal(RequestState.Open, (await Held(store, one.Request.Id).ConfigureAwait(true)).Request.State);
        }
    }

    /// <summary>
    /// A decline of several is held to the same rule about a reason as a decline of one, and the
    /// refusal arrives before any of the requests is touched.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ADeclineOfSeveralWithNoReasonIsRefusedBeforeAnythingIsWritten()
    {
        var store = new InMemoryRequestStore();
        var open = await ThreeOpenRequestsAsync(store).ConfigureAwait(true);

        Assert.Equal(
            nameof(DeclineManyBody.Reason),
            Field(await ControllerFor(store, Operator)
                .DeclineManyAsync(
                    new DeclineManyBody { Requests = Choosing(open) },
                    CancellationToken.None)
                .ConfigureAwait(true)));

        Assert.Equal(
            nameof(DeclineManyBody.Note),
            Field(await ControllerFor(store, Operator)
                .DeclineManyAsync(
                    new DeclineManyBody { Requests = Choosing(open), Reason = DeclineReason.Other },
                    CancellationToken.None)
                .ConfigureAwait(true)));

        foreach (var one in open)
        {
            Assert.Equal(RequestState.Open, (await Held(store, one.Request.Id).ConfigureAwait(true)).Request.State);
        }
    }

    /// <summary>
    /// A call that authenticates and names nobody decides nothing at all, rather than deciding the
    /// action and recording an empty identifier as having made it.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ACallThatNamesNobodyDecidesNothing()
    {
        var store = new InMemoryRequestStore();
        var open = await ThreeOpenRequestsAsync(store).ConfigureAwait(true);

        var refusal = Refusal(await ControllerFor(store, caller: null)
            .ApproveManyAsync(new ApproveManyBody { Requests = Choosing(open) }, CancellationToken.None)
            .ConfigureAwait(true));

        Assert.Equal(RequestFailureCode.NoUserOnTheCall, refusal.Code);

        foreach (var one in open)
        {
            Assert.Equal(RequestState.Open, (await Held(store, one.Request.Id).ConfigureAwait(true)).Request.State);
        }
    }

    /// <summary>
    /// Both decline bodies spell the reason and the note the same way.
    /// <para>
    /// One refusal names a field for both endpoints, and it names it from the single body. A rename
    /// on one side only would leave a caller of the other told that a field it does not have is
    /// wrong, which reads as a client bug rather than as this.
    /// </para>
    /// </summary>
    [Fact]
    public void BothDeclineBodiesSpellTheReasonAndTheNoteTheSameWay()
    {
        Assert.Equal(
            Named<DeclineRequestBody>([nameof(DeclineRequestBody.Reason), nameof(DeclineRequestBody.Note)]),
            Named<DeclineManyBody>([nameof(DeclineManyBody.Reason), nameof(DeclineManyBody.Note)]));
    }

    /// <summary>
    /// The properties one shape carries under the names asked for, so a rename on one side of a
    /// pair shows as a difference rather than as a test that stopped looking.
    /// </summary>
    /// <typeparam name="TBody">The body.</typeparam>
    /// <param name="names">The property names to look for.</param>
    /// <returns>The names that are there, with the type of each.</returns>
    private static string[] Named<TBody>(string[] names)
        => names
            .Select(name => typeof(TBody).GetProperty(name, BindingFlags.Public | BindingFlags.Instance))
            .Where(property => property is not null)
            .Select(property => property!.Name + ":" + property.PropertyType.Name)
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Three open requests in the store, each named by a provider so every move is available on it.
    /// </summary>
    /// <param name="store">Where they go.</param>
    /// <returns>The requests and the revisions the store holds them at.</returns>
    private async Task<IReadOnlyList<StoredRequest>> ThreeOpenRequestsAsync(InMemoryRequestStore store)
    {
        var titles = new[] { ("The Conversation", 1974, "603"), ("Stalker", 1979, "1398"), ("Solaris", 1972, "593") };
        var open = new List<StoredRequest>(titles.Length);

        foreach (var (title, year, provider) in titles)
        {
            open.Add(await store.AddAsync(
                new MediaRequest
                {
                    Id = _identifiers.NewId(),
                    RequestedByUserId = Asker,
                    RequestedAt = Started,
                    StateChangedAt = Started,
                    Kind = RequestedItemKind.Movie,
                    DisplayTitle = title,
                    DisplayYear = year,
                    ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = provider }
                },
                CancellationToken.None).ConfigureAwait(true));
        }

        return open;
    }

    /// <summary>
    /// One request in the store, in the state asked for.
    /// <para>
    /// The moves that get it there are the model's own, and the ones into an observed state are made
    /// as the plugin rather than as a person, because that is who the table admits for them.
    /// </para>
    /// </summary>
    /// <param name="store">Where it goes.</param>
    /// <param name="state">The state it has to be in.</param>
    /// <returns>The request and the revision the store holds it at.</returns>
    private async Task<StoredRequest> InStateAsync(InMemoryRequestStore store, RequestState state)
    {
        var open = (await ThreeOpenRequestsAsync(store).ConfigureAwait(true))[0];

        if (state == RequestState.Open)
        {
            return open;
        }

        var operator_ = RequestCaller.Administrator(Watcher);

        var moved = state switch
        {
            RequestState.Approved => await store.ReplaceAsync(
                RequestLifecycle.Move(open.Request, RequestState.Approved, Started, operator_),
                open.Revision,
                CancellationToken.None).ConfigureAwait(true),
            RequestState.Declined => await store.ReplaceAsync(
                RequestLifecycle.Decline(open.Request, DeclineReason.NotWanted, "Not this one.", Started, operator_),
                open.Revision,
                CancellationToken.None).ConfigureAwait(true),
            RequestState.Fulfilled => await store.ReplaceAsync(
                RequestLifecycle.Move(open.Request, RequestState.Fulfilled, Started, RequestCaller.Plugin),
                open.Revision,
                CancellationToken.None).ConfigureAwait(true),
            _ => await FailedAsync(store, open, operator_).ConfigureAwait(true)
        };

        return moved;
    }

    /// <summary>
    /// A request that was approved and then did not arrive, which is the only route into the failed
    /// state the table has.
    /// </summary>
    /// <param name="store">Where it is.</param>
    /// <param name="open">The request as the store holds it.</param>
    /// <param name="operator_">Who approves it.</param>
    /// <returns>The request and the revision the store holds it at.</returns>
    private static async Task<StoredRequest> FailedAsync(
        InMemoryRequestStore store,
        StoredRequest open,
        RequestCaller operator_)
    {
        var approved = await store.ReplaceAsync(
            RequestLifecycle.Move(open.Request, RequestState.Approved, Started, operator_),
            open.Revision,
            CancellationToken.None).ConfigureAwait(true);

        return await store.ReplaceAsync(
            RequestLifecycle.Move(approved.Request, RequestState.Failed, Started, RequestCaller.Plugin),
            approved.Revision,
            CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// The requests as a caller would choose them off a page of the queue.
    /// </summary>
    /// <param name="stored">What the store holds.</param>
    /// <returns>The selection.</returns>
    private static RequestToDecide[] Choosing(IReadOnlyList<StoredRequest> stored)
        => stored
            .Select(one => new RequestToDecide { Id = one.Request.Id, Revision = one.Revision })
            .ToArray();

    /// <summary>
    /// A decline of one request, carrying the reason and the note the many-decline uses.
    /// </summary>
    /// <param name="revision">The revision it was read at.</param>
    /// <returns>The body.</returns>
    private static DeclineRequestBody ADecline(long revision)
        => new DeclineRequestBody
        {
            Revision = revision,
            Reason = DeclineReason.NotWanted,
            Note = "This one is not going on the disk."
        };

    /// <summary>
    /// A decline of several, carrying the same reason and note as the single one above, so the two
    /// paths differ in nothing but the shape of the call.
    /// </summary>
    /// <param name="chosen">The requests.</param>
    /// <returns>The body.</returns>
    private static DeclineManyBody ADeclineOfMany(IReadOnlyList<RequestToDecide> chosen)
        => new DeclineManyBody
        {
            Requests = chosen,
            Reason = DeclineReason.NotWanted,
            Note = "This one is not going on the disk."
        };

    /// <summary>
    /// What the store holds for one request, which is what a leg asserts against rather than the
    /// answer the endpoint returned.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="id">The request.</param>
    /// <returns>The request and its revision.</returns>
    private static async Task<StoredRequest> Held(InMemoryRequestStore store, Guid id)
    {
        var stored = await store.GetAsync(id, CancellationToken.None).ConfigureAwait(true);

        return Assert.NotNull(stored);
    }

    /// <summary>
    /// A controller wired to one store and one identity, with a clock and an identifier source the
    /// test controls.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="caller">Who the server says is calling.</param>
    /// <returns>The controller under test.</returns>
    private RequestsController ControllerFor(IRequestStore store, Guid? caller)
        => new RequestsController(store, new TestClock(Started), _identifiers, new FakeCallerIdentity(caller), new FakeInstallSettings(), new RecordingJournal(), new RecordingSink(), new RecordingRequesterNotice(), new FakeLibrary());

    /// <summary>
    /// The entries an action came back with, with the status code checked.
    /// </summary>
    /// <param name="answered">What the action returned.</param>
    /// <returns>The entries.</returns>
    private static IReadOnlyList<DecidedRequest> Decided(ActionResult<DecidedRequests> answered)
    {
        var result = Assert.IsAssignableFrom<ObjectResult>(answered.Result);
        Assert.Equal(200, result.StatusCode);
        return Assert.IsType<DecidedRequests>(result.Value).Requests;
    }

    /// <summary>
    /// The refusal a whole action came back with, under the status code its class is reported with.
    /// </summary>
    /// <param name="answered">What the action returned.</param>
    /// <returns>The refusal.</returns>
    private static RequestFailure Refusal(ActionResult<DecidedRequests> answered)
    {
        var result = Assert.IsAssignableFrom<ObjectResult>(answered.Result);
        var failure = Assert.IsType<RequestFailure>(result.Value);
        Assert.Equal(RequestFailure.StatusFor(failure.Code), result.StatusCode);
        return failure;
    }

    /// <summary>
    /// The field a refused body named.
    /// </summary>
    /// <param name="answered">What the action returned.</param>
    /// <returns>The field.</returns>
    private static string? Field(ActionResult<DecidedRequests> answered)
    {
        var failure = Refusal(answered);
        Assert.Equal(RequestFailureCode.InvalidBody, failure.Code);
        Assert.NotEmpty(failure.Message);
        return failure.Field;
    }
}

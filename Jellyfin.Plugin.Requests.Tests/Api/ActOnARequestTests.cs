using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
/// Deciding on a request over the API.
/// <para>
/// The actions are called directly rather than through a server, for the reason the create tests
/// give: the headless rule refuses a running Jellyfin, and what these judge is what an endpoint does
/// with a body, an identity and a store. What that leaves out is the server evaluating the policy
/// on the action, which no test on this board makes and which <c>docs/testing.md</c> carries as a
/// refused test with what stands in for it.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class ActOnARequestTests
{
    private static readonly Guid Asker = new Guid("b2000000-0000-0000-0000-000000000001");
    private static readonly Guid Operator = new Guid("b2000000-0000-0000-0000-000000000002");
    private static readonly Guid SecondOperator = new Guid("b2000000-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset Started = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// An approval moves the request, hands back the row at its new revision, and leaves exactly one
    /// entry in the history naming the person who made it.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ApprovingAnOpenRequestMovesItAndAppendsOneEntry()
    {
        var store = new InMemoryRequestStore();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);

        var answered = await ControllerFor(store, Operator)
            .ApproveAsync(open.Request.Id, new ApproveRequestBody { Revision = open.Revision }, CancellationToken.None)
            .ConfigureAwait(true);

        var row = Moved(answered);
        var held = await Held(store, open.Request.Id).ConfigureAwait(true);

        Assert.Equal(RequestState.Approved, row.State);
        Assert.Equal(open.Revision + 1, row.Revision);
        Assert.Equal(RequestState.Approved, held.Request.State);
        Assert.Equal(Operator, held.Request.StateChangedByUserId);

        var entry = Assert.Single(held.Request.History);
        Assert.Equal(RequestState.Open, entry.From);
        Assert.Equal(RequestState.Approved, entry.To);
        Assert.Equal(Operator, entry.ByUserId);
        Assert.Equal(Started, entry.At);
    }

    /// <summary>
    /// A decline carries the reason and the note, and leaves one entry holding both. The entry is
    /// worth more than the fields on the request: taking the decline back overwrites those and
    /// leaves this.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task DecliningCarriesTheReasonAndAppendsOneEntry()
    {
        var store = new InMemoryRequestStore();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);

        var answered = await ControllerFor(store, Operator)
            .DeclineAsync(
                open.Request.Id,
                new DeclineRequestBody
                {
                    Revision = open.Revision,
                    Reason = DeclineReason.NoRoomForIt,
                    Note = "The disk this would go on is full until the new one arrives."
                },
                CancellationToken.None)
            .ConfigureAwait(true);

        var row = Moved(answered);
        var held = await Held(store, open.Request.Id).ConfigureAwait(true);

        Assert.Equal(RequestState.Declined, row.State);
        Assert.Equal(DeclineReason.NoRoomForIt, row.DeclineReason);

        var entry = Assert.Single(held.Request.History);
        Assert.Equal(RequestState.Declined, entry.To);
        Assert.Equal(DeclineReason.NoRoomForIt, entry.Reason);
        Assert.Equal("The disk this would go on is full until the new one arrives.", entry.Note, StringComparer.Ordinal);
    }

    /// <summary>
    /// Two decisions on one request leave two entries and no more. One entry per move is the
    /// property the whole history is worth reading for, and it is checked over a sequence rather
    /// than over a single call because a second entry appended by the wrong layer would not show on
    /// the first.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EachDecisionAppendsOneEntryAndNoMore()
    {
        var store = new InMemoryRequestStore();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);
        var controller = ControllerFor(store, Operator);

        var approved = Moved(await controller
            .ApproveAsync(open.Request.Id, new ApproveRequestBody { Revision = open.Revision }, CancellationToken.None)
            .ConfigureAwait(true));

        await controller
            .DeclineAsync(
                open.Request.Id,
                new DeclineRequestBody { Revision = approved.Revision, Reason = DeclineReason.NotWanted },
                CancellationToken.None)
            .ConfigureAwait(true);

        var held = await Held(store, open.Request.Id).ConfigureAwait(true);

        Assert.Equal(2, held.Request.History.Count);
        Assert.Equal([RequestState.Approved, RequestState.Declined], held.Request.History.Select(entry => entry.To));
    }

    /// <summary>
    /// A decision made against a revision that has moved is refused, the store keeps what the other
    /// operator decided, and the answer carries the row as it now reads so the caller can decide
    /// again against it rather than re-reading the queue.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ADecisionOnARequestThatMovedIsRefusedWithWhatTheQueueHoldsNow()
    {
        var store = new InMemoryRequestStore();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);

        await ControllerFor(store, Operator)
            .ApproveAsync(open.Request.Id, new ApproveRequestBody { Revision = open.Revision }, CancellationToken.None)
            .ConfigureAwait(true);

        var late = await ControllerFor(store, SecondOperator)
            .DeclineAsync(
                open.Request.Id,
                new DeclineRequestBody { Revision = open.Revision, Reason = DeclineReason.NotWanted },
                CancellationToken.None)
            .ConfigureAwait(true);

        var refused = Refusal(late, expectedStatus: 409);
        var held = await Held(store, open.Request.Id).ConfigureAwait(true);

        Assert.Equal(RequestFailureCode.MovedSinceItWasRead, refused.Code);
        Assert.NotNull(refused.Current);
        Assert.Equal(RequestState.Approved, refused.Current!.State);
        Assert.Equal(open.Revision + 1, refused.Current.Revision);
        Assert.Equal(RequestState.Approved, held.Request.State);
        Assert.Single(held.Request.History);
    }

    /// <summary>
    /// The window between the read and the write is refused too. The revision check above answers
    /// the ordinary case, where the caller has been holding a stale row; this is the case where the
    /// row was current when the call started and stopped being current while it ran, and only the
    /// store can catch it.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ADecisionOvertakenWhileItIsBeingWrittenIsRefused()
    {
        var inner = new InMemoryRequestStore();
        var open = await AnOpenRequestAsync(inner).ConfigureAwait(true);
        var store = new StoreThatMovesARequestUnderTheWrite(
            inner,
            request => RequestLifecycle.Move(request, RequestState.Approved, Started, RequestCaller.Administrator(SecondOperator)));

        var answered = await ControllerFor(store, Operator)
            .DeclineAsync(
                open.Request.Id,
                new DeclineRequestBody { Revision = open.Revision, Reason = DeclineReason.NotWanted },
                CancellationToken.None)
            .ConfigureAwait(true);

        var refused = Refusal(answered, expectedStatus: 409);
        var held = await Held(inner, open.Request.Id).ConfigureAwait(true);

        Assert.Equal(RequestFailureCode.MovedSinceItWasRead, refused.Code);
        Assert.NotNull(refused.Current);
        Assert.Equal(RequestState.Approved, refused.Current!.State);
        Assert.Equal(RequestState.Approved, held.Request.State);
        Assert.Equal(SecondOperator, held.Request.StateChangedByUserId);
    }

    /// <summary>
    /// A request that moved into a state this move is illegal from is refused as having moved, not
    /// as illegal. Both answers are a refusal and they tell the operator different things: one says
    /// somebody else got there first and here is what they did, the other says this request can
    /// never be approved. The second is false, and it is what an endpoint that asked the table
    /// before it looked at the revision would say.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARequestThatMovedIntoAStateTheTableRefusesIsRefusedAsMoved()
    {
        var store = new InMemoryRequestStore();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);
        var arrived = RequestLifecycle.Move(open.Request, RequestState.Fulfilled, Started, RequestCaller.Plugin);
        await store.ReplaceAsync(arrived, open.Revision, CancellationToken.None).ConfigureAwait(true);

        var answered = await ControllerFor(store, Operator)
            .ApproveAsync(open.Request.Id, new ApproveRequestBody { Revision = open.Revision }, CancellationToken.None)
            .ConfigureAwait(true);

        var refused = Refusal(answered, expectedStatus: 409);

        Assert.Equal(RequestFailureCode.MovedSinceItWasRead, refused.Code);
        Assert.NotNull(refused.Current);
        Assert.Equal(RequestState.Fulfilled, refused.Current!.State);
    }

    /// <summary>
    /// A move the table refuses comes back with the table's own sentence for that cell, so an
    /// operator is told why the move is not available rather than that it is not.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AMoveTheTableRefusesIsRefusedWithTheTablesOwnReason()
    {
        var store = new InMemoryRequestStore();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);
        var fulfilled = RequestLifecycle.Move(open.Request, RequestState.Fulfilled, Started, RequestCaller.Plugin);
        var held = await store.ReplaceAsync(fulfilled, open.Revision, CancellationToken.None).ConfigureAwait(true);

        var answered = await ControllerFor(store, Operator)
            .ApproveAsync(held.Request.Id, new ApproveRequestBody { Revision = held.Revision }, CancellationToken.None)
            .ConfigureAwait(true);

        var refused = Refusal(answered, expectedStatus: 409);

        Assert.Equal(RequestFailureCode.TheTableRefusesTheMove, refused.Code);
        Assert.Contains(
            RequestLifecycle.Cell(RequestState.Fulfilled, RequestState.Approved).Why,
            refused.Message,
            StringComparison.Ordinal);
        Assert.Equal(RequestState.Fulfilled, (await Held(store, held.Request.Id).ConfigureAwait(true)).Request.State);
    }

    /// <summary>
    /// A request nobody can act on because it names nothing may only be declined. Approving one is
    /// refused with a value of its own rather than with the table's refusal, because the request is
    /// in a state the move is legal from and the thing standing in the way is the request itself.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARequestThatNamesNothingCanOnlyBeDeclined()
    {
        var store = new InMemoryRequestStore();
        var typed = await store
            .AddAsync(
                new MediaRequest
                {
                    Id = _identifiers.NewId(),
                    RequestedByUserId = Asker,
                    RequestedAt = Started,
                    StateChangedAt = Started,
                    Kind = RequestedItemKind.Movie,
                    DisplayTitle = "A title somebody typed"
                },
                CancellationToken.None)
            .ConfigureAwait(true);

        var approving = await ControllerFor(store, Operator)
            .ApproveAsync(typed.Request.Id, new ApproveRequestBody { Revision = typed.Revision }, CancellationToken.None)
            .ConfigureAwait(true);

        var refused = Refusal(approving, expectedStatus: 409);
        Assert.Equal(RequestFailureCode.TheRequestNamesNothing, refused.Code);

        var declining = await ControllerFor(store, Operator)
            .DeclineAsync(
                typed.Request.Id,
                new DeclineRequestBody { Revision = typed.Revision, Reason = DeclineReason.CannotBeObtained },
                CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(RequestState.Declined, Moved(declining).State);
    }

    /// <summary>
    /// A decision on a request the store does not hold is not found, and nothing is written.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ADecisionOnARequestThatIsNotThereIsNotFound()
    {
        var store = new InMemoryRequestStore();

        var answered = await ControllerFor(store, Operator)
            .ApproveAsync(_identifiers.NewId(), new ApproveRequestBody { Revision = 1 }, CancellationToken.None)
            .ConfigureAwait(true);

        var refused = Refusal(answered, expectedStatus: 404);

        Assert.Equal(RequestFailureCode.NoSuchRequest, refused.Code);
        Assert.Null(refused.Current);
        Assert.Empty(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>
    /// A decision carrying no revision is refused rather than read as a revision nobody sent. This
    /// is the shape a script written against the endpoint arrives in, and obeying it would be the
    /// silent overwrite the revision exists to refuse.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ADecisionCarryingNoRevisionIsRefused()
    {
        var store = new InMemoryRequestStore();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);
        var controller = ControllerFor(store, Operator);

        var approving = await controller
            .ApproveAsync(open.Request.Id, new ApproveRequestBody(), CancellationToken.None)
            .ConfigureAwait(true);

        var declining = await controller
            .DeclineAsync(
                open.Request.Id,
                new DeclineRequestBody { Reason = DeclineReason.NotWanted },
                CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(nameof(ApproveRequestBody.Revision), Field(approving), StringComparer.Ordinal);
        Assert.Equal(nameof(DeclineRequestBody.Revision), Field(declining), StringComparer.Ordinal);
        Assert.Equal(RequestState.Open, (await Held(store, open.Request.Id).ConfigureAwait(true)).Request.State);
    }

    /// <summary>
    /// A call with no body at all names the body rather than the field that would have been in it.
    /// A caller told its revision is missing, when what it actually sent was nothing, is a caller
    /// looking for a bug in the wrong place.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ADecisionWithNoBodyNamesTheBody()
    {
        var store = new InMemoryRequestStore();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);
        var controller = ControllerFor(store, Operator);

        var approving = await controller
            .ApproveAsync(open.Request.Id, null!, CancellationToken.None)
            .ConfigureAwait(true);

        var declining = await controller
            .DeclineAsync(open.Request.Id, null!, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal("body", Field(approving), StringComparer.Ordinal);
        Assert.Equal("body", Field(declining), StringComparer.Ordinal);
        Assert.Equal(RequestState.Open, (await Held(store, open.Request.Id).ConfigureAwait(true)).Request.State);
    }

    /// <summary>
    /// The decline bodies that are refused, each wrong in one field, so a refusal naming a different
    /// one is a failure rather than a message somebody has to read.
    /// </summary>
    /// <param name="field">The field that has to be named.</param>
    /// <param name="reason">The reason the body carries.</param>
    /// <param name="note">The note the body carries.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData(nameof(DeclineRequestBody.Reason), null, "A note without a reason")]
    [InlineData(nameof(DeclineRequestBody.Reason), (DeclineReason)97, null)]
    [InlineData(nameof(DeclineRequestBody.Note), DeclineReason.Other, null)]
    [InlineData(nameof(DeclineRequestBody.Note), DeclineReason.Other, "   ")]
    public async Task ADeclineThatSaysNothingIsRefused(string field, DeclineReason? reason, string? note)
    {
        var store = new InMemoryRequestStore();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);

        var answered = await ControllerFor(store, Operator)
            .DeclineAsync(
                open.Request.Id,
                new DeclineRequestBody { Revision = open.Revision, Reason = reason, Note = note },
                CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(field, Field(answered), StringComparer.Ordinal);
        Assert.Equal(RequestState.Open, (await Held(store, open.Request.Id).ConfigureAwait(true)).Request.State);
    }

    /// <summary>
    /// A note longer than a request keeps is refused rather than cut, by the same rule and the same
    /// number as the note the person who asked wrote. Nothing is stored that was not written.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ADeclineNoteTooLongToKeepIsRefused()
    {
        var store = new InMemoryRequestStore();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);

        var answered = await ControllerFor(store, Operator)
            .DeclineAsync(
                open.Request.Id,
                new DeclineRequestBody
                {
                    Revision = open.Revision,
                    Reason = DeclineReason.NotWanted,
                    Note = new string('n', MediaRequest.NoteMaximumLength + 1)
                },
                CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(nameof(DeclineRequestBody.Note), Field(answered), StringComparer.Ordinal);
        Assert.Equal(RequestState.Open, (await Held(store, open.Request.Id).ConfigureAwait(true)).Request.State);
    }

    /// <summary>
    /// A call that authenticated and names nobody is refused. A decision is somebody's, and the
    /// history entry has a field for who made it that would otherwise read as the plugin having
    /// observed something.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ADecisionByACallerNamingNoUserIsRefused()
    {
        var store = new InMemoryRequestStore();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);

        var answered = await ControllerFor(store, caller: null)
            .ApproveAsync(open.Request.Id, new ApproveRequestBody { Revision = open.Revision }, CancellationToken.None)
            .ConfigureAwait(true);

        var refused = Refusal(answered, expectedStatus: 403);

        Assert.Equal(RequestFailureCode.NoUserOnTheCall, refused.Code);
        Assert.Null(refused.Field);
        Assert.Equal(RequestState.Open, (await Held(store, open.Request.Id).ConfigureAwait(true)).Request.State);
    }

    /// <summary>
    /// Both endpoints build one caller, an administrator, so the only authority these two moves are
    /// ever attempted under is that one. Every legal cell they can reach admits it.
    /// <para>
    /// This is why the arm answering <see cref="RequestFailureCode.TheCallerMayNotMakeThisMove"/> is
    /// not reached by any leg above: no call can produce it while this holds. It is the day it stops
    /// holding that the arm is for, and this reds on that day rather than the endpoint returning a
    /// failure nobody shaped.
    /// </para>
    /// </summary>
    /// <param name="to">The state one of the two endpoints moves into.</param>
    [Theory]
    [InlineData(RequestState.Approved)]
    [InlineData(RequestState.Declined)]
    public void TheOnlyCallerTheseEndpointsBuildIsAdmittedByEveryMoveTheyCanMake(RequestState to)
    {
        var reachable = RequestLifecycle.Table
            .Where(cell => cell.To == to && cell.IsLegal)
            .ToArray();

        Assert.NotEmpty(reachable);
        Assert.Equal(
            [],
            reachable
                .Where(cell => (cell.Permitted & RequestActor.Administrator) == RequestActor.None)
                .Select(cell => string.Concat(cell.From.ToString(), " to ", cell.To.ToString()))
                .ToArray());
    }

    /// <summary>
    /// An open request in the store, asked for by somebody and named by one provider so every move
    /// is available on it.
    /// </summary>
    /// <param name="store">Where it goes.</param>
    /// <returns>The request and the revision the store holds it at.</returns>
    private async Task<StoredRequest> AnOpenRequestAsync(InMemoryRequestStore store)
        => await store.AddAsync(
            new MediaRequest
            {
                Id = _identifiers.NewId(),
                RequestedByUserId = Asker,
                RequestedAt = Started,
                StateChangedAt = Started,
                Kind = RequestedItemKind.Movie,
                DisplayTitle = "The Conversation",
                DisplayYear = 1974,
                ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "603" }
            },
            CancellationToken.None).ConfigureAwait(true);

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
        => new RequestsController(store, new TestClock(Started), _identifiers, new FakeCallerIdentity(caller), new FakeInstallSettings(), new RecordingJournal(), new RecordingSink(), new RecordingRequesterNotice(), new RecordingArrivalNotice(), new FakeLibrary());

    /// <summary>
    /// The row a successful decision handed back, with the status code checked.
    /// </summary>
    /// <param name="answered">What the action returned.</param>
    /// <returns>The row.</returns>
    private static QueuedRequest Moved(ActionResult<QueuedRequest> answered)
    {
        var result = Assert.IsAssignableFrom<ObjectResult>(answered.Result);
        Assert.Equal(200, result.StatusCode);
        return Assert.IsType<QueuedRequest>(result.Value);
    }

    /// <summary>
    /// The refusal a decision came back with, and the status code it came back under.
    /// </summary>
    /// <param name="answered">What the action returned.</param>
    /// <param name="expectedStatus">The status code it has to have used.</param>
    /// <returns>The refusal.</returns>
    private static RequestFailure Refusal(ActionResult<QueuedRequest> answered, int expectedStatus)
    {
        var result = Assert.IsAssignableFrom<ObjectResult>(answered.Result);
        Assert.Equal(expectedStatus, result.StatusCode);
        var failure = Assert.IsType<RequestFailure>(result.Value);
        Assert.Equal(RequestFailure.StatusFor(failure.Code), result.StatusCode);
        return failure;
    }

    /// <summary>
    /// The field a refused body named, with the status code checked.
    /// </summary>
    /// <param name="answered">What the action returned.</param>
    /// <returns>The field.</returns>
    private static string? Field(ActionResult<QueuedRequest> answered)
    {
        var result = Assert.IsAssignableFrom<ObjectResult>(answered.Result);
        Assert.Equal(400, result.StatusCode);
        var failure = Assert.IsType<RequestFailure>(result.Value);
        Assert.Equal(RequestFailureCode.InvalidBody, failure.Code);
        Assert.NotEmpty(failure.Message);
        return failure.Field;
    }
}

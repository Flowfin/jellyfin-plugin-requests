using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Bridge;

/// <summary>
/// What approving does about an external request service, and what it keeps of the answer.
/// <para>
/// Two of these go through the endpoint, because the claim is about approving rather than about a
/// method: a submission that only happens when something calls it directly is a submission an
/// operator never triggers. The rest go through <see cref="BridgeSubmission"/> on its own, for the
/// states the endpoint cannot produce - a request that already carries a reference, and a store that
/// refuses the second write of one decision.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class SubmittingAnApprovalTests
{
    private static readonly Guid Asker = new Guid("c8000000-0000-0000-0000-000000000001");
    private static readonly Guid Operator = new Guid("c8000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Started = new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.Zero);

    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// Approving with a service configured hands the request over once and keeps what the service
    /// called it, on the request in the store and on the row the caller is answered with.
    /// <para>
    /// The row matters as much as the store. The operator's page holds the revision it was answered
    /// with and sends it back on the next action, so answering the revision from before the reference
    /// was written would refuse that action for a conflict this call created itself.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnApprovalWithAServiceConfiguredIsHandedOverOnceAndTheReferenceIsKept()
    {
        var store = new InMemoryRequestStore();
        var service = new AServiceThatKeepsWhatItIsHanded();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);

        var answered = await ControllerOver(store, service)
            .ApproveAsync(open.Request.Id, new ApproveRequestBody { Revision = open.Revision }, CancellationToken.None)
            .ConfigureAwait(true);

        var row = Moved(answered);
        var held = await Held(store, open.Request.Id).ConfigureAwait(true);

        Assert.Equal(open.Request.Id, Assert.Single(service.Handed).Id);
        Assert.Equal(AServiceThatKeepsWhatItIsHanded.Name, held.Request.Backend?.Service);
        Assert.Equal("svc-1", held.Request.Backend?.Id);
        Assert.Equal(RequestState.Approved, held.Request.State);
        Assert.Equal(held.Revision, row.Revision);
    }

    /// <summary>
    /// A submission that failed leaves the request approved, carrying no reference, and says so in
    /// the log with the identifier of the request it is about.
    /// <para>
    /// The approval standing is the whole rule. An operator decided, and a plugin that undid the
    /// decision because a service on another machine refused would be overruling a person for a
    /// reason that has nothing to do with the request. The absent reference is what makes the failure
    /// recoverable: it is what the refusal to submit twice reads, so a request that has one is done
    /// and a request that has none can be handed over again.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AFailedSubmissionLeavesTheRequestApprovedAndSaysSoInTheLog()
    {
        var store = new InMemoryRequestStore();
        var log = new RecordingLogger();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);

        var answered = await ControllerOver(store, new AServiceThatWillNotTakeAnything(), log)
            .ApproveAsync(open.Request.Id, new ApproveRequestBody { Revision = open.Revision }, CancellationToken.None)
            .ConfigureAwait(true);

        var row = Moved(answered);
        var held = await Held(store, open.Request.Id).ConfigureAwait(true);

        Assert.Equal(RequestState.Approved, held.Request.State);
        Assert.Equal(RequestState.Approved, row.State);
        Assert.Null(held.Request.Backend);

        var reported = Assert.Single(log.At(LogLevel.Error));

        Assert.Contains(open.Request.Id.ToString(), reported.Message, StringComparison.Ordinal);
        Assert.Equal(AServiceThatWillNotTakeAnything.Complaint, reported.Exception?.Message);
    }

    /// <summary>
    /// A request that already carries a reference is not handed over a second time.
    /// <para>
    /// This is the answer to what submitting the same request twice does, and it is refused rather
    /// than harmless. A second submission of something a service already accepted is a second copy of
    /// the same download over there, and nothing on this side could tell the two apart afterwards.
    /// </para>
    /// <para>
    /// It goes through the submission directly because the endpoint cannot reach this state: the
    /// transition table refuses approving a request that is already approved. What can reach it is a
    /// request sent onward again after it failed, and the reference it carries is from the first time.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARequestThatWasAlreadyHandedOverIsNotHandedOverAgain()
    {
        var store = new InMemoryRequestStore();
        var service = new AServiceThatKeepsWhatItIsHanded();
        var submission = new BridgeSubmission(service, store, TestClock.AtAFixedMoment(), new RecordingLogger());
        var approved = await AnApprovedRequestAsync(store).ConfigureAwait(true);

        var once = await submission.SubmitAsync(approved, CancellationToken.None).ConfigureAwait(true);
        var twice = await submission.SubmitAsync(once, CancellationToken.None).ConfigureAwait(true);

        Assert.Single(service.Handed);
        Assert.Equal("svc-1", twice.Request.Backend?.Id);
        Assert.Equal(once.Revision, twice.Revision);
    }

    /// <summary>
    /// A reference the store would not take is reported with what the service called it, and nothing
    /// is submitted again.
    /// <para>
    /// This is the one case here that needs somebody to look: the service holds the request and this
    /// queue does not know so. Retrying would be the duplicate the rule above exists against, so the
    /// log carries the service's own identifier instead and the two can be reconciled by hand.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AReferenceThatCouldNotBeWrittenBackIsReportedWithWhatTheServiceCalledIt()
    {
        var store = new InMemoryRequestStore();
        var log = new RecordingLogger();
        var service = new AServiceThatKeepsWhatItIsHanded();
        var approved = await AnApprovedRequestAsync(store).ConfigureAwait(true);

        // Somebody else moves the request in the window between the decision being written and the
        // reference being written onto it, so the store refuses the second write for a conflict.
        var contended = new StoreThatMovesARequestUnderTheWrite(
            store,
            request => request with { RequesterNote = "moved by somebody else" });

        var after = await new BridgeSubmission(service, contended, TestClock.AtAFixedMoment(), log)
            .SubmitAsync(approved, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Single(service.Handed);
        Assert.Null(after.Request.Backend);

        var reported = Assert.Single(log.At(LogLevel.Error));

        Assert.Contains(AServiceThatKeepsWhatItIsHanded.Name, reported.Message, StringComparison.Ordinal);
        Assert.Contains("svc-1", reported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// On a server with no external service an approval keeps nothing and writes no line about it.
    /// <para>
    /// That is the shipping install and it is the one this must not become noisy on. The bridge there
    /// hands back no reference, which is an answer rather than a failure, so an error in the log
    /// would be an operator being told something went wrong on every decision they make.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnApprovalOnAServerWithNoServiceKeepsNothingAndSaysNothing()
    {
        var store = new InMemoryRequestStore();
        var log = new RecordingLogger();
        var approved = await AnApprovedRequestAsync(store).ConfigureAwait(true);

        var after = await new BridgeSubmission(new NoRequestBackend(), store, TestClock.AtAFixedMoment(), log)
            .SubmitAsync(approved, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Null(after.Request.Backend);
        Assert.Equal(approved.Revision, after.Revision);
        Assert.Empty(log.Lines);
    }

    /// <summary>
    /// A move that is not an approval hands nothing over. Declining is a decision that this server
    /// will not fetch the title, and a service asked to fetch it anyway would be doing the opposite
    /// of what the operator said.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task DecliningHandsNothingOver()
    {
        var store = new InMemoryRequestStore();
        var service = new AServiceThatKeepsWhatItIsHanded();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);

        await ControllerOver(store, service)
            .DeclineAsync(
                open.Request.Id,
                new DeclineRequestBody { Revision = open.Revision, Reason = DeclineReason.AlreadyInTheLibrary },
                CancellationToken.None)
            .ConfigureAwait(true);

        var held = await Held(store, open.Request.Id).ConfigureAwait(true);

        Assert.Equal(RequestState.Declined, held.Request.State);
        Assert.Empty(service.Handed);
        Assert.Null(held.Request.Backend);
    }

    /// <summary>
    /// An open request in the store, named by one provider so every move is available on it.
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
    /// A request the store already holds as approved, which is the state the submission is handed.
    /// </summary>
    /// <param name="store">Where it goes.</param>
    /// <returns>The request and the revision the store holds it at.</returns>
    private async Task<StoredRequest> AnApprovedRequestAsync(InMemoryRequestStore store)
    {
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);

        return await store.ReplaceAsync(
            RequestLifecycle.Move(open.Request, RequestState.Approved, Started, RequestCaller.Administrator(Operator)),
            open.Revision,
            CancellationToken.None).ConfigureAwait(true);
    }

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
    /// A controller whose approval reaches the service handed in.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="service">The external request service.</param>
    /// <param name="log">Where a failed submission is reported.</param>
    /// <returns>The controller under test.</returns>
    private RequestsController ControllerOver(
        InMemoryRequestStore store,
        IRequestBackend service,
        ILogger? log = null)
        => new RequestsController(
            store,
            new TestClock(Started),
            _identifiers,
            new FakeCallerIdentity(Operator),
            new FakeInstallSettings(),
            new RecordingJournal(),
            new RecordingSink(),
            new RecordingRequesterNotice(),
            new RecordingArrivalNotice(),
            new FakeLibrary(),
            new BridgeSubmission(service, store, TestClock.AtAFixedMoment(), log ?? new RecordingLogger()));

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
}

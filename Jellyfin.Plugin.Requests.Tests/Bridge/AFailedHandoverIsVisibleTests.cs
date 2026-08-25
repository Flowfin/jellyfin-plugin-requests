using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Bridge;

/// <summary>
/// What an operator working the queue can see about a handover, and in particular about one that
/// failed.
/// <para>
/// The failure this is written against is a silent one. An approval the service never took carries
/// no reference, and so does an approval nothing was ever tried on, so with one field the request
/// that needs somebody is the one that looks ordinary. Everything here is about the pair of fields
/// making three states out of two.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class AFailedHandoverIsVisibleTests
{
    private static readonly Guid Asker = new Guid("d3000000-0000-0000-0000-000000000001");
    private static readonly Guid Operator = new Guid("d3000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Started = new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tried = new DateTimeOffset(2026, 8, 24, 11, 30, 0, TimeSpan.Zero);

    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// An approval the service refused is marked with the moment it was tried, rather than left
    /// looking like an approval nothing has reached yet.
    /// <para>
    /// Through the endpoint, because the claim is about approving. The row the caller is answered
    /// with carries it as well as the store: the operator's page draws that row back into the queue
    /// it came from, so a mark only in the store would appear the next time somebody turned the page
    /// and not at the moment the approval was made.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnApprovalTheServiceRefusedIsMarkedWithTheMomentItWasTried()
    {
        var store = new InMemoryRequestStore();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);

        var answered = await ControllerOver(store, new AServiceThatWillNotTakeAnything())
            .ApproveAsync(open.Request.Id, new ApproveRequestBody { Revision = open.Revision }, CancellationToken.None)
            .ConfigureAwait(true);

        var row = Assert.IsType<QueuedRequest>(Assert.IsType<OkObjectResult>(answered.Result).Value);
        var held = await Held(store, open.Request.Id).ConfigureAwait(true);

        // The approval stands. Nothing about a service that would not answer may undo what a person
        // decided, which is the rule this mark is written beside rather than against.
        Assert.Equal(RequestState.Approved, held.Request.State);
        Assert.Equal(RequestState.Approved, row.State);

        Assert.Null(held.Request.Backend);
        Assert.Null(row.Backend);

        Assert.Equal(Tried, held.Request.HandoverFailedAt);
        Assert.Equal(Tried, row.HandoverFailedAt);

        // The row is answered at the revision the store now holds, because the page sends that
        // revision back on the operator's next action and a stale one would refuse it for a conflict
        // this call created itself.
        Assert.Equal(held.Revision, row.Revision);
    }

    /// <summary>
    /// A handover that succeeds clears an earlier mark, and clears it in the write that keeps the
    /// reference rather than in one of its own.
    /// <para>
    /// Two fields that can both be set would say the service has it and that handing it over failed,
    /// which are opposite facts. A second write to clear the mark would be a window in which the page
    /// says exactly that, so the leg asserts the revision moved once rather than twice.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AHandoverThatSucceedsClearsTheMarkInTheWriteThatKeepsTheReference()
    {
        var store = new InMemoryRequestStore();
        var approved = await AnApprovedRequestAsync(store).ConfigureAwait(true);

        var failed = await Submission(store, new AServiceThatWillNotTakeAnything())
            .SubmitAsync(approved, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(Tried, failed.Request.HandoverFailedAt);

        var accepted = await Submission(store, new AServiceThatKeepsWhatItIsHanded())
            .SubmitAsync(failed, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.NotNull(accepted.Request.Backend);
        Assert.Null(accepted.Request.HandoverFailedAt);
        Assert.Equal(failed.Revision + 1, accepted.Revision);

        var held = await Held(store, approved.Request.Id).ConfigureAwait(true);

        Assert.Null(held.Request.HandoverFailedAt);
    }

    /// <summary>
    /// The three states are pairwise different on the rows the queue answers: nothing tried, tried
    /// and failed, and handed over.
    /// <para>
    /// This is the condition the issue is about, and it is asserted over the pair rather than over
    /// either field. A leg per field would pass on a queue where two of the three states are
    /// indistinguishable, which is exactly the queue this work exists to stop.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task NothingTriedAndTriedAndFailedAndHandedOverAreThreeDifferentRows()
    {
        var store = new InMemoryRequestStore();

        var untried = await AnApprovedRequestAsync(store).ConfigureAwait(true);
        var failing = await AnApprovedRequestAsync(store).ConfigureAwait(true);
        var taken = await AnApprovedRequestAsync(store).ConfigureAwait(true);

        await Submission(store, new AServiceThatWillNotTakeAnything())
            .SubmitAsync(failing, CancellationToken.None)
            .ConfigureAwait(true);

        await Submission(store, new AServiceThatKeepsWhatItIsHanded())
            .SubmitAsync(taken, CancellationToken.None)
            .ConfigureAwait(true);

        var answered = await ControllerOver(store, new NoRequestBackend())
            .QueueAsync(cancellationToken: CancellationToken.None)
            .ConfigureAwait(true);

        var page = Assert.IsType<RequestsPage<QueuedRequest>>(Assert.IsType<OkObjectResult>(answered.Result).Value);
        var rows = page.Requests.ToDictionary(request => request.Id);

        Assert.Equal(3, rows.Values.Select(Said).Distinct(StringComparer.Ordinal).Count());

        Assert.Null(rows[untried.Request.Id].Backend);
        Assert.Null(rows[untried.Request.Id].HandoverFailedAt);

        Assert.Null(rows[failing.Request.Id].Backend);
        Assert.Equal(Tried, rows[failing.Request.Id].HandoverFailedAt);

        // What the service called it, carried through unread, because what an operator does next is
        // quote it at that service.
        Assert.Equal("svc-1", rows[taken.Request.Id].Backend?.Id);
        Assert.Null(rows[taken.Request.Id].HandoverFailedAt);
    }

    /// <summary>
    /// A store that refuses the mark leaves the approval and the answer exactly as they were.
    /// <para>
    /// The near-miss for the write this adds. It runs after a decision the store already accepted, so
    /// an exception escaping it would turn a decision that was taken into a call that reports failure,
    /// and an operator would press the button again against a queue that had already moved.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AStoreThatRefusesTheMarkCostsNeitherTheApprovalNorTheAnswer()
    {
        var store = new InMemoryRequestStore();
        var approved = await AnApprovedRequestAsync(store).ConfigureAwait(true);

        var contended = new StoreThatMovesARequestUnderTheWrite(
            store,
            request => request with { RequesterNote = "moved by somebody else" });

        var after = await new BridgeSubmission(
                new AServiceThatWillNotTakeAnything(),
                contended,
                new TestClock(Tried),
                new RecordingLogger())
            .SubmitAsync(approved, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(RequestState.Approved, after.Request.State);
        Assert.Equal(approved.Revision, after.Revision);
        Assert.Null(after.Request.HandoverFailedAt);
    }

    /// <summary>
    /// One row said as the pair of fields an operator reads it by, so a leg compares states rather
    /// than one field at a time.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The two fields, as one string.</returns>
    private static string Said(QueuedRequest row)
        => string.Concat(
            row.Backend?.Id ?? "no reference",
            " / ",
            row.HandoverFailedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "no failure");

    /// <summary>
    /// The submission under test, over the service handed in, at the moment a failure is marked with.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="service">The external request service.</param>
    /// <returns>The submission.</returns>
    private static BridgeSubmission Submission(IRequestStore store, IRequestBackend service)
        => new BridgeSubmission(service, store, new TestClock(Tried), new RecordingLogger());

    /// <summary>
    /// What the store holds for one request.
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
    /// A controller whose approval reaches the service handed in, with the clock the mark is written
    /// from.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="service">The external request service.</param>
    /// <returns>The controller under test.</returns>
    private RequestsController ControllerOver(IRequestStore store, IRequestBackend service)
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
            new BridgeSubmission(service, store, new TestClock(Tried), new RecordingLogger()));
}

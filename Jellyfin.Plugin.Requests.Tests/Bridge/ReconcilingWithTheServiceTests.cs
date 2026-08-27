using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Bridge;

/// <summary>
/// What asking the external service where things stand does to the queue, and what it refuses to do
/// to it.
/// <para>
/// The condition worth protecting here is the third one on #83, and it is the failure an operator
/// never forgives: a decision made on this server is never reversed by anything the service says. It
/// is held by which requests the run looks at rather than by a check inside the loop, so the legs
/// below assert that a declined request is not asked about at all - a stronger claim than that it did
/// not move, because the second could be true by accident.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class ReconcilingWithTheServiceTests
{
    private static readonly Guid Asker = new Guid("c9000000-0000-0000-0000-000000000001");
    private static readonly Guid Operator = new Guid("c9000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Started = new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Started.AddHours(3);

    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// A service that says it gave up moves the request to failed, through the table, with the
    /// plugin as the actor and an entry in the history for it.
    /// <para>
    /// This is the second condition of #83 in one leg. The actor is the part that is easy to lose: a
    /// move made on somebody else's word is not a move a person made, and a history entry naming an
    /// operator for it would put a decision in front of somebody who did not take one.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AServiceThatGaveUpMovesTheRequestToFailedWithThePluginAsTheActor()
    {
        var store = new InMemoryRequestStore();
        var journal = new RecordingJournal();
        var approved = await AnApprovedRequestAsync(store, "svc-1").ConfigureAwait(true);

        var report = await ReconcilingAsync(store, Saying("svc-1", "FAILED"), journal: journal).ConfigureAwait(true);
        var held = await Held(store, approved.Request.Id).ConfigureAwait(true);
        var entry = held.Request.History[^1];

        Assert.Equal(1, report.Moved);
        Assert.Equal(1, report.Examined);
        Assert.Equal(RequestState.Failed, held.Request.State);
        Assert.Equal(RequestState.Approved, entry.From);
        Assert.Equal(RequestState.Failed, entry.To);
        Assert.Equal(RequestActor.Plugin, entry.By);
        Assert.Equal(Later, entry.At);
        Assert.Single(journal.Written);
    }

    /// <summary>
    /// A local decline is not reversed by anything the service says, and it is not asked about at
    /// all.
    /// <para>
    /// The service is told to say the one word that moves a request, about the reference the declined
    /// request still carries. Both halves are asserted: the request is still declined, and the run
    /// never asked, which is what makes the rule a property of the run's shape rather than of a
    /// mapping that happens not to reach it.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ALocalDeclineIsNeverReversedAndIsNeverAskedAbout()
    {
        var store = new InMemoryRequestStore();
        var approved = await AnApprovedRequestAsync(store, "svc-1").ConfigureAwait(true);

        var declined = await store.ReplaceAsync(
            RequestLifecycle.Decline(
                approved.Request,
                DeclineReason.NotWanted,
                note: null,
                Started,
                RequestCaller.Administrator(Operator)),
            approved.Revision,
            CancellationToken.None).ConfigureAwait(true);

        var service = Saying("svc-1", "FAILED");
        var report = await ReconcilingAsync(store, service).ConfigureAwait(true);
        var held = await Held(store, declined.Request.Id).ConfigureAwait(true);

        Assert.Empty(service.AskedAbout);
        Assert.Equal(0, report.Examined);
        Assert.Equal(0, report.Moved);
        Assert.Equal(RequestState.Declined, held.Request.State);
        Assert.Equal(declined.Revision, held.Revision);
    }

    /// <summary>
    /// A fulfilled request is not asked about either, for the same reason and a different one: the
    /// library said the person can watch it, and no service on another machine knows better.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AFulfilledRequestIsNeverAskedAbout()
    {
        var store = new InMemoryRequestStore();
        var approved = await AnApprovedRequestAsync(store, "svc-1").ConfigureAwait(true);

        await store.ReplaceAsync(
            RequestLifecycle.Move(approved.Request, RequestState.Fulfilled, Started, RequestCaller.Plugin),
            approved.Revision,
            CancellationToken.None).ConfigureAwait(true);

        var service = Saying("svc-1", "FAILED");
        var report = await ReconcilingAsync(store, service).ConfigureAwait(true);
        var held = await Held(store, approved.Request.Id).ConfigureAwait(true);

        Assert.Empty(service.AskedAbout);
        Assert.Equal(RequestState.Fulfilled, held.Request.State);
        Assert.Equal(0, report.Examined);
    }

    /// <summary>
    /// A request nothing was ever handed over for is not asked about, because there is nothing to ask
    /// about it with.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARequestThatWasNeverHandedOverIsNeverAskedAbout()
    {
        var store = new InMemoryRequestStore();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);

        await store.ReplaceAsync(
            RequestLifecycle.Move(open.Request, RequestState.Approved, Started, RequestCaller.Administrator(Operator)),
            open.Revision,
            CancellationToken.None).ConfigureAwait(true);

        var service = Saying("svc-1", "FAILED");
        var report = await ReconcilingAsync(store, service).ConfigureAwait(true);

        Assert.Empty(service.AskedAbout);
        Assert.Equal(0, report.Examined);
    }

    /// <summary>
    /// A word the mapping table has never seen produces a logged refusal and leaves the request
    /// exactly as it is.
    /// <para>
    /// This is the other half of not corrupting a request on a surprising answer. Nothing here
    /// guesses at a word it does not hold, because a guess is a request in a state nobody chose and
    /// the cost of being wrong lands on somebody who cannot see why.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AWordNothingKnowsIsRefusedInTheLogAndMovesNothing()
    {
        var store = new InMemoryRequestStore();
        var log = new RecordingLogger();
        var approved = await AnApprovedRequestAsync(store, "svc-1").ConfigureAwait(true);

        var report = await ReconcilingAsync(store, Saying("svc-1", "ABANDONED"), log).ConfigureAwait(true);
        var held = await Held(store, approved.Request.Id).ConfigureAwait(true);

        Assert.Equal(1, report.Examined);
        Assert.Equal(0, report.Moved);
        Assert.Equal(1, report.Refused);
        Assert.Equal(RequestState.Approved, held.Request.State);
        Assert.Equal(approved.Revision, held.Revision);
        Assert.Contains(
            log.At(LogLevel.Warning),
            line => line.Message.Contains("ABANDONED", StringComparison.Ordinal)
                && line.Message.Contains(approved.Request.Id.ToString(), StringComparison.Ordinal));
    }

    /// <summary>
    /// A word the table knows and deliberately does nothing about moves nothing and is not a refusal.
    /// <para>
    /// The two are different answers and the report tells them apart. A run that counted an inert row
    /// as a refusal would have an operator looking for a defect every time their service reported
    /// that its own approval step had run.
    /// </para>
    /// </summary>
    /// <param name="reported">A word the table holds and acts on in neither direction.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData("PENDING")]
    [InlineData("APPROVED")]
    [InlineData("COMPLETED")]
    [InlineData("AVAILABLE")]
    public async Task AWordTheTableKnowsAndDoesNothingAboutIsNotARefusal(string reported)
    {
        var store = new InMemoryRequestStore();
        var approved = await AnApprovedRequestAsync(store, "svc-1").ConfigureAwait(true);

        var report = await ReconcilingAsync(store, Saying("svc-1", reported)).ConfigureAwait(true);
        var held = await Held(store, approved.Request.Id).ConfigureAwait(true);

        Assert.Equal(1, report.Examined);
        Assert.Equal(0, report.Moved);
        Assert.Equal(0, report.Refused);
        Assert.Equal(RequestState.Approved, held.Request.State);
    }

    /// <summary>
    /// The service's word is matched however it is spelled, because the table already ignores case
    /// and this run reads the table rather than the word.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheWordIsMatchedHoweverTheServiceSpellsIt()
    {
        var store = new InMemoryRequestStore();
        var approved = await AnApprovedRequestAsync(store, "svc-1").ConfigureAwait(true);

        var report = await ReconcilingAsync(store, Saying("svc-1", "failed")).ConfigureAwait(true);
        var held = await Held(store, approved.Request.Id).ConfigureAwait(true);

        Assert.Equal(1, report.Moved);
        Assert.Equal(RequestState.Failed, held.Request.State);
    }

    /// <summary>
    /// A service that did not answer says so in the log, walks nothing and moves nothing.
    /// <para>
    /// One line for the run rather than one per request. The fact is about the service, and a run
    /// that asked about every request in turn against a service that is down would produce a page of
    /// failures for a single thing an operator has to fix.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnUnreachableServiceIsSaidRatherThanSwallowedAndNothingIsWalked()
    {
        var store = new InMemoryRequestStore();
        var log = new RecordingLogger();
        var approved = await AnApprovedRequestAsync(store, "svc-1").ConfigureAwait(true);

        var service = new AServiceThatSaysWhereThingsStand(
            Says("svc-1", "FAILED"),
            BackendReachability.Unreachable);

        var report = await ReconcilingAsync(store, service, log).ConfigureAwait(true);
        var held = await Held(store, approved.Request.Id).ConfigureAwait(true);

        Assert.False(report.Asked);
        Assert.Equal(BackendReachability.Unreachable, report.Reachability);
        Assert.Equal(0, report.Examined);
        Assert.Empty(service.AskedAbout);
        Assert.Equal(RequestState.Approved, held.Request.State);
        Assert.Single(log.At(LogLevel.Warning));
    }

    /// <summary>
    /// A service that could not be asked whether it is there is the same answer as one that did not
    /// answer, and it is reported rather than raised out of the task.
    /// <para>
    /// An unhandled exception in a scheduled task is a task that stops running, and a reconciliation
    /// that stopped running is a queue that drifts with nothing saying so.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AServiceThatCouldNotBeAskedIsReportedRatherThanRaised()
    {
        var store = new InMemoryRequestStore();
        var log = new RecordingLogger();
        await AnApprovedRequestAsync(store, "svc-1").ConfigureAwait(true);

        var report = await ReconcilingAsync(
            store,
            new AServiceThatSaysWhereThingsStand(refusesTheCheck: true),
            log).ConfigureAwait(true);

        Assert.False(report.Asked);
        Assert.Equal(BackendReachability.Unreachable, report.Reachability);
        Assert.Single(log.At(LogLevel.Warning));
    }

    /// <summary>
    /// An install with no service configured does nothing, walks nothing, and says nothing above
    /// debug.
    /// <para>
    /// This is what most servers are, and the run costs one call on them. A line per run at warning
    /// would fill an operator's log with the fact that they run one plugin rather than two systems.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnInstallWithNoServiceDoesNothingAndIsQuietAboutIt()
    {
        var store = new InMemoryRequestStore();
        var log = new RecordingLogger();
        await AnApprovedRequestAsync(store, "svc-1").ConfigureAwait(true);

        var service = new AServiceThatSaysWhereThingsStand(
            Says("svc-1", "FAILED"),
            BackendReachability.NotConfigured);

        var report = await ReconcilingAsync(store, service, log).ConfigureAwait(true);

        Assert.False(report.Asked);
        Assert.Equal(BackendReachability.NotConfigured, report.Reachability);
        Assert.Empty(service.AskedAbout);
        Assert.Empty(log.At(LogLevel.Warning));
        Assert.Empty(log.At(LogLevel.Error));
        Assert.Empty(log.At(LogLevel.Information));
    }

    /// <summary>
    /// One request the service cannot answer about does not stop the others.
    /// <para>
    /// The failure is a fact about one reference - it may have been issued by a service an operator
    /// has since replaced - and a run that stopped at the first one would let a single unknown title
    /// hold up every other request on the server.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task OneRequestTheServiceCannotAnswerAboutDoesNotStopTheOthers()
    {
        var store = new InMemoryRequestStore();
        var log = new RecordingLogger();
        var first = await AnApprovedRequestAsync(store, "svc-1").ConfigureAwait(true);
        var second = await AnApprovedRequestAsync(store, "svc-2").ConfigureAwait(true);

        var service = new AServiceThatSaysWhereThingsStand(
            Says("svc-2", "FAILED"),
            cannotAnswerAbout: ["svc-1"]);

        var report = await ReconcilingAsync(store, service, log).ConfigureAwait(true);

        Assert.Equal(2, report.Examined);
        Assert.Equal(1, report.Moved);
        Assert.Equal(RequestState.Approved, (await Held(store, first.Request.Id).ConfigureAwait(true)).Request.State);
        Assert.Equal(RequestState.Failed, (await Held(store, second.Request.Id).ConfigureAwait(true)).Request.State);
        Assert.Single(log.At(LogLevel.Warning));
    }

    /// <summary>
    /// A reference the service knows nothing about moves nothing and is not a refusal.
    /// <para>
    /// Nothing known is an ordinary answer rather than a fault: a reference issued by a service an
    /// operator has since replaced is a fact about the install, which is what the interface says of
    /// its own null answer.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AReferenceTheServiceKnowsNothingAboutMovesNothing()
    {
        var store = new InMemoryRequestStore();
        var log = new RecordingLogger();
        var approved = await AnApprovedRequestAsync(store, "svc-1").ConfigureAwait(true);

        var report = await ReconcilingAsync(store, new AServiceThatSaysWhereThingsStand(), log).ConfigureAwait(true);
        var held = await Held(store, approved.Request.Id).ConfigureAwait(true);

        Assert.Equal(1, report.Examined);
        Assert.Equal(0, report.Moved);
        Assert.Equal(0, report.Refused);
        Assert.Equal(RequestState.Approved, held.Request.State);
        Assert.Empty(log.At(LogLevel.Warning));
    }

    /// <summary>
    /// The mapping table holds no word in both of the service's lists.
    /// <para>
    /// This is the assumption the run rests on rather than one it makes. The interface hands back one
    /// word and does not say which vocabulary it came from, so the run asks each list in turn; a word
    /// in both would make the answer depend on the order of two lines. Refused here rather than
    /// worked around, because the repair is a row in the table and not a branch in the loop.
    /// </para>
    /// </summary>
    [Fact]
    public void NoWordTheServiceUsesIsInBothOfItsLists()
    {
        var inBoth = BackendStates.Table
            .GroupBy(row => row.Reported, StringComparer.OrdinalIgnoreCase)
            .Where(words => words.Select(row => row.Vocabulary).Distinct().Count() > 1)
            .Select(words => words.Key)
            .OrderBy(word => word, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(BackendStates.Table);
        Assert.Equal([], inBoth);
    }

    /// <summary>
    /// A request that moved while the service was being asked about it is left as the newer decision
    /// left it.
    /// <para>
    /// Dropped rather than retried, and that is the precedence rule again in its narrowest form: a
    /// retry here would put the service's word over an operator's answer given a moment ago.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARequestThatMovedUnderTheRunIsLeftAsTheNewerDecisionLeftIt()
    {
        var inner = new InMemoryRequestStore();
        var approved = await AnApprovedRequestAsync(inner, "svc-1").ConfigureAwait(true);

        // Wrapped after the request is in place, so the one write this double interferes with is the
        // run's own rather than the one that set the leg up.
        var store = new StoreThatMovesARequestUnderTheWrite(
            inner,
            request => RequestLifecycle.Decline(
                request,
                DeclineReason.NotWanted,
                note: null,
                Started,
                RequestCaller.Administrator(Operator)));

        var report = await ReconcilingAsync(store, Saying("svc-1", "FAILED")).ConfigureAwait(true);
        var held = await Held(inner, approved.Request.Id).ConfigureAwait(true);

        Assert.Equal(1, report.Examined);
        Assert.Equal(0, report.Moved);
        Assert.Equal(1, report.Refused);
        Assert.Equal(RequestState.Declined, held.Request.State);
    }

    /// <summary>
    /// What the service reports for one reference.
    /// </summary>
    /// <param name="id">The reference.</param>
    /// <param name="reported">The word.</param>
    /// <returns>The dictionary the double takes.</returns>
    private static Dictionary<string, string?> Says(string id, string reported)
        => new Dictionary<string, string?>(StringComparer.Ordinal) { [id] = reported };

    /// <summary>
    /// A service that reports one word about one reference and is reachable.
    /// </summary>
    /// <param name="id">The reference.</param>
    /// <param name="reported">The word.</param>
    /// <returns>The service.</returns>
    private static AServiceThatSaysWhereThingsStand Saying(string id, string reported)
        => new AServiceThatSaysWhereThingsStand(Says(id, reported));

    /// <summary>
    /// What the store holds for one request, which is what a leg asserts against rather than the
    /// report the run returned.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="id">The request.</param>
    /// <returns>The request and its revision.</returns>
    private static async Task<StoredRequest> Held(InMemoryRequestStore store, Guid id)
    {
        var stored = await store.GetAsync(id, CancellationToken.None).ConfigureAwait(false);

        return Assert.NotNull(stored);
    }

    /// <summary>
    /// One run of the reconciliation over the store and service handed in.
    /// </summary>
    /// <param name="store">Where the requests are.</param>
    /// <param name="service">The service.</param>
    /// <param name="log">Where the run reports.</param>
    /// <param name="journal">The activity log.</param>
    /// <returns>What the run looked at and what it did.</returns>
    private static Task<ReconciliationReport> ReconcilingAsync(
        IRequestStore store,
        IRequestBackend service,
        RecordingLogger? log = null,
        RecordingJournal? journal = null)
        => new BridgeReconciliation(
            store,
            service,
            new TestClock(Later),
            journal ?? new RecordingJournal(),
            new RecordingRequesterNotice(),
            new BridgeWatch(),
            log ?? new RecordingLogger()).ReconcileAsync(CancellationToken.None);

    /// <summary>
    /// One open request in the store.
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
                DisplayTitle = "Solaris",
                DisplayYear = 1972,

                // An approval needs one. A request nothing can match in a library and nothing can be
                // submitted for is refused the moves that need an identifier, which is the model
                // holding a rule this file is not about.
                ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "593" }
            },
            CancellationToken.None).ConfigureAwait(true);

    /// <summary>
    /// One approved request carrying the reference a service issued for it, which is the state a
    /// handover leaves behind.
    /// </summary>
    /// <param name="store">Where it goes.</param>
    /// <param name="reference">What the service called it.</param>
    /// <returns>The request and the revision the store holds it at.</returns>
    private async Task<StoredRequest> AnApprovedRequestAsync(InMemoryRequestStore store, string reference)
    {
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);
        var approved = RequestLifecycle.Move(
            open.Request,
            RequestState.Approved,
            Started,
            RequestCaller.Administrator(Operator));

        return await store.ReplaceAsync(
            approved with { Backend = AServiceThatSaysWhereThingsStand.Reference(reference) },
            open.Revision,
            CancellationToken.None).ConfigureAwait(true);
    }
}

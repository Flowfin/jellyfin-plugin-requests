using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Identity;
using Jellyfin.Plugin.Requests.Intake;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Time;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Seam;

/// <summary>
/// The call that does the work of the seam: one want from the sibling discover plugin, turned into a
/// request in this plugin's queue.
/// <para>
/// <b>A title this plugin has never seen is the ordinary case.</b> There is no pre-agreed catalogue
/// on this side and nothing here fetches one, so a want naming something absent from every request
/// ever made is a request being made for the first time and not an error. The title and the year are
/// stored as they arrived and are never refreshed, which is what lets the queue render on a server
/// where nothing outbound resolves.
/// </para>
/// <para>
/// <b>A refusal is a decision and reaches the caller as one bit.</b> The contract carries no field
/// for a reason, so what the other side learns is that the handover was not accepted, and the reason
/// is written to this server's log where an operator can find it. Answering <see langword="false"/>
/// rather than throwing is deliberate: an exception crossing a plugin boundary is a fault in the
/// caller's own code path for something that is an ordinary answer here.
/// </para>
/// <para>
/// <b>That holds for what nothing here foresaw as well, and for waiting.</b> The refusals below are
/// the ones this side can name; the boundary catches everything else and answers the same one bit,
/// so a defect in this plugin cannot become an exception on a surface it does not own. Waiting is
/// the other half of the same promise: a call into the queue is raced against
/// <see cref="DefaultAnswerWithin"/> and refused where the queue has not answered by then, because a
/// sink that hangs stalls the gesture behind it just as surely as one that throws. Both are safe to
/// do because a want carries an identifier and the other side hands it over again.
/// </para>
/// <para>
/// <b>Whether an ask joins an existing request is not decided here.</b> It is
/// <see cref="RequestIntake"/>'s, which is the same object the HTTP endpoint asks, so two users
/// wanting the same film produce one request with both of them recorded however each of them asked.
/// </para>
/// <para>
/// <b>The same want twice is one request, and that is a separate rule from the one above.</b> The
/// want identifier is an idempotency key: the other side derives it from the title and the user and
/// hands it over again after a refresh that recreated the item, after a restart, and after a gesture
/// undone and redone. It is looked up before anything is built, over everything the store holds and
/// in every state, so a want whose request was declined is still a want that has been taken. The
/// identity rule cannot stand in for this, because it compares provider identifiers and a want
/// carrying none is different from every other want including another copy of itself.
/// </para>
/// </summary>
public sealed class WantHandover : IWantHandover
{
    /// <summary>
    /// The contract version this plugin implements.
    /// <para>
    /// One number rather than a range, and the rule around it is in <c>docs/seam.md</c>. The
    /// sibling's board has not minted a version rule yet, so what this constant records is which
    /// version this side believes it implements rather than a number read off the contract. That is
    /// cheap to correct because this seam is an in-process call: it serialises nothing, nothing on
    /// disk carries it, and no caller outside this process can be pinned to it.
    /// </para>
    /// </summary>
    public const int KnownContractVersion = 1;

    /// <summary>
    /// How long this side waits for the queue before it answers without it.
    /// <para>
    /// The number is deliberately larger than any write this store makes and far smaller than a
    /// person's patience on the surface the other plugin draws. What it is really bounding is the
    /// case no timeout on the store itself can bound, which is a store that has stopped answering
    /// rather than one that is slow, and the cost of being wrong about it is one want handed over
    /// again rather than one want lost.
    /// </para>
    /// </summary>
    public static readonly TimeSpan DefaultAnswerWithin = TimeSpan.FromSeconds(5);

    private readonly IRequestStore _store;
    private readonly RequestIntake _intake;
    private readonly IClock _clock;
    private readonly IIdentifierSource _identifiers;
    private readonly IInstallSettings _settings;
    private readonly IKnownUsers _users;
    private readonly IArrivalNotice _arrivals;
    private readonly ILogger _logger;
    private readonly TimeSpan _answerWithin;

    /// <summary>
    /// Initializes a new instance of the <see cref="WantHandover"/> class.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="clock">The injected clock, so a request's times are testable.</param>
    /// <param name="identifiers">Where a new request's identifier comes from.</param>
    /// <param name="settings">What this install is set to.</param>
    /// <param name="users">The server's answer to whether it has the user a want names.</param>
    /// <param name="arrivals">
    /// Where administrators signed in at that moment are told that somebody has asked for something.
    /// This side announces its own arrivals rather than leaving it to the endpoint, because a want
    /// handed across here is a request an operator has to work exactly like one typed at the API,
    /// and a path wired at one of the two surfaces would carry some arrivals while reading as though
    /// it carried all of them.
    /// </param>
    /// <param name="logger">Where a refusal is written.</param>
    /// <param name="answerWithin">
    /// How long to wait for the queue before answering without it. A server passes
    /// <see cref="DefaultAnswerWithin"/>; it is a parameter so the bound can be proven rather than
    /// waited out.
    /// </param>
    /// <exception cref="ArgumentNullException">Where anything it needs is missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Where the bound is negative.</exception>
    public WantHandover(
        IRequestStore store,
        IClock clock,
        IIdentifierSource identifiers,
        IInstallSettings settings,
        IKnownUsers users,
        IArrivalNotice arrivals,
        ILogger logger,
        TimeSpan answerWithin)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(identifiers);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(arrivals);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(answerWithin, TimeSpan.Zero);

        _store = store;
        _intake = new RequestIntake(store, settings);
        _clock = clock;
        _identifiers = identifiers;
        _settings = settings;
        _users = users;
        _arrivals = arrivals;
        _logger = logger;
        _answerWithin = answerWithin;
    }

    /// <inheritdoc />
    public async Task<bool> AcceptAsync(HandedOverWant want, CancellationToken cancellationToken)
    {
        if (want is null)
        {
            _logger.LogWarning(
                "A handover crossed the seam carrying no field set at all, so there was nothing to make a request of: {Refusal}.",
                HandoverRefusal.NothingWasHandedOver);

            return false;
        }

        try
        {
            return await TakeAsync(want, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception reason)
#pragma warning restore CA1031
        {
            // Nothing at all leaves this call. The caller is another plugin, and the thing it is
            // serving is a user's gesture on a surface this plugin does not own, so a fault of this
            // plugin's arriving there is that user's gesture failing for a reason nobody on that
            // side can act on. Every ordinary outcome is refused by name above; this catches what
            // nothing here foresaw, including a cancellation, which is one more way of saying no
            // request was made.
            _logger.LogError(
                reason,
                "A want handed over the seam ran into something nothing here expected, so it was refused. The other side calls it {WantId}.",
                want.WantId);

            return Refused(want, HandoverRefusal.SomethingBeneathThisSeamFailed);
        }
    }

    /// <summary>
    /// The handover itself, with every refusal it can decide on by name.
    /// <para>
    /// It is separate from <see cref="AcceptAsync"/> so that the boundary is one place rather than a
    /// promise made again at every return. What leaves here may throw; what leaves the method above
    /// may not.
    /// </para>
    /// </summary>
    /// <param name="want">The field set the contract fixes.</param>
    /// <param name="cancellationToken">Cancels the handover.</param>
    /// <returns>Whether a request for this want now exists.</returns>
    private async Task<bool> TakeAsync(HandedOverWant want, CancellationToken cancellationToken)
    {
        // The version is read before any other field. A field set whose version this side does not
        // know is refused whole rather than read for the fields it recognises: reading what is
        // recognised and ignoring the rest makes a version that changed the meaning of a field
        // indistinguishable from one that added a field, and the first of those is a want filed
        // against the wrong thing rather than a want dropped.
        if (want.ContractVersion != KnownContractVersion)
        {
            return Refused(want, HandoverRefusal.ContractVersionNotKnown);
        }

        if (want.WantId == Guid.Empty)
        {
            return Refused(want, HandoverRefusal.NoWantNamed);
        }

        if (want.RequestedByUserId == Guid.Empty)
        {
            return Refused(want, HandoverRefusal.NoUserNamed);
        }

        // The identifier cannot be verified as the person who actually asked, and this side is in no
        // position to check that: there is no session on a call from another plugin. What it can
        // check is that the identifier names somebody, which is the difference between trusting the
        // caller's permission check and storing a request against a user nobody has.
        if (!_users.Has(want.RequestedByUserId))
        {
            return Refused(want, HandoverRefusal.UserNotOnThisServer);
        }

        if (string.IsNullOrWhiteSpace(want.Title))
        {
            return Refused(want, HandoverRefusal.NoTitle);
        }

        if (!Enum.IsDefined(want.Kind))
        {
            return Refused(want, HandoverRefusal.KindNotRecognised);
        }

        bool accepted;

        try
        {
            accepted = Accepts(want.Kind);
        }
        catch (InvalidConfigurationException)
        {
            return Refused(want, HandoverRefusal.ThisInstallCannotRun);
        }

        if (!accepted)
        {
            return Refused(want, HandoverRefusal.KindNotAccepted);
        }

        Answer<StoredRequest?> lookup;

        try
        {
            lookup = await WithinTheBoundAsync(
                _store.FindByWantAsync(want.WantId, cancellationToken)).ConfigureAwait(false);
        }
        catch (RequestStoreLoadException)
        {
            return Refused(want, HandoverRefusal.TheStoreCouldNotBeReached);
        }

        if (!lookup.InTime)
        {
            return Refused(want, HandoverRefusal.TheStoreDidNotAnswerInTime);
        }

        if (lookup.Value is StoredRequest already)
        {
            // The same want again, which is the ordinary case rather than a defect: the other side
            // hands one over after a refresh that recreated the item, after a restart, and after a
            // gesture undone and redone. It is accepted, because a request for it exists, and
            // nothing is written, because it is already recorded. Whatever state that request is in
            // counts, including declined: a want that was answered has still been taken.
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "The want {WantId} was handed over again and is already request {RequestId}, so nothing was made.",
                    want.WantId,
                    already.Request.Id);
            }

            return true;
        }

        var asked = _clock.UtcNow;

        var incoming = new MediaRequest
        {
            Id = _identifiers.NewId(),
            RequestedByUserId = want.RequestedByUserId,
            RequestedAt = asked,
            StateChangedAt = asked,
            Kind = want.Kind,
            DisplayTitle = want.Title,
            DisplayYear = want.Year,
            ProviderIds = want.ProviderIds,
            WantIds = [want.WantId]
        };

        // The one thing a request made here records about the seam. Which plugin handed the want
        // over is not part of it and never will be: the contract carries no field naming the caller,
        // decided on #118, and a value read off anything else would be the sender saying who they
        // are. What this side knows is that no session stood behind the person named, and that is
        // what the entry says.
        //
        // Which of the two seam values it is comes from the marker the other side sends and from
        // nothing inferred here. A replay and a live gesture cross on this same call, and an
        // operator meeting a queue that filled up overnight has no other way to tell them apart:
        // the moment on the entry is when the replay ran, because that is the only moment this side
        // ever sees. Anything that is not the marker set is a want being expressed now, which is
        // what absence means on the contract and what every build from before it sends.
        incoming = RequestLifecycle.Arriving(
            incoming,
            want.Replay == true ? RequestArrival.SeamReplay : RequestArrival.Seam);

        Answer<IntakeResult> intake;

        try
        {
            intake = await WithinTheBoundAsync(
                _intake.AskAsync(incoming, RequestCaller.User(want.RequestedByUserId), cancellationToken)).ConfigureAwait(false);
        }
        catch (RequestStoreLoadException)
        {
            // The store says which file and why in its own log line. What is added here is which
            // want was lost because of it, so the two can be put together.
            return Refused(want, HandoverRefusal.TheStoreCouldNotBeReached);
        }
        catch (RequestQuotaReachedException)
        {
            // The numbers are not carried across: the contract answers whether the want was taken
            // and nothing else, and an operator who needs to know why reads the refusal in the log.
            return Refused(want, HandoverRefusal.TheyAreAtTheirQuota);
        }
        catch (RequestConcurrencyException)
        {
            // The request kept moving underneath the join for as many attempts as the intake makes.
            // That is a contended request rather than a fault, and the contract has no way to ask
            // the caller to try again, so it is refused and the other side's own repeat handling is
            // what brings the want back.
            return Refused(want, HandoverRefusal.TheStoreCouldNotBeReached);
        }

        if (!intake.InTime)
        {
            return Refused(want, HandoverRefusal.TheStoreDidNotAnswerInTime);
        }

        if (intake.Value.Outcome == IntakeOutcome.Created)
        {
            // Only a request that came into existence, for the reason the endpoint gives at its own
            // call: a join is a second person on a row an operator has already been shown. What is
            // announced is the stored request rather than the want, so an administrator's client is
            // handed the same document whichever surface the ask arrived on.
            _arrivals.Tell(OutboundNotice.For(intake.Value.Request.Request, NoticeEvent.Asked));
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "The want {WantId} handed over the seam is request {RequestId}, which the handover {Outcome}.",
                want.WantId,
                intake.Value.Request.Request.Id,
                intake.Value.Outcome);
        }

        return true;
    }

    /// <summary>
    /// Waits for one call into the queue and gives up on it after <see cref="_answerWithin"/>.
    /// <para>
    /// <b>Why this is not a cancellation token handed down.</b> A token is a request the callee
    /// honours, and the case worth bounding is the one where it does not: a write holding a lock
    /// nothing releases, or a disk that has stopped answering, leaves a task that never completes
    /// however politely it is asked to. Racing the call is the only shape that returns whatever the
    /// thing underneath is doing, which is what this side promised the surface it does not own.
    /// </para>
    /// <para>
    /// <b>The abandoned call is left running and its fault is swallowed.</b> Stopping it is not
    /// available, and a fault nobody reads would otherwise arrive later as an unobserved exception
    /// on an unrelated thread. What it might still write is safe to have written: a want carries an
    /// identifier, the other side hands the same one over again, and a request made by the
    /// abandoned call is recognised as the repeat it is rather than made twice.
    /// </para>
    /// </summary>
    /// <typeparam name="T">What the call answers with.</typeparam>
    /// <param name="work">The call already started.</param>
    /// <returns>The answer, or a note that none arrived in time.</returns>
    private async Task<Answer<T>> WithinTheBoundAsync<T>(Task<T> work)
    {
        using var finished = new CancellationTokenSource();

        var bound = Task.Delay(_answerWithin, finished.Token);

        if (!ReferenceEquals(await Task.WhenAny(work, bound).ConfigureAwait(false), work))
        {
            _ = work.ContinueWith(
                static abandoned => _ = abandoned.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

            return new Answer<T>(false, default!);
        }

        await finished.CancelAsync().ConfigureAwait(false);

        return new Answer<T>(true, await work.ConfigureAwait(false));
    }

    /// <summary>
    /// Whether this install accepts that kind of thing at all.
    /// <para>
    /// Asked of the settings on every handover rather than held, because an operator turning a kind
    /// off means the next want of that kind, not the next restart.
    /// </para>
    /// </summary>
    /// <param name="kind">What was wanted.</param>
    /// <returns><see langword="true"/> where this install takes requests for it.</returns>
    /// <exception cref="InvalidConfigurationException">
    /// Where what is stored is something this plugin cannot run on.
    /// </exception>
    private bool Accepts(RequestedItemKind kind)
    {
        var current = _settings.Current;

        return kind switch
        {
            RequestedItemKind.Movie => current.AcceptsMovies,
            RequestedItemKind.Series => current.AcceptsSeries,

            _ => false
        };
    }

    /// <summary>
    /// Writes down why a want was not turned into a request, and answers the caller.
    /// <para>
    /// The line carries the sibling's own identifier for the want, the reason, and the version that
    /// arrived, which is what an operator asked about a want by the other side's identifier needs to
    /// find it. It carries no title and no user, because a log is pasted into issue trackers and
    /// what somebody asked for is the thing in this plugin worth being careful with.
    /// </para>
    /// </summary>
    /// <param name="want">What arrived.</param>
    /// <param name="refusal">Why it is not becoming a request.</param>
    /// <returns>Always <see langword="false"/>, which is what the contract lets this side say.</returns>
    private bool Refused(HandedOverWant want, HandoverRefusal refusal)
    {
        _logger.LogWarning(
            "A want handed over the seam was not made into a request: {Refusal}. The other side calls it {WantId} and built it against contract version {ContractVersion}.",
            refusal,
            want.WantId,
            want.ContractVersion);

        return false;
    }

    /// <summary>
    /// What one bounded call into the queue came back with.
    /// </summary>
    /// <typeparam name="T">What the call answers with.</typeparam>
    /// <param name="InTime">Whether it answered before this side stopped waiting.</param>
    /// <param name="Value">What it answered, where it did.</param>
    private readonly record struct Answer<T>(bool InTime, T Value);
}

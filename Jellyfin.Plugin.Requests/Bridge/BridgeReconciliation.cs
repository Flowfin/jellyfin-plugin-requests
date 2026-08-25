using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Localisation;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Time;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Bridge;

/// <summary>
/// Asks the external service where the requests handed to it stand, and applies what it says through
/// the table rather than to the record.
/// <para>
/// The service is where a handed-over request is actually worked on, so its state moves without this
/// plugin being told. Without this the queue drifts from what is really happening and an operator
/// learns not to trust it, which costs more than the queue being wrong: a page nobody believes is a
/// page nobody reads.
/// </para>
/// <para>
/// <b>The precedence rule is the shape and not a condition.</b> A decision made on this server is
/// never reversed by anything the service says, and what enforces that is which requests this run
/// looks at: only requests that are still <see cref="RequestState.Approved"/>, which is the one
/// state a handover leaves behind. A declined request carrying a reference is never asked about, so
/// there is no answer that could resurrect it, and a fulfilled one is never asked about either,
/// because the library said it arrived and the service cannot know better. A local decline that a
/// remote system keeps resurrecting is the failure an operator never forgives, and it is cheaper to
/// build into the loop than to add to one that already reads well.
/// </para>
/// <para>
/// <b>Nothing is invented for a word the table has never seen.</b>
/// <see cref="BackendStates.Lookup"/> answers nothing for it, and this refuses and says so rather
/// than guessing, which is what keeps a surprising external state from becoming a request in a state
/// nobody chose. A word the table knows and deliberately does nothing about is a different answer
/// and passes silently, because that is the table doing its job.
/// </para>
/// <para>
/// <b>An unreachable service is said rather than swallowed, and nothing is walked.</b> A run that
/// asked about every request in turn against a service that is down would produce one failure per
/// request for one fact, and the fact is about the service. An install with no service configured is
/// not a failure at all and says nothing above debug: that is what most servers are.
/// </para>
/// <para>
/// <b>One request's failure never stops the others.</b> A service that cannot answer about one
/// reference is a fact about that reference - it may have been issued by a service an operator has
/// since replaced - so the run reports it and carries on. Which failures an adapter tells apart, and
/// what each of them then does, is #86 and is not decided here.
/// </para>
/// </summary>
public sealed class BridgeReconciliation
{
    private readonly IRequestStore _store;
    private readonly IRequestBackend _backend;
    private readonly IClock _clock;
    private readonly IActivityJournal _journal;
    private readonly IRequesterNotice _told;
    private readonly BridgeWatch _watch;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BridgeReconciliation"/> class.
    /// </summary>
    /// <param name="store">Where the requests are.</param>
    /// <param name="backend">The service, or the one that has none behind it.</param>
    /// <param name="clock">The injected clock, so a move carries a moment a test can choose.</param>
    /// <param name="journal">The activity log an operator reads afterwards.</param>
    /// <param name="told">The path that tells the person who asked.</param>
    /// <param name="watch">Where a service answering is recorded for the operator page.</param>
    /// <param name="logger">The server's log, where every refusal names the request it was about.</param>
    /// <exception cref="ArgumentNullException">Where anything it needs is missing.</exception>
    public BridgeReconciliation(
        IRequestStore store,
        IRequestBackend backend,
        IClock clock,
        IActivityJournal journal,
        IRequesterNotice told,
        BridgeWatch watch,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(told);
        ArgumentNullException.ThrowIfNull(watch);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _backend = backend;
        _clock = clock;
        _journal = journal;
        _told = told;
        _watch = watch;
        _logger = logger;
    }

    /// <summary>
    /// Whether this run may ask the service about this request.
    /// <para>
    /// Two conditions and both are necessary. It has to have been handed over, or there is nothing
    /// to ask about; and it has to still be approved, which is the state a handover leaves behind
    /// and the only one a decision here has not since moved it out of.
    /// </para>
    /// </summary>
    /// <param name="request">The request being judged.</param>
    /// <returns><see langword="true"/> where the service may be asked about it.</returns>
    public static bool IsOutstanding(MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Backend is not null && request.State == RequestState.Approved;
    }

    /// <summary>
    /// Asks the service about every outstanding request and applies what it says.
    /// </summary>
    /// <param name="cancellationToken">Cancels the run between requests.</param>
    /// <returns>What the run looked at and what it did.</returns>
    public async Task<ReconciliationReport> ReconcileAsync(CancellationToken cancellationToken)
    {
        var reachability = await ReachabilityAsync(cancellationToken).ConfigureAwait(false);

        if (reachability != BackendReachability.Reachable)
        {
            return new ReconciliationReport { Asked = false, Reachability = reachability };
        }

        var held = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var examined = 0;
        var moved = 0;
        var refused = 0;

        foreach (var stored in held)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsOutstanding(stored.Request) || stored.Request.Backend is not BackendReference reference)
            {
                continue;
            }

            examined++;

            var report = await ReportedAsync(stored.Request.Id, reference, cancellationToken).ConfigureAwait(false);

            if (report is null)
            {
                // Either the service knows nothing about this reference, which is an ordinary answer
                // about a reference issued by a service an operator has since replaced, or the call
                // failed and has already been reported. Neither is a refusal to count: nothing was
                // said for this run to decline to act on.
                continue;
            }

            var destination = Destination(stored.Request.Id, report);

            if (destination is not RequestState to)
            {
                // A word the table knows and does nothing about, or one it has never seen. The
                // second was counted and reported inside Destination; the first is the table doing
                // its job and passes in silence.
                refused += Known(report) ? 0 : 1;
                continue;
            }

            if (await MovedAsync(stored, to, cancellationToken).ConfigureAwait(false))
            {
                moved++;
            }
            else
            {
                refused++;
            }
        }

        // At information rather than debug when anything moved: these are requests this plugin moved
        // on somebody else's word, and an operator asked why a request failed should find the run
        // that did it in the log they already read.
        if (moved > 0 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Reconciled {Examined} request(s) with the external request service and moved {Moved} of them.",
                examined,
                moved);
        }

        return new ReconciliationReport
        {
            Asked = true,
            Reachability = reachability,
            Examined = examined,
            Moved = moved,
            Refused = refused
        };
    }

    /// <summary>
    /// Whether the table holds this word at all, in either of the service's two lists.
    /// </summary>
    /// <param name="report">What the service said.</param>
    /// <returns><see langword="true"/> where some row names it.</returns>
    private static bool Known(BackendReport report)
        => BackendStates.Lookup(BackendVocabulary.RequestStatus, report) is not null
            || BackendStates.Lookup(BackendVocabulary.MediaStatus, report) is not null;

    /// <summary>
    /// Asks the bridge whether it is there, records the answer for the operator page, and turns every
    /// way of not answering into one.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>What the bridge said, and <see cref="BackendReachability.Unreachable"/> where asking failed.</returns>
    private async Task<BackendReachability> ReachabilityAsync(CancellationToken cancellationToken)
    {
        BackendReachability reachability;

        try
        {
            reachability = await _backend.CheckReachableAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The caller asked to stop before anything was walked, which is not a service that is
            // down and must not be recorded as one.
            throw;
        }
        catch (Exception reason)
        {
            _logger.LogWarning(
                reason,
                "The external request service could not be asked whether it is there, so nothing was reconciled this run. Every request handed to it is left exactly as it is and the next run will ask again.");

            return BackendReachability.Unreachable;
        }

        _watch.Saw(reachability, _clock.UtcNow);

        switch (reachability)
        {
            case BackendReachability.NotConfigured:
                // Most servers. Not a failure and not worth a line above debug, because saying it
                // once a run would fill an operator's log with the fact that they run one plugin
                // rather than two systems.
                _logger.LogDebug(
                    "There is no external request service on this install, so there is nothing to reconciliate against and this run did nothing.");
                break;

            case BackendReachability.Unreachable:
                // Said rather than swallowed, which is this issue's fourth condition. One line for
                // the run and not one per request: the fact is about the service.
                _logger.LogWarning(
                    "The external request service did not answer, so nothing was reconciled this run. Every request handed to it is left exactly as it is and the next run will ask again.");
                break;

            default:
                break;
        }

        return reachability;
    }

    /// <summary>
    /// Asks the service about one reference, turning a failure into no answer.
    /// </summary>
    /// <param name="requestId">Which request, for the log to name.</param>
    /// <param name="reference">What the service called it.</param>
    /// <param name="cancellationToken">Cancels the question.</param>
    /// <returns>What the service said, or <see langword="null"/> where it said nothing or could not be asked.</returns>
    private async Task<BackendReport?> ReportedAsync(
        Guid requestId,
        BackendReference reference,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _backend.ReportAsync(reference, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception reason)
        {
            // One request rather than the run. A service that cannot answer about one reference is a
            // fact about that reference, and stopping here would let one unknown title hold up every
            // other request on the server.
            _logger.LogWarning(
                reason,
                "The external request service could not be asked about request {RequestId}, so it was left exactly as it is and the rest of this run carried on.",
                requestId);

            return null;
        }
    }

    /// <summary>
    /// What the service's word means here, or nothing.
    /// </summary>
    /// <param name="requestId">Which request, for the log to name.</param>
    /// <param name="report">What the service said.</param>
    /// <returns>The state to move into, or <see langword="null"/> where nothing moves.</returns>
    private RequestState? Destination(Guid requestId, BackendReport report)
    {
        // Both lists, because the interface hands back one word and does not say which of the
        // service's two vocabularies it came from. No word appears in both, which the suite refuses
        // rather than this method assuming, so asking each in turn cannot produce two answers.
        var row = BackendStates.Lookup(BackendVocabulary.RequestStatus, report)
            ?? BackendStates.Lookup(BackendVocabulary.MediaStatus, report);

        if (row is null)
        {
            // A logged refusal rather than a corrupt request. Nothing here guesses at a word the
            // table has never seen, because a guess is a request put into a state nobody chose and
            // the whole cost of being wrong lands on somebody who cannot see why.
            _logger.LogWarning(
                "The external request service said {Reported} about request {RequestId} and nothing here knows that word, so the request was left exactly as it is. A word this plugin should act on is a row in the mapping table rather than a branch here.",
                report.Reported,
                requestId);

            return null;
        }

        return row.MoveTo;
    }

    /// <summary>
    /// Moves one request, through the table and into the history, and tells the two paths that are
    /// told about every other move.
    /// </summary>
    /// <param name="stored">The request as the store holds it.</param>
    /// <param name="to">Where the table says it goes.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> where it moved.</returns>
    private async Task<bool> MovedAsync(StoredRequest stored, RequestState to, CancellationToken cancellationToken)
    {
        MediaRequest moved;

        try
        {
            // Through the table and never around it, with the plugin as the actor: nothing this run
            // moves was decided by a person, and the entry the history keeps says so by carrying no
            // identifier. A move the table refuses is reported here rather than forced, which is the
            // second half of not corrupting a request on a surprising answer.
            moved = RequestLifecycle.Move(stored.Request, to, _clock.UtcNow, RequestCaller.Plugin);
        }
        catch (IllegalRequestTransitionException reason)
        {
            _logger.LogWarning(
                "The external request service put request {RequestId} at {To} and the transition table does not allow that move from {From}, so the request was left exactly as it is. {Why}",
                stored.Request.Id,
                to,
                stored.Request.State,
                reason.Message);

            return false;
        }
        catch (RequestMoveNotPermittedException reason)
        {
            _logger.LogWarning(
                "The transition table allows request {RequestId} to move to {To} and does not admit this plugin for it, so the request was left exactly as it is. {Why}",
                stored.Request.Id,
                to,
                reason.Message);

            return false;
        }

        StoredRequest written;

        try
        {
            written = await _store.ReplaceAsync(moved, stored.Revision, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestConcurrencyException)
        {
            // Somebody decided on this request between the read and the write, and their decision is
            // the newer one. It is dropped rather than retried, for the reason the retention sweep
            // drops one: a retry here would apply the service's word over an operator's answer given
            // a moment ago, which is exactly what this run may never do.
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Request {RequestId} moved while the external request service was being asked about it, so what the service said was dropped and the next run will ask again.",
                    stored.Request.Id);
            }

            return false;
        }

        // After the write and never before it, for the reason the endpoint writes its entry after
        // one: an entry describing a move the store then refused is a line in the operator's list
        // for something that did not happen.
        if (ActivityNote.For(stored.Request, written.Request) is ActivityNote note)
        {
            await _journal.WriteAsync(note, cancellationToken).ConfigureAwait(false);

            // Under the same condition as the entry. Nothing is told for a move to failed today,
            // because no sentence is written for that state; RequesterMessage.ForMove is where that
            // is decided and this run does not decide it a second time.
            if (RequesterMessage.ForMove(written.Request, StringCatalogue.Shipped) is RequesterMessage message)
            {
                _told.Tell(message);
            }
        }

        return true;
    }
}

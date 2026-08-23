using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Bridge;

/// <summary>
/// What an approval does about an external request service, and what it keeps of the answer.
/// <para>
/// Approving is the moment something else is asked to fetch the title. What comes back is the
/// service's own name for the thing it accepted, and losing it means the two systems can never be
/// reconciled again: this side would hold a request it believes was handed over and no way to ask
/// anybody about it. So the reference is written back onto the request, and the write is the whole
/// reason this exists.
/// </para>
/// <para>
/// <b>An approval is never undone by a submission that failed.</b> The operator decided, the store
/// already holds the decision, and a plugin that rolled it back would be overruling a person because
/// a service on another machine was down. What a failure produces here is a log line and an approved
/// request with no reference, which is a state that can be submitted again once the service is back.
/// Making that failure visible to an operator without reading a log is #283.
/// </para>
/// <para>
/// <b>Submitting the same request twice is refused, and it is refused by looking at the request.</b>
/// <see cref="MediaRequest.Backend"/> is written only here and only after a service answered, so a
/// request carrying one has already been handed over and is not handed over again. That is the
/// answer this issue's third condition asks to be stated: refused rather than harmless, because a
/// second submission of an accepted request is a second copy of the same download on the service's
/// side, and nothing on this side could tell the two apart afterwards.
/// </para>
/// <para>
/// On a server with no external service this does nothing at all and says nothing about it. The
/// shipping bridge hands back no reference, which is not a failure, so a decision on such a server
/// leaves no log line and no field, and the caller has no branch for the difference.
/// </para>
/// </summary>
public sealed class BridgeSubmission
{
    private readonly IRequestBackend _backend;
    private readonly IRequestStore _store;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BridgeSubmission"/> class.
    /// </summary>
    /// <param name="backend">Whatever else on this server fetches media.</param>
    /// <param name="store">Where requests are kept, for the reference to be written back into.</param>
    /// <param name="logger">The server's log, which is where a failed submission is reported.</param>
    public BridgeSubmission(IRequestBackend backend, IRequestStore store, ILogger logger)
    {
        _backend = backend;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Hands a request that has just been approved to the service, and keeps what it called it.
    /// <para>
    /// Called with the request as the store wrote it, and answers with the request as the store
    /// holds it afterwards. Where nothing was handed over, where the service refused, or where the
    /// reference could not be written back, that is the same value it was given: the approval stands
    /// in every one of those cases and the caller has one thing to return either way.
    /// </para>
    /// <para>
    /// Nothing here throws. It runs after a decision has already been written, so an exception
    /// escaping it would turn a decision that was taken into a call that reports failure, and the
    /// operator would press the button again against a queue that had already moved.
    /// </para>
    /// </summary>
    /// <param name="written">The request as the store wrote it, at its new revision.</param>
    /// <param name="cancellationToken">Cancels the submission.</param>
    /// <returns>The request at the revision the store holds it at now.</returns>
    public async Task<StoredRequest> SubmitAsync(StoredRequest written, CancellationToken cancellationToken)
    {
        if (written.Request.State != RequestState.Approved || written.Request.Backend is not null)
        {
            // Not an approval, or one that has already been handed over. Both are ordinary and
            // neither is worth a line in an operator's log.
            return written;
        }

        BackendReference? reference;

        try
        {
            reference = await _backend.SubmitAsync(written.Request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception reason)
        {
            // Every failure of the service, including a cancelled call. Cancellation is not rethrown
            // because the decision is already in the store: the caller asked to stop, and what it
            // would stop is a submission rather than the approval, so the honest answer is the
            // approved request with no reference on it.
            //
            // Which failures an adapter distinguishes, and what each of them then does, is #86. What
            // is decided here is only that none of them loses the approval.
            _logger.LogError(
                reason,
                "Request {RequestId} was approved and could not be handed to the external request service. The approval stands and nothing was undone; it carries no reference, so it has not been fetched and can be submitted again.",
                written.Request.Id);

            return written;
        }

        if (reference is not BackendReference kept)
        {
            // Nothing was handed over. This is what a server with no external service answers on
            // every approval, and it is not a failure to report.
            return written;
        }

        try
        {
            return await _store.ReplaceAsync(
                written.Request with { Backend = kept },
                written.Revision,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception reason)
        {
            // The worst of the three and the reason it is reported loudest: the service has the
            // request and this side did not keep what it called it. Nothing here retries, because a
            // second submission against a service that already accepted one is the duplicate the
            // rule above exists against, and the operator is told the identifier so the two can be
            // reconciled by hand.
            _logger.LogError(
                reason,
                "Request {RequestId} was handed to {Service}, which called it {Reference}, and that reference could not be written back. The service holds it and this queue does not know so, which is the one case here that needs somebody to look.",
                written.Request.Id,
                kept.Service,
                kept.Id);

            return written;
        }
    }
}

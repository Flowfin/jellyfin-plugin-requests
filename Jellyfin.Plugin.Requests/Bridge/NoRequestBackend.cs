using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Bridge;

/// <summary>
/// The bridge on a server that has no external request service, which is most of them.
/// <para>
/// This is the shipping default and the implementation the majority of installs run, so it is
/// written and tested as a real one rather than as a placeholder. A caller cannot tell from its own
/// code whether a service is configured: it submits, it gets nothing back, and nothing anywhere
/// above has a branch for the absence.
/// </para>
/// <para>
/// Nothing here fails. There is no service to be unreachable, no credential to be refused, no
/// version to be unknown and no title to be unknown, so the four failures #86 decided belong to an
/// adapter and not to this. What it does
/// not do is pretend: it reports that nothing is configured rather than that something answered,
/// and it hands back no reference rather than one it invented, because a made-up reference is a row
/// an operator would later try to reconcile against a service that never saw it.
/// </para>
/// </summary>
public sealed class NoRequestBackend : IRequestBackend
{
    /// <inheritdoc />
    public Task<BackendReachability> CheckReachableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(BackendReachability.NotConfigured);
    }

    /// <inheritdoc />
    public Task<BackendReference?> SubmitAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The request is not read and nothing is kept. An approval on such a server is a decision an
        // operator took and a queue row they will fulfil themselves, and the plugin has nowhere to
        // send it.
        return Task.FromResult<BackendReference?>(null);
    }

    /// <inheritdoc />
    public Task<BackendReport?> ReportAsync(BackendReference reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Nothing was ever handed over, so nothing is known about any reference. Answered rather
        // than refused, because a caller asking about a reference from a service that has since
        // been unconfigured is the ordinary case.
        return Task.FromResult<BackendReport?>(null);
    }

    /// <inheritdoc />
    public Task WithdrawAsync(BackendReference reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Nothing to tell. Withdrawing something nothing accepted leaves the same state it was in,
        // which is what the caller wanted, so this is not an error to report upward.
        return Task.CompletedTask;
    }
}

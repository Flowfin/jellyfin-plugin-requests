using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// An external request service that throws when it is handed something.
/// <para>
/// It stands for every way a submission can fail from this side of the interface: the service is
/// down, the credential is refused, the answer cannot be read. Which of those an adapter tells apart
/// and what each one then does is a separate question; what this is for is the one rule that holds
/// across all of them, that an approval already written is not taken back.
/// </para>
/// <para>
/// The exception it throws carries no vocabulary of its own on purpose. A double raising a named
/// bridge failure would be a test of a distinction nothing in this tree makes yet.
/// </para>
/// </summary>
internal sealed class AServiceThatWillNotTakeAnything : IRequestBackend
{
    /// <summary>
    /// What the failure says, so a test can find it again in the log.
    /// </summary>
    public const string Complaint = "This service is not taking anything today.";

    /// <inheritdoc />
    public Task<BackendReachability> CheckReachableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(BackendReachability.Reachable);
    }

    /// <inheritdoc />
    public Task<BackendReference?> SubmitAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        throw new InvalidOperationException(Complaint);
    }

    /// <inheritdoc />
    public Task<BackendReport?> ReportAsync(BackendReference reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<BackendReport?>(null);
    }

    /// <inheritdoc />
    public Task WithdrawAsync(BackendReference reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }
}

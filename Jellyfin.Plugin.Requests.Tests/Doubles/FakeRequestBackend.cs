using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// A bridge that answers what the test says it answers.
/// <para>
/// The shipping default reports that nothing is configured, which is the honest answer on a server
/// with no external service and useless for testing what happens when there is one. This is how a
/// test says "there is a service here" without an adapter, an address or anything on the network.
/// </para>
/// </summary>
internal sealed class FakeRequestBackend : IRequestBackend
{
    private readonly BackendReachability _reachability;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeRequestBackend"/> class.
    /// </summary>
    /// <param name="reachability">What it says when it is asked whether it is there.</param>
    public FakeRequestBackend(BackendReachability reachability) => _reachability = reachability;

    /// <inheritdoc />
    public Task<BackendReachability> CheckReachableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_reachability);
    }

    /// <inheritdoc />
    public Task<BackendReference?> SubmitAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<BackendReference?>(null);
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

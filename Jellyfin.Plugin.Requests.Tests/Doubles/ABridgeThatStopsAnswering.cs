using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// A bridge whose reachability moves between calls, for the one thing no install can produce today.
/// <para>
/// The only implementation this plugin ships is the one for a server with no external service, so
/// <see cref="BackendReachability.Unreachable"/> is a value nothing an operator can install answers
/// with. A health panel that shows a failure has to be provable before an adapter exists, and this
/// is what provides the failure: it says what it is told to say, and the test decides when it stops
/// answering.
/// </para>
/// <para>
/// It is not <see cref="FakeRequestBackend"/> widened. That one takes its answer once and never
/// changes it, which is what every test of a fixed state wants, and making it mutable would give
/// every one of those tests a state that can move underneath it.
/// </para>
/// </summary>
internal sealed class ABridgeThatStopsAnswering : IRequestBackend
{
    private BackendReachability _reachability;

    /// <summary>
    /// Initializes a new instance of the <see cref="ABridgeThatStopsAnswering"/> class.
    /// </summary>
    /// <param name="reachability">What it answers to begin with.</param>
    public ABridgeThatStopsAnswering(BackendReachability reachability) => _reachability = reachability;

    /// <summary>
    /// Changes what it answers from the next call on.
    /// </summary>
    /// <param name="reachability">What it answers now.</param>
    public void Answering(BackendReachability reachability) => _reachability = reachability;

    /// <inheritdoc />
    public Task<BackendReachability> CheckReachableAsync(CancellationToken cancellationToken)
        => Task.FromResult(_reachability);

    /// <inheritdoc />
    /// <remarks>Not what this double is about. Nothing is submitted through it.</remarks>
    public Task<BackendReference?> SubmitAsync(MediaRequest request, CancellationToken cancellationToken)
        => Task.FromResult<BackendReference?>(null);

    /// <inheritdoc />
    /// <remarks>Not what this double is about. Nothing is reconciled through it.</remarks>
    public Task<BackendReport?> ReportAsync(BackendReference reference, CancellationToken cancellationToken)
        => Task.FromResult<BackendReport?>(null);

    /// <inheritdoc />
    /// <remarks>Not what this double is about. Nothing is withdrawn through it.</remarks>
    public Task WithdrawAsync(BackendReference reference, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

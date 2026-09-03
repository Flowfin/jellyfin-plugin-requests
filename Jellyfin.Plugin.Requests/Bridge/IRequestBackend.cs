using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Bridge;

/// <summary>
/// Whatever else on this server fetches media, seen from here.
/// <para>
/// Most servers run nothing of the sort, and on those this plugin is the whole system.
/// <see cref="NoRequestBackend"/> is what they get, and it is the shipping default rather than a
/// stub waiting to be replaced. Having one interface with that default behind it is what keeps a
/// second code path out of every caller: nothing above this asks whether a bridge exists before
/// deciding what to do.
/// </para>
/// <para>
/// Four operations and no more. Ask whether there is a service and whether it answered, hand a
/// request over and keep what comes back, ask about something already handed over, and take one
/// back. Everything else about a request stays on this side, because a plugin that also held
/// quality profiles, root folders and download clients would be a worse copy of the thing it is
/// talking to.
/// </para>
/// <para>
/// <b>What this interface does not decide.</b> What an approval means when a submission fails is
/// #82. What happens when the service is unreachable, refuses the credential, reports a version the
/// adapter does not know, or is asked about a title it has never heard of was decided on #86 and is
/// carried by <see cref="BackendReachability"/> and by <c>docs/bridge.md</c>. How a Jellyfin user
/// is named to the service is #84, and where the credential lives is #85. This is the shape those
/// answers have to fit through, and it names no service, no protocol and no address on purpose.
/// </para>
/// <para>
/// <b>Cancellation.</b> A cancelled call throws <see cref="System.OperationCanceledException"/>,
/// including on the null implementation. A default that quietly ignored a token would make a caller
/// look correct here and hang on the first adapter that honours one.
/// </para>
/// </summary>
public interface IRequestBackend
{
    /// <summary>
    /// Asks whether there is a service behind this plugin and whether it answered.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>
    /// Which state the bridge is in. Nothing configured is an answer rather than a failure, and a
    /// failure is a value here rather than an exception, so a scheduled task asking this cannot die
    /// on the answer.
    /// </returns>
    Task<BackendReachability> CheckReachableAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Hands one request to the service and keeps whatever it calls it.
    /// </summary>
    /// <param name="request">The request an operator approved.</param>
    /// <param name="cancellationToken">Cancels the submission.</param>
    /// <returns>
    /// What the service called it, or <see langword="null"/> where nothing was handed over and
    /// there is therefore nothing to keep. A null answer is the ordinary one on a server with no
    /// service configured, and it is not a failure: a failure is an exception, which the caller
    /// marks on the request and never retries, decided on #86.
    /// </returns>
    Task<BackendReference?> SubmitAsync(MediaRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the service where something already handed to it stands.
    /// </summary>
    /// <param name="reference">What the service called it when it was handed over.</param>
    /// <param name="cancellationToken">Cancels the question.</param>
    /// <returns>
    /// The service's own word for where it stands, or <see langword="null"/> where nothing is
    /// known about that reference. Nothing known is an ordinary answer: a reference issued by a
    /// service an operator has since replaced is a fact about the install rather than a defect.
    /// </returns>
    Task<BackendReport?> ReportAsync(BackendReference reference, CancellationToken cancellationToken);

    /// <summary>
    /// Takes something back that was handed over.
    /// </summary>
    /// <param name="reference">What the service called it when it was handed over.</param>
    /// <param name="cancellationToken">Cancels the withdrawal.</param>
    /// <returns>A task that completes when the service has been told.</returns>
    Task WithdrawAsync(BackendReference reference, CancellationToken cancellationToken);
}

using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Requests.Seam;

/// <summary>
/// The one call the sibling discover plugin makes into this one.
/// <para>
/// <b>One way, one moment, one answer.</b> A user expressed a want on a browsing surface this plugin
/// does not own, and the other side says what was wanted and who wanted it. Nothing is learned back
/// except whether the handover was accepted, which is the whole of what the contract allows this
/// side to say, and there is no call in the other direction: <c>docs/seam.md</c> argues why a read
/// back is refused rather than missing.
/// </para>
/// <para>
/// <b>Whether the other plugin can reach this at all is not settled here.</b> Naming a type means
/// having the type, and whether a second plugin in one server process can name this one is the
/// assembly-loading question in #117. This interface is what it would name; that it is registered
/// into the server's container is not a measurement that anything can resolve it.
/// </para>
/// </summary>
public interface IWantHandover
{
    /// <summary>
    /// Takes one want and makes this plugin's answer to it.
    /// </summary>
    /// <param name="want">The field set the contract fixes.</param>
    /// <param name="cancellationToken">Cancels the handover.</param>
    /// <returns>
    /// <see langword="true"/> where a request for this want now exists, whether it was made by this
    /// call or was already there with somebody else waiting on it. <see langword="false"/> where the
    /// handover was refused, and the reason for that is on this side's log rather than in this
    /// answer, because the contract carries no field for one.
    /// </returns>
    Task<bool> AcceptAsync(HandedOverWant want, CancellationToken cancellationToken);
}

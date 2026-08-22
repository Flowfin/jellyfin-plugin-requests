using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Requests.Notify;

/// <summary>
/// The path that tells whoever administers this server, on the connections the server already holds
/// to whatever they are signed in on right now, that somebody has asked for something.
/// <para>
/// <b>It is a courtesy and never a delivery.</b> Nobody is signed in most of the time, so most
/// arrivals reach nobody through here and nothing remembers that they did not. What an operator can
/// rely on is the queue, which holds every open request whenever they next open it. Nothing about a
/// request depends on a message going out here, and a caller that has just taken one in tells the
/// administrators and carries on.
/// </para>
/// <para>
/// <b>Nothing on either claimed server line listens for this today.</b> It is a contract for a
/// client somebody else writes, the same way <see cref="IOutboundSink"/> is a contract for a service
/// an operator already runs, and <c>docs/notifications.md</c> says so in plain words rather than
/// describing the mechanism in a way that implies a dashboard reacts to it. That is why an install
/// says nothing here until an operator turns it on.
/// </para>
/// <para>
/// <b>Telling returns nothing, for the reason <see cref="IRequesterNotice"/> gives.</b> A method
/// handing back a task is a method somebody awaits, and an arrival that waits on a message is an ask
/// that can fail because an administrator's socket is slow.
/// </para>
/// </summary>
public interface IArrivalNotice
{
    /// <summary>
    /// Pushes the document at the administrators' sessions, eventually, or does not. Returns as soon
    /// as the delivery is under way and never raises anything the caller has to handle.
    /// </summary>
    /// <param name="notice">The document, which is the same one the outbound sink posts.</param>
    void Tell(OutboundNotice notice);

    /// <summary>
    /// Waits until nothing told so far is still in flight.
    /// <para>
    /// This exists for two callers and neither is on the path a request takes, which is the same
    /// division <see cref="IRequesterNotice.QuietAsync"/> is written under: the suite uses it to
    /// assert what was pushed without waiting on a clock, and a shutdown can use it to let a message
    /// in flight finish.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Gives up waiting.</param>
    /// <returns>A task that completes when nothing is in flight.</returns>
    Task QuietAsync(CancellationToken cancellationToken);
}

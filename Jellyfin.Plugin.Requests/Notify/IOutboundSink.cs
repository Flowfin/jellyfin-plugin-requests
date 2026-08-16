using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Requests.Notify;

/// <summary>
/// The one place anything this plugin has to say leaves the server, and the shape that keeps it from
/// mattering whether it arrives.
/// <para>
/// <b>Announcing returns nothing, and that is the whole design.</b> A method handing back a task is
/// a method somebody awaits, and the day somebody awaits this one inside a transition is the day an
/// approval fails because a chat service is having a bad day. There is no task to await here, so the
/// mistake is not available: a caller that has just moved a request announces it and carries on, and
/// what the endpoint does about it is between the sink and the endpoint.
/// </para>
/// <para>
/// <b>The cost of that is admitted rather than hidden.</b> A notice can be lost, and nothing retries
/// it or records that it went missing. That is the right trade for a courtesy message and the wrong
/// one for a record, which is why the record is the server's activity log and not this.
/// </para>
/// </summary>
public interface IOutboundSink
{
    /// <summary>
    /// Gets a value indicating whether anything is sent at all.
    /// <para>
    /// It is false on every install where an operator has not typed an address, which is every fresh
    /// one. Nothing above this asks it before announcing: a caller that had to would be a caller
    /// that can forget to, and announcing into a sink that is off is already nothing happening.
    /// </para>
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends the notice, eventually, or does not. Returns as soon as the delivery is under way and
    /// never raises anything the caller has to handle.
    /// </summary>
    /// <param name="notice">What to say.</param>
    void Announce(OutboundNotice notice);

    /// <summary>
    /// Waits until nothing announced so far is still in flight.
    /// <para>
    /// This exists for two callers and neither is on the path a request takes. The suite uses it to
    /// assert what an endpoint received without waiting on a clock, and a shutdown can use it to let
    /// a message in flight finish. Anything on a transition path that calls this has re-created the
    /// blocking the interface above exists to prevent.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Gives up waiting.</param>
    /// <returns>A task that completes when the sink is idle.</returns>
    Task QuietAsync(CancellationToken cancellationToken);
}

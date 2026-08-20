using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Requests.Notify;

/// <summary>
/// The path that tells the person who asked, on the connection the server already holds to whatever
/// they are signed in on right now.
/// <para>
/// <b>It reaches whoever is connected and nobody else, and that is the whole of what it promises.</b>
/// A person who is not signed in at the moment their request moves is told nothing here and nothing
/// remembers that they were not. The durable answer is their own page, which shows the state
/// whenever they next look, and <c>docs/notifications.md</c> names it rather than leaving this
/// looking like a delivery.
/// </para>
/// <para>
/// <b>Telling returns nothing, for the reason <see cref="IOutboundSink"/> gives.</b> A method handing
/// back a task is a method somebody awaits, and a transition that waits on a message is a decision
/// that can fail because a client's socket is slow. There is no task to await here, so the mistake
/// is not available: a caller that has just moved a request tells the person and carries on.
/// </para>
/// </summary>
public interface IRequesterNotice
{
    /// <summary>
    /// Pushes the message to that one person's sessions, eventually, or does not. Returns as soon as
    /// the delivery is under way and never raises anything the caller has to handle.
    /// </summary>
    /// <param name="message">What to say, and who to say it to.</param>
    void Tell(RequesterMessage message);

    /// <summary>
    /// Waits until nothing told so far is still in flight.
    /// <para>
    /// This exists for two callers and neither is on the path a request takes. The suite uses it to
    /// assert what was pushed without waiting on a clock, and a shutdown can use it to let a message
    /// in flight finish. Anything on a transition path that calls this has re-created the blocking
    /// the method above exists to prevent.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Gives up waiting.</param>
    /// <returns>A task that completes when nothing is in flight.</returns>
    Task QuietAsync(CancellationToken cancellationToken);
}

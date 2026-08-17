using System;

namespace Jellyfin.Plugin.Requests.Bridge;

/// <summary>
/// When this server last saw the external request service answer.
/// <para>
/// A bridge that has stopped answering is a fault an operator has to be able to read, and
/// "unreachable" on its own is not enough to act on: a service that went quiet an hour ago and one
/// that went quiet in the last minute are different problems. What separates them is the last moment
/// anything here had evidence, which is what this holds.
/// </para>
/// <para>
/// <b>It advances when something asks, and nothing here asks on its own.</b> There is no timer
/// behind this: it records what a caller already had to find out. So on an install where nobody
/// opens the health panel it stays where it was, and what it says is when this plugin last had
/// evidence rather than when the other system last worked. That distinction is stated wherever it is
/// drawn, because a stale moment read as the start of an outage is worse than no moment.
/// </para>
/// <para>
/// <b>It is not persisted.</b> A restart forgets, and the alternative is this plugin writing a file
/// to record when somebody else's system answered. What draws it says "since this server started"
/// for that reason.
/// </para>
/// </summary>
public sealed class BridgeWatch
{
    private readonly object _gate = new object();

    private DateTimeOffset? _lastReachableAt;

    /// <summary>
    /// Gets when the bridge was last seen answering, or <see langword="null"/> where nothing has
    /// seen it answer since this server started.
    /// </summary>
    public DateTimeOffset? LastReachableAt
    {
        get
        {
            lock (_gate)
            {
                return _lastReachableAt;
            }
        }
    }

    /// <summary>
    /// Records what a caller found when it asked.
    /// <para>
    /// Only the answering case moves anything. An unreachable answer is the state the reader already
    /// has in front of them, and a not-configured one says nothing about a service at all, so
    /// neither of them is evidence about a moment.
    /// </para>
    /// </summary>
    /// <param name="reachability">What the bridge answered.</param>
    /// <param name="at">When it answered.</param>
    public void Saw(BackendReachability reachability, DateTimeOffset at)
    {
        if (reachability != BackendReachability.Reachable)
        {
            return;
        }

        lock (_gate)
        {
            // Never backwards. Two callers asking at once can arrive here out of order, and a
            // moment that went back would read as the service having stopped answering earlier than
            // it did.
            if (_lastReachableAt is not DateTimeOffset held || at > held)
            {
                _lastReachableAt = at;
            }
        }
    }
}

using System;

namespace Jellyfin.Plugin.Requests.Time;

/// <summary>
/// What the plugin asks for the time. Everything that stamps a request, decides whether a retention
/// window has passed, or compares two moments goes through this and never through the machine's
/// clock directly.
/// <para>
/// The reason is what a test looks like otherwise. A retention rule that reads the wall clock can
/// only be tested by waiting, so the test either sleeps, which makes it slow and then flaky, or it
/// is written around the problem and stops being about the rule. With the clock injected a test
/// says what time it is and the code under test believes it.
/// </para>
/// <para>
/// It is an offset rather than a bare time, and it is UTC, for the same reason
/// <see cref="Model.MediaRequest.RequestedAt"/> is: a server that moves timezone must not reorder
/// its own queue.
/// </para>
/// </summary>
public interface IClock
{
    /// <summary>
    /// Gets the current moment in UTC.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}

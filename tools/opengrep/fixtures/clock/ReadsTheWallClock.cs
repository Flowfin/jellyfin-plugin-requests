// Fixture for clock-read-only-through-the-injected-clock. This file is in no
// project and is never compiled; it exists so the rule can be watched refusing
// the mistake it names.
//
// The near-miss is a retention sweep written by somebody who had no IClock to
// hand and needed the time on one line. It works, it ships, and the only way to
// test the window it implements is to wait for it.

namespace Jellyfin.Plugin.Requests.Fixtures;

internal sealed class ReadsTheWallClock
{
    // Legal neighbour, left here on purpose: this is how the plugin asks for the
    // time and the rule has to stay quiet on it.
    public static bool IsExpired(IClock clock, DateTimeOffset requestedAt) =>
        clock.UtcNow - requestedAt > TimeSpan.FromDays(90);

    // The regression, in each spelling the tree could pick up.
    public static bool IsExpiredByTheMachineClock(DateTimeOffset requestedAt)
    {
        var utc = DateTime.UtcNow;
        var local = DateTime.Now;
        var day = DateTime.Today;
        var offsetUtc = DateTimeOffset.UtcNow;
        var offsetLocal = DateTimeOffset.Now;

        return offsetUtc - requestedAt > TimeSpan.FromDays(90);
    }
}

// Fixture for no-waiting-in-a-test. This file is in no project and is never
// compiled; it exists so the rule can be watched refusing the mistake it names.
//
// The near-miss is the first thing somebody reaches for when a timing test goes
// red: give it a moment. The test passes on the machine it was written on and
// fails on a loaded runner, and the failure is then read as flakiness rather
// than as the test never having tested the rule.

namespace Jellyfin.Plugin.Requests.Tests.Fixtures;

internal sealed class WaitsInATest
{
    // Legal neighbour, left here on purpose: this is how a test moves time and
    // the rule has to stay quiet on it.
    public static void MovesTheClock(TestClock clock)
    {
        clock.Advance(TimeSpan.FromDays(30));
    }

    // The regression, in both spellings.
    public static async Task WaitsForIt()
    {
        Thread.Sleep(250);
        await Task.Delay(250).ConfigureAwait(false);
    }
}

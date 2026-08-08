using System;
using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Jellyfin.Plugin.Requests.Time;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Time;

/// <summary>
/// The two clocks: the one a server gets and the one the suite gets. What is being checked is that
/// the first reads the machine and the second reads only what a test told it, because those two
/// properties together are what let every later test about ordering, retention and staleness be
/// written without waiting for anything.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public class ClockTests
{
    /// <summary>
    /// The real clock reads the machine's clock. It is bracketed rather than compared to a single
    /// reading, because two reads of a moving clock are never equal and a test that demanded they
    /// were would fail for being right.
    /// </summary>
    [Fact]
    public void SystemClockReadsTheMachineClock()
    {
        var before = DateTimeOffset.UtcNow;
        var read = new SystemClock().UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(read, before, after);
        Assert.Equal(TimeSpan.Zero, read.Offset);
    }

    /// <summary>
    /// The test clock does not move on its own. This is the property the whole apparatus rests on:
    /// if it drifted, a test asserting that two things happened at the same moment would pass or
    /// fail on how busy the machine was.
    /// </summary>
    [Fact]
    public void TestClockStandsStillUntilItIsMoved()
    {
        var start = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new TestClock(start);

        Assert.Equal(start, clock.UtcNow);
        Assert.Equal(clock.UtcNow, clock.UtcNow);
    }

    /// <summary>
    /// Time is advanced by saying so, and the test takes no longer for it. Thirty days pass here in
    /// the time it takes to add them, which is what a retention window or a staleness rule will be
    /// tested against rather than with a sleep.
    /// </summary>
    [Fact]
    public void TestClockAdvancesWithoutWaiting()
    {
        var start = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new TestClock(start);
        var startedAt = DateTimeOffset.UtcNow;

        clock.Advance(TimeSpan.FromDays(30));

        Assert.Equal(start.AddDays(30), clock.UtcNow);

        // The real elapsed time of the test is nothing like the time the clock moved. A generous
        // bound rather than a tight one: this is here to catch an implementation that waited, not
        // to measure the machine.
        Assert.True(DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Advancing by a negative span is refused. A clock going backwards is a real thing to test
    /// against one day, and it has to be asked for rather than arrived at by a sign error in a
    /// test that meant to move forward.
    /// </summary>
    [Fact]
    public void TestClockRefusesToGoBackwards()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromSeconds(-1)));
    }
}

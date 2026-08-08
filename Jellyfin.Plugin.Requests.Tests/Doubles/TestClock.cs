using System;
using Jellyfin.Plugin.Requests.Time;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// A clock the suite sets and moves. It is the reason no test here waits: a rule that depends on
/// thirty days having passed is tested by moving this thirty days, which takes no time at all and
/// gives the same answer on a slow machine as on a fast one.
/// <para>
/// It never moves on its own. Two reads with no <see cref="Advance"/> between them return the same
/// moment, so a test that expected two events to be simultaneous gets that, and a test that wanted
/// them apart has to say by how much.
/// </para>
/// </summary>
internal sealed class TestClock : IClock
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TestClock"/> class.
    /// </summary>
    /// <param name="start">The moment the clock starts at.</param>
    public TestClock(DateTimeOffset start)
    {
        UtcNow = start;
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; private set; }

    /// <summary>
    /// Moves the clock forward.
    /// </summary>
    /// <param name="by">How far forward. Must not be negative: a test that needs a clock going
    /// backwards is testing something this double does not stand for, and letting it happen by
    /// accident would hide it.</param>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);

        UtcNow = UtcNow.Add(by);
    }
}

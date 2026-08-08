using System;

namespace Jellyfin.Plugin.Requests.Time;

/// <summary>
/// The clock a running server gets: the machine's own, read in UTC.
/// <para>
/// This is the one file in the plugin allowed to read the system clock, and the invariant lint
/// holds it to that. Everything else takes an <see cref="IClock"/>, so there is one place where the
/// wall clock enters and one place a test replaces.
/// </para>
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

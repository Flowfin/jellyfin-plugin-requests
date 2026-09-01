using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// The log, as a list a test can read. It stands in for the server's logger wherever the suite
/// constructs something that writes to one.
/// <para>
/// It keeps the formatted message rather than the template and the arguments, because what a rule
/// about the log is usually about is what an operator ends up reading. Every level is enabled, so a
/// line dropped here is a line the code did not write rather than one this double declined to take.
/// </para>
/// <para>
/// <b>Several threads may write to it at once.</b> Every path in <c>Notify/</c> that starts a task
/// per message writes its failure line from whichever task raises it - the outbound sink, the
/// switch in front of the requester path and the two server notices - so two messages handed over
/// without a wait between them reach this from two threads with nothing above them serialising it.
/// An unguarded <see cref="List{T}"/> under that traffic does not merely reorder: an append racing
/// another loses an entry, keeps one twice or leaves a hole, and each of those reads in a report as
/// a defect in whatever the test was actually about. The whole of what the lock buys is that every
/// line given to this double is kept exactly once.
/// </para>
/// <para>
/// <b>It promises no order across threads, because nothing above it does.</b> Where the lines were
/// written one after another by one thread, the order kept here is that order, which is what every
/// leg reading a single path's log reads.
/// </para>
/// </summary>
internal sealed class RecordingLogger : ILogger
{
    private readonly object _gate = new object();
    private readonly List<Line> _lines = [];

    /// <summary>
    /// Gets every line written to this logger, in the order it was written, as a copy taken under
    /// the lock. A caller reading this while something is still in flight gets a whole answer of
    /// some moment rather than a list changing under its own enumeration.
    /// </summary>
    public IReadOnlyList<Line> Lines
    {
        get
        {
            lock (_gate)
            {
                return _lines.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        lock (_gate)
        {
            _lines.Add(new Line(logLevel, formatter(state, exception), exception));
        }
    }

    /// <summary>
    /// Every line at the given level. Filtered under the lock, so it walks a list nothing is
    /// appending to rather than one a delivery still in flight is.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <returns>The lines.</returns>
    public IReadOnlyList<Line> At(LogLevel level)
    {
        lock (_gate)
        {
            return [.. _lines.Where(line => line.Level == level)];
        }
    }

    /// <summary>
    /// One line as it was written.
    /// </summary>
    /// <param name="Level">How severe the writer said it was.</param>
    /// <param name="Message">The message, formatted as a reader of the log would see it.</param>
    /// <param name="Exception">What was being reported, where there was one.</param>
    internal sealed record Line(LogLevel Level, string Message, Exception? Exception);
}

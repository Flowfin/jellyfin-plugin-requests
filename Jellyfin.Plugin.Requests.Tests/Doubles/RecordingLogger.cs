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
/// </summary>
internal sealed class RecordingLogger : ILogger
{
    private readonly List<Line> _lines = [];

    /// <summary>
    /// Gets every line written to this logger, in the order it was written.
    /// </summary>
    public IReadOnlyList<Line> Lines => _lines;

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

        _lines.Add(new Line(logLevel, formatter(state, exception), exception));
    }

    /// <summary>
    /// Every line at the given level.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <returns>The lines.</returns>
    public IReadOnlyList<Line> At(LogLevel level)
        => [.. _lines.Where(line => line.Level == level)];

    /// <summary>
    /// One line as it was written.
    /// </summary>
    /// <param name="Level">How severe the writer said it was.</param>
    /// <param name="Message">The message, formatted as a reader of the log would see it.</param>
    /// <param name="Exception">What was being reported, where there was one.</param>
    internal sealed record Line(LogLevel Level, string Message, Exception? Exception);
}

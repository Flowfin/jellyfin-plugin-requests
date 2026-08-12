using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Requests.Configuration;

/// <summary>
/// Thrown where a configuration cannot be honoured, on the way in from the settings page or on the
/// way in from the file.
/// <para>
/// It carries the problems as values rather than only as a sentence, so a caller can name the field
/// without reading English. The message exists as well, because the place this most often surfaces
/// is the server's log, and a log line saying only that something was refused is a log line an
/// operator cannot act on.
/// </para>
/// <para>
/// <b>Nothing in the message names a path on the server's disk or a person.</b> A configuration
/// refusal is pasted into an issue tracker as readily as any other log line, which is the same rule
/// the API's failures are written under.
/// </para>
/// </summary>
public sealed class InvalidConfigurationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidConfigurationException"/> class for a set
    /// of problems.
    /// </summary>
    /// <param name="problems">What cannot be honoured, one entry per setting.</param>
    /// <exception cref="ArgumentNullException">Where no problems were given.</exception>
    public InvalidConfigurationException(IReadOnlyList<ConfigurationProblem> problems)
        : base(Describe(problems))
    {
        Problems = problems;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidConfigurationException"/> class. Present
    /// because the analyzers ask every exception for the three ordinary constructors; the
    /// constructor above is the one this type exists for.
    /// </summary>
    public InvalidConfigurationException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidConfigurationException"/> class with a
    /// message of the caller's own. See the note on the parameterless constructor.
    /// </summary>
    /// <param name="message">What happened.</param>
    public InvalidConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidConfigurationException"/> class with a
    /// message and an inner exception. See the note on the parameterless constructor.
    /// </summary>
    /// <param name="message">What happened.</param>
    /// <param name="innerException">What it happened because of.</param>
    public InvalidConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets what cannot be honoured, one entry per setting. Empty where this was built by one of the
    /// constructors that names no problem.
    /// </summary>
    public IReadOnlyList<ConfigurationProblem> Problems { get; } = [];

    private static string Describe(IReadOnlyList<ConfigurationProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        return string.Concat(
            "This plugin's settings cannot be honoured, so it is refusing them rather than running on values nobody chose. ",
            string.Join(" ", problems.Select(problem => problem.Why)));
    }
}

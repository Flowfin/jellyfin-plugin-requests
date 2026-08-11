using System;

namespace Jellyfin.Plugin.Requests.Bridge;

/// <summary>
/// What an external service says about something it was handed, in that service's own words.
/// <para>
/// The word is carried unmapped on purpose. Turning it into one of this plugin's states is #81, and
/// doing it here would put the mapping inside every adapter, where two adapters would disagree
/// about what a state means and nothing would say which was right.
/// </para>
/// </summary>
public sealed record BackendReport
{
    private readonly string _reported = string.Empty;

    /// <summary>
    /// Gets the state the service reported, as it reported it.
    /// </summary>
    /// <exception cref="ArgumentException">Where there is nothing in it.</exception>
    public required string Reported
    {
        get => _reported;
        init => _reported = Present(value);
    }

    /// <summary>
    /// Refuses an empty report. Nothing known is expressed by there being no report at all, and a
    /// report carrying an empty word would be a second way to say the same thing, which is the pair
    /// that leaves half the readers checking for one of them.
    /// </summary>
    /// <param name="value">The text as it arrived.</param>
    /// <returns>The text.</returns>
    private static string Present(string value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "A report with nothing in it is not a state a service reported. Where nothing is known, there is no report.",
                nameof(value))
            : value;
}

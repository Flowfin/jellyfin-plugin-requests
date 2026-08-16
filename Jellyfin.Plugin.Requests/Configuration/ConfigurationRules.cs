using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.Requests.Configuration;

/// <summary>
/// What a configuration has to be for this plugin to run on it, in one place, read by both moments
/// a configuration arrives.
/// <para>
/// The two moments are a save from the dashboard and a load from the file. They need the same
/// answer and they are not the same event: a plugin configuration is an XML file an operator can
/// edit by hand, so a rule enforced only on the page is a rule an editor walks past. One set of
/// rules read twice is what keeps the page and the file from disagreeing about what is allowed.
/// </para>
/// <para>
/// <b>Nothing here corrects a value.</b> Clamping a negative quota to zero, or a short retention up
/// to the floor, is the failure this type exists against: the install then does something other than
/// what its settings page shows, and nobody is told which of the two is true. A refusal keeps the
/// operator's value and says why it cannot be honoured.
/// </para>
/// <para>
/// A rule names the setting it is about. Where one rule is about two settings, as the accepted kinds
/// are, it names both, because either one of them is enough to fix it and a page that highlighted
/// one would be pointing at half the answer.
/// </para>
/// </summary>
public static class ConfigurationRules
{
    /// <summary>
    /// The smallest quota that lets anybody ask for anything. Zero is not a strict install, it is an
    /// install where the request endpoint refuses every caller while the plugin still advertises
    /// itself, and a negative number is the same thing typed by accident.
    /// </summary>
    public const int SmallestQuota = 1;

    /// <summary>
    /// Everything wrong with a configuration, or an empty list where there is nothing wrong.
    /// <para>
    /// Every problem rather than the first. An operator who hand-edited the file and gets one
    /// refusal at a time has to restart the server once per mistake to find out how many they made.
    /// </para>
    /// </summary>
    /// <param name="configuration">The configuration to judge.</param>
    /// <returns>One entry per setting that cannot be honoured, in the order the settings page shows them.</returns>
    /// <exception cref="ArgumentNullException">Where no configuration was given.</exception>
    public static IReadOnlyList<ConfigurationProblem> Problems(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var problems = new List<ConfigurationProblem>();

        if (configuration.OpenRequestsPerUser < SmallestQuota)
        {
            problems.Add(new ConfigurationProblem
            {
                Setting = nameof(PluginConfiguration.OpenRequestsPerUser),
                Why = string.Format(
                    CultureInfo.InvariantCulture,
                    "OpenRequestsPerUser is {0} and has to be at least {1}. Below that nobody may have a request open, so every ask is refused while the plugin still offers itself, and an operator reading the queue sees an empty one rather than a setting.",
                    configuration.OpenRequestsPerUser,
                    SmallestQuota)
            });
        }

        // The two kinds are one rule, because neither switch is wrong on its own and the pair can
        // be. Both are named: turning either back on fixes it, and the operator picks which.
        if (!configuration.AcceptsMovies && !configuration.AcceptsSeries)
        {
            problems.Add(NoKindAccepted(nameof(PluginConfiguration.AcceptsMovies)));
            problems.Add(NoKindAccepted(nameof(PluginConfiguration.AcceptsSeries)));
        }

        if (configuration.FinishedRequestRetentionDays < PluginConfiguration.MinimumRetentionDays)
        {
            problems.Add(new ConfigurationProblem
            {
                Setting = nameof(PluginConfiguration.FinishedRequestRetentionDays),
                Why = string.Format(
                    CultureInfo.InvariantCulture,
                    "FinishedRequestRetentionDays is {0} and has to be at least {1}. A shorter period removes the history while people are still asking about it, and the queue then answers \"was this asked for before\" with no.",
                    configuration.FinishedRequestRetentionDays,
                    PluginConfiguration.MinimumRetentionDays)
            });
        }

        // An address is optional and an unusable one is not. Empty means no sink, which is the
        // shipping state and nothing to refuse; anything typed has to be somewhere a notice can
        // actually be posted, because the alternative is a sink that silently sends nothing while
        // the settings page shows an address an operator believes in.
        if (!string.IsNullOrWhiteSpace(configuration.OutboundNoticeAddress)
            && !IsSomewhereANoticeCanBePosted(configuration.OutboundNoticeAddress))
        {
            problems.Add(new ConfigurationProblem
            {
                Setting = nameof(PluginConfiguration.OutboundNoticeAddress),
                Why = string.Format(
                    CultureInfo.InvariantCulture,
                    "OutboundNoticeAddress is \"{0}\", which is not a complete http or https address. It has to be one a notice can be posted to, scheme included, or empty to send nothing at all.",
                    configuration.OutboundNoticeAddress)
            });
        }

        return problems;
    }

    /// <summary>
    /// Whether an address is one a notice could be posted to.
    /// <para>
    /// Complete rather than relative, because there is nothing here for a relative address to be
    /// relative to, and HTTP rather than any scheme the runtime can parse: a file or mail address
    /// parses perfectly and is not a thing this plugin posts to.
    /// </para>
    /// </summary>
    /// <param name="address">What the operator typed.</param>
    /// <returns><see langword="true"/> where a notice could be posted there.</returns>
    private static bool IsSomewhereANoticeCanBePosted(string address)
        => Uri.TryCreate(address, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Whether this plugin can run on a configuration.
    /// </summary>
    /// <param name="configuration">The configuration to judge.</param>
    /// <returns><see langword="true"/> where nothing about it is refused.</returns>
    /// <exception cref="ArgumentNullException">Where no configuration was given.</exception>
    public static bool CanBeHonoured(PluginConfiguration configuration) => Problems(configuration).Count == 0;

    /// <summary>
    /// Refuses a configuration this plugin cannot run on, and does nothing at all otherwise.
    /// <para>
    /// This is what both moments call. Returning a corrected copy is the shape that is not offered,
    /// because a caller that receives one has no way to tell it apart from the one it passed in.
    /// </para>
    /// </summary>
    /// <param name="configuration">The configuration to judge.</param>
    /// <exception cref="ArgumentNullException">Where no configuration was given.</exception>
    /// <exception cref="InvalidConfigurationException">Where anything about it is refused.</exception>
    public static void RefuseWhatCannotWork(PluginConfiguration configuration)
    {
        var problems = Problems(configuration);

        if (problems.Count > 0)
        {
            throw new InvalidConfigurationException(problems);
        }
    }

    private static ConfigurationProblem NoKindAccepted(string setting)
        => new ConfigurationProblem
        {
            Setting = setting,
            Why = "AcceptsMovies and AcceptsSeries are both off, and at least one kind has to be accepted. With neither there is nothing anybody can ask for, which is an install that cannot work rather than a strict one.",
        };
}

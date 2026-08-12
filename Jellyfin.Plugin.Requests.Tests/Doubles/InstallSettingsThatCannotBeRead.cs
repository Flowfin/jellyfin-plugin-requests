using Jellyfin.Plugin.Requests.Configuration;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// An install whose stored settings are something the plugin cannot run on.
/// <para>
/// The real one refuses on the read rather than correcting what it found, so every caller meets the
/// refusal instead of acting on a value nobody chose. This is that refusal without a configuration
/// file, so a caller can be watched answering for it.
/// </para>
/// </summary>
internal sealed class InstallSettingsThatCannotBeRead : IInstallSettings
{
    /// <inheritdoc />
    /// <exception cref="InvalidConfigurationException">Always.</exception>
    public PluginConfiguration Current
        => throw new InvalidConfigurationException(
            [
                new ConfigurationProblem
                {
                    Setting = nameof(PluginConfiguration.AcceptsMovies),
                    Why = "Neither kind is accepted, so there is nothing anybody can ask for."
                }
            ]);
}

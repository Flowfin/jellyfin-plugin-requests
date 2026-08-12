using System;

namespace Jellyfin.Plugin.Requests.Configuration;

/// <summary>
/// The settings as the server holds them, which is on the plugin instance the host constructed.
/// <para>
/// This is the one place that reaches for that instance. Everything above takes
/// <see cref="IInstallSettings"/>, so the reach exists once and is testable everywhere else.
/// </para>
/// <para>
/// <b>What is read is also what is judged.</b> A configuration is an XML file an operator can edit
/// by hand, and the dashboard is not the only way one arrives, so a file holding a value the plugin
/// cannot honour is refused here rather than quietly replaced with a number nobody chose. Refusing
/// on the read is what makes the refusal reach every caller: nothing above this can act on a
/// corrected value, because no corrected value is ever produced. The rules are
/// <see cref="ConfigurationRules"/> and the save side of the same check is
/// <c>Plugin.UpdateConfiguration</c>.
/// </para>
/// <para>
/// Where an operator finds it is the server's log, through whatever asked. That is the honest limit
/// of this: it is a refusal a person reads in a log rather than a banner on a page, and a page that
/// says what is wrong with this install is the diagnostics view in #63.
/// </para>
/// </summary>
public sealed class ServerInstallSettings : IInstallSettings
{
    private readonly Func<PluginConfiguration?> _stored;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerInstallSettings"/> class over the plugin
    /// the host constructed. This is the one the container builds.
    /// </summary>
    public ServerInstallSettings()
        : this(static () => Plugin.Instance?.Configuration)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerInstallSettings"/> class over some other
    /// way of getting at the stored settings.
    /// <para>
    /// It exists so the refusal above can be proven without a plugin instance. The instance is a
    /// static the host sets while it is loading, so a test that reached for it would be reading a
    /// value any other test running beside it can replace, and the guard on the refusal would fail
    /// for a reason nobody caused.
    /// </para>
    /// </summary>
    /// <param name="stored">
    /// What the server holds, or <see langword="null"/> where this plugin has not been loaded.
    /// </param>
    /// <exception cref="ArgumentNullException">Where nothing was given to read from.</exception>
    public ServerInstallSettings(Func<PluginConfiguration?> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        _stored = stored;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Where the plugin has not been loaded, so there is no configuration to read. That is a host
    /// that never loaded this plugin rather than an install with nothing set, and it says so instead
    /// of answering with defaults nobody chose.
    /// </exception>
    /// <exception cref="InvalidConfigurationException">
    /// Where what is stored is something this plugin cannot run on.
    /// </exception>
    public PluginConfiguration Current
    {
        get
        {
            var configuration = _stored() ?? throw new InvalidOperationException(
                "The settings were asked for before this plugin was loaded, so there is no configuration to read.");

            ConfigurationRules.RefuseWhatCannotWork(configuration);

            return configuration;
        }
    }
}

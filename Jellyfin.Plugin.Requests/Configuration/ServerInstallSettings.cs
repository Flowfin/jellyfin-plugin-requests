using System;

namespace Jellyfin.Plugin.Requests.Configuration;

/// <summary>
/// The settings as the server holds them, which is on the plugin instance the host constructed.
/// <para>
/// This is the one place that reaches for that instance. Everything above takes
/// <see cref="IInstallSettings"/>, so the reach exists once and is testable everywhere else.
/// </para>
/// </summary>
public sealed class ServerInstallSettings : IInstallSettings
{
    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Where the plugin has not been loaded, so there is no configuration to read. That is a host
    /// that never loaded this plugin rather than an install with nothing set, and it says so instead
    /// of answering with defaults nobody chose.
    /// </exception>
    public PluginConfiguration Current
        => (Plugin.Instance ?? throw new InvalidOperationException(
            "The settings were asked for before this plugin was loaded, so there is no configuration to read."))
            .Configuration;
}

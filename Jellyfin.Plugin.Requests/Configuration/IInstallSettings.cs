namespace Jellyfin.Plugin.Requests.Configuration;

/// <summary>
/// What this install is set to, asked of the server rather than reached for.
/// <para>
/// The configuration belongs to the host: it is loaded, saved and replaced by the dashboard, and the
/// plugin instance is where the current one lives. Something that read it through the static
/// instance would be something no test can run without constructing a plugin, and a static an
/// operator can change under a running test is a suite that fails for a reason nobody caused.
/// </para>
/// <para>
/// So this is a seam of the same kind as the caller identity and the library: one property, answered
/// by the server in production and by a double in the suite.
/// </para>
/// </summary>
public interface IInstallSettings
{
    /// <summary>
    /// Gets what this install is set to now.
    /// <para>
    /// Read per call rather than kept, because an operator saving the settings page replaces the
    /// object and anything holding the old one would answer with what was configured when it
    /// started.
    /// </para>
    /// </summary>
    PluginConfiguration Current { get; }
}

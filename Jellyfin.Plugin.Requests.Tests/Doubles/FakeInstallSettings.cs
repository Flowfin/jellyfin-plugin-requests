using Jellyfin.Plugin.Requests.Configuration;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// What an install is set to, decided by the test.
/// <para>
/// The real one reads the configuration off the plugin the host constructed, which is a static a
/// test would have to build a plugin to reach and would then share with every other test running
/// beside it. This holds one object and hands it back, so a test that switches a kind off switches
/// it off for itself alone.
/// </para>
/// </summary>
internal sealed class FakeInstallSettings : IInstallSettings
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FakeInstallSettings"/> class, set to what a
    /// fresh install runs.
    /// </summary>
    public FakeInstallSettings()
        : this(new PluginConfiguration())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeInstallSettings"/> class.
    /// </summary>
    /// <param name="current">What this install is set to.</param>
    public FakeInstallSettings(PluginConfiguration current) => Current = current;

    /// <inheritdoc />
    public PluginConfiguration Current { get; }
}

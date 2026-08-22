using System;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SeamProbe;

/// <summary>
/// A second plugin in the same server process, which is the only position from which the question
/// this probe exists for can be asked. It does nothing else: no page, no endpoint, no configuration
/// anybody would set.
/// </summary>
public sealed class SeamProbePlugin : BasePlugin<BasePluginConfiguration>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SeamProbePlugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">The host's paths.</param>
    /// <param name="xmlSerializer">The host's serialiser.</param>
    public SeamProbePlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
    }

    /// <inheritdoc />
    public override string Name => "Seam probe";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("3c0f5b21-7f4d-4c9a-9a2e-7d1c5b8e4a60");
}

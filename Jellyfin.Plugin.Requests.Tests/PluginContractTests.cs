using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Xunit;

// Aliased rather than imported: inside this namespace the bare name `Plugin` binds to the
// `Jellyfin.Plugin` namespace segment, not to the type.
using PluginUnderTest = Jellyfin.Plugin.Template.Plugin;

namespace Jellyfin.Plugin.Requests.Tests;

/// <summary>
/// Facts about the built plugin assembly that hold without a running server. Neither test
/// constructs the plugin, because that needs host services the suite does not have yet.
/// </summary>
public class PluginContractTests
{
    /// <summary>
    /// The configuration page is served out of the assembly by a path the plugin builds from its
    /// own namespace at run time. If the namespace and the embedded resource ever disagree, the
    /// build stays green and the page is blank in the dashboard, which is a failure nobody sees
    /// until somebody opens it.
    /// </summary>
    [Fact]
    public void ConfigurationPageResourceExistsUnderThePathThePluginAsksFor()
    {
        var expected = string.Format(
            CultureInfo.InvariantCulture,
            "{0}.Configuration.configPage.html",
            typeof(PluginUnderTest).Namespace);

        var embedded = typeof(PluginUnderTest).Assembly.GetManifestResourceNames();

        Assert.Contains(expected, embedded, StringComparer.Ordinal);
    }

    /// <summary>
    /// Each target framework is one Jellyfin server line, and the SDK version is conditioned on the
    /// target in the project file. Nothing refuses the wrong pairing at compile time: a net10.0
    /// assembly built against the 10.11 SDK compiles clean and fails when a 12.0 server loads it.
    /// This reads the version the compiler actually recorded, so the pairing is checked here
    /// instead of at somebody's server start.
    /// </summary>
    [Fact]
    public void PluginIsCompiledAgainstTheServerLineThisTargetIsFor()
    {
        var jellyfin = typeof(PluginUnderTest).Assembly
            .GetReferencedAssemblies()
            .SingleOrDefault(a => string.Equals(a.Name, "MediaBrowser.Common", StringComparison.Ordinal));

        Assert.NotNull(jellyfin);
        Assert.NotNull(jellyfin!.Version);

#if NET10_0_OR_GREATER
        // net10.0 is the Jellyfin 12.0 line.
        Assert.Equal(12, jellyfin.Version!.Major);
#else
        // net9.0 is the Jellyfin 10.11 line.
        Assert.Equal(10, jellyfin.Version!.Major);
        Assert.Equal(11, jellyfin.Version!.Minor);
#endif
    }
}

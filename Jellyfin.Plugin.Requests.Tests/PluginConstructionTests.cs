using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using MediaBrowser.Model.Plugins;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests;

/// <summary>
/// The plugin constructed against doubles rather than a running server. These are the first tests
/// on this board that reach the plugin's own behaviour, and they exist to show the doubles carry
/// the construction path the host would.
/// </summary>
public class PluginConstructionTests
{
    /// <summary>
    /// The plugin reads and writes its configuration through the host's paths and serializer. If
    /// either double is short of what the constructor needs, this throws rather than failing an
    /// assertion, which is the signal that the double set has drifted from the host surface.
    /// </summary>
    [Fact]
    public void PluginConstructsAgainstTheDoubles()
    {
        // Planted for proof/28-invariants-refuse: a second call site, which is the
        // shape plugin-constructed-only-by-the-host-double exists to refuse.
        using var paths = new FakeApplicationPaths();
        var plugin = new Plugin(paths, new FakeXmlSerializer());
        Assert.NotNull(plugin);

        using var host = new PluginHost();

        Assert.NotNull(host.Plugin);
        Assert.False(string.IsNullOrWhiteSpace(host.Plugin.Name));
        Assert.NotEqual(Guid.Empty, host.Plugin.Id);
    }

    /// <summary>
    /// Nothing the plugin writes may land outside the directory the paths double created. A test
    /// that leaves a file in a real server's data directory is a test that changed the machine it
    /// ran on.
    /// </summary>
    [Fact]
    public void EveryPathThePluginIsGivenIsInsideTheTemporaryRoot()
    {
        using var host = new PluginHost();
        var root = Path.GetFullPath(host.ApplicationPaths.ProgramDataPath);

        var paths = new[]
        {
            host.ApplicationPaths.PluginConfigurationsPath,
            host.ApplicationPaths.PluginsPath,
            host.ApplicationPaths.DataPath,
            host.ApplicationPaths.ConfigurationDirectoryPath,
            host.ApplicationPaths.SystemConfigurationFilePath,
            host.ApplicationPaths.LogDirectoryPath,
            host.ApplicationPaths.TempDirectory,
            host.ApplicationPaths.BackupPath,
        };

        Assert.All(paths, p => Assert.StartsWith(root, Path.GetFullPath(p), StringComparison.Ordinal));
    }

    /// <summary>
    /// The configuration page the dashboard asks for comes from the plugin instance, not from the
    /// assembly metadata alone, so this is the instance-level counterpart of the resource check in
    /// <see cref="PluginContractTests"/>.
    /// </summary>
    [Fact]
    public void GetPagesNamesTheEmbeddedConfigurationPage()
    {
        using var host = new PluginHost();

        var pages = host.Plugin.GetPages().ToList();
        var page = Assert.Single(pages);

        Assert.IsType<PluginPageInfo>(page);
        Assert.Contains(
            page.EmbeddedResourcePath,
            host.Plugin.GetType().Assembly.GetManifestResourceNames(),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The paths double deletes what it created. A suite that leaves a directory per test run fills
    /// the machine slowly enough that nobody connects it to the suite.
    /// </summary>
    [Fact]
    public void DisposingTheHostRemovesTheTemporaryRoot()
    {
        string root;

        using (var host = new PluginHost())
        {
            root = host.ApplicationPaths.ProgramDataPath;
            Assert.True(Directory.Exists(root));
        }

        Assert.False(Directory.Exists(root));
    }
}

using System;
using System.Globalization;
using System.IO;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// The host's application paths, rooted at a directory this instance creates and deletes. Every
/// path is under that root, so a test that writes through the plugin cannot reach a real server's
/// data directory, and nothing survives the test that created it.
/// </summary>
internal sealed class FakeApplicationPaths : IApplicationPaths, IDisposable
{
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeApplicationPaths"/> class.
    /// </summary>
    public FakeApplicationPaths()
    {
        ProgramDataPath = Path.Combine(
            Path.GetTempPath(),
            string.Create(CultureInfo.InvariantCulture, $"jellyfin-plugin-requests-tests-{Guid.NewGuid():N}"));

        Directory.CreateDirectory(ProgramDataPath);
        Directory.CreateDirectory(PluginConfigurationsPath);
    }

    /// <inheritdoc />
    public string ProgramDataPath { get; }

    /// <inheritdoc />
    public string WebPath => Under("web");

    /// <inheritdoc />
    public string ProgramSystemPath => Under("system");

    /// <inheritdoc />
    public string DataPath => Under("data");

    /// <inheritdoc />
    public string VirtualDataPath => Under("virtual-data");

    /// <inheritdoc />
    public string ImageCachePath => Under("image-cache");

    /// <inheritdoc />
    public string PluginsPath => Under("plugins");

    /// <inheritdoc />
    public string PluginConfigurationsPath => Under("plugin-configurations");

    /// <inheritdoc />
    public string LogDirectoryPath => Under("log");

    /// <inheritdoc />
    public string ConfigurationDirectoryPath => Under("config");

    /// <inheritdoc />
    public string SystemConfigurationFilePath => Path.Combine(ConfigurationDirectoryPath, "system.xml");

    /// <inheritdoc />
    public string CachePath { get; set; } = string.Empty;

    /// <inheritdoc />
    public string TempDirectory => Under("temp");

    /// <inheritdoc />
    public string TrickplayPath => Under("trickplay");

    /// <inheritdoc />
    public string BackupPath => Under("backup");

    /// <inheritdoc />
    public void CreateAndCheckMarker(string path, string markerName, bool recursive = false)
    {
        Directory.CreateDirectory(path);
    }

    /// <inheritdoc />
    public void MakeSanityCheckOrThrow()
    {
        // Nothing to check: every path this double returns is under a directory it created itself.
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (Directory.Exists(ProgramDataPath))
        {
            Directory.Delete(ProgramDataPath, true);
        }
    }

    private string Under(string name) => Path.Combine(ProgramDataPath, name);
}

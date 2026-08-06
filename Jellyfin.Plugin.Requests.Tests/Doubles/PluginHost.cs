using System;
using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// The one place in the suite that constructs the plugin. Every host service the plugin injects is
/// passed from here, so a new constructor parameter fails to compile at this call site and the suite
/// stops building until its double exists. That is the whole point of routing construction through
/// one file rather than letting each test call the constructor.
/// </summary>
internal sealed class PluginHost : IDisposable
{
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginHost"/> class.
    /// </summary>
    public PluginHost()
    {
        ApplicationPaths = new FakeApplicationPaths();
        XmlSerializer = new FakeXmlSerializer();
        Plugin = new PluginUnderTest(ApplicationPaths, XmlSerializer);
    }

    /// <summary>
    /// Gets the plugin under test.
    /// </summary>
    public PluginUnderTest Plugin { get; }

    /// <summary>
    /// Gets the paths double the plugin was given.
    /// </summary>
    public FakeApplicationPaths ApplicationPaths { get; }

    /// <summary>
    /// Gets the serializer double the plugin was given.
    /// </summary>
    public FakeXmlSerializer XmlSerializer { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ApplicationPaths.Dispose();
    }
}

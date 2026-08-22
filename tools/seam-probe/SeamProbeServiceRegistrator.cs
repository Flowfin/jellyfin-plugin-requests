using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SeamProbe;

/// <summary>
/// Puts the report in the host's own start-up sequence, so it runs after the container is built and
/// can therefore ask it something.
/// </summary>
public sealed class SeamProbeServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHostedService<ContainerReport>();
    }
}

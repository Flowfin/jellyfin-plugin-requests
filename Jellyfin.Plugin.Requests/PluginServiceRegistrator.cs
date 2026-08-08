using Jellyfin.Plugin.Requests.Identity;
using Jellyfin.Plugin.Requests.Time;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Requests;

/// <summary>
/// What the server puts in its container on this plugin's behalf. The host calls this once while it
/// is building the container, so anything registered here can be injected into whatever the plugin
/// later adds.
/// <para>
/// The two registrations are the clock and the identifier source. Both are the real implementations,
/// which is what a server should get and what nothing has to configure. A test never calls this: it
/// hands its own implementations in directly, which is the whole reason the two are interfaces.
/// </para>
/// <para>
/// Both are singletons because both are stateless and because a second clock is not a second
/// opinion about the time. A scoped registration would also mean two objects created in one request
/// could disagree about the moment it happened.
/// </para>
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IClock, SystemClock>();
        serviceCollection.AddSingleton<IIdentifierSource, GuidIdentifierSource>();
    }
}

using System;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Identity;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Time;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        // The store, one per server for the same reason the other two are: it holds the set in
        // memory and serialises the writes against it, and a second instance over one directory
        // would be two sets deciding independently what the file should say.
        //
        // Built from a factory rather than from a type, because the directory is the plugin's own
        // data folder and only the plugin knows it. The factory runs when something first asks for a
        // store, which is after the host has constructed the plugin, so the instance is there by
        // then; where it is not, that is a host that never loaded this plugin and the failure says
        // so instead of writing requests into a path built out of nothing.
        //
        // The logger is the server's, taken from the container rather than made here, so a refusal
        // to open the file lands in the log the operator is already reading.
        serviceCollection.AddSingleton<IRequestStore>(provider => new FileRequestStore(
            (Plugin.Instance ?? throw new InvalidOperationException(
                "The request store was asked for before this plugin was loaded, so there is no data directory to keep requests in."))
            .DataFolderPath,
            provider.GetRequiredService<ILogger<FileRequestStore>>()));

        // Who is calling, asked of the server rather than decided here. Registered so an endpoint
        // takes the seam and never the server's context directly, which is what keeps an endpoint
        // testable without a running server.
        serviceCollection.AddSingleton<ICallerIdentity, ServerCallerIdentity>();
    }
}

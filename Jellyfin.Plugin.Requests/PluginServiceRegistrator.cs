using System;
using System.Net.Http;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Fulfilment;
using Jellyfin.Plugin.Requests.Identity;
using Jellyfin.Plugin.Requests.Localisation;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.People;
using Jellyfin.Plugin.Requests.Seam;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Surface;
using Jellyfin.Plugin.Requests.Time;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Tasks;
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

        // The word catalogues, read out of the assembly once. They are files inside it and cannot
        // change while the server is running, so a second copy would be a second parse of the same
        // bytes per page load.
        serviceCollection.AddSingleton(StringCatalogue.Shipped);

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
            provider.GetRequiredService<ILogger<FileRequestStore>>(),
            provider.GetRequiredService<IClock>()));

        // Who is calling, asked of the server rather than decided here. Registered so an endpoint
        // takes the seam and never the server's context directly, which is what keeps an endpoint
        // testable without a running server.
        serviceCollection.AddSingleton<ICallerIdentity, ServerCallerIdentity>();

        // What this install is set to, for the same reason. The settings live on the plugin instance
        // the host constructed and are replaced whole when an operator saves the page, so this reads
        // them per call rather than holding one, and everything above takes the seam instead of
        // reaching for the static.
        serviceCollection.AddSingleton<IInstallSettings, ServerInstallSettings>();

        // The bridge to an external request service, which on most servers is the one that has no
        // service behind it. Registered like the others because it is the shipping default rather
        // than a placeholder: a fresh install resolves this and no caller above it asks whether a
        // service exists before deciding what to do. An adapter, when there is one, replaces this
        // registration and nothing else.
        serviceCollection.AddSingleton<IRequestBackend, NoRequestBackend>();

        // What an approval hands over and what it keeps of the answer. Registered rather than built
        // inside the controller, because it takes the bridge and the server's log, and because it
        // writes to the store: a second construction of it somewhere else would be a second place
        // that decides whether a request has already been handed over.
        serviceCollection.AddSingleton<BridgeSubmission>(provider => new BridgeSubmission(
            provider.GetRequiredService<IRequestBackend>(),
            provider.GetRequiredService<IRequestStore>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ILogger<BridgeSubmission>>()));

        // When the bridge was last seen answering. One per server, because it is a fact about the
        // install and the controller that reads it is built per call: state kept on that controller
        // would be forgotten between two reads of the same page.
        serviceCollection.AddSingleton<BridgeWatch>();

        // The seam the sibling discover plugin hands a want across. Registered into the server's own
        // collection because that is where a second plugin in this process would resolve it from;
        // whether one can name the type at all is #117 and is not decided by registering it.
        serviceCollection.AddSingleton<IKnownUsers, ServerKnownUsers>();

        serviceCollection.AddSingleton<IWantHandover>(provider => new WantHandover(
            provider.GetRequiredService<IRequestStore>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<IIdentifierSource>(),
            provider.GetRequiredService<IInstallSettings>(),
            provider.GetRequiredService<IKnownUsers>(),
            provider.GetRequiredService<IArrivalNotice>(),
            provider.GetRequiredService<ILogger<WantHandover>>(),
            WantHandover.DefaultAnswerWithin));

        // The record every move leaves behind, which is the server's own activity log rather than
        // anything this plugin keeps. Registered beside the sink below and not instead of it: the
        // sink is a courtesy that may be lost, and this is the line an operator reads afterwards in
        // a dashboard they already have open.
        serviceCollection.AddSingleton<IActivityJournal>(provider => new ServerActivityJournal(
            provider.GetRequiredService<IActivityManager>(),
            provider.GetRequiredService<ILogger<ServerActivityJournal>>()));

        // The one path anything this plugin has to say leaves the server on. Registered on every
        // install rather than only where an address is set, because whether one is set is a value in
        // a file an operator edits while the server is running, and a container built at startup
        // would answer with whatever was true then. The sink reads the address per notice and an
        // install with none sends nothing, so the registration costs an object and no connection.
        //
        // The handler is built here and never shared. It is the socket pipeline the client sends
        // through, and it is a constructor argument rather than something the sink makes so that the
        // suite can put an endpoint in the same process, which is what keeps the outbound path
        // testable under the headless rule.
        serviceCollection.AddSingleton<IOutboundSink>(provider => new OutboundSink(
            provider.GetRequiredService<IInstallSettings>(),
            new SocketsHttpHandler(),
            provider.GetRequiredService<ILogger<OutboundSink>>(),
            OutboundSink.DefaultAnswerWithin));

        // Who has said they do not want to be told about their own requests. One per server for the
        // reason the request store is: it holds the set in memory and serialises its writes against
        // it, and a second instance over one directory would be two sets deciding independently what
        // the file should say. Built from a factory for the same reason too, because the directory
        // is the plugin's own data folder and only the plugin knows it.
        serviceCollection.AddSingleton<INoticePreferences>(_ => new FileNoticePreferences(
            (Plugin.Instance ?? throw new InvalidOperationException(
                "What people have set about being told was asked for before this plugin was loaded, so there is no data directory to keep it in."))
            .DataFolderPath));

        // The path that tells the person who asked that their own request moved, on whatever they
        // are signed in on right now. Registered beside the two above rather than instead of either:
        // the journal is the record an operator reads afterwards, the sink is what leaves the
        // machine on an install that has somewhere to send to, and this is the only one of the three
        // aimed at the person waiting.
        //
        // The person's own switch is in front of it rather than inside it, so the one class that
        // names the host stays the one call it is, and so that neither of the two paths that tell
        // somebody has to remember to ask. What is registered is the wrapper, because everything
        // above resolves the interface and nothing above may reach past the switch.
        serviceCollection.AddSingleton<IRequesterNotice>(provider => new QuietedRequesterNotice(
            new ServerRequesterNotice(
                provider.GetRequiredService<ISessionManager>(),
                provider.GetRequiredService<ILogger<ServerRequesterNotice>>()),
            provider.GetRequiredService<INoticePreferences>(),
            provider.GetRequiredService<ILogger<QuietedRequesterNotice>>()));

        // The path that tells whoever administers the server that somebody has asked for something,
        // on whatever they are signed in on right now. It is a fourth registration rather than a
        // second use of the one above because the audience is different and so is the host call:
        // that one names one person and this one names none, and the server decides which sessions
        // administer it. The settings are handed in because an install says nothing here until an
        // operator turns it on, and the switch is read per notice rather than at startup.
        serviceCollection.AddSingleton<IArrivalNotice>(provider => new ServerArrivalNotice(
            provider.GetRequiredService<ISessionManager>(),
            provider.GetRequiredService<IInstallSettings>(),
            provider.GetRequiredService<ILogger<ServerArrivalNotice>>()));

        // The server's library, as the two questions this plugin asks of it. One per server, because
        // the instance subscribes to the library's own events and a second subscription would look
        // at every arrival twice.
        serviceCollection.AddSingleton<ILibrary, ServerLibrary>();

        // The logger is named for the type that writes through it, which is what puts this plugin's
        // own category beside the rest in an operator's log. It is handed in rather than resolved by
        // the container's generic rule, because both types take a plain logger for the same reason
        // the store does: a test hands one in without a container.
        serviceCollection.AddSingleton(provider => new FulfilmentSweep(
            provider.GetRequiredService<IRequestStore>(),
            provider.GetRequiredService<ILibrary>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<IActivityJournal>(),
            provider.GetRequiredService<IOutboundSink>(),
            provider.GetRequiredService<IRequesterNotice>(),
            provider.GetRequiredService<ILogger<FulfilmentSweep>>()));

        // The event half of the fulfilment check. A hosted service rather than something built when
        // somebody first asks for it, because it has to be subscribed before the first library event
        // rather than after the first request, and because the server is what tells it to stop.
        serviceCollection.AddHostedService(provider => new LibraryWatcher(
            provider.GetRequiredService<ILibrary>(),
            provider.GetRequiredService<FulfilmentSweep>(),
            provider.GetRequiredService<ILogger<LibraryWatcher>>()));

        // The scheduled half. The server finds a scheduled task by scanning the plugin's assembly
        // rather than by reading this container, so nothing here is what makes it run; it is
        // registered so that the one it constructs is built from the same objects as everything
        // above rather than from a second set.
        serviceCollection.AddSingleton<IScheduledTask, FulfilmentTask>();

        // What removes a finished request once it has been kept for as long as this install says.
        // The settings are taken as the seam rather than read here, because the period is a value an
        // operator changes while the server is running and a number captured at startup would be the
        // one they replaced.
        serviceCollection.AddSingleton(provider => new RetentionSweep(
            provider.GetRequiredService<IRequestStore>(),
            provider.GetRequiredService<IInstallSettings>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ILogger<RetentionSweep>>()));

        serviceCollection.AddSingleton<IScheduledTask, RetentionTask>();
        serviceCollection.AddSingleton(provider => new AccountRemoval(
            provider.GetRequiredService<IRequestStore>(),
            provider.GetRequiredService<INoticePreferences>(),
            provider.GetRequiredService<ILogger<AccountRemoval>>()));
        serviceCollection.AddSingleton<IEventConsumer<UserDeletedEventArgs>, RemovedAccounts>();

        // What asks the external request service where the requests handed to it stand. Registered
        // on every install rather than only where a service is configured, for the reason the bridge
        // itself is: whether one exists is a value in a file an operator edits while the server is
        // running, and a task list built at startup would answer with whatever was true then. On an
        // install with no service the run ends at the reachability check and walks nothing.
        serviceCollection.AddSingleton(provider => new BridgeReconciliation(
            provider.GetRequiredService<IRequestStore>(),
            provider.GetRequiredService<IRequestBackend>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<IActivityJournal>(),
            provider.GetRequiredService<IRequesterNotice>(),
            provider.GetRequiredService<BridgeWatch>(),
            provider.GetRequiredService<ILogger<BridgeReconciliation>>()));

        serviceCollection.AddSingleton<IScheduledTask, ReconciliationTask>();

        // The surface every client can reach. The server resolves its channels out of this
        // container, so this registration is what puts a place beside a person's libraries on
        // clients this project will never change.
        //
        // IT NO LONGER TAKES THE STORE, AND THAT IS THE POINT RATHER THAN A TIDYING. #67 measured
        // that an answer built from one person's requests does not stay that person's on a running
        // server, so the channel answers the same folder to everybody and reads nothing. A
        // dependency it cannot use is a dependency somebody adds a use for.
        serviceCollection.AddSingleton<IChannel>(provider => new RequestsChannel(
            provider.GetRequiredService<StringCatalogue>()));
    }
}

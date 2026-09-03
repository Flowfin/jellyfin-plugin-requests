using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Identity;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.People;
using Jellyfin.Plugin.Requests.Seam;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Jellyfin.Plugin.Requests.Time;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests;

/// <summary>
/// What the server ends up holding on this plugin's behalf. The registrator is the only place a
/// server is told which implementation to use, so a registration that was dropped or pointed at the
/// wrong type would leave the plugin resolving nothing on a real server while every test that hands
/// its own doubles in went on passing.
/// <para>
/// RUN THIS CLASS ON ITS OWN AFTER CHANGING IT, because a whole-suite run cannot tell you what it
/// covers here. Several registrations resolve <c>Plugin.Instance</c>, a static the host sets while
/// loading and no test sets, and another collection running first leaves an instance in it often
/// enough that the graph resolves anyway. So a test here that reaches the static passes in the
/// suite and fails alone, which is how one of them stood from the day it landed until it reddened
/// one target framework out of two on the mainline. The gate runs the whole suite and nothing in
/// it runs this class by itself, so what catches the next one is the run below rather than the gate.
/// </para>
/// <para>
/// <c>dotnet test Jellyfin.Plugin.Requests.sln --configuration Release --filter
/// "FullyQualifiedName~PluginServiceRegistrationTests"</c>.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public class PluginServiceRegistrationTests
{
    private static readonly Guid Asker = new Guid("11111111-1111-1111-1111-111111111111");

    /// <summary>
    /// A server asking for the clock gets the machine's clock, and a server asking for the
    /// identifier source gets the framework's generator. Those are the defaults nothing should have
    /// to configure.
    /// </summary>
    [Fact]
    public void ServerGetsTheRealImplementations()
    {
        using var provider = Registered();

        Assert.IsType<SystemClock>(provider.GetRequiredService<IClock>());
        Assert.IsType<GuidIdentifierSource>(provider.GetRequiredService<IIdentifierSource>());
    }

    /// <summary>
    /// A fresh install gets the adapter, and the adapter with no address written is the bridge that
    /// has no external service behind it. That is the shipping default and the one most servers run,
    /// so a registration pointing anywhere else, or missing, would leave the majority case resolving
    /// nothing while every test handing its own bridge in went on passing. Both halves are asserted:
    /// which type is resolved, and that with nothing configured it answers as nothing configured
    /// rather than dialling anything.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ServerGetsTheAdapterWhichWithNoAddressIsTheBridgeWithNoServiceBehindIt()
    {
        var services = new ServiceCollection();
        new PluginServiceRegistrator().RegisterServices(services, null!);

        // The adapter takes the server's log, which the server registers and this bare collection
        // does not, and it reads the settings the host holds, which no plugin instance holds here.
        // Both are stood in for so that what is measured is the registration and not the host.
        services.AddLogging();
        services.AddSingleton<IInstallSettings>(new FakeInstallSettings(new PluginConfiguration()));

        using var provider = services.BuildServiceProvider();

        var bridge = Assert.IsType<Jellyfin.Plugin.Requests.Bridge.Overseerr.OverseerrBackend>(
            provider.GetRequiredService<IRequestBackend>());

        Assert.Equal(
            BackendReachability.NotConfigured,
            await bridge.CheckReachableAsync(CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>
    /// A server asking what this install is set to gets the settings the host holds, rather than a
    /// second object nothing writes to. The dashboard replaces the configuration whole when an
    /// operator saves the page, so anything resolving its own copy would answer with what was set
    /// when the server started.
    /// </summary>
    [Fact]
    public void ServerGetsTheSettingsTheHostHolds()
    {
        using var provider = Registered();

        Assert.IsType<ServerInstallSettings>(provider.GetRequiredService<IInstallSettings>());
    }

    /// <summary>
    /// Two things asking for the clock get the same clock. A second instance is not a second
    /// opinion about the time, and two objects created while handling one request must not disagree
    /// about the moment it happened.
    /// </summary>
    [Fact]
    public void ClockAndIdentifierSourceAreOnePerServer()
    {
        using var provider = Registered();

        Assert.Same(provider.GetRequiredService<IClock>(), provider.GetRequiredService<IClock>());
        Assert.Same(
            provider.GetRequiredService<IIdentifierSource>(),
            provider.GetRequiredService<IIdentifierSource>());
    }

    /// <summary>
    /// A server holds the seam the sibling discover plugin would resolve, and it is this plugin's
    /// implementation rather than something the container built by accident.
    /// <para>
    /// This is the registration half of being a sink. Whether a second plugin in one process can
    /// name the type at all is the assembly-loading question in #117 and is not answered by
    /// resolving it from inside this assembly, which is why the sentence above says what it says.
    /// </para>
    /// </summary>
    [Fact]
    public void ServerGetsTheSeamTheSiblingWouldResolve()
    {
        using var provider = RegisteredWithTheServerStoodInFor(new InMemoryRequestStore());

        Assert.IsType<WantHandover>(provider.GetRequiredService<IWantHandover>());
    }

    /// <summary>
    /// The server reaches the account removal, which is the half of #49 no test of the sweep itself
    /// can cover.
    /// <para>
    /// The rule about what a deleted account leaves behind is worth nothing if nothing on a running
    /// server ever calls it, and what calls it is the consumer the host resolves for its own
    /// user-deleted event. So the registration is asserted by type rather than by the sweep being
    /// constructible: a sweep nobody is wired to would pass every test in
    /// <c>AccountRemovalTests</c> and do nothing on a server.
    /// </para>
    /// </summary>
    [Fact]
    public void ServerGetsTheThingThatHearsAnAccountBeingDeleted()
    {
        using var provider = RegisteredWithTheServerStoodInFor(new InMemoryRequestStore());

        Assert.IsType<RemovedAccounts>(provider.GetRequiredService<IEventConsumer<UserDeletedEventArgs>>());
        Assert.NotNull(provider.GetRequiredService<AccountRemoval>());
    }

    /// <summary>
    /// One sink and no more. The sibling has to define what several implementations of its contract
    /// mean, and this plugin should not be the reason it has to: a second registration here would
    /// hand it two sinks that both believe they own the queue.
    /// </summary>
    [Fact]
    public void ExactlyOneSinkIsRegistered()
    {
        var services = new ServiceCollection();

        new PluginServiceRegistrator().RegisterServices(services, null!);

        Assert.Single(services, service => service.ServiceType == typeof(IWantHandover));
    }

    /// <summary>
    /// Registering the sink is harmless on the ordinary server, which is one with no sibling
    /// installed. Nothing here reaches for the other plugin, nothing starts, and a handover that
    /// never arrives costs the server the object and nothing else.
    /// <para>
    /// That no sibling assembly is loaded while this runs is asserted in
    /// <c>SiblingIndependenceTests</c>, so what is added here is that the registration produces a
    /// working sink in that state rather than one waiting for something absent.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheSinkWorksOnAServerWithNoSiblingInstalled()
    {
        var store = new InMemoryRequestStore();

        using var provider = RegisteredWithTheServerStoodInFor(store);

        var accepted = await provider.GetRequiredService<IWantHandover>().AcceptAsync(
            new HandedOverWant
            {
                ContractVersion = WantHandover.KnownContractVersion,
                WantId = new Guid("44444444-4444-4444-4444-444444444444"),
                RequestedByUserId = Asker,
                Kind = RequestedItemKind.Movie,
                Title = "Solaris",
                Year = 1972
            },
            CancellationToken.None);

        Assert.True(accepted);
        Assert.Single(await store.GetAllAsync(CancellationToken.None));
    }

    private static ServiceProvider Registered()
    {
        var services = new ServiceCollection();

        // The application host is passed in by the server and this registrator does not read it,
        // so the suite has nothing to build here. If a registration ever needs it, this line stops
        // compiling or throws, which is the signal to give the suite a host double.
        new PluginServiceRegistrator().RegisterServices(services, null!);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The registration as it ships, with the six things behind it that only a running server has.
    /// <para>
    /// Each of the six is stood in for because reaching the real one from a test would read something
    /// other than the registration. The logger factory, the user manager and the session manager come
    /// from the server's own container and are not in this collection at all. The store, the settings
    /// and the notice preferences all reach the plugin instance, which is a static the host sets while
    /// loading and which any test running beside this one replaces, so a test resolving them would
    /// fail for a reason nobody caused; <c>ServerInstallSettings</c> says so about itself where it
    /// takes its second constructor, and the registration of <c>FileNoticePreferences</c> throws by
    /// name when the static is empty. What is left over the six is the seam's own registration, which
    /// is what these tests are about.
    /// </para>
    /// <para>
    /// THOSE THREE ARE THE WHOLE POPULATION AS THIS IS WRITTEN, and what says so is the static rather
    /// than this list, which drifts: <c>git grep -n "Plugin.Instance" --
    /// Jellyfin.Plugin.Requests/</c> returns the settings, the store's directory and the notice
    /// preferences' directory and nothing else. A registration added to that set and not to this
    /// helper resolves here only while another collection has run first.
    /// </para>
    /// <para>
    /// The session manager arrived with the arrival notice the seam announces through. It is the
    /// double that raises on every way of reaching somebody this plugin is not allowed to use, so a
    /// registration that resolved a wider path than the one it declares fails here rather than in
    /// front of an operator.
    /// </para>
    /// </summary>
    /// <param name="store">The queue the sink writes into.</param>
    /// <returns>A provider the sink can be resolved from.</returns>
    private static ServiceProvider RegisteredWithTheServerStoodInFor(IRequestStore store)
    {
        var services = new ServiceCollection();

        new PluginServiceRegistrator().RegisterServices(services, null!);

        services.AddLogging();
        services.AddSingleton(store);
        services.AddSingleton<IInstallSettings>(new FakeInstallSettings());
        services.AddSingleton<IKnownUsers>(new FakeKnownUsers(Asker));
        services.AddSingleton<ISessionManager>(new ASessionManagerThatOnlyDelivers());
        services.AddSingleton<INoticePreferences>(new InMemoryNoticePreferences());

        return services.BuildServiceProvider();
    }
}

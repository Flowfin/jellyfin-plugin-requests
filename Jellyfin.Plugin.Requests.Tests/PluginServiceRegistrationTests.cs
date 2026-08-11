using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Identity;
using Jellyfin.Plugin.Requests.Time;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests;

/// <summary>
/// What the server ends up holding on this plugin's behalf. The registrator is the only place a
/// server is told which implementation to use, so a registration that was dropped or pointed at the
/// wrong type would leave the plugin resolving nothing on a real server while every test that hands
/// its own doubles in went on passing.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public class PluginServiceRegistrationTests
{
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
    /// A fresh install gets the bridge that has no external service behind it. That is the shipping
    /// default and the one most servers run, so a registration pointing anywhere else, or missing,
    /// would leave the majority case resolving nothing while every test handing its own bridge in
    /// went on passing.
    /// </summary>
    [Fact]
    public void ServerGetsTheBridgeWithNoServiceBehindIt()
    {
        using var provider = Registered();

        Assert.IsType<NoRequestBackend>(provider.GetRequiredService<IRequestBackend>());
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

    private static ServiceProvider Registered()
    {
        var services = new ServiceCollection();

        // The application host is passed in by the server and this registrator does not read it,
        // so the suite has nothing to build here. If a registration ever needs it, this line stops
        // compiling or throws, which is the signal to give the suite a host double.
        new PluginServiceRegistrator().RegisterServices(services, null!);

        return services.BuildServiceProvider();
    }
}

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SeamProbe;

/// <summary>
/// Asks the running server what a second plugin can see of the first one, and writes the answer
/// where a run can read it.
/// <para>
/// It names the other plugin by string and references none of its types, so the probe compiles
/// against the host alone. That is the point rather than a convenience: if the two plugins do not
/// share a load context, an assembly reference here would fail before anything could be reported,
/// and a probe that cannot run is a probe that says nothing.
/// </para>
/// </summary>
public sealed class ContainerReport : IHostedService
{
    /// <summary>
    /// The string a run greps the server's log for. Every line the probe writes carries it.
    /// </summary>
    public const string Marker = "SEAM-PROBE";

    private const string OtherAssembly = "Jellyfin.Plugin.Requests";
    private const string Contract = "Jellyfin.Plugin.Requests.Seam.IWantHandover";

    private readonly IServiceProvider _services;
    private readonly ILogger<ContainerReport> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContainerReport"/> class.
    /// </summary>
    /// <param name="services">The container the host built.</param>
    /// <param name="logger">The host's logger.</param>
    public ContainerReport(IServiceProvider services, ILogger<ContainerReport> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Look();
        }
        catch (Exception error)
        {
            // A probe that throws takes the server down with it, and a server that did not start
            // answers a different question from the one being asked.
            Say("the probe itself failed: {0}: {1}", error.GetType().FullName ?? "?", error.Message);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Look()
    {
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => string.Equals(assembly.GetName().Name, OtherAssembly, StringComparison.Ordinal))
            .ToArray();

        Say("assemblies loaded under the name {0}: {1}", OtherAssembly, loaded.Length);
        foreach (Assembly assembly in loaded)
        {
            Say("one of them is at {0}", string.IsNullOrEmpty(assembly.Location) ? "(no location)" : assembly.Location);
        }

        Type? contract = null;
        foreach (Assembly assembly in loaded)
        {
            contract = assembly.GetType(Contract, throwOnError: false);
            if (contract is not null)
            {
                break;
            }
        }

        if (contract is null)
        {
            Say("the type {0} is not reachable from this plugin", Contract);
            return;
        }

        Say("the type {0} is reachable from this plugin", Contract);

        int returned = _services.GetServices(contract).Count();
        Say("the container returned {0} implementation(s) of it", returned);
    }

    private void Say(string format, params object[] values)
    {
        _logger.LogWarning(
            "{Marker} {Line}",
            Marker,
            string.Format(CultureInfo.InvariantCulture, format, values));
    }
}

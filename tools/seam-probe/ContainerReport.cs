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
/// against the host alone. That was a convenience while the shape was open and it is the shape
/// itself since 2026-08-28: #117 took the handover by name through reflection, so what this class
/// does IS what a sibling does, and the measurement stops being an analogy for the seam.
/// </para>
/// <para>
/// It goes as far as a sibling goes. Finding the type and being handed an implementation says the
/// lookup works; it says nothing about whether the call can be made across the boundary, and a
/// lookup that returns an object nobody can invoke is not a seam. So the member is called, with a
/// want built by reflection out of the other plugin's own types, and the answer is part of the
/// verdict.
/// </para>
/// </summary>
public sealed class ContainerReport : IHostedService
{
    /// <summary>
    /// The string a run greps the server's log for. Every line the probe writes carries it.
    /// </summary>
    public const string Marker = "SEAM-PROBE";

    /// <summary>
    /// The word that opens the one line a run reads as the answer. The lines around it are prose for
    /// a person; this one is the verdict, in fields, so that reading it is not parsing sentences that
    /// were written to be read rather than matched. `scripts/read-seam-probe-answer.sh` is what reads
    /// it, and it refuses a log that carries no such line at all.
    /// </summary>
    public const string Result = "result";

    private const string OtherAssembly = "Jellyfin.Plugin.Requests";
    private const string Contract = "Jellyfin.Plugin.Requests.Seam.IWantHandover";
    private const string Member = "AcceptAsync";
    private const string WantType = "Jellyfin.Plugin.Requests.Seam.HandedOverWant";
    private const string ImplementationType = "Jellyfin.Plugin.Requests.Seam.WantHandover";
    private const string VersionField = "KnownContractVersion";

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
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await LookAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            // A probe that throws takes the server down with it, and a server that did not start
            // answers a different question from the one being asked.
            Say("the probe itself failed: {0}: {1}", error.GetType().FullName ?? "?", error.Message);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void Set(Type wanted, object want, string property, object value)
        => (wanted.GetProperty(property)
            ?? throw new InvalidOperationException("the want type declares no " + property)).SetValue(want, value);

    private async Task LookAsync()
    {
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => string.Equals(assembly.GetName().Name, OtherAssembly, StringComparison.Ordinal))
            .ToArray();

        Say("assemblies loaded under the name {0}: {1}", OtherAssembly, loaded.Length);
        foreach (Assembly assembly in loaded)
        {
            Say("one of them is at {0}", string.IsNullOrEmpty(assembly.Location) ? "(no location)" : assembly.Location);
        }

        Assembly? holder = null;
        Type? contract = null;
        foreach (Assembly assembly in loaded)
        {
            contract = assembly.GetType(Contract, throwOnError: false);
            if (contract is not null)
            {
                holder = assembly;
                break;
            }
        }

        if (contract is null || holder is null)
        {
            Say("the type {0} is not reachable from this plugin", Contract);
            Answer(loaded.Length, reachable: false, implementations: 0, call: "notattempted");
            return;
        }

        Say("the type {0} is reachable from this plugin", Contract);

        object[] implementations = _services.GetServices(contract)
            .Where(one => one is not null)
            .Select(one => one!)
            .ToArray();

        Say("the container returned {0} implementation(s) of it", implementations.Length);

        if (implementations.Length == 0)
        {
            Answer(loaded.Length, reachable: true, implementations: 0, call: "notattempted");
            return;
        }

        string call = await CallAsync(holder, contract, implementations[0]).ConfigureAwait(false);
        Answer(loaded.Length, reachable: true, implementations: implementations.Length, call: call);
    }

    /// <summary>
    /// Makes the call a sibling makes, over the boundary, with nothing of the other plugin compiled
    /// in.
    /// <para>
    /// The want is deliberately one this side refuses: it names no user, so the implementation runs
    /// its own path and answers without writing anything into the queue of a server the probe does
    /// not own. What is being measured is that the call crossed and came back with the answer the
    /// contract says it carries, not that a request was made.
    /// </para>
    /// </summary>
    /// <param name="holder">The assembly declaring the seam.</param>
    /// <param name="contract">The seam type.</param>
    /// <param name="implementation">What the container handed back for it.</param>
    /// <returns>The word the verdict carries.</returns>
    private async Task<string> CallAsync(Assembly holder, Type contract, object implementation)
    {
        MethodInfo? member = contract.GetMethod(Member);
        if (member is null)
        {
            Say("the seam type declares no member named {0}, so there is nothing to call", Member);
            return "nomember";
        }

        Say("the member is {0}", member.ToString() ?? Member);

        object want;
        try
        {
            want = BuildWant(holder);
        }
        catch (Exception error)
        {
            Say(
                "a want could not be built out of {0}: {1}: {2}",
                WantType,
                error.GetType().FullName ?? "?",
                error.Message);
            return "nowant";
        }

        try
        {
            object? returned = member.Invoke(implementation, [want, CancellationToken.None]);
            if (returned is not Task answering)
            {
                Say("the member did not return a task, so the shape that shipped is not the one the contract names");
                return "failed";
            }

            await answering.ConfigureAwait(false);

            object? answer = answering.GetType().GetProperty("Result")?.GetValue(answering);
            Say("the call crossed the boundary and answered {0}", answer ?? "(nothing)");
            return answer is bool ? "answered" : "failed";
        }
        catch (Exception error)
        {
            Say(
                "the call did not come back: {0}: {1}",
                error.GetType().FullName ?? "?",
                (error as TargetInvocationException)?.InnerException?.Message ?? error.Message);
            return "failed";
        }
    }

    /// <summary>
    /// One want, built the way a sibling with no compile-time reference has to build one.
    /// <para>
    /// The version is read out of the other plugin rather than typed here. A number typed into this
    /// file would be this probe's opinion of the seam version, and what is worth measuring is
    /// whether a sibling can find out what the version is at all.
    /// </para>
    /// </summary>
    /// <param name="holder">The assembly declaring the seam.</param>
    /// <returns>The want to hand across.</returns>
    private object BuildWant(Assembly holder)
    {
        Type wanted = holder.GetType(WantType, throwOnError: true)
            ?? throw new InvalidOperationException("the want type is not declared by " + OtherAssembly);

        int version = 0;
        FieldInfo? declared = holder.GetType(ImplementationType, throwOnError: false)
            ?.GetField(VersionField, BindingFlags.Public | BindingFlags.Static);
        if (declared?.GetRawConstantValue() is int found)
        {
            version = found;
            Say(
                "the seam version this side declares, read out of {0}.{1}: {2}",
                ImplementationType,
                VersionField,
                version);
        }
        else
        {
            Say("no seam version could be read out of {0}.{1}", ImplementationType, VersionField);
        }

        object want = Activator.CreateInstance(wanted)
            ?? throw new InvalidOperationException("the want type could not be constructed");

        Set(wanted, want, "ContractVersion", version);
        Set(wanted, want, "WantId", Guid.NewGuid());
        Set(wanted, want, "RequestedByUserId", Guid.Empty);
        Set(wanted, want, "Title", "what a second plugin can see");

        PropertyInfo kind = wanted.GetProperty("Kind")
            ?? throw new InvalidOperationException("the want type declares no Kind");
        kind.SetValue(want, Enum.ToObject(Nullable.GetUnderlyingType(kind.PropertyType) ?? kind.PropertyType, 0));

        return want;
    }

    /// <summary>
    /// The verdict, written once, in fields rather than in a sentence.
    /// <para>
    /// It is emitted on every path that reached an answer, including the one where the type was not
    /// found, because "the type is missing" is an answer and only a probe that never ran is silence.
    /// A run that produced no line of this shape is refused as having measured nothing.
    /// </para>
    /// </summary>
    /// <param name="assemblies">How many assemblies of the named simple name were loaded.</param>
    /// <param name="reachable">Whether the contract type resolved out of one of them.</param>
    /// <param name="implementations">How many implementations the container returned for it.</param>
    /// <param name="call">What became of the call across the boundary.</param>
    private void Answer(int assemblies, bool reachable, int implementations, string call)
    {
        Say(
            "{0} assemblies={1} contract={2} implementations={3} call={4}",
            Result,
            assemblies,
            reachable ? "reachable" : "missing",
            implementations,
            call);
    }

    private void Say(string format, params object[] values)
    {
        _logger.LogWarning(
            "{Marker} {Line}",
            Marker,
            string.Format(CultureInfo.InvariantCulture, format, values));
    }
}

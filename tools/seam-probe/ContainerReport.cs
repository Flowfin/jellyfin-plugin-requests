using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Seam;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SeamProbe;

/// <summary>
/// Asks the running server what a second plugin can see of the first one, and writes the answer
/// where a run can read it.
/// <para>
/// IT ASKS TWICE, AND THE TWO QUESTIONS ARE DIFFERENT ONES. The first names the contract assembly
/// and the contract type by string and references neither, which is what lets a negative be an
/// answer instead of a crash: if the two plugins do not share a load context, an assembly reference
/// would fail before anything could be reported, and a probe that cannot run is a probe that says
/// nothing. The second is a compile-time reference to the contract, which is the shape a sibling
/// actually ships in and is what the third condition of #117 asks about. It is made inside a method
/// of its own, never inlined and wrapped in a catch, so a runtime that refuses to bind it produces a
/// reported answer rather than a plugin that failed to start.
/// </para>
/// <para>
/// WHAT THE PROBE SHIPS IS THE VARIABLE, and it is what each run of this measures. It carried
/// ExcludeAssets=runtime and Private=false while the arrangement under test was one shipped copy,
/// and a run on both claimed lines on 2026-08-27 found that a plugin shipping no copy cannot resolve
/// the assembly out of another plugin's directory at all - the type is visible to reflection and the
/// reference is unresolvable, at the same time. It now ships its own copy, which is what a package
/// reference does by default, so what is being measured is whether the host merges two copies into
/// one type or leaves two. docs/seam.md carries both answers and what each one decides.
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

    // THE ASSEMBLY THAT DECLARES THE CONTRACT, WHICH IS NO LONGER THE PLUGIN ASSEMBLY. The shape
    // #117 chose is a contract-only assembly shipped once, so the count that matters is how many
    // assemblies of THIS name the process loaded: two of them is the failure the seam exists
    // against, and one is the arrangement working.
    private const string OtherAssembly = "Jellyfin.Plugin.Requests.Contract";
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
            Answer(loaded.Length, reachable: false, implementations: 0);
            return;
        }

        Say("the type {0} is reachable from this plugin", Contract);

        int returned = _services.GetServices(contract).Count();
        Say("the container returned {0} implementation(s) of it", returned);

        // The second question, and the one the third condition of #117 is about. Everything above
        // found the type by name; this asks the container for the type this assembly was COMPILED
        // against, which is what a sibling resolving the contract package does.
        BoundLookup bound = AskAsASiblingWould();
        if (bound.Bound)
        {
            Say(
                "the compile-time reference to {0} bound and the container returned {1} implementation(s) for it",
                Contract,
                bound.Implementations);
            Say(
                "the type it bound to and the type found by name are {0}",
                ReferenceEquals(bound.BoundType, contract) ? "one type" : "two different types");
        }
        else
        {
            Say(
                "the compile-time reference to {0} did not bind: {1}: {2}",
                Contract,
                bound.FailureType,
                bound.FailureMessage);
        }

        Answer(
            loaded.Length,
            reachable: true,
            implementations: returned,
            bound: bound.Bound,
            boundImplementations: bound.Implementations,
            sameType: bound.Bound && ReferenceEquals(bound.BoundType, contract));
    }

    /// <summary>
    /// The lookup a sibling makes: the contract type named at compile time, asked of the container.
    /// <para>
    /// NoInlining is what makes this reportable rather than fatal. A runtime resolves the types a
    /// method body names when that method is first prepared, so a reference it cannot bind throws at
    /// the call site of this method rather than somewhere the caller cannot catch it. In a method of
    /// its own, behind a catch, a reference that did not bind is an answer this probe can write down.
    /// Inlined into its caller it would take the caller's own preparation with it, and the probe
    /// would report nothing at all.
    /// </para>
    /// </summary>
    /// <returns>What the compile-time reference did.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private BoundLookup AskAsASiblingWould()
    {
        try
        {
            return Bind();
        }
        catch (TypeLoadException failure)
        {
            return BoundLookup.Failed(failure);
        }
        catch (FileNotFoundException failure)
        {
            return BoundLookup.Failed(failure);
        }
        catch (FileLoadException failure)
        {
            return BoundLookup.Failed(failure);
        }
        catch (BadImageFormatException failure)
        {
            return BoundLookup.Failed(failure);
        }
        catch (MissingMemberException failure)
        {
            return BoundLookup.Failed(failure);
        }
    }

    /// <summary>
    /// The one method whose body names the contract type. Everything the runtime has to resolve for
    /// this lookup is resolved when this method is prepared, which is what lets its caller catch a
    /// failure to bind instead of dying of it.
    /// </summary>
    /// <returns>What the container returned for the compile-time type.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private BoundLookup Bind()
        => new BoundLookup(
            true,
            _services.GetServices<IWantHandover>().Count(),
            typeof(IWantHandover),
            string.Empty,
            string.Empty);

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
    /// <param name="bound">Whether the compile-time reference to the contract bound at runtime.</param>
    /// <param name="boundImplementations">What the container returned for the bound type.</param>
    /// <param name="sameType">Whether the bound type is the type the name lookup found.</param>
    private void Answer(
        int assemblies,
        bool reachable,
        int implementations,
        bool bound = false,
        int boundImplementations = 0,
        bool sameType = false)
    {
        Say(
            "{0} assemblies={1} contract={2} implementations={3} binding={4} bound-implementations={5} same-type={6}",
            Result,
            assemblies,
            reachable ? "reachable" : "missing",
            implementations,
            bound ? "bound" : "unbound",
            boundImplementations,
            sameType ? "yes" : "no");
    }

    private void Say(string format, params object[] values)
    {
        _logger.LogWarning(
            "{Marker} {Line}",
            Marker,
            string.Format(CultureInfo.InvariantCulture, format, values));
    }

    /// <summary>
    /// What the compile-time reference did, carried out of the catch rather than written from inside
    /// it, so the verdict is composed in one place.
    /// </summary>
    /// <param name="Bound">Whether the reference bound at all.</param>
    /// <param name="Implementations">What the container returned for the bound type.</param>
    /// <param name="BoundType">The type the reference bound to, where it bound.</param>
    /// <param name="FailureType">The failure type name, where it did not.</param>
    /// <param name="FailureMessage">The failure message, where it did not.</param>
    private sealed record BoundLookup(
        bool Bound,
        int Implementations,
        Type? BoundType,
        string FailureType,
        string FailureMessage)
    {
        /// <summary>
        /// A reference the runtime refused to bind, which is an answer rather than a crash.
        /// </summary>
        /// <param name="failure">What the runtime raised.</param>
        /// <returns>The answer.</returns>
        public static BoundLookup Failed(Exception failure)
            => new BoundLookup(false, 0, null, failure.GetType().FullName ?? "?", failure.Message);
    }
}

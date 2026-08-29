using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Seam;

/// <summary>
/// The one line this plugin writes at startup about the seam, so that a server with no sibling on it
/// and a server whose sibling is naming something else do not read the same.
/// <para>
/// <b>This is the price of #117's third option, paid where the operator is.</b> The handover is
/// taken by name through reflection, so a sibling that names a type this side does not declare gets
/// nothing back from the container - and a server with no sibling installed gets nothing back
/// either, because nobody asked. Under a compile-time contract those two states differ at build
/// time. Under this one they are the same silence at runtime, and #117's fourth condition is that an
/// operator can tell them apart anyway.
/// </para>
/// <para>
/// <b>What separates them is what this line says, not a guess about what went wrong.</b> It reports
/// whether another Jellyfin plugin is loaded in this process at all, and it prints the four names a
/// sibling has to get right. On a server with no sibling the line says so and there is nothing to
/// expect. On a server that has one and is getting nothing, the line is where the operator reads the
/// exact strings to compare against what the other plugin is asking for. Neither sentence claims a
/// mismatch was detected: nothing on this side can see what the sibling asked for, that limit is
/// stated in <c>docs/seam.md</c>, and a line that claimed more would be worth less.
/// </para>
/// <para>
/// <b>It is written once, at startup, at information level.</b> A line per handover would be noise
/// on a working server and would say nothing at all on the server this exists for, which is the one
/// where no handover ever arrives.
/// </para>
/// </summary>
public sealed class SeamAnnouncement : IHostedService
{
    /// <summary>
    /// The prefix every Jellyfin plugin assembly carries, which is how one is told from the rest of
    /// a server process without a list of plugin names nobody could keep.
    /// </summary>
    public const string PluginPrefix = "Jellyfin.Plugin.";

    private readonly ILogger _logger;
    private readonly Func<IEnumerable<string>> _loaded;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeamAnnouncement"/> class.
    /// </summary>
    /// <param name="logger">The server's log, which is where an operator reads this.</param>
    /// <exception cref="ArgumentNullException">Where the log is missing.</exception>
    public SeamAnnouncement(ILogger<SeamAnnouncement> logger)
        : this(logger, () => AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name ?? string.Empty))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SeamAnnouncement"/> class over a named set of
    /// loaded assemblies.
    /// <para>
    /// The set is a parameter so that both states can be produced without installing a plugin. A
    /// suite that could only ever observe the state it happens to run in would prove the sentence it
    /// already gets and nothing about the other one, and the other one is the whole reason this
    /// class exists.
    /// </para>
    /// </summary>
    /// <param name="logger">The server's log.</param>
    /// <param name="loaded">What is loaded in this process, by assembly name.</param>
    /// <exception cref="ArgumentNullException">Where anything it needs is missing.</exception>
    public SeamAnnouncement(ILogger logger, Func<IEnumerable<string>> loaded)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(loaded);

        _logger = logger;
        _loaded = loaded;
    }

    /// <summary>
    /// What this side offers, in the words an operator compares against the other plugin.
    /// <para>
    /// Composed rather than logged directly so the sentence itself can be read by a test. Every name
    /// in it comes from <see cref="SeamSurface"/>, which reads them off the types, so a rename moves
    /// this line rather than leaving it describing a seam that is no longer there.
    /// </para>
    /// </summary>
    /// <returns>The offer.</returns>
    public static string Offered()
        => string.Format(
            CultureInfo.InvariantCulture,
            "This plugin registers the want handover as {0} from assembly {1}, member {2}, want type {3}, seam version {4}. A plugin handing a want across names all of those as strings; there is no shared assembly and nothing checks them until the call is made.",
            SeamSurface.TypeName,
            SeamSurface.AssemblyName,
            SeamSurface.MemberName,
            SeamSurface.WantTypeName,
            SeamSurface.Version);

    /// <summary>
    /// The whole line, for the process it is given.
    /// </summary>
    /// <param name="loadedAssemblyNames">What is loaded, by assembly name.</param>
    /// <returns>The sentence written to the log.</returns>
    /// <exception cref="ArgumentNullException">Where the set is missing.</exception>
    public static string Compose(IEnumerable<string> loadedAssemblyNames)
    {
        ArgumentNullException.ThrowIfNull(loadedAssemblyNames);

        string[] siblings = [.. loadedAssemblyNames
            .Where(name => name.StartsWith(PluginPrefix, StringComparison.Ordinal))
            .Where(name => !name.StartsWith(SeamSurface.AssemblyName, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)];

        if (siblings.Length == 0)
        {
            return Offered()
                + " No other Jellyfin plugin is loaded on this server, so nothing here is expected to hand one across and no handover arriving is the ordinary state.";
        }

        return Offered()
            + string.Format(
                CultureInfo.InvariantCulture,
                " Other Jellyfin plugins are loaded on this server: {0}. Whether any of them hands wants across, and what names it asks for, cannot be read from this side. If wants are not arriving from one that should be sending them, compare the names above against what it asks for: under this seam a name that does not match is answered with nothing rather than with an error.",
                string.Join(", ", siblings));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Guarded rather than composed unconditionally. The sentence walks every loaded assembly
        // name, and a server that has turned this level off should not pay for a line nobody will
        // read; CA1873 refuses the unguarded form for exactly that reason.
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("{Seam}", Compose(_loaded()));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

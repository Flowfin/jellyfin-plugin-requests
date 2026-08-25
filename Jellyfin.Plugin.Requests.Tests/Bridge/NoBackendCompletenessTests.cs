using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Requests.Bridge;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Bridge;

/// <summary>
/// The claim on the front of this board is that the whole thing works with or without an external
/// service behind it. A claim like that decays quietly: something gets built assuming a bridge, and
/// the install with none loses it without anybody noticing.
/// <para>
/// What makes it refusable is that a feature needing a bridge is a feature that <b>takes</b> the
/// bridge, and taking it is a fact about a type that can be read off the assembly. So the register
/// in <c>docs/bridge.md</c> is over what touches <see cref="IRequestBackend"/>, and the column
/// beside each entry is where the judgement a machine cannot make is written down for a reader.
/// </para>
/// <para>
/// The expected register below is written out by hand as well, because a comparison between the
/// document and the assembly alone would pass the day both move together and nobody argued about
/// the entry that was added.
/// </para>
/// </summary>
public class NoBackendCompletenessTests
{
    /// <summary>
    /// Everything in the plugin that touches the bridge. Each is a place that has to keep working
    /// on a server with no external service.
    /// </summary>
    private static readonly string[] Expected =
    [
        "BridgeReconciliation",
        "BridgeSubmission",
        "CapabilitiesController",
        "HealthController"
    ];

    /// <summary>
    /// Everything that takes the bridge is named in the register, and nothing is named there that
    /// does not take it.
    /// <para>
    /// Both directions. An unnamed type is the feature this issue exists against, arriving without
    /// anybody saying what it does on a server with no service. A named type that no longer takes
    /// the bridge is a register that has started describing something else, and the reader who
    /// trusts it is the operator deciding whether they need a service at all.
    /// </para>
    /// </summary>
    [Fact]
    public void EverythingThatTakesTheBridgeIsInTheRegister()
    {
        Assert.Equal(
            string.Join(" | ", Expected.OrderBy(name => name, StringComparer.Ordinal)),
            string.Join(" | ", Documented()),
            StringComparer.Ordinal);

        Assert.Equal(
            string.Join(" | ", Expected.OrderBy(name => name, StringComparer.Ordinal)),
            string.Join(" | ", TakesTheBridge()),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Every entry says what it does without a bridge. An entry with an empty column is a name in a
    /// table, and the whole value of the register is the sentence beside the name.
    /// </summary>
    [Fact]
    public void EveryEntrySaysWhatItDoesWithoutOne()
    {
        var silent = Register()
            .Where(entry => entry.Value.Length == 0)
            .Select(entry => entry.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], silent);
    }

    /// <summary>
    /// One implementation of the bridge ships, and it is the one for a server that has none. That is
    /// the other half of the same claim: a second implementation in this assembly would be something
    /// an install could resolve instead, and the register above says nothing about which one a
    /// server got.
    /// </summary>
    [Fact]
    public void TheOnlyBridgeThisPluginShipsIsTheOneWithNothingBehindIt()
    {
        var implementations = typeof(IRequestBackend).Assembly
            .GetTypes()
            .Where(type => typeof(IRequestBackend).IsAssignableFrom(type))
            .Where(type => !type.IsAbstract && !type.IsInterface)
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(NoRequestBackend)], implementations);
    }

    /// <summary>
    /// Every type in the plugin that takes the bridge, read off the assembly. Constructors, fields
    /// and properties, because those are the three ways a type holds one; the interface and its
    /// implementations are not included, since a bridge is not a thing that needs a bridge.
    /// </summary>
    /// <returns>The type names, in name order.</returns>
    private static string[] TakesTheBridge()
        => [.. typeof(IRequestBackend).Assembly
            .GetTypes()
            .Where(type => !typeof(IRequestBackend).IsAssignableFrom(type))
            .Where(Holds)
            .Select(type => type.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)];

    /// <summary>
    /// Whether one type takes the bridge in any of the three ways a type can hold one.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns><see langword="true"/> where it does.</returns>
    private static bool Holds(Type type)
    {
        const BindingFlags Everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        return type.GetConstructors(Everything)
                .SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => parameter.ParameterType == typeof(IRequestBackend))
            || type.GetFields(Everything).Any(field => field.FieldType == typeof(IRequestBackend))
            || type.GetProperties(Everything).Any(property => property.PropertyType == typeof(IRequestBackend));
    }

    /// <summary>
    /// The names the register holds, in the order the table lists them.
    /// </summary>
    /// <returns>The names.</returns>
    private static string[] Documented() => [.. Register().Keys];

    /// <summary>
    /// The register in <c>docs/bridge.md</c>: what touches the bridge, and what it does without one.
    /// </summary>
    /// <returns>The entries, in the table's own order.</returns>
    private static Dictionary<string, string> Register()
    {
        var rows = MarkedSection("needs-a-bridge").Where(line => line.StartsWith('|')).ToArray();

        // The header names the columns and the dashes under it are the table's own furniture.
        return rows
            .Skip(2)
            .Select(SplitRow)
            .ToDictionary(row => row[0].Trim('`'), row => row[1], StringComparer.Ordinal);
    }

    private static string[] SplitRow(string row)
        => [.. row.Trim('|').Split('|').Select(cell => cell.Trim())];

    /// <summary>
    /// Reads the lines the document marks off for one section.
    /// </summary>
    /// <param name="name">The name in the marker comments.</param>
    /// <returns>The trimmed lines between the markers.</returns>
    private static string[] MarkedSection(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "docs", "bridge.md");
        Assert.True(File.Exists(path), FormattableString.Invariant($"{path} was not copied next to the suite."));

        var opening = FormattableString.Invariant($"<!-- {name} begins -->");
        var closing = FormattableString.Invariant($"<!-- {name} ends -->");
        var inside = false;
        var lines = new List<string>();

        foreach (var line in File.ReadLines(path))
        {
            if (line.Trim().Equals(opening, StringComparison.Ordinal))
            {
                inside = true;
                continue;
            }

            if (line.Trim().Equals(closing, StringComparison.Ordinal))
            {
                inside = false;
                continue;
            }

            if (inside && line.Trim().Length > 0)
            {
                lines.Add(line.Trim());
            }
        }

        Assert.True(lines.Count > 0, FormattableString.Invariant($"docs/bridge.md has no {name} section."));

        return [.. lines];
    }
}

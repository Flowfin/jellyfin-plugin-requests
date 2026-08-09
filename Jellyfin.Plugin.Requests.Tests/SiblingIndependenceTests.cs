using System;
using System.Linq;
using System.Reflection;
using Xunit;

using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests;

/// <summary>
/// What this plugin is allowed to depend on.
/// <para>
/// Two plugins that install separately and reference each other at build time fail on the first
/// server that installs one of them. This board carries the harder side of that, because
/// implementing somebody else's interface means having the type, and the moment a sibling assembly
/// is referenced the package stops being installable on its own.
/// </para>
/// <para>
/// The package is one assembly and a metadata file, so the assembly's own reference list is the
/// package's dependency list. That is what is read here.
/// </para>
/// </summary>
public class SiblingIndependenceTests
{
    /// <summary>
    /// Every assembly the plugin references, written out. An addition fails this test, which is the
    /// whole point: a reference arrives by somebody writing a `using`, and nothing else in the tree
    /// would say so. Adding a line here is the deliberate act, and the commit that adds it is where
    /// the reason lives.
    /// <para>
    /// The framework assemblies are in the list rather than filtered out by a name prefix. A filter
    /// would have to decide what counts as the framework, and the shape of that mistake is a filter
    /// wide enough to let a sibling through under a name nobody predicted.
    /// </para>
    /// </summary>
    private static readonly string[] AllowedReferences =
    [
        "MediaBrowser.Common",
        "MediaBrowser.Controller",
        "MediaBrowser.Model",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "System.Collections",
        "System.Linq",
        "System.Runtime",

        // The store keeps requests as JSON, so the serialiser the framework already ships is what
        // reads and writes the file. It is part of the runtime the server provides on both claimed
        // lines rather than a package this plugin carries, and choosing a serialiser from outside
        // would have been a package in the install and a second thing to hold at a version.
        "System.Text.Json",

        // The store's calls are asynchronous and it holds one lock across a write, so the
        // cancellation token, the task and the lock all come from here. It arrives with the store
        // rather than with any decision of its own.
        "System.Threading"
    ];

    /// <summary>
    /// The plugin references exactly the assemblies written above and nothing else. This is the
    /// second condition on #94: the set is written down, and an addition fails until somebody adds
    /// it here.
    /// </summary>
    [Fact]
    public void ThePluginReferencesExactlyTheAssembliesWrittenDown()
    {
        // Joined rather than compared as two sequences, because a collection failure prints the
        // difference with the middle elided and the whole list is what somebody repairing this
        // needs to read.
        Assert.Equal(
            string.Join(" | ", AllowedReferences),
            string.Join(" | ", ReferencedAssemblyNames()),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// No assembly the plugin references is another Jellyfin plugin. The exact list above would
    /// already catch this and would catch it as "an unexpected reference", saying nothing about why
    /// the reference is unwelcome. This one names the reason, and it also refuses the case where
    /// somebody adds the reference to the list and to the project in one change.
    /// <para>
    /// The sibling discover plugin is the one this board is about, and the check is over the shape
    /// rather than over that one name, because a second sibling would arrive under a third.
    /// </para>
    /// </summary>
    [Fact]
    public void NothingThePluginReferencesIsAnotherJellyfinPlugin()
    {
        var siblings = ReferencedAssemblyNames()
            .Where(name => name.StartsWith("Jellyfin.Plugin.", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(siblings);
    }

    /// <summary>
    /// The suite runs with no sibling plugin loaded, which is its ordinary state and is the third
    /// condition on #94. Written as an assertion rather than left as a fact about the day somebody
    /// looked: a test project that grows a package reference to a sibling in order to test the seam
    /// is exactly how the ordinary state stops being ordinary, and it would fail here.
    /// </summary>
    [Fact]
    public void NoSiblingPluginIsLoadedWhileTheSuiteRuns()
    {
        var siblings = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetName().Name ?? string.Empty)
            .Where(name => name.StartsWith("Jellyfin.Plugin.", StringComparison.Ordinal))
            .Where(name => !name.StartsWith("Jellyfin.Plugin.Requests", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(siblings);
    }

    /// <summary>
    /// The assemblies the built plugin references, by name, sorted.
    /// <para>
    /// One name is dropped. `netstandard` is a type-forwarding facade that the Release build carries
    /// and the Debug build does not, measured on this tree: the same command against the two
    /// configurations differs by that name and by nothing else. Keeping it would make the list
    /// depend on which configuration ran, and the list has to be one list, because the whole value
    /// of the comparison is that it is exact. The facade contains no code and forwards to the
    /// framework, so it cannot be a sibling plugin arriving under another name.
    /// </para>
    /// </summary>
    /// <returns>The reference names.</returns>
    private static string[] ReferencedAssemblyNames()
        => [.. typeof(PluginUnderTest).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !string.Equals(name, "netstandard", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)];
}

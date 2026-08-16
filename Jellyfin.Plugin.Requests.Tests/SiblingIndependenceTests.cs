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
        // The library query the fulfilment check makes names the kinds of item it wants back, and
        // that enumeration lives here rather than beside the library interface it is passed to. It
        // arrives with the server's own libraries and is excluded from the install with them.
        "Jellyfin.Data",

        // The seam refuses a handover naming somebody this server does not have, in #118, and the
        // only member of the server's user manager that answers on both claimed lines hands back the
        // user record. Nothing here reads that record: the answer is thrown away and its presence is
        // the whole result. The reference is the price of asking, it arrives with the server's own
        // libraries, and it is excluded from the install with them.
        "Jellyfin.Database.Implementations",
        "MediaBrowser.Common",
        "MediaBrowser.Controller",
        "MediaBrowser.Model",

        // The API this plugin serves is mounted on the server's own, so its controllers are the
        // server's web framework's controllers. These three are that framework, split across
        // assemblies by the framework rather than by anything this plugin chose: the policy
        // attribute, the request context an endpoint reads its caller from, and `ControllerBase`
        // with the routing attributes and the result types. All three are part of the
        // runtime the server already runs on rather than a package this plugin carries, because the
        // project excludes the runtime assets and nothing of them lands in the install. Serving an
        // API without them would mean a second web stack inside a plugin.
        "Microsoft.AspNetCore.Authorization",
        "Microsoft.AspNetCore.Http.Abstractions",
        "Microsoft.AspNetCore.Mvc.Core",
        "Microsoft.Extensions.DependencyInjection.Abstractions",

        // The thing that listens for a library arrival has to be running before the first one rather
        // than when somebody first asks for it, and a hosted service is how the server starts and
        // stops such a thing. This is the abstraction only; the host is the generic host the server
        // already is, and this assembly is part of the runtime it runs on.
        "Microsoft.Extensions.Hosting.Abstractions",

        // The store refuses to open a file it cannot read, and the caller that sees the exception is
        // whichever request happened to be first while the person who can act on it is reading the
        // server's log. Taking the server's own logger is what puts the refusal there. It is the
        // logging abstraction rather than an implementation: the server supplies the implementation,
        // and this assembly is part of the runtime it already runs on.
        "Microsoft.Extensions.Logging.Abstractions",
        "System.Collections",

        // The endpoint declares which status codes it answers with, and the attribute that says so
        // is a description of the method rather than behaviour. It arrives with the controller and
        // is part of the same runtime.
        "System.ComponentModel",
        "System.Linq",

        // A title snapshot is cut to a line before it goes into an activity entry, and the cut is
        // made over a span rather than by taking a substring because CA1846 refuses the substring
        // and the analyzer posture in Directory.Build.props makes that refusal a build failure. The
        // span type lives here. It is a facade over the runtime the server already runs on rather
        // than a package this plugin carries, and it is excluded from the install with the rest.
        "System.Memory",

        // The outbound notification sink posts a document to an address an operator typed, so the
        // client, the pipeline its handler sits in and the address type all come from here. It is
        // the one thing in this plugin that talks outward, it is off on every install where nobody
        // typed an address, and these three assemblies are part of the runtime the server already
        // runs on rather than a package this plugin carries.
        "System.Net.Http",
        "System.Net.Http.Json",
        "System.Net.Primitives",
        "System.Runtime",

        // The store keeps requests as JSON, so the serialiser the framework already ships is what
        // reads and writes the file. It is part of the runtime the server provides on both claimed
        // lines rather than a package this plugin carries, and choosing a serialiser from outside
        // would have been a package in the install and a second thing to hold at a version.
        "System.Text.Json",

        // The store's calls are asynchronous and it holds one lock across a write, so the
        // cancellation token, the task and the lock all come from here. It arrives with the store
        // rather than with any decision of its own.
        "System.Threading",

        // A library event is raised on the thread that is scanning the library, so what handles it
        // puts it in a queue and returns rather than holding that scan behind a store read and a
        // store write per item. The queue is the runtime's own rather than a list and a lock written
        // here, and it is part of the runtime the server already runs on.
        "System.Threading.Channels"
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

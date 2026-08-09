using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Requests.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Api;

/// <summary>
/// Where this plugin's API sits, held by the suite rather than by the document that argues it.
/// <para>
/// The invariant lint holds the same rule over the source text, and the two halves are not the same
/// check. The lint reads files under a path list and cannot see a controller written somewhere that
/// list does not reach; this reads the built assembly and cannot see a route that is correct in a
/// file nobody compiled. Together they cover the case each one misses alone.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class RequestsApiLayoutTests
{
    /// <summary>
    /// The prefix carries the version segment, and the two are not two strings that happen to agree
    /// today. A path and a version that can be edited apart is how an endpoint ends up answering
    /// under a number that no longer describes what it returns.
    /// </summary>
    [Fact]
    public void ThePrefixCarriesTheVersionSegment()
    {
        Assert.Equal("v1", RequestsControllerBase.VersionSegment, StringComparer.Ordinal);
        Assert.Equal("MediaRequests/v1", RequestsControllerBase.RoutePrefix, StringComparer.Ordinal);
        Assert.EndsWith(
            "/" + RequestsControllerBase.VersionSegment,
            RequestsControllerBase.RoutePrefix,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The prefix is relative. A template beginning with a slash is an absolute route, and one on the
    /// base class would put every endpoint at the root of the server's API rather than under a prefix
    /// of this plugin's own, which is the failure the whole layout exists to prevent and the one a
    /// reader of the string would not notice.
    /// </summary>
    [Fact]
    public void ThePrefixIsRelativeRatherThanRootedAtTheServersApi()
    {
        Assert.False(
            RequestsControllerBase.RoutePrefix.StartsWith('/'),
            "The route prefix begins with a slash, which makes it an absolute route at the root of the server's API.");
    }

    /// <summary>
    /// The base class declares the prefix, once, as the route every endpoint inherits.
    /// </summary>
    [Fact]
    public void TheBaseClassDeclaresThePrefixAsItsRoute()
    {
        var routes = typeof(RequestsControllerBase)
            .GetCustomAttributes<RouteAttribute>(inherit: false)
            .Select(route => route.Template)
            .ToArray();

        Assert.Equal([RequestsControllerBase.RoutePrefix], routes);
    }

    /// <summary>
    /// Every controller in the plugin derives from the base and declares no route of its own, so
    /// every endpoint sits under the versioned prefix.
    /// <para>
    /// This is the leg that bites when an endpoint is added. It passes over an assembly with no
    /// controller in it yet, which is the state the tree is in the moment the layout lands and
    /// before anything is served, so the assertion below that at least the base class is present is
    /// what stops the whole test from being vacuously green if the API ever disappears.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryControllerInheritsTheVersionedPrefix()
    {
        var controllers = typeof(RequestsControllerBase).Assembly
            .GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .Where(type => !type.IsAbstract)
            .ToArray();

        var outside = controllers
            .Where(type => !typeof(RequestsControllerBase).IsAssignableFrom(type)
                || type.GetCustomAttributes<RouteAttribute>(inherit: false).Any())
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], outside);
        Assert.Contains(typeof(RequestsControllerBase), typeof(RequestsControllerBase).Assembly.GetTypes());
    }

    /// <summary>
    /// No endpoint anywhere in the plugin carries a template that begins with a slash. ASP.NET reads
    /// one as an absolute route and discards the controller's prefix, so such an endpoint answers
    /// outside the version every promise in <c>docs/api.md</c> is written about, while reading in the
    /// source like a relative path with a stray character.
    /// </summary>
    [Fact]
    public void NoEndpointTemplateIsRootedAtTheServersApi()
    {
        var rooted = typeof(RequestsControllerBase).Assembly
            .GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>()
                    .Where(verb => verb.Template is not null && verb.Template.StartsWith('/'))
                    .Select(verb => string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}.{1} -> {2}",
                        type.Name,
                        method.Name,
                        verb.Template))))
            .OrderBy(named => named, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], rooted);
    }
}

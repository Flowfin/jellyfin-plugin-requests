using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Requests.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Api;

/// <summary>
/// Who may reach each endpoint, held against the built assembly.
/// <para>
/// The invariant lint holds two rules over the source text, and they are not this check. They refuse
/// the two ways a policy is taken away: an anonymous endpoint, and an <c>[Authorize]</c> that names
/// no policy. Neither can see an endpoint that simply never carried an attribute, because a rule
/// about text cannot refuse the absence of a line. That is what this reads the assembly for.
/// </para>
/// <para>
/// What no test here can say is what the server does with a policy. The server evaluates it and
/// there is no server in this suite, which the headless rule in <c>docs/testing.md</c> settles and
/// which its refusal list names with the replacement it stands in for. So the promise held here is
/// that every endpoint carries the policy it is meant to carry, and the promise that a caller
/// outside that policy is turned away belongs to the server.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class EndpointPolicyTests
{
    /// <summary>
    /// Every endpoint and the policy it carries, written down.
    /// <para>
    /// An addition fails this until somebody adds a line here, which is the point: the failure a
    /// per-endpoint test cannot catch is the endpoint added later that no test knows about. The
    /// commit that adds the line is where the reason for that policy lives, and <c>docs/api.md</c>
    /// is where a reader finds it.
    /// </para>
    /// </summary>
    private static readonly string[] Expected =
    [
        // Asking for something. An authenticated user, because a request has to be attributable to
        // somebody and a caller with no session names nobody.
        "RequestsController.CreateAsync POST Requests -> DefaultAuthorization",

        // One person's own requests. The same policy, and the narrowing is the read rather than the
        // policy: this endpoint has nothing wider than the caller's own requests to return.
        "RequestsController.MineAsync GET Requests -> DefaultAuthorization",

        // The whole queue, which is every person's requests and who asked for each. An
        // administrator, and it is the only endpoint here that needs one.
        "RequestsController.QueueAsync GET Requests/Queue -> RequiresElevation"
    ];

    /// <summary>
    /// Every endpoint carries a policy of its own, named, and the set is exactly the one written
    /// above.
    /// </summary>
    [Fact]
    public void EveryEndpointCarriesThePolicyWrittenDownForIt()
    {
        // Joined rather than compared as two sequences, because a collection failure prints the
        // difference with the middle elided and the whole list is what somebody repairing this needs
        // to read.
        Assert.Equal(
            string.Join(" | ", Expected),
            string.Join(" | ", Endpoints()),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// No endpoint relies on the attribute the controller carries.
    /// <para>
    /// An endpoint with no policy of its own is reachable under whatever its class happens to
    /// declare on the day it is added, and a class attribute is edited by somebody who is not
    /// reading the endpoint. This is the leg that catches the endpoint added with no attribute at
    /// all, which is what neither the lint nor a per-endpoint refusal test can see.
    /// </para>
    /// </summary>
    [Fact]
    public void NoEndpointInheritsItsPolicyFromTheControllerItSitsOn()
    {
        var inherited = Actions()
            .Where(action => !action.Method.GetCustomAttributes<AuthorizeAttribute>(inherit: false)
                .Any(attribute => !string.IsNullOrWhiteSpace(attribute.Policy)))
            .Select(action => Named(action.Type, action.Method))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], inherited);
    }

    /// <summary>
    /// Nothing is anonymous. The lint refuses the attribute in the source and this refuses it in the
    /// assembly, which is the same rule read two ways: one sees a file the other's path list would
    /// miss, and the other sees an attribute that arrived some way the text rule does not match.
    /// </summary>
    [Fact]
    public void NoEndpointIsReachableWithoutASession()
    {
        var anonymous = Actions()
            .Where(action => action.Method.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any()
                || action.Type.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any())
            .Select(action => Named(action.Type, action.Method))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], anonymous);
    }

    /// <summary>
    /// The controller carries a policy too, so the floor under every endpoint is a signed-in caller
    /// rather than whatever the framework defaults to. The endpoints do not rely on it, which is the
    /// leg above; this is that it is there.
    /// </summary>
    [Fact]
    public void TheControllerItselfCarriesAPolicy()
    {
        var controllers = Controllers()
            .Where(type => !type.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .Any(attribute => !string.IsNullOrWhiteSpace(attribute.Policy)))
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], controllers);
    }

    /// <summary>
    /// Every controller the plugin ships.
    /// </summary>
    /// <returns>The types.</returns>
    private static Type[] Controllers()
        => [.. typeof(RequestsControllerBase).Assembly
            .GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .Where(type => !type.IsAbstract)
            .OrderBy(type => type.Name, StringComparer.Ordinal)];

    /// <summary>
    /// Every action on every controller, which is every method carrying an HTTP verb.
    /// </summary>
    /// <returns>The actions.</returns>
    private static (Type Type, MethodInfo Method)[] Actions()
        => [.. Controllers()
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: false).Any())
                .Select(method => (Type: type, Method: method)))];

    /// <summary>
    /// One endpoint, its verb, its template and its policy, as one line.
    /// </summary>
    /// <returns>The lines, sorted.</returns>
    private static string[] Endpoints()
        => [.. Actions()
            .Select(action => string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} -> {2}",
                Named(action.Type, action.Method),
                Route(action.Method),
                string.Join(
                    "+",
                    action.Method.GetCustomAttributes<AuthorizeAttribute>(inherit: false)
                        .Select(attribute => attribute.Policy ?? "(none)")
                        .OrderBy(policy => policy, StringComparer.Ordinal))))
            .OrderBy(line => line, StringComparer.Ordinal)];

    /// <summary>
    /// The verb and template an action answers under.
    /// </summary>
    /// <param name="method">The action.</param>
    /// <returns>The verb and the template.</returns>
    private static string Route(MethodInfo method)
    {
        var verb = method.GetCustomAttributes<HttpMethodAttribute>(inherit: false).Single();

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1}",
            string.Join(",", verb.HttpMethods),
            verb.Template ?? "(none)");
    }

    /// <summary>
    /// An action named the way a person repairing this test would look for it.
    /// </summary>
    /// <param name="type">The controller.</param>
    /// <param name="method">The action.</param>
    /// <returns>The name.</returns>
    private static string Named(Type type, MethodInfo method)
        => string.Format(CultureInfo.InvariantCulture, "{0}.{1}", type.Name, method.Name);
}

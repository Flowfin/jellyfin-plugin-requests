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
/// an anonymous endpoint, and a policy written as a string rather than taken from the constant the
/// server holds it in. Neither can see an endpoint that simply never carried an attribute, because a
/// rule about text cannot refuse the absence of a line. That is what this reads the assembly for.
/// </para>
/// <para>
/// <b>An endpoint open to any signed-in caller names no policy, and the empty cell below is that
/// rather than an omission.</b> The server registers no name for "any signed-in person" and builds
/// the requirement into its unnamed default policy, so what an endpoint can carry is the default or
/// a name the server registers. This plugin named <c>DefaultAuthorization</c> for it, which the
/// server registers on neither claimed line, and every endpoint answered 500 to every caller;
/// <c>docs/api.md</c> carries the measurement and the repair. So the leg below asks that the
/// attribute be there and that the policy be the one written down, where the default is written down
/// as itself.
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
    /// <para>
    /// The lines are in the order the comparison sorts them, which is by the whole line and so by
    /// the action's name. A line added in the place a reader would put it fails until it is moved,
    /// which is noise rather than a finding, and the alternative is a comparison that cannot say
    /// which endpoint is missing.
    /// </para>
    /// </summary>
    private static readonly string[] Expected =
    [
        // What this install is and what it allows. A signed-in person, because nothing this plugin
        // answers is safe to hand a caller the server has not authenticated, and because the kinds
        // an install accepts is a fact about somebody's server rather than about this version.
        "CapabilitiesController.CapabilitiesAsync GET Capabilities -> (the server's default)",

        // The page a person opens in a browser to see their own requests. A signed-in person, and
        // it is the policy that makes the page a page rather than a shell: a caller the server has
        // not authenticated is turned away instead of being handed the document and left to meet
        // the refusal on its first call. Nothing elevated is reachable from it, because everything
        // it draws comes from the endpoint below that has nothing but the caller's own to return.
        "MyRequestsPageController.Page GET Page -> (the server's default)",

        // Saying yes. A decision is an administrator's, in the transition table and here, and the
        // two answers have to agree: an endpoint reachable by a signed-in user would refuse every
        // such call in the model, which is a permission decided in two places.
        "RequestsController.ApproveAsync POST Requests/{id}/Approve -> RequiresElevation",

        // Saying yes to several at once. The same policy as saying yes to one, because it is the
        // same decision made more times: an action reachable by a signed-in user would be refused
        // once per request in the model, which is a permission decided in two places.
        "RequestsController.ApproveManyAsync POST Requests/Approve -> RequiresElevation",

        // Asking for something. An authenticated user, because a request has to be attributable to
        // somebody and a caller with no session names nobody.
        "RequestsController.CreateAsync POST Requests -> (the server's default)",

        // Saying no. The same policy as an approval, and the decline reason is something a user
        // reads rather than writes.
        "RequestsController.DeclineAsync POST Requests/{id}/Decline -> RequiresElevation",

        // Saying no to several at once, for one reason.
        "RequestsController.DeclineManyAsync POST Requests/Decline -> RequiresElevation",

        // One person's own requests. The same policy as asking, and the narrowing is the read
        // rather than the policy: this endpoint has nothing wider than the caller's own requests to
        // return.
        "RequestsController.MineAsync GET Requests -> (the server's default)",

        // The whole queue, which is every person's requests and who asked for each. An
        // administrator, and it is the only read here that needs one.
        "RequestsController.QueueAsync GET Requests/Queue -> RequiresElevation"
    ];

    /// <summary>
    /// Every endpoint carries a policy of its own and the set is exactly the one written above.
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
    /// An endpoint with no attribute of its own is reachable under whatever its class happens to
    /// declare on the day it is added, and a class attribute is edited by somebody who is not
    /// reading the endpoint. This is the leg that catches the endpoint added with no attribute at
    /// all, which is what neither the lint nor a per-endpoint refusal test can see.
    /// </para>
    /// <para>
    /// It asks for the attribute rather than for a policy name on it, and that is narrower than it
    /// was: an endpoint open to any signed-in caller carries the server's default policy, which is
    /// spelled by naming none. Which policy each one carries is the leg above, against the list, so
    /// nothing moved from being checked to being assumed.
    /// </para>
    /// </summary>
    [Fact]
    public void NoEndpointInheritsItsPolicyFromTheControllerItSitsOn()
    {
        var inherited = Actions()
            .Where(action => !action.Method.GetCustomAttributes<AuthorizeAttribute>(inherit: false).Any())
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
    /// The controller carries the attribute too, so the floor under every endpoint is a signed-in
    /// caller rather than an open route. The endpoints do not rely on it, which is the leg above;
    /// this is that it is there.
    /// </summary>
    [Fact]
    public void TheControllerItselfCarriesAPolicy()
    {
        var controllers = Controllers()
            .Where(type => !type.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any())
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
                        .Select(attribute => attribute.Policy ?? "(the server's default)")
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

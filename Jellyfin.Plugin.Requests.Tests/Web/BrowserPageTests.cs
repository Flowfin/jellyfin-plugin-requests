using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Requests.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests.Web;

/// <summary>
/// The page a person opens in a browser, held to what it may show and what it may reach.
/// <para>
/// The failure this exists against is the one a page of this kind walks into: it is built beside an
/// administrator's queue, out of the same records, and the difference between the two is a decision
/// about which fields are drawn and which endpoint is called. Neither difference is visible in a
/// browser until the wrong person is looking at it.
/// </para>
/// <para>
/// <b>The bound, and it is the same one every check over these assets carries.</b> Nothing here
/// runs a browser and nothing here runs a server, which the headless rule in <c>docs/testing.md</c>
/// settles and whose refusal list names the replacement. So what is held is that the page asks this
/// plugin for one thing, that everything it draws is a field of the answer to that one thing, and
/// that it offers no decision. That a caller with no session is turned away is the server's
/// evaluation of the policy on the endpoint, and what is held here is that the policy is on it.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class BrowserPageTests
{
    /// <summary>
    /// The one endpoint the page is allowed to send to, and the one element a person can operate on
    /// it. Both are the switch on what this plugin pushes at whoever is reading the page, which is
    /// the only kind of control that belongs here: a setting about this person rather than a
    /// decision about a request.
    /// </summary>
    private const string TheOneItSendsTo = "Notices/Mine";

    /// <summary>
    /// The endpoints the page is allowed to name, in the order the assertion compares them.
    /// </summary>
    private static readonly string[] TheThreeItAsksFor = ["Notices/Mine", "Requests", "Strings"];

    /// <summary>
    /// The elements a browser gives a person something to operate. Anything a page offers a
    /// decision through is one of these, which is why the assertion is over them rather than over
    /// the words on the page.
    /// </summary>
    private static readonly string[] Operable = ["<button", "<form", "<input", "<select", "<textarea"];

    /// <summary>
    /// A field of a request as the page names it while drawing a row.
    /// </summary>
    private static readonly Regex Drawn = new Regex(
        @"request\.(?<field>[A-Za-z][A-Za-z0-9]*)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The endpoint answers with the document the assembly carries, as HTML.
    /// <para>
    /// Read out of the manifest rather than off the working tree, because what a server returns is
    /// the embedded copy: a page left out of the packaging builds clean and is a 500 on somebody's
    /// server the first time a user opens it.
    /// </para>
    /// </summary>
    [Fact]
    public void TheEndpointAnswersWithThePageTheAssemblyCarries()
    {
        var answered = new MyRequestsPageController().Page();

        Assert.Equal(MediaTypeNames.Text.Html, answered.ContentType, StringComparer.Ordinal);
        Assert.Equal(200, answered.StatusCode);
        Assert.Equal(Page(), answered.Content, StringComparer.Ordinal);
    }

    /// <summary>
    /// The page is behind the server's own authentication rather than served to anybody.
    /// <para>
    /// A shell handed to a caller with no session and left to fail on its first call puts this
    /// plugin's existence and shape in front of somebody who has not signed in, and reads to a
    /// person as a broken page rather than as a closed door. The attribute is what the server
    /// evaluates, so this reads the attribute; the invariant lint refuses the one attribute that
    /// would turn it off, and this is the half that would still see it if the lint were not run.
    /// </para>
    /// </summary>
    [Fact]
    public void NothingOfThePageIsServedWithoutASession()
    {
        var action = PageAction();

        Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        Assert.Empty(typeof(MyRequestsPageController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));

        var policies = action
            .GetCustomAttributes<AuthorizeAttribute>(inherit: false)
            .Select(authorize => authorize.Policy)
            .ToArray();

        // One attribute, and the policy on it is the server's own default rather than a name. There
        // is no registered name for "any signed-in person", which is what docs/api.md sets out and
        // what the endpoint that shipped a name for it cost.
        Assert.Single(policies);
        Assert.Null(policies[0]);
    }

    /// <summary>
    /// The page asks this plugin for three things and they are its own words, the caller's own
    /// requests, and the caller's own setting about being told.
    /// <para>
    /// The address is built against the page's own, so what is asserted is the relative part and
    /// that there is one place that builds it. A fourth call added to this page is a fourth thing a
    /// user's browser asks for on their behalf, and that is the shape by which an administrator's
    /// answer arrives on a page that was never meant to hold one. The set is written out rather
    /// than counted, so a call swapped for another of the same number fails here too.
    /// </para>
    /// <para>
    /// The words are among them because the page ships with none, which is #73. What that costs is
    /// in the page's own comment: a catalogue that cannot be fetched leaves the page wordless.
    /// </para>
    /// <para>
    /// Two <c>fetch</c> calls rather than one, because reading and sending are two shapes and the
    /// sending one carries a method and a body. One <c>new URL</c> still, which is the thing that
    /// actually matters: every address this page uses is built in the one place that carries the
    /// credential no further than the call.
    /// </para>
    /// </summary>
    [Fact]
    public void ThePageAsksForNothingButItsWordsAndTheCallersOwn()
    {
        var body = Page();

        Assert.Equal(2, Occurrences(body, "fetch("));
        Assert.Equal(1, Occurrences(body, "new URL("));
        Assert.Equal(
            TheThreeItAsksFor,
            Regex.Matches(body, @"(?:fetched|sent)\(""(?<endpoint>[A-Za-z/]+)""")
                .Select(match => match.Groups["endpoint"].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(endpoint => endpoint, StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// Nothing an administrator reaches is named on this page.
    /// <para>
    /// The elevated endpoints are read off the assembly rather than listed here, so an endpoint
    /// added under elevation is covered by this the first time the suite runs rather than when
    /// somebody remembers to extend a list. What it refuses is the route appearing on the page at
    /// all, which is one step in front of the page calling it.
    /// </para>
    /// </summary>
    [Fact]
    public void NoRouteThatNeedsAnAdministratorAppearsOnThePage()
    {
        var body = Page();
        var elevated = Elevated();

        var named = elevated.Where(route => body.Contains(route, StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(elevated);
        Assert.Empty(named);
    }

    /// <summary>
    /// Every field the page draws is one the caller's own answer carries.
    /// <para>
    /// This is the leg that holds the disclosure rather than the call. <c>MyRequest</c> names
    /// nobody, so a page that can only draw its fields cannot draw a person; a row reaching for
    /// something the queue's own shape carries, such as who asked, fails here rather than in a
    /// browser. It also catches the field that is simply misspelled, which draws an empty cell and
    /// looks like a request with nothing in it.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryFieldThePageDrawsIsOneTheCallersOwnAnswerCarries()
    {
        var body = Page();

        var carried = typeof(MyRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        var fields = Drawn
            .Matches(body)
            .Select(found => found.Groups["field"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(field => field, StringComparer.Ordinal)
            .ToArray();

        var outside = fields.Where(field => !carried.Contains(field, StringComparer.Ordinal)).ToArray();

        Assert.NotEmpty(fields);
        Assert.Empty(outside);
    }

    /// <summary>
    /// The page offers no way to decide anything about a request, and exactly one way to set
    /// something about the person reading it.
    /// <para>
    /// A control is how a decision arrives on a page. This one carries a single checkbox, and what
    /// it sets is what this plugin pushes at whoever is signed in; everything else a page like this
    /// could offer is either an administrator's or waits on a state a request can be withdrawn
    /// into, which is an open decision on #113. So the assertion is over the elements a person can
    /// operate rather than over the words, and it reds the moment a second one is added.
    /// </para>
    /// <para>
    /// The second half is the one that matters more, because a control is only as narrow as what it
    /// sends: the page's one sending call goes to the endpoint that takes no identifier, so there
    /// is no address on this page through which anybody's setting but the caller's own could be
    /// changed.
    /// </para>
    /// </summary>
    [Fact]
    public void TheOnlyControlThePageCarriesIsThePersonsOwnSwitch()
    {
        var body = Page();

        var carried = Operable
            .Where(element => body.Contains(element, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal(["<input"], carried);
        Assert.Equal(1, Occurrences(body, "<input"));
        Assert.Equal(1, Occurrences(body, @"method: ""POST"""));
        Assert.Equal(1, Occurrences(body, @"sent(""" + TheOneItSendsTo + @""""));
    }

    /// <summary>
    /// The routes of every endpoint this plugin serves that needs an administrator.
    /// </summary>
    /// <returns>The templates, as they are written on the actions.</returns>
    private static string[] Elevated()
    {
        return [.. typeof(RequestsControllerBase).Assembly
            .GetTypes()
            .Where(type => typeof(RequestsControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<AuthorizeAttribute>(inherit: false)
                .Any(authorize => authorize.Policy is not null))
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: false))
            .Select(http => http.Template ?? string.Empty)
            .Where(template => template.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(template => template, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The action that serves the page.
    /// </summary>
    /// <returns>The method.</returns>
    private static MethodInfo PageAction()
        => typeof(MyRequestsPageController).GetMethod(nameof(MyRequestsPageController.Page))
            ?? throw new InvalidOperationException("The controller that serves the page carries no action for it.");

    /// <summary>
    /// The page as the built assembly carries it.
    /// </summary>
    /// <returns>The document.</returns>
    private static string Page()
    {
        var assembly = typeof(PluginUnderTest).Assembly;
        var name = string.Format(
            CultureInfo.InvariantCulture,
            "{0}.{1}",
            typeof(PluginUnderTest).Namespace,
            MyRequestsPageController.PageResource);

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The assembly carries no resource named {name}.");

        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// How many times one string appears in another.
    /// </summary>
    /// <param name="body">What to look in.</param>
    /// <param name="looked">What to look for.</param>
    /// <returns>The count.</returns>
    private static int Occurrences(string body, string looked)
    {
        var found = 0;
        var at = body.IndexOf(looked, StringComparison.Ordinal);

        while (at >= 0)
        {
            found++;
            at = body.IndexOf(looked, at + looked.Length, StringComparison.Ordinal);
        }

        return found;
    }
}

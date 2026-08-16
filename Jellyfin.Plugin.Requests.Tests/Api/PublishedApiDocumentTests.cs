using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Api;

/// <summary>
/// What a caller who never reads this source can find out about these endpoints.
/// <para>
/// Jellyfin builds its published API document from the description set ASP.NET derives from the
/// controllers it has loaded, this plugin's included. Everything a generator can put in that
/// document about an endpoint comes from that set: the path, the verb, the parameters and where each
/// one is read from, and the shape and status code of every answer. So the set is what these legs
/// read.
/// </para>
/// <para>
/// <b>The bound, because it is easy to read this as more than it is.</b> This is the input a
/// document is generated from and not the document. It is derived here from the plugin assembly by
/// itself, so what it cannot see is the server: whether the server loads this plugin's controllers
/// into its own application parts at all, and what its generator then writes. The first is the
/// subject of the recorded first-load run in <c>docs/testing.md</c>, and the second is the server's.
/// Between them and this there is no route in the tree that fetches a generated document, which is
/// said in <c>docs/api.md</c> rather than left for a reader to discover.
/// </para>
/// <para>
/// What it does catch is the failure this is for: an endpoint that answers with a shape or a status
/// code the document does not name. That reads as a working API to anybody generating a client from
/// it, and the client breaks on the first refusal rather than on the first call.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class PublishedApiDocumentTests
{
    private static readonly Guid Asker = new Guid("c7000000-0000-0000-0000-000000000001");
    private static readonly Guid Operator = new Guid("c7000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Started = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Every operation a caller can find in the document, with what it takes and what it answers.
    /// <para>
    /// An endpoint added without a line here fails, which is the point: an endpoint nobody annotated
    /// is one a per-endpoint test cannot miss because no per-endpoint test knows it exists. So is a
    /// parameter that quietly moved from the path to the query, and an answer whose shape changed
    /// under a caller reading the document.
    /// </para>
    /// <para>
    /// The lines are in the order the comparison sorts them, which is by the whole line. A line put
    /// where a reader would put it fails until it is moved, which is noise rather than a finding,
    /// and the alternative is a comparison that cannot say which operation is missing.
    /// </para>
    /// </summary>
    private static readonly string[] Expected =
    [
        // What this install is and what it allows, for a caller that has not called anything yet.
        // No failure shape, because it reads no store and refuses nothing: what it answers is known
        // before anybody configures anything.
        "GET MediaRequests/v1/Capabilities () -> 200:InstallCapabilities",

        // The page a browser opens. One status and one shape, and the shape is a string because
        // what comes back is a document rather than a record: a generated client that expected a
        // record here would parse the markup. It refuses nothing of its own, so it publishes no
        // failure; who may reach it is the server's evaluation of the policy and is not an answer.
        "GET MediaRequests/v1/Page () -> 200:String",

        // One person's own requests. The parameters are the same six the queue takes, because the
        // difference between the two reads is what the store is asked rather than what the caller
        // may ask for.
        "GET MediaRequests/v1/Requests"
            + " (descending@Query:Boolean, kind@Query:RequestedItemKind[], order@Query:RequestQueryOrder, skip@Query:Int32, state@Query:RequestState[], take@Query:Int32)"
            + " -> 200:RequestsPage<MyRequest>, 400:RequestFailure, 403:RequestFailure, 503:RequestFailure",

        // The whole queue. No 403 here, and that is not an omission: this endpoint never asks who
        // is calling, so there is no call it refuses for naming nobody. Who may reach it at all is
        // the server's evaluation of the elevation policy, which is not part of any answer.
        "GET MediaRequests/v1/Requests/Queue"
            + " (descending@Query:Boolean, kind@Query:RequestedItemKind[], order@Query:RequestQueryOrder, skip@Query:Int32, state@Query:RequestState[], take@Query:Int32)"
            + " -> 200:RequestsPage<QueuedRequest>, 400:RequestFailure, 503:RequestFailure",

        // Asking for something. Two success codes with one shape: 201 is a new request and 200 is
        // one that was joined or was already the caller's, and a client tells them apart by the
        // status or by the outcome in the body. The 409 is the person's own quota, which is the one
        // refusal here that is about the asker rather than about the body or the server.
        "POST MediaRequests/v1/Requests"
            + " (body@Body:CreateRequestBody)"
            + " -> 200:CreatedRequest, 201:CreatedRequest, 400:RequestFailure, 403:RequestFailure, 409:RequestFailure, 503:RequestFailure",

        // Saying yes to several at once. Three codes where the single decision publishes six, and
        // the three that are missing are the point: a request that is not there, one that moved and
        // a store that could not be read are answers about one request, and they come back inside
        // the entry for that request rather than as the status of a call that may already have
        // written some of the others.
        "POST MediaRequests/v1/Requests/Approve"
            + " (body@Body:ApproveManyBody)"
            + " -> 200:DecidedRequests, 400:RequestFailure, 403:RequestFailure",

        // Saying no to several at once, for one reason.
        "POST MediaRequests/v1/Requests/Decline"
            + " (body@Body:DeclineManyBody)"
            + " -> 200:DecidedRequests, 400:RequestFailure, 403:RequestFailure",

        // Saying yes.
        "POST MediaRequests/v1/Requests/{id}/Approve"
            + " (body@Body:ApproveRequestBody, id@Path:Guid)"
            + " -> 200:QueuedRequest, 400:RequestFailure, 403:RequestFailure, 404:RequestFailure, 409:RequestFailure, 503:RequestFailure",

        // Saying no, which takes the same shape of answer and one more field on the way in.
        "POST MediaRequests/v1/Requests/{id}/Decline"
            + " (body@Body:DeclineRequestBody, id@Path:Guid)"
            + " -> 200:QueuedRequest, 400:RequestFailure, 403:RequestFailure, 404:RequestFailure, 409:RequestFailure, 503:RequestFailure"
    ];

    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// The document lists exactly the operations written down, with their parameters and their
    /// answers.
    /// </summary>
    [Fact]
    public void TheDocumentListsTheOperationsWrittenDownForIt()
    {
        // Joined rather than compared as two sequences, because a collection failure prints the
        // difference with the middle elided and the whole list is what somebody repairing this
        // needs to read.
        Assert.Equal(
            string.Join(" | ", Expected),
            string.Join(" | ", Published()),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Nothing the plugin serves is missing from the document.
    /// <para>
    /// The leg above compares against a list somebody wrote. This one compares against the assembly,
    /// so an endpoint hidden from the document, by <see cref="ApiExplorerSettingsAttribute"/> or by
    /// a route the description set could not build, fails here even if the written list was updated
    /// to match. An endpoint that is reachable and undocumented is the one a caller finds by
    /// accident and depends on.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryEndpointTheAssemblyCarriesIsInTheDocument()
    {
        var carried = Actions()
            .Select(action => Verb(action.Method) + " " + action.Method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var documented = Descriptions()
            .Select(description => description.HttpMethod + " " + ((ControllerActionDescriptor)description.ActionDescriptor).MethodInfo.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(string.Join(" | ", carried), string.Join(" | ", documented), StringComparer.Ordinal);
    }

    /// <summary>
    /// Every failure in the document is published as the shape this API actually answers with.
    /// <para>
    /// This is the near miss, and it is one word rather than one character. A
    /// <c>[ProducesResponseType(StatusCodes.Status400BadRequest)]</c> that names no type is not an
    /// endpoint with no documented failure: under <see cref="ApiControllerAttribute"/> the framework
    /// fills the shape in for you, and what it fills in is <c>ProblemDetails</c>, which nothing here
    /// returns. A client generated from that document parses every refusal into the wrong type and
    /// finds out at the first one.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryFailureIsPublishedAsTheShapeThisApiAnswersWith()
    {
        var wrong = Descriptions()
            .SelectMany(description => description.SupportedResponseTypes
                .Where(response => response.StatusCode >= 400)
                .Where(response => response.Type != typeof(RequestFailure))
                .Select(response => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} {1} publishes {2} for {3}",
                    description.HttpMethod,
                    description.RelativePath,
                    Named(response.Type),
                    response.StatusCode)))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], wrong);
    }

    /// <summary>
    /// The status codes published for an endpoint are the ones it answers with, in both directions.
    /// <para>
    /// This is the leg the written list above cannot be. The list is a list, and a list agrees with
    /// whatever somebody put in it; this walks each endpoint through every answer it has and
    /// compares what came back against what the document promises. A status an endpoint answers with
    /// and does not publish is a refusal a client never planned for, and a status it publishes and
    /// never answers with is a branch somebody wrote for nothing.
    /// </para>
    /// <para>
    /// One arm is deliberately not walked and it changes no set. Both decisions publish <c>403</c>,
    /// and two failures share it: a call that names no person, which is walked here, and a caller
    /// the transition table does not admit, which no call these endpoints build can produce because
    /// both require elevation. <c>ErrorSurfaceTests</c> is where that is written down and
    /// <c>TheOnlyCallerTheseEndpointsBuildIsAdmittedByEveryMoveTheyCanMake</c> is what reds when it
    /// stops being true.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EveryStatusAnEndpointAnswersWithIsOneTheDocumentPublishes()
    {
        var answered = await AnsweredAsync().ConfigureAwait(true);

        var published = Descriptions()
            .ToDictionary(
                description => description.HttpMethod + " " + description.RelativePath,
                description => description.SupportedResponseTypes
                    .Select(response => response.StatusCode)
                    .Distinct()
                    .OrderBy(status => status)
                    .ToArray(),
                StringComparer.Ordinal);

        foreach (var endpoint in answered.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            Assert.Equal(
                string.Join(", ", published[endpoint]),
                string.Join(", ", answered[endpoint].OrderBy(status => status)));
        }

        // And every endpoint in the document was walked, so the loop above cannot pass by covering
        // a subset of them.
        Assert.Equal(
            string.Join(" | ", published.Keys.OrderBy(key => key, StringComparer.Ordinal)),
            string.Join(" | ", answered.Keys.OrderBy(key => key, StringComparer.Ordinal)),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Every status code each endpoint answers with, taken from calls that produce them rather than
    /// from a list.
    /// </summary>
    /// <returns>The statuses, by endpoint, spelled the way the document spells the endpoint.</returns>
    private async Task<Dictionary<string, HashSet<int>>> AnsweredAsync()
    {
        var answered = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var store = new InMemoryRequestStore();
        var unreadable = new StoreThatCannotBeRead();

        const string Capabilities = "GET MediaRequests/v1/Capabilities";
        const string BrowserPage = "GET MediaRequests/v1/Page";
        const string Create = "POST MediaRequests/v1/Requests";
        const string Mine = "GET MediaRequests/v1/Requests";
        const string Queue = "GET MediaRequests/v1/Requests/Queue";
        const string Approve = "POST MediaRequests/v1/Requests/{id}/Approve";
        const string Decline = "POST MediaRequests/v1/Requests/{id}/Decline";
        const string ApproveMany = "POST MediaRequests/v1/Requests/Approve";
        const string DeclineMany = "POST MediaRequests/v1/Requests/Decline";

        // What this install allows. One call and one status: it reads no store, refuses nothing and
        // has no failure to walk, which is why it publishes one code where every other endpoint
        // publishes five.
        Saw(
            Capabilities,
            await new CapabilitiesController(new FakeInstallSettings(), new NoRequestBackend())
                .CapabilitiesAsync(CancellationToken.None)
                .ConfigureAwait(true));

        // The page. One call and one status: it reads no store and refuses nothing of its own, and
        // the only way it answers with anything else is by being served out of an assembly that
        // does not carry it, which raises rather than answering.
        SawPage(BrowserPage, new MyRequestsPageController().Page());

        // Asking for something. The same body twice: the first is a new request and the second is
        // the caller already waiting for it, which is the endpoint's other success code.
        Saw(Create, await ControllerFor(store, Asker).CreateAsync(AFilm(), CancellationToken.None).ConfigureAwait(true));
        Saw(Create, await ControllerFor(store, Asker).CreateAsync(AFilm(), CancellationToken.None).ConfigureAwait(true));
        Saw(Create, await ControllerFor(store, Asker).CreateAsync(new CreateRequestBody { Title = "No kind" }, CancellationToken.None).ConfigureAwait(true));
        Saw(Create, await ControllerFor(store, caller: null).CreateAsync(AFilm(), CancellationToken.None).ConfigureAwait(true));
        Saw(Create, await ControllerFor(unreadable, Asker).CreateAsync(AFilm(), CancellationToken.None).ConfigureAwait(true));

        // The asker is now waiting for the film the two calls above put in the queue, so an install
        // allowing one open request refuses the next thing they ask for.
        Saw(
            Create,
            await ControllerFor(store, Asker, new FakeInstallSettings(new PluginConfiguration { OpenRequestsPerUser = 1 }))
                .CreateAsync(ASeries(), CancellationToken.None)
                .ConfigureAwait(true));

        Saw(Mine, await ControllerFor(store, Asker).MineAsync(cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Saw(Mine, await ControllerFor(store, Asker).MineAsync(take: RequestsController.MaximumPageSize + 1, cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Saw(Mine, await ControllerFor(store, caller: null).MineAsync(cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Saw(Mine, await ControllerFor(unreadable, Asker).MineAsync(cancellationToken: CancellationToken.None).ConfigureAwait(true));

        Saw(Queue, await ControllerFor(store, Operator).QueueAsync(cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Saw(Queue, await ControllerFor(store, Operator).QueueAsync(take: RequestsController.MaximumPageSize + 1, cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Saw(Queue, await ControllerFor(unreadable, Operator).QueueAsync(cancellationToken: CancellationToken.None).ConfigureAwait(true));

        var toApprove = await store.AddAsync(AnAsk(), CancellationToken.None).ConfigureAwait(true);
        var toDecline = await store.AddAsync(AnAsk(), CancellationToken.None).ConfigureAwait(true);
        var absent = _identifiers.NewId();

        Saw(Approve, await ControllerFor(store, Operator).ApproveAsync(toApprove.Request.Id, new ApproveRequestBody { Revision = toApprove.Revision }, CancellationToken.None).ConfigureAwait(true));
        Saw(Approve, await ControllerFor(store, Operator).ApproveAsync(toApprove.Request.Id, new ApproveRequestBody(), CancellationToken.None).ConfigureAwait(true));
        Saw(Approve, await ControllerFor(store, caller: null).ApproveAsync(toApprove.Request.Id, new ApproveRequestBody { Revision = 1 }, CancellationToken.None).ConfigureAwait(true));
        Saw(Approve, await ControllerFor(store, Operator).ApproveAsync(absent, new ApproveRequestBody { Revision = 1 }, CancellationToken.None).ConfigureAwait(true));
        Saw(Approve, await ControllerFor(store, Operator).ApproveAsync(toDecline.Request.Id, new ApproveRequestBody { Revision = toDecline.Revision + 7 }, CancellationToken.None).ConfigureAwait(true));
        Saw(Approve, await ControllerFor(unreadable, Operator).ApproveAsync(toDecline.Request.Id, new ApproveRequestBody { Revision = 1 }, CancellationToken.None).ConfigureAwait(true));

        Saw(Decline, await ControllerFor(store, Operator).DeclineAsync(toDecline.Request.Id, new DeclineRequestBody { Revision = toDecline.Revision, Reason = DeclineReason.NotWanted }, CancellationToken.None).ConfigureAwait(true));
        Saw(Decline, await ControllerFor(store, Operator).DeclineAsync(toDecline.Request.Id, new DeclineRequestBody { Revision = toDecline.Revision }, CancellationToken.None).ConfigureAwait(true));
        Saw(Decline, await ControllerFor(store, caller: null).DeclineAsync(toDecline.Request.Id, new DeclineRequestBody { Revision = 1, Reason = DeclineReason.NotWanted }, CancellationToken.None).ConfigureAwait(true));
        Saw(Decline, await ControllerFor(store, Operator).DeclineAsync(absent, new DeclineRequestBody { Revision = 1, Reason = DeclineReason.NotWanted }, CancellationToken.None).ConfigureAwait(true));
        Saw(Decline, await ControllerFor(store, Operator).DeclineAsync(toApprove.Request.Id, new DeclineRequestBody { Revision = toApprove.Revision + 7, Reason = DeclineReason.NotWanted }, CancellationToken.None).ConfigureAwait(true));
        Saw(Decline, await ControllerFor(unreadable, Operator).DeclineAsync(toApprove.Request.Id, new DeclineRequestBody { Revision = 1, Reason = DeclineReason.NotWanted }, CancellationToken.None).ConfigureAwait(true));

        // The two actions on several requests. Three statuses each and no more, and the third call
        // in each pair is the one worth reading: a store that cannot be read answers 200 here,
        // because by the time one request is refused another may already be written and a status
        // saying the call failed would be saying nothing happened while something had.
        var toApproveTogether = await store.AddAsync(AnAsk(), CancellationToken.None).ConfigureAwait(true);
        var toDeclineTogether = await store.AddAsync(AnAsk(), CancellationToken.None).ConfigureAwait(true);

        Saw(ApproveMany, await ControllerFor(store, Operator).ApproveManyAsync(new ApproveManyBody { Requests = [Choosing(toApproveTogether)] }, CancellationToken.None).ConfigureAwait(true));
        Saw(ApproveMany, await ControllerFor(store, Operator).ApproveManyAsync(new ApproveManyBody(), CancellationToken.None).ConfigureAwait(true));
        Saw(ApproveMany, await ControllerFor(store, caller: null).ApproveManyAsync(new ApproveManyBody { Requests = [Choosing(toApproveTogether)] }, CancellationToken.None).ConfigureAwait(true));
        Saw(ApproveMany, await ControllerFor(unreadable, Operator).ApproveManyAsync(new ApproveManyBody { Requests = [Choosing(toApproveTogether)] }, CancellationToken.None).ConfigureAwait(true));

        Saw(DeclineMany, await ControllerFor(store, Operator).DeclineManyAsync(new DeclineManyBody { Requests = [Choosing(toDeclineTogether)], Reason = DeclineReason.NotWanted }, CancellationToken.None).ConfigureAwait(true));
        Saw(DeclineMany, await ControllerFor(store, Operator).DeclineManyAsync(new DeclineManyBody { Requests = [Choosing(toDeclineTogether)] }, CancellationToken.None).ConfigureAwait(true));
        Saw(DeclineMany, await ControllerFor(store, caller: null).DeclineManyAsync(new DeclineManyBody { Requests = [Choosing(toDeclineTogether)], Reason = DeclineReason.NotWanted }, CancellationToken.None).ConfigureAwait(true));
        Saw(DeclineMany, await ControllerFor(unreadable, Operator).DeclineManyAsync(new DeclineManyBody { Requests = [Choosing(toDeclineTogether)], Reason = DeclineReason.NotWanted }, CancellationToken.None).ConfigureAwait(true));

        return answered;

        void Saw<T>(string endpoint, ActionResult<T> came) => Answered(endpoint, Status(came));

        // The page answers with a document rather than a record, so it comes back as a result that
        // carries its own content type instead of one the framework serialises. That is why it has
        // a walk of its own here rather than going through the one above.
        void SawPage(string endpoint, ContentResult came) => Answered(endpoint, came.StatusCode ?? 200);

        void Answered(string endpoint, int status)
        {
            if (!answered.TryGetValue(endpoint, out var statuses))
            {
                statuses = [];
                answered[endpoint] = statuses;
            }

            statuses.Add(status);
        }
    }

    /// <summary>
    /// The status code an action answered with.
    /// </summary>
    /// <typeparam name="T">What the action answers with when it succeeds.</typeparam>
    /// <param name="came">What the action returned.</param>
    /// <returns>The status code.</returns>
    private static int Status<T>(ActionResult<T> came)
    {
        var result = Assert.IsAssignableFrom<ObjectResult>(came.Result);

        // An Ok() carries no explicit status, and 200 is what the framework writes for one.
        return result.StatusCode ?? 200;
    }

    /// <summary>
    /// The description set a document generator reads, derived from the plugin assembly.
    /// <para>
    /// The application parts are built here rather than discovered, because discovery reads the
    /// entry assembly and the entry assembly under a test run is the test host.
    /// </para>
    /// </summary>
    /// <returns>Every operation the plugin publishes.</returns>
    private static ApiDescription[] Descriptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var parts = new ApplicationPartManager();
        parts.ApplicationParts.Add(new AssemblyPart(typeof(RequestsControllerBase).Assembly));
        services.AddSingleton(parts);
        services.AddControllers();

        using var provider = services.BuildServiceProvider();

        return [.. provider
            .GetRequiredService<IApiDescriptionGroupCollectionProvider>()
            .ApiDescriptionGroups
            .Items
            .SelectMany(group => group.Items)];
    }

    /// <summary>
    /// One operation as a caller reading the document sees it: what it is, what it takes and what it
    /// answers with.
    /// <para>
    /// A parameter's type is the one the action declares rather than the one the description carries,
    /// and the difference is not cosmetic. The description reports a scalar enumeration read from the
    /// query as the type it arrives as, and which type that is moved between the two runtimes this
    /// plugin is built for: <c>order</c> comes through as <c>String</c> on one and as
    /// <c>RequestQueryOrder</c> on the other, off the same source. Writing the runtime's answer down
    /// would mean one of the two lines failing on a difference nobody here made. Where each parameter
    /// is read from is taken from the description, because that is a fact about the endpoint rather
    /// than about the runtime, and it is the half that moves when somebody changes a route.
    /// </para>
    /// </summary>
    /// <returns>The lines, sorted.</returns>
    private static string[] Published()
        => [.. Descriptions()
            .Select(description => string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} ({2}) -> {3}",
                description.HttpMethod,
                description.RelativePath,
                string.Join(
                    ", ",
                    description.ParameterDescriptions
                        .Select(parameter => string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}@{1}:{2}",
                            parameter.Name,
                            parameter.Source.Id,
                            Named(parameter.ParameterDescriptor?.ParameterType ?? parameter.Type)))
                        .OrderBy(parameter => parameter, StringComparer.Ordinal)),
                string.Join(
                    ", ",
                    description.SupportedResponseTypes
                        .Select(response => string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}:{1}",
                            response.StatusCode,
                            Named(response.Type)))
                        .OrderBy(response => response, StringComparer.Ordinal))))
            .OrderBy(line => line, StringComparer.Ordinal)];

    /// <summary>
    /// A type as a reader would write it, generic arguments included, because
    /// <c>RequestsPage`1</c> does not say which page it is.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The name.</returns>
    private static string Named(Type? type)
    {
        if (type is null)
        {
            return "(none)";
        }

        if (!type.IsGenericType)
        {
            return type.IsArray
                ? Named(type.GetElementType()) + "[]"
                : type.Name;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}<{1}>",
            type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)],
            string.Join(", ", type.GetGenericArguments().Select(Named)));
    }

    /// <summary>
    /// Every action on every controller the plugin ships, which is every method carrying a verb.
    /// </summary>
    /// <returns>The actions.</returns>
    private static (Type Type, MethodInfo Method)[] Actions()
        => [.. typeof(RequestsControllerBase).Assembly
            .GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .Where(type => !type.IsAbstract)
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: false).Any())
                .Select(method => (Type: type, Method: method)))];

    /// <summary>
    /// The verb an action answers under.
    /// </summary>
    /// <param name="method">The action.</param>
    /// <returns>The verb.</returns>
    private static string Verb(MethodInfo method)
        => method.GetCustomAttributes<HttpMethodAttribute>(inherit: false)
            .Single()
            .HttpMethods
            .Single();

    /// <summary>
    /// A controller wired to one store and one identity.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="caller">Who the server says is calling.</param>
    /// <returns>The controller under test.</returns>
    private RequestsController ControllerFor(IRequestStore store, Guid? caller)
        => ControllerFor(store, caller, new FakeInstallSettings());

    /// <summary>
    /// A controller wired to one store, one identity and one install, for the answer that depends on
    /// what this server is set to.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="caller">Who the server says is calling.</param>
    /// <param name="settings">What this install is set to.</param>
    /// <returns>The controller under test.</returns>
    private RequestsController ControllerFor(IRequestStore store, Guid? caller, IInstallSettings settings)
        => new RequestsController(store, new TestClock(Started), _identifiers, new FakeCallerIdentity(caller), settings, new RecordingJournal());

    /// <summary>
    /// A request as the store holds one.
    /// </summary>
    /// <returns>The request.</returns>
    private MediaRequest AnAsk() => new MediaRequest
    {
        Id = _identifiers.NewId(),
        RequestedByUserId = Asker,
        RequestedAt = Started,
        StateChangedAt = Started,
        Kind = RequestedItemKind.Movie,
        DisplayTitle = "The Conversation",
        DisplayYear = 1974,
        ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "603" }
    };

    /// <summary>
    /// One request as a caller chooses it off a page of the queue, for the actions that carry
    /// several.
    /// </summary>
    /// <param name="stored">What the store holds.</param>
    /// <returns>The entry.</returns>
    private static RequestToDecide Choosing(StoredRequest stored)
        => new RequestToDecide { Id = stored.Request.Id, Revision = stored.Revision };

    /// <summary>
    /// A body asking for a film.
    /// </summary>
    /// <returns>The body.</returns>
    private static CreateRequestBody AFilm() => new CreateRequestBody
    {
        Kind = RequestedItemKind.Movie,
        Title = "The Conversation",
        Year = 1974,
        ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "605" }
    };

    /// <summary>
    /// A second thing to ask for, so a person already waiting for the film above is asking for
    /// something the queue does not hold rather than joining what they are already on.
    /// </summary>
    /// <returns>The body.</returns>
    private static CreateRequestBody ASeries() => new CreateRequestBody
    {
        Kind = RequestedItemKind.Series,
        Title = "The Singing Detective",
        Year = 1986,
        ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tvdb"] = "76423" }
    };
}

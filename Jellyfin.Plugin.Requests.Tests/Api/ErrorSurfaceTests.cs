using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Api;

/// <summary>
/// Every way this API says no, gathered in one place and judged as a surface rather than one
/// endpoint at a time.
/// <para>
/// A per-endpoint test says the endpoint it is about answers correctly. What it cannot say is that
/// the answers agree with each other, and that is the whole of what an error contract is: one shape,
/// one status code per class, and nothing in a message that a caller must not be told. Those three
/// are properties of the set, so they are checked over the set.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class ErrorSurfaceTests
{
    private static readonly Guid Asker = new Guid("c3000000-0000-0000-0000-000000000001");
    private static readonly Guid Operator = new Guid("c3000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Started = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The status code each failure is reported with, written down.
    /// <para>
    /// A code added without a line here fails the leg below, which is the point: the failure a
    /// per-endpoint test cannot catch is a new code whose status nobody chose, arriving under
    /// whatever the endpoint that raised it happened to use.
    /// </para>
    /// </summary>
    private static readonly (RequestFailureCode Code, int Status)[] Expected =
    [
        (RequestFailureCode.InvalidBody, 400),
        (RequestFailureCode.NoUserOnTheCall, 403),
        (RequestFailureCode.NoSuchRequest, 404),
        (RequestFailureCode.MovedSinceItWasRead, 409),
        (RequestFailureCode.TheTableRefusesTheMove, 409),
        (RequestFailureCode.TheRequestNamesNothing, 409),
        (RequestFailureCode.TheCallerMayNotMakeThisMove, 403),
        (RequestFailureCode.TheStoreCouldNotBeRead, 503)
    ];

    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// Every code the enumeration declares has one status code, and it is the one written above.
    /// This is the only leg that reaches every code, including the one no call can produce today.
    /// </summary>
    [Fact]
    public void EveryFailureCodeHasTheStatusCodeWrittenDownForIt()
    {
        var declared = Enum.GetValues<RequestFailureCode>()
            .Select(code => Line(code, RequestFailure.StatusFor(code)))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            string.Join(" | ", Expected.Select(pair => Line(pair.Code, pair.Status)).OrderBy(line => line, StringComparer.Ordinal)),
            string.Join(" | ", declared),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Every failure a call can actually produce comes back in the one shape, under the status code
    /// its class is written down with, carrying a code and a message.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EveryFailureACallCanProduceCarriesItsCodeAndItsStatus()
    {
        foreach (var refused in await SurfaceAsync().ConfigureAwait(true))
        {
            Assert.True(
                Enum.IsDefined(refused.Failure.Code),
                FormattableString.Invariant($"{refused.What} answered with a code that is not one this API declares."));

            Assert.Equal(RequestFailure.StatusFor(refused.Failure.Code), refused.Status);
            Assert.NotEmpty(refused.Failure.Message);
        }
    }

    /// <summary>
    /// The set covers every code except the one no call can reach.
    /// <para>
    /// Without this the leg above passes over any subset, including a subset that shrank because an
    /// endpoint stopped refusing something. What is left out is
    /// <see cref="RequestFailureCode.TheCallerMayNotMakeThisMove"/>: both endpoints that could raise
    /// it require elevation, so the only caller they build is an administrator, and every legal move
    /// they can make admits one. That is stated rather than covered, and
    /// <c>TheOnlyCallerTheseEndpointsBuildIsAdmittedByEveryMoveTheyCanMake</c> is what reds when it
    /// stops being true.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EveryCodeACallCanReachIsReachedByThisSurface()
    {
        var produced = (await SurfaceAsync().ConfigureAwait(true))
            .Select(refused => refused.Failure.Code)
            .Distinct()
            .OrderBy(code => code)
            .ToArray();

        var reachable = Enum.GetValues<RequestFailureCode>()
            .Where(code => code != RequestFailureCode.TheCallerMayNotMakeThisMove)
            .OrderBy(code => code)
            .ToArray();

        Assert.Equal(reachable, produced);
    }

    /// <summary>
    /// No message names a person, a path on the server disk, or an exception.
    /// <para>
    /// The three are one rule with three shapes. A user identifier in a message tells a caller about
    /// somebody else; a path tells anybody who can reach an endpoint how the server is laid out; and
    /// an exception, or the stack behind it, is the plugin describing its own internals to whoever
    /// asked. The store failure is the one that matters most and is the reason this leg exists: the
    /// exception behind it names the file, and the message that comes back must not.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task NoFailureMessageNamesAPersonAPathOrAnException()
    {
        string[] forbidden =
        [
            Asker.ToString("D", CultureInfo.InvariantCulture),
            Asker.ToString("N", CultureInfo.InvariantCulture),
            Operator.ToString("D", CultureInfo.InvariantCulture),
            Operator.ToString("N", CultureInfo.InvariantCulture),
            StoreThatCannotBeRead.NamedPath,
            StoreThatCannotBeRead.NamedDetail,
            "/var/",
            ".json",
            "\\",
            "Exception",
            "   at "
        ];

        var leaked = (await SurfaceAsync().ConfigureAwait(true))
            .SelectMany(refused => forbidden
                .Where(marker => refused.Failure.Message.Contains(marker, StringComparison.OrdinalIgnoreCase))
                .Select(marker => FormattableString.Invariant($"{refused.What} names {marker}")))
            .OrderBy(named => named, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], leaked);
    }

    /// <summary>
    /// A failure carries the request as the store holds it only where the caller is an
    /// administrator. The row names who asked and everybody waiting alongside them, which is the
    /// disclosure the elevation on the queue exists for, and a user reading their own requests is
    /// not admitted to it.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task OnlyAnAdministratorsCallEverCarriesTheStoredRequestBack()
    {
        var carried = (await SurfaceAsync().ConfigureAwait(true))
            .Where(refused => refused.Failure.Current is not null && !refused.Elevated)
            .Select(refused => refused.What)
            .OrderBy(what => what, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], carried);
    }

    /// <summary>
    /// One failure line, for the comparison that has to print which code is wrong.
    /// </summary>
    /// <param name="code">The failure.</param>
    /// <param name="status">The status code.</param>
    /// <returns>The line.</returns>
    private static string Line(RequestFailureCode code, int status)
        => string.Format(CultureInfo.InvariantCulture, "{0} -> {1}", code, status);

    /// <summary>
    /// Every failure a call to this API can produce, each one made by a real call to a real action.
    /// <para>
    /// They are produced rather than constructed. A table of failure values written here would prove
    /// the shape and nothing about what the endpoints answer with, which is the half that drifts.
    /// </para>
    /// </summary>
    /// <returns>What was called, whether the endpoint is an administrator one, and what came back.</returns>
    private async Task<IReadOnlyList<(string What, bool Elevated, int Status, RequestFailure Failure)>> SurfaceAsync()
    {
        var refusals = new List<(string What, bool Elevated, int Status, RequestFailure Failure)>();
        var store = new InMemoryRequestStore();
        var unreadable = new StoreThatCannotBeRead();

        var open = await store.AddAsync(AnAsk(), CancellationToken.None).ConfigureAwait(true);

        var typed = await store
            .AddAsync(AnAsk() with { Id = _identifiers.NewId(), ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) }, CancellationToken.None)
            .ConfigureAwait(true);

        var fulfilled = await store
            .ReplaceAsync(
                RequestLifecycle.Move(
                    await AnotherAskAsync(store).ConfigureAwait(true),
                    RequestState.Fulfilled,
                    Started,
                    RequestCaller.Plugin),
                expectedRevision: 1,
                CancellationToken.None)
            .ConfigureAwait(true);

        Add("POST Requests with a body that cannot become a request", elevated: false, Of(
            await ControllerFor(store, Asker)
                .CreateAsync(new CreateRequestBody { Title = "No kind" }, CancellationToken.None)
                .ConfigureAwait(true)));

        Add("POST Requests from a call naming no person", elevated: false, Of(
            await ControllerFor(store, caller: null)
                .CreateAsync(AFilm(), CancellationToken.None)
                .ConfigureAwait(true)));

        Add("POST Requests against a store that cannot be read", elevated: false, Of(
            await ControllerFor(unreadable, Asker)
                .CreateAsync(AFilm(), CancellationToken.None)
                .ConfigureAwait(true)));

        Add("GET Requests with a page larger than the cap", elevated: false, Of(
            await ControllerFor(store, Asker)
                .MineAsync(take: RequestsController.MaximumPageSize + 1, cancellationToken: CancellationToken.None)
                .ConfigureAwait(true)));

        Add("GET Requests/Queue against a store that cannot be read", elevated: true, Of(
            await ControllerFor(unreadable, Operator)
                .QueueAsync(cancellationToken: CancellationToken.None)
                .ConfigureAwait(true)));

        Add("POST Requests/{id}/Approve for a request that is not there", elevated: true, Of(
            await ControllerFor(store, Operator)
                .ApproveAsync(_identifiers.NewId(), new ApproveRequestBody { Revision = 1 }, CancellationToken.None)
                .ConfigureAwait(true)));

        Add("POST Requests/{id}/Approve against a revision that has moved", elevated: true, Of(
            await ControllerFor(store, Operator)
                .ApproveAsync(open.Request.Id, new ApproveRequestBody { Revision = open.Revision + 7 }, CancellationToken.None)
                .ConfigureAwait(true)));

        Add("POST Requests/{id}/Approve for a request the table refuses it on", elevated: true, Of(
            await ControllerFor(store, Operator)
                .ApproveAsync(fulfilled.Request.Id, new ApproveRequestBody { Revision = fulfilled.Revision }, CancellationToken.None)
                .ConfigureAwait(true)));

        Add("POST Requests/{id}/Approve for a request that names nothing", elevated: true, Of(
            await ControllerFor(store, Operator)
                .ApproveAsync(typed.Request.Id, new ApproveRequestBody { Revision = typed.Revision }, CancellationToken.None)
                .ConfigureAwait(true)));

        Add("POST Requests/{id}/Decline with no reason", elevated: true, Of(
            await ControllerFor(store, Operator)
                .DeclineAsync(open.Request.Id, new DeclineRequestBody { Revision = open.Revision }, CancellationToken.None)
                .ConfigureAwait(true)));

        Add("POST Requests/{id}/Decline from a call naming no person", elevated: true, Of(
            await ControllerFor(store, caller: null)
                .DeclineAsync(
                    open.Request.Id,
                    new DeclineRequestBody { Revision = open.Revision, Reason = DeclineReason.NotWanted },
                    CancellationToken.None)
                .ConfigureAwait(true)));

        Add("POST Requests/{id}/Decline against a store that cannot be read", elevated: true, Of(
            await ControllerFor(unreadable, Operator)
                .DeclineAsync(
                    open.Request.Id,
                    new DeclineRequestBody { Revision = open.Revision, Reason = DeclineReason.NotWanted },
                    CancellationToken.None)
                .ConfigureAwait(true)));

        // The actions on several requests, and only the refusals that are the call's own. A refusal
        // about one request inside such an action is built by the same code that builds it for the
        // single decision, in RequestsController.DecideAsync, so its message is one of the ones
        // already walked above; what is new here is the refusals of a body carrying a selection.
        Add("POST Requests/Approve with an empty selection", elevated: true, Of(
            await ControllerFor(store, Operator)
                .ApproveManyAsync(new ApproveManyBody(), CancellationToken.None)
                .ConfigureAwait(true)));

        Add("POST Requests/Approve with an entry naming no request", elevated: true, Of(
            await ControllerFor(store, Operator)
                .ApproveManyAsync(
                    new ApproveManyBody { Requests = [new RequestToDecide { Revision = open.Revision }] },
                    CancellationToken.None)
                .ConfigureAwait(true)));

        Add("POST Requests/Approve with an entry carrying no revision", elevated: true, Of(
            await ControllerFor(store, Operator)
                .ApproveManyAsync(
                    new ApproveManyBody { Requests = [new RequestToDecide { Id = open.Request.Id }] },
                    CancellationToken.None)
                .ConfigureAwait(true)));

        Add("POST Requests/Approve naming one request twice", elevated: true, Of(
            await ControllerFor(store, Operator)
                .ApproveManyAsync(
                    new ApproveManyBody
                    {
                        Requests =
                        [
                            new RequestToDecide { Id = open.Request.Id, Revision = open.Revision },
                            new RequestToDecide { Id = open.Request.Id, Revision = open.Revision }
                        ]
                    },
                    CancellationToken.None)
                .ConfigureAwait(true)));

        Add("POST Requests/Approve from a call naming no person", elevated: true, Of(
            await ControllerFor(store, caller: null)
                .ApproveManyAsync(
                    new ApproveManyBody { Requests = [new RequestToDecide { Id = open.Request.Id, Revision = open.Revision }] },
                    CancellationToken.None)
                .ConfigureAwait(true)));

        Add("POST Requests/Decline with no reason", elevated: true, Of(
            await ControllerFor(store, Operator)
                .DeclineManyAsync(
                    new DeclineManyBody { Requests = [new RequestToDecide { Id = open.Request.Id, Revision = open.Revision }] },
                    CancellationToken.None)
                .ConfigureAwait(true)));

        return refusals;

        void Add(string what, bool elevated, (int Status, RequestFailure Failure) answered)
            => refusals.Add((what, elevated, answered.Status, answered.Failure));
    }

    /// <summary>
    /// The failure an action came back with, and the status code it used.
    /// </summary>
    /// <typeparam name="T">What the action answers with when it succeeds.</typeparam>
    /// <param name="answered">What the action returned.</param>
    /// <returns>The status code and the failure.</returns>
    private static (int Status, RequestFailure Failure) Of<T>(ActionResult<T> answered)
    {
        var result = Assert.IsAssignableFrom<ObjectResult>(answered.Result);

        return (result.StatusCode ?? 0, Assert.IsType<RequestFailure>(result.Value));
    }

    /// <summary>
    /// A controller wired to one store and one identity.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="caller">Who the server says is calling.</param>
    /// <returns>The controller under test.</returns>
    private RequestsController ControllerFor(IRequestStore store, Guid? caller)
        => new RequestsController(store, new TestClock(Started), _identifiers, new FakeCallerIdentity(caller));

    /// <summary>
    /// A second request in the store, so one of them can be moved out of the way.
    /// </summary>
    /// <param name="store">Where it goes.</param>
    /// <returns>The request.</returns>
    private async Task<MediaRequest> AnotherAskAsync(InMemoryRequestStore store)
    {
        var added = await store
            .AddAsync(
                AnAsk() with
                {
                    Id = _identifiers.NewId(),
                    ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "604" }
                },
                CancellationToken.None)
            .ConfigureAwait(true);

        return added.Request;
    }

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
}

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Api;

/// <summary>
/// Reading requests back: one person's own, and the whole queue.
/// <para>
/// The actions are called directly rather than through a server, for the reason
/// <see cref="CreateRequestTests"/> gives. What that leaves out is the server enforcing the policy
/// attribute on the queue endpoint, and it is named here rather than implied: the elevation is
/// asserted as an attribute on the action, and what the server does with it is not exercised by any
/// test on this board.
/// </para>
/// <para>
/// The leg that matters most is the one that fills the store with two people's requests and then
/// asks, as one of them, under every combination of filter, order and page this endpoint offers. A
/// test that asks once and gets its own rows back would pass a controller that filtered after the
/// read, which is the shape that leaks the day somebody adds a sort.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class ListRequestsTests
{
    private static readonly Guid Reader = new Guid("c1000000-0000-0000-0000-000000000001");
    private static readonly Guid SomebodyElse = new Guid("c1000000-0000-0000-0000-000000000002");
    private static readonly Guid AThirdPerson = new Guid("c1000000-0000-0000-0000-000000000003");

    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// The one that has to hold whatever is asked for. The store holds requests from three people,
    /// including one the reader joined and one they did not, and every combination of state filter,
    /// kind filter, order, direction and page is asked for as the reader.
    /// <para>
    /// Every row that comes back is one the reader is waiting for, and the count beside it never
    /// exceeds how many of theirs there are. The count is asserted as well as the rows, because a
    /// count taken over the whole queue is the same disclosure as a row: it says how many other
    /// people have asked for things.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task NothingButTheCallersOwnRequestsComesBackWhateverIsAskedFor()
    {
        var store = new InMemoryRequestStore();
        var theirs = await ASpreadOfRequestsAsync(store).ConfigureAwait(true);
        var controller = ControllerFor(store, Reader);

        var everyState = new RequestState[]?[]
        {
            null,
            [],
            [RequestState.Open],
            [RequestState.Approved],
            [RequestState.Declined],
            [RequestState.Open, RequestState.Approved, RequestState.Declined, RequestState.Fulfilled, RequestState.Failed]
        };

        var everyKind = new RequestedItemKind[]?[]
        {
            null,
            [],
            [RequestedItemKind.Movie],
            [RequestedItemKind.Series],
            [RequestedItemKind.Movie, RequestedItemKind.Series]
        };

        var combinations = 0;

        foreach (var states in everyState)
        {
            foreach (var kinds in everyKind)
            {
                foreach (var order in Enum.GetValues<RequestQueryOrder>())
                {
                    foreach (var descending in new[] { false, true })
                    {
                        // Paged one row at a time as well as whole, because a page taken after the
                        // narrowing and a page taken before it differ only once there is more than
                        // one page.
                        foreach (var take in new[] { 0, 1, 2, RequestsController.MaximumPageSize })
                        {
                            for (var skip = 0; skip <= 3; skip++)
                            {
                                var page = Page(await controller.MineAsync(
                                    states,
                                    kinds,
                                    order,
                                    descending,
                                    skip,
                                    take,
                                    CancellationToken.None).ConfigureAwait(true));

                                Assert.All(
                                    page.Requests,
                                    row => Assert.Contains(row.Id, theirs));
                                Assert.True(
                                    page.MatchCount <= theirs.Count,
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        "The count was {0} and this person is waiting for {1} requests.",
                                        page.MatchCount,
                                        theirs.Count));

                                combinations++;
                            }
                        }
                    }
                }
            }
        }

        // The loop is asserted to have run, so a change that makes one of the sequences empty turns
        // into a failure rather than into a test that passes over nothing.
        Assert.Equal(everyState.Length * everyKind.Length * 3 * 2 * 4 * 4, combinations);
    }

    /// <summary>
    /// The rows carry no identifier of any person, checked against the bytes rather than against the
    /// properties. The request the reader joined was asked for by somebody else and a third person
    /// joined it too, so a shape that leaked any of the three would print one of their identifiers
    /// here.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task NoRowNamesAnybodyIncludingTheCaller()
    {
        var store = new InMemoryRequestStore();
        await ASpreadOfRequestsAsync(store).ConfigureAwait(true);
        var controller = ControllerFor(store, Reader);

        var page = Page(await controller.MineAsync(
            take: RequestsController.MaximumPageSize,
            cancellationToken: CancellationToken.None).ConfigureAwait(true));

        Assert.NotEmpty(page.Requests);

        var written = JsonSerializer.Serialize(page);

        foreach (var person in new[] { Reader, SomebodyElse, AThirdPerson })
        {
            Assert.DoesNotContain(person.ToString(), written, StringComparison.OrdinalIgnoreCase);
        }

        // The same statement about the shape rather than about one document: no property of a row is
        // an identifier of a person, whatever a particular request happens to hold.
        var identifiers = typeof(MyRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(Guid) || property.PropertyType == typeof(Guid?)
                || property.PropertyType == typeof(IReadOnlyList<Guid>))
            .Select(property => property.Name)
            .Where(name => !string.Equals(name, nameof(MyRequest.Id), StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], identifiers);
    }

    /// <summary>
    /// A note is the requester's writing, so a joined request carries none. The reader joined a
    /// request whose first asker wrote one, and that text is not in the answer.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ANoteOnARequestSomebodyElseAskedForIsNotHandedToWhoeverJoinedIt()
    {
        var store = new InMemoryRequestStore();
        await ASpreadOfRequestsAsync(store).ConfigureAwait(true);
        var controller = ControllerFor(store, Reader);

        var page = Page(await controller.MineAsync(
            take: RequestsController.MaximumPageSize,
            cancellationToken: CancellationToken.None).ConfigureAwait(true));

        var joined = Assert.Single(page.Requests, row => !row.AskedByYou);
        Assert.Null(joined.YourNote);

        var own = Assert.Single(page.Requests, row => row.AskedByYou && row.YourNote is not null);
        Assert.Equal("The reader wrote this.", own.YourNote, StringComparer.Ordinal);

        Assert.DoesNotContain(
            "somebody else wrote this",
            JsonSerializer.Serialize(page),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Paging over one person's own requests: every match is seen exactly once across the pages, the
    /// count is the same on every page, and it is the count of matches rather than of rows returned.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task PagingWalksEveryMatchExactlyOnceAndTheCountIsTheSameOnEveryPage()
    {
        var store = new InMemoryRequestStore();
        var theirs = await ASpreadOfRequestsAsync(store).ConfigureAwait(true);
        var controller = ControllerFor(store, Reader);

        var seen = new List<Guid>();
        var counts = new List<int>();

        for (var skip = 0; skip < theirs.Count + 2; skip++)
        {
            var page = Page(await controller.MineAsync(
                skip: skip,
                take: 1,
                cancellationToken: CancellationToken.None).ConfigureAwait(true));

            counts.Add(page.MatchCount);
            seen.AddRange(page.Requests.Select(row => row.Id));
        }

        Assert.Equal(theirs.OrderBy(id => id), seen.OrderBy(id => id));
        Assert.Equal(theirs.Count, seen.Distinct().Count());
        Assert.All(counts, count => Assert.Equal(theirs.Count, count));
    }

    /// <summary>
    /// The filters narrow, and each one is asserted against a request that is inside it and one that
    /// is not, so a filter that admitted everything would fail as loudly as one that admitted
    /// nothing.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EachFilterNarrowsToWhatItNames()
    {
        var store = new InMemoryRequestStore();
        await ASpreadOfRequestsAsync(store).ConfigureAwait(true);
        var controller = ControllerFor(store, Reader);

        var open = Page(await controller.MineAsync(
            [RequestState.Open],
            take: RequestsController.MaximumPageSize,
            cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Assert.NotEmpty(open.Requests);
        Assert.All(open.Requests, row => Assert.Equal(RequestState.Open, row.State));

        var approved = Page(await controller.MineAsync(
            [RequestState.Approved],
            take: RequestsController.MaximumPageSize,
            cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Assert.NotEmpty(approved.Requests);
        Assert.All(approved.Requests, row => Assert.Equal(RequestState.Approved, row.State));

        var films = Page(await controller.MineAsync(
            kind: [RequestedItemKind.Movie],
            take: RequestsController.MaximumPageSize,
            cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Assert.NotEmpty(films.Requests);
        Assert.All(films.Requests, row => Assert.Equal(RequestedItemKind.Movie, row.Kind));

        var series = Page(await controller.MineAsync(
            kind: [RequestedItemKind.Series],
            take: RequestsController.MaximumPageSize,
            cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Assert.NotEmpty(series.Requests);
        Assert.All(series.Requests, row => Assert.Equal(RequestedItemKind.Series, row.Kind));

        // Both filters at once narrow by both, rather than by whichever was applied last.
        var openFilms = Page(await controller.MineAsync(
            [RequestState.Open],
            [RequestedItemKind.Movie],
            take: RequestsController.MaximumPageSize,
            cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Assert.All(openFilms.Requests, row =>
        {
            Assert.Equal(RequestState.Open, row.State);
            Assert.Equal(RequestedItemKind.Movie, row.Kind);
        });
        Assert.True(openFilms.MatchCount <= Math.Min(open.MatchCount, films.MatchCount));
    }

    /// <summary>
    /// Each order is asked for in both directions and the rows come back in it, with the descending
    /// answer being the ascending one read backwards. The tiebreak is what makes that true of
    /// requests that compare equal under the chosen column.
    /// </summary>
    /// <param name="order">The order asked for.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData(RequestQueryOrder.RequestedAt)]
    [InlineData(RequestQueryOrder.StateChangedAt)]
    [InlineData(RequestQueryOrder.DisplayTitle)]
    public async Task EachOrderIsAnsweredAndItsReverseIsTheSameOrderBackwards(RequestQueryOrder order)
    {
        var store = new InMemoryRequestStore();
        await ASpreadOfRequestsAsync(store).ConfigureAwait(true);
        var controller = ControllerFor(store, Reader);

        var up = Page(await controller.MineAsync(
            order: order,
            take: RequestsController.MaximumPageSize,
            cancellationToken: CancellationToken.None).ConfigureAwait(true));

        var down = Page(await controller.MineAsync(
            order: order,
            descending: true,
            take: RequestsController.MaximumPageSize,
            cancellationToken: CancellationToken.None).ConfigureAwait(true));

        Assert.NotEmpty(up.Requests);
        Assert.Equal(
            up.Requests.Select(row => row.Id).Reverse(),
            down.Requests.Select(row => row.Id));

        var ascending = order switch
        {
            RequestQueryOrder.RequestedAt => up.Requests.OrderBy(row => row.RequestedAt).ThenBy(row => row.Id),
            RequestQueryOrder.StateChangedAt => up.Requests.OrderBy(row => row.StateChangedAt).ThenBy(row => row.Id),
            _ => up.Requests.OrderBy(row => row.DisplayTitle, StringComparer.InvariantCulture).ThenBy(row => row.Id)
        };

        Assert.Equal(ascending.Select(row => row.Id), up.Requests.Select(row => row.Id));
    }

    /// <summary>
    /// A parameter that cannot be answered is refused with the field named, rather than answered
    /// with whatever it happens to match. A state outside the enumeration is the one worth having:
    /// it binds as a number, it matches nothing, and an empty page reads exactly like an empty
    /// queue.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AParameterThatCannotBeAnsweredIsRefusedWithTheFieldNamed()
    {
        var store = new InMemoryRequestStore();
        await ASpreadOfRequestsAsync(store).ConfigureAwait(true);
        var controller = ControllerFor(store, Reader);

        var refusedState = Refusal(await controller.MineAsync([(RequestState)99], cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Assert.Equal("state", refusedState.Field, StringComparer.Ordinal);

        var refusedKind = Refusal(await controller.MineAsync(kind: [(RequestedItemKind)99], cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Assert.Equal("kind", refusedKind.Field, StringComparer.Ordinal);

        var refusedOrder = Refusal(await controller.MineAsync(order: (RequestQueryOrder)99, cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Assert.Equal("order", refusedOrder.Field, StringComparer.Ordinal);

        var refusedSkip = Refusal(await controller.MineAsync(skip: -1, cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Assert.Equal("skip", refusedSkip.Field, StringComparer.Ordinal);

        var refusedTake = Refusal(await controller.MineAsync(take: -1, cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Assert.Equal("take", refusedTake.Field, StringComparer.Ordinal);

        // Asked for more than a page holds, and refused rather than quietly given fewer.
        var refusedTooMany = Refusal(await controller.MineAsync(take: RequestsController.MaximumPageSize + 1, cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Assert.Equal("take", refusedTooMany.Field, StringComparer.Ordinal);
    }

    /// <summary>
    /// The same refusals on the queue endpoint. Two endpoints reading one set of parameters is two
    /// places a check can be dropped from, so both are asked.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheQueueRefusesTheSameParameters()
    {
        var store = new InMemoryRequestStore();
        await ASpreadOfRequestsAsync(store).ConfigureAwait(true);
        var controller = ControllerFor(store, Reader);

        var result = await controller.QueueAsync(
            [(RequestState)99],
            cancellationToken: CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("state", Field(result), StringComparer.Ordinal);

        result = await controller.QueueAsync(
            take: RequestsController.MaximumPageSize + 1,
            cancellationToken: CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("take", Field(result), StringComparer.Ordinal);
    }

    /// <summary>
    /// The queue answers with everything, which is what the elevation on it is for, and every row
    /// carries the revision the store has it at.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheQueueHoldsEverybodysRequestsAndEachRowCarriesItsRevision()
    {
        var store = new InMemoryRequestStore();
        var theirs = await ASpreadOfRequestsAsync(store).ConfigureAwait(true);
        var controller = ControllerFor(store, Reader);

        var result = await controller.QueueAsync(
            take: RequestsController.MaximumPageSize,
            cancellationToken: CancellationToken.None).ConfigureAwait(true);
        var page = Assert.IsType<RequestsPage<QueuedRequest>>(Assert.IsType<OkObjectResult>(result.Result).Value);

        var held = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(held.Count, page.MatchCount);
        Assert.True(page.MatchCount > theirs.Count, "The queue holds no request the reader is not waiting for, so this proves nothing.");
        Assert.All(page.Requests, row => Assert.True(row.Revision >= 1));
        Assert.Contains(page.Requests, row => row.RequestedByUserId == SomebodyElse);
    }

    /// <summary>
    /// The queue endpoint carries the elevation policy and the endpoint a user reaches carries the
    /// signed-in one, read off the built assembly.
    /// <para>
    /// This is the whole of what the suite says about who may reach the queue. The server evaluates
    /// the policy and there is no server here, which the headless rule in <c>docs/testing.md</c>
    /// settles and whose refusal list names this with its replacement. That every endpoint carries a
    /// policy of its own at all is <see cref="EndpointPolicyTests"/>; this leg is that these two
    /// carry the two different ones, which is what makes the queue the elevated read.
    /// </para>
    /// </summary>
    [Fact]
    public void TheQueueEndpointCarriesTheElevationPolicyAndTheOwnRequestsEndpointDoesNot()
    {
        var queue = typeof(RequestsController).GetMethod(nameof(RequestsController.QueueAsync));
        var mine = typeof(RequestsController).GetMethod(nameof(RequestsController.MineAsync));

        Assert.NotNull(queue);
        Assert.NotNull(mine);

        Assert.Equal(
            ["RequiresElevation"],
            queue.GetCustomAttributes<AuthorizeAttribute>(inherit: false).Select(attribute => attribute.Policy));

        // No policy on the endpoint a user reaches, which is the server's default policy rather than
        // a missing one: the server registers no name for "any signed-in person", so naming one is
        // how this plugin came to answer 500 to every caller. The attribute is there, which is the
        // difference between the default and nothing at all, and EndpointPolicyTests holds that for
        // every endpoint rather than for these two.
        Assert.Equal(
            new string?[] { null },
            mine.GetCustomAttributes<AuthorizeAttribute>(inherit: false).Select(attribute => attribute.Policy));

        // The controller's own attribute is the floor under both, so an endpoint that ever lost its
        // own would still need a signed-in caller rather than being open.
        Assert.Equal(
            new string?[] { null },
            typeof(RequestsController).GetCustomAttributes<AuthorizeAttribute>(inherit: false).Select(attribute => attribute.Policy));
    }

    /// <summary>
    /// A call that authenticates and names nobody is refused rather than answered with an empty
    /// page. There is no "own" for an API key, and an empty page would say there are none.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ACallNamingNobodyIsRefusedRatherThanAnsweredWithAnEmptyPage()
    {
        var store = new InMemoryRequestStore();
        await ASpreadOfRequestsAsync(store).ConfigureAwait(true);
        var controller = ControllerFor(store, caller: null);

        var refused = Refusal(await controller.MineAsync(cancellationToken: CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(RequestFailureCode.NoUserOnTheCall, refused.Code);
        Assert.Null(refused.Field);
    }

    /// <summary>
    /// Requests from three people, in several states and both kinds, with the reader asking for some
    /// and joining one somebody else asked for.
    /// </summary>
    /// <param name="store">Where they go.</param>
    /// <returns>The identifiers of the requests the reader is waiting for.</returns>
    private async Task<IReadOnlyList<Guid>> ASpreadOfRequestsAsync(InMemoryRequestStore store)
    {
        var asked = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var theirs = new List<Guid>();

        // The reader's own, one per state that a request can be moved into from open, so a state
        // filter has something on both sides of it.
        var open = await Add(store, Reader, asked, RequestedItemKind.Movie, "Zulu", note: "The reader wrote this.").ConfigureAwait(true);
        theirs.Add(open.Id);

        var approved = await Add(store, Reader, asked.AddHours(2), RequestedItemKind.Series, "Alpha").ConfigureAwait(true);
        theirs.Add(approved.Id);
        await Move(store, approved, RequestState.Approved, asked.AddHours(9)).ConfigureAwait(true);

        var declined = await Add(store, Reader, asked.AddHours(3), RequestedItemKind.Movie, "Mike").ConfigureAwait(true);
        theirs.Add(declined.Id);
        await Move(store, declined, RequestState.Declined, asked.AddHours(4)).ConfigureAwait(true);

        // Somebody else's, joined by the reader and then by a third person, so the row the reader
        // sees is one whose requester and whose other joiner are both other people.
        var joined = await Add(store, SomebodyElse, asked.AddHours(1), RequestedItemKind.Series, "Bravo", note: "Somebody else wrote this.").ConfigureAwait(true);
        theirs.Add(joined.Id);
        var withReader = RequestLifecycle.Join(joined, Reader);
        var withBoth = RequestLifecycle.Join(withReader, AThirdPerson);
        await store.ReplaceAsync(withBoth, 1, CancellationToken.None).ConfigureAwait(true);

        // Two the reader has nothing to do with, so the queue is wider than their own list and a
        // filter that stopped narrowing would show them.
        await Add(store, SomebodyElse, asked.AddHours(5), RequestedItemKind.Movie, "Yankee").ConfigureAwait(true);
        await Add(store, AThirdPerson, asked.AddHours(6), RequestedItemKind.Series, "Charlie").ConfigureAwait(true);

        return theirs;
    }

    /// <summary>
    /// A request in the store, made by the given person.
    /// </summary>
    /// <param name="store">Where it goes.</param>
    /// <param name="asker">Who asked.</param>
    /// <param name="asked">When.</param>
    /// <param name="kind">What sort of thing.</param>
    /// <param name="title">The title, which is also what the title order is read from.</param>
    /// <param name="note">What they wrote, where they wrote anything.</param>
    /// <returns>The request as it was stored.</returns>
    private async Task<MediaRequest> Add(
        InMemoryRequestStore store,
        Guid asker,
        DateTimeOffset asked,
        RequestedItemKind kind,
        string title,
        string? note = null)
    {
        var request = new MediaRequest
        {
            Id = _identifiers.NewId(),
            RequestedByUserId = asker,
            RequestedAt = asked,
            StateChangedAt = asked,
            Kind = kind,
            DisplayTitle = title,

            // Named by a provider, because a request carrying no identifier cannot be approved: the
            // model refuses the moves that need one, and a fixture that could not be moved would
            // leave the state filter with nothing on one side of it.
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = title },
            RequesterNote = note
        };

        var stored = await store.AddAsync(request, CancellationToken.None).ConfigureAwait(true);
        return stored.Request;
    }

    /// <summary>
    /// Moves a request, so the state filter and the moved-at order have something to work on.
    /// </summary>
    /// <param name="store">Where it is.</param>
    /// <param name="request">The request.</param>
    /// <param name="state">Where it goes.</param>
    /// <param name="at">When it moved.</param>
    /// <returns>A task that completes when the store holds the move.</returns>
    private static async Task Move(InMemoryRequestStore store, MediaRequest request, RequestState state, DateTimeOffset at)
    {
        var operator_ = RequestCaller.Administrator(new Guid("c1000000-0000-0000-0000-0000000000ad"));

        // A decline carries a reason and is made through its own verb, because a decline with no
        // reason reads as arbitrary to the person who asked.
        var moved = state == RequestState.Declined
            ? RequestLifecycle.Decline(request, DeclineReason.NotWanted, "The operator wrote this.", at, operator_)
            : RequestLifecycle.Move(request, state, at, operator_);

        await store.ReplaceAsync(moved, 1, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// The page, with the status code checked.
    /// </summary>
    /// <param name="answered">What the action returned.</param>
    /// <returns>The page.</returns>
    private static RequestsPage<MyRequest> Page(ActionResult<RequestsPage<MyRequest>> answered)
    {
        var result = Assert.IsType<OkObjectResult>(answered.Result);
        Assert.Equal(200, result.StatusCode);
        return Assert.IsType<RequestsPage<MyRequest>>(result.Value);
    }

    /// <summary>
    /// The refusal, with the status code and the code in the body checked.
    /// </summary>
    /// <param name="answered">What the action returned.</param>
    /// <returns>The refusal.</returns>
    private static RequestFailure Refusal(ActionResult<RequestsPage<MyRequest>> answered)
    {
        var result = Assert.IsAssignableFrom<ObjectResult>(answered.Result);
        var failure = Assert.IsType<RequestFailure>(result.Value);
        Assert.Equal(RequestFailure.StatusFor(failure.Code), result.StatusCode);
        return failure;
    }

    /// <summary>
    /// The field a refused query named, with the status code and the code checked.
    /// </summary>
    /// <param name="answered">What the action returned.</param>
    /// <returns>The field.</returns>
    private static string? Field(ActionResult<RequestsPage<QueuedRequest>> answered)
    {
        var result = Assert.IsAssignableFrom<ObjectResult>(answered.Result);
        var failure = Assert.IsType<RequestFailure>(result.Value);
        Assert.Equal(RequestFailureCode.InvalidBody, failure.Code);
        Assert.Equal(400, result.StatusCode);
        return failure.Field;
    }

    /// <summary>
    /// A controller wired to one store and one identity.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="caller">Who the server says is calling.</param>
    /// <returns>The controller under test.</returns>
    private RequestsController ControllerFor(IRequestStore store, Guid? caller)
        => new RequestsController(
            store,
            new TestClock(new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero)),
            _identifiers,
            new FakeCallerIdentity(caller));
}

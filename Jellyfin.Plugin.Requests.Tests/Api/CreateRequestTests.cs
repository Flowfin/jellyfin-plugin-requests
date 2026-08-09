using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.Json;
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
/// Asking for something over the API.
/// <para>
/// The action is called directly rather than through a server. The headless rule refuses a running
/// Jellyfin, and what these judge is what the endpoint does with a body and an identity, which is
/// the whole of its behaviour. What that leaves out is named where it matters: the model binder is
/// not exercised by the framework, so the leg about a body naming a different user deserialises the
/// bytes with the same serialiser the server's binder uses and asserts on the result, rather than
/// claiming a round trip that did not happen.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class CreateRequestTests
{
    /// <summary>
    /// How the server's binder reads a body, which is System.Text.Json with the casing the framework
    /// uses. Held once so the leg below measures the binder's behaviour rather than an option set
    /// invented per call.
    /// </summary>
    private static readonly JsonSerializerOptions AsTheBinderReadsIt = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Guid Asker = new Guid("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondPerson = new Guid("a1000000-0000-0000-0000-000000000002");

    /// <summary>
    /// One source of identifiers for the whole of a test, however many controllers it builds. A
    /// source per controller would hand the same first identifier to two of them, and the second
    /// request would be refused by the store for a reason no server would ever produce.
    /// </summary>
    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// Nothing in the queue names this, so a request is made, it is returned with its identifier and
    /// its state, and the store holds it against the person who called.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AskingForSomethingNobodyHasAskedForCreatesIt()
    {
        var store = new InMemoryRequestStore();
        var controller = ControllerFor(store, Asker);

        var answered = await controller.CreateAsync(AFilm(), CancellationToken.None).ConfigureAwait(true);
        var made = Answer(answered, expectedStatus: 201);

        var held = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(RequestOutcome.Created, made.Outcome);
        Assert.Equal(RequestState.Open, made.State);
        Assert.Single(held);
        Assert.Equal(made.Id, held[0].Request.Id);
        Assert.Equal(Asker, held[0].Request.RequestedByUserId);
        Assert.Equal("The Conversation", held[0].Request.DisplayTitle, StringComparer.Ordinal);
    }

    /// <summary>
    /// A second person asking for the same thing joins the request that is already there, and the
    /// answer says so. Two people asking for one film is one request, so the failure this stands for
    /// is a queue that grows a second row an operator has to decide twice.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AskingForSomethingAlreadyInTheQueueJoinsIt()
    {
        var store = new InMemoryRequestStore();
        var first = Answer(
            await ControllerFor(store, Asker).CreateAsync(AFilm(), CancellationToken.None).ConfigureAwait(true),
            expectedStatus: 201);

        var second = Answer(
            await ControllerFor(store, SecondPerson).CreateAsync(AFilm(), CancellationToken.None).ConfigureAwait(true),
            expectedStatus: 200);

        var held = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(RequestOutcome.Joined, second.Outcome);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(held);
        Assert.True(held[0].Request.WasAskedForBy(Asker));
        Assert.True(held[0].Request.WasAskedForBy(SecondPerson));
    }

    /// <summary>
    /// Asking again for something you are already waiting for writes nothing and says as much.
    /// Asking twice is not a second fact about the request, and a store that recorded it would count
    /// clicks rather than people.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AskingAgainForYourOwnRequestChangesNothing()
    {
        var store = new InMemoryRequestStore();
        var controller = ControllerFor(store, Asker);
        await controller.CreateAsync(AFilm(), CancellationToken.None).ConfigureAwait(true);
        var before = (await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true))[0];

        var again = Answer(
            await controller.CreateAsync(AFilm(), CancellationToken.None).ConfigureAwait(true),
            expectedStatus: 200);

        var after = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(RequestOutcome.AlreadyWaiting, again.Outcome);
        Assert.Single(after);
        Assert.Equal(before.Revision, after[0].Revision);
        Assert.Empty(after[0].Request.JoinedByUserIds);
    }

    /// <summary>
    /// A body naming a different user is not honoured, and the reason it is not is that there is
    /// nowhere for it to go.
    /// <para>
    /// Two halves. The type carries no property that could hold a requester, so the serialiser the
    /// server's binder uses has nothing to bind such a field to and drops it. And the request the
    /// endpoint stored is against the caller, which is the outcome that would be wrong if the first
    /// half ever stopped being true.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ABodyNamingADifferentUserIsIgnoredRatherThanHonoured()
    {
        var somebodyElse = new Guid("a1000000-0000-0000-0000-00000000dead");
        // The field a caller would use to file a request as somebody else. It is written out here
        // because there is nothing in the plugin to name: the point of the leg is that no such
        // property exists.
        var sent = string.Concat(
            "{\"kind\":0,\"title\":\"The Conversation\",\"providerIds\":{\"Tmdb\":\"603\"},\"requestedByUserId\":\"",
            somebodyElse.ToString("D", CultureInfo.InvariantCulture),
            "\"}");

        var carriesARequester = typeof(CreateRequestBody)
            .GetProperties()
            .Any(property => property.Name.Contains("User", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Requester", StringComparison.OrdinalIgnoreCase));

        var bound = JsonSerializer.Deserialize<CreateRequestBody>(sent, AsTheBinderReadsIt);

        var store = new InMemoryRequestStore();
        await ControllerFor(store, Asker).CreateAsync(bound!, CancellationToken.None).ConfigureAwait(true);

        var held = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.False(carriesARequester, "The body carries a property that could name a requester.");
        Assert.Equal(Asker, held[0].Request.RequestedByUserId);
        Assert.False(held[0].Request.WasAskedForBy(somebodyElse));
    }

    /// <summary>
    /// A call that authenticated but names no person is refused. An API key reaches this endpoint
    /// under the server's policy and there is nobody to record as having asked, so the alternative
    /// is a request filed against an identifier that is not anybody.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ACallThatNamesNoPersonIsRefused()
    {
        var store = new InMemoryRequestStore();
        var controller = ControllerFor(store, caller: null);

        var refused = Refusal(await controller.CreateAsync(AFilm(), CancellationToken.None).ConfigureAwait(true));

        Assert.Equal("caller", refused.Field, StringComparer.Ordinal);
        Assert.Empty(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>
    /// Every field is validated and the refusal names the field, so a client can put the message
    /// beside the box the person typed in rather than having to read English to find out which one.
    /// </summary>
    /// <param name="field">The field the refusal has to name.</param>
    /// <param name="body">A body that is wrong in exactly that field.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [MemberData(nameof(BodiesThatAreRefused))]
    public async Task AnInvalidBodyIsRefusedWithTheFieldNamed(string field, CreateRequestBody body)
    {
        var store = new InMemoryRequestStore();

        var refused = Refusal(
            await ControllerFor(store, Asker).CreateAsync(body, CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(field, refused.Field, StringComparer.Ordinal);
        Assert.NotEmpty(refused.Reason);
        Assert.Empty(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>
    /// A request nobody can act on any more is not joined. Somebody asking for a film that was
    /// declined last month gets a new request an operator sees, rather than inheriting a refusal they
    /// never saw and never being told anything.
    /// </summary>
    /// <param name="finished">The state the existing request is in.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData(RequestState.Declined)]
    [InlineData(RequestState.Fulfilled)]
    [InlineData(RequestState.Failed)]
    public async Task AFinishedRequestIsNotJoined(RequestState finished)
    {
        var store = new InMemoryRequestStore();
        var existing = Answer(
            await ControllerFor(store, Asker).CreateAsync(AFilm(), CancellationToken.None).ConfigureAwait(true),
            expectedStatus: 201);

        var held = (await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true))[0];
        await store.ReplaceAsync(held.Request with { State = finished }, held.Revision, CancellationToken.None).ConfigureAwait(true);

        var made = Answer(
            await ControllerFor(store, SecondPerson).CreateAsync(AFilm(), CancellationToken.None).ConfigureAwait(true),
            expectedStatus: 201);

        Assert.Equal(RequestOutcome.Created, made.Outcome);
        Assert.NotEqual(existing.Id, made.Id);
        Assert.Equal(2, (await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true)).Count);
    }

    /// <summary>
    /// A series request that names seasons somebody is already waiting for, and seasons nobody is,
    /// creates a request for the ones that are left. Joining the existing one would approve seasons
    /// nobody approved; creating a request for all four would put two people in the queue for the two
    /// that are already there.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AskingForSeasonsPartlyCoveredCreatesARequestForWhatIsLeft()
    {
        var store = new InMemoryRequestStore();
        await ControllerFor(store, Asker)
            .CreateAsync(ASeries([1, 2]), CancellationToken.None).ConfigureAwait(true);

        var made = Answer(
            await ControllerFor(store, SecondPerson).CreateAsync(ASeries([2, 3]), CancellationToken.None).ConfigureAwait(true),
            expectedStatus: 201);

        var held = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);
        var created = held.Single(stored => stored.Request.Id == made.Id);
        Assert.Equal(RequestOutcome.Created, made.Outcome);
        Assert.Equal([3], created.Request.Seasons);
        Assert.Equal(2, held.Count);
    }

    /// <summary>
    /// A series request whose seasons are all already asked for joins the request that covers them,
    /// which is the same rule as a film and is worth its own leg because the comparison that decides
    /// it is not symmetric.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AskingForSeasonsAlreadyCoveredJoins()
    {
        var store = new InMemoryRequestStore();
        var first = Answer(
            await ControllerFor(store, Asker).CreateAsync(ASeries([1, 2, 3]), CancellationToken.None).ConfigureAwait(true),
            expectedStatus: 201);

        var second = Answer(
            await ControllerFor(store, SecondPerson).CreateAsync(ASeries([2]), CancellationToken.None).ConfigureAwait(true),
            expectedStatus: 200);

        Assert.Equal(RequestOutcome.Joined, second.Outcome);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>
    /// A request carrying no identifier is nobody's duplicate, including its own. Two people typing
    /// one title get two requests, because matching them would mean matching on the text they typed,
    /// which is the rule the identity comparison exists to refuse.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TwoRequestsTypedByHandAreTwoRequests()
    {
        var store = new InMemoryRequestStore();
        var typed = new CreateRequestBody { Kind = RequestedItemKind.Movie, Title = "Nosferatu" };

        await ControllerFor(store, Asker).CreateAsync(typed, CancellationToken.None).ConfigureAwait(true);
        var second = Answer(
            await ControllerFor(store, SecondPerson).CreateAsync(typed, CancellationToken.None).ConfigureAwait(true),
            expectedStatus: 201);

        Assert.Equal(RequestOutcome.Created, second.Outcome);
        Assert.Equal(2, (await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true)).Count);
    }

    /// <summary>
    /// The bodies the endpoint refuses, each wrong in one field, so a refusal naming a different one
    /// is a failure rather than a message somebody has to read.
    /// </summary>
    /// <returns>The field that has to be named, and the body that is wrong in it.</returns>
    public static TheoryData<string, CreateRequestBody> BodiesThatAreRefused()
        => new TheoryData<string, CreateRequestBody>
        {
            { nameof(CreateRequestBody.Kind), new CreateRequestBody { Title = "No kind" } },
            { nameof(CreateRequestBody.Kind), new CreateRequestBody { Kind = (RequestedItemKind)97, Title = "Not a kind" } },
            { nameof(CreateRequestBody.Title), new CreateRequestBody { Kind = RequestedItemKind.Movie } },
            { nameof(CreateRequestBody.Title), new CreateRequestBody { Kind = RequestedItemKind.Movie, Title = "   " } },
            {
                nameof(CreateRequestBody.Title),
                new CreateRequestBody { Kind = RequestedItemKind.Movie, Title = new string('t', MediaRequest.NoteMaximumLength + 1) }
            },
            {
                nameof(CreateRequestBody.Note),
                new CreateRequestBody
                {
                    Kind = RequestedItemKind.Movie,
                    Title = "A film",
                    Note = new string('n', MediaRequest.NoteMaximumLength + 1)
                }
            },
            {
                nameof(CreateRequestBody.ProviderIds),
                new CreateRequestBody
                {
                    Kind = RequestedItemKind.Movie,
                    Title = "A film",
                    ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = " " }
                }
            },
            {
                nameof(CreateRequestBody.Seasons),
                new CreateRequestBody { Kind = RequestedItemKind.Movie, Title = "A film", Seasons = [1] }
            },
            {
                nameof(CreateRequestBody.Seasons),
                new CreateRequestBody { Kind = RequestedItemKind.Series, Title = "A show", Seasons = [0] }
            },
            {
                nameof(CreateRequestBody.Seasons),
                new CreateRequestBody { Kind = RequestedItemKind.Series, Title = "A show", Seasons = [2, 2] }
            }
        };

    /// <summary>
    /// A controller wired to one store and one identity, with a clock and an identifier source the
    /// test controls.
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

    /// <summary>
    /// The answer, with the status code checked, so a leg cannot pass on a body that came back under
    /// the wrong one.
    /// </summary>
    /// <param name="answered">What the action returned.</param>
    /// <param name="expectedStatus">The status code it has to have used.</param>
    /// <returns>The answer.</returns>
    private static CreatedRequest Answer(ActionResult<CreatedRequest> answered, int expectedStatus)
    {
        var result = Assert.IsAssignableFrom<ObjectResult>(answered.Result);
        Assert.Equal(expectedStatus, result.StatusCode);
        return Assert.IsType<CreatedRequest>(result.Value);
    }

    /// <summary>
    /// The refusal, with the status code checked.
    /// </summary>
    /// <param name="answered">What the action returned.</param>
    /// <returns>The refusal.</returns>
    private static RequestRefused Refusal(ActionResult<CreatedRequest> answered)
    {
        var result = Assert.IsType<BadRequestObjectResult>(answered.Result);
        Assert.Equal(400, result.StatusCode);
        return Assert.IsType<RequestRefused>(result.Value);
    }

    /// <summary>
    /// A film, named by one provider so it has an identity.
    /// </summary>
    /// <returns>The body.</returns>
    private static CreateRequestBody AFilm() => new CreateRequestBody
    {
        Kind = RequestedItemKind.Movie,
        Title = "The Conversation",
        Year = 1974,
        ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "603" }
    };

    /// <summary>
    /// A series, named by one provider, asking for the given seasons.
    /// </summary>
    /// <param name="seasons">The seasons wanted.</param>
    /// <returns>The body.</returns>
    private static CreateRequestBody ASeries(IReadOnlyList<int> seasons) => new CreateRequestBody
    {
        Kind = RequestedItemKind.Series,
        Title = "The Wire",
        ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tvdb"] = "79126" },
        Seasons = seasons
    };
}

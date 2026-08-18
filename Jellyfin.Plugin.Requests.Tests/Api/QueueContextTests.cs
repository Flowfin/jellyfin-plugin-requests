using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.AspNetCore.Mvc;
using Xunit;

using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests.Api;

/// <summary>
/// The two items <c>docs/queue.md</c> names as things a decision is worse without and the queue
/// answer could not carry: what was already decided about the same work, and how much the person
/// asking is already waiting for.
/// <para>
/// Each is asserted against the endpoint rather than against
/// <see cref="Jellyfin.Plugin.Requests.Api.QueueContext"/> alone, because the failure worth
/// refusing is the queue answering without them rather than the calculation being wrong in
/// isolation. The two legs at the end read the page as the assembly carries it, which is the same
/// bound every check over these assets has: they read the page, they do not run it.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class QueueContextTests
{
    private static readonly Guid Operator = new Guid("e1000000-0000-0000-0000-0000000000ad");
    private static readonly Guid Asker = new Guid("e1000000-0000-0000-0000-000000000001");
    private static readonly Guid SomebodyElse = new Guid("e1000000-0000-0000-0000-000000000002");

    private static readonly DateTimeOffset LongAgo = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Lately = new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A title that was declined once and is asked for again carries the earlier answer, with the
    /// reason and the sentence the operator wrote beside it.
    /// <para>
    /// This is the item the list says an operator most notices the absence of. Without it they
    /// either decline from memory, which fails the first time it is somebody else's, or approve
    /// something that was refused for a reason that has not changed.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ATitleDeclinedBeforeIsBesideTheRequestAskingForItAgain()
    {
        var store = new InMemoryRequestStore();

        var refused = await AddAsync(store, "a", Asker, LongAgo, "Solaris").ConfigureAwait(true);
        await DeclineAsync(store, refused, DeclineReason.CannotBeObtained, "No print anybody sells.")
            .ConfigureAwait(true);

        await AddAsync(store, "b", SomebodyElse, Lately, "Solaris").ConfigureAwait(true);

        var row = Single(await QueueAsync(store).ConfigureAwait(true), Lately);
        var context = Assert.IsType<QueueContext>(row.Context);
        var earlier = Assert.Single(context.EarlierDecisions);

        Assert.Equal(RequestState.Declined, earlier.State);
        Assert.Equal(DeclineReason.CannotBeObtained, earlier.DeclineReason);
        Assert.Equal("No print anybody sells.", earlier.DeclineNote, StringComparer.Ordinal);
        Assert.Equal(refused, earlier.Id);
    }

    /// <summary>
    /// A request nobody has answered is not a decision, and neither is an approved one.
    /// <para>
    /// The near-miss this is aimed at is the obvious implementation, which gathers every other
    /// request naming the same work. An open row for the same title is a state to look at rather
    /// than a decision to weigh, and showing it as one would tell an operator somebody has already
    /// answered this when nobody has.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task NothingUnansweredIsShownAsSomethingSomebodyDecided()
    {
        var store = new InMemoryRequestStore();

        await AddAsync(store, "a", Asker, LongAgo, "Stalker").ConfigureAwait(true);

        var approved = await AddAsync(store, "b", SomebodyElse, LongAgo.AddDays(1), "Stalker").ConfigureAwait(true);
        await MoveAsync(store, approved, RequestState.Approved).ConfigureAwait(true);

        var asking = await AddAsync(store, "c", SomebodyElse, Lately, "Stalker").ConfigureAwait(true);

        var row = Single(await QueueAsync(store).ConfigureAwait(true), Lately);

        Assert.Equal(asking, row.Id);
        Assert.Empty(Assert.IsType<QueueContext>(row.Context).EarlierDecisions);
    }

    /// <summary>
    /// A series decided on other seasons is still the same work, and the decision says which seasons
    /// it covered.
    /// <para>
    /// This is where the two questions in <see cref="RequestIdentity"/> come apart, and the reason
    /// this column does not reuse <see cref="RequestIdentity.Compare"/>. That comparison calls a
    /// request for season five different from a request for seasons one and two, which is right for
    /// deciding whether a new asker joins an existing request and wrong for an operator who wants to
    /// know what has been decided about the series.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ASeriesDecidedOnOtherSeasonsIsStillTheSameWork()
    {
        var store = new InMemoryRequestStore();

        var refused = await AddAsync(store, "a", Asker, LongAgo, "Twin Peaks", RequestedItemKind.Series, [1, 2])
            .ConfigureAwait(true);
        await DeclineAsync(store, refused, DeclineReason.NoRoomForIt, null).ConfigureAwait(true);

        await AddAsync(store, "b", SomebodyElse, Lately, "Twin Peaks", RequestedItemKind.Series, [5])
            .ConfigureAwait(true);

        var row = Single(await QueueAsync(store).ConfigureAwait(true), Lately);
        var earlier = Assert.Single(Assert.IsType<QueueContext>(row.Context).EarlierDecisions);

        Assert.Equal(RequestState.Declined, earlier.State);
        Assert.Equal([1, 2], earlier.Seasons);

        // The rule the column is built on, held directly as well, because the leg above would also
        // pass on an implementation that compared nothing but the provider identifier and forgot
        // that a film and a series can carry the same number under one provider.
        Assert.Equal(RequestMatch.Different, RequestIdentity.Compare(Held(store, refused), Held(store, row.Id)));
    }

    /// <summary>
    /// The decisions beside a row are newest first, so the answer an operator is most likely to be
    /// asked about is the one at the top of the cell.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheDecisionsBesideARowAreNewestFirst()
    {
        var store = new InMemoryRequestStore();

        var first = await AddAsync(store, "a", Asker, LongAgo, "Andrei Rublev").ConfigureAwait(true);
        await DeclineAsync(store, first, DeclineReason.NotWanted, null).ConfigureAwait(true);

        var second = await AddAsync(store, "b", Asker, LongAgo.AddDays(30), "Andrei Rublev").ConfigureAwait(true);
        await MoveAsync(store, second, RequestState.Fulfilled).ConfigureAwait(true);

        await AddAsync(store, "c", SomebodyElse, Lately, "Andrei Rublev").ConfigureAwait(true);

        var row = Single(await QueueAsync(store).ConfigureAwait(true), Lately);
        var decisions = Assert.IsType<QueueContext>(row.Context).EarlierDecisions;

        Assert.Equal([second, first], decisions.Select(decision => decision.Id));
    }

    /// <summary>
    /// The number beside a row is what the quota would count: the person's open and approved
    /// requests, joined ones included, and nothing that has been answered.
    /// <para>
    /// It is asserted against <see cref="RequestQuota.CountedIn"/> rather than against a number
    /// written here, so the column and the limit somebody is refused against cannot drift into two
    /// rules. A hard-coded number would go on passing the day the quota stopped counting joins.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheNumberBesideARowIsWhatTheQuotaWouldCount()
    {
        var store = new InMemoryRequestStore();

        var answered = await AddAsync(store, "a", Asker, LongAgo, "Nostalghia").ConfigureAwait(true);
        await DeclineAsync(store, answered, DeclineReason.NotWanted, null).ConfigureAwait(true);

        var approved = await AddAsync(store, "b", Asker, LongAgo.AddDays(2), "The Mirror").ConfigureAwait(true);
        await MoveAsync(store, approved, RequestState.Approved).ConfigureAwait(true);

        // One somebody else asked for and this person joined. It is one more thing they are waiting
        // for and the queue holds it for them as much as for whoever asked first.
        var joined = await AddAsync(store, "c", SomebodyElse, LongAgo.AddDays(3), "Ivan's Childhood")
            .ConfigureAwait(true);
        var held = Held(store, joined);
        await store.ReplaceAsync(held with { JoinedByUserIds = [Asker] }, 1, CancellationToken.None)
            .ConfigureAwait(true);

        await AddAsync(store, "d", Asker, Lately, "Solaris").ConfigureAwait(true);

        var row = Single(await QueueAsync(store).ConfigureAwait(true), Lately);
        var counted = Assert.IsType<QueueContext>(row.Context).OpenRequestsByRequester;

        var theirs = await store.FindForUserAsync(Asker, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(RequestQuota.CountedIn(theirs.Select(stored => stored.Request)), counted);
        Assert.Equal(3, counted);
    }

    /// <summary>
    /// The answer to a move carries no context rather than an empty one.
    /// <para>
    /// An empty list and a list nobody built read identically to a page, and the sentence they would
    /// produce is the wrong one: it would tell an operator that nothing has ever been decided about
    /// a title on a route that did not look. The distinction is the whole reason the shape is
    /// nullable.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AMoveHandsBackARequestWithNoContextRatherThanAnEmptyOne()
    {
        var store = new InMemoryRequestStore();

        var refused = await AddAsync(store, "a", Asker, LongAgo, "Solaris").ConfigureAwait(true);
        await DeclineAsync(store, refused, DeclineReason.NotWanted, null).ConfigureAwait(true);

        var asking = await AddAsync(store, "b", SomebodyElse, Lately, "Solaris").ConfigureAwait(true);

        var answered = await Controller(store)
            .ApproveAsync(asking, new ApproveRequestBody { Revision = 1 }, CancellationToken.None)
            .ConfigureAwait(true);

        var moved = Assert.IsType<QueuedRequest>(Assert.IsAssignableFrom<ObjectResult>(answered.Result).Value);

        Assert.Equal(RequestState.Approved, moved.State);
        Assert.Null(moved.Context);
    }

    /// <summary>
    /// The queue page draws both items and asks the server for the names behind the identifiers.
    /// <para>
    /// Read out of the page the assembly carries. What this refuses is the change that widens the
    /// answer and leaves the page drawing what it drew before, which builds clean, formats clean and
    /// shows an operator nothing new.
    /// </para>
    /// </summary>
    [Fact]
    public void TheQueuePageDrawsBothItemsAndAsksWhoTheIdentifiersAre()
    {
        var page = Asset("Web.queue.html");

        foreach (var drawn in new[]
        {
            "request.Context",
            "EarlierDecisions",
            "OpenRequestsByRequester",
            "queue.column.askedBefore",
            "queue.column.waitingFor",
            "ApiClient.getUsers()"
        })
        {
            Assert.Contains(drawn, page, StringComparison.Ordinal);
        }

        // Drawn through the one call that puts a string in as text, which is what
        // PageRulesTests holds for the rest of the row and what a decline note written by an
        // operator needs here too.
        Assert.Contains("cell(row, askedBefore(request))", page, StringComparison.Ordinal);
        Assert.Contains("cell(row, waitingFor(request))", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing on the page a person opens for their own requests asks this server who anybody is.
    /// <para>
    /// The names arrive on the queue, which is administrators only, and that is where they stop.
    /// A user list fetched by the page any signed-in person can open would be this plugin handing
    /// out the roll of everybody on the server, which is a disclosure nothing on that page needs.
    /// </para>
    /// </summary>
    [Fact]
    public void NothingOnThePageAPersonOpensAsksThisServerWhoAnybodyIs()
    {
        foreach (var asset in new[] { "Web.mine.html", "Web.shell.js" })
        {
            Assert.DoesNotContain("getUsers", Asset(asset), StringComparison.Ordinal);
        }

        // The queue does ask, so this leg cannot pass by reading nothing.
        Assert.Contains("getUsers", Asset("Web.queue.html"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The row for a given moment, with the status code checked.
    /// </summary>
    /// <param name="answered">What the queue action returned.</param>
    /// <param name="asked">When the wanted row was asked for.</param>
    /// <returns>The row.</returns>
    private static QueuedRequest Single(ActionResult<RequestsPage<QueuedRequest>> answered, DateTimeOffset asked)
    {
        var result = Assert.IsType<OkObjectResult>(answered.Result);
        Assert.Equal(200, result.StatusCode);

        var page = Assert.IsType<RequestsPage<QueuedRequest>>(result.Value);

        return page.Requests.Single(request => request.RequestedAt == asked);
    }

    /// <summary>
    /// The whole queue, in every state.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <returns>What the action returned.</returns>
    private static Task<ActionResult<RequestsPage<QueuedRequest>>> QueueAsync(InMemoryRequestStore store)
        => Controller(store).QueueAsync(cancellationToken: CancellationToken.None);

    /// <summary>
    /// One request, made under a provider identifier that names the title.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="tail">What makes this request's identifier distinct.</param>
    /// <param name="by">Who is asking.</param>
    /// <param name="at">When they asked.</param>
    /// <param name="title">The title, which is also the provider value, so two asks for one title
    /// share an identifier the way two real ones would.</param>
    /// <param name="kind">What sort of thing it is.</param>
    /// <param name="seasons">The seasons asked for, where it is a series.</param>
    /// <returns>The identifier of the request that was added.</returns>
    private static async Task<Guid> AddAsync(
        InMemoryRequestStore store,
        string tail,
        Guid by,
        DateTimeOffset at,
        string title,
        RequestedItemKind kind = RequestedItemKind.Movie,
        IReadOnlyList<int>? seasons = null)
    {
        var stored = await store.AddAsync(
            new MediaRequest
            {
                Id = new Guid("e200000" + tail + "-0000-0000-0000-000000000000"),
                RequestedByUserId = by,
                RequestedAt = at,
                StateChangedAt = at,
                Kind = kind,
                DisplayTitle = title,
                Seasons = seasons ?? [],
                ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = title }
            },
            CancellationToken.None).ConfigureAwait(false);

        return stored.Request.Id;
    }

    /// <summary>
    /// Declines a request as an operator would.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="id">The request being declined.</param>
    /// <param name="reason">Why.</param>
    /// <param name="note">What was written beside it.</param>
    /// <returns>A task that completes when the write has been made.</returns>
    private static async Task DeclineAsync(InMemoryRequestStore store, Guid id, DeclineReason reason, string? note)
    {
        var held = Held(store, id);

        var moved = RequestLifecycle.Decline(
            held,
            reason,
            note,
            held.RequestedAt.AddDays(1),
            RequestCaller.Administrator(Operator));

        await store.ReplaceAsync(moved, 1, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves a request to a state that is not a decline.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="id">The request being moved.</param>
    /// <param name="to">Where it is going.</param>
    /// <returns>A task that completes when the write has been made.</returns>
    private static async Task MoveAsync(InMemoryRequestStore store, Guid id, RequestState to)
    {
        var held = Held(store, id);

        var moved = to == RequestState.Fulfilled
            ? RequestLifecycle.Move(
                RequestLifecycle.Move(held, RequestState.Approved, held.RequestedAt.AddDays(1), RequestCaller.Administrator(Operator)),
                RequestState.Fulfilled,
                held.RequestedAt.AddDays(2),
                RequestCaller.Plugin)
            : RequestLifecycle.Move(held, to, held.RequestedAt.AddDays(1), RequestCaller.Administrator(Operator));

        await store.ReplaceAsync(moved, 1, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// One request as the store holds it now.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="id">The request wanted.</param>
    /// <returns>The request.</returns>
    private static MediaRequest Held(InMemoryRequestStore store, Guid id)
        => store.GetAsync(id, CancellationToken.None).GetAwaiter().GetResult()?.Request
            ?? throw new InvalidOperationException($"The store holds no request under {id}.");

    /// <summary>
    /// One embedded asset, as the assembly carries it.
    /// </summary>
    /// <param name="ending">What the resource name ends with.</param>
    /// <returns>The asset.</returns>
    private static string Asset(string ending)
    {
        var assembly = typeof(PluginUnderTest).Assembly;

        var resource = assembly
            .GetManifestResourceNames()
            .Single(name => name.EndsWith(ending, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The assembly carries no resource named {resource}.");

        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// A controller reading one store, as an administrator.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <returns>The controller under test.</returns>
    private static RequestsController Controller(InMemoryRequestStore store)
        => new RequestsController(
            store,
            new TestClock(new DateTimeOffset(2026, 8, 17, 8, 0, 0, TimeSpan.Zero)),
            new SequentialIdentifierSource(),
            new FakeCallerIdentity(Operator),
            new FakeInstallSettings(),
            new RecordingJournal(),
            new RecordingSink());
}

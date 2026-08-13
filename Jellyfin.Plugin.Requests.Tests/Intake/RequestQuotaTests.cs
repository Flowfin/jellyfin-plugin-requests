using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Intake;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Intake;

/// <summary>
/// How many things one person may be waiting for, enforced where a request is made rather than at
/// each surface that makes one.
/// <para>
/// The legs below call the intake directly. That is the point of them: a quota checked in an
/// endpoint is a quota the seam does not have, and a second surface added later is a second place
/// somebody has to remember. What these hold is the property that there is one place, so the
/// question "can this caller get past it" has one answer.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class RequestQuotaTests
{
    private static readonly Guid Asker = new Guid("c5000000-0000-0000-0000-000000000001");
    private static readonly Guid SomebodyElse = new Guid("c5000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// Somebody at their limit is refused, and the refusal carries what they hold and what the limit
    /// is rather than a sentence a surface would have to read.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SomebodyAtTheirLimitIsRefusedByTheIntakeItself()
    {
        var store = new InMemoryRequestStore();

        await FillAsync(store, Asker, RequestState.Open, 2).ConfigureAwait(true);

        var refused = await Assert.ThrowsAsync<RequestQuotaReachedException>(
            () => Intake(store, quota: 2).AskAsync(Something(Asker), RequestCaller.User(Asker), CancellationToken.None))
            .ConfigureAwait(true);

        Assert.Equal(2, refused.Held);
        Assert.Equal(2, refused.Limit);

        // Nothing was written. A refusal that stored the request and then complained would be worse
        // than no limit at all, because the queue would disagree with what the caller was told.
        Assert.Equal(2, (await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true)).Count);
    }

    /// <summary>
    /// One place under their limit is one more thing they may ask for.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SomebodyUnderTheirLimitIsNotRefused()
    {
        var store = new InMemoryRequestStore();

        await FillAsync(store, Asker, RequestState.Open, 1).ConfigureAwait(true);

        var asked = await Intake(store, quota: 2)
            .AskAsync(Something(Asker), RequestCaller.User(Asker), CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(IntakeOutcome.Created, asked.Outcome);
    }

    /// <summary>
    /// A finished request frees the place it held. This is the difference between a quota and a
    /// lifetime allowance, and getting it wrong means a person whose asks were all answered can
    /// never ask again.
    /// </summary>
    /// <param name="finished">The state the answered request is in.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData(RequestState.Fulfilled)]
    [InlineData(RequestState.Declined)]
    [InlineData(RequestState.Failed)]
    public async Task AFinishedRequestFreesThePlaceItHeld(RequestState finished)
    {
        var store = new InMemoryRequestStore();

        await FillAsync(store, Asker, finished, 2).ConfigureAwait(true);

        var asked = await Intake(store, quota: 2)
            .AskAsync(Something(Asker), RequestCaller.User(Asker), CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(IntakeOutcome.Created, asked.Outcome);
    }

    /// <summary>
    /// An approved request is not finished and still holds its place. It is the state this is
    /// easiest to get wrong in: the operator has answered, and the thing has not arrived.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnApprovedRequestStillHoldsItsPlace()
    {
        var store = new InMemoryRequestStore();

        await FillAsync(store, Asker, RequestState.Approved, 1).ConfigureAwait(true);

        await Assert.ThrowsAsync<RequestQuotaReachedException>(
            () => Intake(store, quota: 1).AskAsync(Something(Asker), RequestCaller.User(Asker), CancellationToken.None))
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Joining somebody else's request is one more thing this person is waiting for, so it is bound
    /// by the quota as well. Without this leg the limit is one anybody can walk around by asking for
    /// what is already in the queue.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task JoiningSomebodyElsesRequestIsBoundByItToo()
    {
        var store = new InMemoryRequestStore();

        await FillAsync(store, Asker, RequestState.Open, 1).ConfigureAwait(true);

        var theirs = Something(SomebodyElse);
        await store.AddAsync(theirs, CancellationToken.None).ConfigureAwait(true);

        await Assert.ThrowsAsync<RequestQuotaReachedException>(
            () => Intake(store, quota: 1).AskAsync(
                theirs with { Id = _identifiers.NewId(), RequestedByUserId = Asker },
                RequestCaller.User(Asker),
                CancellationToken.None))
            .ConfigureAwait(true);

        var held = await store.FindForUserAsync(Asker, CancellationToken.None).ConfigureAwait(true);
        Assert.Single(held);
    }

    /// <summary>
    /// Asking again for something they are already waiting for is never refused for the quota. It
    /// takes no new place in the queue, and a person who asks twice out of impatience being told
    /// they are at their limit is a limit that reads as broken.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AskingAgainForSomethingTheyAreAlreadyWaitingForIsNotRefused()
    {
        var store = new InMemoryRequestStore();

        var already = Something(Asker);
        await store.AddAsync(already, CancellationToken.None).ConfigureAwait(true);

        var asked = await Intake(store, quota: 1)
            .AskAsync(
                already with { Id = _identifiers.NewId() },
                RequestCaller.User(Asker),
                CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(IntakeOutcome.AlreadyWaiting, asked.Outcome);
    }

    /// <summary>
    /// An administrator is not subject to it, and neither is the plugin acting on its own
    /// observation. No surface hands either of those in when something is asked for today, so this
    /// is the leg that holds the rule at the one place it is decided.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnAdministratorAndThePluginAreNotSubjectToIt()
    {
        var store = new InMemoryRequestStore();

        await FillAsync(store, Asker, RequestState.Open, 1).ConfigureAwait(true);

        var byAdministrator = await Intake(store, quota: 1)
            .AskAsync(Something(Asker), RequestCaller.Administrator(Asker), CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(IntakeOutcome.Created, byAdministrator.Outcome);

        var byPlugin = await Intake(store, quota: 1)
            .AskAsync(Something(Asker), RequestCaller.Plugin, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(IntakeOutcome.Created, byPlugin.Outcome);
    }

    /// <summary>
    /// The count is of that person and nobody else. A quota measured over the whole queue would
    /// refuse a person who has asked for nothing on a busy server.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SomebodyElsesRequestsDoNotCountAgainstThem()
    {
        var store = new InMemoryRequestStore();

        await FillAsync(store, SomebodyElse, RequestState.Open, 5).ConfigureAwait(true);

        var asked = await Intake(store, quota: 1)
            .AskAsync(Something(Asker), RequestCaller.User(Asker), CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(IntakeOutcome.Created, asked.Outcome);
    }

    /// <summary>
    /// The two states that count are the two the model says count, read off the model rather than
    /// restated here, so a state added to the lifecycle without a decision about the quota shows up
    /// as a difference instead of being counted or not by accident.
    /// </summary>
    [Fact]
    public void TheStatesThatCountAreTheOnesStillWaitingForAnAnswer()
    {
        var counted = Enum.GetValues<RequestState>()
            .Where(state => RequestQuota.CountsAgainstIt(A(Asker) with { State = state }))
            .ToArray();

        Assert.Equal([RequestState.Open, RequestState.Approved], counted);
    }

    /// <summary>
    /// A quota nobody filled in refuses rather than admits. No configuration produces one, because
    /// the settings refuse a stored value below one on the way in and on the way out, and the
    /// direction is what matters: a limit that defaults to nothing is not a limit.
    /// </summary>
    [Fact]
    public void AQuotaNobodyFilledInRefuses()
    {
        Assert.True(default(RequestQuota).IsReachedBy(0));
        Assert.False(new RequestQuota(1).IsReachedBy(0));
        Assert.True(new RequestQuota(1).IsReachedBy(1));
    }

    /// <summary>
    /// The intake over one store, with the quota this install is set to.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="quota">How many open or approved requests one person may hold.</param>
    /// <returns>The intake under test.</returns>
    private static RequestIntake Intake(IRequestStore store, int quota)
        => new RequestIntake(
            store,
            new FakeInstallSettings(new PluginConfiguration { OpenRequestsPerUser = quota }));

    /// <summary>
    /// A request as somebody asked for it.
    /// </summary>
    /// <param name="asker">Who asked.</param>
    /// <returns>The request.</returns>
    private static MediaRequest A(Guid asker) => new MediaRequest
    {
        Id = new Guid("c5000000-0000-0000-0000-0000000000ff"),
        RequestedByUserId = asker,
        RequestedAt = Noon,
        StateChangedAt = Noon,
        Kind = RequestedItemKind.Movie,
        DisplayTitle = "Sans Soleil",
        DisplayYear = 1983
    };

    /// <summary>
    /// Requests for one person, already in the store and in one state.
    /// </summary>
    /// <param name="store">Where they go.</param>
    /// <param name="asker">Who asked for them.</param>
    /// <param name="state">What state they are in.</param>
    /// <param name="howMany">How many to write.</param>
    /// <returns>A task that completes when they are in the store.</returns>
    private async Task FillAsync(InMemoryRequestStore store, Guid asker, RequestState state, int howMany)
    {
        for (var index = 0; index < howMany; index++)
        {
            await store.AddAsync(
                Something(asker) with { State = state, StateChangedAt = Noon },
                CancellationToken.None).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// A request for something nothing else in these tests asks for, so a leg about counting is
    /// never a leg about joining.
    /// </summary>
    /// <param name="asker">Who is asking.</param>
    /// <returns>The request.</returns>
    private MediaRequest Something(Guid asker)
    {
        var id = _identifiers.NewId();

        return A(asker) with
        {
            Id = id,
            DisplayTitle = "Title " + id.ToString("N", CultureInfo.InvariantCulture),
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Tmdb"] = id.ToString("N", CultureInfo.InvariantCulture)
            }
        };
    }
}

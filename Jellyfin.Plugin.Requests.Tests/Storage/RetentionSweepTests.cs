using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Storage;

/// <summary>
/// The retention period enforced rather than declared, which is #49's second condition.
/// <para>
/// Every period here is proven by moving the injected clock, so nothing waits and a slow machine
/// gets the same answer as a fast one. The dates are the store's and the model's own
/// <see cref="MediaRequest.StateChangedAt"/>, never the wall clock.
/// </para>
/// </summary>
public class RetentionSweepTests
{
    private static readonly DateTimeOffset Asked = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Requester = new Guid("7a4e2f18-6b0c-4d39-95e7-1f8a3c6d2b40");
    private static readonly Guid Operator = new Guid("2c9f5a31-0d84-49b6-8f2e-5b7c1a0e6d93");

    /// <summary>
    /// A fulfilled request older than the period is gone, and the run says it removed one. The
    /// clock is moved one day past the configured year rather than to some distant date, so what
    /// the assertion rests on is the period and not the size of the jump.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AFulfilledRequestKeptPastThePeriodIsRemoved()
    {
        var store = new InMemoryRequestStore();
        var clock = new TestClock(Asked);
        var settings = new FakeInstallSettings();
        var sweep = Sweep(store, settings, clock);

        await store.AddAsync(Finished(RequestState.Fulfilled), CancellationToken.None);

        clock.Advance(TimeSpan.FromDays(settings.Current.FinishedRequestRetentionDays + 1));

        Assert.Equal(1, await sweep.SweepAsync(CancellationToken.None));
        Assert.Empty(await store.GetAllAsync(CancellationToken.None));
    }

    /// <summary>
    /// A finished request that has not yet been kept for the whole period stays. This is the leg
    /// that makes the one above about a period rather than about being finished: the same request,
    /// one day short.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AFinishedRequestOneDayShortOfThePeriodIsKept()
    {
        var store = new InMemoryRequestStore();
        var clock = new TestClock(Asked);
        var settings = new FakeInstallSettings();
        var sweep = Sweep(store, settings, clock);

        await store.AddAsync(Finished(RequestState.Fulfilled), CancellationToken.None);

        clock.Advance(TimeSpan.FromDays(settings.Current.FinishedRequestRetentionDays - 1));

        Assert.Equal(0, await sweep.SweepAsync(CancellationToken.None));
        Assert.Single(await store.GetAllAsync(CancellationToken.None));
    }

    /// <summary>
    /// A request nobody has answered is never removed, however long it has been sitting there. The
    /// period is about how long a finished record is kept, and an open request that vanished on its
    /// anniversary would be this plugin quietly answering it with nothing.
    /// </summary>
    /// <param name="state">The two states somebody still owes something on.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [InlineData(RequestState.Open)]
    [InlineData(RequestState.Approved)]
    public async Task ARequestNobodyHasFinishedIsNeverRemoved(RequestState state)
    {
        var store = new InMemoryRequestStore();
        var clock = new TestClock(Asked);
        var settings = new FakeInstallSettings();
        var sweep = Sweep(store, settings, clock);

        var request = state == RequestState.Open
            ? Open()
            : RequestLifecycle.Move(Open(), RequestState.Approved, Asked, RequestCaller.Administrator(Operator));

        await store.AddAsync(request, CancellationToken.None);

        clock.Advance(TimeSpan.FromDays(settings.Current.FinishedRequestRetentionDays * 5));

        Assert.Equal(0, await sweep.SweepAsync(CancellationToken.None));
        Assert.Single(await store.GetAllAsync(CancellationToken.None));
    }

    /// <summary>
    /// All three finished states are reached, not only the fulfilled one the first leg uses. A
    /// declined request is the one that holds the most about a person, because it carries what an
    /// operator wrote about their ask, and a failed one is finished by the same reading.
    /// </summary>
    /// <param name="state">The state the request was left in.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [InlineData(RequestState.Declined)]
    [InlineData(RequestState.Failed)]
    public async Task EveryFinishedStateIsReachedByThePeriod(RequestState state)
    {
        var store = new InMemoryRequestStore();
        var clock = new TestClock(Asked);
        var settings = new FakeInstallSettings();
        var sweep = Sweep(store, settings, clock);

        await store.AddAsync(Finished(state), CancellationToken.None);

        clock.Advance(TimeSpan.FromDays(settings.Current.FinishedRequestRetentionDays + 1));

        Assert.Equal(1, await sweep.SweepAsync(CancellationToken.None));
        Assert.Empty(await store.GetAllAsync(CancellationToken.None));
    }

    /// <summary>
    /// The period runs from the move that finished the request and not from the day it was asked
    /// for. A request open for a year and declined yesterday has been finished for a day, and the
    /// other reading would delete it the moment an operator answered it.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ThePeriodRunsFromTheMoveAndNotFromTheAsk()
    {
        var store = new InMemoryRequestStore();
        var clock = new TestClock(Asked);
        var settings = new FakeInstallSettings();
        var sweep = Sweep(store, settings, clock);

        clock.Advance(TimeSpan.FromDays(settings.Current.FinishedRequestRetentionDays));

        var declined = RequestLifecycle.Decline(
            Open(),
            DeclineReason.CannotBeObtained,
            null,
            clock.UtcNow,
            RequestCaller.Administrator(Operator));

        await store.AddAsync(declined, CancellationToken.None);

        Assert.Equal(0, await sweep.SweepAsync(CancellationToken.None));
        Assert.Single(await store.GetAllAsync(CancellationToken.None));
    }

    /// <summary>
    /// An operator with a shorter period gets the shorter period, and the run reads it per run
    /// rather than holding what it was built with. This is what makes the setting a setting.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ThePeriodIsTheOneTheInstallIsSetTo()
    {
        var store = new InMemoryRequestStore();
        var clock = new TestClock(Asked);
        var settings = new FakeInstallSettings(
            new PluginConfiguration { FinishedRequestRetentionDays = PluginConfiguration.MinimumRetentionDays });
        var sweep = Sweep(store, settings, clock);

        await store.AddAsync(Finished(RequestState.Fulfilled), CancellationToken.None);

        clock.Advance(TimeSpan.FromDays(PluginConfiguration.MinimumRetentionDays + 1));

        Assert.Equal(1, await sweep.SweepAsync(CancellationToken.None));
        Assert.Empty(await store.GetAllAsync(CancellationToken.None));
    }

    /// <summary>
    /// An install whose stored settings this plugin will not run on removes nothing at all, and the
    /// refusal reaches the caller. A run that deleted what a valid period would have reached and
    /// then refused would leave an operator repairing a number after the data it governed had gone.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AnInstallWhoseSettingsAreRefusedRemovesNothing()
    {
        var store = new InMemoryRequestStore();
        var clock = new TestClock(Asked);
        var stored = new PluginConfiguration
        {
            FinishedRequestRetentionDays = PluginConfiguration.MinimumRetentionDays - 1
        };
        var sweep = Sweep(store, new ServerInstallSettings(() => stored), clock);

        await store.AddAsync(Finished(RequestState.Fulfilled), CancellationToken.None);

        clock.Advance(TimeSpan.FromDays(3650));

        await Assert.ThrowsAsync<InvalidConfigurationException>(
            () => sweep.SweepAsync(CancellationToken.None));

        Assert.Single(await store.GetAllAsync(CancellationToken.None));
    }

    /// <summary>
    /// A request that moved between the read and the removal is left alone rather than removed
    /// anyway. The case this is really about is a declined request an operator has just approved:
    /// removing it on the revision the sweep read would delete the decision they made a moment ago.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ARequestThatMovedUnderTheRunIsLeftAlone()
    {
        var held = new InMemoryRequestStore();
        var clock = new TestClock(Asked);
        var settings = new FakeInstallSettings();

        var declined = await held.AddAsync(
            RequestLifecycle.Decline(
                Open(),
                DeclineReason.CannotBeObtained,
                null,
                Asked,
                RequestCaller.Administrator(Operator)),
            CancellationToken.None);

        clock.Advance(TimeSpan.FromDays(settings.Current.FinishedRequestRetentionDays + 1));

        // The operator changes their mind while the run is walking: the request is approved at the
        // revision the store currently holds, so the revision the sweep read no longer names what
        // is there.
        var moving = new StoreThatMovesARequestUnderTheRemoval(
            held,
            request => RequestLifecycle.Move(
                request,
                RequestState.Approved,
                clock.UtcNow,
                RequestCaller.Administrator(Operator)));

        Assert.Equal(0, await Sweep(moving, settings, clock).SweepAsync(CancellationToken.None));

        var left = Assert.Single(await held.GetAllAsync(CancellationToken.None));

        Assert.Equal(declined.Request.Id, left.Request.Id);
        Assert.Equal(RequestState.Approved, left.Request.State);
    }

    /// <summary>
    /// What this sweep calls finished is what <see cref="RequestQuota"/> calls finished, over every
    /// value of <see cref="RequestState"/> rather than over the ones somebody thought of. A state
    /// added to the model with no answer here would otherwise be kept forever or deleted at once,
    /// depending on which way the two definitions happened to fall apart.
    /// </summary>
    [Fact]
    public void FinishedMeansWhatTheQuotaAlreadyMeansByIt()
    {
        foreach (var state in Enum.GetValues<RequestState>())
        {
            var request = state == RequestState.Open
                ? Open()
                : Open() with { State = state, StateChangedAt = Asked };

            Assert.NotEqual(RequestQuota.CountsAgainstIt(request), RetentionSweep.IsFinished(request));
        }
    }

    /// <summary>
    /// A store holding both kinds loses only the expired ones, which is the shape a real queue is
    /// in. Asserted by identity rather than by count, so a run that removed the wrong one and kept
    /// the right number is red.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task OnlyTheExpiredOnesGo()
    {
        var store = new InMemoryRequestStore();
        var clock = new TestClock(Asked);
        var settings = new FakeInstallSettings();
        var sweep = Sweep(store, settings, clock);

        var expired = await store.AddAsync(Finished(RequestState.Fulfilled), CancellationToken.None);
        var open = await store.AddAsync(Open(), CancellationToken.None);

        clock.Advance(TimeSpan.FromDays(settings.Current.FinishedRequestRetentionDays + 1));

        var recent = await store.AddAsync(
            Finished(RequestState.Declined) with { StateChangedAt = clock.UtcNow },
            CancellationToken.None);

        Assert.Equal(1, await sweep.SweepAsync(CancellationToken.None));

        var left = (await store.GetAllAsync(CancellationToken.None))
            .Select(stored => stored.Request.Id)
            .ToHashSet();

        Assert.DoesNotContain(expired.Request.Id, left);
        Assert.Contains(open.Request.Id, left);
        Assert.Contains(recent.Request.Id, left);
    }

    private static RetentionSweep Sweep(IRequestStore store, IInstallSettings settings, TestClock clock)
        => new RetentionSweep(store, settings, clock, NullLogger.Instance);

    private static MediaRequest Open()
        => new MediaRequest
        {
            Id = Guid.NewGuid(),
            RequestedByUserId = Requester,
            RequestedAt = Asked,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = "A film somebody asked for",
            StateChangedAt = Asked,
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" }
        };

    /// <summary>
    /// A request left in a finished state at <see cref="Asked"/>, so the period is measured from a
    /// moment the test names.
    /// </summary>
    /// <param name="state">Which finished state.</param>
    /// <returns>The request.</returns>
    private static MediaRequest Finished(RequestState state)
        => Open() with { State = state, StateChangedAt = Asked };
}

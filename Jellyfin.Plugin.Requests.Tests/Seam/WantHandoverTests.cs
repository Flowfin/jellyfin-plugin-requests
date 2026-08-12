using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Seam;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Seam;

/// <summary>
/// The call the sibling discover plugin makes into this one.
/// <para>
/// The failures these exist against are the three the issue asks to be decided rather than
/// discovered: a title this side has never seen treated as an error, a field set carrying a version
/// this side does not know read for the fields it recognises, and a refusal that reaches nobody
/// because the contract has no field for one.
/// </para>
/// </summary>
public class WantHandoverTests
{
    private static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Asker = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondAsker = new Guid("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// A want naming something no request has ever named creates a request. There is no pre-agreed
    /// catalogue on this side, so this is the ordinary case rather than the exception, and the store
    /// it is answered against is empty.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AWantForATitleNothingHasEverAskedForBecomesARequest()
    {
        var store = new InMemoryRequestStore();
        var handover = Seam(store);

        Assert.Empty(await store.GetAllAsync(CancellationToken.None));

        var accepted = await handover.AcceptAsync(Want(), CancellationToken.None);

        var held = await store.GetAllAsync(CancellationToken.None);
        var made = Assert.Single(held).Request;

        Assert.True(accepted);
        Assert.Equal(Asker, made.RequestedByUserId);
        Assert.Equal(RequestedItemKind.Movie, made.Kind);
        Assert.Equal(RequestState.Open, made.State);
        Assert.Equal(Noon, made.RequestedAt);
    }

    /// <summary>
    /// The queue renders from the title and the year that crossed, and nothing refreshes either.
    /// <para>
    /// The row is read the way the administrator queue reads it, through the store's own query, so
    /// what is asserted is what an operator would see. That nothing could have been fetched to build
    /// it is the assembly's reference list in <c>SiblingIndependenceTests</c> and the dependency
    /// count in <c>CatalogueSplitTests</c>; what is added here is the count for this seam, below.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheQueueRendersFromTheTitleAndYearThatCrossed()
    {
        var store = new InMemoryRequestStore();

        Assert.True(await Seam(store).AcceptAsync(
            Want() with { Title = "Der Himmel über Berlin", Year = 1987 },
            CancellationToken.None));

        var page = await store.PageAsync(new RequestQuery { Take = 10 }, CancellationToken.None);
        var row = Assert.Single(page.Requests).Request;

        Assert.Equal("Der Himmel über Berlin", row.DisplayTitle);
        Assert.Equal(1987, row.DisplayYear);
    }

    /// <summary>
    /// This seam is built from the store, the clock, the identifier source, the settings and a
    /// logger, and from nothing else. A metadata source would arrive as a dependency, and the
    /// sentence about the queue rendering with nothing outbound reachable is only true while there
    /// is nothing here that could fetch.
    /// </summary>
    [Fact]
    public void TheSeamIsBuiltFromNothingThatCouldFetchATitle()
    {
        var taken = typeof(WantHandover)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name)
            .ToArray();

        Assert.Equal(
            ["IRequestStore", "IClock", "IIdentifierSource", "IInstallSettings", "IKnownUsers", "ILogger", "TimeSpan"],
            taken);
    }

    /// <summary>
    /// Two people wanting the same film are one request with both of them recorded. The rule is the
    /// identity rule and it is applied by the same intake the HTTP endpoint asks, which is what stops
    /// a want arriving over the seam being acquired a second time.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TwoPeopleWantingTheSameFilmAreOneRequest()
    {
        var store = new InMemoryRequestStore();
        var handover = Seam(store);

        Assert.True(await handover.AcceptAsync(Want(), CancellationToken.None));
        Assert.True(await handover.AcceptAsync(
            Want() with { RequestedByUserId = SecondAsker, WantId = Guid.NewGuid() },
            CancellationToken.None));

        var held = await store.GetAllAsync(CancellationToken.None);
        var made = Assert.Single(held).Request;

        Assert.Equal(Asker, made.RequestedByUserId);
        Assert.Equal([SecondAsker], made.JoinedByUserIds);
    }

    /// <summary>
    /// A field set built against a version this plugin does not know is refused whole. Nothing is
    /// read out of it, which is the half that matters: a version that changed the meaning of a field
    /// and one that added a field are indistinguishable to a reader that takes what it recognises,
    /// and the first of those files a want against the wrong thing.
    /// </summary>
    /// <param name="version">A contract version that is not the one this plugin implements.</param>
    /// <returns>A task that completes when the case has been checked.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(97)]
    public async Task AWantBuiltAgainstAVersionThisPluginDoesNotKnowIsRefusedWhole(int version)
    {
        var store = new InMemoryRequestStore();
        var log = new RecordingLogger();

        var accepted = await Seam(store, log: log).AcceptAsync(
            Want() with { ContractVersion = version },
            CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        Assert.Contains(
            nameof(HandoverRefusal.ContractVersionNotKnown),
            Assert.Single(log.At(LogLevel.Warning)).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A kind this install is set not to accept is refused, and the refusal names itself in the log.
    /// The setting is read on every handover rather than held, so an operator turning a kind off
    /// means the next want of that kind.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AKindThisInstallDoesNotAcceptIsRefusedAndSaysSo()
    {
        var store = new InMemoryRequestStore();
        var log = new RecordingLogger();
        var settings = new FakeInstallSettings(new PluginConfiguration { AcceptsSeries = false });

        var accepted = await Seam(store, settings, log).AcceptAsync(
            Want() with { Kind = RequestedItemKind.Series },
            CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        Assert.Contains(
            nameof(HandoverRefusal.KindNotAccepted),
            Assert.Single(log.At(LogLevel.Warning)).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The field set has to carry a user and a title for a request to exist at all, and a kind has to
    /// be one of the kinds this plugin knows. Each is refused with its own reason rather than with one
    /// that covers all three, because the reason is the only thing an operator gets.
    /// </summary>
    /// <param name="broken">The want, broken in one way.</param>
    /// <param name="expected">The refusal that names what is wrong with it.</param>
    /// <returns>A task that completes when the case has been checked.</returns>
    [Theory]
    [MemberData(nameof(FieldSetsThatCannotBecomeARequest))]
    public async Task AFieldSetThatCannotBecomeARequestIsRefusedWithItsOwnReason(
        HandedOverWant broken,
        HandoverRefusal expected)
    {
        var store = new InMemoryRequestStore();
        var log = new RecordingLogger();

        Assert.False(await Seam(store, log: log).AcceptAsync(broken, CancellationToken.None));
        Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        Assert.Contains(
            expected.ToString(),
            Assert.Single(log.At(LogLevel.Warning)).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A store that cannot be read refuses the handover rather than throwing across the seam. An
    /// exception leaving this call is a fault in the calling plugin's own path for something that is
    /// an ordinary answer here.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AStoreThatCannotBeReadRefusesRatherThanThrowingAtTheCaller()
    {
        var log = new RecordingLogger();

        var accepted = await Seam(new StoreThatCannotBeRead(), log: log)
            .AcceptAsync(Want(), CancellationToken.None);

        Assert.False(accepted);
        Assert.Contains(
            nameof(HandoverRefusal.TheStoreCouldNotBeReached),
            Assert.Single(log.At(LogLevel.Warning)).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Settings the plugin cannot run on refuse the handover in the same way, and say that it is this
    /// server rather than the field set that is wrong.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SettingsThisPluginCannotRunOnRefuseTheHandoverRatherThanThrowing()
    {
        var store = new InMemoryRequestStore();
        var log = new RecordingLogger();

        var accepted = await Seam(store, new InstallSettingsThatCannotBeRead(), log)
            .AcceptAsync(Want(), CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        Assert.Contains(
            nameof(HandoverRefusal.ThisInstallCannotRun),
            Assert.Single(log.At(LogLevel.Warning)).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// No line a refusal writes carries the title or the person. A log is pasted into issue trackers,
    /// and what somebody asked for is the thing in this plugin worth being careful with; the want
    /// identifier is what an operator needs to find it and it names nobody.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARefusalNamesTheWantAndNeitherTheTitleNorThePerson()
    {
        var log = new RecordingLogger();
        var want = Want() with { ContractVersion = 4, Title = "Nosferatu" };

        Assert.False(await Seam(new InMemoryRequestStore(), log: log).AcceptAsync(want, CancellationToken.None));

        var line = Assert.Single(log.At(LogLevel.Warning)).Message;

        Assert.Contains(want.WantId.ToString(), line, StringComparison.Ordinal);
        Assert.DoesNotContain("Nosferatu", line, StringComparison.Ordinal);
        Assert.DoesNotContain(Asker.ToString(), line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same want handed over three times is one request. That is the sequence the other side
    /// actually produces: a refresh recreated the item, the server restarted, the user undid the
    /// gesture and did it again, and each of those hands the same identifier across.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheSameWantHandedOverThreeTimesIsOneRequest()
    {
        var store = new InMemoryRequestStore();
        var handover = Seam(store);
        var want = Want();

        Assert.True(await handover.AcceptAsync(want, CancellationToken.None));
        Assert.True(await handover.AcceptAsync(want, CancellationToken.None));
        Assert.True(await handover.AcceptAsync(want, CancellationToken.None));

        var made = Assert.Single(await store.GetAllAsync(CancellationToken.None));

        Assert.Equal([want.WantId], made.Request.WantIds);
        Assert.Equal(1, made.Revision);
    }

    /// <summary>
    /// A repeat is recognised by the want alone, with nothing else to go on. A want carrying no
    /// provider identifiers has no identity for the identity rule to compare, so this is the case
    /// where the two rules are visibly different things rather than one written twice.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARepeatIsCaughtEvenWhereThereAreNoIdentifiersToCompare()
    {
        var store = new InMemoryRequestStore();
        var handover = Seam(store);
        var want = Want() with { ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) };

        Assert.True(await handover.AcceptAsync(want, CancellationToken.None));
        Assert.True(await handover.AcceptAsync(want, CancellationToken.None));

        Assert.Single(await store.GetAllAsync(CancellationToken.None));
    }

    /// <summary>
    /// A want whose request was already answered is still a want that has been taken. Letting a
    /// declined request be the one thing that lets a repeat through would make a refusal the way to
    /// acquire something twice.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AWantWhoseRequestWasDeclinedDoesNotComeBackAsANewOne()
    {
        var store = new InMemoryRequestStore();
        var handover = Seam(store);
        var want = Want();

        Assert.True(await handover.AcceptAsync(want, CancellationToken.None));

        var made = Assert.Single(await store.GetAllAsync(CancellationToken.None));

        await store.ReplaceAsync(
            made.Request with { State = RequestState.Declined },
            made.Revision,
            CancellationToken.None);

        Assert.True(await handover.AcceptAsync(want, CancellationToken.None));

        var held = Assert.Single(await store.GetAllAsync(CancellationToken.None));

        Assert.Equal(RequestState.Declined, held.Request.State);
    }

    /// <summary>
    /// One request absorbs the wants of everybody who asked. Two people wanting the same film are
    /// two wants on the other side, and each of them has to be recognisable afterwards or the second
    /// person's repeat creates the request the first person is already waiting for.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARequestKeepsEveryWantItAbsorbed()
    {
        var store = new InMemoryRequestStore();
        var handover = Seam(store);

        var first = Want();
        var second = Want() with
        {
            WantId = new Guid("44444444-4444-4444-4444-444444444444"),
            RequestedByUserId = SecondAsker
        };

        Assert.True(await handover.AcceptAsync(first, CancellationToken.None));
        Assert.True(await handover.AcceptAsync(second, CancellationToken.None));
        Assert.True(await handover.AcceptAsync(second, CancellationToken.None));

        var made = Assert.Single(await store.GetAllAsync(CancellationToken.None));

        Assert.Equal([first.WantId, second.WantId], made.Request.WantIds);
        Assert.Equal([SecondAsker], made.Request.JoinedByUserIds);
    }

    /// <summary>
    /// The whole set the sibling recorded before this plugin was installed, replayed twice, is the
    /// queue it produced the first time and no second copy of it.
    /// <para>
    /// On a server that ran the browsing plugin first there is a list of people who already asked for
    /// things, and installing this plugin has to mean something for them or they ask again. The
    /// contract is one way and this side cannot pull that list, so the replay is the sibling's to
    /// initiate and it uses the same call a live handover uses. What this side owes against it is
    /// that a replay is safe to run, and that a replay that was interrupted is safe to run again.
    /// </para>
    /// <para>
    /// A whole set replayed is a stronger statement than one want handed over twice, which is what
    /// the tests above make. The set carries the shapes that go wrong together rather than alone:
    /// two people wanting one film, a want with no provider identifiers for the identity rule to
    /// compare, and a second kind. Revisions are asserted rather than counts, because a second pass
    /// that rewrote every request in place would leave the count unmoved.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheWholeSetReplayedTwiceIsTheQueueItMadeTheFirstTime()
    {
        var store = new InMemoryRequestStore();
        var handover = Seam(store);
        var recorded = WhatTheSiblingRecordedBefore();

        foreach (var want in recorded)
        {
            Assert.True(await handover.AcceptAsync(want, CancellationToken.None));
        }

        var afterTheFirstPass = await store.GetAllAsync(CancellationToken.None);

        foreach (var want in recorded)
        {
            Assert.True(await handover.AcceptAsync(want, CancellationToken.None));
        }

        var afterTheSecond = await store.GetAllAsync(CancellationToken.None);

        Assert.Equal(
            afterTheFirstPass.Select(held => (held.Request.Id, held.Revision)),
            afterTheSecond.Select(held => (held.Request.Id, held.Revision)));

        // The set is four wants and three of them name one film between two people, so what a
        // correct first pass produces is three requests. Asserting it here means the comparison
        // above cannot pass by both passes having produced nothing.
        Assert.Equal(3, afterTheFirstPass.Count);
        Assert.Equal(
            [.. recorded.Select(want => want.WantId).Order()],
            [.. afterTheSecond.SelectMany(held => held.Request.WantIds).Order()]);
    }

    /// <summary>
    /// A replay that stopped halfway and was run again from the start finishes the set, and the part
    /// that had already landed is not made a second time.
    /// <para>
    /// That is the ordinary way a replay of somebody's whole history ends: the server was restarted,
    /// the other side gave up on a refusal, or somebody closed a browser. A replay that can only be
    /// run once is one nobody can safely start.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AReplayThatStoppedHalfwayIsSafeToRunAgainFromTheStart()
    {
        var store = new InMemoryRequestStore();
        var handover = Seam(store);
        var recorded = WhatTheSiblingRecordedBefore();

        foreach (var want in recorded.Take(2))
        {
            Assert.True(await handover.AcceptAsync(want, CancellationToken.None));
        }

        var whatLandedBeforeItStopped = await store.GetAllAsync(CancellationToken.None);

        foreach (var want in recorded)
        {
            Assert.True(await handover.AcceptAsync(want, CancellationToken.None));
        }

        var held = await store.GetAllAsync(CancellationToken.None);

        // What the interrupted pass had already made is still the same request rather than a second
        // one beside it. The prefix carries the want with no provider identifiers on purpose: that
        // is the one the identity rule cannot answer, so it is the one whose second arrival becomes
        // a second request where nothing recognises the want itself.
        //
        // Revisions are not asserted here and are asserted in the whole-set case. A completing pass
        // is allowed to write to a request that already existed, because the wants it carries that
        // had not landed yet include one that joins it.
        foreach (var landed in whatLandedBeforeItStopped)
        {
            Assert.Single(held, request => request.Request.Id == landed.Request.Id);
        }

        Assert.Equal(3, held.Count);
        Assert.Equal(
            [.. recorded.Select(want => want.WantId).Order()],
            [.. held.SelectMany(request => request.Request.WantIds).Order()]);
    }

    /// <summary>
    /// The check survives a restart, because what it reads is on the disk rather than in memory. The
    /// store is closed and a second one is opened over the same directory, which is what a server
    /// stopping and starting does to it.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheSameWantAfterARestartIsStillTheSameWant()
    {
        var directory = TestRunDirectory.CreateSubdirectory();
        var want = Want();

        try
        {
            using (var before = new FileRequestStore(directory, new RecordingLogger()))
            {
                Assert.True(await Seam(before).AcceptAsync(want, CancellationToken.None));
                Assert.Single(await before.GetAllAsync(CancellationToken.None));
            }

            using var after = new FileRequestStore(directory, new RecordingLogger());

            Assert.True(await Seam(after).AcceptAsync(want, CancellationToken.None));

            var held = Assert.Single(await after.GetAllAsync(CancellationToken.None));

            Assert.Equal([want.WantId], held.Request.WantIds);
            Assert.Equal(1, held.Revision);
        }
        finally
        {
            TestRunDirectory.Remove(directory);
        }
    }

    /// <summary>
    /// A handover naming somebody this server does not have is refused. The identifier cannot be
    /// verified as the person who actually asked, and nothing here pretends otherwise; what it can
    /// be checked against is the server's own users, and a request stored against a user nobody has
    /// is one no surface can ever show to anybody.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AHandoverNamingAUserTheServerDoesNotHaveIsRefused()
    {
        var store = new InMemoryRequestStore();
        var log = new RecordingLogger();

        var accepted = await Seam(store, log: log, users: FakeKnownUsers.Nobody())
            .AcceptAsync(Want(), CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        Assert.Contains(
            nameof(HandoverRefusal.UserNotOnThisServer),
            Assert.Single(log.At(LogLevel.Warning)).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The server is asked about the user the want names and not about anybody else. A check that
    /// passed for the wrong person would be worse than none, because it reads afterwards as a check
    /// that was made.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheUserAskedAboutIsTheOneTheWantNames()
    {
        var store = new InMemoryRequestStore();

        var accepted = await Seam(store, users: new FakeKnownUsers(SecondAsker))
            .AcceptAsync(Want(), CancellationToken.None);

        Assert.False(accepted);
        Assert.True(await Seam(store, users: new FakeKnownUsers(SecondAsker))
            .AcceptAsync(Want() with { RequestedByUserId = SecondAsker }, CancellationToken.None));
    }

    /// <summary>
    /// Every layer beneath the seam, failing one at a time in a way nothing above it names, and no
    /// exception reaching the caller from any of them.
    /// <para>
    /// The refusals the seam decides on by name are proven case by case above, with the exceptions
    /// that carry them. This is the other half: what arrives that nobody wrote a name for. The
    /// caller is another plugin serving a user's gesture on a surface this one does not own, so a
    /// defect here becoming an exception there fails that gesture for a reason nobody on that side
    /// can act on.
    /// </para>
    /// </summary>
    /// <param name="layer">Which layer beneath the seam fails.</param>
    /// <returns>A task that completes when the case has been checked.</returns>
    [Theory]
    [InlineData("the queue")]
    [InlineData("the clock")]
    [InlineData("the identifier source")]
    [InlineData("the settings")]
    [InlineData("the server's users")]
    public async Task NothingLeavesTheSeamWhenALayerBeneathItFails(string layer)
    {
        var log = new RecordingLogger();

        var accepted = await SeamOver(layer, log).AcceptAsync(Want(), CancellationToken.None);

        Assert.False(accepted);
        Assert.Contains(
            nameof(HandoverRefusal.SomethingBeneathThisSeamFailed),
            Assert.Single(log.At(LogLevel.Warning)).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// What failed is on the log at error level with the fault itself, so an operator has the thing
    /// the caller was not told. A refusal the other side cannot read a reason out of is only
    /// acceptable while the reason reaches somebody.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task WhatFailedBeneathTheSeamReachesTheOperatorEvenThoughTheCallerIsNotTold()
    {
        var log = new RecordingLogger();

        Assert.False(await SeamOver("the queue", log).AcceptAsync(Want(), CancellationToken.None));

        var reported = Assert.Single(log.At(LogLevel.Error));

        Assert.Equal(ALayerThatFails.Detail, Assert.IsType<InvalidOperationException>(reported.Exception).Message);
    }

    /// <summary>
    /// A call carrying no field set at all is answered rather than raised. It is a defect in the
    /// caller rather than a want that could not be taken, and it is still answered the same way,
    /// because the boundary is what it is regardless of who was wrong.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AHandoverCarryingNoFieldSetIsAnsweredRatherThanRaised()
    {
        var log = new RecordingLogger();

        Assert.False(await Seam(new InMemoryRequestStore(), log: log).AcceptAsync(null!, CancellationToken.None));
        Assert.Contains(
            nameof(HandoverRefusal.NothingWasHandedOver),
            Assert.Single(log.At(LogLevel.Warning)).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A store that will never answer is given up on rather than waited out, so the handover returns
    /// whatever the queue is doing.
    /// <para>
    /// This is the case a cancellation token does not reach. A token is a request the callee honours
    /// and the case worth bounding is the one where it cannot: a write holding a lock nothing will
    /// release leaves a task that never completes however politely it is asked to stop.
    /// </para>
    /// <para>
    /// The bound is passed as nothing here, so the test spends no real time and does not depend on
    /// how busy the machine is. What it proves is that the seam races the queue rather than awaiting
    /// it; what the number is on a server is <see cref="WantHandover.DefaultAnswerWithin"/>, and it
    /// is what the registrator hands over.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AQueueThatNeverAnswersIsRefusedRatherThanWaitedOut()
    {
        var log = new RecordingLogger();

        var accepted = await Seam(new StoreThatNeverAnswers(), log: log, answerWithin: TimeSpan.Zero)
            .AcceptAsync(Want(), CancellationToken.None);

        Assert.False(accepted);
        Assert.Contains(
            nameof(HandoverRefusal.TheStoreDidNotAnswerInTime),
            Assert.Single(log.At(LogLevel.Warning)).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A handover the caller cancelled is answered rather than raised, for the same reason as
    /// everything else here. No request was made, which is what the one bit says.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ACancelledHandoverIsAnsweredRatherThanRaised()
    {
        using var gone = new CancellationTokenSource();

        await gone.CancelAsync();

        var store = new InMemoryRequestStore();

        Assert.False(await Seam(store).AcceptAsync(Want(), gone.Token));
        Assert.Empty(await store.GetAllAsync(CancellationToken.None));
    }

    /// <summary>
    /// A server hands the seam the bound this plugin ships with, and it is a real one rather than
    /// none. A bound of nothing would refuse every handover a store did not answer synchronously.
    /// </summary>
    [Fact]
    public void TheBoundAServerGetsIsARealOne()
    {
        Assert.True(WantHandover.DefaultAnswerWithin > TimeSpan.Zero);
    }

    /// <summary>
    /// The field sets that cannot become a request, each with the refusal that names what is wrong.
    /// </summary>
    /// <returns>One case per way of being wrong.</returns>
    public static TheoryData<HandedOverWant, HandoverRefusal> FieldSetsThatCannotBecomeARequest()
        => new TheoryData<HandedOverWant, HandoverRefusal>
        {
            { Want() with { WantId = Guid.Empty }, HandoverRefusal.NoWantNamed },
            { Want() with { RequestedByUserId = Guid.Empty }, HandoverRefusal.NoUserNamed },
            { Want() with { Title = "   " }, HandoverRefusal.NoTitle },
            { Want() with { Kind = (RequestedItemKind)9 }, HandoverRefusal.KindNotRecognised }
        };

    /// <summary>
    /// What the sibling had already recorded on a server that ran it before this plugin arrived.
    /// <para>
    /// Four wants, chosen for the shapes that go wrong together. Two people want one film, so the
    /// identity rule joins the second onto the first and a replay has two chances to make it twice.
    /// One want carries no provider identifiers, which is the case the identity rule cannot answer
    /// and the want identifier has to. One is a series rather than a film.
    /// </para>
    /// </summary>
    /// <returns>The set, in the order the other side would replay it.</returns>
    private static HandedOverWant[] WhatTheSiblingRecordedBefore()
        =>
        [
            Want(),
            Want() with
            {
                WantId = new Guid("55555555-5555-5555-5555-555555555555"),
                Title = "Le Samouraï",
                Year = 1967,
                ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal)
            },
            Want() with
            {
                WantId = new Guid("44444444-4444-4444-4444-444444444444"),
                RequestedByUserId = SecondAsker
            },
            Want() with
            {
                WantId = new Guid("66666666-6666-6666-6666-666666666666"),
                RequestedByUserId = SecondAsker,
                Kind = RequestedItemKind.Series,
                Title = "Das Boot",
                Year = 1985,
                ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tvdb"] = "78804" }
            }
        ];

    /// <summary>
    /// A want as the contract carries one, which every case here starts from and breaks in one way.
    /// </summary>
    /// <returns>A field set this plugin accepts.</returns>
    private static HandedOverWant Want()
        => new HandedOverWant
        {
            ContractVersion = WantHandover.KnownContractVersion,
            WantId = new Guid("33333333-3333-3333-3333-333333333333"),
            RequestedByUserId = Asker,
            Kind = RequestedItemKind.Movie,
            Title = "Stalker",
            Year = 1979,
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "1398" }
        };

    /// <summary>
    /// The seam over a store, with a clock that does not move and identifiers that count.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="settings">What the install is set to, or a fresh install where not given.</param>
    /// <param name="log">Where refusals are written, or a discarded one where not given.</param>
    /// <param name="users">Who the server has, or both people these tests use where not given.</param>
    /// <param name="answerWithin">
    /// How long to wait for the queue, or the shipping bound where not given. A test that is not
    /// about waiting takes the shipping one, so nothing here passes for the wrong reason.
    /// </param>
    /// <returns>The seam under test.</returns>
    private static WantHandover Seam(
        IRequestStore store,
        IInstallSettings? settings = null,
        RecordingLogger? log = null,
        IKnownUsers? users = null,
        TimeSpan? answerWithin = null)
        => new WantHandover(
            store,
            new TestClock(Noon),
            new SequentialIdentifierSource(),
            settings ?? new FakeInstallSettings(),
            users ?? new FakeKnownUsers(Asker, SecondAsker),
            log ?? new RecordingLogger(),
            answerWithin ?? WantHandover.DefaultAnswerWithin);

    /// <summary>
    /// The seam with one layer beneath it failing and every other layer as it is elsewhere here.
    /// </summary>
    /// <param name="layer">Which layer fails.</param>
    /// <param name="log">Where the refusal is written.</param>
    /// <returns>The seam under test.</returns>
    private static WantHandover SeamOver(string layer, RecordingLogger log)
    {
        var failing = new ALayerThatFails();

        // An unrecognised name is a case nobody wrote, and it fails here rather than passing as a
        // handover over five working layers.
        return layer switch
        {
            "the queue" => new WantHandover(
                failing,
                new TestClock(Noon),
                new SequentialIdentifierSource(),
                new FakeInstallSettings(),
                new FakeKnownUsers(Asker, SecondAsker),
                log,
                WantHandover.DefaultAnswerWithin),
            "the clock" => new WantHandover(
                new InMemoryRequestStore(),
                failing,
                new SequentialIdentifierSource(),
                new FakeInstallSettings(),
                new FakeKnownUsers(Asker, SecondAsker),
                log,
                WantHandover.DefaultAnswerWithin),
            "the identifier source" => new WantHandover(
                new InMemoryRequestStore(),
                new TestClock(Noon),
                failing,
                new FakeInstallSettings(),
                new FakeKnownUsers(Asker, SecondAsker),
                log,
                WantHandover.DefaultAnswerWithin),
            "the settings" => new WantHandover(
                new InMemoryRequestStore(),
                new TestClock(Noon),
                new SequentialIdentifierSource(),
                failing,
                new FakeKnownUsers(Asker, SecondAsker),
                log,
                WantHandover.DefaultAnswerWithin),
            "the server's users" => new WantHandover(
                new InMemoryRequestStore(),
                new TestClock(Noon),
                new SequentialIdentifierSource(),
                new FakeInstallSettings(),
                failing,
                log,
                WantHandover.DefaultAnswerWithin),

            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "That is not a layer beneath the seam.")
        };
    }
}

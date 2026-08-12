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
            ["IRequestStore", "IClock", "IIdentifierSource", "IInstallSettings", "ILogger"],
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
    /// The field sets that cannot become a request, each with the refusal that names what is wrong.
    /// </summary>
    /// <returns>One case per way of being wrong.</returns>
    public static TheoryData<HandedOverWant, HandoverRefusal> FieldSetsThatCannotBecomeARequest()
        => new TheoryData<HandedOverWant, HandoverRefusal>
        {
            { Want() with { RequestedByUserId = Guid.Empty }, HandoverRefusal.NoUserNamed },
            { Want() with { Title = "   " }, HandoverRefusal.NoTitle },
            { Want() with { Kind = (RequestedItemKind)9 }, HandoverRefusal.KindNotRecognised }
        };

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
    /// <returns>The seam under test.</returns>
    private static WantHandover Seam(
        IRequestStore store,
        IInstallSettings? settings = null,
        RecordingLogger? log = null)
        => new WantHandover(
            store,
            new TestClock(Noon),
            new SequentialIdentifierSource(),
            settings ?? new FakeInstallSettings(),
            log ?? new RecordingLogger());
}

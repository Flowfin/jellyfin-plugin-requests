using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.Seam;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Notify;

/// <summary>
/// What a live administrator is told when something arrives, which is #76.
/// <para>
/// The delivery is a websocket to a client on a running server, and the headless rule in
/// <c>docs/testing.md</c> refuses one, so what is asserted here stops at the server's own session
/// interface: that one document went to the administrators, that nobody was named, that no other
/// way of reaching anybody was used, and that an install nobody switched on says nothing at all.
/// Whether a client then does something with it is a different claim, and this plugin makes it about
/// no client: nothing on either claimed line listens, which <c>docs/notifications.md</c> carries
/// with the reading behind it.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class TellingAdministratorsTests
{
    private static readonly Guid Asker = new Guid("76000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondPerson = new Guid("76000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Started = new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// A request that came into existence is announced once, and one that joined an existing request
    /// is not announced at all.
    /// <para>
    /// This is the first condition of #76 on the surface a person asks over. The second half is what
    /// a count alone cannot show: a join is a second person on a row an operator has already been
    /// shown, so announcing it would put the same title in front of them once per person waiting for
    /// it, and one film six people want would read as six things to work.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnArrivalOverTheEndpointIsAnnouncedOnceAndAJoinIsNotAnnouncedAtAll()
    {
        var store = new InMemoryRequestStore();
        var arrivals = new RecordingArrivalNotice();

        await Controller(store, Asker, arrivals).CreateAsync(AFilm(), CancellationToken.None).ConfigureAwait(true);

        var announced = Assert.Single(arrivals.Told);

        Assert.Equal(NoticeEvent.Asked, announced.Event);
        Assert.Equal("The Conversation", announced.Title);
        Assert.Equal(Asker, announced.RequestedByUserId);
        Assert.Equal(RequestState.Open, announced.State);
        Assert.Null(announced.MovedByUserId);

        await Controller(store, SecondPerson, arrivals).CreateAsync(AFilm(), CancellationToken.None).ConfigureAwait(true);

        Assert.Single(arrivals.Told);
    }

    /// <summary>
    /// A want handed across the seam is announced the same way, and the same want handed over again
    /// is not.
    /// <para>
    /// This is the first condition of #76 on the other surface an arrival comes in on. It is a leg
    /// of its own rather than a variation of the one above, because the failure it stands for is the
    /// one <c>docs/notifications.md</c> already names for the outbound switches: a path wired at the
    /// endpoint alone carries some arrivals while reading as though it carried all of them, and an
    /// operator would then hear about what people typed and never about what the sibling handed
    /// across.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AWantHandedAcrossTheSeamIsAnnouncedOnceAndTheSameWantAgainIsNot()
    {
        var store = new InMemoryRequestStore();
        var arrivals = new RecordingArrivalNotice();
        var seam = Seam(store, arrivals);

        Assert.True(await seam.AcceptAsync(AWant(), CancellationToken.None).ConfigureAwait(true));

        var announced = Assert.Single(arrivals.Told);

        Assert.Equal(NoticeEvent.Asked, announced.Event);
        Assert.Equal("Stalker", announced.Title);
        Assert.Equal(Asker, announced.RequestedByUserId);

        Assert.True(await seam.AcceptAsync(AWant(), CancellationToken.None).ConfigureAwait(true));

        Assert.Single(arrivals.Told);
    }

    /// <summary>
    /// The one call this plugin makes to the server addresses whoever administers it, names nobody,
    /// and carries the document the outbound sink would post.
    /// <para>
    /// This is the rest of the first condition, and the part of it about who is reached rather than
    /// how many times. The double keeps a push at a named person and a push at the administrators in
    /// separate lists and raises on every other way of sending anything, so the absence asserted here
    /// is a real one: a device broadcast or a remote-control command would end the test rather than
    /// pass it, and a document that named the person who asked would land in the list this leg
    /// requires to be empty.
    /// </para>
    /// <para>
    /// The document is compared against <see cref="OutboundNotice.For"/> rather than against fields
    /// written out here, because what makes it a contract is that one shape leaves this plugin
    /// whichever carrier took it.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ThePushReachesTheAdministratorsAndNamesNobody()
    {
        var sessions = new ASessionManagerThatOnlyDelivers();
        var arriving = AnAsk(Asker, "The Conversation");
        var notice = new ServerArrivalNotice(sessions, SwitchedOn(), new RecordingLogger());

        notice.Tell(OutboundNotice.For(arriving, NoticeEvent.Asked));

        await notice.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        var pushed = Assert.Single(sessions.Broadcasts);

        Assert.Empty(sessions.Delivered);
        Assert.Equal(SessionMessageType.ActivityLogEntry, pushed.Name);
        Assert.Equal(OutboundNotice.For(arriving, NoticeEvent.Asked), Assert.IsType<OutboundNotice>(pushed.Payload));
    }

    /// <summary>
    /// A fresh install pushes nothing, and turning the switch on is what makes it push.
    /// <para>
    /// This is the third condition of #76, and off is the shipping state rather than a degraded one:
    /// no client on either claimed line reads this document, so an install that sent it by default
    /// would push a message at every administrator's client on every arrival for nobody to read.
    /// Both halves are in one leg because either alone passes on a path that is broken the other
    /// way, and a switch stuck in one position looks exactly like a switch.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AFreshInstallSaysNothingAndAnInstallSwitchedOnSays()
    {
        var quiet = new ASessionManagerThatOnlyDelivers();
        var arriving = AnAsk(Asker, "The Conversation");
        var off = new ServerArrivalNotice(quiet, new FakeInstallSettings(), new RecordingLogger());

        off.Tell(OutboundNotice.For(arriving, NoticeEvent.Asked));

        await off.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(quiet.Broadcasts);
        Assert.Empty(quiet.Delivered);

        var loud = new ASessionManagerThatOnlyDelivers();
        var on = new ServerArrivalNotice(loud, SwitchedOn(), new RecordingLogger());

        on.Tell(OutboundNotice.For(arriving, NoticeEvent.Asked));

        await on.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Single(loud.Broadcasts);
    }

    /// <summary>
    /// The switch is read per arrival rather than when the path was built.
    /// <para>
    /// An operator turning this off while the server is running expects the next arrival to be
    /// silent, not the one after the next restart. The failure this stands for is a value captured
    /// in a constructor, which passes the leg above and leaves the switch inert on a running server,
    /// which is the only place anybody ever uses it.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TurningItOffStopsTheNextArrivalRatherThanTheNextRestart()
    {
        var sessions = new ASessionManagerThatOnlyDelivers();
        var settings = SwitchedOn();
        var arriving = AnAsk(Asker, "The Conversation");
        var notice = new ServerArrivalNotice(sessions, settings, new RecordingLogger());

        notice.Tell(OutboundNotice.For(arriving, NoticeEvent.Asked));

        await notice.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Single(sessions.Broadcasts);

        settings.Current.TellsAdministratorsAboutArrivals = false;

        notice.Tell(OutboundNotice.For(arriving, NoticeEvent.Asked));

        await notice.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Single(sessions.Broadcasts);
    }

    /// <summary>
    /// A push nobody could take costs the ask nothing and lands in the log.
    /// <para>
    /// This is the second condition of #76. The request is in the store before anybody is told and
    /// telling hands nothing back for a caller to check, so a client that cannot be reached must not
    /// be able to undo somebody's ask. The exception is caught by kind rather than by name, because
    /// the host decides what a push can raise on two server generations, and the one nobody listed
    /// would otherwise arrive later on a thread nothing is watching.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task APushThatFailsCostsTheAskNothing()
    {
        var store = new InMemoryRequestStore();
        var log = new RecordingLogger();
        var notice = new ServerArrivalNotice(
            new ASessionManagerThatOnlyDelivers { Refuses = true },
            SwitchedOn(),
            log);

        var answered = await Controller(store, Asker, notice)
            .CreateAsync(AFilm(), CancellationToken.None)
            .ConfigureAwait(true);

        // Returns rather than raising, which is the whole promise of the interface.
        await notice.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(201, Assert.IsType<ObjectResult>(answered.Result).StatusCode);
        Assert.Single(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));

        var reported = Assert.Single(log.At(LogLevel.Warning));

        Assert.NotNull(reported.Exception);
        Assert.Contains("stands", reported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// One install with the switch on, held so a test can move it.
    /// </summary>
    /// <returns>The settings.</returns>
    private static FakeInstallSettings SwitchedOn()
        => new FakeInstallSettings(new PluginConfiguration { TellsAdministratorsAboutArrivals = true });

    /// <summary>
    /// A film, named by one provider.
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
    /// A want as the sibling hands one across.
    /// </summary>
    /// <returns>The want.</returns>
    private static HandedOverWant AWant()
        => new HandedOverWant
        {
            ContractVersion = WantHandover.KnownContractVersion,
            WantId = new Guid("76333333-3333-3333-3333-333333333333"),
            RequestedByUserId = Asker,
            Kind = RequestedItemKind.Movie,
            Title = "Stalker",
            Year = 1979,
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "1398" }
        };

    /// <summary>
    /// A controller announcing arrivals to the path handed in.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="caller">Who is asking.</param>
    /// <param name="arrivals">Where an arrival is announced.</param>
    /// <returns>The controller.</returns>
    private RequestsController Controller(InMemoryRequestStore store, Guid caller, IArrivalNotice arrivals)
        => new RequestsController(
            store,
            new TestClock(Started),
            _identifiers,
            new FakeCallerIdentity(caller),
            new FakeInstallSettings(),
            new RecordingJournal(),
            new RecordingSink(),
            new RecordingRequesterNotice(),
            arrivals,
            new FakeLibrary());

    /// <summary>
    /// The seam announcing arrivals to the path handed in.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="arrivals">Where an arrival is announced.</param>
    /// <returns>The seam.</returns>
    private WantHandover Seam(InMemoryRequestStore store, IArrivalNotice arrivals)
        => new WantHandover(
            store,
            new TestClock(Started),
            _identifiers,
            new FakeInstallSettings(),
            new FakeKnownUsers(Asker, SecondPerson),
            arrivals,
            new RecordingLogger(),
            WantHandover.DefaultAnswerWithin);

    /// <summary>
    /// One request, from one person, for one title.
    /// </summary>
    /// <param name="who">Who asked.</param>
    /// <param name="title">What they asked for.</param>
    /// <returns>The request.</returns>
    private MediaRequest AnAsk(Guid who, string title)
        => new MediaRequest
        {
            Id = _identifiers.NewId(),
            RequestedByUserId = who,
            RequestedAt = Started,
            StateChangedAt = Started,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = title,
            DisplayYear = 1974,
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "603" }
        };
}

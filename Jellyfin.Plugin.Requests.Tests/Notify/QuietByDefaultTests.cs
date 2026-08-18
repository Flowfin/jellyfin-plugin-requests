using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Fulfilment;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Notify;

/// <summary>
/// What an install sends outward before anybody has configured it, and what an operator can switch
/// once they have. This is #79.
/// <para>
/// Every leg runs the real sink over an endpoint inside this process, because the subject is what
/// leaves the server and a double that records what it was handed would assert the call rather than
/// the send. The two paths that move a request are driven through their own surfaces for the same
/// reason: what has to be caught is a path that announces nothing, and a leg that announced by hand
/// would pass on a tree where neither path does.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class QuietByDefaultTests
{
    private const string Address = "https://example.invalid/hook";

    private static readonly Guid Asker = new Guid("79000000-0000-0000-0000-000000000001");
    private static readonly Guid Somebody = new Guid("79000000-0000-0000-0000-000000000003");
    private static readonly Guid Operator = new Guid("79000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Started = new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Every movement this plugin announces, in the order the legs below compare against.
    /// </summary>
    private static readonly NoticeEvent[] Announced =
    [
        NoticeEvent.Approved,
        NoticeEvent.Declined,
        NoticeEvent.Fulfilled
    ];

    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// A fresh install writes the move to the activity log and posts nothing anywhere.
    /// <para>
    /// The first condition of #79, and it is asserted over a path that does announce rather than
    /// over one that cannot. The settings are the ones a server runs with when nobody has opened the
    /// page, so what makes it quiet is the install and not the absence of the code.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AFreshInstallWritesToTheActivityLogAndSendsNothingOutward()
    {
        var store = new InMemoryRequestStore();
        var journal = new RecordingJournal();
        var endpoint = ASinkEndpoint.ThatAccepts();
        var settings = new FakeInstallSettings();

        using var sink = Sink(endpoint, settings);

        var open = await store.AddAsync(AnAsk(Asker, "The Conversation"), CancellationToken.None).ConfigureAwait(true);

        await Controller(store, journal, sink, settings)
            .ApproveAsync(open.Request.Id, new ApproveRequestBody { Revision = open.Revision }, CancellationToken.None)
            .ConfigureAwait(true);

        await sink.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Single(journal.Written);
        Assert.Empty(endpoint.Received);

        // The three switches are on and the install is still silent, which is the shape this issue
        // asks for: what turns the path on is the address, and the switches are what narrows it
        // afterwards. An install quiet because every switch is off would be quiet for a reason an
        // operator has to go and read three fields to discover.
        Assert.True(settings.Current.AnnouncesApprovals);
        Assert.True(settings.Current.AnnouncesDeclines);
        Assert.True(settings.Current.AnnouncesFulfilments);
        Assert.Equal(string.Empty, settings.Current.OutboundNoticeAddress);
    }

    /// <summary>
    /// With an address typed, each movement is switched on its own: turning one off stops that one
    /// and leaves the others arriving.
    /// <para>
    /// The second condition of #79. It is one theory over the three rather than three legs, because
    /// what a switch has to be is independent, and a leg per switch asserts only that each one is
    /// read somewhere. Every case walks the same request through the same two surfaces, so the leg
    /// that reds when a switch stops being read is the leg for that switch alone.
    /// </para>
    /// </summary>
    /// <param name="off">The movement this case switches off.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData(NoticeEvent.Approved)]
    [InlineData(NoticeEvent.Declined)]
    [InlineData(NoticeEvent.Fulfilled)]
    public async Task EachMovementIsSwitchedOnItsOwn(NoticeEvent off)
    {
        var settings = new FakeInstallSettings(new PluginConfiguration
        {
            OutboundNoticeAddress = Address,
            AnnouncesApprovals = off != NoticeEvent.Approved,
            AnnouncesDeclines = off != NoticeEvent.Declined,
            AnnouncesFulfilments = off != NoticeEvent.Fulfilled
        });

        var endpoint = ASinkEndpoint.ThatAccepts();
        using var sink = Sink(endpoint, settings);

        var sent = await EveryMovementAsync(sink, endpoint, settings).ConfigureAwait(true);

        // The two that were left on arrived, in the vocabulary that goes on the wire rather than in
        // the states behind it, because the word is what an operator's endpoint branches on.
        Assert.Equal(
            Announced
                .Where(what => what != off)
                .Select(what => what.ToString())
                .ToArray(),
            sent.OrderBy(what => what, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Every movement arrives where nothing is switched off, which is what makes the leg above a
    /// statement about the switch rather than about the path.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task WithNothingSwitchedOffEveryMovementArrives()
    {
        var settings = new FakeInstallSettings(new PluginConfiguration { OutboundNoticeAddress = Address });
        var endpoint = ASinkEndpoint.ThatAccepts();

        using var sink = Sink(endpoint, settings);

        var sent = await EveryMovementAsync(sink, endpoint, settings).ConfigureAwait(true);

        Assert.Equal(
            Announced.Select(what => what.ToString()).ToArray(),
            sent.OrderBy(what => what, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// A movement nobody has decided a switch for is not sent, however configured the install is.
    /// <para>
    /// The direction the sink fails in, asserted rather than described. An arrival has no path here
    /// and <see cref="NoticeEvent.Asked"/> is the value that reaches the sink if one is written
    /// without a switch beside it; withholding a message costs somebody a field to find, and sending
    /// one costs a message that has already left the server.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AMovementWithNoSwitchOfItsOwnIsNotSent()
    {
        var settings = new FakeInstallSettings(new PluginConfiguration { OutboundNoticeAddress = Address });
        var endpoint = ASinkEndpoint.ThatAccepts();

        using var sink = Sink(endpoint, settings);

        Assert.True(sink.IsConfigured);

        sink.Announce(OutboundNotice.For(AnAsk(Asker, "Stalker"), NoticeEvent.Asked));
        await sink.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(endpoint.Received);
    }

    /// <summary>
    /// Nothing that leaves on this path carries a second person's request, and nothing on it is
    /// addressed to a person at all.
    /// <para>
    /// The third condition of #79. Two people are used because one cannot fail: a document about the
    /// only request in the store names the only person in it whatever the shape is. What is asserted
    /// is per document, so a notice that reached for the wrong record reds on the pair rather than on
    /// a count.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task NoNoticeCarriesAnotherPersonsRequestAndNoneIsAddressedToAPerson()
    {
        var store = new InMemoryRequestStore();
        var journal = new RecordingJournal();
        var settings = new FakeInstallSettings(new PluginConfiguration { OutboundNoticeAddress = Address });
        var endpoint = ASinkEndpoint.ThatAccepts();

        using var sink = Sink(endpoint, settings);

        var mine = await store.AddAsync(AnAsk(Asker, "The Conversation"), CancellationToken.None).ConfigureAwait(true);
        var theirs = await store.AddAsync(AnAsk(Somebody, "Stalker"), CancellationToken.None).ConfigureAwait(true);

        var controller = Controller(store, journal, sink, settings);

        await controller
            .ApproveAsync(mine.Request.Id, new ApproveRequestBody { Revision = mine.Revision }, CancellationToken.None)
            .ConfigureAwait(true);

        await controller
            .ApproveAsync(theirs.Request.Id, new ApproveRequestBody { Revision = theirs.Revision }, CancellationToken.None)
            .ConfigureAwait(true);

        await sink.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        var about = new Dictionary<Guid, Guid>
        {
            [mine.Request.Id] = Asker,
            [theirs.Request.Id] = Somebody
        };

        Assert.Equal(2, endpoint.Received.Count);

        foreach (var posted in endpoint.Received)
        {
            // There is one address and it is the one the operator typed. A notice addressed
            // per person would be this path handing one user something about a queue, which is
            // the failure the issue leads with.
            Assert.Equal(new Uri(Address), posted.Address);

            using var read = JsonDocument.Parse(posted.Body);

            var id = read.RootElement.GetProperty("requestId").GetGuid();
            var named = read.RootElement.GetProperty("requestedByUserId").GetGuid();

            Assert.Equal(about[id], named);

            // The only two people a document names are the one who asked and the one who
            // answered, so the other person's identifier is nowhere in these bytes at all.
            Assert.DoesNotContain(
                about.First(pair => pair.Key != id).Value.ToString(),
                posted.Body,
                StringComparison.OrdinalIgnoreCase);

            Assert.Equal(Operator, read.RootElement.GetProperty("movedByUserId").GetGuid());
        }
    }

    /// <summary>
    /// One request through an approval, a second through a decline and a third through the sweep,
    /// against the sink handed in, answering with the words that arrived at the endpoint.
    /// </summary>
    /// <param name="sink">The sink under test.</param>
    /// <param name="endpoint">What answers the post.</param>
    /// <param name="settings">What the install is set to.</param>
    /// <returns>The event word of every document the endpoint received.</returns>
    private async Task<IReadOnlyList<string>> EveryMovementAsync(
        OutboundSink sink,
        ASinkEndpoint endpoint,
        IInstallSettings settings)
    {
        var store = new InMemoryRequestStore();
        var journal = new RecordingJournal();
        var library = new FakeLibrary();
        var clock = new TestClock(Started);
        var controller = Controller(store, journal, sink, settings);

        var approved = await store.AddAsync(AnAsk(Asker, "The Conversation"), CancellationToken.None).ConfigureAwait(true);
        var declined = await store.AddAsync(AnAsk(Asker, "Stalker"), CancellationToken.None).ConfigureAwait(true);

        await controller
            .ApproveAsync(approved.Request.Id, new ApproveRequestBody { Revision = approved.Revision }, CancellationToken.None)
            .ConfigureAwait(true);

        await controller
            .DeclineAsync(
                declined.Request.Id,
                new DeclineRequestBody { Revision = declined.Revision, Reason = DeclineReason.NoRoomForIt },
                CancellationToken.None)
            .ConfigureAwait(true);

        library.Put(RequestedItemKind.Movie, "Tmdb", "603");
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(
            1,
            await new FulfilmentSweep(store, library, clock, journal, sink, new RecordingLogger())
                .SweepAsync(CancellationToken.None)
                .ConfigureAwait(true));

        await sink.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        return [.. endpoint.Received.Select(posted =>
        {
            using var read = JsonDocument.Parse(posted.Body);

            return read.RootElement.GetProperty("event").GetString() ?? string.Empty;
        })];
    }

    /// <summary>
    /// The sink under test, wired to the endpoint and to the settings handed in.
    /// </summary>
    /// <param name="endpoint">What answers the post.</param>
    /// <param name="settings">What the install is set to.</param>
    /// <returns>The sink.</returns>
    private static OutboundSink Sink(ASinkEndpoint endpoint, IInstallSettings settings)
        => new OutboundSink(settings, endpoint, new RecordingLogger(), OutboundSink.DefaultAnswerWithin);

    /// <summary>
    /// A controller wired to the store, the journal and the sink handed in.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="journal">Where the entries go.</param>
    /// <param name="sink">Where a movement is announced.</param>
    /// <param name="settings">What the install is set to.</param>
    /// <returns>The controller.</returns>
    private RequestsController Controller(
        IRequestStore store,
        IActivityJournal journal,
        IOutboundSink sink,
        IInstallSettings settings)
        => new RequestsController(
            store,
            new TestClock(Started),
            _identifiers,
            new FakeCallerIdentity(Operator),
            settings,
            journal,
            sink);

    /// <summary>
    /// One open request, with a provider identifier the library can match.
    /// </summary>
    /// <param name="asker">Who asked.</param>
    /// <param name="title">What for.</param>
    /// <returns>The request.</returns>
    private MediaRequest AnAsk(Guid asker, string title)
        => new MediaRequest
        {
            Id = _identifiers.NewId(),
            RequestedByUserId = asker,
            RequestedAt = Started,
            StateChangedAt = Started,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = title,
            DisplayYear = 1974,
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "603" }
        };
}

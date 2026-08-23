using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Fulfilment;
using Jellyfin.Plugin.Requests.Localisation;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Notify;

/// <summary>
/// What the person who asked is told when their own request moves, which is #77.
/// <para>
/// The delivery is a websocket to a client on a running server, and the headless rule in
/// <c>docs/testing.md</c> refuses one, so what is asserted here stops at the server's own session
/// interface: which person was named, how many times, what the message said, and that no other way
/// of reaching anybody was used. Whether a client then draws it is a reading of that client rather
/// than a test, and <c>docs/notifications.md</c> carries it with what it does not prove.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class TellingTheRequesterTests
{
    private static readonly Guid Asker = new Guid("77000000-0000-0000-0000-000000000001");
    private static readonly Guid Operator = new Guid("77000000-0000-0000-0000-000000000002");
    private static readonly Guid Somebody = new Guid("77000000-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset Started = new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// A decision names the person who asked and nobody else, with one message per move.
    /// <para>
    /// This is the first condition of #77 and it is written over two requests belonging to two
    /// people, because a path that told everybody would pass a leg holding one person's request.
    /// The counting matters as much as the naming: a second message about one move is a second
    /// interruption for the same news.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ADecisionTellsThePersonWhoAskedForThatRequestAndNobodyElse()
    {
        var store = new InMemoryRequestStore();
        var told = new RecordingRequesterNotice();
        var mine = await store.AddAsync(AnAsk(Asker, "The Conversation"), CancellationToken.None).ConfigureAwait(true);
        var theirs = await store.AddAsync(AnAsk(Somebody, "Stalker"), CancellationToken.None).ConfigureAwait(true);

        await Controller(store, told)
            .ApproveAsync(mine.Request.Id, new ApproveRequestBody { Revision = mine.Revision }, CancellationToken.None)
            .ConfigureAwait(true);

        var one = Assert.Single(told.Told);

        Assert.Equal(Asker, one.ToUserId);
        Assert.Contains("The Conversation", one.Text, StringComparison.Ordinal);

        await Controller(store, told)
            .DeclineAsync(
                theirs.Request.Id,
                new DeclineRequestBody { Revision = theirs.Revision, Reason = DeclineReason.NoRoomForIt },
                CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(2, told.Told.Count);
        Assert.Equal(Somebody, told.Told[1].ToUserId);
        Assert.Contains("Stalker", told.Told[1].Text, StringComparison.Ordinal);

        // Neither person appears in a message about the other's request, which is the half a count
        // cannot show: two messages to the same person would also be two messages.
        Assert.Equal(new[] { Asker, Somebody }, told.Told.Select(message => message.ToUserId));
    }

    /// <summary>
    /// A decline carries the reason, in the words the person's own page uses for it.
    /// <para>
    /// This is the second condition of #77. The reason is read out of the catalogue under the same
    /// key a surface draws it under, so a person who sees the message and then opens their page is
    /// told the same thing twice rather than two things.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ADeclineCarriesTheReasonAndNeitherNote()
    {
        var store = new InMemoryRequestStore();
        var told = new RecordingRequesterNotice();
        var held = await store.AddAsync(
            AnAsk(Asker, "The Conversation") with { RequesterNote = "the one from 1974, not the remake" },
            CancellationToken.None).ConfigureAwait(true);

        await Controller(store, told)
            .DeclineAsync(
                held.Request.Id,
                new DeclineRequestBody
                {
                    Revision = held.Revision,
                    Reason = DeclineReason.CannotBeObtained,
                    Note = "nothing I can reach has it in a watchable copy"
                },
                CancellationToken.None)
            .ConfigureAwait(true);

        var one = Assert.Single(told.Told);

        Assert.Contains(
            StringCatalogue.Shipped.Get("declineReason.CannotBeObtained", culture: null),
            one.Text,
            StringComparison.Ordinal);

        // Neither note. The operator's is on the person's own page, which is still there tomorrow,
        // and the requester's own tells them nothing they do not already know.
        Assert.DoesNotContain("watchable copy", one.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("the remake", one.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The library moving a request to fulfilled tells the person who asked, which is the movement
    /// nobody decided and therefore the one nothing else would tell them about until they next
    /// looked.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheLibraryFindingItTellsThePersonWhoWasWaiting()
    {
        var store = new InMemoryRequestStore();
        var told = new RecordingRequesterNotice();
        var library = new FakeLibrary();
        var clock = new TestClock(Started);
        var held = await store.AddAsync(AnAsk(Asker, "The Conversation"), CancellationToken.None).ConfigureAwait(true);

        await store.ReplaceAsync(
            RequestLifecycle.Move(held.Request, RequestState.Approved, Started, RequestCaller.Administrator(Operator)),
            held.Revision,
            CancellationToken.None).ConfigureAwait(true);

        library.Put(RequestedItemKind.Movie, "Tmdb", "603");
        clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(1, await Sweep(store, library, clock, told).SweepAsync(CancellationToken.None).ConfigureAwait(true));

        var one = Assert.Single(told.Told);

        Assert.Equal(Asker, one.ToUserId);
        Assert.Equal(StringCatalogue.Shipped.Get(LiveSentences.Fulfilled, culture: null).Replace("{0}", "The Conversation", StringComparison.Ordinal), one.Text);
    }

    /// <summary>
    /// An observation that changed nothing tells nobody.
    /// <para>
    /// The sweep writes on every run that sees a title's availability move, and only some of those
    /// writes are a request moving. A message per write is a person told that their request changed
    /// every time the sweep looked at it.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnObservationThatMovedNothingTellsNobody()
    {
        var store = new InMemoryRequestStore();
        var told = new RecordingRequesterNotice();
        var library = new FakeLibrary();
        var clock = new TestClock(Started);
        var held = await store.AddAsync(AnAsk(Asker, "The Conversation"), CancellationToken.None).ConfigureAwait(true);

        // Declined rather than open, because the table refuses a declined request moving to
        // fulfilled: approving it first is the move that says a person changed the answer. So the
        // sweep sees the title arrive, writes the availability it saw, and moves nothing.
        await store.ReplaceAsync(
            RequestLifecycle.Decline(
                held.Request,
                DeclineReason.NotWanted,
                note: null,
                Started,
                RequestCaller.Administrator(Operator)),
            held.Revision,
            CancellationToken.None).ConfigureAwait(true);

        library.Put(RequestedItemKind.Movie, "Tmdb", "603");

        Assert.Equal(0, await Sweep(store, library, clock, told).SweepAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Empty(told.Told);
    }

    /// <summary>
    /// A state this plugin has no sentence for sends nothing, and so does a decline carrying no
    /// reason.
    /// <para>
    /// Both are the same rule pointed at two ways of arriving somewhere nobody wrote the words for.
    /// Withholding a message is recoverable, because the person's own page holds the state; sending
    /// one with a hole where the reason goes is not.
    /// </para>
    /// </summary>
    [Fact]
    public void AMovementWithNoSentenceForItSendsNothing()
    {
        var open = AnAsk(Asker, "The Conversation");

        Assert.Null(RequesterMessage.ForMove(open, StringCatalogue.Shipped));
        Assert.Null(RequesterMessage.ForMove(open with { State = RequestState.Failed }, StringCatalogue.Shipped));

        // A declined request written by something older than the rule that a decline carries a
        // reason. The model refuses to make one; the store can hold one.
        Assert.Null(RequesterMessage.ForMove(open with { State = RequestState.Declined }, StringCatalogue.Shipped));
        Assert.NotNull(RequesterMessage.ForMove(
            open with { State = RequestState.Declined, DeclineReason = DeclineReason.NotWanted },
            StringCatalogue.Shipped));
    }

    /// <summary>
    /// A title longer than a message carries is cut, with an ellipsis so the reader can see that it
    /// was.
    /// <para>
    /// The title is a snapshot of what whoever asked typed and nothing caps it on the way here, so
    /// without this a person's notification area holds five hundred characters somebody felt like
    /// typing.
    /// </para>
    /// </summary>
    [Fact]
    public void ATitleLongerThanAMessageCarriesIsCut()
    {
        var long_ = new string('x', RequesterMessage.TitleMaximumLength + 40);

        var message = RequesterMessage.ForMove(
            AnAsk(Asker, long_) with { State = RequestState.Approved },
            StringCatalogue.Shipped);

        Assert.NotNull(message);
        Assert.DoesNotContain(long_, message.Text, StringComparison.Ordinal);
        Assert.Contains(new string('x', RequesterMessage.TitleMaximumLength) + "...", message.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one call this plugin makes to the server names that one person, goes out under the name a
    /// client acts on, and carries a timeout.
    /// <para>
    /// The double raises on every way of sending anything except the two this plugin is allowed to
    /// make, so this leg asserts the absence twice over: a device broadcast or a remote-control
    /// command would end the test rather than pass it, and the one remaining way of reaching
    /// somebody the message was not about, which is the broadcast to whoever administers the server,
    /// is asserted here to be empty rather than left to raise. The timeout is not decoration - the
    /// web client draws a message carrying one as a notice that fades and one without as a dialog
    /// somebody has to dismiss.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ThePushNamesThatOnePersonAndNothingElseIsReachedFor()
    {
        var sessions = new ASessionManagerThatOnlyDelivers();
        var notice = new ServerRequesterNotice(sessions, new RecordingLogger());

        notice.Tell(new RequesterMessage { ToUserId = Asker, Header = "Requests", Text = "It arrived." });

        await notice.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        var pushed = Assert.Single(sessions.Delivered);

        Assert.Empty(sessions.Broadcasts);
        Assert.Equal(new[] { Asker }, pushed.UserIds);
        Assert.Equal(SessionMessageType.GeneralCommand, pushed.Name);

        var command = Assert.IsType<GeneralCommand>(pushed.Payload);

        Assert.Equal(GeneralCommandType.DisplayMessage, command.Name);
        Assert.Equal("Requests", command.Arguments["Header"]);
        Assert.Equal("It arrived.", command.Arguments["Text"]);
        Assert.Equal("8000", command.Arguments["TimeoutMs"]);
    }

    /// <summary>
    /// A push that fails costs the request nothing and lands in the log.
    /// <para>
    /// The move is in the store before anybody is told, so a client that cannot be reached must not
    /// be able to undo a decision, and telling somebody is a call that hands nothing back for a
    /// caller to check. What is left is the log line, and the failure the line has to survive is the
    /// one nobody named: the exception is caught by kind rather than by name, because the host
    /// decides what a push can raise on two server generations.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task APushThatFailsCostsTheRequestNothing()
    {
        var log = new RecordingLogger();
        var notice = new ServerRequesterNotice(new ASessionManagerThatOnlyDelivers { Refuses = true }, log);

        notice.Tell(new RequesterMessage { ToUserId = Asker, Header = "Requests", Text = "It arrived." });

        // Returns rather than raising, which is the whole promise of the interface.
        await notice.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        var reported = Assert.Single(log.At(LogLevel.Warning));

        Assert.NotNull(reported.Exception);
        Assert.Contains("stands", reported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A controller telling the path handed in.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="told">Where the messages go.</param>
    /// <returns>The controller.</returns>
    private RequestsController Controller(InMemoryRequestStore store, IRequesterNotice told)
        => new RequestsController(
            store,
            new TestClock(Started),
            _identifiers,
            new FakeCallerIdentity(Operator),
            new FakeInstallSettings(),
            new RecordingJournal(),
            new RecordingSink(),
            told,
            new RecordingArrivalNotice(),
            new FakeLibrary(),
            ABridgeSubmission.WithNothingBehindIt(store));

    /// <summary>
    /// A sweep telling the path handed in.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="library">What the server holds.</param>
    /// <param name="clock">The clock the observation is stamped from.</param>
    /// <param name="told">Where the messages go.</param>
    /// <returns>The sweep.</returns>
    private static FulfilmentSweep Sweep(InMemoryRequestStore store, FakeLibrary library, TestClock clock, IRequesterNotice told)
        => new FulfilmentSweep(store, library, clock, new RecordingJournal(), new RecordingSink(), told, new RecordingLogger());

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

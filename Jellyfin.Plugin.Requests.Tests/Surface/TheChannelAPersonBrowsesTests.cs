using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Localisation;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Surface;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using MediaBrowser.Controller.Channels;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Surface;

/// <summary>
/// The view a person gets of their own requests on a client this project has never touched.
/// <para>
/// The page reaches a browser and nothing else. The channel is the surface that reaches a
/// television client and a set-top box, which on a media server is most of the people using it, and
/// <c>docs/surface.md</c> is where that was decided. What this holds is what the channel answers:
/// one folder per state the person has something in, the titles inside them, the reason a decline
/// was given, and a sentence rather than an empty tree for somebody who has asked for nothing.
/// </para>
/// <para>
/// <b>What the catalogue legs cannot see.</b> Every sentence below is compared against the value
/// the catalogue holds rather than against a word typed here, which catches a channel that stops
/// looking a key up and catches a key that is missing. It does not catch a copy: the same sentence
/// written into the channel by hand passes, because the two strings are then equal. That near-miss
/// was executed rather than reasoned about, with the catalogue's own words for a declined reason
/// written straight into the channel, and the suite stayed green. What the assets have for this,
/// <c>NoAssetCarriesOneOfTheseSentencesItself</c>, reads the shipped resources, and there is no
/// resource here to read: the channel is code. So this is a bound on the legs below rather than a
/// gap somebody has not noticed.
/// </para>
/// <para>
/// <b>The bound, and it is the same one every check over this surface carries.</b> Nothing here
/// runs a server and nothing renders anything, which the headless rule in <c>docs/testing.md</c>
/// settles. So what is held is the answer this plugin hands the server, never what the server does
/// with it afterwards and never what a client draws. What a running server does with two callers in
/// turn is #67, and it is measured on a real server rather than argued here.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class TheChannelAPersonBrowsesTests
{
    private static readonly Guid Asker = new Guid("70000000-0000-0000-0000-000000000001");
    private static readonly Guid Somebody = new Guid("70000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Asked = new DateTimeOffset(2026, 8, 8, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The root is one folder per state this person actually has something in, in the order
    /// somebody reads rather than in the order the states are stored.
    /// <para>
    /// Grouping is the whole shape of this view: a flat list with a state column is what the page
    /// draws, and a folder tree that repeats it says nothing a person can navigate. A state they
    /// have nothing in is not a folder, because an empty folder in a tree is a thing somebody opens
    /// for no reason.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheRootIsOneFolderPerStateTheyHaveSomethingIn()
    {
        var store = new InMemoryRequestStore();
        await store.AddAsync(ARequest(1, Asker), CancellationToken.None).ConfigureAwait(true);
        await store.AddAsync(ARequest(2, Asker) with { State = RequestState.Fulfilled }, CancellationToken.None).ConfigureAwait(true);
        await store.AddAsync(ARequest(3, Asker) with { State = RequestState.Fulfilled }, CancellationToken.None).ConfigureAwait(true);

        var answer = await Channel(store).GetChannelItems(Browsing(Asker, null), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(
            [
                RequestsChannel.StateFolderPrefix + nameof(RequestState.Open),
                RequestsChannel.StateFolderPrefix + nameof(RequestState.Fulfilled)
            ],
            answer.Items.Select(item => item.Id));

        Assert.All(answer.Items, item => Assert.Equal(ChannelItemType.Folder, item.Type));
        Assert.Equal(2, answer.TotalRecordCount);
    }

    /// <summary>
    /// Every folder name is the catalogue's word for that state, compared against the catalogue
    /// rather than against a word typed into this test.
    /// <para>
    /// A leg asserting the wording would go red the day somebody improves a sentence, which teaches
    /// whoever meets it to change the test. What matters is that the channel and the page say the
    /// same thing, and they do that by both looking the same key up.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EveryFolderIsNamedByTheCatalogueRatherThanBySomethingWrittenHere()
    {
        var store = new InMemoryRequestStore();
        await store.AddAsync(ARequest(1, Asker), CancellationToken.None).ConfigureAwait(true);
        await store.AddAsync(ARequest(2, Asker) with { State = RequestState.Declined, DeclineReason = DeclineReason.NoRoomForIt }, CancellationToken.None).ConfigureAwait(true);

        var answer = await Channel(store).GetChannelItems(Browsing(Asker, null), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(
            [
                StringCatalogue.Shipped.Get("mine.state." + nameof(RequestState.Open), null),
                StringCatalogue.Shipped.Get("mine.state." + nameof(RequestState.Declined), null)
            ],
            answer.Items.Select(item => item.Name));
    }

    /// <summary>
    /// A folder holds the requests in its own state and no others.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AFolderHoldsTheRequestsInThatStateAndNoOthers()
    {
        var store = new InMemoryRequestStore();
        await store.AddAsync(ARequest(1, Asker), CancellationToken.None).ConfigureAwait(true);
        await store.AddAsync(ARequest(2, Asker) with { State = RequestState.Fulfilled }, CancellationToken.None).ConfigureAwait(true);

        var answer = await Channel(store)
            .GetChannelItems(Browsing(Asker, RequestsChannel.StateFolderPrefix + nameof(RequestState.Fulfilled)), CancellationToken.None)
            .ConfigureAwait(true);

        var row = Assert.Single(answer.Items);

        Assert.Equal(Identifier(2).ToString("D", CultureInfo.InvariantCulture), row.Id);
        Assert.Equal(ChannelItemType.Media, row.Type);
    }

    /// <summary>
    /// Nothing but this person's own requests reaches the answer, whichever folder is opened.
    /// <para>
    /// The store is asked for one person's requests rather than for everything and filtered
    /// afterwards, and this is what holds that: a second person's request in the same state as the
    /// first person's is in the store while the folder is opened, and it is not in the answer.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task NothingButTheirOwnRequestsReachesTheAnswer()
    {
        var store = new InMemoryRequestStore();
        await store.AddAsync(ARequest(1, Asker), CancellationToken.None).ConfigureAwait(true);
        await store.AddAsync(ARequest(2, Somebody), CancellationToken.None).ConfigureAwait(true);

        var inside = await Channel(store)
            .GetChannelItems(Browsing(Asker, RequestsChannel.StateFolderPrefix + nameof(RequestState.Open)), CancellationToken.None)
            .ConfigureAwait(true);

        var row = Assert.Single(inside.Items);
        Assert.Equal(Identifier(1).ToString("D", CultureInfo.InvariantCulture), row.Id);
    }

    /// <summary>
    /// No row names anybody, including the person reading it.
    /// <para>
    /// This is the leg that goes past the obvious reading of the one above. It is not enough that
    /// somebody sees only their own rows: a row that carries a user identifier is a way to learn
    /// one, and a channel's rows are written into the server's own library database where this
    /// plugin no longer decides who reads them. Everything the answer carries is searched rather
    /// than the fields this test happens to know the names of.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task NoRowNamesAnybodyIncludingThePersonReadingIt()
    {
        var store = new InMemoryRequestStore();
        await store.AddAsync(
            ARequest(1, Asker) with
            {
                JoinedByUserIds = [Somebody],
                State = RequestState.Declined,
                DeclineReason = DeclineReason.NoRoomForIt,
                DeclineNote = "There is no room for it this month.",
                StateChangedByUserId = Somebody
            },
            CancellationToken.None).ConfigureAwait(true);

        var channel = Channel(store);
        var root = await channel.GetChannelItems(Browsing(Asker, null), CancellationToken.None).ConfigureAwait(true);
        var inside = await channel
            .GetChannelItems(Browsing(Asker, RequestsChannel.StateFolderPrefix + nameof(RequestState.Declined)), CancellationToken.None)
            .ConfigureAwait(true);

        var written = string.Join(
            "\n",
            root.Items.Concat(inside.Items).Select(item => string.Join("\n", item.Id, item.Name, item.Overview)));

        Assert.DoesNotContain(Asker.ToString("D", CultureInfo.InvariantCulture), written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Somebody.ToString("D", CultureInfo.InvariantCulture), written, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A declined request carries the reason it was declined, and the note beside it where one was
    /// given.
    /// <para>
    /// It is the second condition of #66 and it is the one thing a person cannot get anywhere else:
    /// a state name says the answer was no, and the reason is what stops them asking again for the
    /// same thing or going and asking the operator why.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ADeclinedRequestCarriesTheReasonAndTheNoteWhereOneWasGiven()
    {
        const string Note = "There is no room for it this month.";

        var store = new InMemoryRequestStore();
        await store.AddAsync(
            ARequest(1, Asker) with
            {
                State = RequestState.Declined,
                DeclineReason = DeclineReason.NoRoomForIt,
                DeclineNote = Note
            },
            CancellationToken.None).ConfigureAwait(true);

        var answer = await Channel(store)
            .GetChannelItems(Browsing(Asker, RequestsChannel.StateFolderPrefix + nameof(RequestState.Declined)), CancellationToken.None)
            .ConfigureAwait(true);

        var said = Assert.Single(answer.Items).Overview;

        // Composed out of the catalogue rather than typed, and compared whole rather than by
        // containment, so a channel that drops the note or joins the two with wording of its own
        // reds here instead of passing on a substring.
        var why = string.Format(
            CultureInfo.InvariantCulture,
            StringCatalogue.Shipped.Get("declineReason.withNote", null),
            StringCatalogue.Shipped.Get("declineReason." + nameof(DeclineReason.NoRoomForIt), null),
            Note);

        Assert.Equal(
            string.Format(
                CultureInfo.InvariantCulture,
                StringCatalogue.Shipped.Get("queue.askedBefore.withReason", null),
                StringCatalogue.Shipped.Get("outcome.declined", null),
                why),
            said);
    }

    /// <summary>
    /// A request nobody has answered says what is happening and that asking again does not move it,
    /// rather than repeating a state name.
    /// <para>
    /// The waiting case is where somebody goes and asks the operator, which is the message this
    /// plugin exists to remove, and the sentence is the catalogue's own so that this surface and
    /// the page say one thing rather than two.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SomethingNobodyHasAnsweredCarriesTheWaitingSentence()
    {
        var store = new InMemoryRequestStore();
        await store.AddAsync(ARequest(1, Asker), CancellationToken.None).ConfigureAwait(true);

        var answer = await Channel(store)
            .GetChannelItems(Browsing(Asker, RequestsChannel.StateFolderPrefix + nameof(RequestState.Open)), CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(StringCatalogue.Shipped.Get(Sentences.Waiting, null), Assert.Single(answer.Items).Overview);
    }

    /// <summary>
    /// Somebody who has never asked for anything is told so, and opening what they are shown is
    /// answered with nothing rather than with a failure.
    /// <para>
    /// This is the third condition of #66 and it is the cheap one that gets skipped. A tree with
    /// nothing in it is indistinguishable from a plugin that is broken, and a folder that raises
    /// when it is opened is worse than either.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SomebodyWhoHasAskedForNothingIsToldSoAndOpeningItIsHarmless()
    {
        var channel = Channel(new InMemoryRequestStore());

        var root = await channel.GetChannelItems(Browsing(Asker, null), CancellationToken.None).ConfigureAwait(true);

        var shown = Assert.Single(root.Items);
        Assert.Equal(StringCatalogue.Shipped.Get("mine.empty", null), shown.Name);

        var inside = await channel.GetChannelItems(Browsing(Asker, shown.Id), CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(inside.Items);
        Assert.Equal(0, inside.TotalRecordCount);
    }

    /// <summary>
    /// A folder identifier this channel did not write is answered with nothing rather than with a
    /// failure.
    /// <para>
    /// The server keeps the identifiers it was handed earlier, so one arriving after this channel
    /// has stopped writing it is a stale caller rather than a fault, and a channel that raises on
    /// one is a folder tree that breaks for everybody after a rename.
    /// </para>
    /// </summary>
    /// <param name="folderId">The identifier the server hands back.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData("state:Whatever")]
    [InlineData("state:")]
    [InlineData("something-else")]
    public async Task AFolderIdentifierThisChannelDidNotWriteIsAnsweredWithNothing(string folderId)
    {
        var store = new InMemoryRequestStore();
        await store.AddAsync(ARequest(1, Asker), CancellationToken.None).ConfigureAwait(true);

        var answer = await Channel(store).GetChannelItems(Browsing(Asker, folderId), CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(answer.Items);
    }

    /// <summary>
    /// A store that cannot be read is a sentence and a line in the log, not an exception thrown at
    /// whoever opened the folder.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AStoreThatCannotBeReadIsASentenceRatherThanAFailure()
    {
        var log = new RecordingLogger();

        var answer = await new RequestsChannel(new StoreThatCannotBeRead(), StringCatalogue.Shipped, log)
            .GetChannelItems(Browsing(Asker, null), CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(StringCatalogue.Shipped.Get("mine.notRead", null), Assert.Single(answer.Items).Name);
        Assert.NotEmpty(log.At(LogLevel.Warning));
    }

    /// <summary>
    /// Two people are never handed one another's copy of this answer, and neither is handed one
    /// from before the store moved.
    /// <para>
    /// A channel that does not carry a cache key derives one path for every user on the server, and
    /// this channel's answer is one person's requests. That measurement is on #66 and it is read off
    /// the server's own source; what is held here is the half this plugin decides, which is that
    /// the key it hands over separates two people and moves when the store does.
    /// </para>
    /// </summary>
    [Fact]
    public void TheCacheKeySeparatesTwoPeopleAndMovesWhenTheStoreDoes()
    {
        var store = new InMemoryRequestStore();
        var channel = Channel(store);

        var first = channel.GetCacheKey(Asker.ToString("D", CultureInfo.InvariantCulture));
        var second = channel.GetCacheKey(Somebody.ToString("D", CultureInfo.InvariantCulture));

        Assert.NotEqual(first, second);

        store.LastWrittenAt = Asked;

        Assert.NotEqual(first, channel.GetCacheKey(Asker.ToString("D", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// No row carries a provider identifier.
    /// <para>
    /// A channel item with one is a row the server can match against real media, and these rows are
    /// not media: each is a record that somebody asked for something. This is the leg that keeps the
    /// awkwardness <c>docs/surface.md</c> accepts from turning into the thing it rejected outright,
    /// which is rows that are not media appearing as if they were.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task NoRowCarriesAProviderIdentifier()
    {
        var store = new InMemoryRequestStore();
        await store.AddAsync(
            ARequest(1, Asker) with { ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "603" } },
            CancellationToken.None).ConfigureAwait(true);

        var answer = await Channel(store)
            .GetChannelItems(Browsing(Asker, RequestsChannel.StateFolderPrefix + nameof(RequestState.Open)), CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Empty(Assert.Single(answer.Items).ProviderIds);
    }

    private static RequestsChannel Channel(IRequestStore store)
        => new RequestsChannel(store, StringCatalogue.Shipped, new RecordingLogger());

    private static InternalChannelItemQuery Browsing(Guid who, string? folderId)
        => new InternalChannelItemQuery { UserId = who, FolderId = folderId };

    private static Guid Identifier(int ordinal)
        => new Guid(string.Format(CultureInfo.InvariantCulture, "00000000-0000-0000-0000-{0:D12}", ordinal));

    private static MediaRequest ARequest(int ordinal, Guid who) => new MediaRequest
    {
        Id = Identifier(ordinal),
        RequestedByUserId = who,
        RequestedAt = Asked,
        StateChangedAt = Asked.AddMinutes(ordinal),
        Kind = RequestedItemKind.Movie,
        DisplayTitle = string.Format(CultureInfo.InvariantCulture, "The Conversation {0}", ordinal)
    };
}

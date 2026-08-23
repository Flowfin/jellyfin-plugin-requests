using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Localisation;
using Jellyfin.Plugin.Requests.Surface;
using MediaBrowser.Controller.Channels;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Surface;

/// <summary>
/// What the channel hands the server, which is one folder and is the same whoever is asking.
/// <para>
/// <b>This file held a view of one person's own requests until 2026-08-23.</b> It was replaced
/// because #67 measured, on a running server of each claimed line, that an answer built from one
/// person's requests does not stay that person's: two people browsed in turn, the first browsed
/// again, and the first was handed a title only the second had asked for.
/// <c>scripts/verify-user-isolation.sh</c> is that reading and <c>docs/surface.md</c> carries the
/// transcript with the jobs it came from.
/// </para>
/// <para>
/// <b>So the property these legs hold is not a filter, it is an absence.</b> A leg asserting that
/// somebody is shown their own rows and not another person's is exactly what the old file held, and
/// it passed while the server was handing one person the other's answer, because the answer this
/// plugin gives and the answer the server serves are two different things. What cannot be the wrong
/// person's is an answer that never depended on a person, and that is what is checked below.
/// </para>
/// <para>
/// <b>The bound, and it is the same one every check over this surface carries.</b> Nothing here
/// runs a server and nothing renders anything, which the headless rule in <c>docs/testing.md</c>
/// settles. So what is held is the answer this plugin hands the server, never what the server does
/// with it and never what a client draws. That is precisely why the reading on a running server
/// exists beside these legs rather than instead of them.
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

    /// <summary>
    /// The root is one folder, and it says where a person reads their own requests.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheRootIsOneFolderSayingWhereToLook()
    {
        var answer = await Channel().GetChannelItems(Browsing(Asker, null), CancellationToken.None).ConfigureAwait(true);

        var folder = Assert.Single(answer.Items);

        Assert.Equal(RequestsChannel.WhereToLookFolderId, folder.Id);
        Assert.Equal(ChannelItemType.Folder, folder.Type);
        Assert.Equal(1, answer.TotalRecordCount);
    }

    /// <summary>
    /// Two people are handed the same answer, field for field.
    /// <para>
    /// This is the leg the whole file turns on. #67's failure was one person being served another
    /// person's answer, and the only thing that makes that impossible rather than unlikely is an
    /// answer that is not a function of who asked. Everything a caller can be told apart by is
    /// compared, rather than the row count, because two answers of one row each are the case this
    /// would otherwise pass on.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TwoPeopleAreHandedTheSameAnswer()
    {
        var channel = Channel();

        var mine = await channel.GetChannelItems(Browsing(Asker, null), CancellationToken.None).ConfigureAwait(true);
        var theirs = await channel.GetChannelItems(Browsing(Somebody, null), CancellationToken.None).ConfigureAwait(true);
        var nobodys = await channel.GetChannelItems(Browsing(Guid.Empty, null), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(
            mine.Items.Select(Describe).ToArray(),
            theirs.Items.Select(Describe).ToArray());

        Assert.Equal(
            mine.Items.Select(Describe).ToArray(),
            nobodys.Items.Select(Describe).ToArray());
    }

    /// <summary>
    /// The folder is named by the catalogue rather than by a word written here.
    /// <para>
    /// It catches a channel that stops looking the key up and a key that is missing. It does not
    /// catch a copy: the same sentence written into the channel by hand passes, because the two
    /// strings are then equal. That bound was true of the file this replaces and is unchanged.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheFolderIsNamedByTheCatalogue()
    {
        var answer = await Channel().GetChannelItems(Browsing(Asker, null), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(
            StringCatalogue.Shipped.Get(ChannelWords.WhereToLook, null),
            Assert.Single(answer.Items).Name);
    }

    /// <summary>
    /// Opening anything is answered with nothing rather than with a failure, including a folder
    /// identifier written by the shape this replaced.
    /// <para>
    /// The server keeps identifiers it was handed earlier, so a client that saved a state folder
    /// from the view this channel used to answer will ask for it again. A refusal there is a folder
    /// that fails to open, which on a client is indistinguishable from a plugin that is gone.
    /// </para>
    /// </summary>
    /// <param name="folderId">The folder being opened.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData(RequestsChannel.WhereToLookFolderId)]
    [InlineData("state:Open")]
    [InlineData("state:Declined")]
    [InlineData("nothing-yet")]
    [InlineData("something-nothing-ever-wrote")]
    public async Task OpeningAnythingIsAnsweredWithNothing(string folderId)
    {
        var answer = await Channel().GetChannelItems(Browsing(Asker, folderId), CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(answer.Items);
        Assert.Equal(0, answer.TotalRecordCount);
    }

    /// <summary>
    /// The channel is built from the catalogue and from nothing else.
    /// <para>
    /// This is the guard against the repair somebody will reach for first. The answer above cannot
    /// be one person's while this holds, because there is nothing in the object to make it one
    /// from, and re-adding the store is then a change to this leg rather than a line inside a
    /// method that nothing reads. #67 is why the store is not there, and the reading on a running
    /// server is what would have to be re-run before it comes back.
    /// </para>
    /// </summary>
    [Fact]
    public void TheChannelIsBuiltFromTheCatalogueAndNothingElse()
    {
        var built = Assert.Single(typeof(RequestsChannel).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        Assert.Equal(
            new[] { typeof(StringCatalogue) },
            built.GetParameters().Select(taken => taken.ParameterType).ToArray());
    }

    /// <summary>
    /// The name and the description come from the catalogue, and the answer the server keys its own
    /// copy on does not move.
    /// <para>
    /// The version is a constant because the answer is one folder that is the same for everybody.
    /// A version that moved would make the server ask again for an answer that cannot have changed,
    /// and a version that named a person would be per-user data on the surface that carries none.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWordsAndTheVersionAreWhatTheCatalogueAndTheAnswerMake()
    {
        var channel = Channel();

        Assert.Equal(StringCatalogue.Shipped.Get("mine.title", null), channel.Name);
        Assert.Equal(StringCatalogue.Shipped.Get(ChannelWords.Description, null), channel.Description);
        Assert.Equal(channel.DataVersion, Channel().DataVersion);
        Assert.NotEmpty(channel.DataVersion);
    }

    /// <summary>
    /// One row, as everything a caller could tell two answers apart by.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>What it holds.</returns>
    private static string Describe(ChannelItemInfo row) => string.Join(
        "|",
        row.Id,
        row.Name,
        row.Type.ToString(),
        row.FolderType.ToString(),
        row.Overview ?? string.Empty,
        row.ProductionYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

    private static RequestsChannel Channel() => new RequestsChannel(StringCatalogue.Shipped);

    private static InternalChannelItemQuery Browsing(Guid who, string? folderId) => new()
    {
        UserId = who,
        FolderId = folderId
    };
}

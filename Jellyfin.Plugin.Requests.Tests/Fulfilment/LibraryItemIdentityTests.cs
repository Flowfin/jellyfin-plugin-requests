using System.Collections.Generic;
using Jellyfin.Plugin.Requests.Fulfilment;
using Jellyfin.Plugin.Requests.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Fulfilment;

/// <summary>
/// The translation between the server's library types and this plugin's vocabulary, against the
/// server's real types rather than against a description of them.
/// </summary>
public class LibraryItemIdentityTests
{
    /// <summary>
    /// A film in the library is a film here, carrying the identifiers it was scanned with.
    /// </summary>
    [Fact]
    public void AFilmIsAFilmAndKeepsItsIdentifiers()
    {
        var change = LibraryItemIdentity.Of(new Movie
        {
            Name = "The Matrix",
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" }
        });

        Assert.NotNull(change);
        Assert.Equal(RequestedItemKind.Movie, change.Kind);
        Assert.Equal("603", change.ProviderIds["Tmdb"]);
    }

    /// <summary>
    /// A programme is a series here. The identifiers are read back under a differently spelled
    /// provider name, because that is the rule identity is written to and a translation that
    /// answered only the exact spelling would quietly narrow it.
    /// </summary>
    [Fact]
    public void AProgrammeIsASeriesAndItsProviderNameIsReadWithoutCase()
    {
        var change = LibraryItemIdentity.Of(new Series
        {
            Name = "Tinker Tailor Soldier Spy",
            ProviderIds = new Dictionary<string, string> { ["Tvdb"] = "76648" }
        });

        Assert.NotNull(change);
        Assert.Equal(RequestedItemKind.Series, change.Kind);
        Assert.Equal("76648", change.ProviderIds["tvdb"]);
    }

    /// <summary>
    /// The answer is a copy. A library item is a live object the server goes on writing to, and this
    /// answer is put in a queue and read later on another thread.
    /// </summary>
    [Fact]
    public void TheIdentifiersAreCopiedRatherThanTheItemsOwn()
    {
        var film = new Movie
        {
            Name = "The Matrix",
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" }
        };

        var change = LibraryItemIdentity.Of(film);

        film.ProviderIds["Tmdb"] = "something else";

        Assert.NotNull(change);
        Assert.Equal("603", change.ProviderIds["Tmdb"]);
    }

    /// <summary>
    /// An item nobody identified answers with no identifiers rather than with nothing, so the caller
    /// finds no request rather than having to test for a missing map.
    /// </summary>
    [Fact]
    public void AnItemNobodyIdentifiedAnswersWithAnEmptySet()
    {
        var change = LibraryItemIdentity.Of(new Movie { Name = "Something off a disk" });

        Assert.NotNull(change);
        Assert.Empty(change.ProviderIds);
    }

    /// <summary>
    /// Something a request cannot name answers nothing. The two kinds a record can express are the
    /// two answered, and a library holds a great deal that is neither.
    /// </summary>
    /// <param name="item">A library item of a kind no request names.</param>
    [Theory]
    [MemberData(nameof(ItemsNoRequestNames))]
    public void SomethingNoRequestCanNameAnswersNothing(BaseItem item)
        => Assert.Null(LibraryItemIdentity.Of(item));

    /// <summary>
    /// Gets library items of kinds this plugin's records cannot express.
    /// </summary>
    /// <returns>One item per row.</returns>
    public static TheoryData<BaseItem> ItemsNoRequestNames() => new TheoryData<BaseItem>
    {
        new Season { Name = "Season 1" },
        new Episode { Name = "The one with the mole" },
        new Audio { Name = "A song" },
        new Folder { Name = "A folder" }
    };
}

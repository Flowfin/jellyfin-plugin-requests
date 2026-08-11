using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Requests.Fulfilment;
using Jellyfin.Plugin.Requests.Model;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Fulfilment;

/// <summary>
/// What the library holding for a request means, which is the rule #42 asks to be stated rather
/// than rounded. The partly satisfied series is the case the whole rule exists for and it has a
/// value of its own here, so a request waiting on three seasons with one of them present reads as
/// partial rather than as arrived or as missing.
/// </summary>
public class FulfilmentRuleTests
{
    /// <summary>
    /// A film the server does not have is absent, and one it has is present. There is no third
    /// answer for a film, because there is no part of one to have arrived.
    /// </summary>
    /// <param name="held">Whether the server holds it.</param>
    /// <param name="expected">What that means.</param>
    [Theory]
    [InlineData(false, LibraryAvailability.Absent)]
    [InlineData(true, LibraryAvailability.Present)]
    public void AFilmIsPresentOrAbsentAndNothingElse(bool held, LibraryAvailability expected)
    {
        var film = Request(RequestedItemKind.Movie);
        var holding = held ? new LibraryHolding { Held = true } : LibraryHolding.Nothing;

        Assert.Equal(expected, FulfilmentRule.AvailabilityOf(film, holding));
    }

    /// <summary>
    /// A request that named seasons is judged against those seasons and against nothing else.
    /// Every one present is present, some of them is partial, and none of them is absent even
    /// though the programme itself is in the library.
    /// </summary>
    /// <param name="asked">The seasons the request named.</param>
    /// <param name="arrived">The seasons the server has files for.</param>
    /// <param name="expected">What that means for this request.</param>
    [Theory]
    [InlineData(new[] { 2, 3 }, new[] { 2, 3 }, LibraryAvailability.Present)]
    [InlineData(new[] { 2, 3 }, new[] { 2 }, LibraryAvailability.Partial)]
    [InlineData(new[] { 2, 3 }, new[] { 1 }, LibraryAvailability.Absent)]
    [InlineData(new[] { 2, 3 }, new int[0], LibraryAvailability.Absent)]
    [InlineData(new[] { 1 }, new[] { 1, 2, 3 }, LibraryAvailability.Present)]
    public void ASeriesIsJudgedAgainstTheSeasonsThatWereAskedFor(
        int[] asked,
        int[] arrived,
        LibraryAvailability expected)
    {
        var series = Request(RequestedItemKind.Series) with { Seasons = asked };
        var holding = new LibraryHolding { Held = true, SeasonsHeld = arrived };

        Assert.Equal(expected, FulfilmentRule.AvailabilityOf(series, holding));
    }

    /// <summary>
    /// A request for the programme rather than for named seasons is present the moment the server
    /// holds the programme, whatever is under it.
    /// <para>
    /// This is the stated rule and it is the only one available. Nothing here can learn how many
    /// seasons a programme has, because this plugin calls no metadata source, so a completeness test
    /// would be written against a number nobody can produce. What it costs is that somebody who
    /// asked for a programme and got its first season sees a fulfilled request, and what they do
    /// about that is ask for the seasons they want by name.
    /// </para>
    /// </summary>
    /// <param name="arrived">The seasons the server has files for.</param>
    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 1, 2, 3 })]
    public void ASeriesAskedForWholeIsPresentAsSoonAsTheServerHoldsIt(int[] arrived)
    {
        var whole = Request(RequestedItemKind.Series);
        var holding = new LibraryHolding { Held = true, SeasonsHeld = arrived };

        Assert.Empty(whole.Seasons);
        Assert.Equal(LibraryAvailability.Present, FulfilmentRule.AvailabilityOf(whole, holding));
    }

    /// <summary>
    /// The reading above is the one <see cref="RequestIdentity"/> already makes, and the two would
    /// contradict each other if this rule read an empty season set as anything else: a request for
    /// the whole programme covers a request for one of its seasons, so it cannot also be a request
    /// that no arrival satisfies.
    /// </summary>
    [Fact]
    public void TheEmptySeasonSetMeansTheSameThingHereAsItDoesToIdentity()
    {
        var whole = Request(RequestedItemKind.Series);
        var oneSeason = Request(RequestedItemKind.Series) with { Seasons = new[] { 2 } };

        Assert.Equal(RequestMatch.Same, RequestIdentity.Compare(whole, oneSeason));
        Assert.Equal(
            LibraryAvailability.Present,
            FulfilmentRule.AvailabilityOf(whole, new LibraryHolding { Held = true, SeasonsHeld = [2] }));
    }

    /// <summary>
    /// A series the server does not hold at all is absent, whether the request named seasons or not.
    /// </summary>
    [Fact]
    public void ASeriesTheServerDoesNotHoldIsAbsent()
    {
        var whole = Request(RequestedItemKind.Series);
        var named = Request(RequestedItemKind.Series) with { Seasons = new[] { 1 } };

        Assert.Equal(LibraryAvailability.Absent, FulfilmentRule.AvailabilityOf(whole, LibraryHolding.Nothing));
        Assert.Equal(LibraryAvailability.Absent, FulfilmentRule.AvailabilityOf(named, LibraryHolding.Nothing));
    }

    /// <summary>
    /// The rule never answers unknown. Unknown is the absence of a look, and something that looked
    /// has to say what it saw.
    /// </summary>
    [Fact]
    public void NothingTheRuleAnswersIsUnknown()
    {
        foreach (var kind in Enum.GetValues<RequestedItemKind>())
        {
            foreach (var holding in new[] { LibraryHolding.Nothing, new LibraryHolding { Held = true } })
            {
                Assert.NotEqual(LibraryAvailability.Unknown, FulfilmentRule.AvailabilityOf(Request(kind), holding));
            }
        }
    }

    private static MediaRequest Request(RequestedItemKind kind)
    {
        var asked = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        return new MediaRequest
        {
            Id = new Guid("2b8f4c61-0d75-4a39-9e26-3f5a8c1d7b04"),
            RequestedByUserId = new Guid("7a4e2f18-6b0c-4d39-95e7-1f8a3c6d2b40"),
            RequestedAt = asked,
            StateChangedAt = asked,
            Kind = kind,
            DisplayTitle = "Tinker Tailor Soldier Spy",
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tvdb"] = "76648" }
        };
    }
}

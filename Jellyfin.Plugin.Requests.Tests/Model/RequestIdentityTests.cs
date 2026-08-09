using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Requests.Model;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Model;

/// <summary>
/// What makes two requests the same request. The failure this stands against is a queue an operator
/// decides three times, and the failure on the other side is two people waiting for one row that
/// only covers what one of them asked for.
/// </summary>
public class RequestIdentityTests
{
    private static readonly Guid First = new("7a4e2f18-6b0c-4d39-95e7-1f8a3c6d2b40");

    private static readonly Guid Second = new("5f0c8a26-3d17-4b94-8e05-9a1b7c2d6e38");

    /// <summary>
    /// A film is the same film when one provider names the same value on both, whatever the titles
    /// say. The two here are the same work under two titles and two release years, which is what a
    /// title comparison gets wrong and what this rule exists to get right.
    /// </summary>
    [Fact]
    public void OneSharedIdentifierMakesTwoFilmsOneRequest()
    {
        var asked = AFilm("Le Salaire de la peur", ("Tmdb", "269"));
        var again = AFilm("The Wages of Fear", ("Tmdb", "269")) with { DisplayYear = 1955 };

        Assert.Equal(RequestMatch.Same, RequestIdentity.Compare(asked, again));
    }

    /// <summary>
    /// One shared identifier is enough even where the rest disagree. A client that knows only one
    /// provider would otherwise file a second request for something already in the queue, which is
    /// the duplicate this rule is about.
    /// </summary>
    [Fact]
    public void OneSharedIdentifierIsEnoughWhereTheOthersAreAbsent()
    {
        var rich = AFilm("Solaris", ("Tmdb", "593"), ("Imdb", "tt0069293"));
        var thin = AFilm("Solaris", ("Imdb", "tt0069293"));

        Assert.Equal(RequestMatch.Same, RequestIdentity.Compare(rich, thin));
        Assert.Equal(RequestMatch.Same, RequestIdentity.Compare(thin, rich));
    }

    /// <summary>
    /// The provider is named without regard to case and the value is not. Two callers spell the
    /// provider differently and neither is wrong; two identifiers that differ are somebody else's
    /// identifiers and this plugin does not get to decide they mean one thing.
    /// </summary>
    [Fact]
    public void TheProviderIsMatchedWithoutCaseAndTheValueExactly()
    {
        var upper = AFilm("Stalker", ("Tmdb", "1398"));
        var lower = AFilm("Stalker", ("tmdb", "1398"));
        var other = AFilm("Stalker", ("Tmdb", "1398 "));

        Assert.Equal(RequestMatch.Same, RequestIdentity.Compare(upper, lower));
        Assert.Equal(RequestMatch.Different, RequestIdentity.Compare(upper, other));
    }

    /// <summary>
    /// Two identical titles with no identifier between them are two requests. This is the answer the
    /// issue asked for rather than a gap: matching them would mean matching on the text somebody
    /// typed, which is the rule the whole class refuses, and it would join two people who may well
    /// have meant two different films.
    /// </summary>
    [Fact]
    public void ARequestWithNoIdentifierIsNobodysDuplicate()
    {
        var typed = AFilm("Nosferatu");
        var typedAgain = AFilm("Nosferatu");

        Assert.Equal(RequestMatch.Different, RequestIdentity.Compare(typed, typedAgain));
        Assert.Equal(RequestMatch.Different, RequestIdentity.Compare(typed, typed));
    }

    /// <summary>
    /// A film and a series carrying the same number under the same provider are two different works.
    /// Provider numbering is per kind, so without this the first collision would join a programme to
    /// a film.
    /// </summary>
    [Fact]
    public void TheKindIsPartOfTheIdentity()
    {
        var film = AFilm("Fargo", ("Tmdb", "275"));
        var series = ASeries("Fargo", [], ("Tmdb", "275"));

        Assert.Equal(RequestMatch.Different, RequestIdentity.Compare(film, series));
    }

    /// <summary>
    /// The season cases, which are the ones this rule was written for. An empty set means the whole
    /// series, and the comparison is not symmetric: the show covers a season of it and a season does
    /// not cover the show.
    /// </summary>
    /// <param name="existingSeasons">The seasons the open request covers.</param>
    /// <param name="incomingSeasons">The seasons being asked for now.</param>
    /// <param name="expected">What the two are.</param>
    [Theory]

    // The same seasons, in any order, are the same ask.
    [InlineData(new[] { 1, 2 }, new[] { 1, 2 }, RequestMatch.Same)]
    [InlineData(new[] { 1, 2 }, new[] { 2, 1 }, RequestMatch.Same)]

    // Everything asked for is already covered, so there is nothing to create.
    [InlineData(new[] { 1, 2, 3 }, new[] { 2 }, RequestMatch.Same)]
    [InlineData(new int[0], new[] { 4 }, RequestMatch.Same)]
    [InlineData(new int[0], new int[0], RequestMatch.Same)]

    // Something is covered and something is not.
    [InlineData(new[] { 1, 2 }, new[] { 2, 3 }, RequestMatch.Overlapping)]
    [InlineData(new[] { 1 }, new int[0], RequestMatch.Overlapping)]

    // The same programme, and nothing in common. Two people waiting for different things is two
    // requests, and a queue showing the series twice there is telling the truth.
    [InlineData(new[] { 1, 2 }, new[] { 3 }, RequestMatch.Different)]
    public void TheSeasonsDecideWhetherASeriesRequestIsTheSameRequest(
        int[] existingSeasons,
        int[] incomingSeasons,
        RequestMatch expected)
    {
        var existing = ASeries("Twin Peaks", existingSeasons, ("Tvdb", "70533"));
        var incoming = ASeries("Twin Peaks", incomingSeasons, ("Tvdb", "70533"));

        Assert.Equal(expected, RequestIdentity.Compare(existing, incoming));
    }

    /// <summary>
    /// What is left to ask for when the seasons overlap. The seasons somebody is already waiting for
    /// are not asked for twice and the ones nobody has asked for are not lost, which is the whole
    /// reason the third answer exists.
    /// </summary>
    [Fact]
    public void WhatIsLeftToAskForIsTheSeasonsTheOpenRequestDoesNotCover()
    {
        var existing = ASeries("Twin Peaks", [1, 2], ("Tvdb", "70533"));
        var incoming = ASeries("Twin Peaks", [3, 2, 4], ("Tvdb", "70533"));

        Assert.Equal(
            [3, 4],
            RequestIdentity.SeasonsNotAlreadyAskedFor(existing, incoming, []));
    }

    /// <summary>
    /// Asking for the whole show against a request for part of it needs to know what the whole show
    /// is. Where the caller knows, the answer is every other season; where it does not, the answer is
    /// empty, which reads as the whole series and is the safe direction, because it asks for no more
    /// than the person did.
    /// </summary>
    [Fact]
    public void AskingForTheWholeShowLeavesTheSeasonsTheCallerKnowsAbout()
    {
        var existing = ASeries("Twin Peaks", [1, 2], ("Tvdb", "70533"));
        var wholeShow = ASeries("Twin Peaks", [], ("Tvdb", "70533"));

        Assert.Equal(
            [3],
            RequestIdentity.SeasonsNotAlreadyAskedFor(existing, wholeShow, [1, 2, 3]));

        Assert.Empty(RequestIdentity.SeasonsNotAlreadyAskedFor(existing, wholeShow, []));
    }

    /// <summary>
    /// Somebody asking for something already open joins it rather than filing a second one, and they
    /// are recorded on the request they joined. Without the record the second person is either a row
    /// an operator decides twice or a person nobody can tell when the answer arrives.
    /// </summary>
    [Fact]
    public void JoiningRecordsTheSecondPersonOnTheRequestTheyJoined()
    {
        var open = AFilm("Solaris", ("Tmdb", "593"));

        var joined = RequestLifecycle.Join(open, Second);

        Assert.Equal([Second], joined.JoinedByUserIds);
        Assert.True(joined.WasAskedForBy(Second));
        Assert.True(joined.WasAskedForBy(First));
        Assert.Empty(open.JoinedByUserIds);
    }

    /// <summary>
    /// A join changes nothing else, and asking twice is not two facts. The state, the times and the
    /// history are what somebody decided; a second person's interest is none of those, so nothing
    /// is appended to the history and nothing moves.
    /// </summary>
    [Fact]
    public void JoiningTwiceRecordsOnePersonAndMovesNothing()
    {
        var open = AFilm("Solaris", ("Tmdb", "593"));

        var joined = RequestLifecycle.Join(RequestLifecycle.Join(open, Second), Second);

        Assert.Single(joined.JoinedByUserIds);
        Assert.Empty(joined.History);
        Assert.Equal(open, joined with { JoinedByUserIds = open.JoinedByUserIds });

        // The person who asked first joining their own request is the same non-event.
        Assert.Empty(RequestLifecycle.Join(open, First).JoinedByUserIds);
    }

    /// <summary>
    /// Only a request that is still waiting can be joined. A declined or fulfilled request has had
    /// its answer, and handing somebody an answer that was given before they asked is worse than
    /// giving them a request of their own.
    /// </summary>
    /// <param name="state">The state the request is in.</param>
    [Theory]
    [InlineData(RequestState.Declined)]
    [InlineData(RequestState.Fulfilled)]
    [InlineData(RequestState.Failed)]
    public void ADecidedRequestCannotBeJoined(RequestState state)
    {
        var decided = AFilm("Solaris", ("Tmdb", "593")) with { State = state };

        Assert.Throws<InvalidOperationException>(() => RequestLifecycle.Join(decided, Second));
    }

    /// <summary>
    /// Somebody who joined is a requester on that request. Both surfaces answer "is this yours"
    /// through the same comparison, so the second person sees on their own page the request they
    /// joined rather than nothing at all.
    /// </summary>
    [Fact]
    public void SomebodyWhoJoinedIsARequesterOnThatRequest()
    {
        var joined = RequestLifecycle.Join(AFilm("Solaris", ("Tmdb", "593")), Second);

        Assert.Equal(RequestActor.Requester, RequestCaller.User(Second).RolesOn(joined));
        Assert.Equal(RequestActor.Requester, RequestCaller.User(First).RolesOn(joined));
        Assert.Equal(
            RequestActor.None,
            RequestCaller.User(new Guid("9c3b7e05-1d42-4a86-b0f9-5e2c8a7d6134")).RolesOn(joined));
    }

    /// <summary>
    /// A request nobody can identify may be declined and may not be moved anywhere else. An
    /// approval on such a request is an operator saying yes to something no part of the plugin can
    /// act on afterwards, and a decline is the one answer that needs no identifier, so it is the one
    /// that stays.
    /// </summary>
    [Fact]
    public void ARequestWithNoIdentifierMayOnlyBeDeclined()
    {
        var typed = AFilm("something a person typed");
        var at = new DateTimeOffset(2026, 8, 9, 15, 0, 0, TimeSpan.Zero);
        var operatorCalling = RequestCaller.Administrator(new Guid("2d8f4b16-3a5c-4d97-8e21-7c6b5a4f3e29"));

        var refusal = Assert.Throws<RequestNotIdentifiedException>(
            () => RequestLifecycle.Move(typed, RequestState.Approved, at, operatorCalling));

        Assert.Equal(RequestState.Approved, refusal.To);

        Assert.Throws<RequestNotIdentifiedException>(
            () => RequestLifecycle.Move(typed, RequestState.Fulfilled, at, RequestCaller.Plugin));

        var declined = RequestLifecycle.Decline(typed, DeclineReason.Other, "Nobody could tell what this was.", at, operatorCalling);

        Assert.Equal(RequestState.Declined, declined.State);
    }

    /// <summary>
    /// The seasons asked for are a set of seasons. A repeat is a caller's mistake stored as a fact,
    /// and a zero is not a season any client has; both are refused rather than tidied away, because
    /// a list quietly cleaned up is one the caller never learns was wrong.
    /// </summary>
    [Fact]
    public void TheSeasonsAskedForAreASetOfSeasons()
    {
        Assert.Throws<ArgumentException>(() => ASeries("Twin Peaks", [1, 1], ("Tvdb", "70533")));
        Assert.Throws<ArgumentException>(() => ASeries("Twin Peaks", [0], ("Tvdb", "70533")));
        Assert.Throws<ArgumentException>(() => ASeries("Twin Peaks", [-1], ("Tvdb", "70533")));

        // The order the caller gave is the order it keeps, because two requests naming the same
        // seasons in different orders are one ask and the comparison does not read the order anyway.
        Assert.Equal([3, 1], ASeries("Twin Peaks", [3, 1], ("Tvdb", "70533")).Seasons);
    }

    private static MediaRequest AFilm(string title, params (string Provider, string Value)[] identifiers)
        => ARequest(RequestedItemKind.Movie, title, [], identifiers);

    private static MediaRequest ASeries(
        string title,
        int[] seasons,
        params (string Provider, string Value)[] identifiers)
        => ARequest(RequestedItemKind.Series, title, seasons, identifiers);

    private static MediaRequest ARequest(
        RequestedItemKind kind,
        string title,
        int[] seasons,
        (string Provider, string Value)[] identifiers)
    {
        var asked = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);
        var providerIds = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (provider, value) in identifiers)
        {
            providerIds[provider] = value;
        }

        return new MediaRequest
        {
            Id = new Guid("41d7c0b2-9e3a-4f58-b6d1-8c2f5a0e7b93"),
            RequestedByUserId = First,
            RequestedAt = asked,
            StateChangedAt = asked,
            Kind = kind,
            DisplayTitle = title,
            Seasons = seasons,
            ProviderIds = providerIds
        };
    }
}

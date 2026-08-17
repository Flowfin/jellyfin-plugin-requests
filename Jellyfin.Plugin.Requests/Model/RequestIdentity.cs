using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// What makes two requests the same request, in one function.
/// <para>
/// It is one function because the question gets asked in three places: when a request is created,
/// when the sibling hands one over, and when an operator looks at a queue and wonders why the same
/// film is in it twice. Three implementations of "the same" are three answers, and the one that
/// disagrees is found by a person reading a queue rather than by anything here.
/// </para>
/// <para>
/// <b>Identity is a provider identifier and a kind, never a title.</b> Titles collide, get
/// re-released, differ by language and are typed by people. Two films called <c>Nosferatu</c> are
/// two films, and one film is called two things on two clients. The identifiers are what the sibling
/// hands over and what a fulfilment check matches on, so they are what identity is made of here as
/// well; anything else would make three parts of this plugin disagree about one request.
/// </para>
/// <para>
/// <b>A request carrying no identifier is nobody's duplicate.</b> It has no identity, so it is
/// <see cref="RequestMatch.Different"/> from everything including another copy of itself, and two
/// people typing the same title by hand get two requests. That is the honest answer rather than a
/// convenient one: the alternative is matching on the text they typed, which is the rule this class
/// exists to refuse. What such a request may do until somebody identifies it is
/// <see cref="RequestLifecycle"/>'s answer, and it is narrow.
/// </para>
/// <para>
/// <b>A series is identified by its seasons as well.</b> A request for one season is not a request
/// for the show, and the set of seasons is part of what is being asked for rather than a detail of
/// it. An empty set means the whole series, which is the one convention here that has to be
/// remembered, and it is the shape a client sends when a person asks for a programme rather than for
/// part of one.
/// </para>
/// </summary>
public static class RequestIdentity
{
    /// <summary>
    /// Holds a new ask up against a request that already exists.
    /// <para>
    /// The order of the arguments matters for a series and only for a series. Whether the seasons
    /// being asked for are already covered is not a symmetric question: a whole-show request covers
    /// a request for its second season, and the second season does not cover the show.
    /// </para>
    /// </summary>
    /// <param name="existing">A request the store already holds.</param>
    /// <param name="incoming">The ask being made now.</param>
    /// <returns>Whether the two are one request, one request with seasons left over, or two.</returns>
    /// <exception cref="ArgumentNullException">Where either request is missing.</exception>
    public static RequestMatch Compare(MediaRequest existing, MediaRequest incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        if (!NameTheSameWork(existing, incoming))
        {
            return RequestMatch.Different;
        }

        if (existing.Kind != RequestedItemKind.Series)
        {
            return RequestMatch.Same;
        }

        // The whole show covers everything, so nothing can be left over against it.
        if (existing.Seasons.Count == 0)
        {
            return RequestMatch.Same;
        }

        // Asked for the whole show against a request for some of it. Every season outside the
        // existing request is still being asked for, and there is at least one.
        if (incoming.Seasons.Count == 0)
        {
            return RequestMatch.Overlapping;
        }

        var covered = incoming.Seasons.Count(season => existing.Seasons.Contains(season));

        if (covered == 0)
        {
            // The same series and no season in common. Two requests, and the queue showing the
            // series twice is the truth rather than a duplicate: they are waiting for different
            // things.
            return RequestMatch.Different;
        }

        return covered == incoming.Seasons.Count ? RequestMatch.Same : RequestMatch.Overlapping;
    }

    /// <summary>
    /// Whether two requests are about the same work at all, before any question about seasons.
    /// <para>
    /// A film and a series can carry the same number under the same provider and be two different
    /// works, so the kind is part of the identity rather than a property of it.
    /// </para>
    /// <para>
    /// This is the coarser of the two questions in this class and the two are not interchangeable.
    /// <see cref="Compare"/> asks whether a new asker joins an existing request, and for a series it
    /// answers on the seasons, so a request for season five is <see cref="RequestMatch.Different"/>
    /// from a request for seasons one and two. That is right for joining and wrong for a reader who
    /// wants to know what has been decided about a series, which is what this answers instead.
    /// </para>
    /// </summary>
    /// <param name="one">A request.</param>
    /// <param name="other">The request it is being held against.</param>
    /// <returns><see langword="true"/> where both name one work.</returns>
    /// <exception cref="ArgumentNullException">Where either request is missing.</exception>
    public static bool NameTheSameWork(MediaRequest one, MediaRequest other)
    {
        ArgumentNullException.ThrowIfNull(one);
        ArgumentNullException.ThrowIfNull(other);

        return one.Kind == other.Kind && ShareAnIdentifier(one, other);
    }

    /// <summary>
    /// The seasons a new ask names that an existing request does not already cover. This is what a
    /// caller creates a request for when <see cref="Compare"/> answered
    /// <see cref="RequestMatch.Overlapping"/>, so that the seasons somebody is already waiting for
    /// are not asked for twice and the ones nobody has asked for are not lost.
    /// </summary>
    /// <param name="existing">A request the store already holds.</param>
    /// <param name="incoming">The ask being made now.</param>
    /// <param name="everySeason">
    /// Every season the series is known to have, for the case where the new ask means the whole show
    /// and the existing request covers part of it. Empty where the caller does not know, and then
    /// the answer is empty as well, which reads as "the whole show" and is the safe direction: it
    /// asks for no more than the person did.
    /// </param>
    /// <returns>The seasons left to ask for, in order, without repeats.</returns>
    /// <exception cref="ArgumentNullException">Where either request or the season list is missing.</exception>
    public static IReadOnlyList<int> SeasonsNotAlreadyAskedFor(
        MediaRequest existing,
        MediaRequest incoming,
        IReadOnlyCollection<int> everySeason)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(everySeason);

        var asked = incoming.Seasons.Count == 0 ? everySeason : incoming.Seasons;

        return [.. asked.Where(season => !existing.Seasons.Contains(season)).Distinct().Order()];
    }

    /// <summary>
    /// Whether the two name the same thing under at least one provider. One is enough: a request
    /// arriving from a client may carry only the identifier that client knows, and requiring every
    /// identifier to match would make the same film two requests because one of them was asked for
    /// from somewhere that had never heard of the other provider.
    /// <para>
    /// Provider names are compared without case, because the same provider is spelled <c>Tmdb</c>
    /// and <c>tmdb</c> by different callers and neither is wrong. The values are compared exactly,
    /// because they are somebody else's identifiers and this plugin does not get to decide that two
    /// of them that differ are the same.
    /// </para>
    /// <para>
    /// The comparison is written out rather than made by looking the name up in the other map. A
    /// dictionary answers under whatever comparer it was built with, and the maps arriving here were
    /// built by callers this class does not control, so a lookup would make the rule depend on how a
    /// caller happened to construct theirs. A request carries a handful of identifiers, so walking
    /// both is not a cost worth trading a rule for.
    /// </para>
    /// </summary>
    /// <param name="existing">A request the store already holds.</param>
    /// <param name="incoming">The ask being made now.</param>
    /// <returns><see langword="true"/> where one provider names one value on both.</returns>
    private static bool ShareAnIdentifier(MediaRequest existing, MediaRequest incoming)
        => existing.ProviderIds.Any(mine =>
            incoming.ProviderIds.Any(theirs =>
                string.Equals(mine.Key, theirs.Key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(mine.Value, theirs.Value, StringComparison.Ordinal)));
}

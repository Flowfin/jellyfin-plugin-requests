using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Storage;

/// <summary>
/// What the administrator queue asks the store for: which requests, in what order, and which slice
/// of them.
/// <para>
/// Two filters rather than one per field. The state and the kind are what an operator narrows a
/// queue by, and each one the store offers is a condition it has to be able to answer at the size
/// the queue reaches. Adding a third is one line in the store's filter and one property here, and
/// it is a decision rather than a convenience: <c>docs/storage.md</c> states what this path costs
/// and a filter added without reading that is a filter added without paying for it.
/// </para>
/// <para>
/// Whose requests these are is deliberately not a filter here. One person's requests are their own
/// query path, <see cref="IRequestStore.FindForUserAsync"/>, because it is asked once per page view
/// by every user on the server rather than by one operator, and it is served by a lookup instead of
/// by a walk of the queue.
/// </para>
/// </summary>
public sealed record RequestQuery
{
    private readonly IReadOnlyList<RequestState> _states = [];
    private readonly IReadOnlyList<RequestedItemKind> _kinds = [];
    private readonly int _skip;
    private readonly int _take;

    /// <summary>
    /// Gets the states a request may be in to match, or empty for all of them. Empty means all
    /// rather than none, because a query naming no state is a queue that has not been narrowed and
    /// not a queue with nothing in it.
    /// </summary>
    public IReadOnlyList<RequestState> States
    {
        get => _states;
        init => _states = [.. value ?? []];
    }

    /// <summary>
    /// Gets the kinds of thing a request may name to match, or empty for all of them. Empty means
    /// all, under the same rule as <see cref="States"/>.
    /// </summary>
    public IReadOnlyList<RequestedItemKind> Kinds
    {
        get => _kinds;
        init => _kinds = [.. value ?? []];
    }

    /// <summary>
    /// Gets what the matches are ordered by before the page is taken.
    /// </summary>
    public RequestQueryOrder Order { get; init; } = RequestQueryOrder.RequestedAt;

    /// <summary>
    /// Gets a value indicating whether the order runs the other way. A queue is usually read oldest
    /// first, which is why this defaults to <see langword="false"/>, and the recently moved one is
    /// read newest first.
    /// </summary>
    public bool Descending { get; init; }

    /// <summary>
    /// Gets how many matches to step over before the page starts.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Where it is below zero.</exception>
    public int Skip
    {
        get => _skip;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _skip = value;
        }
    }

    /// <summary>
    /// Gets how many matches the page holds at most. Required, because a page size nobody stated is
    /// one somebody inherited: the caller that wants the whole queue in one answer should have to
    /// write that down.
    /// <para>
    /// Zero is legal and is the shape of "how many are there". It returns no requests and the count
    /// of matches, which is what a surface asks when it is drawing a pager before it draws rows.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Where it is below zero.</exception>
    public required int Take
    {
        get => _take;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _take = value;
        }
    }

    /// <summary>
    /// Whether one request is inside this query's filter. Here rather than in the store, so the
    /// rule a page is taken under and the rule a count is made under cannot drift apart, and so a
    /// filter added to this type is a filter every store keeps.
    /// </summary>
    /// <param name="request">The request being judged.</param>
    /// <returns><see langword="true"/> where every named filter admits it.</returns>
    /// <exception cref="ArgumentNullException">Where the request is missing.</exception>
    public bool Matches(MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The emptiness is tested before the membership, in both filters, because an empty list
        // contains nothing and asking it directly would turn "not narrowed" into "matches nothing".
        if (_states.Count > 0 && !_states.Contains(request.State))
        {
            return false;
        }

        return _kinds.Count == 0 || _kinds.Contains(request.Kind);
    }

    /// <summary>
    /// This query answered against a set of requests: the filter, the order and the page, and the
    /// count of everything that matched before the page was taken.
    /// <para>
    /// It is here for the reason <see cref="Matches"/> is. A store answers this over everything it
    /// holds; the user surface answers it over one person's own requests, which the store hands back
    /// through its own lookup rather than by walking the queue. Both are the same question over a
    /// different set, and a second implementation of the ordering is a second place the tiebreak can
    /// be dropped from.
    /// </para>
    /// <para>
    /// One walk of the candidates, and the count and the page come out of the same walk, so a pager
    /// cannot disagree with the rows above it.
    /// </para>
    /// </summary>
    /// <param name="candidates">The requests this query is answered over.</param>
    /// <returns>The page, and how many matched.</returns>
    /// <exception cref="ArgumentNullException">Where the candidates are missing.</exception>
    /// <exception cref="InvalidOperationException">
    /// Where <see cref="Order"/> is a value nothing here has a comparison for. It is refused rather
    /// than served by whichever arm happened to be written last, because a queue quietly ordered by
    /// something else is worse than one that says it cannot answer.
    /// </exception>
    public RequestPage PageOf(IEnumerable<StoredRequest> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var matched = candidates.Where(stored => Matches(stored.Request)).ToArray();

        var page = Ordered(matched)
            .Skip(Skip)
            .Take(Take)
            .ToArray();

        return new RequestPage(page, matched.Length);
    }

    /// <summary>
    /// The matches in the order this query asked for.
    /// <para>
    /// The identifier is the last key of every order, so requests that compare equal under the
    /// chosen one still have exactly one position. Without it their order is whatever order the set
    /// is enumerated in, and a set held by a store is rebuilt on every write: a request created
    /// between two page turns can reorder rows that have nothing to do with it, so a reader turning
    /// to the next page sees one of them again and never sees another.
    /// </para>
    /// <para>
    /// Descending reverses the identifier as well as the chosen key, so the descending order is
    /// exactly the ascending one read backwards. A tiebreak left ascending under a reversed primary
    /// key is a third order, and two requests made in the same tick would sit in one order going
    /// down the queue and the other going up it.
    /// </para>
    /// <para>
    /// The title is compared as text somebody reads rather than as bytes, so an accented title
    /// sorts beside its unaccented neighbour instead of after every unaccented title there is. That
    /// is the one order here where the byte comparison and the reading one differ, and the reading
    /// one is what a person scanning a column expects.
    /// </para>
    /// </summary>
    /// <param name="matched">What survived the filter.</param>
    /// <returns>The matches, ordered.</returns>
    private IOrderedEnumerable<StoredRequest> Ordered(IEnumerable<StoredRequest> matched)
        => Order switch
        {
            RequestQueryOrder.RequestedAt => Descending
                ? matched.OrderByDescending(stored => stored.Request.RequestedAt).ThenByDescending(stored => stored.Request.Id)
                : matched.OrderBy(stored => stored.Request.RequestedAt).ThenBy(stored => stored.Request.Id),
            RequestQueryOrder.StateChangedAt => Descending
                ? matched.OrderByDescending(stored => stored.Request.StateChangedAt).ThenByDescending(stored => stored.Request.Id)
                : matched.OrderBy(stored => stored.Request.StateChangedAt).ThenBy(stored => stored.Request.Id),
            RequestQueryOrder.DisplayTitle => Descending
                ? matched.OrderByDescending(stored => stored.Request.DisplayTitle, StringComparer.InvariantCulture).ThenByDescending(stored => stored.Request.Id)
                : matched.OrderBy(stored => stored.Request.DisplayTitle, StringComparer.InvariantCulture).ThenBy(stored => stored.Request.Id),

            _ => throw new InvalidOperationException(FormattableString.Invariant(
                $"There is no comparison for the order {Order}, so this query cannot say what its page holds."))
        };
}

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
}

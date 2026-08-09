using System.Collections.Generic;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// One page of requests, and how many the filter matched before the page was taken.
/// <para>
/// The count comes back with the page rather than from a second call, because the two have to be
/// answered from one read. A page and a count taken separately can disagree, and a surface saying
/// "1 to 50 of 49" is one an operator stops trusting for the rest of the numbers on the screen.
/// </para>
/// <para>
/// The slice the caller asked for is echoed back. A client that has to remember what it sent in
/// order to know what it got is a client that draws the wrong pager the first time two calls are in
/// flight at once, and the endpoint may return a smaller page than was asked for.
/// </para>
/// </summary>
/// <typeparam name="TRequest">
/// What one row looks like, which is not the same shape for a person reading their own requests and
/// for an administrator reading the queue.
/// </typeparam>
public sealed record RequestsPage<TRequest>
{
    /// <summary>
    /// Gets the rows on this page, in the order that was asked for. Empty where the page starts past
    /// the end of the matches, which is an ordinary answer rather than an error.
    /// </summary>
    public required IReadOnlyList<TRequest> Requests { get; init; }

    /// <summary>
    /// Gets how many requests matched, whatever this page holds. This is what a pager is drawn from,
    /// and it is the count of matches rather than the length of the page.
    /// </summary>
    public required int MatchCount { get; init; }

    /// <summary>
    /// Gets how many matches were stepped over before this page.
    /// </summary>
    public required int Skip { get; init; }

    /// <summary>
    /// Gets how many rows were asked for at most.
    /// </summary>
    public required int Take { get; init; }
}

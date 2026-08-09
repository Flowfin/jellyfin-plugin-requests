using System.Collections.Generic;

namespace Jellyfin.Plugin.Requests.Storage;

/// <summary>
/// One page of the queue, and how many requests the filter matched before the page was taken.
/// <para>
/// The count is here rather than on a second call because the two have to be answered from one
/// snapshot. A page and a count taken from two reads can disagree, and a queue that says "showing 1
/// to 50 of 49" is a queue an operator stops trusting for the rest of the numbers on the screen.
/// </para>
/// </summary>
/// <param name="Requests">
/// The requests on this page, in the order <see cref="RequestQuery.Order"/> asked for. Empty where
/// the page starts past the end of the matches, which is an ordinary answer rather than an error:
/// a request removed between two page turns shortens the queue under the reader.
/// </param>
/// <param name="MatchCount">
/// How many requests matched the filter, whatever the page holds. This is what a pager is rendered
/// from, and it is never the length of <see cref="Requests"/> unless the page happens to hold every
/// match.
/// </param>
public readonly record struct RequestPage(IReadOnlyList<StoredRequest> Requests, int MatchCount);

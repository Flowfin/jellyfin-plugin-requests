using System.Collections.Generic;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// What an approval of several requests carries: the requests, each with the revision it was read
/// at.
/// <para>
/// A separate shape from <see cref="ApproveRequestBody"/> rather than the same one repeated, for the
/// reason there are two endpoints at all: what a caller sends is what it may send, and a body that
/// meant one request or many depending on which field was filled in is a body whose rule is read in
/// prose.
/// </para>
/// </summary>
public sealed record ApproveManyBody
{
    /// <summary>
    /// Gets the requests being approved, in the order the caller chose them.
    /// <para>
    /// Nullable so that a body with no list at all is refused as a body that is wrong rather than
    /// read as an action on nothing, which would answer that it decided everything it was asked to.
    /// </para>
    /// </summary>
    public IReadOnlyList<RequestToDecide>? Requests { get; init; }
}

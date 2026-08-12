using System;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// What one request in an action on several came to: the row as the queue now holds it, or the
/// refusal that request got.
/// <para>
/// Exactly one of the two is present, and which one is what a caller reads to tell a request that
/// moved from one that did not. The identifier is on the entry either way, so a client matches the
/// answer to what it sent by name rather than by counting positions.
/// </para>
/// <para>
/// <b>There is no status code on an entry.</b> A status code is an answer to a call, and this is not
/// a call: several of these come back under one status. The failure carries the code a client
/// branches on, which <c>docs/api.md</c> pairs with a status once and in one place, and putting that
/// status here too would be a second copy of a pairing whose whole value is that there is one.
/// </para>
/// </summary>
public sealed record DecidedRequest
{
    /// <summary>
    /// Gets the request this entry is about, as the caller named it.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the request at its new revision, where the move was made. Absent where it was refused.
    /// </summary>
    public QueuedRequest? Request { get; init; }

    /// <summary>
    /// Gets why this one was refused, in the shape every failure of this API comes back in. Absent
    /// where the move was made.
    /// </summary>
    public RequestFailure? Failure { get; init; }
}

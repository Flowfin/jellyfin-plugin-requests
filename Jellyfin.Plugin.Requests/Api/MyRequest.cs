using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// One request as the person waiting for it sees it.
/// <para>
/// <b>It carries no identifier of any person, including the caller's own.</b> That is the whole
/// reason this type exists rather than the stored request being returned. A user's own list holds
/// requests they asked for and requests they joined, and a joined one was asked for by somebody
/// else: handing back the stored record would tell the caller who that was, and who else is waiting
/// alongside them. Nothing here says how many people are waiting either, because a count is the
/// same disclosure made smaller.
/// </para>
/// <para>
/// The history is left out for the same reason one step further. Every entry names the
/// administrator who made the move, and a queue a user can read is not an audit trail.
/// </para>
/// <para>
/// What a user may learn about other people's requests at all is #51, and #71 asks the question from
/// the surface side. Nothing here aggregates across people, so this endpoint takes no position on
/// either beyond refusing to be the place it leaks.
/// </para>
/// </summary>
public sealed record MyRequest
{
    /// <summary>
    /// Gets the request's identifier, which is the only identifier in this shape and names a
    /// request rather than a person.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets what sort of thing was asked for.
    /// </summary>
    public required RequestedItemKind Kind { get; init; }

    /// <summary>
    /// Gets the title as it read when it was asked for.
    /// </summary>
    public required string DisplayTitle { get; init; }

    /// <summary>
    /// Gets the release year, where the ask carried one.
    /// </summary>
    public int? DisplayYear { get; init; }

    /// <summary>
    /// Gets the seasons asked for. Empty means the whole series, and on a film it is always empty.
    /// </summary>
    public IReadOnlyList<int> Seasons { get; init; } = [];

    /// <summary>
    /// Gets where the request stands.
    /// </summary>
    public required RequestState State { get; init; }

    /// <summary>
    /// Gets when it was asked for. On a request the caller joined this is when the first person
    /// asked, which is what the caller is waiting behind.
    /// </summary>
    public required DateTimeOffset RequestedAt { get; init; }

    /// <summary>
    /// Gets when it last moved.
    /// </summary>
    public required DateTimeOffset StateChangedAt { get; init; }

    /// <summary>
    /// Gets a value indicating whether the caller is the person who asked first, as opposed to
    /// somebody who joined a request that already existed. It is a fact about the caller and
    /// discloses nobody else.
    /// </summary>
    public required bool AskedByYou { get; init; }

    /// <summary>
    /// Gets the note the caller wrote when they asked, and nothing where they joined instead. A
    /// note on a joined request is another person's writing, and this shape does not carry it.
    /// </summary>
    public string? YourNote { get; init; }

    /// <summary>
    /// Gets why it was declined, where it was. A decline reason is required, which I decided on
    /// #113, and a requester who is not told why asks the same thing again.
    /// </summary>
    public DeclineReason? DeclineReason { get; init; }

    /// <summary>
    /// Gets what the operator wrote alongside that reason, where they wrote anything. It is written
    /// for the requester to read, which is why it is here and the history is not.
    /// </summary>
    public string? DeclineNote { get; init; }

    /// <summary>
    /// Gets how much of what was asked for the library already holds.
    /// </summary>
    public required LibraryAvailability Availability { get; init; }
}

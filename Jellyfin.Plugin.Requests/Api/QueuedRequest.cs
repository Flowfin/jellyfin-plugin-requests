using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// One request as an administrator reading the queue sees it: everything a decision is made from,
/// and the revision the store has it at.
/// <para>
/// The revision is here because the next thing an operator does is move the request, and the store
/// refuses a write made against a revision that has moved underneath it. A row without one would
/// force a second read before every decision, and the answer to that read is what the queue already
/// held.
/// </para>
/// <para>
/// Who asked and who joined are here, and this is the only shape that carries them. Reading the
/// whole queue is what the elevation on the endpoint is for. What a queue must show for a decision
/// to be possible at all is #59, so this is what the queue can show rather than a settled answer to
/// what it should.
/// </para>
/// <para>
/// The history is left out. It is the record of every move a request made, it grows without bound,
/// and a page of the queue is not where it is read. Whichever surface needs it should read one
/// request rather than have every row carry one.
/// </para>
/// </summary>
public sealed record QueuedRequest
{
    /// <summary>
    /// Gets the request's identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets what the store has this request at. It is handed back with a write, which is how two
    /// operators deciding one request at the same moment end with one decision rather than a silent
    /// overwrite.
    /// </summary>
    public required long Revision { get; init; }

    /// <summary>
    /// Gets the person who asked first.
    /// </summary>
    public required Guid RequestedByUserId { get; init; }

    /// <summary>
    /// Gets everyone who asked for this after it existed, oldest first.
    /// </summary>
    public IReadOnlyList<Guid> JoinedByUserIds { get; init; } = [];

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
    /// Gets the external identifiers that name the thing.
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderIds { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the seasons asked for. Empty means the whole series.
    /// </summary>
    public IReadOnlyList<int> Seasons { get; init; } = [];

    /// <summary>
    /// Gets where the request stands.
    /// </summary>
    public required RequestState State { get; init; }

    /// <summary>
    /// Gets when it was asked for.
    /// </summary>
    public required DateTimeOffset RequestedAt { get; init; }

    /// <summary>
    /// Gets when it last moved.
    /// </summary>
    public required DateTimeOffset StateChangedAt { get; init; }

    /// <summary>
    /// Gets who moved it last, where anybody has.
    /// </summary>
    public Guid? StateChangedByUserId { get; init; }

    /// <summary>
    /// Gets the note the person who asked wrote.
    /// </summary>
    public string? RequesterNote { get; init; }

    /// <summary>
    /// Gets why it was declined, where it was.
    /// </summary>
    public DeclineReason? DeclineReason { get; init; }

    /// <summary>
    /// Gets what was written alongside that reason.
    /// </summary>
    public string? DeclineNote { get; init; }

    /// <summary>
    /// Gets how much of what was asked for the library already holds.
    /// </summary>
    public required LibraryAvailability Availability { get; init; }

    /// <summary>
    /// Gets when that was last worked out, where it has been.
    /// </summary>
    public DateTimeOffset? AvailabilityCheckedAt { get; init; }
}

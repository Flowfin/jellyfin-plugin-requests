using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// A decision somebody already made about the same work, as the queue shows it beside the request
/// being decided now.
/// <para>
/// It carries the answer and not the asker. Who asked for the earlier one is on that request, the
/// operator can read it there, and repeating it on every row that mentions it would put a person's
/// name beside a title they did not ask for this time. What a decision is worth here is the answer
/// and the reason for it.
/// </para>
/// <para>
/// The seasons are here because a series decision is not one answer. Declining seasons one and two
/// says nothing certain about season five, and a row that showed the decision without them would
/// invite an operator to read it as covering the whole show.
/// </para>
/// </summary>
public sealed record EarlierDecision
{
    /// <summary>
    /// Gets the request the decision was made on, so an operator can find it in the queue rather
    /// than take this row's word for it.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets what it was decided to.
    /// </summary>
    public required RequestState State { get; init; }

    /// <summary>
    /// Gets when that decision was made, which is when the request last moved.
    /// </summary>
    public required DateTimeOffset DecidedAt { get; init; }

    /// <summary>
    /// Gets the seasons that decision covered. Empty means the whole series, and it is empty for
    /// every film.
    /// </summary>
    public IReadOnlyList<int> Seasons { get; init; } = [];

    /// <summary>
    /// Gets why it was declined, where it was. Absent for a decision that was not a decline.
    /// </summary>
    public DeclineReason? DeclineReason { get; init; }

    /// <summary>
    /// Gets what the operator wrote alongside that reason.
    /// </summary>
    public string? DeclineNote { get; init; }
}

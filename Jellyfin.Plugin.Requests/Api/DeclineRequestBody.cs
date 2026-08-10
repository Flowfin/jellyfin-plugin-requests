using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// What a decline carries: the revision the operator was looking at, why the answer is no, and
/// whatever they want to say about it.
/// <para>
/// The reason is a field on this body and not on the approval, which is the reason there are two
/// endpoints rather than one taking a target state. A single endpoint would carry a reason that is
/// required for one value of another field, which is a rule a caller reads in prose; two shapes is
/// a rule the caller meets at the door.
/// </para>
/// </summary>
public sealed record DeclineRequestBody
{
    /// <summary>
    /// Gets the revision the caller read the request at, from <see cref="QueuedRequest.Revision"/>.
    /// Nullable for the same reason as on an approval: a body that left it out is refused rather
    /// than read as a revision nobody sent.
    /// </summary>
    public long? Revision { get; init; }

    /// <summary>
    /// Gets why the request is being declined. Required, decided on #113: a decline with no reason
    /// reads as arbitrary to the person who asked, and what they do next is ask for the same title
    /// again, because nothing told them what was wrong with the first attempt.
    /// </summary>
    public DeclineReason? Reason { get; init; }

    /// <summary>
    /// Gets what the operator wants to say about it. Required beside
    /// <see cref="DeclineReason.Other"/>, which says nothing on its own, and optional beside every
    /// other reason.
    /// </summary>
    public string? Note { get; init; }
}

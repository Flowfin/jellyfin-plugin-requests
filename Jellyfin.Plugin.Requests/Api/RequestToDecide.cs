using System;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// One request inside an action on several: which request, and the revision the operator was
/// looking at when they chose it.
/// <para>
/// The revision is per request rather than per action, because the rows on an operator's screen were
/// read at whatever revision each one was at and one of them can move while the others do not. An
/// action carrying a single revision would either refuse everything because one row is stale, or
/// carry no revision at all, which is the silent overwrite the single decision refuses.
/// </para>
/// </summary>
public sealed record RequestToDecide
{
    /// <summary>
    /// Gets the request being decided.
    /// <para>
    /// Nullable so that an entry which left it out is refused rather than read as the empty
    /// identifier, which is a value no request has and which would come back as one that does not
    /// exist rather than as a body that is wrong.
    /// </para>
    /// </summary>
    public Guid? Id { get; init; }

    /// <summary>
    /// Gets the revision the caller read that request at, from <see cref="QueuedRequest.Revision"/>.
    /// Nullable for the reason it is on the single decision: a body that left it out is refused
    /// rather than read as a revision nobody sent.
    /// </summary>
    public long? Revision { get; init; }
}

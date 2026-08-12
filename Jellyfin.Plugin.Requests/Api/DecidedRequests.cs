using System.Collections.Generic;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// What an action on several requests did, one entry per request, in the order the caller sent them.
/// <para>
/// <b>The answer is per request because the outcome is.</b> Some of an action succeeds and some of
/// it is refused, and a single verdict over the whole thing would have to be either "done", which is
/// false the moment one row moved underneath the operator, or "failed", which throws away the ones
/// that were decided and cannot be undecided. A surface that reports this as done when part of it
/// was refused is the failure this shape exists against.
/// </para>
/// <para>
/// There is no count of what succeeded and none of what failed. Both are read off the entries, and a
/// number beside a list it is derived from is a number that disagrees with the list the first time
/// one of them is built wrong.
/// </para>
/// </summary>
public sealed record DecidedRequests
{
    /// <summary>
    /// Gets one entry per request the action carried, in the order it carried them. Every request
    /// the caller sent has an entry: there is no entry missing for a request nothing happened to,
    /// because "nothing happened to it" is something a caller has to be told rather than left to
    /// infer from an absence.
    /// </summary>
    public required IReadOnlyList<DecidedRequest> Requests { get; init; }
}

using System.Collections.Generic;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// The words a page draws, in the culture it asked for.
/// </summary>
public sealed class PageStrings
{
    /// <summary>
    /// Gets the culture the strings were resolved for, as the caller named it.
    /// <para>
    /// It is answered back rather than assumed, because a caller that asked for one culture and got
    /// English by fallback has no other way to tell. Nothing on a page depends on it today; it is
    /// what makes a half-translated install visible to whoever is looking at it.
    /// </para>
    /// </summary>
    public required string Culture { get; init; }

    /// <summary>
    /// Gets every string, keyed the way the pages name them.
    /// <para>
    /// The set is complete whatever the culture's own catalogue holds, because English is merged
    /// underneath it before it is answered. A page therefore never has to know that a fallback rule
    /// exists.
    /// </para>
    /// </summary>
    public required IReadOnlyDictionary<string, string> Strings { get; init; }
}

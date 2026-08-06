using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Requests;

/// <summary>
/// Deliberate use of an API that does not exist on the floor the packaging metadata claims.
/// This file exists to make the floor build go red and is thrown away afterwards.
/// </summary>
internal static class FloorProof
{
    /// <summary>
    /// Sets a property added to <see cref="InternalItemsQuery"/> after 10.11.0.
    /// </summary>
    /// <returns>The query.</returns>
    public static InternalItemsQuery Build()
    {
        return new InternalItemsQuery { UseRawName = true };
    }
}

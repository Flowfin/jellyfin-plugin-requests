using System;

namespace Jellyfin.Plugin.Requests.Seam;

/// <summary>
/// Whether the server has a user, asked of the server rather than assumed.
/// <para>
/// One question and no more. A handover names a user this plugin cannot verify, and what is owed
/// against that is not a permission check, which this side is in no position to make; it is that the
/// identifier names somebody. A seam that could answer more would be a seam somebody later uses to
/// answer more.
/// </para>
/// <para>
/// It is an interface for the reason the clock and the install settings are: the real one reaches
/// into the server, and a test that had to build a server to run is a test nobody runs.
/// </para>
/// </summary>
public interface IKnownUsers
{
    /// <summary>
    /// Whether this server has that user.
    /// </summary>
    /// <param name="userId">The identifier a caller handed over.</param>
    /// <returns><see langword="true"/> where the server knows somebody by it.</returns>
    bool Has(Guid userId);
}

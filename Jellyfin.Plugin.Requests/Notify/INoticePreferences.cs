using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Requests.Notify;

/// <summary>
/// Whether one person wants to be told when their own request moves, kept per person.
/// <para>
/// <b>It is the person's and not the operator's.</b> The three settings on the plugin configuration
/// narrow what leaves the server on the outbound sink; none of them reaches the message pushed to
/// whoever asked, and none of them may, because an administrator able to silence what somebody else
/// is told is a different thing from an administrator deciding what their server sends outward.
/// <c>docs/notifications.md</c> carries who owns which switch.
/// </para>
/// <para>
/// <b>The default is on and it is the absence of a value rather than a value.</b> An install nobody
/// has touched holds nothing here at all, so it behaves exactly as it did before this existed, and
/// what is kept is the list of people who said no rather than a row per person on the server.
/// </para>
/// <para>
/// It is a seam for the same reason the clock and the store are: what keeps the answer is a file in
/// the plugin's data directory, and a test of the path that reads it should not need one.
/// </para>
/// </summary>
public interface INoticePreferences
{
    /// <summary>
    /// Whether this person is to be told about their own requests.
    /// </summary>
    /// <param name="userId">Whose setting to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true"/> where they are, which is what a person who has never touched it gets.</returns>
    /// <exception cref="NoticePreferencesException">Where what is kept cannot be read.</exception>
    Task<bool> TellsThemAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets whether this person is to be told, and keeps it across a restart.
    /// </summary>
    /// <param name="userId">Whose setting to write. There is one identifier here and it is always the caller's own.</param>
    /// <param name="tellsThem">Whether to tell them.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the setting is after the write, which is what a caller shows back to the person.</returns>
    /// <exception cref="NoticePreferencesException">Where what is kept cannot be read or cannot be written.</exception>
    Task<bool> SetAsync(Guid userId, bool tellsThem, CancellationToken cancellationToken);
}

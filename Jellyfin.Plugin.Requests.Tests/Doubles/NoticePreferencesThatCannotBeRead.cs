using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Notify;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// What is kept about who wants to be told, on a disk that will not answer.
/// <para>
/// It refuses both calls rather than only the read, because a write reads before it decides and a
/// double that let the write through would let a test pass that the shipped code fails.
/// </para>
/// </summary>
internal sealed class NoticePreferencesThatCannotBeRead : INoticePreferences
{
    /// <inheritdoc />
    public Task<bool> TellsThemAsync(Guid userId, CancellationToken cancellationToken)
        => throw new NoticePreferencesException();

    /// <inheritdoc />
    public Task<bool> SetAsync(Guid userId, bool tellsThem, CancellationToken cancellationToken)
        => throw new NoticePreferencesException();
}

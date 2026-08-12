using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// A store that takes every call and answers none of them, ever.
/// <para>
/// This is the case a cancellation token cannot reach and the one worth having a double for. A store
/// that throws is refused by name; a store that is slow finishes eventually; a store holding a lock
/// nothing will release, or sitting on a disk that has stopped answering, leaves a task that never
/// completes however politely it is asked to stop. A caller that only awaits it waits for as long as
/// the process lives.
/// </para>
/// <para>
/// It never completes rather than waiting a measured amount, so no test built on it spends real
/// time or depends on how busy the machine is. What is asserted against it is that a caller answered
/// at all.
/// </para>
/// </summary>
internal sealed class StoreThatNeverAnswers : IRequestStore
{
    /// <inheritdoc />
    public Task<StoredRequest?> GetAsync(Guid id, CancellationToken cancellationToken) => Never<StoredRequest?>();

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredRequest>> GetAllAsync(CancellationToken cancellationToken)
        => Never<IReadOnlyList<StoredRequest>>();

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredRequest>> FindForUserAsync(Guid userId, CancellationToken cancellationToken)
        => Never<IReadOnlyList<StoredRequest>>();

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredRequest>> FindByProviderIdentifierAsync(
        RequestedItemKind kind,
        string provider,
        string value,
        CancellationToken cancellationToken)
        => Never<IReadOnlyList<StoredRequest>>();

    /// <inheritdoc />
    public Task<StoredRequest?> FindByWantAsync(Guid wantId, CancellationToken cancellationToken)
        => Never<StoredRequest?>();

    /// <inheritdoc />
    public Task<RequestPage> PageAsync(RequestQuery query, CancellationToken cancellationToken) => Never<RequestPage>();

    /// <inheritdoc />
    public Task<StoredRequest> AddAsync(MediaRequest request, CancellationToken cancellationToken)
        => Never<StoredRequest>();

    /// <inheritdoc />
    public Task<StoredRequest> ReplaceAsync(
        MediaRequest request,
        long expectedRevision,
        CancellationToken cancellationToken)
        => Never<StoredRequest>();

    /// <inheritdoc />
    public Task<bool> RemoveAsync(Guid id, long expectedRevision, CancellationToken cancellationToken)
        => Never<bool>();

    private static Task<T> Never<T>() => new TaskCompletionSource<T>().Task;
}

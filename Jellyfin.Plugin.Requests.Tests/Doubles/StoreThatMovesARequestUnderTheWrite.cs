using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// A store that lets somebody else move a request in the window between a caller reading it and the
/// same caller writing it back.
/// <para>
/// That window is what the revision on a write exists for, and it cannot be reached from outside:
/// a test can move a request before the call or after it, and either one is checked before the
/// write is attempted. This moves it during, once, on the first write it is asked to make, which is
/// the only way to watch the store refuse a write the caller had every reason to believe was safe.
/// </para>
/// <para>
/// It moves the request by writing it at the revision it currently holds, through the same
/// interface every other caller uses, so what the caller under test then meets is the store's own
/// refusal and not one this double invented.
/// </para>
/// </summary>
internal sealed class StoreThatMovesARequestUnderTheWrite : IRequestStore
{
    private readonly IRequestStore _inner;
    private readonly Func<MediaRequest, MediaRequest> _move;
    private bool _moved;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreThatMovesARequestUnderTheWrite"/> class.
    /// </summary>
    /// <param name="inner">The store that actually holds the requests.</param>
    /// <param name="move">
    /// What the other caller does to the request, applied once, immediately before the first write
    /// this store is asked to make.
    /// </param>
    public StoreThatMovesARequestUnderTheWrite(IRequestStore inner, Func<MediaRequest, MediaRequest> move)
    {
        _inner = inner;
        _move = move;
    }

    /// <inheritdoc />
    /// <remarks>Not what this double is about, so it answers as a store that has written nothing.</remarks>
    public DateTimeOffset? LastWrittenAt => null;

    /// <inheritdoc />
    public Task<StoredRequest?> GetAsync(Guid id, CancellationToken cancellationToken)
        => _inner.GetAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredRequest>> GetAllAsync(CancellationToken cancellationToken)
        => _inner.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredRequest>> FindForUserAsync(Guid userId, CancellationToken cancellationToken)
        => _inner.FindForUserAsync(userId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredRequest>> FindByProviderIdentifierAsync(
        RequestedItemKind kind,
        string provider,
        string value,
        CancellationToken cancellationToken)
        => _inner.FindByProviderIdentifierAsync(kind, provider, value, cancellationToken);

    /// <inheritdoc />
    public Task<StoredRequest?> FindByWantAsync(Guid wantId, CancellationToken cancellationToken)
        => _inner.FindByWantAsync(wantId, cancellationToken);

    /// <inheritdoc />
    public Task<RequestPage> PageAsync(RequestQuery query, CancellationToken cancellationToken)
        => _inner.PageAsync(query, cancellationToken);

    /// <inheritdoc />
    public Task<StoredRequest> AddAsync(MediaRequest request, CancellationToken cancellationToken)
        => _inner.AddAsync(request, cancellationToken);

    /// <inheritdoc />
    public async Task<StoredRequest> ReplaceAsync(
        MediaRequest request,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_moved)
        {
            _moved = true;

            var held = await _inner.GetAsync(request.Id, cancellationToken).ConfigureAwait(false);

            if (held is StoredRequest current)
            {
                await _inner
                    .ReplaceAsync(_move(current.Request), current.Revision, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return await _inner.ReplaceAsync(request, expectedRevision, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(Guid id, long expectedRevision, CancellationToken cancellationToken)
        => _inner.RemoveAsync(id, expectedRevision, cancellationToken);
}

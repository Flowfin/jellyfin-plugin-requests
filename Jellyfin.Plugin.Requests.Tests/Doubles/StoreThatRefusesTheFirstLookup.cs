using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// A store that refuses the first lookup by identifier and then behaves. It stands for the one
/// library item whose handling goes wrong, so a test can watch what happens to the ones behind it
/// rather than to it.
/// <para>
/// Refusing every lookup would prove the loop reported a failure and prove nothing about whether it
/// carried on, which is the property that matters when a scan raises thousands of these.
/// </para>
/// </summary>
internal sealed class StoreThatRefusesTheFirstLookup : IRequestStore
{
    private readonly IRequestStore _inner;
    private bool _refused;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreThatRefusesTheFirstLookup"/> class.
    /// </summary>
    /// <param name="inner">The store that actually holds the requests.</param>
    public StoreThatRefusesTheFirstLookup(IRequestStore inner)
    {
        _inner = inner;
    }

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
    {
        if (!_refused)
        {
            _refused = true;

            throw new InvalidOperationException("The store could not be read for this lookup.");
        }

        return _inner.FindByProviderIdentifierAsync(kind, provider, value, cancellationToken);
    }

    /// <inheritdoc />
    public Task<RequestPage> PageAsync(RequestQuery query, CancellationToken cancellationToken)
        => _inner.PageAsync(query, cancellationToken);

    /// <inheritdoc />
    public Task<StoredRequest> AddAsync(MediaRequest request, CancellationToken cancellationToken)
        => _inner.AddAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<StoredRequest> ReplaceAsync(
        MediaRequest request,
        long expectedRevision,
        CancellationToken cancellationToken)
        => _inner.ReplaceAsync(request, expectedRevision, cancellationToken);

    /// <inheritdoc />
    public Task<bool> RemoveAsync(Guid id, long expectedRevision, CancellationToken cancellationToken)
        => _inner.RemoveAsync(id, expectedRevision, cancellationToken);
}

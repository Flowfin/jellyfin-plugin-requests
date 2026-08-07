using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// A store that keeps everything in a dictionary and nothing on a disk. It exists so the contract
/// in <see cref="IRequestStore"/> has something to be proven against before a durable store is
/// built, and so the conformance suite that proves it is a suite rather than a set of assertions
/// about one implementation.
/// <para>
/// It is deliberately in the test project. A store that loses every request when the server
/// restarts is not a thing this plugin should be able to be configured with by accident, and which
/// medium the shipped store uses is not settled here.
/// </para>
/// <para>
/// The compare and the write are under one lock, which is what makes the conflict rule hold at all:
/// with the read and the write apart, two callers both find the revision they expected and both
/// write, which is the last-writer-wins behaviour the contract refuses.
/// </para>
/// </summary>
internal sealed class InMemoryRequestStore : IRequestStore
{
    private readonly Dictionary<Guid, StoredRequest> _held = [];
    private readonly object _gate = new object();

    /// <inheritdoc />
    public Task<StoredRequest?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(_held.TryGetValue(id, out var stored) ? stored : (StoredRequest?)null);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredRequest>> GetAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<StoredRequest>>(_held.Values.ToArray());
        }
    }

    /// <inheritdoc />
    public Task<StoredRequest> AddAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_held.ContainsKey(request.Id))
            {
                throw new DuplicateRequestException(request.Id);
            }

            var stored = new StoredRequest(request, 1);
            _held.Add(request.Id, stored);
            return Task.FromResult(stored);
        }
    }

    /// <inheritdoc />
    public Task<StoredRequest> ReplaceAsync(MediaRequest request, long expectedRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var current = _held.TryGetValue(request.Id, out var held) ? held : (StoredRequest?)null;

            if (current is null || current.Value.Revision != expectedRevision)
            {
                throw new RequestConcurrencyException(request.Id, expectedRevision, current);
            }

            var written = new StoredRequest(request, expectedRevision + 1);
            _held[request.Id] = written;
            return Task.FromResult(written);
        }
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(Guid id, long expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_held.TryGetValue(id, out var held))
            {
                return Task.FromResult(false);
            }

            if (held.Revision != expectedRevision)
            {
                throw new RequestConcurrencyException(id, expectedRevision, held);
            }

            _held.Remove(id);
            return Task.FromResult(true);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Storage;

/// <summary>
/// Where requests are kept, and the rules every implementation has to keep.
/// <para>
/// The rules are here rather than in a document because the callers are concurrent by
/// construction. Two clients can create a request in the same moment, and a sweep that looks at the
/// library can be walking the store while an operator approves something. A contract left implicit
/// is one that holds on a machine with one user and stops holding on the machine nobody tested on.
/// </para>
/// <para>
/// <b>What is atomic.</b> Each of <see cref="AddAsync"/>, <see cref="ReplaceAsync"/> and
/// <see cref="RemoveAsync"/> either happens completely or not at all, for the one request it names.
/// Nothing here is atomic across two requests: there is no operation that moves two requests
/// together and no caller may assume one. Whether the write has reached a disk by the time the call
/// returns is a separate promise and it is not made here.
/// </para>
/// <para>
/// <b>What a reader may see.</b> A read returns a snapshot. Every request it returns is a value
/// some write completed, never a mixture of two, and never a half-written one.
/// <see cref="GetAllAsync"/> is a snapshot of each request rather than of the store, so a caller
/// that reads the whole store while writes are running may see request A as of one moment and
/// request B as of another. Reading never blocks a writer and never blocks another reader. A
/// snapshot is stale the moment it is taken, which is exactly why a write carries the revision it
/// was read at.
/// </para>
/// <para>
/// <b>Two callers moving the same request.</b> Not last writer wins. Every write names the revision
/// the caller read, the store accepts it only if that is still the revision it holds, and a write
/// that does not match is refused with <see cref="RequestConcurrencyException"/> carrying what the
/// store holds now. Where several callers write against one revision, exactly one is accepted and
/// every other is refused. The refused caller has lost nothing it cannot recover: it re-reads,
/// decides again against what it now sees, and writes again.
/// </para>
/// <para>
/// <b>Ordering.</b> Revisions on one request are a total order and go up by one per accepted write.
/// Across two requests there is no order: nothing here says which of two writes to two different
/// requests happened first, and no caller may infer one from a revision.
/// </para>
/// <para>
/// <b>Cancellation.</b> A cancelled call throws <see cref="OperationCanceledException"/>. Whether
/// the write it was cancelled in the middle of took effect is not defined, so a caller that
/// cancels a write and needs to know reads again.
/// </para>
/// <para>
/// Which medium this is kept in, and what happens to a write that is interrupted, are not decided
/// here. This interface is what those answers have to fit through, and it says nothing about files,
/// databases or serialisation on purpose.
/// </para>
/// </summary>
public interface IRequestStore
{
    /// <summary>
    /// Reads one request.
    /// </summary>
    /// <param name="id">The request's own identifier.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The request and the revision the store holds it at, or <see langword="null"/> where the
    /// store holds no request with that identifier. Absent is an ordinary answer and never an
    /// exception, because a caller asking about a request that was removed while they held its
    /// identifier is the normal case rather than a defect.
    /// </returns>
    Task<StoredRequest?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Reads every request the store holds.
    /// <para>
    /// This is the whole-store read the contract above is stated over, and it is not the query
    /// surface the administrator and user pages need. Filtering, sorting and paging are their own
    /// work and are not on this interface yet.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every request, each with the revision the store holds it at, in no defined order.</returns>
    Task<IReadOnlyList<StoredRequest>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Puts a request the store does not already hold into it.
    /// </summary>
    /// <param name="request">The request to keep.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The request at revision 1.</returns>
    /// <exception cref="DuplicateRequestException">
    /// The store already holds a request with that identifier. Where several callers add the same
    /// identifier at once, exactly one is accepted and every other is refused this way.
    /// </exception>
    Task<StoredRequest> AddAsync(MediaRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a request the store already holds, against the revision the caller read.
    /// </summary>
    /// <param name="request">
    /// The request as it should now read. Its identifier says which stored request is being
    /// written, and it may not be changed by a write.
    /// </param>
    /// <param name="expectedRevision">The revision the caller read this request at.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The request at its new revision, which is one higher than the one it was written against.</returns>
    /// <exception cref="RequestConcurrencyException">
    /// The store holds a different revision, or holds this request no longer. The exception carries
    /// what the store holds now, so the caller can show it or decide again against it.
    /// </exception>
    Task<StoredRequest> ReplaceAsync(MediaRequest request, long expectedRevision, CancellationToken cancellationToken);

    /// <summary>
    /// Takes a request out of the store, against the revision the caller read.
    /// </summary>
    /// <param name="id">The request's own identifier.</param>
    /// <param name="expectedRevision">The revision the caller read this request at.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// <see langword="true"/> where a request was taken out, and <see langword="false"/> where the
    /// store held none with that identifier. Removing something already gone is not an error,
    /// because a retention sweep and a person deleting the same request is a race with no wrong
    /// outcome.
    /// </returns>
    /// <exception cref="RequestConcurrencyException">
    /// The store holds this request at a different revision, so the caller is deciding from a read
    /// that has since been overtaken.
    /// </exception>
    Task<bool> RemoveAsync(Guid id, long expectedRevision, CancellationToken cancellationToken);
}

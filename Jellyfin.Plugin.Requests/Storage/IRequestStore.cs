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
/// was read at. <see cref="PageAsync"/> is the one read that promises more: its page and its count
/// come from one snapshot, because the two are read side by side on a screen and a disagreement
/// between them is visible to the person reading it.
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
    /// Gets when this store last accepted a write, or <see langword="null"/> where it has accepted
    /// none.
    /// <para>
    /// It is here because an operator whose requests have stopped moving asks whether anything is
    /// working at all, and the answer nobody can give them from outside is whether this plugin has
    /// written anything. A store that has never been written to on a server that has had requests
    /// for a week is a different fault from a queue nobody has decided on.
    /// </para>
    /// <para>
    /// <b>It is a fact about this process and not about the data.</b> Nothing persists it, so a
    /// restart answers <see langword="null"/> until the next write, and an implementation that
    /// keeps nothing at all may answer <see langword="null"/> always. That is why what reads it says
    /// "not since this server started" rather than "never".
    /// </para>
    /// </summary>
    DateTimeOffset? LastWrittenAt { get; }

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
    /// This is the whole-store read the contract above is stated over, and it is not what a surface
    /// should call. The three reads the surfaces actually make are below, and each is named because
    /// a store built for three questions is a different store from one that answers all three by
    /// walking everything. What each costs is in <c>docs/storage.md</c>.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every request, each with the revision the store holds it at, in no defined order.</returns>
    Task<IReadOnlyList<StoredRequest>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Every request one person is waiting for, whether they asked first or joined an existing one.
    /// <para>
    /// The two lists are one question. A store answering only <see cref="MediaRequest.RequestedByUserId"/>
    /// would show a person nothing for the request they joined, which is the request they are most
    /// likely to be looking for.
    /// </para>
    /// </summary>
    /// <param name="userId">The Jellyfin user being asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// Their requests, each at the revision the store holds it at, in no defined order. Empty where
    /// they have asked for nothing, which is an answer rather than an error.
    /// </returns>
    Task<IReadOnlyList<StoredRequest>> FindForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Every request naming one external identifier, which is the question a fulfilment sweep asks
    /// once per library item.
    /// <para>
    /// It answers identity and never policy. Which states a match may move, and whether a series
    /// with some of its seasons counts, are the model's answers in <see cref="RequestIdentity"/> and
    /// <see cref="LibraryAvailability"/>; a store deciding either would put half of the fulfilment
    /// rule somewhere nobody looks for it. What comes back is at most a handful of requests, so the
    /// caller filtering them costs nothing.
    /// </para>
    /// <para>
    /// The kind is part of the question because a film and a series can carry the same number under
    /// the same provider and be two different works, which is the rule <see cref="RequestIdentity"/>
    /// is written to. Provider names match without case and values match exactly, for the reasons
    /// stated there.
    /// </para>
    /// </summary>
    /// <param name="kind">What sort of thing the identifier names.</param>
    /// <param name="provider">The provider's name, matched without case.</param>
    /// <param name="value">The identifier under that provider, matched exactly.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The requests carrying that identifier, each at the revision the store holds it at, in no
    /// defined order. Empty where nothing has been asked for under it.
    /// </returns>
    /// <exception cref="ArgumentException">Where the provider name or the value is empty.</exception>
    Task<IReadOnlyList<StoredRequest>> FindByProviderIdentifierAsync(
        RequestedItemKind kind,
        string provider,
        string value,
        CancellationToken cancellationToken);

    /// <summary>
    /// The request that already absorbed one want from the sibling discover plugin, if any.
    /// <para>
    /// This is the question the seam asks before it does anything else, and it is a different
    /// question from the one above. The identifier lookup asks whether anything names the same
    /// title; this asks whether this exact want has already been taken. The other side derives its
    /// identifier from the title and the user and hands it over again after a refresh, a restart, or
    /// a gesture undone and redone, so an answer here is what stops each of those becoming another
    /// acquisition.
    /// </para>
    /// <para>
    /// Answered over everything held, whatever state the request is in. A want whose request was
    /// declined has still been taken, and answering only for the open ones would make a refusal the
    /// one thing that lets a repeat through.
    /// </para>
    /// </summary>
    /// <param name="wantId">The sibling's own identifier for the want.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The request carrying that want, at the revision the store holds it at, or
    /// <see langword="null"/> where no request has absorbed it. At most one request can, because a
    /// want is absorbed once.
    /// </returns>
    /// <exception cref="ArgumentException">Where the want identifier names nothing.</exception>
    Task<StoredRequest?> FindByWantAsync(Guid wantId, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the queue an operator reads, filtered, ordered and counted from a single
    /// snapshot.
    /// <para>
    /// The count comes back with the page because a pager rendered from a second read can disagree
    /// with the rows above it. What is filterable and what the order is are
    /// <see cref="RequestQuery"/>'s, and the order is total: requests equal under the chosen key are
    /// ordered by their own identifier, so walking the pages sees every match exactly once.
    /// </para>
    /// </summary>
    /// <param name="query">Which requests, in what order, and which slice of them.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page and how many requests the filter matched.</returns>
    /// <exception cref="ArgumentNullException">Where the query is missing.</exception>
    Task<RequestPage> PageAsync(RequestQuery query, CancellationToken cancellationToken);

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

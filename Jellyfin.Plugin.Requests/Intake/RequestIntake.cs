using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;

namespace Jellyfin.Plugin.Requests.Intake;

/// <summary>
/// One ask turned into a request, against everything the store already holds.
/// <para>
/// <b>Two surfaces ask, and this is the one that answers.</b> A person asks over the HTTP endpoint
/// and the sibling discover plugin hands a want across the seam, and both of them mean the same
/// thing: somebody wants this title. The identity rule that decides whether that is a new request or
/// a second person on an existing one is <see cref="RequestIdentity"/>, and it is applied here so
/// there is one place that applies it rather than one per surface. The failure this prevents is the
/// cheap one to write and the expensive one to undo: a second acquisition of a film somebody is
/// already waiting for, because the surface it arrived on did not know the other surface's rule.
/// </para>
/// <para>
/// It takes the store and nothing else. What a request says about who asked, when, and what the
/// title read as is decided by whoever built the record; this decides only whether that record joins
/// something or becomes something.
/// </para>
/// </summary>
public sealed class RequestIntake
{
    /// <summary>
    /// How many times a join is attempted before giving up. A join is a read followed by a write
    /// against the revision that was read, so two people joining one request in the same moment
    /// means one of them is refused and re-decides. Three is enough for that and small enough that
    /// a genuinely contended request fails visibly instead of spinning.
    /// </summary>
    public const int JoinAttempts = 3;

    private readonly IRequestStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestIntake"/> class.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <exception cref="ArgumentNullException">Where there is no store to ask.</exception>
    public RequestIntake(IRequestStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
    }

    /// <summary>
    /// Asks for something, joining whatever is already waiting for the same thing.
    /// <para>
    /// The seasons are narrowed as candidates are compared, so a caller asking for two seasons where
    /// one is already on an open request ends up asking for the other. That narrowing is the
    /// identity rule's, not this method's.
    /// </para>
    /// </summary>
    /// <param name="incoming">The request as the surface built it.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The request the asker is now waiting for, and what asking did.</returns>
    /// <exception cref="ArgumentNullException">Where there is no request to ask with.</exception>
    /// <exception cref="RequestStoreLoadException">Where the store cannot be read.</exception>
    /// <exception cref="RequestConcurrencyException">
    /// Where a join was refused <see cref="JoinAttempts"/> times because the request kept moving
    /// underneath the write. That is a contended request rather than a fault, and it is raised
    /// rather than retried forever so the caller answers instead of spinning.
    /// </exception>
    public async Task<IntakeResult> AskAsync(MediaRequest incoming, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        for (var attempt = 1; ; attempt++)
        {
            var ask = incoming;
            var candidates = await CandidatesAsync(ask, cancellationToken).ConfigureAwait(false);
            StoredRequest? joining = null;

            foreach (var candidate in candidates)
            {
                var match = RequestIdentity.Compare(candidate.Request, ask);

                if (match == RequestMatch.Same)
                {
                    joining = candidate;
                    break;
                }

                if (match == RequestMatch.Overlapping)
                {
                    ask = ask with
                    {
                        Seasons = RequestIdentity.SeasonsNotAlreadyAskedFor(candidate.Request, ask, [])
                    };
                }
            }

            if (joining is not StoredRequest existing)
            {
                var added = await _store.AddAsync(ask, cancellationToken).ConfigureAwait(false);

                return new IntakeResult(added, IntakeOutcome.Created);
            }

            if (existing.Request.WasAskedForBy(ask.RequestedByUserId))
            {
                return new IntakeResult(existing, IntakeOutcome.AlreadyWaiting);
            }

            var joined = existing.Request with
            {
                JoinedByUserIds = [.. existing.Request.JoinedByUserIds, ask.RequestedByUserId]
            };

            try
            {
                var written = await _store
                    .ReplaceAsync(joined, existing.Revision, cancellationToken)
                    .ConfigureAwait(false);

                return new IntakeResult(written, IntakeOutcome.Joined);
            }
            catch (RequestConcurrencyException) when (attempt < JoinAttempts)
            {
                // Somebody moved that request between the read and the write, which here is usually
                // a second person joining it in the same moment. Deciding again against what the
                // store holds now is exactly what the store contract asks a refused caller to do,
                // and the decision may come out differently: an operator who declined it meanwhile
                // makes it no longer joinable, and the next pass creates instead.
            }
        }
    }

    /// <summary>
    /// Whether a request is one a later asker can be added to. A declined or fulfilled request is
    /// not: joining it would leave somebody waiting on an answer that has already been given.
    /// </summary>
    /// <param name="request">The request being judged.</param>
    /// <returns><see langword="true"/> where somebody else can still join it.</returns>
    private static bool StillOpenToJoiners(MediaRequest request)
        => request.State is RequestState.Open or RequestState.Approved;

    /// <summary>
    /// Everything already in the queue that could be the same thing as what is being asked for,
    /// oldest first.
    /// </summary>
    /// <param name="ask">What is being asked for.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The requests worth comparing against.</returns>
    private async Task<IReadOnlyList<StoredRequest>> CandidatesAsync(
        MediaRequest ask,
        CancellationToken cancellationToken)
    {
        var found = new Dictionary<Guid, StoredRequest>();

        foreach (var identifier in ask.ProviderIds)
        {
            var carrying = await _store
                .FindByProviderIdentifierAsync(ask.Kind, identifier.Key, identifier.Value, cancellationToken)
                .ConfigureAwait(false);

            foreach (var candidate in carrying.Where(candidate => StillOpenToJoiners(candidate.Request)))
            {
                // Keyed by identifier, because one existing request can carry two of the identifiers
                // the caller sent and would otherwise be compared and joined twice.
                found[candidate.Request.Id] = candidate;
            }
        }

        // Oldest first, so the request people have been waiting on longest is the one a new asker
        // joins, and so the answer does not depend on which order the store happened to return them.
        return [.. found.Values.OrderBy(candidate => candidate.Request.RequestedAt).ThenBy(candidate => candidate.Request.Id)];
    }
}

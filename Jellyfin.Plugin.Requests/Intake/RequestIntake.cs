using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Configuration;
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
    private readonly IInstallSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestIntake"/> class.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="settings">
    /// What this install is set to, read per ask rather than kept, so an operator raising the quota
    /// applies to the next person who asks rather than to the next restart.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Where there is no store to ask, or nothing to read the settings from.
    /// </exception>
    public RequestIntake(IRequestStore store, IInstallSettings settings)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);

        _store = store;
        _settings = settings;
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
    /// <param name="caller">
    /// Who is asking and with what authority. It is here so the quota binds every surface at one
    /// place: a surface that forgot to check it cannot get past this, because there is no way to ask
    /// without saying who is asking.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The request the asker is now waiting for, and what asking did.</returns>
    /// <exception cref="ArgumentNullException">
    /// Where there is no request to ask with, or nobody is named as asking.
    /// </exception>
    /// <exception cref="RequestStoreLoadException">Where the store cannot be read.</exception>
    /// <exception cref="RequestQuotaReachedException">
    /// Where the person asking is already waiting for as many open or approved requests as this
    /// install allows.
    /// </exception>
    /// <exception cref="Configuration.InvalidConfigurationException">
    /// Where this install is set to something the plugin cannot run on, so there is no quota to
    /// judge the ask against. Nothing is written, and it is raised rather than defaulted because a
    /// number nobody chose is how a limit stops being one.
    /// </exception>
    /// <exception cref="RequestConcurrencyException">
    /// Where a join was refused <see cref="JoinAttempts"/> times because the request kept moving
    /// underneath the write. That is a contended request rather than a fault, and it is raised
    /// rather than retried forever so the caller answers instead of spinning.
    /// </exception>
    public async Task<IntakeResult> AskAsync(
        MediaRequest incoming,
        RequestCaller caller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(caller);

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
                await RefuseWhereTheyAreAtTheirQuotaAsync(ask, caller, cancellationToken)
                    .ConfigureAwait(false);

                var added = await _store.AddAsync(ask, cancellationToken).ConfigureAwait(false);

                return new IntakeResult(added, IntakeOutcome.Created);
            }

            var alreadyWaiting = existing.Request.WasAskedForBy(ask.RequestedByUserId);

            // Wants the existing request has not absorbed yet. Empty for every ask that arrived over
            // the HTTP endpoint, because a person typing a title carries none, so nothing below this
            // changes what that endpoint does.
            var unabsorbed = ask.WantIds
                .Where(wantId => !existing.Request.WantIds.Contains(wantId))
                .ToArray();

            if (alreadyWaiting && unabsorbed.Length == 0)
            {
                return new IntakeResult(existing, IntakeOutcome.AlreadyWaiting);
            }

            if (!alreadyWaiting)
            {
                // Joining is one more thing this person is waiting for, so it is bound by the quota
                // exactly as making a request is. The case above is not: somebody already on the
                // request takes no new place in the queue, and refusing them would be refusing an
                // ask that changes nothing.
                await RefuseWhereTheyAreAtTheirQuotaAsync(ask, caller, cancellationToken)
                    .ConfigureAwait(false);
            }

            // A want is written even where the person is already waiting, because the want is what a
            // repeat is recognised by and one that was never recorded is one that arrives again as
            // something new. That is the only case where this writes without adding anybody.
            var joined = existing.Request with
            {
                JoinedByUserIds = alreadyWaiting
                    ? existing.Request.JoinedByUserIds
                    : [.. existing.Request.JoinedByUserIds, ask.RequestedByUserId],
                WantIds = [.. existing.Request.WantIds, .. unabsorbed]
            };

            try
            {
                var written = await _store
                    .ReplaceAsync(joined, existing.Revision, cancellationToken)
                    .ConfigureAwait(false);

                return new IntakeResult(written, alreadyWaiting ? IntakeOutcome.AlreadyWaiting : IntakeOutcome.Joined);
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
    /// Refuses the ask where the person is already waiting for as many things as this install
    /// allows, and does nothing where they are not.
    /// <para>
    /// <b>An administrator is not subject to it, and neither is the plugin itself.</b> The first is
    /// this issue's rule: an operator answering their own server is not the person a quota exists to
    /// bound. The second is a shape rather than a case anything reaches today, because nothing makes
    /// a request on the plugin's own behalf; a limit that applied to it would be a limit on the
    /// server's own housekeeping. No surface hands an administrator in here yet, because neither the
    /// endpoint nor the seam can say whether the person calling is one, so the exemption is provable
    /// against the model and unreachable from outside it until they can.
    /// </para>
    /// <para>
    /// <b>The count is a snapshot and two asks arriving together can both pass it.</b> The store is
    /// atomic per request and says so, so nothing here can hold a person's whole set still while it
    /// counts. What that costs is one request over the limit for somebody asking twice in the same
    /// instant, and what the alternative costs is a lock across every request in the queue on the
    /// path every ask takes. The limit is a bound on a person's habit rather than a security
    /// boundary, and this is written down rather than left for somebody to find in a race.
    /// </para>
    /// </summary>
    /// <param name="ask">What is being asked for, which names the person asking.</param>
    /// <param name="caller">Who is asking and with what authority.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Nothing. It either returns or refuses.</returns>
    /// <exception cref="RequestQuotaReachedException">Where they are at their limit.</exception>
    private async Task RefuseWhereTheyAreAtTheirQuotaAsync(
        MediaRequest ask,
        RequestCaller caller,
        CancellationToken cancellationToken)
    {
        var roles = caller.RolesOn(ask);

        if (roles.HasFlag(RequestActor.Administrator) || roles.HasFlag(RequestActor.Plugin))
        {
            return;
        }

        var quota = new RequestQuota(_settings.Current.OpenRequestsPerUser);

        var theirs = await _store
            .FindForUserAsync(ask.RequestedByUserId, cancellationToken)
            .ConfigureAwait(false);

        var held = RequestQuota.CountedIn(theirs.Select(stored => stored.Request));

        if (quota.IsReachedBy(held))
        {
            throw new RequestQuotaReachedException(held, quota.Limit);
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

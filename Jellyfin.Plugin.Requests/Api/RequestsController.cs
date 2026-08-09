using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Identity;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// Asking for something. This is the endpoint everything else calls: the user surface, the sibling
/// discover plugin, and any script an operator writes.
/// <para>
/// <b>Who asked is the authenticated caller and nothing else.</b> It is read from the server's own
/// authorisation context rather than from the body, and <see cref="CreateRequestBody"/> carries no
/// field that could name a user, so filing a request as somebody else is not something this endpoint
/// declines to do, it is something it has no way to express.
/// </para>
/// <para>
/// <b>Asking for something already in the queue joins it.</b> Identity is
/// <see cref="RequestIdentity"/>'s answer and is not re-decided here: the store is asked for every
/// request naming each identifier the caller sent, and each candidate is compared. What this
/// endpoint adds is which requests are eligible to be joined at all, which is a question about state
/// rather than identity and is answered below.
/// </para>
/// <para>
/// The policy is the floor rather than the decision. Every endpoint carries an explicit policy and
/// which one each carries is #51; what is here is an authenticated user, because a request has to be
/// attributable to somebody to exist at all and an endpoint reachable without a session could not
/// name a requester.
/// </para>
/// </summary>
[Authorize(Policy = AuthenticatedUserPolicy)]
public sealed class RequestsController : RequestsControllerBase
{
    /// <summary>
    /// The server's policy for a call that has to come from a signed-in user. Named as a literal
    /// because the constant that holds it lives in the server's own web assembly, which a plugin
    /// does not reference; the string is the contract either way.
    /// </summary>
    private const string AuthenticatedUserPolicy = "DefaultAuthorization";

    /// <summary>
    /// How many times a join is attempted before giving up. A join is a read followed by a write
    /// against the revision that was read, so two people joining one request in the same moment
    /// means one of them is refused and re-decides. Three is enough for that and small enough that
    /// a genuinely contended request fails visibly instead of spinning.
    /// </summary>
    private const int JoinAttempts = 3;

    private readonly IRequestStore _store;
    private readonly IClock _clock;
    private readonly IIdentifierSource _identifiers;
    private readonly ICallerIdentity _callers;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestsController"/> class.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="clock">The injected clock, so a request's times are testable.</param>
    /// <param name="identifiers">Where a new request's identifier comes from.</param>
    /// <param name="callers">The server's answer to who is calling.</param>
    public RequestsController(
        IRequestStore store,
        IClock clock,
        IIdentifierSource identifiers,
        ICallerIdentity callers)
    {
        _store = store;
        _clock = clock;
        _identifiers = identifiers;
        _callers = callers;
    }

    /// <summary>
    /// Asks for something.
    /// </summary>
    /// <param name="body">What is wanted. It carries no requester and cannot.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The request the caller is now waiting for and what asking did. A new request answers 201;
    /// joining one, or already waiting for it, answers 200, because nothing was
    /// created. The shape of a refusal and the status code each failure gets are #56's, and what is
    /// here is the smallest thing that names the field that was wrong.
    /// </returns>
    [HttpPost("Requests")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreatedRequest>> CreateAsync(
        [FromBody] CreateRequestBody body,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return BadRequest(new RequestRefused
            {
                Field = "body",
                Reason = "There is no body on this call, and every field is in it."
            });
        }

        if (!Valid(body, out var refusal))
        {
            return BadRequest(new RequestRefused { Field = refusal.Field, Reason = refusal.Reason });
        }

        var caller = await _callers.UserIdAsync(HttpContext).ConfigureAwait(false);

        if (caller is not Guid asker)
        {
            // A token that authenticates but names no user is an API key rather than a person. It
            // can reach this endpoint under the server's policy and there is nobody to attribute the
            // request to, so it is refused here rather than stored against an empty identifier.
            return BadRequest(new RequestRefused
            {
                Field = "caller",
                Reason = "This call is authenticated but names no user, so there is nobody to record as having asked."
            });
        }

        var asked = _clock.UtcNow;

        var incoming = new MediaRequest
        {
            Id = _identifiers.NewId(),
            RequestedByUserId = asker,
            RequestedAt = asked,
            StateChangedAt = asked,
            Kind = body.Kind!.Value,
            DisplayTitle = body.Title!,
            DisplayYear = body.Year,
            ProviderIds = body.IdentifiersOrEmpty(),
            Seasons = body.SeasonsOrEmpty(),
            RequesterNote = body.Note
        };

        var answer = await AskAsync(incoming, cancellationToken).ConfigureAwait(false);

        // 201 with no Location header, on purpose. Nothing reads one request back yet, so a
        // Location would point at something that answers 404, and a header that lies is worse than
        // one that is absent. Adding it when #53 lands is not a breaking change under the rule in
        // docs/api.md.
        return answer.Outcome == RequestOutcome.Created
            ? StatusCode(StatusCodes.Status201Created, answer)
            : Ok(answer);
    }

    /// <summary>
    /// Whether a request already in the queue is one a new ask may join.
    /// <para>
    /// Only a request that is still waiting for an answer or waiting to arrive. A declined request
    /// is an answer somebody gave and joining it would make a new asker inherit a refusal they never
    /// saw; a fulfilled one is finished, and a person asking for something the server already holds
    /// is asking a question the queue is the wrong place to answer; a failed one has been given up
    /// on. In every one of those a new ask is a new request, which is also what puts it back in front
    /// of an operator.
    /// </para>
    /// </summary>
    /// <param name="request">A request the store holds.</param>
    /// <returns><see langword="true"/> where a new ask may join it.</returns>
    private static bool StillOpenToJoiners(MediaRequest request)
        => request.State is RequestState.Open or RequestState.Approved;

    /// <summary>
    /// Refuses a body that cannot become a request, naming the field that is wrong.
    /// <para>
    /// The cap on the note and the shape of a season list are the record's own rules and are refused
    /// there by throwing. They are checked here as well, ahead of the record, so the caller is told
    /// which field was wrong instead of getting whatever an unhandled exception turns into. Two
    /// checks of one rule would be two rules if the numbers were written twice, so the length comes
    /// from <see cref="MediaRequest.NoteMaximumLength"/> and the season rule is stated once here in
    /// terms the record agrees with.
    /// </para>
    /// </summary>
    /// <param name="body">What the caller sent.</param>
    /// <param name="refusal">The field and the reason, where the answer is no.</param>
    /// <returns><see langword="true"/> where the body can become a request.</returns>
    private static bool Valid(CreateRequestBody body, out (string Field, string Reason) refusal)
    {
        if (body.Kind is not RequestedItemKind kind || !Enum.IsDefined(kind))
        {
            refusal = (nameof(CreateRequestBody.Kind), "This is not a kind of thing that can be asked for.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(body.Title))
        {
            refusal = (nameof(CreateRequestBody.Title), "A request carries the title as it read when it was asked for, and this one has none.");
            return false;
        }

        if (body.Title.Length > MediaRequest.NoteMaximumLength)
        {
            refusal = (nameof(CreateRequestBody.Title), Longer(nameof(CreateRequestBody.Title), body.Title.Length));
            return false;
        }

        if (body.Note is not null && body.Note.Length > MediaRequest.NoteMaximumLength)
        {
            refusal = (nameof(CreateRequestBody.Note), Longer(nameof(CreateRequestBody.Note), body.Note.Length));
            return false;
        }

        foreach (var identifier in body.IdentifiersOrEmpty())
        {
            if (string.IsNullOrWhiteSpace(identifier.Key) || string.IsNullOrWhiteSpace(identifier.Value))
            {
                refusal = (
                    nameof(CreateRequestBody.ProviderIds),
                    "An identifier carries a provider name and a value, and one of these carries an empty one.");
                return false;
            }
        }

        var seasons = body.SeasonsOrEmpty();

        if (seasons.Count > 0 && kind != RequestedItemKind.Series)
        {
            refusal = (
                nameof(CreateRequestBody.Seasons),
                "Seasons name part of a series, and this request is not for one.");
            return false;
        }

        if (seasons.Any(season => season < 1))
        {
            refusal = (nameof(CreateRequestBody.Seasons), "A season number has to be 1 or more.");
            return false;
        }

        if (seasons.Distinct().Count() != seasons.Count)
        {
            refusal = (
                nameof(CreateRequestBody.Seasons),
                "A season is named twice. The seasons asked for are a set, so a repeat is a mistake rather than a request for it twice.");
            return false;
        }

        refusal = default;
        return true;
    }

    /// <summary>
    /// The message for a field that is longer than a request keeps.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="length">How long it arrived.</param>
    /// <returns>The message.</returns>
    private static string Longer(string field, int length)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0} is {1} characters and a request keeps at most {2}. It is refused rather than cut, so nothing is stored that was not written.",
            field,
            length,
            MediaRequest.NoteMaximumLength);

    /// <summary>
    /// Puts the ask against what is already in the queue, and either joins one of them or creates it.
    /// <para>
    /// The candidates are read out of the store by identifier rather than by walking it, which is
    /// what makes this cheap on a queue of any size. A request carrying no identifier reaches nobody
    /// and is joined by nobody, which is <see cref="RequestIdentity"/>'s answer and not this
    /// method's: such a request has no identity, so it is different from everything including
    /// another copy of itself.
    /// </para>
    /// <para>
    /// The seasons narrow as the walk goes on. Where an existing request covers some of what is
    /// being asked for, what is left to ask for is what the next candidate is compared against, so a
    /// series whose seasons are spread over two existing requests is joined to the second rather
    /// than duplicated. If the narrowing ever leaves nothing, the candidate that took the last
    /// season compares as the same request and is joined.
    /// </para>
    /// </summary>
    /// <param name="incoming">The ask, as a request that does not exist yet.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What the caller is now waiting for and what asking did.</returns>
    private async Task<CreatedRequest> AskAsync(MediaRequest incoming, CancellationToken cancellationToken)
    {
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

                return new CreatedRequest
                {
                    Id = added.Request.Id,
                    State = added.Request.State,
                    Outcome = RequestOutcome.Created
                };
            }

            if (existing.Request.WasAskedForBy(ask.RequestedByUserId))
            {
                return new CreatedRequest
                {
                    Id = existing.Request.Id,
                    State = existing.Request.State,
                    Outcome = RequestOutcome.AlreadyWaiting
                };
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

                return new CreatedRequest
                {
                    Id = written.Request.Id,
                    State = written.Request.State,
                    Outcome = RequestOutcome.Joined
                };
            }
            catch (RequestConcurrencyException) when (attempt < JoinAttempts)
            {
                // Somebody moved that request between the read and the write, which on this endpoint
                // is usually a second person joining it in the same moment. Deciding again against
                // what the store holds now is exactly what the store contract asks a refused caller
                // to do, and the decision may come out differently: an operator who declined it
                // meanwhile makes it no longer joinable, and the next pass creates instead.
            }
        }
    }

    /// <summary>
    /// The requests already in the queue that could be the same thing as this ask.
    /// </summary>
    /// <param name="ask">The ask.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>Every joinable request naming at least one of the ask's identifiers, each once.</returns>
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

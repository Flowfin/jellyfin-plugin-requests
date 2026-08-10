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
/// Asking for something, reading what has been asked for, and deciding on it. These are the
/// endpoints everything else calls: the user surface, the administrator page, the sibling discover
/// plugin, and any script an operator writes.
/// <para>
/// <b>A decision is a call into the model and never a state written here.</b> Approving and
/// declining go through <see cref="RequestLifecycle"/>, which is where the transition table, the
/// caller's authority and the one history entry per move are. Four surfaces ask the same question,
/// and four copies of the rule are four chances for one of them to be a version behind. The lint
/// rule <c>state-written-only-by-the-lifecycle</c> refuses the shortcut in the source.
/// </para>
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
/// <b>Every endpoint carries its own policy, and the one on the controller is the floor.</b> An
/// endpoint that carried none would be reachable by whatever the class happens to declare on the day
/// it is added, and a class attribute is edited by somebody who is not reading the endpoint. Which
/// policy each one carries, and what a caller may see under it, is <c>docs/api.md</c>; that the
/// attribute is there at all is refused by <c>EndpointPolicyTests</c> over the built assembly and by
/// two rules in the invariant lint over the source.
/// </para>
/// <para>
/// Two policies are used here and no endpoint is anonymous. Creating a request and reading one's own
/// need an authenticated user, because a request has to be attributable to somebody to exist at all
/// and a caller with no session has no "own". Reading the whole queue needs an administrator.
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
    /// The server's policy for a call that has to come from an administrator, named as a literal for
    /// the same reason as the one above. It is what the server's own dashboard endpoints carry, so an
    /// endpoint under it is reachable by exactly the people who can already administer the server.
    /// </summary>
    private const string AdministratorPolicy = "RequiresElevation";

    /// <summary>
    /// How many rows a page holds when the caller does not say. Fifty is a screen of a queue and a
    /// small answer for a client that only wanted to know whether there is anything at all.
    /// </summary>
    public const int DefaultPageSize = 50;

    /// <summary>
    /// The largest page any caller may ask for. A page size is what stands between a queue with
    /// three years of history and an answer nobody can render, and a caller asking for more is
    /// refused rather than quietly given fewer: a client that asked for a thousand, got two hundred
    /// and was not told has just decided it has seen everything.
    /// </summary>
    public const int MaximumPageSize = 200;

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
    [Authorize(Policy = AuthenticatedUserPolicy)]
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
    /// The caller's own requests: the ones they asked for and the ones they joined.
    /// <para>
    /// <b>Nobody else's request can come back from here, whatever is asked for.</b> The narrowing is
    /// the read rather than a filter over a wider one: the store is asked for this person's requests
    /// through its own lookup, and the filter, the order and the page are applied to what that
    /// returns. There is no parameter that widens it, because there is nothing wider to widen to.
    /// </para>
    /// <para>
    /// The rows carry no identifier of any person. A request the caller joined was asked for by
    /// somebody else, so the stored record would name them and everybody else waiting alongside;
    /// <see cref="MyRequest"/> is what this returns instead.
    /// </para>
    /// </summary>
    /// <param name="state">The states to show, or none of them for every state.</param>
    /// <param name="kind">The kinds to show, or none of them for every kind.</param>
    /// <param name="order">What the rows are ordered by.</param>
    /// <param name="descending">Whether that order runs the other way.</param>
    /// <param name="skip">How many matches to step over before the page starts.</param>
    /// <param name="take">How many rows the page holds at most.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page, and how many of the caller's requests matched.</returns>
    [HttpGet("Requests")]
    [Authorize(Policy = AuthenticatedUserPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RequestsPage<MyRequest>>> MineAsync(
        [FromQuery] RequestState[]? state = null,
        [FromQuery] RequestedItemKind[]? kind = null,
        [FromQuery] RequestQueryOrder order = RequestQueryOrder.RequestedAt,
        [FromQuery] bool descending = false,
        [FromQuery] int skip = 0,
        [FromQuery] int take = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!Asked(state, kind, order, skip, take, descending, out var query, out var refusal))
        {
            return BadRequest(refusal);
        }

        var caller = await _callers.UserIdAsync(HttpContext).ConfigureAwait(false);

        if (caller is not Guid reader)
        {
            // Authenticated and naming nobody, which is what an API key looks like from here. There
            // is no "own" for such a caller, and answering with an empty page would say there are
            // none rather than that the question does not apply.
            return BadRequest(new RequestRefused
            {
                Field = "caller",
                Reason = "This call is authenticated but names no user, so there is nobody whose requests these would be."
            });
        }

        var theirs = await _store.FindForUserAsync(reader, cancellationToken).ConfigureAwait(false);
        var page = query.PageOf(theirs);

        return Ok(new RequestsPage<MyRequest>
        {
            Requests = [.. page.Requests.Select(stored => Mine(stored.Request, reader))],
            MatchCount = page.MatchCount,
            Skip = query.Skip,
            Take = query.Take
        });
    }

    /// <summary>
    /// The whole queue, for an administrator deciding on it.
    /// <para>
    /// The elevation is the endpoint's own and is on top of the controller's policy. It is what makes
    /// this the one place the whole queue is readable, and it is why the endpoint below it can be
    /// written without a filter that decides who may see what: a caller who is not an administrator
    /// never reaches this action at all, and the other one has nothing wider than one person's own
    /// requests to return.
    /// </para>
    /// <para>
    /// What the server does with that policy is the server's, and no test on this board exercises it:
    /// the headless rule in <c>docs/testing.md</c> refuses a running Jellyfin, so what the suite holds
    /// is that the attribute is on the action and that the other endpoint cannot be widened.
    /// </para>
    /// </summary>
    /// <param name="state">The states to show, or none of them for every state.</param>
    /// <param name="kind">The kinds to show, or none of them for every kind.</param>
    /// <param name="order">What the rows are ordered by.</param>
    /// <param name="descending">Whether that order runs the other way.</param>
    /// <param name="skip">How many matches to step over before the page starts.</param>
    /// <param name="take">How many rows the page holds at most.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page, and how many requests matched.</returns>
    [HttpGet("Requests/Queue")]
    [Authorize(Policy = AdministratorPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RequestsPage<QueuedRequest>>> QueueAsync(
        [FromQuery] RequestState[]? state = null,
        [FromQuery] RequestedItemKind[]? kind = null,
        [FromQuery] RequestQueryOrder order = RequestQueryOrder.RequestedAt,
        [FromQuery] bool descending = false,
        [FromQuery] int skip = 0,
        [FromQuery] int take = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!Asked(state, kind, order, skip, take, descending, out var query, out var refusal))
        {
            return BadRequest(refusal);
        }

        var page = await _store.PageAsync(query, cancellationToken).ConfigureAwait(false);

        return Ok(new RequestsPage<QueuedRequest>
        {
            Requests = [.. page.Requests.Select(Queued)],
            MatchCount = page.MatchCount,
            Skip = query.Skip,
            Take = query.Take
        });
    }

    /// <summary>
    /// Approves a request.
    /// <para>
    /// The move is <see cref="RequestLifecycle.Move"/>'s and nothing here decides it. Which states
    /// can be approved from, and by whom, is the table, so this endpoint cannot be the surface that
    /// knows one rule fewer than the page or the bridge does.
    /// </para>
    /// </summary>
    /// <param name="id">The request being approved.</param>
    /// <param name="body">The revision the operator was looking at.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The request as the queue now holds it, at its new revision. A request that moved since it
    /// was read, or one the table refuses this move on, is refused with
    /// <see cref="RequestMoveRefused"/> rather than obeyed.
    /// </returns>
    [HttpPost("Requests/{id}/Approve")]
    [Authorize(Policy = AdministratorPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QueuedRequest>> ApproveAsync(
        Guid id,
        [FromBody] ApproveRequestBody body,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return BadRequest(Refused(
                "body",
                "There is no body on this call, and the revision the decision was made against is in it."));
        }

        if (body.Revision is not long revision)
        {
            return BadRequest(Refused(
                nameof(ApproveRequestBody.Revision),
                "A decision carries the revision it was made against. Without one this would be a write against whatever the store holds by the time it arrives, which is how two operators deciding one request end with one decision silently lost."));
        }

        return await MoveAsync(
            id,
            revision,
            static (request, at, by) => RequestLifecycle.Move(request, RequestState.Approved, at, by),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Declines a request, with the reason a decline is required to carry.
    /// <para>
    /// The reason is checked here as well as in the model, so the caller is told which field was
    /// wrong instead of getting whatever an unhandled exception turns into. The rule is stated once,
    /// in <see cref="RequestLifecycle.Decline"/>, and this agrees with it rather than restating it.
    /// </para>
    /// </summary>
    /// <param name="id">The request being declined.</param>
    /// <param name="body">The revision, the reason, and what the operator wants to say.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The request as the queue now holds it, at its new revision, or the refusal.
    /// </returns>
    [HttpPost("Requests/{id}/Decline")]
    [Authorize(Policy = AdministratorPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QueuedRequest>> DeclineAsync(
        Guid id,
        [FromBody] DeclineRequestBody body,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return BadRequest(Refused(
                "body",
                "There is no body on this call, and the revision the decision was made against is in it."));
        }

        if (body.Revision is not long revision)
        {
            return BadRequest(Refused(
                nameof(DeclineRequestBody.Revision),
                "A decision carries the revision it was made against. Without one this would be a write against whatever the store holds by the time it arrives, which is how two operators deciding one request end with one decision silently lost."));
        }

        if (body.Reason is not DeclineReason reason || !Enum.IsDefined(reason))
        {
            return BadRequest(Refused(
                nameof(DeclineRequestBody.Reason),
                "A decline carries a reason. Without one the person who asked is told no and nothing else, and what they do next is ask for the same title again."));
        }

        if (reason == DeclineReason.Other && string.IsNullOrWhiteSpace(body.Note))
        {
            return BadRequest(Refused(
                nameof(DeclineRequestBody.Note),
                "A decline for a reason that is not on the list has to say what the reason was. Other with nothing beside it is a decline with no reason, which is the thing a required reason exists to prevent."));
        }

        if (body.Note is not null && body.Note.Length > MediaRequest.NoteMaximumLength)
        {
            return BadRequest(Refused(nameof(DeclineRequestBody.Note), Longer(nameof(DeclineRequestBody.Note), body.Note.Length)));
        }

        return await MoveAsync(
            id,
            revision,
            (request, at, by) => RequestLifecycle.Decline(request, reason, body.Note, at, by),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the request, makes the move the caller asked for, and writes it back against the
    /// revision they read it at.
    /// <para>
    /// Both endpoints above are this method and a move. What each one does that is its own is the
    /// body it takes and the cell of the table it aims at; everything else - who is calling, whether
    /// the request is still where the caller thinks it is, what the table says, and what a refused
    /// write means - is one piece of code, so a third operation added later cannot come with one of
    /// these steps missing.
    /// </para>
    /// <para>
    /// <b>The revision is checked before the model is asked and again by the store.</b> The second
    /// is what makes the write safe; the first is what makes the answer true. Without it, a request
    /// that was fulfilled between the read and the call would be refused by the table, and the
    /// caller would be told this move is never available on that request when what actually happened
    /// is that somebody else moved it a moment ago.
    /// </para>
    /// </summary>
    /// <param name="id">The request being moved.</param>
    /// <param name="revision">The revision the caller read it at.</param>
    /// <param name="move">The move, as the model makes it.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The request at its new revision, or the refusal.</returns>
    private async Task<ActionResult<QueuedRequest>> MoveAsync(
        Guid id,
        long revision,
        Func<MediaRequest, DateTimeOffset, RequestCaller, MediaRequest> move,
        CancellationToken cancellationToken)
    {
        var caller = await _callers.UserIdAsync(HttpContext).ConfigureAwait(false);

        if (caller is not Guid administrator)
        {
            // Authenticated and naming nobody, which is what an API key looks like from here. A
            // decision is somebody's, and the history entry this move appends has a field for who
            // made it that would otherwise read as the plugin having observed something.
            return BadRequest(Refused(
                "caller",
                "This call is authenticated but names no user, so there is nobody to record as having decided."));
        }

        var stored = await _store.GetAsync(id, cancellationToken).ConfigureAwait(false);

        if (stored is not StoredRequest held)
        {
            return NotFound();
        }

        if (held.Revision != revision)
        {
            return Conflict(new RequestMoveRefused
            {
                Refusal = RequestMoveRefusal.MovedSinceItWasRead,
                Reason = "This request has moved since it was read, so the decision was made against something that is no longer there. What the queue holds now is beside this.",
                Current = Queued(held)
            });
        }

        MediaRequest moved;

        try
        {
            // The whole of the decision is this line. The transition table, who may make the move
            // and the one history entry it appends are the model's, and an endpoint that reached
            // around them would be a fourth copy of a rule that already has three readers.
            moved = move(held.Request, _clock.UtcNow, RequestCaller.Administrator(administrator));
        }
        catch (IllegalRequestTransitionException refused)
        {
            return Conflict(new RequestMoveRefused
            {
                Refusal = RequestMoveRefusal.TheTableRefusesTheMove,
                Reason = refused.Message,
                Current = Queued(held)
            });
        }
        catch (RequestNotIdentifiedException refused)
        {
            return Conflict(new RequestMoveRefused
            {
                Refusal = RequestMoveRefusal.TheRequestNamesNothing,
                Reason = refused.Message,
                Current = Queued(held)
            });
        }
        catch (RequestMoveNotPermittedException refused)
        {
            // No call that reaches here can produce this today, and the reason is measured rather
            // than assumed: both endpoints require elevation, so the caller is built as an
            // administrator, and every legal cell into Approved or Declined is a decision cell that
            // admits one. TheOnlyCallerTheseEndpointsBuildIsAdmittedByEveryMoveTheyCanMake holds
            // that, and it reds the day a cell in the table stops admitting an administrator. This
            // arm is what that day answers with instead of a stack trace.
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new RequestMoveRefused
                {
                    Refusal = RequestMoveRefusal.TheCallerMayNotMakeThisMove,
                    Reason = refused.Message,
                    Current = Queued(held)
                });
        }

        try
        {
            var written = await _store.ReplaceAsync(moved, revision, cancellationToken).ConfigureAwait(false);

            return Ok(Queued(written));
        }
        catch (RequestConcurrencyException lost)
        {
            // The window between the read above and this write. It is small and it is real: two
            // administrators clicking within it both pass the revision check and exactly one of
            // them is accepted here.
            return Conflict(new RequestMoveRefused
            {
                Refusal = RequestMoveRefusal.MovedSinceItWasRead,
                Reason = "This request moved while the decision was being written, so it was refused rather than applied over somebody else's.",
                Current = lost.Current is StoredRequest now ? Queued(now) : null
            });
        }
    }

    /// <summary>
    /// The query a call asked for, or the field that made it refusable.
    /// <para>
    /// Every enumeration is checked against what it declares. A value outside it arrives as a number
    /// the binder is happy to cast, so a state of 99 would otherwise reach the filter and match
    /// nothing, and the caller would read an empty queue as an empty queue.
    /// </para>
    /// </summary>
    /// <param name="states">The states asked for.</param>
    /// <param name="kinds">The kinds asked for.</param>
    /// <param name="order">The order asked for.</param>
    /// <param name="skip">The offset asked for.</param>
    /// <param name="take">The page size asked for.</param>
    /// <param name="descending">Whether the order runs the other way.</param>
    /// <param name="query">The query, where the call is answerable.</param>
    /// <param name="refusal">The field and the reason, where it is not.</param>
    /// <returns><see langword="true"/> where the call is answerable.</returns>
    private static bool Asked(
        RequestState[]? states,
        RequestedItemKind[]? kinds,
        RequestQueryOrder order,
        int skip,
        int take,
        bool descending,
        out RequestQuery query,
        out RequestRefused refusal)
    {
        query = null!;

        if (states is not null && Array.Exists(states, asked => !Enum.IsDefined(asked)))
        {
            refusal = Refused("state", "That is not a state a request can be in.");
            return false;
        }

        if (kinds is not null && Array.Exists(kinds, asked => !Enum.IsDefined(asked)))
        {
            refusal = Refused("kind", "That is not a kind of thing that can be asked for.");
            return false;
        }

        if (!Enum.IsDefined(order))
        {
            refusal = Refused("order", "That is not something these requests can be ordered by.");
            return false;
        }

        if (skip < 0)
        {
            refusal = Refused("skip", "A page cannot start before the beginning.");
            return false;
        }

        if (take < 0)
        {
            refusal = Refused("take", "A page cannot hold fewer than no rows.");
            return false;
        }

        if (take > MaximumPageSize)
        {
            refusal = Refused(
                "take",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "A page holds at most {0} rows. This is refused rather than answered with fewer, because a caller given fewer than it asked for and not told has no way to know there is more.",
                    MaximumPageSize));
            return false;
        }

        query = new RequestQuery
        {
            States = states ?? [],
            Kinds = kinds ?? [],
            Order = order,
            Descending = descending,
            Skip = skip,
            Take = take
        };

        refusal = null!;
        return true;
    }

    /// <summary>
    /// A refusal naming the query parameter that is wrong.
    /// </summary>
    /// <param name="field">The parameter, spelled as the caller wrote it.</param>
    /// <param name="reason">What is wrong with it.</param>
    /// <returns>The refusal.</returns>
    private static RequestRefused Refused(string field, string reason)
        => new RequestRefused { Field = field, Reason = reason };

    /// <summary>
    /// One request as the person waiting for it sees it.
    /// </summary>
    /// <param name="request">The stored request.</param>
    /// <param name="reader">Who is reading it.</param>
    /// <returns>The row.</returns>
    private static MyRequest Mine(MediaRequest request, Guid reader)
    {
        var asked = request.RequestedByUserId == reader;

        return new MyRequest
        {
            Id = request.Id,
            Kind = request.Kind,
            DisplayTitle = request.DisplayTitle,
            DisplayYear = request.DisplayYear,
            Seasons = request.Seasons,
            State = request.State,
            RequestedAt = request.RequestedAt,
            StateChangedAt = request.StateChangedAt,
            AskedByYou = asked,

            // Only where the caller wrote it. On a request they joined, the note is the first
            // person's writing and this shape does not carry another person's words.
            YourNote = asked ? request.RequesterNote : null,
            DeclineReason = request.DeclineReason,
            DeclineNote = request.DeclineNote,
            Availability = request.Availability
        };
    }

    /// <summary>
    /// One request as an administrator reading the queue sees it.
    /// </summary>
    /// <param name="stored">The request and the revision the store has it at.</param>
    /// <returns>The row.</returns>
    private static QueuedRequest Queued(StoredRequest stored)
        => new QueuedRequest
        {
            Id = stored.Request.Id,
            Revision = stored.Revision,
            RequestedByUserId = stored.Request.RequestedByUserId,
            JoinedByUserIds = stored.Request.JoinedByUserIds,
            Kind = stored.Request.Kind,
            DisplayTitle = stored.Request.DisplayTitle,
            DisplayYear = stored.Request.DisplayYear,
            ProviderIds = stored.Request.ProviderIds,
            Seasons = stored.Request.Seasons,
            State = stored.Request.State,
            RequestedAt = stored.Request.RequestedAt,
            StateChangedAt = stored.Request.StateChangedAt,
            StateChangedByUserId = stored.Request.StateChangedByUserId,
            RequesterNote = stored.Request.RequesterNote,
            DeclineReason = stored.Request.DeclineReason,
            DeclineNote = stored.Request.DeclineNote,
            Availability = stored.Request.Availability,
            AvailabilityCheckedAt = stored.Request.AvailabilityCheckedAt
        };

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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Identity;
using Jellyfin.Plugin.Requests.Intake;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Time;
using MediaBrowser.Common.Api;
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
/// attribute is there at all is refused by <c>EndpointPolicyTests</c> over the built assembly, which
/// is the only place it can be: the two rules in the invariant lint refuse the two ways one is taken
/// away, and a rule about text cannot see a line nobody wrote.
/// </para>
/// <para>
/// Two policies are used here and no endpoint is anonymous. Creating a request and reading one's own
/// need an authenticated user, because a request has to be attributable to somebody to exist at all
/// and a caller with no session has no "own"; the server registers no name for that and builds it
/// into its default policy, so those endpoints carry <c>[Authorize]</c> with nothing after it.
/// Reading the whole queue and every decision need an administrator, which the server does name, so
/// those carry its own constant.
/// </para>
/// </summary>
[Authorize]
public sealed class RequestsController : RequestsControllerBase
{
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
    /// The store failure, written once because it is answered from a call and from inside one
    /// request of an action on several.
    /// <para>
    /// The sentence says nothing about the rest of the call on purpose. It was true of a single
    /// decision that nothing at all had been changed, and it is not true of the fourth request in an
    /// action whose first three were written; what holds in both places is that this request was not
    /// decided and nothing about it moved.
    /// </para>
    /// </summary>
    private static readonly RequestFailure StoreCouldNotBeRead = new RequestFailure
    {
        Code = RequestFailureCode.TheStoreCouldNotBeRead,
        Message = "The queue could not be read, so this was not decided and nothing about it was changed. This is a fault on the server rather than anything wrong with the call, and the server log says which file and why."
    };

    private readonly IRequestStore _store;
    private readonly IClock _clock;
    private readonly IIdentifierSource _identifiers;
    private readonly ICallerIdentity _callers;
    private readonly RequestIntake _intake;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestsController"/> class.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="clock">The injected clock, so a request's times are testable.</param>
    /// <param name="identifiers">Where a new request's identifier comes from.</param>
    /// <param name="callers">The server's answer to who is calling.</param>
    /// <param name="settings">
    /// What this install is set to. The intake reads the quota out of it on every ask, so the
    /// endpoint cannot ask without one.
    /// </param>
    public RequestsController(
        IRequestStore store,
        IClock clock,
        IIdentifierSource identifiers,
        ICallerIdentity callers,
        IInstallSettings settings)
    {
        _store = store;
        _clock = clock;
        _identifiers = identifiers;
        _callers = callers;

        // Built here rather than injected, because it is this controller's use of the store rather
        // than a sixth thing the server has to supply. CatalogueSplitTests reads the list this
        // constructor takes, and a request intake that arrived as a dependency would read as one.
        _intake = new RequestIntake(store, settings);
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
    [Authorize]
    [ProducesResponseType<CreatedRequest>(StatusCodes.Status201Created)]
    [ProducesResponseType<CreatedRequest>(StatusCodes.Status200OK)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CreatedRequest>> CreateAsync(
        [FromBody] CreateRequestBody body,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return Invalid("body", "There is no body on this call, and every field is in it.");
        }

        if (!Valid(body, out var refusal))
        {
            return Invalid(refusal.Field, refusal.Reason);
        }

        var caller = await _callers.UserIdAsync(HttpContext).ConfigureAwait(false);

        if (caller is not Guid asker)
        {
            // A token that authenticates but names no user is an API key rather than a person. It
            // can reach this endpoint under the server's policy and there is nobody to attribute the
            // request to, so it is refused here rather than stored against an empty identifier.
            return NoUser("This call is authenticated but names no user, so there is nobody to record as having asked.");
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

        CreatedRequest answer;

        try
        {
            answer = await AskAsync(incoming, RequestCaller.User(asker), cancellationToken).ConfigureAwait(false);
        }
        catch (RequestStoreLoadException)
        {
            return TheStoreCouldNotBeRead();
        }
        catch (RequestQuotaReachedException atTheirQuota)
        {
            // The numbers are the caller's own and say nothing about anybody else's queue, which is
            // why they may be reported: how many things this person is waiting for is something they
            // can already read off their own page.
            return Failed(
                RequestFailureCode.TheyAreAtTheirQuota,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "You are waiting for {0} requests and this server allows {1} at once. One of them has to be answered before you can ask for something else.",
                    atTheirQuota.Held,
                    atTheirQuota.Limit));
        }
        catch (InvalidConfigurationException)
        {
            // Nothing from the exception reaches the caller. It names the settings that are wrong,
            // which is the operator's business and not the asker's, and the server log carries it.
            return Failed(
                RequestFailureCode.ThisInstallCannotRun,
                "This server is set to something the plugin cannot run on, so nothing can be asked for until an operator corrects it. The server log says which setting.");
        }

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
    [Authorize]
    [ProducesResponseType<RequestsPage<MyRequest>>(StatusCodes.Status200OK)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status503ServiceUnavailable)]
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
            return Invalid(refusal.Field, refusal.Reason);
        }

        var caller = await _callers.UserIdAsync(HttpContext).ConfigureAwait(false);

        if (caller is not Guid reader)
        {
            // Authenticated and naming nobody, which is what an API key looks like from here. There
            // is no "own" for such a caller, and answering with an empty page would say there are
            // none rather than that the question does not apply.
            return NoUser("This call is authenticated but names no user, so there is nobody whose requests these would be.");
        }

        IReadOnlyList<StoredRequest> theirs;

        try
        {
            theirs = await _store.FindForUserAsync(reader, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestStoreLoadException)
        {
            return TheStoreCouldNotBeRead();
        }

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
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType<RequestsPage<QueuedRequest>>(StatusCodes.Status200OK)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status503ServiceUnavailable)]
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
            return Invalid(refusal.Field, refusal.Reason);
        }

        RequestPage page;

        try
        {
            page = await _store.PageAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestStoreLoadException)
        {
            return TheStoreCouldNotBeRead();
        }

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
    /// <see cref="RequestFailure"/> rather than obeyed.
    /// </returns>
    [HttpPost("Requests/{id}/Approve")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType<QueuedRequest>(StatusCodes.Status200OK)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<QueuedRequest>> ApproveAsync(
        Guid id,
        [FromBody] ApproveRequestBody body,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return Invalid(
                "body",
                "There is no body on this call, and the revision the decision was made against is in it.");
        }

        if (body.Revision is not long revision)
        {
            return Invalid(
                nameof(ApproveRequestBody.Revision),
                "A decision carries the revision it was made against. Without one this would be a write against whatever the store holds by the time it arrives, which is how two operators deciding one request end with one decision silently lost.");
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
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType<QueuedRequest>(StatusCodes.Status200OK)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<QueuedRequest>> DeclineAsync(
        Guid id,
        [FromBody] DeclineRequestBody body,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return Invalid(
                "body",
                "There is no body on this call, and the revision the decision was made against is in it.");
        }

        if (body.Revision is not long revision)
        {
            return Invalid(
                nameof(DeclineRequestBody.Revision),
                "A decision carries the revision it was made against. Without one this would be a write against whatever the store holds by the time it arrives, which is how two operators deciding one request end with one decision silently lost.");
        }

        if (!Declining(body.Reason, body.Note, out var refusal))
        {
            return Invalid(refusal.Field, refusal.Reason);
        }

        var reason = body.Reason!.Value;

        return await MoveAsync(
            id,
            revision,
            (request, at, by) => RequestLifecycle.Decline(request, reason, body.Note, at, by),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Approves several requests in one action.
    /// <para>
    /// The gesture is an operator who has been away and is answering a batch. What it is not is a
    /// faster way to make one decision: every request in it goes through the same code as the single
    /// endpoint, one at a time, so no rule of the transition table is reachable here that is not
    /// reachable there.
    /// </para>
    /// </summary>
    /// <param name="body">The requests, each with the revision the operator was looking at.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>One entry per request, in the order they were sent.</returns>
    [HttpPost("Requests/Approve")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType<DecidedRequests>(StatusCodes.Status200OK)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DecidedRequests>> ApproveManyAsync(
        [FromBody] ApproveManyBody body,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return Invalid(
                "body",
                "There is no body on this call, and the requests being decided are in it.");
        }

        if (!Chosen(body.Requests, out var chosen, out var refusal))
        {
            return Invalid(refusal.Field, refusal.Reason);
        }

        return await DecideEachAsync(
            chosen,
            static (request, at, by) => RequestLifecycle.Move(request, RequestState.Approved, at, by),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Declines several requests in one action, for one reason.
    /// </summary>
    /// <param name="body">The requests, the reason they are all declined for, and the note.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>One entry per request, in the order they were sent.</returns>
    [HttpPost("Requests/Decline")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType<DecidedRequests>(StatusCodes.Status200OK)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DecidedRequests>> DeclineManyAsync(
        [FromBody] DeclineManyBody body,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return Invalid(
                "body",
                "There is no body on this call, and the requests being decided are in it.");
        }

        if (!Chosen(body.Requests, out var chosen, out var refusal)
            || !Declining(body.Reason, body.Note, out refusal))
        {
            return Invalid(refusal.Field, refusal.Reason);
        }

        var reason = body.Reason!.Value;

        return await DecideEachAsync(
            chosen,
            (request, at, by) => RequestLifecycle.Decline(request, reason, body.Note, at, by),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes one move on each of several requests, in the order they were sent.
    /// <para>
    /// <b>One at a time, and each one through <see cref="DecideAsync"/>.</b> That is the whole of
    /// what makes this path the same path as the single decision: the read, the revision check, the
    /// call into the model and the write against the revision are one piece of code with one caller
    /// more, rather than a second implementation that has to be kept in step with the first.
    /// </para>
    /// <para>
    /// Sequential rather than at once. A decision is a read followed by a write against the revision
    /// that was read, so running them together would have the writes of one action contend with each
    /// other and refuse each other for a conflict this call created itself.
    /// </para>
    /// <para>
    /// <b>A refusal that is about one request is in that request's entry, not in the status of the
    /// call.</b> By the time one of them is refused another may already be written, and a call that
    /// answered with a failure would be saying nothing happened while something had. What stays a
    /// refusal of the call is what is decided before anything is written: a body that cannot be
    /// read, and a caller that names nobody.
    /// </para>
    /// </summary>
    /// <param name="chosen">The requests and the revisions they were read at.</param>
    /// <param name="move">The move, as the model makes it.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>One entry per request.</returns>
    private async Task<ActionResult<DecidedRequests>> DecideEachAsync(
        IReadOnlyList<(Guid Id, long Revision)> chosen,
        Func<MediaRequest, DateTimeOffset, RequestCaller, MediaRequest> move,
        CancellationToken cancellationToken)
    {
        var caller = await _callers.UserIdAsync(HttpContext).ConfigureAwait(false);

        if (caller is not Guid administrator)
        {
            return NoUser("This call is authenticated but names no user, so there is nobody to record as having decided.");
        }

        var decided = new List<DecidedRequest>(chosen.Count);

        foreach (var one in chosen)
        {
            decided.Add(await DecideAsync(one.Id, one.Revision, administrator, move, cancellationToken).ConfigureAwait(false));
        }

        return Ok(new DecidedRequests { Requests = decided });
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
            return NoUser("This call is authenticated but names no user, so there is nobody to record as having decided.");
        }

        var decided = await DecideAsync(id, revision, administrator, move, cancellationToken).ConfigureAwait(false);

        // One request, so the entry's refusal becomes the status of the call. On the endpoints that
        // carry several, the same entry stays in the answer beside the others, which is the only
        // difference between the two paths.
        return decided.Failure is RequestFailure refusal ? Answer(refusal) : Ok(decided.Request);
    }

    /// <summary>
    /// The decision itself: read the request, check it is where the caller thinks it is, ask the
    /// model to move it, and write it back against the revision that was read.
    /// <para>
    /// It answers with an entry rather than with a status code, which is what lets one request and
    /// forty go through it. A status code is an answer to a call, and an action on several requests
    /// is one call with several answers in it.
    /// </para>
    /// <para>
    /// <b>Who is calling is decided above this and passed in.</b> An action on forty requests asks
    /// the server once who is calling rather than forty times, and the answer cannot change halfway
    /// through an action.
    /// </para>
    /// </summary>
    /// <param name="id">The request being moved.</param>
    /// <param name="revision">The revision the caller read it at.</param>
    /// <param name="administrator">Who is deciding, recorded on the move.</param>
    /// <param name="move">The move, as the model makes it.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The request at its new revision, or why it was refused.</returns>
    private async Task<DecidedRequest> DecideAsync(
        Guid id,
        long revision,
        Guid administrator,
        Func<MediaRequest, DateTimeOffset, RequestCaller, MediaRequest> move,
        CancellationToken cancellationToken)
    {
        StoredRequest? stored;

        try
        {
            stored = await _store.GetAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestStoreLoadException)
        {
            return NotMoved(id, StoreCouldNotBeRead);
        }

        if (stored is not StoredRequest held)
        {
            return NotMoved(
                id,
                Refusal(
                    RequestFailureCode.NoSuchRequest,
                    "There is no request with that identifier. It may have been removed and it may never have existed, and this answer is the same either way."));
        }

        if (held.Revision != revision)
        {
            return NotMoved(
                id,
                Refusal(
                    RequestFailureCode.MovedSinceItWasRead,
                    "This request has moved since it was read, so the decision was made against something that is no longer there. What the queue holds now is beside this.",
                    current: Queued(held)));
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
            return NotMoved(id, Refusal(RequestFailureCode.TheTableRefusesTheMove, refused.Message, current: Queued(held)));
        }
        catch (RequestNotIdentifiedException refused)
        {
            return NotMoved(id, Refusal(RequestFailureCode.TheRequestNamesNothing, refused.Message, current: Queued(held)));
        }
        catch (RequestMoveNotPermittedException refused)
        {
            // No call that reaches here can produce this today, and the reason is measured rather
            // than assumed: every endpoint that reaches this requires elevation, so the caller is
            // built as an administrator, and every legal cell into Approved or Declined is a
            // decision cell that admits one.
            // TheOnlyCallerTheseEndpointsBuildIsAdmittedByEveryMoveTheyCanMake holds that, and it
            // reds the day a cell in the table stops admitting an administrator. This arm is what
            // that day answers with instead of a stack trace.
            return NotMoved(id, Refusal(RequestFailureCode.TheCallerMayNotMakeThisMove, refused.Message, current: Queued(held)));
        }

        try
        {
            var written = await _store.ReplaceAsync(moved, revision, cancellationToken).ConfigureAwait(false);

            return new DecidedRequest { Id = id, Request = Queued(written) };
        }
        catch (RequestConcurrencyException lost)
        {
            // The window between the read above and this write. It is small and it is real: two
            // administrators clicking within it both pass the revision check and exactly one of
            // them is accepted here.
            return NotMoved(
                id,
                Refusal(
                    RequestFailureCode.MovedSinceItWasRead,
                    "This request moved while the decision was being written, so it was refused rather than applied over the decision that got there first.",
                    current: lost.Current is StoredRequest now ? Queued(now) : null));
        }
        catch (RequestStoreLoadException)
        {
            return NotMoved(id, StoreCouldNotBeRead);
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
        out (string Field, string Reason) refusal)
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

        refusal = default;
        return true;
    }

    /// <summary>
    /// A refusal naming the query parameter that is wrong.
    /// </summary>
    /// <param name="field">The parameter, spelled as the caller wrote it.</param>
    /// <param name="reason">What is wrong with it.</param>
    /// <returns>The field and the reason.</returns>
    private static (string Field, string Reason) Refused(string field, string reason) => (field, reason);

    /// <summary>
    /// One failure, under the status code its class is reported with.
    /// <para>
    /// Every failure this controller answers goes through here, so the pairing of a code and a
    /// status code is decided once, in <see cref="RequestFailure.StatusFor"/>, rather than at each
    /// call. An endpoint that answered one code under two status codes would leave a caller
    /// branching on the status for one of them and on the code for the other, and the first breaks
    /// the day the second endpoint is the one it meets.
    /// </para>
    /// </summary>
    /// <param name="code">What went wrong.</param>
    /// <param name="message">The sentence for the person reading it.</param>
    /// <param name="field">The field that is wrong, where there is one.</param>
    /// <param name="current">What the store holds now, where the caller may see it.</param>
    /// <returns>The failure.</returns>
    private ObjectResult Failed(
        RequestFailureCode code,
        string message,
        string? field = null,
        QueuedRequest? current = null)
        => Answer(Refusal(code, message, field, current));

    /// <summary>
    /// One failure, under the status code its class is reported with.
    /// <para>
    /// The pairing is <see cref="RequestFailure.StatusFor"/>'s and is read here rather than chosen,
    /// which is what lets a refusal built inside a decision come back from the single endpoint under
    /// the same status code it would have had if the endpoint had built it.
    /// </para>
    /// </summary>
    /// <param name="failure">What went wrong.</param>
    /// <returns>The failure, under its status code.</returns>
    private ObjectResult Answer(RequestFailure failure)
        => StatusCode(RequestFailure.StatusFor(failure.Code), failure);

    /// <summary>
    /// One failure, as the shape every failure of this API comes back in.
    /// </summary>
    /// <param name="code">What went wrong.</param>
    /// <param name="message">The sentence for the person reading it.</param>
    /// <param name="field">The field that is wrong, where there is one.</param>
    /// <param name="current">What the store holds now, where the caller may see it.</param>
    /// <returns>The failure.</returns>
    private static RequestFailure Refusal(
        RequestFailureCode code,
        string message,
        string? field = null,
        QueuedRequest? current = null)
        => new RequestFailure { Code = code, Message = message, Field = field, Current = current };

    /// <summary>
    /// One request an action did not move, and why.
    /// </summary>
    /// <param name="id">The request, as the caller named it.</param>
    /// <param name="failure">Why it was refused.</param>
    /// <returns>The entry.</returns>
    private static DecidedRequest NotMoved(Guid id, RequestFailure failure)
        => new DecidedRequest { Id = id, Failure = failure };

    /// <summary>
    /// A body that cannot be acted on, naming the field that is wrong.
    /// </summary>
    /// <param name="field">The field, spelled as the body spells it.</param>
    /// <param name="reason">What is wrong with it.</param>
    /// <returns>The failure.</returns>
    private ObjectResult Invalid(string field, string reason)
        => Failed(RequestFailureCode.InvalidBody, reason, field: field);

    /// <summary>
    /// A call that authenticated and names no person.
    /// </summary>
    /// <param name="reason">What that means for this endpoint.</param>
    /// <returns>The failure.</returns>
    private ObjectResult NoUser(string reason)
        => Failed(RequestFailureCode.NoUserOnTheCall, reason);

    /// <summary>
    /// A store this call could not read.
    /// <para>
    /// The exception is caught and nothing from it reaches the caller. Its message names the file
    /// the store could not read, which is a path on the server disk and is exactly what an error
    /// from this API may not carry. The message here is written for the person who will read it and
    /// says the one thing they can act on, which is that this is not their call being wrong.
    /// </para>
    /// <para>
    /// The rule is here once and the <c>try</c> around each store call is what repeats. That is the
    /// smaller of the two costs: a wrapper that swallowed every store call would also swallow the
    /// concurrency refusal, which is an answer rather than a failure.
    /// </para>
    /// </summary>
    /// <returns>The failure.</returns>
    private ObjectResult TheStoreCouldNotBeRead()
        => Answer(StoreCouldNotBeRead);

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
    /// Refuses a decline that carries no usable reason, naming the field that is wrong.
    /// <para>
    /// One rule for the decline of one request and the decline of forty. The reason and the note are
    /// the model's rules and are refused there by throwing; they are checked here as well, ahead of
    /// the model, so the caller is told which field was wrong. Written once because a second copy is
    /// the endpoint that ends up one rule behind, and both bodies spell these two fields the same,
    /// which <c>BothDeclineBodiesSpellTheReasonAndTheNoteTheSameWay</c> holds.
    /// </para>
    /// </summary>
    /// <param name="reason">The reason the caller sent.</param>
    /// <param name="note">What the caller wants to say about it.</param>
    /// <param name="refusal">The field and the reason, where the answer is no.</param>
    /// <returns><see langword="true"/> where the decline can be made.</returns>
    private static bool Declining(DeclineReason? reason, string? note, out (string Field, string Reason) refusal)
    {
        if (reason is not DeclineReason chosen || !Enum.IsDefined(chosen))
        {
            refusal = Refused(
                nameof(DeclineRequestBody.Reason),
                "A decline carries a reason. Without one the person who asked is told no and nothing else, and what they do next is ask for the same title again.");
            return false;
        }

        if (chosen == DeclineReason.Other && string.IsNullOrWhiteSpace(note))
        {
            refusal = Refused(
                nameof(DeclineRequestBody.Note),
                "A decline for a reason that is not on the list has to say what the reason was. Other with nothing beside it is a decline with no reason, which is the thing a required reason exists to prevent.");
            return false;
        }

        if (note is not null && note.Length > MediaRequest.NoteMaximumLength)
        {
            refusal = Refused(nameof(DeclineRequestBody.Note), Longer(nameof(DeclineRequestBody.Note), note.Length));
            return false;
        }

        refusal = default;
        return true;
    }

    /// <summary>
    /// Refuses a selection that cannot be acted on, and hands back the requests where it can.
    /// <para>
    /// <b>The whole selection is refused rather than the part of it that is readable.</b> Every
    /// check here is about the body rather than about the queue, so it is answered before anything
    /// is written; acting on the readable half of a body somebody built wrong would leave an
    /// operator with some of an action done and no way to tell which part was even attempted.
    /// </para>
    /// <para>
    /// A position is named rather than an index, because the message is read by a person looking at
    /// a body they sent, and the field is the list rather than one entry: an entry of a list is not
    /// something the body names.
    /// </para>
    /// </summary>
    /// <param name="requests">What the body carried.</param>
    /// <param name="chosen">The requests and their revisions, where the selection can be acted on.</param>
    /// <param name="refusal">The field and the reason, where it cannot.</param>
    /// <returns><see langword="true"/> where the selection can be acted on.</returns>
    private static bool Chosen(
        IReadOnlyList<RequestToDecide>? requests,
        out IReadOnlyList<(Guid Id, long Revision)> chosen,
        out (string Field, string Reason) refusal)
    {
        chosen = [];

        if (requests is null || requests.Count == 0)
        {
            refusal = Refused(
                nameof(ApproveManyBody.Requests),
                "An action carries the requests it is an action on. An empty one would be answered as having decided everything it was asked to, which is true and says nothing.");
            return false;
        }

        if (requests.Count > MaximumPageSize)
        {
            refusal = Refused(
                nameof(ApproveManyBody.Requests),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "An action carries at most {0} requests, which is the page an operator selects them from. It is refused rather than acted on as far as the cap, because a caller told nothing would read a partly done action as a whole one.",
                    MaximumPageSize));
            return false;
        }

        var picked = new List<(Guid Id, long Revision)>(requests.Count);
        var seen = new Dictionary<Guid, int>();

        for (var position = 0; position < requests.Count; position++)
        {
            var one = requests[position];

            if (one?.Id is not Guid id || id == Guid.Empty)
            {
                refusal = Refused(
                    nameof(ApproveManyBody.Requests),
                    At(position, "names no request, so there is nothing for this action to decide there."));
                return false;
            }

            if (one.Revision is not long revision)
            {
                refusal = Refused(
                    nameof(ApproveManyBody.Requests),
                    At(position, "carries no revision. A decision made against whatever the store holds by the time it arrives is how two operators deciding one request end with one decision silently lost."));
                return false;
            }

            if (seen.TryGetValue(id, out var first))
            {
                refusal = Refused(
                    nameof(ApproveManyBody.Requests),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "The same request is in position {0} and position {1}. The second would be refused as having moved since it was read, against a move this same action had just made, so the action is refused instead of answering with a conflict it created itself.",
                        first + 1,
                        position + 1));
                return false;
            }

            seen.Add(id, position);
            picked.Add((id, revision));
        }

        chosen = picked;
        refusal = default;
        return true;
    }

    /// <summary>
    /// One sentence about the request in one position of a selection, counted the way somebody
    /// reading their own body counts.
    /// </summary>
    /// <param name="position">Where it is in the list, counted from zero.</param>
    /// <param name="wrong">What is wrong with it.</param>
    /// <returns>The sentence.</returns>
    private static string At(int position, string wrong)
        => string.Format(CultureInfo.InvariantCulture, "The request in position {0} {1}", position + 1, wrong);

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
    /// Puts the ask against what is already in the queue, and answers in the shape this endpoint
    /// returns.
    /// <para>
    /// Whether an ask joins something or becomes something is <see cref="RequestIntake"/>'s, and it
    /// is there rather than here because the seam to the sibling discover plugin asks the same
    /// question over the same store. What is left here is the translation into
    /// <see cref="RequestOutcome"/>, which is the wire shape this endpoint publishes and is not the
    /// vocabulary the seam answers in.
    /// </para>
    /// </summary>
    /// <param name="incoming">The ask, as a request that does not exist yet.</param>
    /// <param name="caller">
    /// Who is asking. This endpoint hands in an ordinary user whoever they are, because the server
    /// tells it which person is calling and not whether that person administers this server, and a
    /// caller built as an administrator on a guess would exempt somebody from the quota on one.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What the caller is now waiting for and what asking did.</returns>
    private async Task<CreatedRequest> AskAsync(
        MediaRequest incoming,
        RequestCaller caller,
        CancellationToken cancellationToken)
    {
        var intake = await _intake.AskAsync(incoming, caller, cancellationToken).ConfigureAwait(false);

        return new CreatedRequest
        {
            Id = intake.Request.Request.Id,
            State = intake.Request.Request.State,
            Outcome = Reported(intake.Outcome)
        };
    }

    /// <summary>
    /// One intake outcome as this endpoint publishes it.
    /// <para>
    /// Written as a switch with no default arm on purpose. A value added to
    /// <see cref="IntakeOutcome"/> and not answered here fails the build rather than being reported
    /// as whichever arm happened to be written last.
    /// </para>
    /// </summary>
    /// <param name="outcome">What asking did.</param>
    /// <returns>The same thing in the vocabulary this endpoint answers in.</returns>
    /// <exception cref="InvalidOperationException">
    /// Where the outcome is a value nothing here reports.
    /// </exception>
    private static RequestOutcome Reported(IntakeOutcome outcome)
        => outcome switch
        {
            IntakeOutcome.Created => RequestOutcome.Created,
            IntakeOutcome.Joined => RequestOutcome.Joined,
            IntakeOutcome.AlreadyWaiting => RequestOutcome.AlreadyWaiting,

            _ => throw new InvalidOperationException(FormattableString.Invariant(
                $"There is no answer this endpoint reports for the intake outcome {outcome}."))
        };
}

using System;
using System.Globalization;

namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// Thrown when the move is one <see cref="RequestLifecycle.Table"/> allows, but not to this caller.
/// <para>
/// It is a different exception from <see cref="IllegalRequestTransitionException"/> because the two
/// mean different things and are repaired differently. An illegal move is refused to everybody and
/// stays refused; this one says the move is a real move that somebody else may make, and telling an
/// operator that a request "cannot be approved" when what happened is that a user tried to approve
/// their own is the sentence that sends them looking for a bug in the queue.
/// </para>
/// <para>
/// <b>What it says and what it does not.</b> The message names the two states and the callers the
/// table admits for that pair, all of which are printed in the documentation and true of every
/// request in that state. It names nothing about the request it was thrown for: not the title, not
/// the identifier, not who asked, not what they wrote. A refusal is the one message a caller can
/// make the plugin produce about a request they were not allowed to touch, so it is written to be
/// safe to hand back whole. What the caller turned out to be is deliberately absent too: on somebody
/// else's request that value is a fact about the request rather than about the caller.
/// </para>
/// </summary>
public sealed class RequestMoveNotPermittedException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestMoveNotPermittedException"/> class for a
    /// named pair of states and the callers that pair admits.
    /// </summary>
    /// <param name="from">The state the move was out of.</param>
    /// <param name="to">The state the move was into.</param>
    /// <param name="permitted">The callers the table admits for that pair.</param>
    public RequestMoveNotPermittedException(RequestState from, RequestState to, RequestActor permitted)
        : base(Describe(from, to, permitted))
    {
        From = from;
        To = to;
        Permitted = permitted;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestMoveNotPermittedException"/> class.
    /// Present because the analyzers ask every exception for the three ordinary constructors; the
    /// constructor above is the one this type exists for, and it is the only one that can say which
    /// move was refused to whom.
    /// </summary>
    public RequestMoveNotPermittedException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestMoveNotPermittedException"/> class with a
    /// message of the caller's own. See the note on the parameterless constructor.
    /// </summary>
    /// <param name="message">The message.</param>
    public RequestMoveNotPermittedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestMoveNotPermittedException"/> class with a
    /// message and an inner exception. See the note on the parameterless constructor.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public RequestMoveNotPermittedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the state the refused move was out of, or <see cref="RequestState.Open"/> where this was
    /// built by one of the constructors that names no pair.
    /// </summary>
    public RequestState From { get; }

    /// <summary>
    /// Gets the state the refused move was into, or <see cref="RequestState.Open"/> where this was
    /// built by one of the constructors that names no pair.
    /// </summary>
    public RequestState To { get; }

    /// <summary>
    /// Gets the callers the table admits for that pair, or <see cref="RequestActor.None"/> where
    /// this was built by one of the constructors that names no pair. This is a cell of the table
    /// rather than anything about the request, so a surface may show it.
    /// </summary>
    public RequestActor Permitted { get; }

    private static string Describe(RequestState from, RequestState to, RequestActor permitted)
        => string.Format(
            CultureInfo.InvariantCulture,
            "This caller may not move a request from {0} to {1}. That move is made by: {2}.",
            from,
            to,
            permitted);
}

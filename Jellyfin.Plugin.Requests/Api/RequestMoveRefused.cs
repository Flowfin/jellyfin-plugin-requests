namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// A move that was refused, with enough beside it for the caller to do something other than give up.
/// <para>
/// This is what separates a refusal an operator can act on from a failed call. Two administrators
/// working one queue is the ordinary case rather than the exceptional one, and the second of them
/// should be told what the request looks like now, not obeyed and not left guessing.
/// </para>
/// <para>
/// The shape of an error across the whole API, and which status code each class of failure gets, is
/// #56. This carries what refusing a move needs and is expected to be folded into whatever that
/// decides.
/// </para>
/// </summary>
public sealed record RequestMoveRefused
{
    /// <summary>
    /// Gets why the move was refused, as a value a client branches on.
    /// </summary>
    public required RequestMoveRefusal Refusal { get; init; }

    /// <summary>
    /// Gets the same answer as a sentence for the person reading the screen. Where the transition
    /// table refused the move, this is that cell's own reason rather than a restatement of the
    /// verdict, so an operator is told why the move is not available instead of that it is not.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Gets the request as the store holds it now, where that is known.
    /// <para>
    /// It is here so a refusal is one round trip rather than two: the client redraws the row from
    /// this and the operator decides against what is actually there. It is absent where the store no
    /// longer holds the request at all, which is a real answer and not a missing field.
    /// </para>
    /// </summary>
    public QueuedRequest? Current { get; init; }
}

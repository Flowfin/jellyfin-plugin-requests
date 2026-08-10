namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// Why a move was refused, as a value a caller can branch on rather than a sentence it has to read.
/// <para>
/// A page that can only show the message has one behaviour for every refusal. These four want three
/// different ones: redraw the row and let the operator decide again, tell them this move is not
/// available on this request at all, or say the request has to be declined because nothing can be
/// done with it. The sentence beside the value is for the person, and this is for the client.
/// </para>
/// <para>
/// A caller must treat a value it does not recognise as one it does not recognise, per the rule in
/// <c>docs/api.md</c>. Adding a value here is not a breaking change; a caller switching exhaustively
/// over the values that existed the day it was written is a caller this API promises nothing to.
/// </para>
/// </summary>
public enum RequestMoveRefusal
{
    /// <summary>
    /// Somebody moved the request between the caller reading it and the caller acting on it. The
    /// current row comes back with this, so the client can draw what is there now and let the
    /// operator decide against it.
    /// </summary>
    MovedSinceItWasRead = 0,

    /// <summary>
    /// The transition table refuses this move from the state the request is in. Approving something
    /// already fulfilled is the ordinary case, and the reason beside it is the table's own sentence
    /// for that cell.
    /// </summary>
    TheTableRefusesTheMove = 1,

    /// <summary>
    /// The request carries no external identifier, so nothing downstream can act on it and the only
    /// move it has is a decline. This is a title somebody typed, and approving it would be an
    /// operator saying yes to something that then sits still forever.
    /// </summary>
    TheRequestNamesNothing = 2,

    /// <summary>
    /// The table allows this move and does not admit this caller for it.
    /// </summary>
    TheCallerMayNotMakeThisMove = 3
}

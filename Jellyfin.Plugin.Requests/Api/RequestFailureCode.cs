namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// Why a call failed, as a value a caller branches on rather than a sentence it has to read.
/// <para>
/// One value per way this API says no, and each one has exactly one status code, in
/// <see cref="RequestFailure.StatusFor"/>. Error handling written per endpoint ends as five shapes
/// and a stack trace, and a caller cannot then tell a refused move from a missing request from a
/// wrong body; every one of them arrives as "something went wrong" and the client shows the same
/// dialog for all three.
/// </para>
/// <para>
/// A caller must treat a value it does not recognise as one it does not recognise, per the rule in
/// <c>docs/api.md</c>, and must fall back on the status code, which is why every code has one that
/// is right on its own. Adding a value here is not a breaking change.
/// </para>
/// </summary>
public enum RequestFailureCode
{
    /// <summary>
    /// The body cannot become what it is for. <see cref="RequestFailure.Field"/> names the field
    /// that is wrong, so a client can put the message beside the box somebody typed in rather than
    /// having to read English to work out which one.
    /// </summary>
    InvalidBody = 0,

    /// <summary>
    /// The call authenticated and names no person, which is what an API key looks like from an
    /// endpoint. A request has to be attributable to somebody to exist, and a decision is
    /// somebody's, so there is nothing to record either way.
    /// </summary>
    NoUserOnTheCall = 1,

    /// <summary>
    /// The store holds no request with that identifier. Nothing about who it belonged to, or
    /// whether it ever existed, is said.
    /// </summary>
    NoSuchRequest = 2,

    /// <summary>
    /// Somebody moved the request between the caller reading it and the caller acting on it.
    /// <see cref="RequestFailure.Current"/> carries what the store holds now, so the client can draw
    /// what is there and let the operator decide against it.
    /// </summary>
    MovedSinceItWasRead = 3,

    /// <summary>
    /// The transition table refuses this move from the state the request is in, and the message is
    /// that cell's own sentence.
    /// </summary>
    TheTableRefusesTheMove = 4,

    /// <summary>
    /// The request carries no external identifier, so nothing downstream can act on it and the only
    /// move it has is a decline. Separate from <see cref="TheTableRefusesTheMove"/> because the cell
    /// is fine and what stands in the way is the request, and a caller told the table refuses it
    /// would be told something false.
    /// </summary>
    TheRequestNamesNothing = 5,

    /// <summary>
    /// The table allows this move and does not admit this caller for it.
    /// </summary>
    TheCallerMayNotMakeThisMove = 6,

    /// <summary>
    /// The store could not be read. It is here because the alternative is an exception escaping an
    /// endpoint, and the exception this plugin's store raises names the file it could not read; a
    /// path on the server's disk is exactly what an error may not carry. The caller is told the
    /// answer is unavailable rather than that its call was wrong, because its call was not.
    /// </summary>
    TheStoreCouldNotBeRead = 7
}

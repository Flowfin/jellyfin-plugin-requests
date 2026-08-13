using System;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// The one shape every failure of this API comes back in.
/// <para>
/// One shape rather than one per endpoint. A caller writes the handling once, and a failure it has
/// never seen before still parses and still says which of the ways this API says no it is. What that
/// costs is a field that is absent on most codes, which is cheaper than the alternative: a client
/// that reads five shapes reads four of them from an example rather than from a contract.
/// </para>
/// <para>
/// <b>Nothing in a message names a person, a path on the server's disk, or an exception.</b> The
/// messages are written for the operator or the user who will read them, and
/// <c>ErrorSurfaceTests</c> walks every failure this API can produce and refuses any of the three.
/// </para>
/// </summary>
public sealed record RequestFailure
{
    /// <summary>
    /// Gets what went wrong, as a value a client branches on. The status code says the same thing
    /// coarsely, for a client that does not know this enumeration.
    /// </summary>
    public required RequestFailureCode Code { get; init; }

    /// <summary>
    /// Gets the same answer as a sentence for the person reading the screen. It says why rather than
    /// restating the verdict, so somebody refused can do something other than try again.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the field that is wrong, named as the body names it, where the code is
    /// <see cref="RequestFailureCode.InvalidBody"/>. Absent otherwise.
    /// </summary>
    public string? Field { get; init; }

    /// <summary>
    /// Gets the request as the store holds it now, where the failure is about a request that is
    /// there and one the caller may read in full. Absent otherwise, and absent rather than empty:
    /// there is no code whose meaning depends on telling a null apart from a row nobody filled in.
    /// </summary>
    public QueuedRequest? Current { get; init; }

    /// <summary>
    /// The status code one failure is reported with.
    /// <para>
    /// One rule per class, in one place, so the pair cannot drift: a code returned under two status
    /// codes by two endpoints is a caller branching on the status for one of them and on the code
    /// for the other, and the first of those breaks the day the second endpoint is the one it meets.
    /// </para>
    /// </summary>
    /// <param name="code">The failure.</param>
    /// <returns>The status code it is reported with.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Where the code is not one this knows, which can only happen if a value was added to
    /// <see cref="RequestFailureCode"/> without adding its line here.
    /// </exception>
    public static int StatusFor(RequestFailureCode code) => code switch
    {
        // The caller sent something that cannot be acted on. It is the caller's to fix and the
        // field says where.
        RequestFailureCode.InvalidBody => StatusCodes.Status400BadRequest,

        // Authenticated, and there is no person behind it. Nothing the caller can put in the body
        // changes that, which is what separates it from the code above.
        RequestFailureCode.NoUserOnTheCall => StatusCodes.Status403Forbidden,

        RequestFailureCode.NoSuchRequest => StatusCodes.Status404NotFound,

        // The three ways a request is not in the state the call assumed. A conflict rather than a
        // bad request: the call was well formed and the world moved, or the request is not one this
        // move applies to.
        RequestFailureCode.MovedSinceItWasRead => StatusCodes.Status409Conflict,
        RequestFailureCode.TheTableRefusesTheMove => StatusCodes.Status409Conflict,
        RequestFailureCode.TheRequestNamesNothing => StatusCodes.Status409Conflict,

        RequestFailureCode.TheCallerMayNotMakeThisMove => StatusCodes.Status403Forbidden,

        // A conflict as well, and for the reason the three above are: the call is well formed and
        // the state of this person's queue refuses it. A 403 would say they may not ask, which is
        // not true tomorrow, or after an operator answers one of the things they are waiting for.
        RequestFailureCode.TheyAreAtTheirQuota => StatusCodes.Status409Conflict,

        // Nothing is wrong with the call and the answer is not available. A 500 would say this
        // plugin broke, which is one of the two possibilities and not the likely one: a store that
        // cannot be read is usually a disk or a file, and telling an operator to try again later is
        // the true statement.
        RequestFailureCode.TheStoreCouldNotBeRead => StatusCodes.Status503ServiceUnavailable,

        // Unavailable for the same reason and not a 500: the call was fine, this server is set to
        // something the plugin cannot run on, and an operator fixing the settings makes the same
        // call work.
        RequestFailureCode.ThisInstallCannotRun => StatusCodes.Status503ServiceUnavailable,

        _ => throw new ArgumentOutOfRangeException(
            nameof(code),
            $"There is no status code for {code}. A value was added to RequestFailureCode without adding its line here.")
    };
}

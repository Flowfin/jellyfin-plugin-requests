namespace Jellyfin.Plugin.Requests.Seam;

/// <summary>
/// Why a handover was not turned into a request.
/// <para>
/// The contract allows this side to answer one thing, which is whether the handover was accepted, so
/// none of this reaches the plugin that called. It exists because the reason has to reach somebody:
/// an operator whose users report that nothing happens when they ask for a film needs to be able to
/// find out that this install does not accept series, rather than to discover it by reading the
/// source.
/// </para>
/// <para>
/// Where the operator reads it is the server's log, which is the honest limit of this today. A page
/// that says what this install is refusing and why is the diagnostics view in #63.
/// </para>
/// </summary>
public enum HandoverRefusal
{
    /// <summary>
    /// The field set was built against a version of the contract this plugin does not know.
    /// </summary>
    ContractVersionNotKnown = 0,

    /// <summary>
    /// The want named no user, so there would be nobody to record as having asked.
    /// </summary>
    NoUserNamed = 1,

    /// <summary>
    /// The user the want named is not a user this server has. The identifier could not be verified
    /// as belonging to the person who asked, which is the trust position <c>docs/seam.md</c> states,
    /// but it can be checked against the server's own users and this one is not among them.
    /// </summary>
    UserNotOnThisServer = 8,

    /// <summary>
    /// The field set carried no identifier for the want itself, so a repeat of it could never be
    /// recognised as one and every arrival would be a new request.
    /// </summary>
    NoWantNamed = 7,

    /// <summary>
    /// The want named no title, and a request carries the title as it read when it was asked for.
    /// </summary>
    NoTitle = 2,

    /// <summary>
    /// The want named something that is not a kind of thing this plugin knows.
    /// </summary>
    KindNotRecognised = 3,

    /// <summary>
    /// The want named a kind this install is set not to accept.
    /// </summary>
    KindNotAccepted = 4,

    /// <summary>
    /// The queue could not be read or written, so nothing could be decided about this want.
    /// </summary>
    TheStoreCouldNotBeReached = 5,

    /// <summary>
    /// What this install is set to is something the plugin cannot run on, so no want can be judged
    /// against it. That is a fault on this server rather than anything the other side sent.
    /// </summary>
    ThisInstallCannotRun = 6,

    /// <summary>
    /// Nothing arrived to make a request of. The call carried no field set at all, which is a defect
    /// in the caller rather than a want that could not be taken, and it is answered the same way
    /// because the alternative is an exception crossing a plugin boundary.
    /// </summary>
    NothingWasHandedOver = 9,

    /// <summary>
    /// The queue was still deciding when this side ran out of the time it gives itself. The want is
    /// not lost by it: the other side hands the same identifier over again and the repeat is
    /// recognised, which is the whole reason this side is allowed to stop waiting.
    /// </summary>
    TheStoreDidNotAnswerInTime = 10,

    /// <summary>
    /// Something under this seam failed in a way nothing here expected. It is a refusal rather than
    /// an exception for the same reason every other entry is, and what actually went wrong is on
    /// this server's log at error level with the fault itself.
    /// </summary>
    SomethingBeneathThisSeamFailed = 11,

    /// <summary>
    /// The person the want names is already waiting for as many open or approved requests as this
    /// install allows. A want arriving over the seam is somebody asking for something, so it is
    /// bound by the same quota as an ask over the endpoint; a path that was not would be the way
    /// around the limit rather than a second way to ask.
    /// </summary>
    TheyAreAtTheirQuota = 12
}

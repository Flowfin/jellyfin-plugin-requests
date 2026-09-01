namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// Why a request was declined, from a short list.
/// <para>
/// A reason is required on every decline, decided on #113. A decline with no reason reads as
/// arbitrary to the person who asked, and the thing they do next is ask for the same title again,
/// because nothing told them what was wrong with the first attempt.
/// </para>
/// <para>
/// The list is short on purpose. It is chosen by an operator making the same decision for the tenth
/// time that evening, and a list long enough to need reading is a list that gets whatever is at the
/// top. Anything the list does not cover is <see cref="Other"/>, which requires the free text beside
/// it, so the escape hatch cannot be used to give no reason at all.
/// </para>
/// <para>
/// These are the values a surface offers and a notification reads. Adding one is cheap; removing one
/// is not, because requests already declined carry it.
/// </para>
/// </summary>
public enum DeclineReason
{
    /// <summary>
    /// Not one of the reasons below. The free text beside it says what happened, and a decline
    /// carrying this value without that text is refused.
    /// </summary>
    Other = 0,

    /// <summary>
    /// The server already has it. Usually a search that missed, or a title under a different name,
    /// and the person who asked can go and watch it now.
    /// </summary>
    AlreadyInTheLibrary = 1,

    /// <summary>
    /// Somebody already asked for this and that request is the live one. Points at a request rather
    /// than at a refusal, which is what separates it from the reasons below.
    /// </summary>
    AlreadyRequested = 2,

    /// <summary>
    /// Nothing can be found to fetch. The title exists and this server has no way to get it, which
    /// is a different answer from not wanting it.
    /// </summary>
    CannotBeObtained = 3,

    /// <summary>
    /// The operator does not want it on this server. The honest reason for a decision that is
    /// somebody's choice rather than a fact about availability, and one an operator should be able
    /// to give without dressing it as something else.
    /// </summary>
    NotWanted = 4,

    /// <summary>
    /// There is no room for it. Separate from <see cref="NotWanted"/> because it is a fact about the
    /// disk rather than a judgement about the title, and asking again later is reasonable.
    /// </summary>
    NoRoomForIt = 5,

    /// <summary>
    /// The person who asked for it is gone. Their Jellyfin account was deleted while this request
    /// was still open, so there is nobody left to give it to and nobody left to tell.
    /// <para>
    /// <b>This is the one reason no operator chooses.</b> Every value above is a sentence somebody
    /// decided; this one is a fact the plugin establishes when the server tells it an account has
    /// been deleted. <see cref="RequestLifecycle"/> refuses it from an administrator and refuses
    /// every other reason from the plugin, so which of the two made a decline is readable off the
    /// reason as well as off the history entry beside it.
    /// </para>
    /// <para>
    /// <b>Why this is a reason on a state that exists rather than a sixth state.</b> The ruling of
    /// 2026-08-28 on #49 asks for such a request to be closed rather than removed, and #337 answered
    /// what "closed" is here: the terminal state this plugin already has. A withdrawn-shaped
    /// <see cref="RequestState"/> was considered and refused on #113, and that refusal stands. What
    /// the surfaces need is a finished request whose reason says why, and a reason is exactly what
    /// this list is for - a request declined for this reason reads correctly on every surface that
    /// already renders a decline, and a sixth state would have needed a cell in
    /// <see cref="RequestLifecycle.Table"/> against every other state and a rendering rule in each
    /// surface.
    /// </para>
    /// </summary>
    TheRequesterIsGone = 6
}

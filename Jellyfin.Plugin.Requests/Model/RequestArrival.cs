namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// How an ask reached this plugin.
/// <para>
/// Two surfaces make requests and they carry different amounts of proof. The HTTP endpoint takes the
/// requester from the authenticated session, so the person named on the request is the person the
/// server says is calling. The seam has no session: the caller is another plugin in the same process
/// and it passes a user identifier this side cannot verify, which is the trust position stated in
/// <c>docs/seam.md</c> and repeated in <c>docs/personal-data.md</c>. An operator asked to answer for
/// a request needs to know which of the two it was, and until this existed the record said nothing
/// about it.
/// </para>
/// <para>
/// <b>Three values over two surfaces.</b> The seam carries two of them, because a want the other
/// side recorded earlier and one somebody is expressing now cross on the same call and are
/// different things to an operator meeting a queue that filled up overnight. Which of the two it is
/// comes from the marker the contract carries for it and from nothing this side infers; #93 is
/// where that was asked for and the sibling's own contract note is where the field is fixed.
/// </para>
/// <para>
/// <b>It says how, and never who.</b> Which plugin handed a want over is not recorded, decided on
/// #118: the contract carries no field naming the caller, and reading one off an assembly name or a
/// call stack would be this side inventing a value nobody agreed. A history that records an
/// unverified self-declaration as fact is worse than one that records less.
/// </para>
/// </summary>
public enum RequestArrival
{
    /// <summary>
    /// Another plugin in this server process handed a want across the seam. The person named on the
    /// request is whoever that caller said asked for it, and no session stands behind it.
    /// <para>
    /// It is the zero value on purpose, in the same direction as
    /// <see cref="RequestActor.None"/>: a value nobody filled in reads as the arrival this side
    /// could not check rather than as the one it could, so a defect that loses the value understates
    /// what is known instead of claiming a session that was never there.
    /// </para>
    /// </summary>
    Seam = 0,

    /// <summary>
    /// Somebody asked over this plugin's own HTTP endpoint, on a session the server authenticated.
    /// </summary>
    Endpoint = 1,

    /// <summary>
    /// A want the other side had already recorded, handed over the seam when a replay was run rather
    /// than at the moment somebody expressed it.
    /// <para>
    /// It is a third value rather than a flag beside <see cref="Seam"/> because the question an
    /// operator arrives with is one question - how did this reach the queue - and two fields
    /// answering it is two fields to read and one of them to forget. The proof position is the
    /// seam's in both cases and nothing about it is softened here.
    /// </para>
    /// <para>
    /// <b>What it costs to read it as the moment somebody asked.</b> The moment on the entry is when
    /// the replay ran, because that is the only moment this side ever sees; the other side holds
    /// when the want was recorded and the contract carries no field for it. So a queue that filled
    /// up at once reads as a queue that filled up at once, and this value is what says the filling
    /// was a replay rather than a morning of people asking.
    /// </para>
    /// <para>
    /// A build of the sibling from before the marker existed hands every want over without one, so
    /// its replays land as <see cref="Seam"/>. That is absence read as absence: the contract's
    /// marker says the unusual thing and says nothing where it is not sent, so this side records
    /// what it was told rather than guessing at a replay it has no evidence of.
    /// </para>
    /// </summary>
    SeamReplay = 2
}

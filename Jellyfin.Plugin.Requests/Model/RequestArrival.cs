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
    Endpoint = 1
}

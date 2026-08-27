using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Requests.Configuration;

/// <summary>
/// What an operator can change about this install, and what it does when they change nothing.
/// <para>
/// <b>Almost every install runs the defaults, so the defaults are the product.</b> Each one below is
/// the conservative answer: a bounded number of open requests rather than none, the two media kinds
/// this version knows how to recognise in a library, and a retention period that keeps a year of
/// history rather than everything forever. <c>docs/configuration.md</c> carries the same list with
/// the reason each default is the safe one, and a test compares the two.
/// </para>
/// <para>
/// <b>What is deliberately not here is as much of the design as what is.</b> A setting nobody can
/// act on yet is a shape handed to whoever implements the thing behind it, and this class started
/// empty precisely so that would not happen twice.
/// </para>
/// <para>
/// There is no automatic-approval setting. Approval is required, decided on #113, and automatic
/// approval arrives there as a per-user setting rather than a switch for the whole server, so a
/// boolean added now would be the wrong shape rather than an early version of the right one.
/// </para>
/// <para>
/// The notification switches below name the three movements this plugin announces outward, and one
/// more names the arrival this plugin tells a live administrator about. A switch for a path that
/// does not exist is a setting an operator can change with no effect, so the set grows when a path
/// does rather than ahead of one, and this is that rule applied rather than an exception to it.
/// </para>
/// <para>
/// There is no bridge address and no credential. The only implementation of the bridge in this tree
/// is the one for a server that has none, so a field for an address would be a place to type
/// something nothing reads. No issue on this board asks for the adapter that would need one, and
/// #85 decides where a credential is kept and what may be claimed about it once there is one.
/// </para>
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// The shortest retention period an operator may set, in days.
    /// <para>
    /// It is a constant here rather than a number written into the sweep and again into the
    /// validation, because two copies of a floor are two answers the day one is changed. Nothing in
    /// this class refuses a value below it: a configuration class is data the dashboard writes, and
    /// refusing what cannot work is #96.
    /// </para>
    /// <para>
    /// The floor exists so that retention cannot be set to nothing. Zero would delete the history
    /// silently and leave a queue that answers "was this asked for before" with no, which is the
    /// one question a year of history is kept for.
    /// </para>
    /// </summary>
    public const int MinimumRetentionDays = 30;

    /// <summary>
    /// Gets or sets how many requests one person may have open at once.
    /// <para>
    /// Bounded rather than unlimited, because the quota is the only thing standing between one user
    /// and the whole disk, and a limit introduced later has to be enforced against habits people
    /// already have. Ten is a number somebody has to be trying to reach, and an operator who wants
    /// more moves one field.
    /// </para>
    /// <para>
    /// It counts open requests and not requests ever made, so a person whose asks are answered can
    /// keep asking. Where this is enforced is #114.
    /// </para>
    /// </summary>
    public int OpenRequestsPerUser { get; set; } = 10;

    /// <summary>
    /// Gets or sets a value indicating whether films may be asked for.
    /// </summary>
    public bool AcceptsMovies { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether series may be asked for.
    /// <para>
    /// A kind is a setting of its own rather than a list, so a third kind is a visible change in the
    /// three places that have to move together: this class, the page and the document. A list would
    /// let one arrive by being appended somewhere and reach a fulfilment check that has no rule for
    /// it.
    /// </para>
    /// </summary>
    public bool AcceptsSeries { get; set; } = true;

    /// <summary>
    /// Gets or sets how long a finished request is kept, in days.
    /// <para>
    /// A request record says a named person asked for a named title on a date, which is more
    /// revealing than most of what a media server holds, and it accumulates forever unless something
    /// removes it. A year is the span in which somebody asks "did I already ask for this", which is
    /// what the history is kept for at all.
    /// </para>
    /// <para>
    /// The number is here rather than in the code, decided on #113, so an operator with a different
    /// answer changes a field instead of asking for a release. <see cref="MinimumRetentionDays"/> is
    /// the floor under it, and what removes an expired request is <see cref="Storage.RetentionSweep"/>.
    /// </para>
    /// </summary>
    public int FinishedRequestRetentionDays { get; set; } = 365;

    /// <summary>
    /// Gets or sets a value indicating whether administrators signed in at that moment are told that
    /// somebody has asked for something.
    /// <para>
    /// False, and off is the shipping state rather than a degraded one. Nothing on either claimed
    /// server line listens for this document, which <c>docs/notifications.md</c> measures rather than
    /// asserts, so an install that sent it by default would push a message at every administrator's
    /// client on every arrival and no client would do anything with it. An operator running
    /// something written against the contract turns it on.
    /// </para>
    /// <para>
    /// It is a switch of its own rather than one of the three below it. Those three narrow what
    /// leaves the machine on the outbound sink, and this leaves nothing: it goes down connections
    /// the server already holds to clients already signed in. Sharing a switch with the sink would
    /// make turning off what a chat service receives also turn off what an operator's own dashboard
    /// is handed, which are different decisions.
    /// </para>
    /// </summary>
    public bool TellsAdministratorsAboutArrivals { get; set; }

    /// <summary>
    /// Gets or sets where a notice about a request is posted, or nothing to send none.
    /// <para>
    /// Empty on a fresh install, and empty is not a degraded setting: it is the whole of how the
    /// outbound path is turned off, and it is off on every install where nobody has decided
    /// otherwise. There is no second switch beside it, because two ways to express off is one of
    /// them being wrong the day somebody sets the other.
    /// </para>
    /// <para>
    /// <b>This is the one field in this class that sends anything anywhere.</b> What leaves the
    /// server when it is set is written out in <c>docs/notifications.md</c> and counted in
    /// <c>docs/personal-data.md</c>, because an operator answering for their users needs to read
    /// what a value here costs before they type one rather than afterwards.
    /// </para>
    /// <para>
    /// It is an address and never a credential. Anything a sink needs to authenticate with belongs
    /// where #85 decides credentials are kept, and typing one into a query string here would put it
    /// in this plugin's configuration file, in the settings page's markup and in every log line that
    /// ever prints the address.
    /// </para>
    /// </summary>
    public string OutboundNoticeAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether an approval is announced to the address above.
    /// <para>
    /// True, and the three switches below it are true for the same reason: what turns the outbound
    /// path on is the address, and an operator who has just typed one expects the thing they turned
    /// on to work. Defaulting these to false would make an install with an address set and nothing
    /// arriving the ordinary case, which is a second way to express off and the wrong one of the two.
    /// </para>
    /// <para>
    /// So a fresh install still sends nothing outward, because there is nowhere to send it, and these
    /// are what an operator narrows a sink with once it has somewhere to go.
    /// </para>
    /// </summary>
    public bool AnnouncesApprovals { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a decline is announced to the address above.
    /// <para>
    /// It is a switch of its own rather than sharing one with an approval, because the two are
    /// different messages to an operator's automation: a decline is the answer somebody may have to
    /// explain, and an install that wants only the yeses forwarded is a real install.
    /// </para>
    /// <para>
    /// The reason a request was declined is not on the wire whichever way this is set, for the
    /// reason <see cref="Notify.OutboundNotice"/> gives: it is free text somebody typed.
    /// </para>
    /// </summary>
    public bool AnnouncesDeclines { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a fulfilment is announced to the address above.
    /// <para>
    /// This is the noisy one on a server whose library is filling, because nobody decides it: the
    /// sweep moves a request the moment the title turns up, so the volume follows the library rather
    /// than the operator. It is the switch most likely to be turned off, which is why it is one.
    /// </para>
    /// </summary>
    public bool AnnouncesFulfilments { get; set; } = true;
}

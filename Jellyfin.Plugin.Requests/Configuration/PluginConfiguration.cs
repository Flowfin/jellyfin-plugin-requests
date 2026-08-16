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
/// There are no notification switches. One path is built and it is turned on by being pointed
/// somewhere, which is <see cref="OutboundNoticeAddress"/> below; the rest are unbuilt, and #79 is
/// where the switches land beside the paths they turn off rather than ahead of them.
/// </para>
/// <para>
/// There is no bridge address and no credential. The only implementation of the bridge in this tree
/// is the one for a server that has none, so a field for an address would be a place to type
/// something nothing reads. #82 brings the adapter and #85 decides where a credential is kept and
/// what may be claimed about it.
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
    /// the floor under it, and what removes an expired request is #49.
    /// </para>
    /// </summary>
    public int FinishedRequestRetentionDays { get; set; } = 365;

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
}

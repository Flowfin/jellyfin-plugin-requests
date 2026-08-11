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
/// There are no notification switches. Every path they would switch is unbuilt, a fresh install
/// sends nothing outward because there is nothing that could, and #79 is where the switches land
/// beside the paths they turn off.
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
}

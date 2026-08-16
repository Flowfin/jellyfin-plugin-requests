using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Requests.Notify;

/// <summary>
/// Where a move a request made is written down so it survives a restart.
/// <para>
/// This is the record and <see cref="IOutboundSink"/> is the courtesy, which is why the two have
/// different shapes. A notice can be lost and nothing says so; an entry here is the answer to what
/// the server did, an operator reads it in a dashboard that is already in front of them, and it is
/// there whether or not anybody was connected when it happened.
/// </para>
/// <para>
/// <b>Writing an entry never raises.</b> A move that has already been written to the store is a
/// decision that has been taken, and an activity log that is unavailable is not a reason to answer
/// the operator that their decision failed. The implementation that reaches the server logs its own
/// failure and returns, which is the one place that trade is made rather than a thing every caller
/// has to remember; what a caller owes instead is to write the entry only after the store accepted
/// the move, so an entry never claims something that did not happen.
/// </para>
/// </summary>
public interface IActivityJournal
{
    /// <summary>
    /// Writes one entry.
    /// </summary>
    /// <param name="note">What to say, built by <see cref="ActivityNote.For"/>.</param>
    /// <param name="cancellationToken">Gives up writing.</param>
    /// <returns>A task that completes when the entry has been written or given up on.</returns>
    Task WriteAsync(ActivityNote note, CancellationToken cancellationToken);
}

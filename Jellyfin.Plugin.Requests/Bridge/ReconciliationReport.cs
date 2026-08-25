namespace Jellyfin.Plugin.Requests.Bridge;

/// <summary>
/// What one reconciliation run looked at and what it did, as the answer the run hands back.
/// <para>
/// It exists so a test can assert a run's shape rather than reading a log for it, and so the task
/// above can say nothing at all: a scheduled task that swallows its own outcome is where a bridge
/// silently stops reconciling.
/// </para>
/// </summary>
public sealed record ReconciliationReport
{
    /// <summary>
    /// Gets a value indicating whether the run asked the service anything at all. False on an
    /// install with no service configured and on one whose service did not answer, which are two
    /// different reasons and are told apart by <see cref="Reachability"/>.
    /// </summary>
    public required bool Asked { get; init; }

    /// <summary>
    /// Gets what the bridge said when it was asked whether it was there.
    /// </summary>
    public required BackendReachability Reachability { get; init; }

    /// <summary>
    /// Gets how many handed-over requests this run asked the service about.
    /// </summary>
    public int Examined { get; init; }

    /// <summary>
    /// Gets how many requests this run moved.
    /// </summary>
    public int Moved { get; init; }

    /// <summary>
    /// Gets how many answers this run refused to act on: a word the mapping table has never seen, a
    /// move the transition table does not allow, and a request that moved underneath the run. Each
    /// one is a line in the log naming which request it was about.
    /// </summary>
    public int Refused { get; init; }
}

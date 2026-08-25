using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Requests.Bridge;

/// <summary>
/// What makes the reconciliation happen without an operator remembering, which is the half of #83's
/// first condition a loop in a class cannot be.
/// <para>
/// It runs on a schedule and at startup. The schedule is what keeps the queue from drifting on a
/// server nobody is watching; startup is what a server that was switched off for a week needs,
/// because everything the service decided while the machine was off is waiting to be asked about.
/// </para>
/// <para>
/// Hourly, and the interval is stated rather than tuned. What it costs is one call per handed-over
/// request against a service on the same machine or the same network, and what it buys is a queue
/// no more than an hour behind. A shorter interval would ask a service that has not changed the same
/// question more often; a longer one is a page an operator finds wrong often enough to stop reading.
/// </para>
/// <para>
/// <b>On the ordinary install it does nothing and costs one call.</b> Most servers run no external
/// service, and the run ends at the reachability check rather than walking the store. The task is
/// registered on every install anyway, for the reason the bridge itself is: whether a service exists
/// is a value in a file an operator edits while the server is running, and a task list built at
/// startup would answer with whatever was true then.
/// </para>
/// </summary>
public sealed class ReconciliationTask : IScheduledTask
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly BridgeReconciliation _reconciliation;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReconciliationTask"/> class.
    /// </summary>
    /// <param name="reconciliation">The one thing that asks the service and applies what it says.</param>
    /// <exception cref="ArgumentNullException">Where no reconciliation was given.</exception>
    public ReconciliationTask(BridgeReconciliation reconciliation)
    {
        ArgumentNullException.ThrowIfNull(reconciliation);

        _reconciliation = reconciliation;
    }

    /// <inheritdoc />
    public string Name => "Ask the external request service where handed-over requests stand";

    /// <inheritdoc />
    public string Description =>
        "Asks whatever else on this server fetches media about the requests handed to it, and applies what it says through the mapping table. It does nothing on a server that has no such service.";

    /// <inheritdoc />
    public string Category => "Requests";

    /// <summary>
    /// Gets the identifier the server keeps this task's schedule under. Written out rather than
    /// derived from the type name, so renaming the class does not silently discard an interval an
    /// operator changed.
    /// </summary>
    public string Key => "RequestsBridgeReconciliation";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger },
        new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = Interval.Ticks }
    ];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        progress.Report(0);

        await _reconciliation.ReconcileAsync(cancellationToken).ConfigureAwait(false);

        progress.Report(100);
    }
}

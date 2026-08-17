using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Requests.Storage;

/// <summary>
/// What makes the retention period happen without an operator remembering, which is the half of
/// #49's second condition a number in a settings file cannot be.
/// <para>
/// It runs on a schedule and at startup. The schedule is what an unattended server needs; startup
/// is what a server that was switched off for a month needs, because a period that only elapses
/// while the machine is running is not the period an operator set.
/// </para>
/// <para>
/// Daily, and the interval is stated rather than tuned. The shortest period this plugin will accept
/// is thirty days, so a run a day removes a record within a day of its period ending, which is as
/// close as anything short of a timer per request gets and is a walk of the store rather than a
/// watch on it.
/// </para>
/// </summary>
public sealed class RetentionTask : IScheduledTask
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private readonly RetentionSweep _sweep;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetentionTask"/> class.
    /// </summary>
    /// <param name="sweep">The one thing that decides what has been kept long enough.</param>
    /// <exception cref="ArgumentNullException">Where no sweep was given.</exception>
    public RetentionTask(RetentionSweep sweep)
    {
        ArgumentNullException.ThrowIfNull(sweep);

        _sweep = sweep;
    }

    /// <inheritdoc />
    public string Name => "Remove finished requests that have been kept long enough";

    /// <inheritdoc />
    public string Description =>
        "Deletes requests that were fulfilled, declined or failed longer ago than this install keeps them for.";

    /// <inheritdoc />
    public string Category => "Requests";

    /// <summary>
    /// Gets the identifier the server keeps this task's schedule under. Written out rather than
    /// derived from the type name, so renaming the class does not silently discard an interval an
    /// operator changed.
    /// </summary>
    public string Key => "RequestsRetention";

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

        await _sweep.SweepAsync(cancellationToken).ConfigureAwait(false);

        progress.Report(100);
    }
}

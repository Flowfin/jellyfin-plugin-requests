using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Requests.Fulfilment;

/// <summary>
/// The scheduled half of #42: a run the server starts on its own, so a library that changed while
/// this plugin was not listening is still noticed.
/// <para>
/// It exists because the event path cannot be complete. A server that was stopped when the file
/// arrived gets no event for it, an event raised while the plugin was loading is one nobody was
/// subscribed for, and an episode landing under a series that is already in the library moves what
/// a partly satisfied request is waiting for without the series itself changing. None of those is
/// an edge case on a machine that gets restarted, and each of them is a request that would sit open
/// forever with the event path alone.
/// </para>
/// <para>
/// It is deliberately not the only path either. Waiting hours to be told that the thing you asked
/// for arrived is the delay that makes people ask an operator instead, which is what this plugin
/// exists to stop.
/// </para>
/// </summary>
public sealed class FulfilmentTask : IScheduledTask
{
    /// <summary>
    /// How often the server runs this on its own. Six hours is a compromise stated rather than
    /// tuned: the event path is what makes an arrival prompt, and this run is the one that catches
    /// what the event path missed, so it is set to notice a missed arrival within a day without
    /// walking the store every few minutes for nothing.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly FulfilmentSweep _sweep;

    /// <summary>
    /// Initializes a new instance of the <see cref="FulfilmentTask"/> class.
    /// </summary>
    /// <param name="sweep">The one thing that looks at the library, shared with the event path.</param>
    /// <exception cref="ArgumentNullException">Where no sweep was given.</exception>
    public FulfilmentTask(FulfilmentSweep sweep)
    {
        ArgumentNullException.ThrowIfNull(sweep);

        _sweep = sweep;
    }

    /// <inheritdoc />
    public string Name => "Check requests against the library";

    /// <inheritdoc />
    public string Description =>
        "Looks at what the library holds for every request and moves the ones that have arrived to fulfilled.";

    /// <inheritdoc />
    public string Category => "Requests";

    /// <summary>
    /// Gets the identifier the server keeps this task's schedule under. It is written out rather
    /// than derived from the type name, because a rename of the class would otherwise silently
    /// discard an interval an operator had changed.
    /// </summary>
    public string Key => "RequestsLibraryFulfilment";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        // Startup as well as the interval, because the gap this task exists to close is the one a
        // server that was off has just been through.
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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Fulfilment;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Time;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// The answer to "is this thing working", for the operator whose queue has stopped moving.
/// <para>
/// A controller of its own rather than an action beside the queue. What it reads is the plugin
/// rather than the requests: the sweep, the bridge and the store's own state, and giving the queue
/// those dependencies would make every test of a queue answer carry a sweep.
/// </para>
/// <para>
/// <b>Elevated, and not because the numbers are secret.</b> They are counts of other people's
/// requests, and the rule this board keeps is that what one person learns about another's request is
/// nothing. A total is a disclosure like any other row, so this sits where the queue sits.
/// </para>
/// <para>
/// <b>It answers 200 on a broken install.</b> A store that cannot be read is reported as a field and
/// never as a refusal, because an endpoint that failed when the plugin is unhealthy would go quiet
/// at exactly the moment somebody is reading it to find out why.
/// </para>
/// </summary>
[Authorize]
public sealed class HealthController : RequestsControllerBase
{
    private readonly IRequestStore _store;
    private readonly IRequestBackend _backend;
    private readonly FulfilmentSweep _sweep;
    private readonly BridgeWatch _watch;
    private readonly IClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthController"/> class.
    /// </summary>
    /// <param name="store">Where requests are kept, which is asked for the counts and for when it last wrote.</param>
    /// <param name="backend">The bridge, which on most servers is the one with nothing behind it.</param>
    /// <param name="sweep">The fulfilment sweep, which is asked what its last full run did.</param>
    /// <param name="watch">
    /// Where the last moment the bridge answered is kept. It is a singleton rather than state on
    /// this type, because a controller is built per call and would forget between two of them.
    /// </param>
    /// <param name="clock">The injected clock, so the moment a bridge answered is one a test can set.</param>
    /// <exception cref="ArgumentNullException">Where anything it needs is missing.</exception>
    public HealthController(
        IRequestStore store,
        IRequestBackend backend,
        FulfilmentSweep sweep,
        BridgeWatch watch,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(sweep);
        ArgumentNullException.ThrowIfNull(watch);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _backend = backend;
        _sweep = sweep;
        _watch = watch;
        _clock = clock;
    }

    /// <summary>
    /// Whether this plugin is working, in the few facts that answer it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The counts, the last sweep, the bridge and the last store write.</returns>
    [HttpGet("Health")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType<PluginHealth>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PluginHealth>> HealthAsync(CancellationToken cancellationToken)
    {
        var counts = NothingInAnyState();
        var readable = true;

        try
        {
            foreach (var stored in await _store.GetAllAsync(cancellationToken).ConfigureAwait(false))
            {
                // Guarded rather than incremented straight, because a request read from a file
                // written by a later version can carry a state this build has no name for, and a
                // count under a number nobody can render is worse than one request unaccounted for.
                if (counts.TryGetValue(stored.Request.State, out var so_far))
                {
                    counts[stored.Request.State] = so_far + 1;
                }
            }
        }
        catch (RequestStoreLoadException)
        {
            // Reported rather than raised. The counts stay at zero and the flag beside them says
            // they are not a measurement, which is the distinction a page has to draw: an empty
            // queue and an unreadable one produce the same numbers and are opposite answers.
            readable = false;
        }

        var reachability = await _backend.CheckReachableAsync(cancellationToken).ConfigureAwait(false);

        _watch.Saw(reachability, _clock.UtcNow);

        var sweep = _sweep.LastSweep;

        return Ok(new PluginHealth
        {
            Counts = counts,
            StoreReadable = readable,
            LastStoreWriteAt = _store.LastWrittenAt,
            LastSweepAt = sweep?.At,
            LastSweepExamined = sweep?.Examined,
            LastSweepFulfilled = sweep?.Fulfilled,
            Bridge = reachability,
            BridgeLastReachableAt = _watch.LastReachableAt
        });
    }

    /// <summary>
    /// A count of zero for every state, built from the enumeration so a state added to the model
    /// appears on this answer without anybody remembering to add it here.
    /// </summary>
    /// <returns>The counts, all zero.</returns>
    private static Dictionary<RequestState, int> NothingInAnyState()
    {
        var counts = new Dictionary<RequestState, int>();

        foreach (var state in Enum.GetValues<RequestState>())
        {
            counts[state] = 0;
        }

        return counts;
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Time;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Storage;

/// <summary>
/// Removes a finished request once it has been kept for as long as the install says to keep it.
/// This is the half of #49 the retention period was a number without: until it existed, a server
/// kept every request a person ever made, whatever
/// <see cref="PluginConfiguration.FinishedRequestRetentionDays"/> said.
/// <para>
/// <b>Removed rather than stripped of its requester.</b> A request whose requester has been taken
/// off it is a row saying somebody asked for a title, which still says that the title was asked for
/// on this server on that date and leaves a record that answers nothing anybody wanted to ask. The
/// period exists so the data stops existing, and half of it stopping is the shape that reads as
/// deletion in a document and is not one in the file.
/// </para>
/// <para>
/// <b>What counts as finished is the partition the model already draws.</b>
/// <see cref="RequestQuota"/> says of the same five states that open and approved are the ones
/// somebody still owes an answer or a delivery on and that fulfilled, declined and failed are
/// finished. That sentence is the one this reads, so the two cannot mean different things by the
/// word: <see cref="IsFinished"/> is the complement of
/// <see cref="RequestQuota.CountsAgainstIt"/> and is asserted to be over every value of
/// <see cref="RequestState"/>.
/// </para>
/// <para>
/// <b>The clock starts when the request last moved, not when it was asked for.</b> A request
/// declined a year after it was made has been finished for no time at all, and
/// <see cref="MediaRequest.StateChangedAt"/> is the moment it reached the state it is in. That also
/// makes the one legal way back out of a finished state honest: a declined request an operator
/// later approves, or a failed one that arrives after all, moves and therefore starts its period
/// again from the move rather than being removed out from under the person who asked.
/// </para>
/// <para>
/// <b>An install whose settings cannot be honoured removes nothing.</b> The period is read through
/// <see cref="IInstallSettings"/>, which refuses a stored configuration this plugin cannot run on
/// rather than correcting it, so a file holding a retention below the floor stops this run with the
/// refusal instead of deleting against a number nobody chose.
/// </para>
/// </summary>
public sealed class RetentionSweep
{
    private readonly IRequestStore _store;
    private readonly IInstallSettings _settings;
    private readonly IClock _clock;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetentionSweep"/> class.
    /// </summary>
    /// <param name="store">Where the requests are, and what removes one.</param>
    /// <param name="settings">What this install is set to, read per run rather than held.</param>
    /// <param name="clock">The injected clock, so a period is proven by moving time rather than by waiting.</param>
    /// <param name="logger">The server's log, where a removal and a refused one are reported.</param>
    /// <exception cref="ArgumentNullException">Where anything it needs is missing.</exception>
    public RetentionSweep(
        IRequestStore store,
        IInstallSettings settings,
        IClock clock,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Whether this request is one the retention period applies to.
    /// </summary>
    /// <param name="request">The request being judged.</param>
    /// <returns>
    /// <see langword="true"/> where it is fulfilled, declined or failed, which are the three states
    /// <see cref="RequestQuota"/> already calls finished.
    /// </returns>
    public static bool IsFinished(MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return !RequestQuota.CountsAgainstIt(request);
    }

    /// <summary>
    /// Removes every finished request that has been finished for longer than this install keeps
    /// them.
    /// </summary>
    /// <param name="cancellationToken">Cancels the run between requests.</param>
    /// <returns>How many requests this run removed.</returns>
    /// <exception cref="InvalidConfigurationException">
    /// Where the stored settings hold a retention period this plugin will not act on. Nothing is
    /// removed in that case, including requests a shorter period would not have reached: a run that
    /// deleted what it could and then refused would leave an operator repairing a number after the
    /// data it governed had already gone.
    /// </exception>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        // Read before anything is walked, so a refusal happens before the first removal rather than
        // between two of them.
        var keepForDays = _settings.Current.FinishedRequestRetentionDays;
        var keepFor = TimeSpan.FromDays(keepForDays);

        var held = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var removeWhatMovedBefore = _clock.UtcNow - keepFor;
        var removed = 0;

        foreach (var stored in held)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = stored.Request;

            if (!IsFinished(request) || request.StateChangedAt > removeWhatMovedBefore)
            {
                continue;
            }

            try
            {
                if (await _store.RemoveAsync(request.Id, stored.Revision, cancellationToken).ConfigureAwait(false))
                {
                    removed++;
                }
            }
            catch (RequestConcurrencyException)
            {
                // Somebody moved this request between the read and the removal, so what was decided
                // above was decided about a request that no longer exists in that shape. A declined
                // request an operator has just approved is exactly this case, and removing it on a
                // retry would delete the decision they made a moment ago. The next run reads it as
                // it now is.
                _logger.LogDebug(
                    "A request moved while it was being removed for age, so it was left alone and the next run will look at it again.");
            }
        }

        // At information rather than debug: this is the plugin deleting somebody's record without
        // anybody asking it to, and an operator answering for what is held should be able to find
        // that it happened in the log they already read.
        if (removed > 0 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Removed {Removed} finished request(s) that had been finished for longer than the {Days} day(s) this install keeps them for.",
                removed,
                keepForDays);
        }

        return removed;
    }
}

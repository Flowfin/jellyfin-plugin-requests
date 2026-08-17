using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Time;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Fulfilment;

/// <summary>
/// Looks at the library on behalf of requests and writes down what it saw. This is the whole of
/// #42: a request is fulfilled because the server holds what was asked for, never because somebody
/// remembered to say so.
/// <para>
/// Two ways in and one thing done. <see cref="SweepAsync"/> walks the store and is what the
/// scheduled task calls; <see cref="ItemChangedAsync"/> takes one title and is what the library
/// event calls. Both end in the same private step, so the two paths cannot disagree, and neither is
/// the only one that works: a server that was stopped when the file arrived catches up on the
/// schedule, and a server that is running does not wait for it.
/// </para>
/// <para>
/// <b>Removing the item does not move anything back.</b> A title leaving the library is an
/// observation and not a decision being undone, which is what
/// <see cref="RequestLifecycle.Table"/> already says by refusing every move out of
/// <see cref="RequestState.Fulfilled"/>. So a removal is recorded where an observation belongs, in
/// <see cref="MediaRequest.Availability"/>, and the request stays fulfilled with a row that now
/// reads "fulfilled, and the server no longer holds it". An operator seeing that decides what to do;
/// nothing here decides it for them.
/// </para>
/// <para>
/// <b>What is written and when.</b> An observation is written only where it differs from the one the
/// request already carries, or where it moves the request. A sweep that wrote every request every
/// time would rewrite the whole store on every run for no new fact, and the store is one document.
/// What that costs is the precision of <see cref="MediaRequest.AvailabilityCheckedAt"/>: it says
/// when the availability now recorded was established, so it answers whether anything has ever
/// looked and not how long ago the last look was.
/// </para>
/// <para>
/// <b>A request carrying no provider identifier is skipped.</b> There is nothing to look it up by,
/// so its availability stays <see cref="LibraryAvailability.Unknown"/> rather than becoming an
/// absence nothing checked. That is the same answer <see cref="RequestIdentity"/> gives such a
/// request, and <see cref="RequestLifecycle"/> already refuses to move it anywhere but declined.
/// </para>
/// </summary>
public sealed class FulfilmentSweep
{
    private readonly IRequestStore _store;
    private readonly ILibrary _library;
    private readonly IClock _clock;
    private readonly IActivityJournal _journal;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FulfilmentSweep"/> class.
    /// </summary>
    /// <param name="store">Where the requests are.</param>
    /// <param name="library">What the server holds.</param>
    /// <param name="clock">The injected clock, so an observation's time is one a test can set.</param>
    /// <param name="journal">
    /// Where a move is written down. This path is the one nobody watched happen, so the entry is
    /// the only thing that tells an operator afterwards that a request moved on its own.
    /// </param>
    /// <param name="logger">The server's log, where a refused write is reported.</param>
    /// <exception cref="ArgumentNullException">Where anything it needs is missing.</exception>
    public FulfilmentSweep(
        IRequestStore store,
        ILibrary library,
        IClock clock,
        IActivityJournal journal,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _library = library;
        _clock = clock;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>
    /// Gets what the last full run did, or <see langword="null"/> where none has run in this
    /// process.
    /// <para>
    /// The scheduled run only. A library event moves requests too, and it looks at the handful of
    /// requests naming one title rather than at the queue, so recording it here would answer "the
    /// sweep last examined two" on a server holding four hundred. What an operator is asking is
    /// whether the thing that walks everything is walking, and that is this.
    /// </para>
    /// <para>
    /// Held in this process and nowhere else, so a restart answers <see langword="null"/> until the
    /// task next runs. Persisting it would mean this plugin writing a file to say when it last read
    /// one.
    /// </para>
    /// </summary>
    public SweepReport? LastSweep { get; private set; }

    /// <summary>
    /// Looks at the library for every request the store holds.
    /// <para>
    /// Every request, in every state, and not only the ones that can still move. A declined request
    /// whose title has since arrived is exactly the row an operator wants to see before they change
    /// their mind, and a fulfilled one whose title has gone is the row that explains a complaint.
    /// Which of them the table then lets move is asked of the table.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Cancels the sweep between requests.</param>
    /// <returns>How many requests this run moved to fulfilled.</returns>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var held = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var fulfilled = 0;

        foreach (var stored in held)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await LookAsync(stored, cancellationToken).ConfigureAwait(false))
            {
                fulfilled++;
            }
        }

        // Recorded after the walk rather than at the top of it, so a run that was cancelled part way
        // leaves the previous report standing. A half run reported as the last one would tell an
        // operator the sweep examined forty of their four hundred and say nothing about why.
        LastSweep = new SweepReport(_clock.UtcNow, held.Count, fulfilled);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Looked at the library for {Count} request(s) and moved {Fulfilled} to fulfilled.",
                held.Count,
                fulfilled);
        }

        return fulfilled;
    }

    /// <summary>
    /// Looks at the library for the requests naming one title, which is what a library event is
    /// worth acting on.
    /// <para>
    /// The store is asked once per identifier the item carries, because a request may have been made
    /// with only one of them. The same request answering under two identifiers is looked at once.
    /// </para>
    /// </summary>
    /// <param name="change">The title the library gained or lost.</param>
    /// <param name="cancellationToken">Cancels the work between requests.</param>
    /// <returns>How many requests this moved to fulfilled.</returns>
    /// <exception cref="ArgumentNullException">Where no change was given.</exception>
    public async Task<int> ItemChangedAsync(LibraryChangeEventArgs change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var matched = new Dictionary<Guid, StoredRequest>();

        foreach (var identifier in change.ProviderIds)
        {
            if (string.IsNullOrWhiteSpace(identifier.Key) || string.IsNullOrWhiteSpace(identifier.Value))
            {
                continue;
            }

            var found = await _store.FindByProviderIdentifierAsync(
                change.Kind,
                identifier.Key,
                identifier.Value,
                cancellationToken).ConfigureAwait(false);

            foreach (var stored in found)
            {
                matched[stored.Request.Id] = stored;
            }
        }

        var fulfilled = 0;

        foreach (var stored in matched.Values)
        {
            if (await LookAsync(stored, cancellationToken).ConfigureAwait(false))
            {
                fulfilled++;
            }
        }

        return fulfilled;
    }

    /// <summary>
    /// The one step both paths end in: ask the library, decide with
    /// <see cref="FulfilmentRule"/>, and write only where something is new.
    /// </summary>
    /// <param name="stored">The request and the revision it was read at.</param>
    /// <param name="cancellationToken">Cancels the lookup and the write.</param>
    /// <returns><see langword="true"/> where this moved the request to fulfilled.</returns>
    private async Task<bool> LookAsync(StoredRequest stored, CancellationToken cancellationToken)
    {
        var request = stored.Request;

        if (request.ProviderIds.Count == 0)
        {
            return false;
        }

        var holding = await _library.HoldingOfAsync(
            request.Kind,
            request.ProviderIds,
            cancellationToken).ConfigureAwait(false);

        var availability = FulfilmentRule.AvailabilityOf(request, holding);

        // The table decides which states may reach fulfilled, so a state added to the model is a
        // row there rather than a condition here. An approved request that has arrived moves; a
        // declined one whose title has arrived does not, because approving it first is the move
        // that says a person changed the answer.
        var moves = availability == LibraryAvailability.Present
            && RequestLifecycle.IsLegal(request.State, RequestState.Fulfilled);

        if (availability == request.Availability && !moves)
        {
            return false;
        }

        var at = _clock.UtcNow;
        var observed = request with { Availability = availability, AvailabilityCheckedAt = at };

        if (moves)
        {
            observed = RequestLifecycle.Move(observed, RequestState.Fulfilled, at, RequestCaller.Plugin);
        }

        try
        {
            await _store.ReplaceAsync(observed, stored.Revision, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestConcurrencyException)
        {
            // Somebody moved this request between the read and the write, so what was decided above
            // was decided against a request that no longer exists in that shape. Nothing is retried
            // here: the next look reads the request as it now is and decides again, and a retry loop
            // in a sweep is a way to write over a decision an operator has just made.
            _logger.LogDebug(
                "A request moved while the library was being looked at for it, so this observation was dropped and the next look will make it again.");

            return false;
        }

        // After the write, for the reason the endpoint writes its entry after one. Nothing this
        // path moves was asked for by a person, so the entry is what the operator has instead of
        // having been there.
        if (ActivityNote.For(request, observed) is ActivityNote note)
        {
            await _journal.WriteAsync(note, cancellationToken).ConfigureAwait(false);
        }

        return moves;
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Notify;

/// <summary>
/// The person's own switch, in front of the path that pushes to them.
/// <para>
/// <b>It is one place rather than a condition at each call site.</b> Two paths tell somebody their
/// request moved, the endpoint an administrator decides on and the fulfilment sweep, and a third
/// will arrive. A check written at each of them is a check the third one forgets, so the switch sits
/// where the message goes out and nothing above it knows the switch exists.
/// </para>
/// <para>
/// <b>A setting that cannot be read silences the message rather than sending it.</b> The two ways of
/// being wrong are not equal: not sending a courtesy costs somebody a line they would have read on
/// their own page anyway, and sending it costs a person who asked not to be told being told, which
/// is the whole of what this issue was about. The refusal is written to the log, so an operator
/// meets a file they have to repair rather than silence nobody can explain.
/// </para>
/// <para>
/// <b>Telling still costs the caller nothing.</b> Reading the setting is a file read on the first
/// call and a set lookup afterwards, and both happen off the calling thread for the reason
/// <see cref="ServerRequesterNotice"/> pushes off it: what this interface promises is that a caller
/// which has just moved a request tells the person and carries on.
/// </para>
/// <para>
/// <b>The order two messages reach the path underneath in is not promised, and that is the price of
/// the paragraph above.</b> Each message is decided on a task of its own, so two people whose
/// requests moved one after another are told in whichever order their settings finished being read.
/// Promising the caller's order instead would mean holding the second message until the first
/// setting had been read, which is the waiting this class is here to keep off the caller's thread.
/// What is promised is that every message is decided exactly once and that
/// <see cref="QuietAsync"/> does not return while one is still being decided.
/// </para>
/// </summary>
public sealed class QuietedRequesterNotice : IRequesterNotice
{
    private readonly IRequesterNotice _inner;
    private readonly INoticePreferences _preferences;
    private readonly ILogger _logger;
    private readonly object _gate = new object();

    private Task _inFlight = Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuietedRequesterNotice"/> class.
    /// </summary>
    /// <param name="inner">The path that actually tells somebody.</param>
    /// <param name="preferences">Who has turned it off.</param>
    /// <param name="logger">Where a setting that could not be read is reported.</param>
    /// <exception cref="ArgumentNullException">Where any of the three is absent.</exception>
    public QuietedRequesterNotice(IRequesterNotice inner, INoticePreferences preferences, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _preferences = preferences;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Tell(RequesterMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var asking = Task.Run(() => TellIfTheyWantItAsync(message));

        lock (_gate)
        {
            _inFlight = Both(_inFlight, asking);
        }
    }

    /// <inheritdoc />
    public Task QuietAsync(CancellationToken cancellationToken)
    {
        Task waiting;

        lock (_gate)
        {
            waiting = _inFlight;
        }

        return Settled(waiting, _inner, cancellationToken);
    }

    /// <summary>
    /// Both, as one thing to wait for. Written out rather than done with <c>Task.WhenAll</c> so that
    /// a chain of messages does not grow an array per message, which is the reason
    /// <see cref="ServerRequesterNotice"/> writes its own out.
    /// </summary>
    /// <param name="earlier">What was already being asked about.</param>
    /// <param name="later">What has just been asked about.</param>
    /// <returns>A task that completes when both have.</returns>
    private static async Task Both(Task earlier, Task later)
    {
        await earlier.ConfigureAwait(false);
        await later.ConfigureAwait(false);
    }

    /// <summary>
    /// Nothing here is still being decided and nothing underneath is still in flight. Both halves,
    /// and in that order: a message this has not finished deciding about has not reached the path
    /// below yet, so waiting on that path first would answer before it was handed anything.
    /// </summary>
    /// <param name="deciding">What is still being decided here.</param>
    /// <param name="inner">The path underneath.</param>
    /// <param name="cancellationToken">Gives up waiting.</param>
    /// <returns>A task that completes when nothing is in flight.</returns>
    private static async Task Settled(Task deciding, IRequesterNotice inner, CancellationToken cancellationToken)
    {
        await deciding.WaitAsync(cancellationToken).ConfigureAwait(false);
        await inner.QuietAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One message, passed on or dropped.
    /// </summary>
    /// <param name="message">What to say, and who to say it to.</param>
    /// <returns>A task that completes once the message has been passed on or dropped.</returns>
    private async Task TellIfTheyWantItAsync(RequesterMessage message)
    {
        bool wanted;

        try
        {
            wanted = await _preferences.TellsThemAsync(message.ToUserId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (NoticePreferencesException refused)
        {
            // The identifier is not in the line. Which person could not be told is a fact about a
            // person and the file is the same file for everybody, so the sentence an operator needs
            // is that the file is unreadable rather than whose message went with it.
            _logger.LogError(
                refused,
                "Nobody is being told about their own requests, because what this plugin keeps about who wants to be told cannot be read. The message that was about to go out was dropped rather than sent to somebody who may have asked not to receive it.");

            return;
        }

        if (wanted)
        {
            _inner.Tell(message);
        }
    }
}

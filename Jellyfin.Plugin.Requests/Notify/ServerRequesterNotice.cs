using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Notify;

/// <summary>
/// The server's own session manager, as the one call this plugin makes to it.
/// <para>
/// This is the only place that names the host's sessions, so the reach exists once and everything
/// above it takes <see cref="IRequesterNotice"/> instead. The shape of the host call is the same on
/// both claimed lines: <c>SendMessageToUserSessions</c> takes a list of user identifiers, a name out
/// of the server's own closed enumeration, and a payload.
/// </para>
/// <para>
/// <b>A plugin cannot add a name to that enumeration, so this borrows the one a client acts on.</b>
/// The message goes out as <see cref="SessionMessageType.GeneralCommand"/> carrying
/// <see cref="GeneralCommandType.DisplayMessage"/>, which is the shape the server itself builds for
/// <c>SendMessageCommand</c> and the one the web client turns into something a person reads.
/// <c>docs/notifications.md</c> carries the reading of both sides and what it does not prove.
/// </para>
/// <para>
/// <b><see cref="DismissedAfter"/> is not decoration.</b> The web client shows a message with a
/// timeout as a notice that fades and one without as a dialog somebody has to dismiss. A courtesy
/// that interrupts what a person is doing until they click it is worse than not sending it, so this
/// path always sets one.
/// </para>
/// </summary>
public sealed class ServerRequesterNotice : IRequesterNotice
{
    /// <summary>
    /// How long the message stays on the person's screen before it goes by itself.
    /// <para>
    /// Long enough to read one sentence and short enough not to sit over whatever they were doing.
    /// Its presence matters more than its value: the value decides how long a notice lasts, and
    /// leaving it out decides that it is a dialog instead.
    /// </para>
    /// </summary>
    public static readonly TimeSpan DismissedAfter = TimeSpan.FromSeconds(8);

    private readonly ISessionManager _sessions;
    private readonly ILogger _logger;
    private readonly object _gate = new object();

    private Task _inFlight = Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerRequesterNotice"/> class.
    /// </summary>
    /// <param name="sessions">The server's sessions.</param>
    /// <param name="logger">Where a message that could not be pushed is reported.</param>
    /// <exception cref="ArgumentNullException">Where there is nothing to push through or nothing to report on.</exception>
    public ServerRequesterNotice(ISessionManager sessions, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(logger);

        _sessions = sessions;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Tell(RequesterMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var command = new GeneralCommand { Name = GeneralCommandType.DisplayMessage };

        // The three argument names the server's own SendMessageCommand writes, spelled the same
        // way, because they are what a client reads out of the command rather than anything this
        // plugin gets to choose.
        command.Arguments["Header"] = message.Header;
        command.Arguments["Text"] = message.Text;
        command.Arguments["TimeoutMs"] = ((long)DismissedAfter.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);

        // Started rather than awaited, and started off this thread, for the reason the outbound sink
        // gives: what this interface promises is that telling somebody costs the caller nothing, and
        // that promise should not depend on how fast the slowest connected client is.
        var delivery = Task.Run(() => PushAsync(message.ToUserId, command));

        lock (_gate)
        {
            _inFlight = Both(_inFlight, delivery);
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

        return waiting.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Both pushes, as one thing to wait for. Written out rather than done with <c>Task.WhenAll</c>
    /// so that a chain of messages does not grow an array per message.
    /// </summary>
    /// <param name="earlier">What was already in flight.</param>
    /// <param name="later">What has just been started.</param>
    /// <returns>A task that completes when both have.</returns>
    private static async Task Both(Task earlier, Task later)
    {
        await earlier.ConfigureAwait(false);
        await later.ConfigureAwait(false);
    }

    /// <summary>
    /// One push, which either happens or is written to the log.
    /// </summary>
    /// <param name="to">Whose sessions it goes to, and the only person named anywhere in the call.</param>
    /// <param name="command">The command a client turns into something a person reads.</param>
    /// <returns>A task that completes when the attempt is over, however it went.</returns>
    private async Task PushAsync(Guid to, GeneralCommand command)
    {
        try
        {
            await _sessions
                .SendMessageToUserSessions(new List<Guid> { to }, SessionMessageType.GeneralCommand, command, CancellationToken.None)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception reason)
#pragma warning restore CA1031
        {
            // Every outcome of a push is the same outcome here: the person was not told and the
            // request is unaffected. Catching by name instead would leave whichever exception nobody
            // listed to surface out of a task nothing observes, and the failure this path exists to
            // avoid is a courtesy message deciding what happens to a request.
            _logger.LogWarning(
                reason,
                "A request moved and the person who asked for it could not be told on their live sessions. The move itself was written and stands, and their own page shows the state whenever they next look. Nothing will be retried.");
        }
    }
}

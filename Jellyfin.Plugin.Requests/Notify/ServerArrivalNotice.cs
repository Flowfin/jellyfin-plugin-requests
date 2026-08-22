using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Configuration;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Notify;

/// <summary>
/// The server's own broadcast to administrator sessions, as the one call this plugin makes to it.
/// <para>
/// This is the only place that names <c>SendMessageToAdminSessions</c>, so the reach exists once and
/// everything above it takes <see cref="IArrivalNotice"/> instead. The shape of the host call is the
/// same on both claimed lines: a name out of the server's own closed enumeration and a payload, with
/// the server deciding which sessions administer it.
/// </para>
/// <para>
/// <b>A plugin cannot add a name to that enumeration, and none of its members means "a plugin has
/// something to say".</b> They are playback commands, server lifecycle, package installation,
/// scheduled tasks, sessions, sync play and the activity log. So a name has to be borrowed, and the
/// two kinds of member are not equally safe to borrow: a client obeys a command and merely reads a
/// notice. <see cref="SessionMessageType.ActivityLogEntry"/> is the notice closest in meaning to
/// what this sends, which is that something happened an operator's view may want to react to, and
/// the dashboard subscribes to no such name, so a document under it is ignored there rather than
/// acted on. <c>docs/notifications.md</c> carries the reading of both sides and the price of the
/// borrowing.
/// </para>
/// <para>
/// <b>The document is <see cref="OutboundNotice"/> and not a second shape.</b> A reader written
/// against this plugin gets one set of field names whichever carrier brought them, and a field added
/// to one carrier cannot be missing from the other. What differs is the carrier and the audience,
/// which the two interfaces already say.
/// </para>
/// <para>
/// <b>An install says nothing here until an operator turns it on.</b> The switch is read per notice
/// from the settings rather than captured, so turning it on applies to the next arrival rather than
/// to the next restart, which is the same reading the outbound sink makes of its own three.
/// </para>
/// </summary>
public sealed class ServerArrivalNotice : IArrivalNotice
{
    /// <summary>
    /// The name the document goes out under, borrowed from the server's closed enumeration for the
    /// reason above. It is a constant here so that a reader of a test and a reader of the ship both
    /// find one answer, and so that changing it is one edit rather than two.
    /// </summary>
    public const SessionMessageType SentAs = SessionMessageType.ActivityLogEntry;

    private readonly ISessionManager _sessions;
    private readonly IInstallSettings _settings;
    private readonly ILogger _logger;
    private readonly object _gate = new object();

    private Task _inFlight = Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerArrivalNotice"/> class.
    /// </summary>
    /// <param name="sessions">The server's sessions.</param>
    /// <param name="settings">What this install is set to, read per notice rather than kept.</param>
    /// <param name="logger">Where a message that could not be pushed is reported.</param>
    /// <exception cref="ArgumentNullException">
    /// Where there is nothing to push through, nothing to read the settings from, or nothing to
    /// report on.
    /// </exception>
    public ServerArrivalNotice(ISessionManager sessions, IInstallSettings settings, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _sessions = sessions;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Tell(OutboundNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        if (!_settings.Current.TellsAdministratorsAboutArrivals)
        {
            return;
        }

        // Started rather than awaited, and started off this thread, for the reason the requester
        // path gives: what this interface promises is that telling costs the caller nothing, and
        // that promise should not depend on how fast the slowest connected administrator is.
        var delivery = Task.Run(() => PushAsync(notice));

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
    /// <param name="notice">The document the administrators' clients are handed.</param>
    /// <returns>A task that completes when the attempt is over, however it went.</returns>
    private async Task PushAsync(OutboundNotice notice)
    {
        try
        {
            await _sessions
                .SendMessageToAdminSessions(SentAs, notice, CancellationToken.None)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception reason)
#pragma warning restore CA1031
        {
            // Every outcome of a push is the same outcome here: nobody was told and the request is
            // unaffected. Catching by name instead would leave whichever exception nobody listed to
            // surface out of a task nothing observes, and the failure this path exists to avoid is a
            // courtesy message deciding what happens to somebody's ask.
            _logger.LogWarning(
                reason,
                "A request arrived and the administrators signed in at that moment could not be told. The request itself was written and stands, and it is in the queue whenever an operator next opens it. Nothing will be retried. {RequestId}",
                notice.RequestId);
        }
    }
}

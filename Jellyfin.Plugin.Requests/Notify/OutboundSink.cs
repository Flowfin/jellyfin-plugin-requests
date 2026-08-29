using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Notify;

/// <summary>
/// The sink as an HTTP post to whatever an operator pointed it at.
/// <para>
/// <b>Nothing here is allowed to matter to a request.</b> The delivery is started on a thread of its
/// own and the caller is not given it, so a handler that blocks, an address that refuses the
/// connection and an endpoint that never answers all cost exactly the same as an endpoint that
/// answered: nothing. Everything a send can fail with is caught and written to the server's log,
/// which is where an operator whose messages stopped arriving looks.
/// </para>
/// <para>
/// <b>Off is the shipping state and it is off by absence rather than by a switch.</b> An install
/// where nobody typed an address has no sink at all: <see cref="IsConfigured"/> is false, announcing
/// does nothing, and no client is ever built. That is why the address is the setting and there is no
/// second one beside it saying whether to use it, which would be two ways to express the same off
/// and one of them wrong. The per-event switches are not that second one: they narrow a sink that
/// has somewhere to send to, they are on until an operator turns one off, and an install with no
/// address is silent whatever they say.
/// </para>
/// <para>
/// <b>It gives up rather than waiting.</b> A send is bounded by <see cref="DefaultAnswerWithin"/>,
/// because an endpoint that accepts a connection and then says nothing holds the send open for as
/// long as it likes, and a plugin that lets it is a plugin accumulating one of those per movement in
/// the queue.
/// </para>
/// </summary>
public sealed class OutboundSink : IOutboundSink, IDisposable
{
    /// <summary>
    /// How long a send is given before it is abandoned.
    /// <para>
    /// Ten seconds is long enough for a service that is answering slowly and short enough that a
    /// service which has stopped answering is noticed rather than accumulated. Nothing waits on it,
    /// so the number is a bound on resources rather than on anybody's patience.
    /// </para>
    /// </summary>
    public static readonly TimeSpan DefaultAnswerWithin = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How the document is written. Declared once here rather than left to whatever the default is,
    /// because the shape on the wire is the contract in <see cref="OutboundNotice"/> and a global
    /// default changing under it would change what a reader receives.
    /// </summary>
    private static readonly JsonSerializerOptions AsWritten = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly IInstallSettings _settings;
    private readonly HttpClient _client;
    private readonly ILogger _logger;
    private readonly TimeSpan _answerWithin;
    private readonly object _gate = new object();

    private Task _inFlight = Task.CompletedTask;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboundSink"/> class.
    /// </summary>
    /// <param name="settings">
    /// What this install is set to, read per notice rather than kept, so an operator who changes the
    /// address gets the next message at the new one instead of at the next restart.
    /// </param>
    /// <param name="handler">
    /// What actually sends. It is handed in rather than made here so the suite can put an endpoint
    /// in the same process and exercise this class's own serialisation, timeout and error handling
    /// without a socket, which is the headless rule in <c>docs/testing.md</c>.
    /// </param>
    /// <param name="logger">Where a failed delivery is written.</param>
    /// <param name="answerWithin">
    /// How long a send is given. It is a parameter so the bound can be proven rather than described:
    /// a suite can set it to nothing and watch a send that would otherwise hang be abandoned.
    /// </param>
    /// <exception cref="ArgumentNullException">Where anything it needs is missing.</exception>
    public OutboundSink(
        IInstallSettings settings,
        HttpMessageHandler handler,
        ILogger logger,
        TimeSpan answerWithin)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _logger = logger;
        _answerWithin = answerWithin;

        // The client's own timeout is left off and the bound is applied per send instead. They are
        // two mechanisms for one thing, and the one that can be handed a value is the one a test can
        // prove bites.
        _client = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    /// <inheritdoc />
    public bool IsConfigured => Address() is not null;

    /// <inheritdoc />
    public void Announce(OutboundNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        if (_disposed || Address() is not Uri address || !Wanted(notice.Event))
        {
            return;
        }

        // Started rather than awaited, and started off this thread rather than inline. Inline, the
        // delivery would run on the caller's thread as far as its first await, and a handler that
        // blocks without one would hold the request that announced it. What this interface promises
        // is that announcing costs a caller nothing, and that promise should not depend on how
        // somebody else's handler is written.
        var delivery = Task.Run(() => DeliverAsync(address, notice));

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
    /// Drops the client. The handler is not disposed with it, because it was handed in and whoever
    /// handed it in may still be using it.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
    }

    /// <summary>
    /// One delivery, which either happens or is written to the log.
    /// </summary>
    /// <param name="address">Where the operator pointed it.</param>
    /// <param name="notice">The document to send.</param>
    /// <returns>A task that completes when the attempt is over, however it went.</returns>
    private async Task DeliverAsync(Uri address, OutboundNotice notice)
    {
        using var giveUp = new CancellationTokenSource();
        giveUp.CancelAfter(_answerWithin);

        try
        {
            using var response = await _client
                .PostAsJsonAsync(address, notice, AsWritten, giveUp.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "The notification sink answered {StatusCode} for the notice about request {RequestId}, so that message was not delivered. Nothing about the request changed.",
                    (int)response.StatusCode,
                    notice.RequestId);
            }
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception reason)
#pragma warning restore CA1031
        {
            // Every outcome of a send is the same outcome here: the message did not arrive and the
            // request is unaffected. Catching by name instead would leave whichever exception nobody
            // listed to surface out of a task nothing observes, and the failure this sink exists to
            // avoid is exactly a notification path deciding what happens to a request.
            //
            // WHAT IS LOGGED IS THE CLASS OF THE FAILURE AND NEVER THE EXCEPTION ITSELF. The
            // platform puts the destination of a request into the message of the exception it
            // raises for it - `HttpRequestException` names the host and the port it could not reach
            // - and the address is a marked secret, so handing the object to the logger writes it
            // into a log an operator pastes into a tracker. Nothing in this method composes that
            // sentence, so it cannot be fixed by writing a more careful one; what stops it is not
            // passing the object. #100.
            //
            // WHAT THAT COSTS IS THE STACK AND THE PLATFORM'S OWN WORDING, and the type name is
            // what an operator actually acts on: a refused connection, a name that does not
            // resolve, a timeout and a certificate that did not verify are four different type
            // names and four different things to go and look at.
            _logger.LogError(
                "The notification sink could not deliver the notice about request {RequestId}: {Failure}. The request is unaffected and nothing will be retried. The failure is named by class rather than reported in full, because the address it was posted to is a secret and the platform writes it into the exception.",
                notice.RequestId,
                reason.GetType().Name);
        }
    }

    /// <summary>
    /// Where notices go, or nothing where this install sends none.
    /// <para>
    /// Read per call, and parsed rather than trusted: a value that is not an absolute HTTP address
    /// is treated as no sink at all. <c>ConfigurationRules</c> refuses such a value when it arrives,
    /// so this is the second reading rather than the only one, and the two agree on purpose. What it
    /// is here for is the moment they cannot: a configuration file edited by hand on a server that
    /// is already running.
    /// </para>
    /// </summary>
    /// <returns>The address, or <see langword="null"/> where there is none to use.</returns>
    private Uri? Address()
    {
        var written = _settings.Current.OutboundNoticeAddress;

        if (string.IsNullOrWhiteSpace(written))
        {
            return null;
        }

        return Uri.TryCreate(written, UriKind.Absolute, out var address)
            && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps)
                ? address
                : null;
    }

    /// <summary>
    /// Whether this install wants to hear about that kind of movement.
    /// <para>
    /// Read per notice from the same settings the address comes from, so an operator who switches an
    /// event off stops getting it at the next movement rather than at the next restart.
    /// </para>
    /// <para>
    /// <b>An event with no arm here is not announced.</b> A value added to <see cref="NoticeEvent"/>
    /// reaches this with nobody having decided whether an operator wants it, and the two ways to be
    /// wrong are not equal: an event withheld is a message somebody has to go and switch on, and an
    /// event sent is a message that has already left the server. <see cref="NoticeEvent.Asked"/> is
    /// in that arm today by the same rule and not by omission - nothing here announces an arrival,
    /// because the endpoint is not the only way one is made and a switch that catches some arrivals
    /// is worse than one that catches none.
    /// </para>
    /// </summary>
    /// <param name="what">What the notice is about.</param>
    /// <returns><see langword="true"/> where the operator has left that event switched on.</returns>
    private bool Wanted(NoticeEvent what)
    {
        var settings = _settings.Current;

        return what switch
        {
            NoticeEvent.Approved => settings.AnnouncesApprovals,
            NoticeEvent.Declined => settings.AnnouncesDeclines,
            NoticeEvent.Fulfilled => settings.AnnouncesFulfilments,
            _ => false
        };
    }

    /// <summary>
    /// Both deliveries, as one thing to wait for. Written out rather than done with
    /// <c>Task.WhenAll</c> so that a chain of announcements does not grow an array per announcement.
    /// </summary>
    /// <param name="earlier">What was already in flight.</param>
    /// <param name="later">What has just been started.</param>
    /// <returns>A task that completes when both have.</returns>
    private static async Task Both(Task earlier, Task later)
    {
        await earlier.ConfigureAwait(false);
        await later.ConfigureAwait(false);
    }
}

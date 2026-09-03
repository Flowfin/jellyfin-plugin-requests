using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Model;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Bridge.Overseerr;

/// <summary>
/// The bridge to a service that speaks the Overseerr form, which is the one form an adapter is
/// written against here, decided on #113 and asked for by #315.
/// <para>
/// <b>On a server with no address set this is the bridge with nothing behind it.</b> Every call
/// reads the settings first, and where no address is written it hands the call to
/// <see cref="NoRequestBackend"/> and adds nothing: nothing configured is an answer, not a failure,
/// and it is the answer most servers give. Read per call rather than kept, so an operator who types
/// an address gets the next approval handed over rather than the one after the next restart.
/// </para>
/// <para>
/// <b>Four calls, one each.</b> <c>GET /status</c> says whether the service is up, and it is
/// answered without a credential, so a green answer from it says nothing about whether the key is
/// accepted; <c>docs/bridge.md</c> records that bound and #86 owns the question of which call would
/// answer it. <c>POST /request</c> hands an approval over and the number the service answers with
/// is what is kept. <c>GET /request/{id}</c> asks where something stands, and the number it reports
/// is turned into the mapping table's word by <see cref="OverseerrWords"/>, which is the one place
/// that step lives. <c>DELETE /request/{id}</c> takes something back.
/// </para>
/// <para>
/// <b>The credential travels in a header and never in an address.</b> The form takes it as
/// <c>X-Api-Key</c>, and that is the whole of what makes the four claims in <c>docs/bridge.md</c>
/// about the credential cheap to keep: the exception the platform raises for a failed call names
/// the destination and not the headers, so an exception handed to the logger carries the address
/// and not the key. No sentence this class composes reads the key either, which the invariant lint
/// refuses over the marked setting by name.
/// </para>
/// <para>
/// <b>A request the form cannot take is refused before anything is sent.</b> The form identifies a
/// title by its TMDB number and nothing else, and identifies a person by its own numeric user
/// identifier; a request with no TMDB identifier, or a mapping row whose account is not a number,
/// raises <see cref="HandoverRefusedException"/> and no call is made. The approval stands, which is
/// the first of the three answers <c>docs/bridge.md</c> sets out, and the other two are refused
/// there by name.
/// </para>
/// <para>
/// <b>It gives up rather than waiting.</b> Every call is bounded by the interval it was constructed
/// with, and a call that runs out of it is a <see cref="TimeoutException"/> rather than a
/// cancellation, because a caller that treats cancellation as its own token having fired would
/// otherwise stop a whole reconciliation run for one slow answer.
/// </para>
/// <para>
/// <b>What is not decided here.</b> Which failures are told apart, and what a bound retry is, is #86.
/// A submission that the service accepts and that this side then cannot write back is
/// <see cref="BridgeSubmission"/>'s, and its log line carries the identifier so the two can be
/// reconciled by hand. Nothing here has been run against a service: every leg that proves this
/// class drives it through an in-process handler, which is the headless rule in
/// <c>docs/testing.md</c>, and <c>docs/bridge.md</c> says what a reading of a description is worth
/// against a reading of an instance.
/// </para>
/// </summary>
public sealed class OverseerrBackend : IRequestBackend, IDisposable
{
    /// <summary>
    /// What this adapter calls the service it speaks to, carried on every reference it issues so a
    /// reference from a service an operator has since replaced is recognised as somebody else's.
    /// </summary>
    public const string ServiceName = "overseerr";

    /// <summary>
    /// The header the form reads the credential out of. A header and never a query string, for the
    /// reason the class summary gives.
    /// </summary>
    public const string ApiKeyHeader = "X-Api-Key";

    /// <summary>
    /// The provider whose identifier a submission carries. The form hands the value straight to its
    /// own TMDB client, which <c>docs/bridge.md</c> reads off the form's implementation, so no other
    /// provider's number will do.
    /// </summary>
    public const string TmdbProvider = "Tmdb";

    /// <summary>
    /// How long one call is given on a server, before it is given up on.
    /// </summary>
    public static readonly TimeSpan DefaultAnswerWithin = TimeSpan.FromSeconds(10);

    private const string Prefix = "api/v1/";

    private readonly IInstallSettings _settings;
    private readonly HttpClient _client;
    private readonly ILogger _logger;
    private readonly TimeSpan _answerWithin;
    private readonly NoRequestBackend _nothingBehindIt = new NoRequestBackend();

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OverseerrBackend"/> class.
    /// </summary>
    /// <param name="settings">
    /// What this install is set to, read per call rather than kept, so a changed address or key is
    /// used by the next call instead of by the next restart.
    /// </param>
    /// <param name="handler">
    /// What actually sends. It is handed in rather than made here so the suite can put a service in
    /// the same process and drive this class's own serialisation, timeout and error handling without
    /// a socket, which is the headless rule in <c>docs/testing.md</c>.
    /// </param>
    /// <param name="logger">Where a service that did not answer is written.</param>
    /// <param name="answerWithin">
    /// How long one call is given. A parameter so the bound can be proven rather than described: a
    /// suite can set it to nothing and watch a call that would otherwise hang be given up on.
    /// </param>
    /// <exception cref="ArgumentNullException">Where anything it needs is missing.</exception>
    public OverseerrBackend(
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

        // The client's own timeout is left off and the bound is applied per call instead, for the
        // reason the outbound sink gives: two mechanisms for one thing, and the one that can be
        // handed a value is the one a test can prove bites.
        _client = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    /// <inheritdoc />
    public async Task<BackendReachability> CheckReachableAsync(CancellationToken cancellationToken)
    {
        if (Configured() is not ConfiguredBridge bridge)
        {
            return await _nothingBehindIt.CheckReachableAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var answer = await SendAsync(bridge, HttpMethod.Get, "status", content: null, cancellationToken)
                .ConfigureAwait(false);

            if (answer.IsSuccessStatusCode)
            {
                return BackendReachability.Reachable;
            }

            _logger.LogWarning(
                "The external request service answered {StatusCode} when asked whether it is up, so it is reported as unreachable. Nothing else was asked of it.",
                (int)answer.StatusCode);

            return BackendReachability.Unreachable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception reason)
#pragma warning restore CA1031
        {
            // Every way of not answering is the same answer here: configured, and did not answer.
            // The interface names that state so a caller does not have to tell a refused connection
            // from a name that does not resolve, and #86 is where the ones worth telling apart are
            // decided.
            _logger.LogWarning(
                reason,
                "The external request service could not be asked whether it is up, so it is reported as unreachable. Nothing else was asked of it.");

            return BackendReachability.Unreachable;
        }
    }

    /// <inheritdoc />
    public async Task<BackendReference?> SubmitAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Configured() is not ConfiguredBridge bridge)
        {
            return await _nothingBehindIt.SubmitAsync(request, cancellationToken).ConfigureAwait(false);
        }

        // Built before anything is sent, so a request the form cannot take is refused with nothing
        // over there having seen it.
        var submission = Submission(request, bridge.Accounts);

        using var content = new StringContent(submission.ToJsonString(), Encoding.UTF8, "application/json");
        using var answer = await SendAsync(bridge, HttpMethod.Post, "request", content, cancellationToken)
            .ConfigureAwait(false);

        if (!answer.IsSuccessStatusCode)
        {
            throw Refused(answer.StatusCode, "the submission of request " + Text(request.Id));
        }

        var created = await ReadAsync(answer, cancellationToken).ConfigureAwait(false);

        if (Number(created, "id") is not long id)
        {
            // The one case the interface's own documentation says needs somebody to look: the service
            // may well hold the request, and this side has nothing to ask about it with. Said as
            // loudly as a refusal, and never retried, because a second submission is the duplicate
            // BridgeSubmission exists against.
            throw new InvalidDataException(
                "The external request service accepted the submission of request " + Text(request.Id)
                + " and answered with no identifier for it, so nothing can be kept to ask about it later. The service may hold the request; nothing here retries, and the two sides have to be reconciled by hand.");
        }

        return new BackendReference { Service = ServiceName, Id = id.ToString(CultureInfo.InvariantCulture) };
    }

    /// <inheritdoc />
    public async Task<BackendReport?> ReportAsync(BackendReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (Configured() is not ConfiguredBridge bridge)
        {
            return await _nothingBehindIt.ReportAsync(reference, cancellationToken).ConfigureAwait(false);
        }

        if (!IssuedHere(reference))
        {
            // Somebody else's reference. Nothing known is the ordinary answer the interface names
            // for it, and asking this service about a number another one issued would be asking
            // about whatever happens to have that number here.
            return null;
        }

        using var answer = await SendAsync(bridge, HttpMethod.Get, "request/" + Uri.EscapeDataString(reference.Id), content: null, cancellationToken)
            .ConfigureAwait(false);

        if (answer.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!answer.IsSuccessStatusCode)
        {
            throw Refused(answer.StatusCode, "the question about reference " + reference.Id);
        }

        var reported = await ReadAsync(answer, cancellationToken).ConfigureAwait(false);

        if (Number(reported, "status") is not long status)
        {
            throw new InvalidDataException(
                "The external request service answered about reference " + reference.Id
                + " with no status, so nothing is known about where it stands and the request is left as it is.");
        }

        return new BackendReport { Reported = OverseerrWords.RequestStatus(status) };
    }

    /// <inheritdoc />
    public async Task WithdrawAsync(BackendReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (Configured() is not ConfiguredBridge bridge)
        {
            await _nothingBehindIt.WithdrawAsync(reference, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!IssuedHere(reference))
        {
            return;
        }

        using var answer = await SendAsync(bridge, HttpMethod.Delete, "request/" + Uri.EscapeDataString(reference.Id), content: null, cancellationToken)
            .ConfigureAwait(false);

        // Gone already is the state the caller wanted, so it is not a failure to report.
        if (answer.IsSuccessStatusCode || answer.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        throw Refused(answer.StatusCode, "the withdrawal of reference " + reference.Id);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
    }

    private static bool IssuedHere(BackendReference reference)
        => string.Equals(reference.Service, ServiceName, StringComparison.Ordinal);

    private static string Text(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    private static HttpRequestException Refused(HttpStatusCode status, string what)
        => new HttpRequestException(
            string.Format(
                CultureInfo.InvariantCulture,
                "The external request service answered {0} to {1}. The body of that answer is not quoted, because it is whatever the thing at that address chose to send.",
                (int)status,
                what),
            inner: null,
            status);

    private static JsonObject Submission(MediaRequest request, BackendAccounts accounts)
    {
        var body = new JsonObject
        {
            ["mediaType"] = request.Kind switch
            {
                RequestedItemKind.Movie => "movie",
                RequestedItemKind.Series => "tv",
                _ => throw new HandoverRefusedException(
                    "Request " + Text(request.Id) + " is of a kind the external request service has no word for, so it was not handed over. The approval stands and nothing was sent.")
            },
            ["tmdbId"] = TmdbIdentifier(request)
        };

        if (request.Kind == RequestedItemKind.Series)
        {
            // The whole show is asked for by name rather than by listing every season, because the
            // form is the side that knows how many there are.
            body["seasons"] = request.Seasons.Count == 0
                ? JsonValue.Create("all")
                : new JsonArray(request.Seasons.Select(season => (JsonNode?)JsonValue.Create(season)).ToArray());
        }

        var account = accounts.For(request.RequestedByUserId);

        if (account.Name is string name)
        {
            body["userId"] = ServiceUser(name, request.RequestedByUserId);
        }

        return body;
    }

    private static long TmdbIdentifier(MediaRequest request)
    {
        foreach (var identifier in request.ProviderIds)
        {
            if (!string.Equals(identifier.Key, TmdbProvider, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (long.TryParse(identifier.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            throw new HandoverRefusedException(
                "Request " + Text(request.Id)
                + " carries a TMDB identifier that is not a number, and the external request service identifies a title by that number and nothing else. The approval stands and nothing was sent.");
        }

        throw new HandoverRefusedException(
            "Request " + Text(request.Id)
            + " carries no TMDB identifier, and the external request service identifies a title by that number and nothing else. The approval stands and nothing was sent; the request can be handed over once it carries one.");
    }

    private static long ServiceUser(string account, Guid user)
    {
        if (long.TryParse(account, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        // The account text is deliberately not quoted: it is the operator's string for the other
        // side and belongs there rather than in this server's log.
        throw new HandoverRefusedException(
            "The account mapped for user " + Text(user)
            + " is not a number, and the external request service identifies its users by number, so the request was not handed over. The approval stands and nothing was sent; correct the row and hand it over again.");
    }

    private static long? Number(JsonElement body, string name)
        => body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
                ? number
                : null;

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage answer, CancellationToken cancellationToken)
    {
        var text = await answer.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var document = JsonDocument.Parse(text);

            return document.RootElement.Clone();
        }
        catch (JsonException reason)
        {
            throw new InvalidDataException(
                "The external request service answered with a body that is not JSON, so nothing in it can be read. The body is not quoted, because it is whatever the thing at that address chose to send.",
                reason);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        ConfiguredBridge bridge,
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var request = new HttpRequestMessage(method, new Uri(bridge.Root, path)) { Content = content };
        request.Headers.TryAddWithoutValidation(ApiKeyHeader, bridge.Key);

        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        giveUp.CancelAfter(_answerWithin);

        try
        {
            // The whole answer is read before this returns, so the bound covers the body as well as
            // the headers and nothing below reads past it.
            return await _client.SendAsync(request, HttpCompletionOption.ResponseContentRead, giveUp.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException reason) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The external request service did not answer {0} {1} within {2} seconds, so the call was given up on.",
                    method.Method,
                    path,
                    _answerWithin.TotalSeconds),
                reason);
        }
    }

    private ConfiguredBridge? Configured()
    {
        var settings = _settings.Current;
        var written = settings.BridgeAddress;

        if (string.IsNullOrWhiteSpace(written))
        {
            return null;
        }

        // The same reading the configuration rules make, so an address those rules would refuse is
        // treated as no address rather than dialled. Through the server it never gets this far: the
        // settings are refused on the way in.
        if (!Uri.TryCreate(written.Trim().TrimEnd('/') + "/" + Prefix, UriKind.Absolute, out var root)
            || (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var rows = new Dictionary<Guid, string>();

        foreach (var row in settings.BridgeAccounts)
        {
            // Two rows for one person are refused by the configuration rules; where a reading skips
            // those, the dictionary refuses the second and names the identifier, which is the answer
            // rather than one of the two rows winning quietly.
            rows.Add(row.UserId, row.Account);
        }

        return new ConfiguredBridge(root, settings.BridgeApiKey ?? string.Empty, new BackendAccounts(rows));
    }

    private sealed record ConfiguredBridge(Uri Root, string Key, BackendAccounts Accounts);
}

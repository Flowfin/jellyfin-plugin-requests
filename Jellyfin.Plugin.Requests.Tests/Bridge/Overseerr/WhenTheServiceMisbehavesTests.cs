using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Bridge.Overseerr;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Fulfilment;
using Jellyfin.Plugin.Requests.Localisation;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Bridge.Overseerr;

/// <summary>
/// What happens when the service misbehaves, which is #86: the four failures its body names, each
/// driven through the real adapter and the real submission or reconciliation path against the
/// in-process service from #35, so that what is measured is the behaviour an operator gets and not
/// a double agreeing with itself.
/// <para>
/// The four are a service that does not answer, a service that refuses this server's key, a service
/// reporting a version this adapter does not know, and a title the service has never heard of. The
/// first is temporary and is asked again; the second and third stop the reconciliation and are said
/// at error; the fourth is a fact about one request. Every leg reads the store back afterwards,
/// which is the second condition of #86: no failure of the service loses a request or moves it.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class WhenTheServiceMisbehavesTests
{
    private const string Address = "https://requests.nobody-else-names.invalid";
    private const string Key = "THE-KEY-THAT-AUTHENTICATES-THIS-SERVER";
    private const string StatusRoute = "/api/v1/status";
    private const string MeRoute = "/api/v1/auth/me";
    private const string RequestRoute = "/api/v1/request";
    private const string Refusal = "{\"status\":403,\"error\":\"You do not have permission to access this endpoint\"}";

    private static readonly Guid Asker = new Guid("86000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Noon.AddHours(1);

    /// <summary>
    /// A service that is up, reports a major version this adapter knows, and accepts the key is
    /// reachable, and finding that out is two calls in that order with the credential on both. The
    /// two versions are the two lines the adapter was written against.
    /// </summary>
    /// <param name="version">What the status route reports.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData("1.33.2")]
    [InlineData("2.7.3")]
    public async Task AServiceThatIsUpWithAKnownVersionAndAnAcceptedKeyIsReachable(string version)
    {
        var service = AService(version);
        using var backend = Backend(service);

        var reachability = await backend.CheckReachableAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(BackendReachability.Reachable, reachability);
        Assert.Equal([StatusRoute, MeRoute], service.Calls.Select(call => call.Path).ToArray());
        Assert.All(service.Calls, call => Assert.Equal(Key, call.Key));
    }

    /// <summary>
    /// A service that refuses the key is the refused-credential state, said once at error, without
    /// the key and without the body the service answered with. The status route takes no credential,
    /// so this is the answer that route could never give, and the form answers it with 403; 401 is
    /// read the same way for a proxy that answers first.
    /// </summary>
    /// <param name="refused">How the service refuses.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task AServiceThatRefusesTheKeyIsCredentialRefusedAndTheLogSaysSoWithoutTheKey(HttpStatusCode refused)
    {
        var log = new RecordingLogger();
        var service = AService(me: refused);
        using var backend = Backend(service, log);

        var reachability = await backend.CheckReachableAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(BackendReachability.CredentialRefused, reachability);
        Assert.Equal([StatusRoute, MeRoute], service.Calls.Select(call => call.Path).ToArray());

        var said = Assert.Single(log.At(LogLevel.Error));
        Assert.Contains("refused this server's key", said.Message, StringComparison.Ordinal);
        Assert.All(log.Lines, line => Assert.DoesNotContain(Key, Written(line), StringComparison.Ordinal));
        Assert.All(log.Lines, line => Assert.DoesNotContain("You do not have permission", Written(line), StringComparison.Ordinal));
    }

    /// <summary>
    /// A version this adapter does not know is the incompatible state, and the key is not even
    /// tried: a service of a form this adapter was never read against is not asked anything else.
    /// A major outside the known list, a development build that names itself with a word first, a
    /// status answer with no version in it, and a version that is not text are all that state.
    /// </summary>
    /// <param name="status">What the status route answers.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData("{\"version\":\"3.0.0\"}")]
    [InlineData("{\"version\":\"develop-1a2b3c4\"}")]
    [InlineData("{\"commitTag\":\"local\"}")]
    [InlineData("{\"version\":42}")]
    public async Task AVersionThisAdapterDoesNotKnowIsIncompatibleAndTheKeyIsNotEvenTried(string status)
    {
        var log = new RecordingLogger();
        var service = AnOverseerrService.ThatAnswersWith(call => call.Path == StatusRoute
            ? AnOverseerrService.Json(HttpStatusCode.OK, status)
            : AnOverseerrService.Json(HttpStatusCode.OK, "{\"id\":1}"));
        using var backend = Backend(service, log);

        var reachability = await backend.CheckReachableAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(BackendReachability.Incompatible, reachability);
        Assert.Equal([StatusRoute], service.Calls.Select(call => call.Path).ToArray());

        var said = Assert.Single(log.At(LogLevel.Error));
        Assert.Contains("version", said.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("develop", said.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every major the adapter declares it knows is one the check accepts, so the list and the check
    /// cannot drift apart; a change to one without the other reds here.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EveryMajorTheAdapterDeclaresIsOneTheCheckAccepts()
    {
        Assert.NotEmpty(OverseerrBackend.KnownMajorVersions);

        foreach (var major in OverseerrBackend.KnownMajorVersions)
        {
            using var backend = Backend(AService(major + ".0.0"));

            Assert.Equal(BackendReachability.Reachable, await backend.CheckReachableAsync(CancellationToken.None).ConfigureAwait(true));
        }
    }

    /// <summary>
    /// A service that is down is unreachable, the run walks nothing and says so at warning, and the
    /// next run asks again and gets on with it: nothing remembers the service as down, so it recovers
    /// without anybody acting. That is the whole of what "retried with a bound" means here, and the
    /// bound is the one every call carries. Between the two runs the request is exactly as it was.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AServiceThatIsDownIsUnreachableAndIsAskedAgainOnTheNextRun()
    {
        var down = true;
        var log = new RecordingLogger();
        var store = new InMemoryRequestStore();
        var handedOver = await AHandedOverRequestAsync(store, "1").ConfigureAwait(true);
        var service = AnOverseerrService.ThatAnswersWith(call => down
            ? throw new HttpRequestException("There is nothing listening at that address. (requests.nobody-else-names.invalid:443)")
            : Answer(call, reporting: new Dictionary<string, string> { ["1"] = "{\"id\":1,\"status\":4}" }));
        using var backend = Backend(service, log);

        var first = await ReconcilingAsync(store, backend, log).ConfigureAwait(true);
        var between = await Held(store, handedOver.Request.Id).ConfigureAwait(true);

        Assert.False(first.Asked);
        Assert.Equal(BackendReachability.Unreachable, first.Reachability);
        Assert.Equal(RequestState.Approved, between.Request.State);
        Assert.Equal(handedOver.Request.Backend, between.Request.Backend);
        Assert.Equal(handedOver.Revision, between.Revision);
        Assert.Contains(log.At(LogLevel.Warning), line => line.Message.Contains("next run will ask again", StringComparison.Ordinal));
        Assert.Empty(log.At(LogLevel.Error));

        down = false;

        var second = await ReconcilingAsync(store, backend, log).ConfigureAwait(true);
        var after = await Held(store, handedOver.Request.Id).ConfigureAwait(true);

        Assert.True(second.Asked);
        Assert.Equal(1, second.Examined);
        Assert.Equal(1, second.Moved);
        Assert.Equal(RequestState.Failed, after.Request.State);
    }

    /// <summary>
    /// A refused key stops the run: nothing is asked about any request, the run says so at error
    /// rather than warning, and every handed-over request is left exactly as it was, revision
    /// included. The service is told to say the one word that moves a request, so a run that asked
    /// would have moved it.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARefusedKeyStopsTheRunAtErrorAndLeavesEveryHandedOverRequestAsItIs()
    {
        var log = new RecordingLogger();
        var store = new InMemoryRequestStore();
        var handedOver = await AHandedOverRequestAsync(store, "1").ConfigureAwait(true);
        var service = AService(me: HttpStatusCode.Forbidden, reporting: new Dictionary<string, string> { ["1"] = "{\"id\":1,\"status\":4}" });
        using var backend = Backend(service, log);

        var report = await ReconcilingAsync(store, backend, log).ConfigureAwait(true);
        var held = await Held(store, handedOver.Request.Id).ConfigureAwait(true);

        Assert.False(report.Asked);
        Assert.Equal(BackendReachability.CredentialRefused, report.Reachability);
        Assert.Equal([StatusRoute, MeRoute], service.Calls.Select(call => call.Path).ToArray());
        Assert.Equal(RequestState.Approved, held.Request.State);
        Assert.Equal(handedOver.Revision, held.Revision);
        Assert.Contains(log.At(LogLevel.Error), line => line.Message.Contains("nothing will be until the key is corrected", StringComparison.Ordinal));
        Assert.Empty(log.At(LogLevel.Warning));
        Assert.All(log.Lines, line => Assert.DoesNotContain(Key, Written(line), StringComparison.Ordinal));
    }

    /// <summary>
    /// A version the adapter does not know stops the run the same way a refused key does, as an
    /// incompatibility said at error, with nothing asked and nothing moved.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnUnknownVersionStopsTheRunAsAnIncompatibility()
    {
        var log = new RecordingLogger();
        var store = new InMemoryRequestStore();
        var handedOver = await AHandedOverRequestAsync(store, "1").ConfigureAwait(true);
        var service = AService("3.0.0", reporting: new Dictionary<string, string> { ["1"] = "{\"id\":1,\"status\":4}" });
        using var backend = Backend(service, log);

        var report = await ReconcilingAsync(store, backend, log).ConfigureAwait(true);
        var held = await Held(store, handedOver.Request.Id).ConfigureAwait(true);

        Assert.False(report.Asked);
        Assert.Equal(BackendReachability.Incompatible, report.Reachability);
        Assert.Equal([StatusRoute], service.Calls.Select(call => call.Path).ToArray());
        Assert.Equal(RequestState.Approved, held.Request.State);
        Assert.Equal(handedOver.Revision, held.Revision);
        Assert.Contains(log.At(LogLevel.Error), line => line.Message.Contains("version this plugin does not know", StringComparison.Ordinal));
    }

    /// <summary>
    /// A title the service has never heard of fails that one handover and no other. The form answers
    /// such a submission with a 500 carrying its TMDB client's message, which <c>docs/bridge.md</c>
    /// reads off the route; here the first request is marked as one whose handover failed, with the
    /// approval standing and no reference, and the next approval is handed over as if nothing had
    /// happened. The service's own sentence is in no log line, because it is whatever the thing at
    /// that address chose to send.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ATitleTheServiceDoesNotKnowFailsThatHandoverAndTheNextOneGoesThrough()
    {
        var log = new RecordingLogger();
        var store = new InMemoryRequestStore();
        var unknown = await store.AddAsync(AFilm(new Guid("86000000-0000-0000-0000-0000000000aa"), tmdb: "603"), CancellationToken.None).ConfigureAwait(true);
        var known = await store.AddAsync(AFilm(new Guid("86000000-0000-0000-0000-0000000000bb"), tmdb: "550"), CancellationToken.None).ConfigureAwait(true);
        var service = AnOverseerrService.ThatAnswersWith(call => call.Body.Contains("603", StringComparison.Ordinal)
            ? AnOverseerrService.Json(HttpStatusCode.InternalServerError, "{\"status\":500,\"message\":\"[TMDB] Failed to fetch movie details: 404\"}")
            : AnOverseerrService.Json(HttpStatusCode.Created, "{\"id\":9,\"status\":2}"));
        using var backend = Backend(service, log);
        var submission = new BridgeSubmission(backend, store, new TestClock(Noon), log);

        var refused = await submission.SubmitAsync(unknown, CancellationToken.None).ConfigureAwait(true);
        var accepted = await submission.SubmitAsync(known, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(RequestState.Approved, refused.Request.State);
        Assert.Null(refused.Request.Backend);
        Assert.Equal(Noon, refused.Request.HandoverFailedAt);
        Assert.Equal("9", accepted.Request.Backend?.Id);
        Assert.Null(accepted.Request.HandoverFailedAt);
        Assert.Equal(2, service.Calls.Count(call => call.Path == RequestRoute));

        var said = Assert.Single(log.At(LogLevel.Error));
        Assert.Contains(unknown.Request.Id.ToString("D"), said.Message, StringComparison.Ordinal);
        Assert.All(log.Lines, line => Assert.DoesNotContain("[TMDB]", Written(line), StringComparison.Ordinal));
    }

    /// <summary>
    /// A reference the service no longer knows is a fact about that one request: it is left exactly
    /// as it is, and the others in the same run still move on what the service says about them.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AReferenceTheServiceNoLongerKnowsLeavesThatRequestAloneAndTheOthersStillMove()
    {
        var log = new RecordingLogger();
        var store = new InMemoryRequestStore();
        var forgotten = await AHandedOverRequestAsync(store, "1").ConfigureAwait(true);
        var remembered = await AHandedOverRequestAsync(store, "2").ConfigureAwait(true);
        var service = AService(reporting: new Dictionary<string, string> { ["2"] = "{\"id\":2,\"status\":4}" });
        using var backend = Backend(service, log);

        var report = await ReconcilingAsync(store, backend, log).ConfigureAwait(true);
        var left = await Held(store, forgotten.Request.Id).ConfigureAwait(true);
        var moved = await Held(store, remembered.Request.Id).ConfigureAwait(true);

        Assert.True(report.Asked);
        Assert.Equal(2, report.Examined);
        Assert.Equal(1, report.Moved);
        Assert.Equal(RequestState.Approved, left.Request.State);
        Assert.Equal(forgotten.Revision, left.Revision);
        Assert.Equal(RequestState.Failed, moved.Request.State);
        Assert.Empty(log.At(LogLevel.Error));
    }

    /// <summary>
    /// The two states that stop the bridge reach the operator's page as themselves, through the
    /// health answer and the sentence the catalogue carries for each, so an operator reads which of
    /// the two it is without opening a log. The third condition of #86, for the two failures the
    /// page did not have a word for before.
    /// </summary>
    /// <param name="version">What the status route reports.</param>
    /// <param name="me">What the route that returns whoever the key stands for answers.</param>
    /// <param name="expected">What the page is told.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData("2.7.3", HttpStatusCode.Forbidden, BackendReachability.CredentialRefused)]
    [InlineData("3.0.0", HttpStatusCode.OK, BackendReachability.Incompatible)]
    public async Task TheOperatorPageIsToldWhichOfTheTwoStoppedTheBridge(string version, HttpStatusCode me, BackendReachability expected)
    {
        var store = new InMemoryRequestStore();
        var clock = new TestClock(Noon);
        using var backend = Backend(AService(version, me));
        var health = new HealthController(
            store,
            backend,
            new FulfilmentSweep(store, new FakeLibrary(), clock, new RecordingJournal(), new RecordingSink(), new RecordingRequesterNotice(), new RecordingLogger()),
            new BridgeWatch(),
            clock);

        var answered = await health.HealthAsync(CancellationToken.None).ConfigureAwait(true);
        var answer = Assert.IsType<PluginHealth>(Assert.IsType<OkObjectResult>(answered.Result).Value);

        Assert.Equal(expected, answer.Bridge);
        Assert.Null(answer.BridgeLastReachableAt);

        var sentence = StringCatalogue.Shipped.For(culture: null)["queue.health.bridge." + expected];
        Assert.False(string.IsNullOrWhiteSpace(sentence));
        Assert.Contains("until", sentence, StringComparison.Ordinal);
    }

    /// <summary>
    /// A service that is up, of a known version, and accepts the key, and answers the given
    /// per-reference reports on the request route; anything else it is asked about is unknown to it.
    /// </summary>
    /// <param name="version">What the status route reports.</param>
    /// <param name="me">What the route that returns whoever the key stands for answers.</param>
    /// <param name="reporting">What the service says about each reference, by reference.</param>
    /// <returns>The service.</returns>
    private static AnOverseerrService AService(
        string version = "2.7.3",
        HttpStatusCode me = HttpStatusCode.OK,
        IReadOnlyDictionary<string, string>? reporting = null)
        => AnOverseerrService.ThatAnswersWith(call => call.Path switch
        {
            StatusRoute => AnOverseerrService.Json(HttpStatusCode.OK, "{\"version\":\"" + version + "\",\"commitTag\":\"local\"}"),
            MeRoute => AnOverseerrService.Json(me, me == HttpStatusCode.OK ? "{\"id\":1}" : Refusal),
            _ => Answer(call, reporting ?? new Dictionary<string, string>())
        });

    /// <summary>
    /// The answer to a question about one reference, where the status and key routes are already
    /// answered as up and accepted.
    /// </summary>
    /// <param name="call">The call as it arrived.</param>
    /// <param name="reporting">What the service says about each reference, by reference.</param>
    /// <returns>The answer.</returns>
    private static HttpResponseMessage Answer(AnOverseerrService.Call call, IReadOnlyDictionary<string, string> reporting)
    {
        if (call.Path == StatusRoute)
        {
            return AnOverseerrService.Json(HttpStatusCode.OK, "{\"version\":\"2.7.3\"}");
        }

        if (call.Path == MeRoute)
        {
            return AnOverseerrService.Json(HttpStatusCode.OK, "{\"id\":1}");
        }

        var id = call.Path.StartsWith(RequestRoute + "/", StringComparison.Ordinal)
            ? call.Path[(RequestRoute.Length + 1)..]
            : string.Empty;

        return reporting.TryGetValue(id, out var report)
            ? AnOverseerrService.Json(HttpStatusCode.OK, report)
            : AnOverseerrService.Json(HttpStatusCode.NotFound, "{\"message\":\"Request not found.\"}");
    }

    private static OverseerrBackend Backend(AnOverseerrService service, RecordingLogger? log = null)
        => new OverseerrBackend(
            new FakeInstallSettings(new PluginConfiguration { BridgeAddress = Address, BridgeApiKey = Key }),
            service,
            log ?? new RecordingLogger(),
            OverseerrBackend.DefaultAnswerWithin);

    private static Task<ReconciliationReport> ReconcilingAsync(IRequestStore store, IRequestBackend backend, RecordingLogger log)
        => new BridgeReconciliation(
            store,
            backend,
            new TestClock(Later),
            new RecordingJournal(),
            new RecordingRequesterNotice(),
            new BridgeWatch(),
            log).ReconcileAsync(CancellationToken.None);

    private static async Task<StoredRequest> Held(InMemoryRequestStore store, Guid id)
    {
        var stored = await store.GetAsync(id, CancellationToken.None).ConfigureAwait(false);

        return Assert.NotNull(stored);
    }

    private static string Written(RecordingLogger.Line line)
        => line.Message + (line.Exception?.ToString() ?? string.Empty);

    /// <summary>
    /// One approved request carrying the reference the service issued for it, which is the state a
    /// handover leaves behind and the only one a reconciliation asks about.
    /// </summary>
    /// <param name="store">Where it goes.</param>
    /// <param name="reference">What the service called it.</param>
    /// <returns>The request and the revision the store holds it at.</returns>
    private static Task<StoredRequest> AHandedOverRequestAsync(InMemoryRequestStore store, string reference)
        => store.AddAsync(
            AFilm(new Guid("86000000-0000-0000-0000-0000000000" + reference.PadLeft(2, '0')), tmdb: "593") with
            {
                Backend = new BackendReference { Service = OverseerrBackend.ServiceName, Id = reference }
            },
            CancellationToken.None);

    private static MediaRequest AFilm(Guid id, string tmdb)
        => new MediaRequest
        {
            Id = id,
            RequestedByUserId = Asker,
            RequestedAt = Noon,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = "Solaris",
            DisplayYear = 1972,
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = tmdb },
            State = RequestState.Approved,
            StateChangedAt = Noon
        };
}

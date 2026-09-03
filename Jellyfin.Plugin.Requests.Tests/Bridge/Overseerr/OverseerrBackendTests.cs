using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Bridge.Overseerr;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Bridge.Overseerr;

/// <summary>
/// The adapter that speaks the Overseerr form, driven through an in-process service so that no
/// socket is opened, which is the headless rule in <c>docs/testing.md</c>.
/// <para>
/// What these legs measure is what leaves the adapter and what it makes of what comes back: the
/// path, the header the credential travels in, the body of a submission, and the number a report
/// turns into a word. What they cannot measure is a running service, and <c>docs/bridge.md</c> says
/// what that costs; #315's last clause is that round trip and is not claimed here.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class OverseerrBackendTests
{
    private const string Address = "https://requests.nobody-else-names.invalid";
    private const string Key = "THE-KEY-THAT-AUTHENTICATES-THIS-SERVER";

    private static readonly Guid Asker = new Guid("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid Mapped = new Guid("a1000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// With no address written the adapter is the bridge with nothing behind it: every answer is the
    /// one <see cref="NoRequestBackend"/> gives, and the service in the same process sees nothing.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task NoAddressIsNoBridgeAndNothingIsDialled()
    {
        var service = AnOverseerrService.ThatAnswers(HttpStatusCode.OK);
        using var backend = Backend(service, new PluginConfiguration());
        var reference = new BackendReference { Service = OverseerrBackend.ServiceName, Id = "7" };

        Assert.Equal(BackendReachability.NotConfigured, await backend.CheckReachableAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Null(await backend.SubmitAsync(AFilm(), CancellationToken.None).ConfigureAwait(true));
        Assert.Null(await backend.ReportAsync(reference, CancellationToken.None).ConfigureAwait(true));
        await backend.WithdrawAsync(reference, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(service.Calls);
    }

    /// <summary>
    /// The status route answering is what reachable means, and it is asked at the form's own path
    /// under the address the operator wrote, with the credential in the header the form reads.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AServiceThatAnswersItsStatusRouteIsReachable()
    {
        var service = AnOverseerrService.ThatAnswers(HttpStatusCode.OK, "{\"version\":\"1.33.2\"}");
        using var backend = Backend(service);

        var reachability = await backend.CheckReachableAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(BackendReachability.Reachable, reachability);
        var call = Assert.Single(service.Calls);
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.Equal("/api/v1/status", call.Path);
        Assert.Equal(Key, call.Key);
    }

    /// <summary>
    /// An address written with a trailing slash and one written without dial the same path, because
    /// an operator copies the address out of a browser and a browser shows both.
    /// </summary>
    /// <param name="written">The address as the operator wrote it.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData(Address)]
    [InlineData(Address + "/")]
    public async Task ATrailingSlashOnTheAddressChangesNothing(string written)
    {
        var service = AnOverseerrService.ThatAnswers(HttpStatusCode.OK);
        using var backend = Backend(service, Configured(configuration => configuration.BridgeAddress = written));

        await backend.CheckReachableAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(Address + "/api/v1/status", Assert.Single(service.Calls).Address?.ToString());
    }

    /// <summary>
    /// Nothing listening is configured-and-did-not-answer, which the interface names so that a
    /// caller does not confuse it with nothing configured. The log says so, and says it without the
    /// credential.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AServiceWithNothingListeningIsUnreachableAndTheLogSaysSoWithoutTheKey()
    {
        var log = new RecordingLogger();
        using var backend = Backend(AnOverseerrService.ThatRefusesTheConnection(), log: log);

        var reachability = await backend.CheckReachableAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(BackendReachability.Unreachable, reachability);
        Assert.Contains(log.Lines, line => line.Message.Contains("could not be asked", StringComparison.Ordinal));
        Assert.All(log.Lines, line => Assert.DoesNotContain(Key, Written(line), StringComparison.Ordinal));
    }

    /// <summary>
    /// A service answering a failure to its own status route is not up, whatever the body says.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AServiceAnsweringAFailureToItsStatusRouteIsUnreachable()
    {
        using var backend = Backend(AnOverseerrService.ThatAnswers(HttpStatusCode.ServiceUnavailable));

        Assert.Equal(
            BackendReachability.Unreachable,
            await backend.CheckReachableAsync(CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>
    /// A service that never answers is given up on within the bound, and the caller gets the
    /// unreachable answer rather than a wait of the service's choosing. The bound is set to nothing
    /// here so the leg proves it bites rather than describes it.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AServiceThatNeverAnswersIsGivenUpOnWithinTheBound()
    {
        var service = AnOverseerrService.ThatNeverAnswers();
        using var backend = Backend(service, within: TimeSpan.FromMilliseconds(1));

        var reachability = await backend.CheckReachableAsync(CancellationToken.None).ConfigureAwait(true);

        service.Release();

        Assert.Equal(BackendReachability.Unreachable, reachability);
    }

    /// <summary>
    /// The adapter's own bound is a timeout and never a cancellation, because the reconciliation
    /// treats a cancellation as its own token having fired and stops the whole run on it. One slow
    /// answer is one request left as it is, not a run abandoned.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ASlowAnswerToAQuestionIsATimeoutAndNeverACancellation()
    {
        var service = AnOverseerrService.ThatNeverAnswers();
        using var backend = Backend(service, within: TimeSpan.FromMilliseconds(1));

        var failure = await Assert.ThrowsAsync<TimeoutException>(
            () => backend.ReportAsync(Issued("7"), CancellationToken.None)).ConfigureAwait(true);

        service.Release();

        Assert.Contains("did not answer", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, failure.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The caller's own token is honoured as a cancellation, on the null implementation and on the
    /// adapter alike, which the interface promises so that a caller correct against one is correct
    /// against the other.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheCallersOwnCancellationIsACancellation()
    {
        using var backend = Backend(AnOverseerrService.ThatNeverAnswers());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync().ConfigureAwait(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => backend.CheckReachableAsync(cancelled.Token)).ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => backend.SubmitAsync(AFilm(), cancelled.Token)).ConfigureAwait(true);
    }

    /// <summary>
    /// A film is posted in the form: the media type, the TMDB number, nothing else the form does not
    /// need, the credential in its header and nowhere in the address or the body. What the service
    /// answers with is what is kept, under this adapter's own name for the service.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AFilmIsPostedInTheFormAndTheNumberTheServiceAnswersWithIsKept()
    {
        var service = AnOverseerrService.ThatAnswers(HttpStatusCode.Created, "{\"id\":77,\"status\":2}");
        using var backend = Backend(service);

        var reference = await backend.SubmitAsync(AFilm(), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(OverseerrBackend.ServiceName, reference?.Service);
        Assert.Equal("77", reference?.Id);

        var call = Assert.Single(service.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("/api/v1/request", call.Path);
        Assert.Equal("application/json", call.MediaType);
        Assert.Equal(Key, call.Key);
        Assert.DoesNotContain(Key, call.Address?.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Key, call.Body, StringComparison.Ordinal);

        using var body = JsonDocument.Parse(call.Body);
        Assert.Equal("movie", body.RootElement.GetProperty("mediaType").GetString());
        Assert.Equal(603, body.RootElement.GetProperty("mediaId").GetInt64());
        Assert.False(body.RootElement.TryGetProperty("seasons", out _));
        Assert.False(body.RootElement.TryGetProperty("userId", out _));
    }

    /// <summary>
    /// A series carries the seasons that were asked for, and the whole show is asked for by the
    /// form's own word for it rather than by a list this side would have to know the length of.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ASeriesCarriesTheSeasonsAskedForAndTheWholeShowWhereNoneWereNamed()
    {
        var service = AnOverseerrService.ThatAnswers(HttpStatusCode.Created, "{\"id\":1,\"status\":1}");
        using var backend = Backend(service);

        await backend.SubmitAsync(ASeries([1, 3]), CancellationToken.None).ConfigureAwait(true);
        await backend.SubmitAsync(ASeries([]), CancellationToken.None).ConfigureAwait(true);

        using var some = JsonDocument.Parse(service.Calls[0].Body);
        using var all = JsonDocument.Parse(service.Calls[1].Body);

        Assert.Equal("tv", some.RootElement.GetProperty("mediaType").GetString());
        Assert.Equal([1, 3], some.RootElement.GetProperty("seasons").EnumerateArray().Select(season => season.GetInt32()).ToArray());
        Assert.Equal("all", all.RootElement.GetProperty("seasons").GetString());
    }

    /// <summary>
    /// A person with a row arrives under the account the operator wrote for them, as the form's own
    /// numeric user identifier. A person with no row arrives under nobody's: no user identifier of
    /// any kind is on the wire, which is the decision on #113 and what <c>docs/personal-data.md</c>
    /// counts as leaving.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AMappedPersonArrivesUnderTheirAccountAndAnUnmappedOneUnderNobodys()
    {
        var service = AnOverseerrService.ThatAnswers(HttpStatusCode.Created, "{\"id\":1,\"status\":1}");
        using var backend = Backend(service, Configured(configuration =>
            configuration.BridgeAccounts.Add(new BridgeAccountRow { UserId = Mapped, Account = "42" })));

        await backend.SubmitAsync(AFilm(by: Mapped), CancellationToken.None).ConfigureAwait(true);
        await backend.SubmitAsync(AFilm(by: Asker), CancellationToken.None).ConfigureAwait(true);

        using var mapped = JsonDocument.Parse(service.Calls[0].Body);
        using var unmapped = JsonDocument.Parse(service.Calls[1].Body);

        Assert.Equal(42, mapped.RootElement.GetProperty("userId").GetInt64());
        Assert.False(unmapped.RootElement.TryGetProperty("userId", out _));
        Assert.DoesNotContain(Asker.ToString("D"), service.Calls[1].Body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The form identifies a title by its TMDB number and nothing else, so a request carrying only
    /// another provider's number is refused before anything is sent. That is the first of the three
    /// answers <c>docs/bridge.md</c> sets out, and the refusal names what is missing.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARequestWithNoTmdbIdentifierIsRefusedBeforeAnythingIsSent()
    {
        var service = AnOverseerrService.ThatAnswers(HttpStatusCode.Created, "{\"id\":1}");
        using var backend = Backend(service);
        var onlyImdb = AFilm(identifiers: new Dictionary<string, string> { ["Imdb"] = "tt0133093" });

        var refusal = await Assert.ThrowsAsync<HandoverRefusedException>(
            () => backend.SubmitAsync(onlyImdb, CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains("TMDB", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(service.Calls);
    }

    /// <summary>
    /// The provider name is matched without case, because the same provider is spelled two ways by
    /// different callers and the identity rule already treats them as one.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheProviderNameIsMatchedWithoutCase()
    {
        var service = AnOverseerrService.ThatAnswers(HttpStatusCode.Created, "{\"id\":1}");
        using var backend = Backend(service);

        await backend.SubmitAsync(
            AFilm(identifiers: new Dictionary<string, string> { ["tmdb"] = "550" }),
            CancellationToken.None).ConfigureAwait(true);

        using var body = JsonDocument.Parse(Assert.Single(service.Calls).Body);
        Assert.Equal(550, body.RootElement.GetProperty("mediaId").GetInt64());
    }

    /// <summary>
    /// A mapping row whose account is not a number is refused before anything is sent, because the
    /// form identifies its users by number. The refusal names the person by this server's identifier
    /// and never quotes the account text, which is the operator's string for the other side.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AMappedAccountThatIsNotANumberIsRefusedBeforeAnythingIsSent()
    {
        var service = AnOverseerrService.ThatAnswers(HttpStatusCode.Created, "{\"id\":1}");
        using var backend = Backend(service, Configured(configuration =>
            configuration.BridgeAccounts.Add(new BridgeAccountRow { UserId = Mapped, Account = "alice-over-there" })));

        var refusal = await Assert.ThrowsAsync<HandoverRefusedException>(
            () => backend.SubmitAsync(AFilm(by: Mapped), CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains(Mapped.ToString("D"), refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("alice-over-there", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(service.Calls);
    }

    /// <summary>
    /// A refused submission is a failure carrying the status the service answered, and never the
    /// credential or the body of the answer. The status is what an operator acts on: a refused key
    /// and a title the service does not know are two different numbers.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARefusedSubmissionCarriesTheStatusAndNeverTheKey()
    {
        using var backend = Backend(AnOverseerrService.ThatAnswers(HttpStatusCode.Unauthorized, "{\"message\":\"the key was refused\"}"));

        var failure = await Assert.ThrowsAsync<HttpRequestException>(
            () => backend.SubmitAsync(AFilm(), CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        Assert.Contains("401", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("the key was refused", failure.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A body that is not JSON is a failure and never a reference. It is what a reverse proxy answers
    /// with when the service behind it is gone, and it is one of the two cases #35 named.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnAnswerThatIsNotJsonIsAFailureRatherThanAReference()
    {
        using var backend = Backend(AnOverseerrService.ThatAnswersWith(_ => AnOverseerrService.Html(HttpStatusCode.OK, "<html><body>502 Bad Gateway</body></html>")));

        var failure = await Assert.ThrowsAsync<InvalidDataException>(
            () => backend.SubmitAsync(AFilm(), CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains("not JSON", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("<html>", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// JSON of the wrong shape is a failure and never a reference, which is the other case #35 named.
    /// The sentence says what is at stake: the service may hold the request and this side has nothing
    /// to ask about it with.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnAnswerOfTheWrongShapeIsAFailureRatherThanAReference()
    {
        using var backend = Backend(AnOverseerrService.ThatAnswers(HttpStatusCode.Created, "{\"request\":{\"id\":5}}"));

        var failure = await Assert.ThrowsAsync<InvalidDataException>(
            () => backend.SubmitAsync(AFilm(), CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains("no identifier", failure.Message, StringComparison.Ordinal);
        Assert.Contains("reconciled by hand", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A report turns the number the form reports into the word the mapping table holds, at the one
    /// place that step lives, and the word is one the table then moves a request on. The media
    /// status beside it is not reported: the report carries one word, and the request's own status
    /// is the one that says what happened to the request.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AReportTurnsTheNumberIntoTheWordTheTableHolds()
    {
        var service = AnOverseerrService.ThatAnswers(HttpStatusCode.OK, "{\"id\":77,\"status\":4,\"media\":{\"status\":5}}");
        using var backend = Backend(service);

        var report = await backend.ReportAsync(Issued("77"), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("FAILED", report?.Reported);
        Assert.Equal(RequestState.Failed, BackendStates.Lookup(BackendVocabulary.RequestStatus, report!)?.MoveTo);

        var call = Assert.Single(service.Calls);
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.Equal("/api/v1/request/77", call.Path);
        Assert.Equal(Key, call.Key);
    }

    /// <summary>
    /// A number nothing here knows is reported as its own digits, so the mapping table's rule for an
    /// unseen word is what answers rather than a guess at the nearest word. Both vocabularies answer
    /// with no row for it.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AReportOfANumberNothingKnowsCarriesTheNumberAndMovesNothing()
    {
        using var backend = Backend(AnOverseerrService.ThatAnswers(HttpStatusCode.OK, "{\"id\":77,\"status\":9}"));

        var report = await backend.ReportAsync(Issued("77"), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("9", report?.Reported);
        Assert.Null(BackendStates.Lookup(BackendVocabulary.RequestStatus, report!));
        Assert.Null(BackendStates.Lookup(BackendVocabulary.MediaStatus, report!));
    }

    /// <summary>
    /// A reference the service does not know is no report, which the interface names as an ordinary
    /// answer: it is what a reference issued by an install the operator has since wiped looks like.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AReferenceTheServiceDoesNotKnowIsNoReport()
    {
        using var backend = Backend(AnOverseerrService.ThatAnswers(HttpStatusCode.NotFound, "{\"message\":\"Request not found\"}"));

        Assert.Null(await backend.ReportAsync(Issued("404"), CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>
    /// A report answered with no status is a failure rather than a word, because a word invented
    /// here would be the guess the mapping table exists against.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AReportWithNoStatusIsAFailureRatherThanAWord()
    {
        using var backend = Backend(AnOverseerrService.ThatAnswers(HttpStatusCode.OK, "{\"id\":77}"));

        var failure = await Assert.ThrowsAsync<InvalidDataException>(
            () => backend.ReportAsync(Issued("77"), CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains("no status", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A reference another service issued is never asked about here and never withdrawn here: a
    /// number means something only to the service that issued it, and asking this one about it
    /// would be asking about whatever happens to carry that number over there.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AReferenceAnotherServiceIssuedIsNeitherAskedAboutNorWithdrawn()
    {
        var service = AnOverseerrService.ThatAnswers(HttpStatusCode.OK, "{\"id\":7,\"status\":3}");
        using var backend = Backend(service);
        var theirs = new BackendReference { Service = "somebody-elses-service", Id = "7" };

        Assert.Null(await backend.ReportAsync(theirs, CancellationToken.None).ConfigureAwait(true));
        await backend.WithdrawAsync(theirs, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(service.Calls);
    }

    /// <summary>
    /// A withdrawal is a delete of the form's own path, gone already is the state the caller wanted
    /// and so not a failure, and anything else the service answers is one.
    /// </summary>
    /// <param name="status">What the service answers.</param>
    /// <param name="fails">Whether that is a failure here.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData(HttpStatusCode.NoContent, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    public async Task AWithdrawalSendsDeleteAndOnlyARefusalIsAFailure(HttpStatusCode status, bool fails)
    {
        var service = AnOverseerrService.ThatAnswers(status, string.Empty);
        using var backend = Backend(service);

        var withdrawing = backend.WithdrawAsync(Issued("77"), CancellationToken.None);

        if (fails)
        {
            var failure = await Assert.ThrowsAsync<HttpRequestException>(() => withdrawing).ConfigureAwait(true);
            Assert.Equal(status, failure.StatusCode);
        }
        else
        {
            await withdrawing.ConfigureAwait(true);
        }

        var call = Assert.Single(service.Calls);
        Assert.Equal(HttpMethod.Delete, call.Method);
        Assert.Equal("/api/v1/request/77", call.Path);
    }

    /// <summary>
    /// Across all four calls the credential is in the header and nowhere else: not in an address,
    /// not in a body. This is the guard that keeps the platform's own exception, which names the
    /// destination, from carrying the key into a log, and it is asserted over every call rather
    /// than the one somebody thought of.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task NoCallCarriesTheKeyAnywhereButTheHeader()
    {
        var service = AnOverseerrService.ThatAnswers(HttpStatusCode.OK, "{\"id\":1,\"status\":2}");
        using var backend = Backend(service);

        await backend.CheckReachableAsync(CancellationToken.None).ConfigureAwait(true);
        await backend.SubmitAsync(AFilm(), CancellationToken.None).ConfigureAwait(true);
        await backend.ReportAsync(Issued("1"), CancellationToken.None).ConfigureAwait(true);
        await backend.WithdrawAsync(Issued("1"), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(4, service.Calls.Count);
        Assert.All(service.Calls, call =>
        {
            Assert.Equal(Key, call.Key);
            Assert.DoesNotContain(Key, call.Address?.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(Key, call.Body, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A handover that fails on the wire is logged by the submission path with the whole exception,
    /// and no part of the credential is in it at any level. This is the second condition of #85,
    /// measured against the failure the platform raises rather than a sentence this tree wrote.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AFailedHandoverWritesNoPartOfTheKeyToTheLog()
    {
        var log = new RecordingLogger();
        var store = new InMemoryRequestStore();
        using var backend = Backend(AnOverseerrService.ThatRefusesTheConnection(), log: log);
        var approved = await store.AddAsync(AFilm() with { State = RequestState.Approved }, CancellationToken.None).ConfigureAwait(true);
        var submission = new BridgeSubmission(backend, store, new TestClock(Noon), log);

        var after = await submission.SubmitAsync(approved, CancellationToken.None).ConfigureAwait(true);

        Assert.Null(after.Request.Backend);
        Assert.Equal(Noon, after.Request.HandoverFailedAt);
        Assert.Contains(log.Lines, line => line.Message.Contains("could not be handed", StringComparison.Ordinal) && line.Exception is not null);
        Assert.All(log.Lines, line => Assert.DoesNotContain(Key, Written(line), StringComparison.Ordinal));
    }

    private static OverseerrBackend Backend(
        AnOverseerrService service,
        PluginConfiguration? configuration = null,
        RecordingLogger? log = null,
        TimeSpan? within = null)
        => new OverseerrBackend(
            new FakeInstallSettings(configuration ?? Configured(_ => { })),
            service,
            log ?? new RecordingLogger(),
            within ?? OverseerrBackend.DefaultAnswerWithin);

    private static PluginConfiguration Configured(Action<PluginConfiguration> change)
    {
        var configuration = new PluginConfiguration
        {
            BridgeAddress = Address,
            BridgeApiKey = Key
        };

        change(configuration);

        return configuration;
    }

    private static BackendReference Issued(string id)
        => new BackendReference { Service = OverseerrBackend.ServiceName, Id = id };

    private static string Written(RecordingLogger.Line line)
        => line.Message + (line.Exception?.ToString() ?? string.Empty);

    private static MediaRequest AFilm(Guid? by = null, IReadOnlyDictionary<string, string>? identifiers = null)
        => new MediaRequest
        {
            Id = new Guid("a1000000-0000-0000-0000-0000000000aa"),
            RequestedByUserId = by ?? Asker,
            RequestedAt = Noon,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = "The Matrix",
            DisplayYear = 1999,
            ProviderIds = identifiers ?? new Dictionary<string, string> { ["Tmdb"] = "603" },
            State = RequestState.Approved,
            StateChangedAt = Noon
        };

    private static MediaRequest ASeries(IReadOnlyList<int> seasons)
        => new MediaRequest
        {
            Id = new Guid("a1000000-0000-0000-0000-0000000000bb"),
            RequestedByUserId = Asker,
            RequestedAt = Noon,
            Kind = RequestedItemKind.Series,
            DisplayTitle = "Twin Peaks",
            DisplayYear = 1990,
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "1920" },
            Seasons = new ReadOnlyCollection<int>([.. seasons]),
            State = RequestState.Approved,
            StateChangedAt = Noon
        };
}

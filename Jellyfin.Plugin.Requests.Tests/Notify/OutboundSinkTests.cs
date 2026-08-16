using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Notify;

/// <summary>
/// The one path anything this plugin has to say leaves the server on, and the property that makes it
/// safe to have: nothing an endpoint does reaches a request.
/// <para>
/// Every leg runs against an endpoint inside this process, reached through the sink's own client and
/// its own handler pipeline, so the serialisation, the bound on a send and the error handling are
/// the real ones and no socket is opened. Nothing here waits on a clock: the endpoint that never
/// answers holds the send until the test lets go of it, and the bound is proven by handing the sink
/// no time at all rather than by outlasting one.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class OutboundSinkTests
{
    private const string Address = "https://example.invalid/hook";

    private static readonly Guid Asker = new Guid("78000000-0000-0000-0000-000000000001");
    private static readonly Guid Operator = new Guid("78000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A fresh install sends nothing, because nobody has said where to. This is the whole of how the
    /// path is off, and it is asserted at the endpoint rather than off the setting, so it still says
    /// something the day somebody adds a second way to configure an address.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnInstallWhereNobodyTypedAnAddressSendsNothing()
    {
        var endpoint = ASinkEndpoint.ThatAccepts();
        using var sink = Sink(endpoint, address: string.Empty);

        Assert.False(sink.IsConfigured);

        sink.Announce(Notice());
        await sink.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(endpoint.Received);
    }

    /// <summary>
    /// An address that is not somewhere a notice can be posted is no sink either.
    /// <para>
    /// The rules refuse such a value when it arrives, so this is the second reading rather than the
    /// only one. What it is for is the case the first cannot reach: a configuration file edited by
    /// hand on a server that is already running.
    /// </para>
    /// </summary>
    /// <param name="written">What is in the configuration file.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData("   ")]
    [InlineData("example.invalid/hook")]
    [InlineData("/hook")]
    [InlineData("file:///tmp/hook")]
    [InlineData("mailto:somebody@example.invalid")]
    public async Task AnAddressANoticeCannotBePostedToIsNoSinkAtAll(string written)
    {
        var endpoint = ASinkEndpoint.ThatAccepts();
        using var sink = Sink(endpoint, written);

        Assert.False(sink.IsConfigured);

        sink.Announce(Notice());
        await sink.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(endpoint.Received);
    }

    /// <summary>
    /// With an address, the document is posted to it as JSON, once.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheDocumentIsPostedAsJsonToTheAddressTheOperatorTyped()
    {
        var endpoint = ASinkEndpoint.ThatAccepts();
        using var sink = Sink(endpoint, Address);

        Assert.True(sink.IsConfigured);

        sink.Announce(Notice());
        await sink.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        var sent = Assert.Single(endpoint.Received);

        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(new Uri(Address), sent.Address);
        Assert.Equal("application/json", sent.MediaType);

        using var read = JsonDocument.Parse(sent.Body);

        Assert.Equal(OutboundNotice.CurrentVersion, read.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("Approved", read.RootElement.GetProperty("event").GetString());
    }

    /// <summary>
    /// An endpoint that has taken the connection and is saying nothing does not hold whoever
    /// announced.
    /// <para>
    /// This is the second condition of #78 in the form that can be watched. The request is moved and
    /// written while the endpoint is still holding the send, and both assertions run before it is let
    /// go. An announcement that handed its caller something to await would fail here rather than
    /// somewhere subtle, which is why the interface hands back nothing.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnEndpointStillHoldingTheSendDoesNotHoldTheRequestThatWasAnnounced()
    {
        var endpoint = ASinkEndpoint.ThatNeverAnswers();
        using var sink = Sink(endpoint, Address);
        var store = new InMemoryRequestStore();

        var stored = await store.AddAsync(Requested(), CancellationToken.None).ConfigureAwait(true);

        sink.Announce(OutboundNotice.For(stored.Request, NoticeEvent.Asked));

        // The endpoint has the send and is not going to answer it. Everything below happens anyway.
        await endpoint.Entered.ConfigureAwait(true);

        var approved = RequestLifecycle.Move(
            stored.Request,
            RequestState.Approved,
            Noon,
            RequestCaller.Administrator(Operator));

        var written = await store
            .ReplaceAsync(approved, stored.Revision, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(RequestState.Approved, written.Request.State);

        var held = await store.GetAsync(stored.Request.Id, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(RequestState.Approved, held?.Request.State);

        endpoint.Release();
        await sink.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        // And it stayed moved after the endpoint finally answered, which is the half of the
        // condition about a notification path reversing anything.
        held = await store.GetAsync(stored.Request.Id, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(RequestState.Approved, held?.Request.State);
    }

    /// <summary>
    /// An endpoint that is not there raises nothing at the caller and leaves the request where the
    /// operator put it. The failure is written to the log instead, which is where somebody whose
    /// messages stopped arriving looks.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnEndpointThatRefusesTheConnectionLeavesTheRequestMovedAndSaysSoInTheLog()
    {
        var endpoint = ASinkEndpoint.ThatRefusesTheConnection();
        var log = new RecordingLogger();
        using var sink = Sink(endpoint, Address, log);
        var store = new InMemoryRequestStore();

        var stored = await store.AddAsync(Requested(), CancellationToken.None).ConfigureAwait(true);

        var approved = RequestLifecycle.Move(
            stored.Request,
            RequestState.Approved,
            Noon,
            RequestCaller.Administrator(Operator));

        var written = await store
            .ReplaceAsync(approved, stored.Revision, CancellationToken.None)
            .ConfigureAwait(true);

        sink.Announce(OutboundNotice.For(written.Request, NoticeEvent.Approved));
        await sink.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        var held = await store.GetAsync(stored.Request.Id, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(RequestState.Approved, held?.Request.State);
        Assert.Contains(log.Lines, line => line.Message.Contains("could not deliver", StringComparison.Ordinal));
    }

    /// <summary>
    /// An endpoint that never answers is given up on rather than waited out.
    /// <para>
    /// The bound is handed in as nothing, so the send is abandoned the moment it is made and the leg
    /// needs no clock. With the bound removed this leg does not fail slowly, it does not finish at
    /// all, which is the failure the bound exists against.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnEndpointThatNeverAnswersIsGivenUpOnRatherThanWaitedOut()
    {
        var endpoint = ASinkEndpoint.ThatNeverAnswers();
        var log = new RecordingLogger();
        using var sink = Sink(endpoint, Address, log, TimeSpan.Zero);

        sink.Announce(Notice());

        await sink.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Contains(log.Lines, line => line.Message.Contains("could not deliver", StringComparison.Ordinal));

        endpoint.Release();
    }

    /// <summary>
    /// An endpoint that answers with a failure is a message that did not arrive and nothing more.
    /// The status is in the log so an operator can tell a wrong address from a service that is down.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnEndpointAnsweringWithAFailureIsAMessageLostAndNothingElse()
    {
        var endpoint = ASinkEndpoint.ThatAnswers(HttpStatusCode.InternalServerError);
        var log = new RecordingLogger();
        using var sink = Sink(endpoint, Address, log);

        sink.Announce(Notice());
        await sink.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Single(endpoint.Received);
        Assert.Contains(
            log.Lines,
            line => line.Message.Contains(
                ((int)HttpStatusCode.InternalServerError).ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Every announcement is sent. Nothing is dropped because something was already in flight and
    /// nothing is collapsed into one message, so five movements in the queue are five documents at
    /// the endpoint.
    /// <para>
    /// What this leg does not prove is that waiting for quiet waits for all of them rather than for
    /// the last one. Five sends against an endpoint that answers immediately all finish either way,
    /// so a sink that tracked only the most recent delivery passes this leg. That property is held
    /// by the chaining in <c>Both</c> and by nothing that fails when it is removed, and saying so is
    /// cheaper than a leg that looks like a guard and is not one.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EveryAnnouncementIsSentRatherThanDroppedWhileAnotherIsInFlight()
    {
        var endpoint = ASinkEndpoint.ThatAccepts();
        using var sink = Sink(endpoint, Address);

        for (var i = 0; i < 5; i++)
        {
            sink.Announce(Notice());
        }

        await sink.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(5, endpoint.Received.Count);
    }

    private static OutboundSink Sink(
        HttpMessageHandler endpoint,
        string address,
        RecordingLogger? log = null,
        TimeSpan? answerWithin = null)
        => new OutboundSink(
            new FakeInstallSettings(new PluginConfiguration { OutboundNoticeAddress = address }),
            endpoint,
            log ?? new RecordingLogger(),
            answerWithin ?? OutboundSink.DefaultAnswerWithin);

    private static OutboundNotice Notice()
        => OutboundNotice.For(
            Requested() with
            {
                State = RequestState.Approved,
                StateChangedAt = Noon,
                StateChangedByUserId = Operator
            },
            NoticeEvent.Approved);

    private static MediaRequest Requested()
        => new MediaRequest
        {
            Id = new Guid("78000000-0000-0000-0000-0000000000aa"),
            RequestedByUserId = Asker,
            RequestedAt = Noon,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = "Solaris",
            DisplayYear = 1972,
            ProviderIds = new Dictionary<string, string> { ["Imdb"] = "tt0069293" },
            StateChangedAt = Noon
        };
}

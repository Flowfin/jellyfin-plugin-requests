using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Notify;

/// <summary>
/// The switch a person owns over what this plugin pushes at them about their own request: who it
/// obeys, who may move it, and what it does on an install nobody has touched.
/// <para>
/// The rule it is built to keep is #9's decision of 2026-08-24. The person who made the request owns
/// this, default on, and no operator setting overrides it - which is why the leg that has an
/// administrator try is here rather than in a document.
/// </para>
/// <para>
/// <b>No order is promised over two people, and #330 is where that was decided rather than left
/// open.</b> Two readings were available: compare what was told as a set, or make
/// <see cref="QuietedRequesterNotice"/> promise the order its caller handed the messages over in.
/// The second is refused, because keeping such a promise means holding the second message until the
/// first person's setting has been read, and reading a setting off the calling thread is the whole
/// of what that class exists to do. So a caller that has just moved two requests is entitled to have
/// both people told, exactly once each, and to nothing about which of them hears first. Every leg
/// below therefore compares <see cref="SortedRecipients"/> rather than the sequence as it arrived,
/// and a leg that reintroduced a sequence would be asserting something the subject does not offer.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class APersonsOwnSwitchTests
{
    private static readonly Guid Asker = new Guid("f2000000-0000-0000-0000-000000000001");
    private static readonly Guid Somebody = new Guid("f2000000-0000-0000-0000-000000000002");
    private static readonly Guid Administrator = new Guid("f2000000-0000-0000-0000-000000000003");

    /// <summary>
    /// A person who has turned it off is not told, and everybody else still is.
    /// <para>
    /// Both halves in one leg on purpose. A switch that silenced everybody would pass the first half
    /// alone, and it is the failure a person would notice last.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SomebodyWhoTurnedItOffIsNotToldAndNobodyElseIsAffected()
    {
        var inner = new RecordingRequesterNotice();
        var switched = new QuietedRequesterNotice(inner, new InMemoryNoticePreferences(Asker), new RecordingLogger());

        switched.Tell(Message(Asker));
        switched.Tell(Message(Somebody));

        await switched.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal([Somebody], SortedRecipients(inner));
    }

    /// <summary>
    /// An install nobody has touched tells everybody, which is what it did before this existed.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnInstallNobodyHasTouchedTellsEverybody()
    {
        var inner = new RecordingRequesterNotice();
        var switched = new QuietedRequesterNotice(inner, new InMemoryNoticePreferences(), new RecordingLogger());

        switched.Tell(Message(Asker));
        switched.Tell(Message(Somebody));

        await switched.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal([Asker, Somebody], SortedRecipients(inner));
    }

    /// <summary>
    /// A setting that cannot be read silences the message and says so in the log.
    /// <para>
    /// The two ways of being wrong are not equal. Not sending a courtesy costs somebody a line they
    /// would have read on their own page anyway; sending it costs a person who asked not to be told
    /// being told, which is the whole of what the switch is for. So the failure goes the quiet way,
    /// and it is written to the log so an operator meets a file to repair rather than silence
    /// nobody can explain.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ASettingThatCannotBeReadDropsTheMessageAndIsReported()
    {
        var inner = new RecordingRequesterNotice();
        var logger = new RecordingLogger();
        var switched = new QuietedRequesterNotice(inner, new NoticePreferencesThatCannotBeRead(), logger);

        switched.Tell(Message(Asker));

        await switched.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(inner.Told);
        Assert.Single(logger.At(LogLevel.Error));
        Assert.DoesNotContain(Asker.ToString(), logger.At(LogLevel.Error)[0].Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The endpoint answers the caller's own setting, and it is on where they have never touched it.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheEndpointAnswersTheCallersOwnSetting()
    {
        var preferences = new InMemoryNoticePreferences(Somebody);

        Assert.True(await ReadAsync(preferences, Asker).ConfigureAwait(true));
        Assert.False(await ReadAsync(preferences, Somebody).ConfigureAwait(true));
    }

    /// <summary>
    /// Turning it off through the endpoint is what the notice path then obeys.
    /// <para>
    /// Through both halves rather than by reading the store, because what this issue asks for is
    /// that a person can turn the message off from a surface they can reach: a setting written by
    /// one and ignored by the other is two mechanisms that agree today.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task WhatSomebodySetsThroughTheEndpointIsWhatTheNoticePathObeys()
    {
        var preferences = new InMemoryNoticePreferences();
        var inner = new RecordingRequesterNotice();
        var switched = new QuietedRequesterNotice(inner, preferences, new RecordingLogger());

        Assert.False(await SetAsync(preferences, Asker, tellsMe: false).ConfigureAwait(true));

        switched.Tell(Message(Asker));
        await switched.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(inner.Told);

        Assert.True(await SetAsync(preferences, Asker, tellsMe: true).ConfigureAwait(true));

        switched.Tell(Message(Asker));
        await switched.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal([Asker], SortedRecipients(inner));
    }

    /// <summary>
    /// An administrator calling this endpoint changes their own setting and nobody else's.
    /// <para>
    /// This is the leg #287's second condition asks for, and what it can assert is stronger than a
    /// refusal: there is no call that reaches somebody else's setting, because there is no field,
    /// route segment or parameter on either endpoint that names a person. So the administrator here
    /// sends the body an administrator would send if they wanted to silence the asker, and what
    /// moves is the administrator's own row.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnAdministratorTurningItOffTurnsOffTheirOwn()
    {
        var preferences = new InMemoryNoticePreferences();

        Assert.False(await SetAsync(preferences, Administrator, tellsMe: false).ConfigureAwait(true));

        Assert.True(await ReadAsync(preferences, Asker).ConfigureAwait(true));
        Assert.False(await ReadAsync(preferences, Administrator).ConfigureAwait(true));

        // And the notice path agrees with the store, so nothing about the asker went quiet.
        var inner = new RecordingRequesterNotice();
        var switched = new QuietedRequesterNotice(inner, preferences, new RecordingLogger());

        switched.Tell(Message(Asker));
        switched.Tell(Message(Administrator));
        await switched.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal([Asker], SortedRecipients(inner));
    }

    /// <summary>
    /// A body that says nothing is refused rather than read as a refusal to be told.
    /// <para>
    /// The direction where a mistake is not noticed. A client sending an empty object would
    /// otherwise silence the person, and the person would find out by not being told something.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ABodyThatSaysNothingIsRefusedRatherThanTakenAsOff()
    {
        var preferences = new InMemoryNoticePreferences();
        var answered = await ControllerFor(preferences, Asker)
            .SetMineAsync(new SetMyNoticeBody(), CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(RequestFailureCode.InvalidBody, Refused(answered).Code);
        Assert.Equal(0, preferences.Writes);
        Assert.True(await ReadAsync(preferences, Asker).ConfigureAwait(true));
    }

    /// <summary>
    /// A call that authenticated and names no person is refused rather than kept against nobody.
    /// <para>
    /// An API key reaches this endpoint under the server's default policy and there is nobody whose
    /// setting it would be. Writing it against the empty identifier would silence whoever that
    /// identifier is next taken for, which is the entry the store refuses on its way back in.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ACallNamingNobodyIsRefusedOnBothEndpoints()
    {
        var preferences = new InMemoryNoticePreferences();

        var read = await ControllerFor(preferences, caller: null).MineAsync(CancellationToken.None).ConfigureAwait(true);
        var written = await ControllerFor(preferences, caller: null)
            .SetMineAsync(new SetMyNoticeBody { TellsMe = false }, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(RequestFailureCode.NoUserOnTheCall, Refused(read).Code);
        Assert.Equal(RequestFailureCode.NoUserOnTheCall, Refused(written).Code);
        Assert.Equal(0, preferences.Writes);
    }

    /// <summary>
    /// A setting that cannot be read is an unavailable answer rather than an exception out of the
    /// endpoint, and nothing of the refusal reaches the caller.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ASettingThatCannotBeReadIsAnswerAsUnavailable()
    {
        var refusing = new NoticePreferencesThatCannotBeRead();

        var read = await ControllerFor(refusing, Asker).MineAsync(CancellationToken.None).ConfigureAwait(true);
        var written = await ControllerFor(refusing, Asker)
            .SetMineAsync(new SetMyNoticeBody { TellsMe = false }, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(RequestFailureCode.TheStoreCouldNotBeRead, Refused(read).Code);
        Assert.Equal(RequestFailureCode.TheStoreCouldNotBeRead, Refused(written).Code);
    }

    /// <summary>
    /// Who was told, sorted here rather than left in the order the messages arrived.
    /// <para>
    /// The sort is what makes a leg of this class independent of which of two tasks finished first,
    /// and it is a comparison of sequences rather than of sets on purpose: a message kept twice or
    /// lost moves the sorted answer as readily as it moves the unsorted one, so the count and the
    /// membership are still asserted and only the order is given up.
    /// </para>
    /// <para>
    /// The identifiers this class uses differ in their last byte and sort ordinally in the order
    /// they are declared, so the expectations below read in the obvious order.
    /// </para>
    /// </summary>
    /// <param name="inner">The path that kept what it was told.</param>
    /// <returns>The people who were told, ordinally by identifier.</returns>
    private static Guid[] SortedRecipients(RecordingRequesterNotice inner)
        => [.. inner.Told
            .Select(message => message.ToUserId)
            .OrderBy(id => id.ToString(), StringComparer.Ordinal)];

    /// <summary>
    /// One message for one person.
    /// </summary>
    /// <param name="toUserId">Who it is for.</param>
    /// <returns>The message.</returns>
    private static RequesterMessage Message(Guid toUserId)
        => new RequesterMessage { ToUserId = toUserId, Header = "Requests", Text = "It arrived." };

    /// <summary>
    /// The endpoint, as one caller sees it.
    /// </summary>
    /// <param name="preferences">What is kept.</param>
    /// <param name="caller">Who the server says is calling.</param>
    /// <returns>The controller.</returns>
    private static MyNoticeSettingController ControllerFor(INoticePreferences preferences, Guid? caller)
        => new MyNoticeSettingController(new FakeCallerIdentity(caller), preferences);

    /// <summary>
    /// What the endpoint answers one caller about their own setting.
    /// </summary>
    /// <param name="preferences">What is kept.</param>
    /// <param name="caller">Who is asking.</param>
    /// <returns>Whether they are told.</returns>
    private static async Task<bool> ReadAsync(INoticePreferences preferences, Guid caller)
    {
        var answered = await ControllerFor(preferences, caller).MineAsync(CancellationToken.None).ConfigureAwait(false);

        return Answered(answered).TellsMe;
    }

    /// <summary>
    /// What the endpoint answers after one caller sets their own setting.
    /// </summary>
    /// <param name="preferences">What is kept.</param>
    /// <param name="caller">Who is setting it.</param>
    /// <param name="tellsMe">Which way.</param>
    /// <returns>Whether they are told afterwards.</returns>
    private static async Task<bool> SetAsync(INoticePreferences preferences, Guid caller, bool tellsMe)
    {
        var answered = await ControllerFor(preferences, caller)
            .SetMineAsync(new SetMyNoticeBody { TellsMe = tellsMe }, CancellationToken.None)
            .ConfigureAwait(false);

        return Answered(answered).TellsMe;
    }

    /// <summary>
    /// The setting out of an answer that carried one.
    /// </summary>
    /// <param name="answered">What the endpoint returned.</param>
    /// <returns>The setting.</returns>
    private static MyNoticeSetting Answered(ActionResult<MyNoticeSetting> answered)
    {
        var ok = Assert.IsType<OkObjectResult>(answered.Result);

        return Assert.IsType<MyNoticeSetting>(ok.Value);
    }

    /// <summary>
    /// The failure out of an answer that carried one.
    /// </summary>
    /// <param name="answered">What the endpoint returned.</param>
    /// <returns>The failure.</returns>
    private static RequestFailure Refused(ActionResult<MyNoticeSetting> answered)
    {
        var refusal = Assert.IsType<ObjectResult>(answered.Result);

        Assert.Equal(RequestFailure.StatusFor(Assert.IsType<RequestFailure>(refusal.Value).Code), refusal.StatusCode);

        return Assert.IsType<RequestFailure>(refusal.Value);
    }
}

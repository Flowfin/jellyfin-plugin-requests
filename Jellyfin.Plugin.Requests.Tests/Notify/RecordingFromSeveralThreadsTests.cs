using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Notify;

/// <summary>
/// The suite's own recording double, told by several threads at once.
/// <para>
/// This is the half of #330 that is not about an assertion. <c>QuietedRequesterNotice</c> decides
/// each message on a task of its own and calls the path underneath from whichever one finishes, so
/// <see cref="RecordingRequesterNotice"/> is written to concurrently by every test that drives that
/// class, and it is the double most of this suite uses for this interface. An unguarded list under
/// that traffic loses an entry, keeps one twice or is enumerated mid-append, and each of those
/// reads in a report as a defect in whatever the test was actually about.
/// </para>
/// <para>
/// <b>These legs are about the double and not about the plugin.</b> A test apparatus that can be
/// wrong in a way that looks like the subject being wrong is worth a guard of its own, and the
/// alternative reading - keeping the double unguarded and forbidding the tests to drive it
/// concurrently - was refused because the concurrency is the subject's, not the suite's.
/// </para>
/// <para>
/// <b>What they can and cannot prove, and the two legs differ.</b> A race is shown by driving it
/// rather than by asserting an absence, so the first leg's redness without the lock is a matter of
/// scheduling: it was watched failing on three runs of three, and that is evidence rather than a
/// certainty. The second leg is decidable from one thread and was watched failing with only the
/// copy removed, which is a certainty. Greenness of either with the guard in place is not a matter
/// of scheduling: no interleaving of the guarded code loses, doubles or half-publishes an entry,
/// and no answer it hands out changes underneath its reader.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class RecordingFromSeveralThreadsTests
{
    /// <summary>
    /// How many threads tell it at once. More than the two a notice path produces, because a race
    /// that needs a particular interleaving is met by trying many rather than by trying twice.
    /// </summary>
    private const int Tellers = 8;

    /// <summary>
    /// How many messages each of them hands over.
    /// </summary>
    private const int EachTells = 2000;

    private static readonly Guid Asker = new Guid("f3000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Every message given from several threads at once is kept, exactly once each.
    /// <para>
    /// The text of each message names the thread and the position, so the assertion separates the
    /// three ways an unguarded append is wrong: a short count is a lost entry, a full count with
    /// fewer distinct texts is a doubled one, and a hole would be a null nothing here would find a
    /// text on.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EveryMessageFromSeveralThreadsAtOnceIsKeptExactlyOnce()
    {
        var recording = new RecordingRequesterNotice();
        using var ready = new Barrier(Tellers);

        var telling = new Task[Tellers];

        for (var teller = 0; teller < Tellers; teller++)
        {
            var mine = teller;

            telling[mine] = Task.Factory.StartNew(
                () =>
                {
                    // Started rather than staggered: the appends have to overlap for the guard to
                    // be under any pressure at all.
                    ready.SignalAndWait();

                    for (var position = 0; position < EachTells; position++)
                    {
                        recording.Tell(Message(mine, position));
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        await Task.WhenAll(telling).ConfigureAwait(true);

        var kept = recording.Told;

        Assert.Equal(Tellers * EachTells, kept.Count);
        Assert.Equal(
            Tellers * EachTells,
            kept.Select(message => message.Text).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// What was told is answered as a copy rather than as the list itself.
    /// <para>
    /// The direction the count above cannot reach, and the one a reader meets rather than a writer.
    /// A caller handed the double's own list walks a collection another thread is still appending
    /// to, which raises out of the enumeration and reads as the test's subject having thrown. This
    /// leg is written without a second thread on purpose: whether the answer is a copy is decidable
    /// from one thread, and a leg that raced for it would only sometimes be the leg it claims to be.
    /// </para>
    /// </summary>
    [Fact]
    public void WhatWasToldIsAnsweredAsACopyRatherThanAsTheListItself()
    {
        var recording = new RecordingRequesterNotice();

        recording.Tell(Message(0, 0));

        var earlier = recording.Told;

        recording.Tell(Message(0, 1));

        Assert.Single(earlier);
        Assert.Equal(2, recording.Told.Count);
    }

    /// <summary>
    /// Every line written from several threads at once is kept, exactly once each.
    /// <para>
    /// The same claim as the leg above, over the double every one of those background paths writes
    /// to rather than over the one that only the requester path does. What the count separates is
    /// the same three ways an unguarded append is wrong, and the text of each line names the thread
    /// and the position so a short count and a full count with fewer distinct texts are different
    /// failures.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EveryLineFromSeveralThreadsAtOnceIsKeptExactlyOnce()
    {
        var log = new RecordingLogger();
        using var ready = new Barrier(Tellers);

        var writing = new Task[Tellers];

        for (var writer = 0; writer < Tellers; writer++)
        {
            var mine = writer;

            writing[mine] = Task.Factory.StartNew(
                () =>
                {
                    ready.SignalAndWait();

                    for (var position = 0; position < EachTells; position++)
                    {
                        Write(log, mine, position);
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        await Task.WhenAll(writing).ConfigureAwait(true);

        var kept = log.Lines;

        Assert.Equal(Tellers * EachTells, kept.Count);
        Assert.Equal(
            Tellers * EachTells,
            kept.Select(line => line.Message).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// What was logged is answered as a copy rather than as the list itself, by both readers.
    /// <para>
    /// <see cref="RecordingLogger.At"/> is the reader most of this suite uses and it filters, so it
    /// hands back a new list either way and the direction that matters there is that it does not
    /// walk the live one while a delivery is still writing to it. That is the same defect as the
    /// one this leg names for <see cref="RecordingLogger.Lines"/> and is not decidable from one
    /// thread, so what is asserted here is the half that is: an answer already handed out does not
    /// move when the next line arrives.
    /// </para>
    /// </summary>
    [Fact]
    public void WhatWasLoggedIsAnsweredAsACopyRatherThanAsTheListItself()
    {
        var log = new RecordingLogger();

        Write(log, 0, 0);

        var earlier = log.Lines;

        Write(log, 0, 1);

        Assert.Single(earlier);
        Assert.Equal(2, log.Lines.Count);
    }

    /// <summary>
    /// Every push made from several threads at once is kept, exactly once each.
    /// <para>
    /// <c>ServerRequesterNotice</c> and <c>ServerArrivalNotice</c> each start a task per message and
    /// call the session manager from whichever one runs, so two messages handed over without a wait
    /// between them reach this double concurrently. The lists it keeps them in are what every leg
    /// asserting who was reached reads.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EveryPushFromSeveralThreadsAtOnceIsKeptExactlyOnce()
    {
        var sessions = new ASessionManagerThatOnlyDelivers();
        using var ready = new Barrier(Tellers);

        var pushing = new Task[Tellers];

        for (var pusher = 0; pusher < Tellers; pusher++)
        {
            var mine = pusher;

            pushing[mine] = Task.Factory.StartNew(
                () =>
                {
                    ready.SignalAndWait();

                    for (var position = 0; position < EachTells; position++)
                    {
                        Push(sessions, mine, position);
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        await Task.WhenAll(pushing).ConfigureAwait(true);

        var kept = sessions.Delivered;

        Assert.Equal(Tellers * EachTells, kept.Count);
        Assert.Equal(
            Tellers * EachTells,
            kept.Select(delivery => delivery.Payload as string).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// What was delivered is answered as a copy rather than as the list itself.
    /// <para>
    /// The reader's direction, decidable from one thread, exactly as for the notice double. A leg
    /// that read this list while a push was still in flight would walk a collection another thread
    /// is appending to and fail as though the plugin had thrown.
    /// </para>
    /// </summary>
    [Fact]
    public void WhatWasDeliveredIsAnsweredAsACopyRatherThanAsTheListItself()
    {
        var sessions = new ASessionManagerThatOnlyDelivers();

        Push(sessions, 0, 0);

        var earlier = sessions.Delivered;

        Push(sessions, 0, 1);

        Assert.Single(earlier);
        Assert.Equal(2, sessions.Delivered.Count);
    }

    /// <summary>
    /// One line, named so that a lost one and a doubled one are different failures.
    /// </summary>
    /// <param name="log">The logger to write to.</param>
    /// <param name="writer">Which thread is writing it.</param>
    /// <param name="position">Where in that thread's run it sits.</param>
    private static void Write(RecordingLogger log, int writer, int position)
    {
        // Named into a local rather than composed in the call: CA1873 refuses an argument a
        // disabled logger would have paid for, and this double enables every level anyway.
        var text = string.Create(CultureInfo.InvariantCulture, $"{writer}/{position}");

        log.Log(LogLevel.Information, default, text, null, static (state, _) => state);
    }

    /// <summary>
    /// One push, named the same way and addressed to the one person these doubles allow.
    /// </summary>
    /// <param name="sessions">The session manager to push through.</param>
    /// <param name="pusher">Which thread is pushing it.</param>
    /// <param name="position">Where in that thread's run it sits.</param>
    private static void Push(ASessionManagerThatOnlyDelivers sessions, int pusher, int position)
        => sessions
            .SendMessageToUserSessions(
                [Asker],
                SessionMessageType.GeneralCommand,
                string.Create(CultureInfo.InvariantCulture, $"{pusher}/{position}"),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    /// <summary>
    /// One message, named so that a lost one and a doubled one are different failures.
    /// </summary>
    /// <param name="teller">Which thread is handing it over.</param>
    /// <param name="position">Where in that thread's run it sits.</param>
    /// <returns>The message.</returns>
    private static RequesterMessage Message(int teller, int position)
        => new RequesterMessage
        {
            ToUserId = Asker,
            Header = "Requests",
            Text = string.Create(CultureInfo.InvariantCulture, $"{teller}/{position}"),
        };
}

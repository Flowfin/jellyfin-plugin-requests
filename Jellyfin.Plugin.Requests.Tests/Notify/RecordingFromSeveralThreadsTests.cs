using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.Tests.Doubles;
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

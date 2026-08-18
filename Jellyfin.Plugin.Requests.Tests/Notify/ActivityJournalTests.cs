using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Fulfilment;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Notify;

/// <summary>
/// What a move leaves in the server's activity log, which is #75.
/// <para>
/// The server's activity log is a database on a running server and the headless rule in
/// <c>docs/testing.md</c> refuses one, so what is asserted here is what this plugin asked to be
/// written. That is where every rule on this issue lives: one entry per transition, what the line
/// says, and what it may never carry. Whether the entry is then readable in the dashboard is the
/// second condition of #75 and is a procedure against a running server rather than a test.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class ActivityJournalTests
{
    private static readonly Guid Asker = new Guid("75000000-0000-0000-0000-000000000001");
    private static readonly Guid Operator = new Guid("75000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Started = new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// A request walked through its life leaves one entry per transition and none for anything else.
    /// <para>
    /// This is the first condition of #75 and it is written as the walk rather than as three
    /// separate legs, because what it has to catch is a path that writes no entry, and a leg per
    /// path is a set somebody adds a fourth path to without adding a fourth leg. The two surfaces
    /// that move a request share the one journal here for the same reason.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARequestWalkedThroughItsLifeLeavesOneEntryPerTransition()
    {
        var store = new InMemoryRequestStore();
        var journal = new RecordingJournal();
        var library = new FakeLibrary();
        var clock = new TestClock(Started);
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);

        // Asking for something is not a move. Nothing has been decided, the model appends no history
        // entry for it, and an entry here would be this plugin announcing its own arrival in a list
        // an operator reads for what the server did. Telling an administrator that something arrived
        // is #76 and is a live message rather than a record.
        Assert.Empty(journal.Written);

        await Controller(store, journal)
            .ApproveAsync(open.Request.Id, new ApproveRequestBody { Revision = open.Revision }, CancellationToken.None)
            .ConfigureAwait(true);

        library.Put(RequestedItemKind.Movie, "Tmdb", "603");
        clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(
            1,
            await new FulfilmentSweep(store, library, clock, journal, new RecordingSink(), new RecordingLogger())
                .SweepAsync(CancellationToken.None)
                .ConfigureAwait(true));

        Assert.Equal(2, journal.Written.Count);

        var approved = journal.Written[0];
        var fulfilled = journal.Written[1];

        Assert.Equal("Request approved: The Conversation", approved.Name, StringComparer.Ordinal);
        Assert.Equal("MediaRequestApproved", approved.Type, StringComparer.Ordinal);
        Assert.Equal(Operator, approved.UserId);
        Assert.Contains("Open to Approved", approved.ShortOverview, StringComparison.Ordinal);

        Assert.Equal("Request fulfilled: The Conversation", fulfilled.Name, StringComparer.Ordinal);
        Assert.Equal("MediaRequestFulfilled", fulfilled.Type, StringComparer.Ordinal);

        // The plugin looked at the library and nobody decided anything, so the entry is attributed
        // to nobody and says so in words as well. The server's entity has no nullable user, so an
        // entry that only left the identifier empty would read in the dashboard as an entry whose
        // user could not be resolved.
        Assert.Equal(Guid.Empty, fulfilled.UserId);
        Assert.Contains("by the plugin rather than by a person", fulfilled.ShortOverview, StringComparison.Ordinal);

        // Enough to find the request again, which is the third thing the issue asks an entry to
        // carry. It is in the text rather than in the entity's item identifier, because that field
        // is a library item on the server's side and the dashboard offers it as a link.
        Assert.Contains(
            open.Request.Id.ToString(),
            fulfilled.ShortOverview,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A decline writes an entry and the entry carries nothing of what the operator typed.
    /// <para>
    /// This is the third condition of #75. The note is the operator's message to one person, it can
    /// be five hundred characters, and the activity list is read by every administrator on the
    /// server, so a note reaching it is a disclosure nobody asked for and a wall of text in a list
    /// of one-line entries.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ADeclineWritesAnEntryCarryingNothingTheOperatorTyped()
    {
        var store = new InMemoryRequestStore();
        var journal = new RecordingJournal();
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);

        await Controller(store, journal)
            .DeclineAsync(
                open.Request.Id,
                new DeclineRequestBody
                {
                    Revision = open.Revision,
                    Reason = DeclineReason.Other,
                    Note = "Ask me again after the disk arrives, and stop asking for this one at midnight."
                },
                CancellationToken.None)
            .ConfigureAwait(true);

        var entry = Assert.Single(journal.Written);

        Assert.Equal("Request declined: The Conversation", entry.Name, StringComparer.Ordinal);
        Assert.Equal("MediaRequestDeclined", entry.Type, StringComparer.Ordinal);
        Assert.DoesNotContain("disk arrives", entry.Name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("disk arrives", entry.ShortOverview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("midnight", entry.ShortOverview, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A write that moved nothing writes no entry.
    /// <para>
    /// The fulfilment sweep writes every time what it saw in the library changed, whether or not the
    /// request moved. Without this the activity list gains a line every time a title's availability
    /// is re-observed, which is the wall of entries the issue says is worse than none.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnObservationThatMovedNothingWritesNoEntry()
    {
        var store = new InMemoryRequestStore();
        var journal = new RecordingJournal();
        var library = new FakeLibrary();
        var clock = new TestClock(Started);
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);

        await Controller(store, journal)
            .DeclineAsync(
                open.Request.Id,
                new DeclineRequestBody { Revision = open.Revision, Reason = DeclineReason.AlreadyInTheLibrary },
                CancellationToken.None)
            .ConfigureAwait(true);

        // A declined request whose title then arrives. The sweep writes the new availability and the
        // table refuses the move, so the store is written and the state is where it was.
        library.Put(RequestedItemKind.Movie, "Tmdb", "603");
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(
            0,
            await new FulfilmentSweep(store, library, clock, journal, new RecordingSink(), new RecordingLogger())
                .SweepAsync(CancellationToken.None)
                .ConfigureAwait(true));

        var held = Assert.IsType<StoredRequest>(
            await store.GetAsync(open.Request.Id, CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(LibraryAvailability.Present, held.Request.Availability);
        Assert.Equal(RequestState.Declined, held.Request.State);
        Assert.Single(journal.Written);
    }

    /// <summary>
    /// A title longer than an entry carries is cut, and the cut is visible.
    /// <para>
    /// The title is a snapshot of what whoever asked typed and nothing caps it on the way here, so
    /// without this one row of the operator's activity list is as long as somebody wanted it to be.
    /// </para>
    /// <para>
    /// The two lengths are the cap and one character past it, which is the mistake somebody makes.
    /// A leg written only against a title far longer than the cap passes with the comparison off by
    /// one in either direction, because the cut lands in the same place either way; the pair is what
    /// pins the threshold rather than the cutting.
    /// </para>
    /// </summary>
    /// <param name="length">How long the title is.</param>
    /// <param name="cut">Whether an entry built from it is expected to be cut.</param>
    [Theory]
    [InlineData(ActivityNote.TitleMaximumLength, false)]
    [InlineData(ActivityNote.TitleMaximumLength + 1, true)]
    public void ATitleLongerThanAnEntryCarriesIsCutAndSaysSo(int length, bool cut)
    {
        var before = AnAsk() with { DisplayTitle = new string('t', length) };
        var moved = RequestLifecycle.Move(before, RequestState.Approved, Started, RequestCaller.Administrator(Operator));

        var note = ActivityNote.For(before, moved);

        Assert.NotNull(note);
        Assert.Equal(
            cut
                ? "Request approved: " + new string('t', ActivityNote.TitleMaximumLength) + "..."
                : "Request approved: " + new string('t', length),
            note.Name,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The activity log refusing a write does not undo a decision that has already been made.
    /// <para>
    /// The move is in the store by the time an entry is attempted, so an exception from the host
    /// would answer the operator that their approval failed when it had not. This is the reason the
    /// swallow is in <see cref="ServerActivityJournal"/> rather than left to each caller, and it is
    /// read here against a journal that refuses everything.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnActivityLogThatRefusesTheWriteDoesNotUndoTheDecision()
    {
        var store = new InMemoryRequestStore();
        var logger = new RecordingLogger();
        var journal = new ServerActivityJournal(new AnActivityLogThatRefuses(), logger);
        var open = await AnOpenRequestAsync(store).ConfigureAwait(true);

        var answered = await Controller(store, journal)
            .ApproveAsync(open.Request.Id, new ApproveRequestBody { Revision = open.Revision }, CancellationToken.None)
            .ConfigureAwait(true);

        var held = Assert.IsType<StoredRequest>(
            await store.GetAsync(open.Request.Id, CancellationToken.None).ConfigureAwait(true));

        Assert.IsType<QueuedRequest>(Assert.IsAssignableFrom<ObjectResult>(answered.Result).Value);
        Assert.Equal(RequestState.Approved, held.Request.State);

        var reported = Assert.Single(logger.At(LogLevel.Warning));

        Assert.Contains("activity log", reported.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(reported.Exception);
    }

    /// <summary>
    /// A controller writing its entries into the journal handed in.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="journal">Where the entries go.</param>
    /// <returns>The controller.</returns>
    private RequestsController Controller(InMemoryRequestStore store, IActivityJournal journal)
        => new RequestsController(
            store,
            new TestClock(Started),
            _identifiers,
            new FakeCallerIdentity(Operator),
            new FakeInstallSettings(),
            journal,
            new RecordingSink());

    /// <summary>
    /// One open request in the store, with a provider identifier so the library can match it.
    /// </summary>
    /// <param name="store">Where it goes.</param>
    /// <returns>The request and its revision.</returns>
    private async Task<StoredRequest> AnOpenRequestAsync(InMemoryRequestStore store)
        => await store.AddAsync(AnAsk(), CancellationToken.None).ConfigureAwait(true);

    /// <summary>
    /// The request every leg here starts from.
    /// </summary>
    /// <returns>The request.</returns>
    private MediaRequest AnAsk()
        => new MediaRequest
        {
            Id = _identifiers.NewId(),
            RequestedByUserId = Asker,
            RequestedAt = Started,
            StateChangedAt = Started,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = "The Conversation",
            DisplayYear = 1974,
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "603" }
        };
}

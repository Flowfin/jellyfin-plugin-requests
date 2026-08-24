using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Requests.Model;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Model;

/// <summary>
/// The history a request carries. The state says what is true now; this says what happened, which is
/// the question an operator dealing with a complaint actually has.
/// </summary>
public class RequestHistoryTests
{
    private static readonly Guid FirstOperator = new("a1f4c827-9d3b-4e06-8175-2c6e5b9a4d31");

    private static readonly Guid SecondOperator = new("6b09e13d-5f78-4a2c-90b4-8e1d7c3a5029");

    private static readonly RequestCaller ByFirstOperator = RequestCaller.Administrator(FirstOperator);

    private static readonly RequestCaller BySecondOperator = RequestCaller.Administrator(SecondOperator);

    /// <summary>
    /// A request the model built and nothing has decided has an empty history. Being created is not
    /// a move, so nothing here appends one.
    /// <para>
    /// The head of a real request's history is an arrival rather than a move, and it is written by
    /// the surface the ask came in on rather than by this type, because only that surface knows
    /// which of them it was. #118 is where that was decided, and the legs for it are on the two
    /// surfaces. What this leg holds is that the model itself invents no row.
    /// </para>
    /// </summary>
    [Fact]
    public void ARequestNothingHasDecidedHasNoHistory()
    {
        Assert.Empty(ARequest().History);
    }

    /// <summary>
    /// An arrival entry is derived from the request it goes on, so a surface cannot write one that
    /// disagrees with the record beneath it. Every field but the arrival itself comes off the
    /// request, and the arrival is the one thing the record does not already hold.
    /// </summary>
    [Fact]
    public void AnArrivalEntryIsDerivedFromTheRequestItIsOn()
    {
        var request = ARequest();

        var entry = RequestHistoryEntry.Arriving(RequestArrival.Seam, request);

        Assert.Equal(RequestArrival.Seam, entry.Arrival);
        Assert.Equal(request.RequestedAt, entry.At);
        Assert.Equal(request.RequestedByUserId, entry.ByUserId);
        Assert.Equal(request.State, entry.From);
        Assert.Equal(request.State, entry.To);
        Assert.Null(entry.Reason);
        Assert.Null(entry.Note);
    }

    /// <summary>
    /// An arrival and a move are told apart by the arrival and never by the pair of states. An
    /// arrival moves nothing, so both its states are the one the request came into existence in, and
    /// a reader that separated the two rows by comparing <c>From</c> with <c>To</c> would be
    /// separating them by a coincidence rather than by what they are.
    /// </summary>
    [Fact]
    public void AnArrivalIsToldFromAMoveByTheArrivalRatherThanByItsStates()
    {
        var request = ARequest();

        var arrival = RequestHistoryEntry.Arriving(RequestArrival.Endpoint, request);
        var move = Assert.Single(
            RequestLifecycle.Move(request, RequestState.Approved, At(9), ByFirstOperator).History);

        Assert.NotNull(arrival.Arrival);
        Assert.Equal(arrival.From, arrival.To);

        Assert.Null(move.Arrival);
        Assert.NotEqual(move.From, move.To);
    }

    /// <summary>
    /// A move appends beneath an arrival rather than replacing it. The history is append-only, so
    /// the row saying where the request came from has to survive every decision made on it
    /// afterwards, which is what makes it worth writing at all.
    /// </summary>
    [Fact]
    public void AMoveAppendsBeneathAnArrivalAndLeavesItThere()
    {
        var request = ARequest();
        var arrived = request with { History = [RequestHistoryEntry.Arriving(RequestArrival.Seam, request)] };

        var approved = RequestLifecycle.Move(arrived, RequestState.Approved, At(9), ByFirstOperator);

        Assert.Equal(2, approved.History.Count);
        Assert.Equal(RequestArrival.Seam, approved.History[0].Arrival);
        Assert.Null(approved.History[1].Arrival);
        Assert.Equal(RequestState.Approved, approved.History[1].To);
    }

    /// <summary>
    /// There is no arrival to derive without a request to derive it from. It refuses rather than
    /// building an entry out of defaults, because an entry naming nobody at the epoch is a row that
    /// reads as a fact.
    /// </summary>
    [Fact]
    public void AnArrivalCannotBeDerivedFromNothing()
    {
        Assert.Throws<ArgumentNullException>(
            () => RequestHistoryEntry.Arriving(RequestArrival.Endpoint, null!));
    }

    /// <summary>
    /// A request walked through its whole life, with one entry per move and no others. This is the
    /// first condition on #43, and it is written as one walk rather than as a move per test because
    /// the failure it stands for is an entry appended twice or not at all somewhere in the middle of
    /// a sequence, which a test of a single move cannot see.
    /// </summary>
    [Fact]
    public void EveryMoveAppendsExactlyOneEntryAndNothingElseDoes()
    {
        var request = ARequest();

        var approved = RequestLifecycle.Move(request, RequestState.Approved, At(9), ByFirstOperator);
        var failed = RequestLifecycle.Move(approved, RequestState.Failed, At(10), RequestCaller.Plugin);
        var declined = RequestLifecycle.Decline(failed, DeclineReason.CannotBeObtained, "Nothing has it.", At(11), BySecondOperator);
        var approvedAgain = RequestLifecycle.Move(declined, RequestState.Approved, At(12), ByFirstOperator);
        var fulfilled = RequestLifecycle.Move(approvedAgain, RequestState.Fulfilled, At(13), RequestCaller.Plugin);

        Assert.Equal(
            [
                "Open to Approved",
                "Approved to Failed",
                "Failed to Declined",
                "Declined to Approved",
                "Approved to Fulfilled"
            ],
            fulfilled.History.Select(entry => $"{entry.From} to {entry.To}").ToArray());

        Assert.Equal([1, 2, 3, 4, 5], new[] { approved, failed, declined, approvedAgain, fulfilled }.Select(step => step.History.Count).ToArray());
    }

    /// <summary>
    /// An entry carries when the move happened and who made it, and says so where nobody did. A
    /// history that cannot tell an operator's decision from the plugin's own would make somebody
    /// answer for a decision they never took.
    /// </summary>
    [Fact]
    public void AnEntryCarriesTheTimeAndTheMoverIncludingWhereThereWasNone()
    {
        var approved = RequestLifecycle.Move(ARequest(), RequestState.Approved, At(9), ByFirstOperator);
        var fulfilled = RequestLifecycle.Move(approved, RequestState.Fulfilled, At(13), RequestCaller.Plugin);

        Assert.Equal(At(9), fulfilled.History[0].At);
        Assert.Equal(FirstOperator, fulfilled.History[0].ByUserId);

        Assert.Equal(At(13), fulfilled.History[1].At);
        Assert.Null(fulfilled.History[1].ByUserId);
    }

    /// <summary>
    /// The reason a decline was given for survives the decline being taken back. This is the whole
    /// reason an entry holds more than a pair of states: the reason on the request is the current
    /// one and is cleared the moment somebody changes their mind, and an operator answering a
    /// complaint about a refusal that was later reversed has nowhere else to read it.
    /// </summary>
    [Fact]
    public void AReasonSurvivesTheDeclineBeingTakenBack()
    {
        var declined = RequestLifecycle.Decline(
            ARequest(),
            DeclineReason.NoRoomForIt,
            "The disk is full until the archive move finishes.",
            At(9),
            ByFirstOperator);

        var approved = RequestLifecycle.Move(declined, RequestState.Approved, At(10), BySecondOperator);

        Assert.Null(approved.DeclineReason);
        Assert.Equal(DeclineReason.NoRoomForIt, approved.History[0].Reason);
        Assert.Equal(
            "The disk is full until the archive move finishes.",
            approved.History[0].Note,
            StringComparer.Ordinal);
        Assert.Null(approved.History[1].Reason);
    }

    /// <summary>
    /// A refused move appends nothing. A history that grew on an attempt would read as a request
    /// that moved and came back, and the thing that actually happened is that nothing happened.
    /// </summary>
    [Fact]
    public void ARefusedMoveAppendsNothing()
    {
        var fulfilled = RequestLifecycle.Move(
            RequestLifecycle.Move(ARequest(), RequestState.Approved, At(9), ByFirstOperator),
            RequestState.Fulfilled,
            At(10),
            RequestCaller.Plugin);

        Assert.Throws<IllegalRequestTransitionException>(
            () => RequestLifecycle.Move(fulfilled, RequestState.Approved, At(11), ByFirstOperator));

        Assert.Equal(2, fulfilled.History.Count);
    }

    /// <summary>
    /// A move that fails at the cap appends nothing either. The entry is built before the record is,
    /// so this is the case where a history could grow for a move that never happened.
    /// </summary>
    [Fact]
    public void ADeclineRefusedForItsNoteLengthAppendsNothing()
    {
        var request = ARequest();

        Assert.Throws<RequestTextTooLongException>(
            () => RequestLifecycle.Decline(
                request,
                DeclineReason.Other,
                new string('x', MediaRequest.NoteMaximumLength + 1),
                At(9),
                ByFirstOperator));

        Assert.Empty(request.History);
    }

    /// <summary>
    /// Moving a request does not touch the history of the value it was made from. The record is
    /// immutable and a move produces a new value, so a caller still holding the earlier one is
    /// holding what they read, and a shared list grown in place would take that away.
    /// </summary>
    [Fact]
    public void MovingARequestLeavesTheEarlierValuesHistoryAlone()
    {
        var request = ARequest();
        var approved = RequestLifecycle.Move(request, RequestState.Approved, At(9), ByFirstOperator);

        _ = RequestLifecycle.Move(approved, RequestState.Fulfilled, At(10), RequestCaller.Plugin);

        Assert.Empty(request.History);
        Assert.Single(approved.History);
    }

    /// <summary>
    /// Entries are oldest first, and stay that way. The order is what makes a history readable as a
    /// sequence of events rather than as a set of them, and a page that shows it will show it in the
    /// order the list is in.
    /// </summary>
    [Fact]
    public void EntriesAreOldestFirst()
    {
        var fulfilled = RequestLifecycle.Move(
            RequestLifecycle.Move(ARequest(), RequestState.Approved, At(9), ByFirstOperator),
            RequestState.Fulfilled,
            At(13),
            RequestCaller.Plugin);

        Assert.Equal(
            fulfilled.History.Select(entry => entry.At).OrderBy(at => at).ToArray(),
            fulfilled.History.Select(entry => entry.At).ToArray());
    }

    /// <summary>
    /// The note on an entry is capped by the same rule as the note on the request. A copy of a
    /// capped value that is not capped is the cap not being enforced, one indirection away.
    /// </summary>
    [Fact]
    public void TheNoteOnAnEntryIsCappedToo()
    {
        var refusal = Assert.Throws<RequestTextTooLongException>(
            () => new RequestHistoryEntry
            {
                From = RequestState.Open,
                To = RequestState.Declined,
                At = At(9),
                Note = new string('x', MediaRequest.NoteMaximumLength + 1)
            });

        Assert.Equal("Note", refusal.Field, StringComparer.Ordinal);
    }

    private static DateTimeOffset At(int hour) => new(2026, 8, 9, hour, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A request with only the fields that have no default, in the state a new one is created in.
    /// </summary>
    /// <returns>A newly asked-for request.</returns>
    private static MediaRequest ARequest()
    {
        var asked = At(8);

        return new MediaRequest
        {
            Id = new Guid("e70b4c19-8a5d-4236-91f7-0c3b6e2a8d54"),
            RequestedByUserId = new Guid("3f8c1d05-2e69-4b74-a018-9d5e7c4b6a12"),
            RequestedAt = asked,
            StateChangedAt = asked,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = "Sorcerer",
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "11631" }
        };
    }
}

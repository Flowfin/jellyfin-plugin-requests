using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// The moves a request may make, as a table, and the two methods that make one.
/// <para>
/// It is a table rather than a chain of conditionals because the same question gets asked by the
/// API, by the administrator page, by the bridge and by whatever detects fulfilment, and four
/// copies of a rule are four chances for one of them to be a version behind. A table can be read
/// by a person, printed into the documentation and tested cell by cell, including the cells that
/// must refuse.
/// </para>
/// <para>
/// Every ordered pair of states has a cell, including a state paired with itself, so adding a value
/// to <see cref="RequestState"/> is a set of entries here rather than a silent widening. The three
/// cells people disagree about are answered as follows, and each answer is in the table's own
/// <see cref="RequestTransition.Why"/> beside the cell rather than only here.
/// </para>
/// <para>
/// Nothing returns to <see cref="RequestState.Open"/>. Undeciding is not a decision, and a request
/// that reads as undecided after somebody decided it hides the decision from the next person to
/// look at the queue. An approval given by mistake is repaired by declining it, and both moves stay
/// in the history.
/// </para>
/// <para>
/// A decline can be taken back. An operator changing their mind is ordinary, and the alternative is
/// asking the person who was refused to ask again, which loses the connection between the two and
/// depends on that person still being there.
/// </para>
/// <para>
/// <see cref="RequestState.Fulfilled"/> is the end. A file that turns out to be the wrong one is a
/// new request for the right one, and a library that stops holding the media is an observation
/// rather than a decision being undone, which is what <see cref="LibraryAvailability"/> and
/// <see cref="MediaRequest.AvailabilityCheckedAt"/> are for.
/// </para>
/// <para>
/// <b>Who may make a move is the second half of every cell.</b> A move is legal or illegal by the
/// table and separately permitted or not permitted to the caller, and both are checked here rather
/// than at whatever is calling, so that a surface added later cannot forget the second one. The
/// caller says what it is, in <see cref="RequestCaller"/>; the cell says what it admits, in
/// <see cref="RequestTransition.Permitted"/>; the move is permitted where the two sets share a
/// value. A refused cell admits nobody, so the two checks never disagree about a move the table
/// already refuses.
/// </para>
/// <para>
/// One line decides every cell's permitted set: <b>a decision is an administrator's and an
/// observation is the plugin's</b>. Approving, declining, taking either back and sending something
/// onward again are answers a person gives, and they are the six cells an administrator may make.
/// Arriving in the library and failing to arrive are things that happened whether or not anybody
/// looked, and they are the four cells the plugin may make. A person marking a request fulfilled
/// would be making the state say something about the library that the library does not say, and
/// #42 is where that is detected instead.
/// </para>
/// <para>
/// No cell admits the requester alone. Asking is not a move: approving one's own request is the
/// case this check exists for, a decline is the operator's answer rather than the asker's, and a
/// user withdrawing has no state to move to because <c>Cancelled</c> was refused on #113. An
/// administrator who asked for something themselves holds both roles on it and may still decide it,
/// and whether that should be so is a configuration question left to M12 that is answered at the
/// caller rather than in this table.
/// </para>
/// </summary>
public static class RequestLifecycle
{
    /// <summary>
    /// Gets every ordered pair of states, whether the move is allowed, and why. This is the source
    /// the grid and the reasons in <c>docs/lifecycle.md</c> are written from, and
    /// <c>TheDocumentedGridIsTheTableInTheCode</c> and
    /// <c>TheDocumentedReasonsAreTheReasonsInTheCode</c> refuse the two disagreeing.
    /// </summary>
    public static IReadOnlyList<RequestTransition> Table { get; } = BuildTable();

    /// <summary>
    /// Whether a request may move between two states.
    /// </summary>
    /// <param name="from">The state being moved out of.</param>
    /// <param name="to">The state being moved into.</param>
    /// <returns><see langword="true"/> where the table allows the move.</returns>
    public static bool IsLegal(RequestState from, RequestState to) => Cell(from, to).IsLegal;

    /// <summary>
    /// Looks up one cell of the table.
    /// </summary>
    /// <param name="from">The state being moved out of.</param>
    /// <param name="to">The state being moved into.</param>
    /// <returns>The cell for that pair.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Where either value is not one the table has a cell for, which can only happen if a value was
    /// added to <see cref="RequestState"/> without adding its rows here.
    /// </exception>
    public static RequestTransition Cell(RequestState from, RequestState to)
    {
        var cell = Table.FirstOrDefault(entry => entry.From == from && entry.To == to);

        return cell ?? throw new ArgumentOutOfRangeException(
            nameof(to),
            $"The transition table has no cell for {from} to {to}. A state was added to RequestState without its rows.");
    }

    /// <summary>
    /// Moves a request into any state except <see cref="RequestState.Declined"/>, or refuses to.
    /// <para>
    /// This and <see cref="Decline"/> are the only places in the plugin that change a request's
    /// state. The record is immutable and a caller could still write <c>with { State = ... }</c>, so
    /// that being true is held by review and by the callers that exist rather than by anything that
    /// refuses the alternative; that gap is named in <c>docs/lifecycle.md</c>.
    /// </para>
    /// </summary>
    /// <param name="request">The request to move.</param>
    /// <param name="to">The state to move it into.</param>
    /// <param name="at">
    /// When the move happened, from the injected clock rather than the machine's.
    /// </param>
    /// <param name="by">
    /// Who is making the move, and with what authority. <see cref="RequestCaller.Plugin"/> is the
    /// one that records no person, for a move made on something the plugin observed.
    /// </param>
    /// <returns>A new request in the new state.</returns>
    /// <exception cref="ArgumentNullException">Where no request or no caller was given.</exception>
    /// <exception cref="IllegalRequestTransitionException">Where the table refuses the move.</exception>
    /// <exception cref="RequestMoveNotPermittedException">
    /// Where the table allows the move and does not admit this caller for it.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Where the state asked for is <see cref="RequestState.Declined"/>, which needs a reason and so
    /// has a door of its own.
    /// </exception>
    public static MediaRequest Move(
        MediaRequest request,
        RequestState to,
        DateTimeOffset at,
        RequestCaller by)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (to == RequestState.Declined)
        {
            throw new ArgumentException(
                "A decline carries a reason, so it is made with Decline rather than with Move. A decline with no reason reads as arbitrary to the person who asked, decided on #113.",
                nameof(to));
        }

        // A reason describes the decline it was given for. Carried onto a request that is no longer
        // declined it is a sentence that is no longer true, and the surfaces would show it beside a
        // state it contradicts. The entry that carried it stays in the history, so taking a decline
        // back loses the current reason and not the record of it.
        return Moved(request, to, at, by, reason: null, note: null);
    }

    /// <summary>
    /// Declines a request, with the reason a decline is required to carry.
    /// <para>
    /// It is a method of its own rather than a parameter on <see cref="Move"/> because the reason is
    /// mandatory for exactly one destination. A nullable parameter that is required for one value of
    /// another parameter is a rule a caller reads in prose; two doors is a rule the compiler carries.
    /// </para>
    /// </summary>
    /// <param name="request">The request to decline.</param>
    /// <param name="reason">Why it is being declined.</param>
    /// <param name="note">
    /// What the operator wants to say about it. Required beside <see cref="DeclineReason.Other"/>,
    /// which says nothing on its own, and optional beside every other reason.
    /// </param>
    /// <param name="at">
    /// When the decline happened, from the injected clock rather than the machine's.
    /// </param>
    /// <param name="by">Who is declining it, and with what authority.</param>
    /// <returns>A new request, declined, carrying the reason.</returns>
    /// <exception cref="ArgumentNullException">Where no request or no caller was given.</exception>
    /// <exception cref="IllegalRequestTransitionException">Where the table refuses the move.</exception>
    /// <exception cref="RequestMoveNotPermittedException">
    /// Where the table allows the decline and does not admit this caller for it.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Where the reason is <see cref="DeclineReason.Other"/> and no note says what happened.
    /// </exception>
    /// <exception cref="RequestTextTooLongException">Where the note is too long.</exception>
    public static MediaRequest Decline(
        MediaRequest request,
        DeclineReason reason,
        string? note,
        DateTimeOffset at,
        RequestCaller by)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (reason == DeclineReason.Other && string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException(
                "A decline for a reason not on the list has to say what the reason was. Other with nothing beside it is a decline with no reason, which is the thing a required reason exists to prevent.",
                nameof(note));
        }

        return Moved(request, RequestState.Declined, at, by, reason, note);
    }

    /// <summary>
    /// The one place a request changes state, and therefore the one place the history grows and the
    /// one place a caller's authority is checked. Both public methods above go through here, so
    /// "every transition appends exactly one entry" and "every transition asks who is making it" are
    /// properties of the code's shape rather than things two call sites have to remember.
    /// </summary>
    /// <param name="request">The request to move.</param>
    /// <param name="to">The state to move it into.</param>
    /// <param name="at">When the move happened.</param>
    /// <param name="by">Who is making the move, and with what authority.</param>
    /// <param name="reason">The decline reason, where this is a decline.</param>
    /// <param name="note">The text written beside the reason.</param>
    /// <returns>A new request in the new state, one entry longer.</returns>
    private static MediaRequest Moved(
        MediaRequest request,
        RequestState to,
        DateTimeOffset at,
        RequestCaller by,
        DeclineReason? reason,
        string? note)
    {
        ArgumentNullException.ThrowIfNull(by);

        var cell = Cell(request.State, to);

        // Legality first, and the order is deliberate. A move the table refuses is refused to
        // everybody, so answering it with "not you" would be wrong for every caller including the
        // one who tried; and neither refusal names anything about the request, so nothing is
        // disclosed by either order.
        if (!cell.IsLegal)
        {
            throw new IllegalRequestTransitionException(cell.From, cell.To, cell.Why);
        }

        if ((cell.Permitted & by.RolesOn(request)) == RequestActor.None)
        {
            throw new RequestMoveNotPermittedException(cell.From, cell.To, cell.Permitted);
        }

        var entry = new RequestHistoryEntry
        {
            From = cell.From,
            To = cell.To,
            At = at,
            ByUserId = by.UserId,
            Reason = reason,
            Note = note
        };

        return request with
        {
            State = to,
            StateChangedAt = at,
            StateChangedByUserId = by.UserId,
            DeclineReason = reason,
            DeclineNote = note,

            // A new list rather than an addition to the old one. The request handed in is a value
            // somebody else may still be holding, and growing its list underneath them would move a
            // history they had already read.
            History = [.. request.History, entry]
        };
    }

    private static ReadOnlyCollection<RequestTransition> BuildTable()
    {
        const string NotAMove =
            "A move to the state it is already in is not a move, and appending a history entry saying nothing happened makes the history harder to read rather than more complete.";
        const string NoUndeciding =
            "Nothing returns to open. A request reading as undecided after somebody decided it hides that decision from the next person to look at the queue.";
        const string FulfilledIsTheEnd =
            "Fulfilled is the end of this request. A file that turns out to be the wrong one is a new request for the right one, and a library that stops holding it is an availability observation rather than a decision being undone.";
        const string NothingWasSent =
            "Nothing was ever sent onward, so there is nothing that could have failed.";

        // The two permitted sets the whole table is built out of, named rather than repeated, so
        // that a cell reads as one of the two kinds of move rather than as its own arrangement. A
        // cell needing a third set is a cell that has stopped being either a decision or an
        // observation, and giving it a name here is the moment to say what it is instead.
        const RequestActor Decision = RequestActor.Administrator;
        const RequestActor Observation = RequestActor.Plugin;

        var table = new List<RequestTransition>
        {
            Refused(RequestState.Open, RequestState.Open, NotAMove),
            Legal(RequestState.Open, RequestState.Approved, Decision, "An operator says yes."),
            Legal(RequestState.Open, RequestState.Declined, Decision, "An operator says no."),
            Legal(
                RequestState.Open,
                RequestState.Fulfilled,
                Observation,
                "The library already holds what was asked for, so there is nothing left for anybody to decide."),
            Refused(RequestState.Open, RequestState.Failed, NothingWasSent),

            Refused(RequestState.Approved, RequestState.Open, NoUndeciding),
            Refused(RequestState.Approved, RequestState.Approved, NotAMove),
            Legal(
                RequestState.Approved,
                RequestState.Declined,
                Decision,
                "An operator takes an approval back, and the reason says why. This is the repair for an approval given by mistake."),
            Legal(
                RequestState.Approved,
                RequestState.Fulfilled,
                Observation,
                "It arrived and the person who asked can watch it."),
            Legal(
                RequestState.Approved,
                RequestState.Failed,
                Observation,
                "It was sent onward and did not arrive, so it stops looking like an operator forgot about it."),

            Refused(RequestState.Declined, RequestState.Open, NoUndeciding),
            Legal(
                RequestState.Declined,
                RequestState.Approved,
                Decision,
                "An operator changes their mind. One request carrying both moves beats asking the person who was refused to ask again."),
            Refused(RequestState.Declined, RequestState.Declined, NotAMove),
            Refused(
                RequestState.Declined,
                RequestState.Fulfilled,
                "A declined request whose title later appears in the library is an availability observation and not a decision. Approving it first is the move that says a person changed the answer."),
            Refused(RequestState.Declined, RequestState.Failed, NothingWasSent),

            Refused(RequestState.Fulfilled, RequestState.Open, FulfilledIsTheEnd),
            Refused(RequestState.Fulfilled, RequestState.Approved, FulfilledIsTheEnd),
            Refused(RequestState.Fulfilled, RequestState.Declined, FulfilledIsTheEnd),
            Refused(RequestState.Fulfilled, RequestState.Fulfilled, NotAMove),
            Refused(RequestState.Fulfilled, RequestState.Failed, FulfilledIsTheEnd),

            Refused(RequestState.Failed, RequestState.Open, NoUndeciding),
            Legal(RequestState.Failed, RequestState.Approved, Decision, "An operator sends it onward again."),
            Legal(
                RequestState.Failed,
                RequestState.Declined,
                Decision,
                "An operator gives up on it, and the reason says why. Without this a failure has no ending."),
            Legal(
                RequestState.Failed,
                RequestState.Fulfilled,
                Observation,
                "It arrived after all, by this route or by somebody putting it in the library by hand."),
            Refused(RequestState.Failed, RequestState.Failed, NotAMove)
        };

        return new ReadOnlyCollection<RequestTransition>(table);
    }

    private static RequestTransition Legal(RequestState from, RequestState to, RequestActor permitted, string why)
        => new() { From = from, To = to, IsLegal = true, Permitted = permitted, Why = why };

    /// <summary>
    /// A cell nobody may make. The permitted set is <see cref="RequestActor.None"/> rather than a
    /// parameter, because "this move is refused" and "some caller may still make it" cannot both be
    /// true, and a helper that let them be written together is the disagreement waiting to happen.
    /// </summary>
    /// <param name="from">The state being moved out of.</param>
    /// <param name="to">The state being moved into.</param>
    /// <param name="why">The reason this pair is refused.</param>
    /// <returns>The refused cell.</returns>
    private static RequestTransition Refused(RequestState from, RequestState to, string why)
        => new() { From = from, To = to, IsLegal = false, Permitted = RequestActor.None, Why = why };
}

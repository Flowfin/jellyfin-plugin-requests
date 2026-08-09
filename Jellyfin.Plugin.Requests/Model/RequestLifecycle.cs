using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// The moves a request may make, as a table, and the one place a move is made.
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
    /// Moves a request, or refuses to.
    /// <para>
    /// This is the only place in the plugin that changes a request's state. The record is immutable
    /// and a caller could still write <c>with { State = ... }</c>, so this being the one place is
    /// held by review and by the callers that exist rather than by anything that refuses the
    /// alternative; that gap is named in <c>docs/lifecycle.md</c>.
    /// </para>
    /// </summary>
    /// <param name="request">The request to move.</param>
    /// <param name="to">The state to move it into.</param>
    /// <param name="at">
    /// When the move happened, from the injected clock rather than the machine's.
    /// </param>
    /// <param name="movedByUserId">
    /// The Jellyfin user who moved it, or <see langword="null"/> where the plugin moved it on its
    /// own after looking at the library.
    /// </param>
    /// <returns>A new request in the new state.</returns>
    /// <exception cref="ArgumentNullException">Where no request was given.</exception>
    /// <exception cref="IllegalRequestTransitionException">Where the table refuses the move.</exception>
    public static MediaRequest Move(
        MediaRequest request,
        RequestState to,
        DateTimeOffset at,
        Guid? movedByUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cell = Cell(request.State, to);

        if (!cell.IsLegal)
        {
            throw new IllegalRequestTransitionException(cell.From, cell.To, cell.Why);
        }

        return request with
        {
            State = to,
            StateChangedAt = at,
            StateChangedByUserId = movedByUserId
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

        var table = new List<RequestTransition>
        {
            Refused(RequestState.Open, RequestState.Open, NotAMove),
            Legal(RequestState.Open, RequestState.Approved, "An operator says yes."),
            Legal(RequestState.Open, RequestState.Declined, "An operator says no."),
            Legal(
                RequestState.Open,
                RequestState.Fulfilled,
                "The library already holds what was asked for, so there is nothing left for anybody to decide."),
            Refused(RequestState.Open, RequestState.Failed, NothingWasSent),

            Refused(RequestState.Approved, RequestState.Open, NoUndeciding),
            Refused(RequestState.Approved, RequestState.Approved, NotAMove),
            Legal(
                RequestState.Approved,
                RequestState.Declined,
                "An operator takes an approval back, and the reason says why. This is the repair for an approval given by mistake."),
            Legal(RequestState.Approved, RequestState.Fulfilled, "It arrived and the person who asked can watch it."),
            Legal(
                RequestState.Approved,
                RequestState.Failed,
                "It was sent onward and did not arrive, so it stops looking like an operator forgot about it."),

            Refused(RequestState.Declined, RequestState.Open, NoUndeciding),
            Legal(
                RequestState.Declined,
                RequestState.Approved,
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
            Legal(RequestState.Failed, RequestState.Approved, "An operator sends it onward again."),
            Legal(
                RequestState.Failed,
                RequestState.Declined,
                "An operator gives up on it, and the reason says why. Without this a failure has no ending."),
            Legal(
                RequestState.Failed,
                RequestState.Fulfilled,
                "It arrived after all, by this route or by somebody putting it in the library by hand."),
            Refused(RequestState.Failed, RequestState.Failed, NotAMove)
        };

        return new ReadOnlyCollection<RequestTransition>(table);
    }

    private static RequestTransition Legal(RequestState from, RequestState to, string why)
        => new() { From = from, To = to, IsLegal = true, Why = why };

    private static RequestTransition Refused(RequestState from, RequestState to, string why)
        => new() { From = from, To = to, IsLegal = false, Why = why };
}

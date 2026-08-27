using System;

namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// One thing that happened to a request, kept forever. Every entry but the first is a move.
/// <para>
/// The state on a request answers what is true now. An operator dealing with a complaint needs what
/// happened: who approved it, when, who declined the earlier one and for what reason, and whether a
/// person did it at all. <see cref="MediaRequest.State"/> and
/// <see cref="MediaRequest.StateChangedByUserId"/> answer only the last of those, and answering the
/// rest from them is guessing.
/// </para>
/// <para>
/// An entry carries the reason and the note the move was made with, so a decline that is later taken
/// back leaves its reason here rather than nowhere. That is the one thing the current-value fields
/// cannot do: they are overwritten by the next move by definition.
/// </para>
/// <para>
/// <b>The first entry is an arrival rather than a move.</b> It says how the ask reached this server,
/// which is #118's condition and is a question about the record's provenance rather than about
/// anything anybody decided. <see cref="Arriving"/> is the only way to build one, and
/// <see cref="Arrival"/> is what separates it from the rows beneath it.
/// </para>
/// </summary>
public sealed record RequestHistoryEntry
{
    private readonly string? _note;

    /// <summary>
    /// Gets the state the request was in before this move.
    /// </summary>
    public required RequestState From { get; init; }

    /// <summary>
    /// Gets the state it was in after it.
    /// </summary>
    public required RequestState To { get; init; }

    /// <summary>
    /// Gets when the move happened, from the injected clock rather than the machine's.
    /// </summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>
    /// Gets what the mover was, and never which person they were.
    /// <para>
    /// <b>It says a role because a history outlives the accounts it names.</b> An entry naming an
    /// account that has since been deleted keeps an identifier for somebody who asked to be gone,
    /// and an append-only store is a design choice rather than an answer to that. A role keeps every
    /// fact this history exists for, that a decision was taken and when and by what kind of caller,
    /// while the identifier goes: the decision survives, the person does not have to.
    /// </para>
    /// <para>
    /// <b>What that costs is real and permanent, and is not softened here.</b> Nothing in this tree
    /// can attribute a past decision to an individual any more. That is the trade taken deliberately
    /// on #49, in exchange for not holding identifiers this plugin has no right to keep.
    /// <see cref="MediaRequest.StateChangedByUserId"/> is not a substitute for it: it is the current
    /// mover only, it is overwritten by the next move, and what it means after a deletion is decided
    /// separately.
    /// </para>
    /// <para>
    /// <b><see cref="RequestActor.Plugin"/> is a real answer rather than an absent one.</b> A move
    /// the plugin made on its own after looking at the library reads as the plugin having made it,
    /// which is the distinction that matters in a complaint: an operator should not have to answer
    /// for a decision nobody took.
    /// </para>
    /// </summary>
    public required RequestActor By { get; init; }

    /// <summary>
    /// Gets the reason the move was made with, where it was a decline, and <see langword="null"/>
    /// otherwise. This is why an entry is worth more than a pair of states: the reason on the
    /// request is overwritten the moment somebody changes their mind, and this copy is not.
    /// </summary>
    public DeclineReason? Reason { get; init; }

    /// <summary>
    /// Gets the free text written alongside the reason, or <see langword="null"/> where there was
    /// none. Untrusted where it is shown, and capped by the same rule as the note on the request
    /// itself, because a cap the copy does not carry is no cap at all.
    /// </summary>
    /// <exception cref="RequestTextTooLongException">
    /// Where the text is longer than <see cref="MediaRequest.NoteMaximumLength"/>.
    /// </exception>
    public string? Note
    {
        get => _note;
        init => _note = MediaRequest.Note(value, nameof(Note));
    }

    /// <summary>
    /// Gets how the ask reached this server, on the entry recording that the request came into
    /// existence, and <see langword="null"/> on every entry recording a move. A move is something
    /// somebody did to a request that was already here, so it has no arrival to name.
    /// </summary>
    public RequestArrival? Arrival { get; init; }

    /// <summary>
    /// The entry a request comes into existence with, saying how the ask reached this server.
    /// <para>
    /// <b>It is derived from the request rather than passed beside it</b>, so a surface cannot write
    /// an arrival that disagrees with the record it is on: the moment, the person and the state all
    /// come off the request being made. What the caller supplies is the one thing the record does not
    /// already hold, which is which surface it arrived on.
    /// </para>
    /// <para>
    /// <b><see cref="From"/> and <see cref="To"/> both carry the state the request came into
    /// existence in, because an arrival moves nothing.</b> Reading either of them alone therefore
    /// answers "what state was this in" correctly at every row of a history including this one, and
    /// the pair reading as a move that changed nothing is what <see cref="Arrival"/> is there to
    /// separate. The alternative, a <see cref="From"/> that can be absent, changes what an existing
    /// field means for every entry already written to somebody's disk, which
    /// <c>docs/storage.md</c> is explicit costs a new on-disk version; adding a field an older
    /// reader ignores does not.
    /// </para>
    /// </summary>
    /// <param name="over">Which surface the ask arrived on.</param>
    /// <param name="request">The request being made, as the surface built it.</param>
    /// <returns>The entry to put at the head of that request's history.</returns>
    /// <exception cref="ArgumentNullException">Where there is no request to derive it from.</exception>
    public static RequestHistoryEntry Arriving(RequestArrival over, MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new RequestHistoryEntry
        {
            From = request.State,
            To = request.State,
            At = request.RequestedAt,

            // An arrival is the ask, and the only caller an ask can have come from is the person it
            // is filed against. Nobody else has touched the request yet, so an entry saying anything
            // else here would be saying something that was not so.
            By = RequestActor.Requester,
            Arrival = over
        };
    }
}

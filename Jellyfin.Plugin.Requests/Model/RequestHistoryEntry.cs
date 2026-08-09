using System;

namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// One move a request made, kept forever.
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
    /// Gets the Jellyfin user who made the move, or <see langword="null"/> where the plugin made it
    /// on its own after looking at the library. The distinction matters in a complaint: an operator
    /// should not have to answer for a decision nobody took.
    /// </summary>
    public Guid? ByUserId { get; init; }

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
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// One person asking for one thing. This is the object the plugin is about, and everything else on
/// this board reads or moves it.
/// <para>
/// It is a value with no behaviour and no dependencies. Every property is a number, a string, an
/// identifier, a time or an enumeration, so the record cannot reach a metadata source, a library or
/// a client to answer a question about itself. That is deliberate and it is what makes the display
/// snapshot below meaningful.
/// </para>
/// <para>
/// The snapshot is stored rather than fetched because the queue has to be readable when no metadata
/// source is reachable, and because this plugin calls none by design, decided in #92. A title that
/// is only an identifier is a row an operator cannot decide on the day the sibling that resolved it
/// is uninstalled, and a title that was renamed upstream should still read in the history as what
/// the person actually asked for.
/// </para>
/// <para>
/// It holds nothing about how media is fetched. Quality profiles, root folders, download clients
/// and indexers belong to whatever service does the fetching, and carrying them here would make
/// this plugin a worse copy of one.
/// </para>
/// <para>
/// The record is immutable. A move produces a new value with <c>with</c> rather than an edit in
/// place, so the history in #43 and the store contract in #45 are free to decide what a change
/// means without a caller having already made one behind their back.
/// </para>
/// </summary>
public sealed record MediaRequest
{
    /// <summary>
    /// The longest a piece of free text on a request may be. One number for both notes, because two
    /// numbers that happen to be equal are two numbers that can drift apart for no reason anybody
    /// wrote down.
    /// <para>
    /// Five hundred characters is a short paragraph. Both fields are read whole, in a queue row and
    /// in whatever tells the requester what happened, and neither surface has anywhere to put an
    /// essay. The cap is refused rather than applied, so nothing is ever stored that the person who
    /// typed it did not write.
    /// </para>
    /// </summary>
    public const int NoteMaximumLength = 500;

    private readonly string? _requesterNote;
    private readonly string? _declineNote;

    /// <summary>
    /// Gets this request's own identifier, which is not the identifier of anything it refers to.
    /// Where it comes from is the injected identifier source in #34, so a test does not get a
    /// different value on every run.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the Jellyfin user who asked. Held as the server's user identifier rather than a name,
    /// because a name is renamed and because the surfaces have to answer "is this yours" against
    /// the identifier the session carries.
    /// </summary>
    public required Guid RequestedByUserId { get; init; }

    /// <summary>
    /// Gets when it was asked for, from the injected clock in #34 rather than the wall clock. An
    /// offset rather than a bare time, so a server that moves timezone does not reorder its own
    /// queue.
    /// </summary>
    public required DateTimeOffset RequestedAt { get; init; }

    /// <summary>
    /// Gets what sort of thing was asked for.
    /// </summary>
    public required RequestedItemKind Kind { get; init; }

    /// <summary>
    /// Gets the title as it read at the moment of the request. This is the snapshot: an operator
    /// reads the queue from it with nothing else reachable, and it stays what was asked for even if
    /// the upstream title changes afterwards.
    /// </summary>
    public required string DisplayTitle { get; init; }

    /// <summary>
    /// Gets the release year as it read at the moment of the request, or <see langword="null"/>
    /// where the request arrived without one. Part of the same snapshot, and the field that
    /// separates two films sharing a title.
    /// </summary>
    public int? DisplayYear { get; init; }

    /// <summary>
    /// Gets the external identifiers the request arrived with, keyed by provider name. Empty is a
    /// real case: a request typed by a person carries a title and nothing else.
    /// <para>
    /// This is what a fulfilment check and a bridge match on, and whether two requests naming the
    /// same title are the same request is decided against these rather than against
    /// <see cref="DisplayTitle"/>, in #38. Nothing here says which providers exist.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderIds { get; init; } = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>
    /// Gets where the request stands. Defaults to <see cref="RequestState.Open"/>, because a
    /// request that was never decided is the state a new one is in.
    /// </summary>
    public RequestState State { get; init; } = RequestState.Open;

    /// <summary>
    /// Gets when the state last moved. On a request nothing has decided this is when it was
    /// created, so the queue can be sorted by it without a null case.
    /// </summary>
    public required DateTimeOffset StateChangedAt { get; init; }

    /// <summary>
    /// Gets the Jellyfin user who last moved the state, or <see langword="null"/> where no person
    /// did: a request that has not moved since it was created, or one the plugin moved on its own
    /// after looking at the library. This is the current mover only. Who moved it at every earlier
    /// step is the append-only history in #43, and this field is not a substitute for it.
    /// </summary>
    public Guid? StateChangedByUserId { get; init; }

    /// <summary>
    /// Gets what the person who asked wanted to say about it, or <see langword="null"/> where they
    /// said nothing. Sometimes this carries the only information that makes the request decidable:
    /// which of two films with one title, or that it is for somebody's birthday on Friday.
    /// <para>
    /// Free text written by a person, so it is untrusted wherever it is shown. It is stored exactly
    /// as it was typed and nothing here escapes it, because escaping belongs at the one place that
    /// renders it and text escaped twice is text with visible ampersands in it.
    /// </para>
    /// <para>
    /// Text that is only whitespace is held as <see langword="null"/>. "No note" and "an empty note"
    /// are the same fact, and keeping two representations of it means every reader has to test for
    /// both and one of them eventually will not.
    /// </para>
    /// </summary>
    /// <exception cref="RequestTextTooLongException">
    /// Where the text is longer than <see cref="NoteMaximumLength"/>.
    /// </exception>
    public string? RequesterNote
    {
        get => _requesterNote;
        init => _requesterNote = Note(value, nameof(RequesterNote));
    }

    /// <summary>
    /// Gets why this request was declined, or <see langword="null"/> where it is not declined. A
    /// decline always carries one: <see cref="RequestLifecycle.Decline"/> is the only way into
    /// <see cref="RequestState.Declined"/> and it requires a reason, decided on #113.
    /// <para>
    /// It is cleared when a request leaves <see cref="RequestState.Declined"/>, because a reason
    /// standing on an approved request is a sentence that is no longer true. What the request used
    /// to say is the append-only history in #43, and until that exists the earlier reason is gone
    /// rather than kept somewhere else.
    /// </para>
    /// </summary>
    public DeclineReason? DeclineReason { get; init; }

    /// <summary>
    /// Gets what the operator wrote alongside <see cref="DeclineReason"/>, or
    /// <see langword="null"/> where they wrote nothing. Required beside
    /// <see cref="Model.DeclineReason.Other"/>, which says nothing on its own, and optional beside
    /// every other value.
    /// <para>
    /// Free text written by a person, under the same rules as
    /// <see cref="RequesterNote"/>: untrusted where it is shown, stored as typed, whitespace held
    /// as <see langword="null"/>, and cleared when the request stops being declined.
    /// </para>
    /// </summary>
    /// <exception cref="RequestTextTooLongException">
    /// Where the text is longer than <see cref="NoteMaximumLength"/>.
    /// </exception>
    public string? DeclineNote
    {
        get => _declineNote;
        init => _declineNote = Note(value, nameof(DeclineNote));
    }

    /// <summary>
    /// Gets whether the server holds what was asked for. Separate from <see cref="State"/> on
    /// purpose: approval is a decision and availability is an observation, and the pair
    /// approved-and-absent is the ordinary case rather than a contradiction.
    /// </summary>
    public LibraryAvailability Availability { get; init; } = LibraryAvailability.Unknown;

    /// <summary>
    /// Gets when the library was last looked at for this request, or <see langword="null"/> where
    /// nothing has looked. An availability with no observation time is a claim with no date, and an
    /// operator asking why a request still says absent needs to know whether anything checked.
    /// </summary>
    public DateTimeOffset? AvailabilityCheckedAt { get; init; }

    /// <summary>
    /// Refuses a note that is too long and flattens an empty one to nothing.
    /// </summary>
    /// <param name="text">The text as it arrived.</param>
    /// <param name="field">The property being written, for the refusal to name.</param>
    /// <returns>The text, or <see langword="null"/> where there was nothing in it.</returns>
    private static string? Note(string? text, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text.Length > NoteMaximumLength
            ? throw new RequestTextTooLongException(field, NoteMaximumLength, text.Length)
            : text;
    }
}

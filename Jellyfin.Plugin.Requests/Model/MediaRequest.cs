using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Jellyfin.Plugin.Requests.Bridge;

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
    private readonly IReadOnlyList<int> _seasons = [];
    private readonly IReadOnlyList<Guid> _wantIds = [];

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
    /// Gets the seasons asked for, in order and without repeats. Empty means the whole series, and
    /// on a film it means nothing at all and is the only value allowed there.
    /// <para>
    /// It is part of what was asked for rather than a detail of it, so it is part of what makes two
    /// requests the same request in <see cref="RequestIdentity"/>. A request for one season and a
    /// request for the programme are different asks, and a queue that treated them as one would
    /// leave somebody waiting for seasons nobody ever approved.
    /// </para>
    /// <para>
    /// Empty meaning the whole series is the one convention here worth remembering. The alternative,
    /// listing every season a show has, means the request is wrong the day another one airs, and
    /// this plugin has no metadata source to ask how many there are, decided in #92.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Where a season is repeated, or is not a number a season can have.
    /// </exception>
    public IReadOnlyList<int> Seasons
    {
        get => _seasons;
        init => _seasons = SeasonSet(value);
    }

    /// <summary>
    /// Gets everyone who asked for this after it existed, oldest first. Empty on a request only one
    /// person has asked for, which is most of them.
    /// <para>
    /// Two people asking for one film is one request, decided in #38, and this is where the second
    /// person is recorded. Without it the second ask is either a duplicate row an operator decides
    /// twice or a person who is silently not told when the answer arrives.
    /// </para>
    /// <para>
    /// It never holds <see cref="RequestedByUserId"/>, and it never holds the same person twice.
    /// Asking again for something you have already asked for is not a second fact about the request,
    /// and a count of this list is a count of people rather than of clicks.
    /// </para>
    /// </summary>
    public IReadOnlyList<Guid> JoinedByUserIds { get; init; } = [];

    /// <summary>
    /// Gets the sibling discover plugin's own identifiers for the wants this request absorbed,
    /// oldest first. Empty on a request nobody handed over, which is every request a person typed.
    /// <para>
    /// <b>This is an idempotency key and not a second identity.</b> Whether two asks are the same
    /// thing is <see cref="RequestIdentity"/>'s answer, over the provider identifiers and the
    /// seasons. This answers a narrower question: has this exact want already been taken. The other
    /// side derives one identifier per title and user and hands it over again after a refresh that
    /// recreated the item, after a restart, and after a user undid and redid the gesture, so
    /// without it each of those is another acquisition of something somebody is already waiting for.
    /// </para>
    /// <para>
    /// A list rather than one value, because one request absorbs several wants: two people wanting
    /// the same film are two wants on the other side and one request here, in #38.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Where a want is named twice, or where one of them names nothing.
    /// </exception>
    public IReadOnlyList<Guid> WantIds
    {
        get => _wantIds;
        init => _wantIds = WantSet(value);
    }

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
    /// Gets every move this request has made, oldest first. Empty on a request nothing has decided,
    /// because being created is not a move.
    /// <para>
    /// Append-only. <see cref="RequestLifecycle"/> is the only thing that adds to it and it never
    /// removes or rewrites an entry, which is what makes the list worth reading at all. Nothing in
    /// the type system says so, because a list that could refuse a removal would be a type of its
    /// own; what says so is the lint rule <c>history-is-only-appended-to</c>, which refuses an
    /// assignment to this property anywhere but the one place that appends.
    /// </para>
    /// <para>
    /// This is where a decline reason survives being taken back. The reason on the request is the
    /// current one and is cleared when the request stops being declined; the entry that carried it
    /// stays.
    /// </para>
    /// </summary>
    public IReadOnlyList<RequestHistoryEntry> History { get; init; } = [];

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
    /// Gets what an external request service called this after it was handed over, or
    /// <see langword="null"/> where nothing was. Null is the ordinary value: most servers run no
    /// such service, and on those nothing is ever handed anywhere.
    /// <para>
    /// It is kept on the request because losing it means the two systems can never be reconciled
    /// again. A reference held anywhere else would be a second store to keep in step with this one,
    /// and the first restore from a backup would separate them.
    /// </para>
    /// <para>
    /// Both halves of it are strings the service chose, unread. Nothing here parses one, which is
    /// what keeps the record a value with nothing to resolve, and it is why a reference on a plain
    /// record is not the same kind of field as a client or a manager would be.
    /// </para>
    /// <para>
    /// It is written once. <see cref="Bridge.BridgeSubmission"/> hands a request over only where
    /// this is null, so a request that already carries one is never submitted a second time, and
    /// this field is the fact that decides it.
    /// </para>
    /// </summary>
    public BackendReference? Backend { get; init; }

    /// <summary>
    /// Gets when a handover to an external request service was last tried and failed, or
    /// <see langword="null"/> where none has. Null is the ordinary value on every server, including
    /// every server that runs no such service and therefore never tries.
    /// <para>
    /// It exists because <see cref="Backend"/> alone cannot answer the question an operator working
    /// the queue actually has. A request that was never submitted and a request whose submission
    /// failed both carry no reference, so with one field the two read as the same request, and the
    /// second one is an approval nobody is fetching. Two fields make three states: nothing tried,
    /// tried and failed, handed over.
    /// </para>
    /// <para>
    /// <b>It is the last attempt and not a count.</b> A tally of failures would be a number an
    /// operator cannot act on differently, and the moment is what separates a service that broke an
    /// hour ago from one that broke while they were reading the page.
    /// </para>
    /// <para>
    /// <b>Only <see cref="Bridge.BridgeSubmission"/> writes it, and a handover that succeeds clears
    /// it in the same write that sets <see cref="Backend"/>.</b> So the pair can never say both at
    /// once, and a request carrying a reference carries no failure behind it. A request re-approved
    /// after a failure keeps the mark until the next attempt answers, which is the honest reading:
    /// what is known then is still that the last attempt failed.
    /// </para>
    /// </summary>
    public DateTimeOffset? HandoverFailedAt { get; init; }

    /// <summary>
    /// Whether this person is one of the people waiting for this request, whether they asked first
    /// or joined an existing one. Both surfaces have to answer "is this yours" the same way, and a
    /// caller working it out for themselves is a caller that will check one of the two lists.
    /// </summary>
    /// <param name="userId">The Jellyfin user being asked about.</param>
    /// <returns><see langword="true"/> where that person asked for this.</returns>
    public bool WasAskedForBy(Guid userId)
        => RequestedByUserId == userId || JoinedByUserIds.Contains(userId);

    /// <summary>
    /// Refuses a want list that is not a set of wants. A want named twice is a caller's mistake
    /// being stored as a fact, and an empty identifier names nothing, so both are refused rather
    /// than tidied: the whole point of these is that they are the key a repeat is recognised by,
    /// and a key quietly cleaned up is one nobody can rely on.
    /// <para>
    /// The order is the order the wants were absorbed and is kept, so the first is the want the
    /// request was made for.
    /// </para>
    /// </summary>
    /// <param name="wantIds">The wants as they arrived.</param>
    /// <returns>The same wants.</returns>
    private static IReadOnlyList<Guid> WantSet(IReadOnlyList<Guid>? wantIds)
    {
        if (wantIds is null || wantIds.Count == 0)
        {
            return [];
        }

        if (wantIds.Any(wantId => wantId == Guid.Empty))
        {
            throw new ArgumentException(
                "A want identifier that names nothing is refused, because these are what a repeat of the same want is recognised by.",
                nameof(wantIds));
        }

        if (wantIds.Distinct().Count() != wantIds.Count)
        {
            throw new ArgumentException(
                "The same want is named twice, and a want absorbed twice is one fact recorded as two.",
                nameof(wantIds));
        }

        return [.. wantIds];
    }

    /// <summary>
    /// Refuses a season list that is not a set of seasons. A repeat is a caller's mistake being
    /// stored as a fact, and a zero or a negative number is not a season any client has, so both
    /// are refused rather than tidied: a list quietly cleaned up is one the caller never learns was
    /// wrong.
    /// <para>
    /// The order is the caller's own and is kept. Sorting here would make two requests that name the
    /// same seasons in different orders read as different asks in a history, and the comparison in
    /// <see cref="RequestIdentity"/> does not depend on the order anyway.
    /// </para>
    /// </summary>
    /// <param name="seasons">The seasons as they arrived.</param>
    /// <returns>The same seasons.</returns>
    private static IReadOnlyList<int> SeasonSet(IReadOnlyList<int>? seasons)
    {
        if (seasons is null || seasons.Count == 0)
        {
            return [];
        }

        // Any rather than FirstOrDefault, because the value this refuses includes zero and a
        // sentinel that is also a refused value says "nothing was wrong" for exactly one of them.
        if (seasons.Any(season => season < 1))
        {
            throw new ArgumentException(
                FormattableString.Invariant(
                    $"A season number has to be 1 or more, and {seasons.First(season => season < 1)} is not."),
                nameof(seasons));
        }

        if (seasons.Distinct().Count() != seasons.Count)
        {
            throw new ArgumentException(
                "A season is named twice. The seasons asked for are a set, and a repeat is a caller's mistake rather than a request for it twice.",
                nameof(seasons));
        }

        return [.. seasons];
    }

    /// <summary>
    /// Refuses a note that is too long and flattens an empty one to nothing. Internal because
    /// <see cref="RequestHistoryEntry"/> holds a copy of the same text and has to hold it under the
    /// same rule; two implementations of one cap is one of them being raised alone.
    /// </summary>
    /// <param name="text">The text as it arrived.</param>
    /// <param name="field">The property being written, for the refusal to name.</param>
    /// <returns>The text, or <see langword="null"/> where there was nothing in it.</returns>
    internal static string? Note(string? text, string field)
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

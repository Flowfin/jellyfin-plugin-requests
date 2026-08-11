using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Bridge;

/// <summary>
/// What the external service's words mean on this side, as a table.
/// <para>
/// It is a table rather than a switch inside an adapter because the mapping is where a bridge
/// quietly goes wrong: the two models do not line up, the mismatch looks like a detail, and a
/// request sits in the wrong state for a week before anybody notices. A table can be read by a
/// person, printed into <c>docs/bridge.md</c> and tested row by row, including the rows that move
/// nothing. A second adapter gets rows here rather than a mapping of its own, so two adapters
/// cannot disagree about what a word means with nothing saying which is right.
/// </para>
/// <para>
/// <b>Most rows move nothing, and that is the finding rather than an omission.</b> The service sits
/// downstream of a decision an operator already made here and upstream of a library check that runs
/// here. So it can tell this side that its own approval step has run, which this side does not act
/// on; that it has finished, which is not the same fact as this server holding the media; or that
/// it has given up, which is the one thing this side could not otherwise know. Only the last of
/// those moves a request.
/// </para>
/// <para>
/// <b>A word this table does not hold moves nothing and says so.</b> <see cref="Lookup"/> answers
/// <see langword="null"/> for it, which is a different answer from a row that moves nothing: the
/// first is "never seen", the second is "seen and deliberately inert". There is no default case
/// that guesses, because a guess here is a request put into a state nobody chose, and the whole
/// cost of being wrong lands on somebody who cannot see why. What a caller does with an unseen word
/// is #83's, and refusing to invent one is this table's.
/// </para>
/// <para>
/// <b>Which words the service uses is fixed by this board and was not read off a running service.</b>
/// The list is the one #81 names for the Overseerr form, which #113 chose as the form the first
/// adapter is written against. Nothing here reached a service to confirm it, and nothing in this
/// tree could. That is what the unseen rule is for, and <c>docs/bridge.md</c> says the same in the
/// place a reader would otherwise assume the list was measured.
/// </para>
/// </summary>
public static class BackendStates
{
    /// <summary>
    /// Gets every word the external service uses, what this side does about it, and why. This is the
    /// source the table and the reasons in <c>docs/bridge.md</c> are printed from, and
    /// <c>TheDocumentedMappingIsTheTableInTheCode</c> and
    /// <c>TheDocumentedReasonsAreTheReasonsInTheCode</c> refuse the two disagreeing.
    /// </summary>
    public static IReadOnlyList<BackendStateMapping> Table { get; } = BuildTable();

    /// <summary>
    /// Looks up what one report means here.
    /// </summary>
    /// <param name="vocabulary">Which of the service's two lists the adapter read the word from.</param>
    /// <param name="report">What the service said, in its own words.</param>
    /// <returns>
    /// The row for that word, or <see langword="null"/> where this table has never seen it. A row
    /// whose <see cref="BackendStateMapping.MoveTo"/> is <see langword="null"/> is not the same
    /// answer: that word is known and moves nothing on purpose.
    /// </returns>
    /// <exception cref="ArgumentNullException">Where no report was given.</exception>
    public static BackendStateMapping? Lookup(BackendVocabulary vocabulary, BackendReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        // Case is ignored and nothing else is. Two adapters against one service will spell the same
        // word two ways, and both spellings mean what the service meant; anything further, such as
        // trimming or folding separators, would be this table deciding that a word it does not hold
        // is really one it does, which is the guess the whole class refuses.
        return Table.FirstOrDefault(row =>
            row.Vocabulary == vocabulary
            && string.Equals(row.Reported, report.Reported, StringComparison.OrdinalIgnoreCase));
    }

    private static ReadOnlyCollection<BackendStateMapping> BuildTable()
    {
        const string TheLibrarySaysFulfilled =
            "The service having finished is not this server holding the media. Fulfilled is the library's word here, observed by the sweep when the person who asked can actually watch it, and taking the service's word for it would fulfil requests on a server whose library never received anything.";

        var table = new List<BackendStateMapping>
        {
            Inert(
                BackendVocabulary.RequestStatus,
                "PENDING",
                "The service is waiting for its own approval step. Nothing is handed to it until an operator here has already approved the request, so this word says where the service stands and nothing about where the request stands."),
            Inert(
                BackendVocabulary.RequestStatus,
                "APPROVED",
                "The service agrees with the decision this side already made, and a request cannot be approved twice. The row exists so that agreement is an answer rather than a word nothing recognises."),
            Moves(
                BackendVocabulary.RequestStatus,
                "DECLINED",
                RequestState.Failed,
                "The service will not fetch it, so it was sent onward and will not arrive by that route. It is not a decline here: a decline is an operator's answer and carries a reason, and from failed an operator can still decline it or send it onward again."),
            Moves(
                BackendVocabulary.RequestStatus,
                "FAILED",
                RequestState.Failed,
                "The thing doing the fetching gave up, which is what this state names and the reason it exists."),
            Inert(BackendVocabulary.RequestStatus, "COMPLETED", TheLibrarySaysFulfilled),
            Inert(
                BackendVocabulary.MediaStatus,
                "AVAILABLE",
                "Available there is available on whatever that service can see, which is neither this server's library nor this user's access to it. It has a row of its own because it is the word most likely to be wired straight to fulfilled by somebody in a hurry.")
        };

        return new ReadOnlyCollection<BackendStateMapping>(table);
    }

    private static BackendStateMapping Moves(
        BackendVocabulary vocabulary,
        string reported,
        RequestState moveTo,
        string why)
        => new() { Vocabulary = vocabulary, Reported = reported, MoveTo = moveTo, Why = why };

    /// <summary>
    /// A word that changes nothing here. It is a helper of its own rather than
    /// <see cref="Moves"/> with a null destination, so that a row moving nothing reads as a decision
    /// somebody took and cannot be produced by leaving an argument out.
    /// </summary>
    /// <param name="vocabulary">Which of the service's two lists the word belongs to.</param>
    /// <param name="reported">The word, as the service spells it.</param>
    /// <param name="why">Why hearing it moves nothing.</param>
    /// <returns>The row.</returns>
    private static BackendStateMapping Inert(BackendVocabulary vocabulary, string reported, string why)
        => new() { Vocabulary = vocabulary, Reported = reported, MoveTo = null, Why = why };
}

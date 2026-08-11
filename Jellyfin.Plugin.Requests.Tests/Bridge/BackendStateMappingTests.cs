using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Model;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Bridge;

/// <summary>
/// What the external service's words mean here, word by word, and the three things that make the
/// mapping a table rather than an adapter's opinion: every word answers what this list says, a word
/// nobody has seen is answered as unseen rather than guessed at, and the table printed in the
/// documentation is the one the code reads.
/// <para>
/// The expected answers below are written out by hand rather than derived from
/// <see cref="BackendStates.Table"/>. Derived ones would compare the table with itself and pass for
/// whatever it happened to say, including the day somebody wires availability over there to
/// fulfilment here.
/// </para>
/// </summary>
public class BackendStateMappingTests
{
    /// <summary>
    /// Every word the table holds, with what it does here, asserted against this list rather than
    /// against the table. Six lines is the whole mapping, and a change to any of them is a change to
    /// this list with a reason in the commit that made it.
    /// </summary>
    /// <param name="vocabulary">Which of the service's two lists the word was read from.</param>
    /// <param name="word">The word the service reported.</param>
    /// <param name="expected">The state this side moves the request into, or nothing.</param>
    [Theory]

    // The service's own approval step and its agreement with a decision already made here say
    // nothing this side acts on. Only a fetch that will not arrive does.
    [InlineData(BackendVocabulary.RequestStatus, "PENDING", null)]
    [InlineData(BackendVocabulary.RequestStatus, "APPROVED", null)]
    [InlineData(BackendVocabulary.RequestStatus, "DECLINED", RequestState.Failed)]
    [InlineData(BackendVocabulary.RequestStatus, "FAILED", RequestState.Failed)]

    // Finished there is not fulfilled here. Fulfilment is what this server's library says, and both
    // of these are the words somebody in a hurry would wire straight to it.
    [InlineData(BackendVocabulary.RequestStatus, "COMPLETED", null)]
    [InlineData(BackendVocabulary.MediaStatus, "AVAILABLE", null)]
    public void TheMappingSaysWhatThisListSays(BackendVocabulary vocabulary, string word, RequestState? expected)
    {
        var row = BackendStates.Lookup(vocabulary, new BackendReport { Reported = word });

        Assert.NotNull(row);
        Assert.Equal(expected, row.MoveTo);
    }

    /// <summary>
    /// The table holds those six words and no others, and no word twice in one list. Without this a
    /// row added later is invisible: the theory above still passes, because it only asks about the
    /// words it names.
    /// </summary>
    [Fact]
    public void TheTableHoldsEveryWordOnThatListAndNothingElse()
    {
        var expected = new[]
        {
            "RequestStatus PENDING",
            "RequestStatus APPROVED",
            "RequestStatus DECLINED",
            "RequestStatus FAILED",
            "RequestStatus COMPLETED",
            "MediaStatus AVAILABLE"
        };

        var held = BackendStates.Table
            .Select(row => string.Concat(row.Vocabulary.ToString(), " ", row.Reported))
            .ToArray();

        Assert.Equal(expected, held);
        Assert.Equal(expected.Length, held.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A word the table has never seen moves nothing, and it is answered as unseen rather than by
    /// the nearest row that looks about right. This is the guard against a default case: a request
    /// put into a state nobody chose reads exactly like one an operator moved, in the queue and in
    /// the history, and the person it belongs to is told something untrue about their own request.
    /// </summary>
    /// <param name="word">A word from the service that this table does not hold.</param>
    [Theory]
    [InlineData("BLACKLISTED")]
    [InlineData("DELETED")]
    [InlineData("PROCESSING")]
    public void AWordThisTableHasNeverSeenIsAnsweredAsUnseen(string word)
    {
        Assert.Null(BackendStates.Lookup(BackendVocabulary.RequestStatus, new BackendReport { Reported = word }));
    }

    /// <summary>
    /// Seen and inert is not the same answer as never seen. Both leave the request where it is
    /// today, so a caller that collapsed them would look correct until somebody wanted to report
    /// which words a service is sending that this plugin does not understand.
    /// </summary>
    [Fact]
    public void AWordThatMovesNothingIsADifferentAnswerFromAWordNobodyHasSeen()
    {
        var inert = BackendStates.Lookup(
            BackendVocabulary.RequestStatus,
            new BackendReport { Reported = "COMPLETED" });

        var unseen = BackendStates.Lookup(
            BackendVocabulary.RequestStatus,
            new BackendReport { Reported = "PROCESSING" });

        Assert.NotNull(inert);
        Assert.Null(inert.MoveTo);
        Assert.Null(unseen);
    }

    /// <summary>
    /// The list a word was read from is part of the question. The two lists share a word, so a
    /// lookup that ignored the list would answer "where the request stands" with the row for "what
    /// the service holds", which is the mistake this whole enumeration exists against.
    /// </summary>
    [Fact]
    public void TheSameWordInTheOtherListIsNotTheSameRow()
    {
        var report = new BackendReport { Reported = "PENDING" };

        Assert.NotNull(BackendStates.Lookup(BackendVocabulary.RequestStatus, report));
        Assert.Null(BackendStates.Lookup(BackendVocabulary.MediaStatus, report));
    }

    /// <summary>
    /// Case is ignored, because two adapters written against one service will spell one word two
    /// ways and both mean what the service meant.
    /// </summary>
    /// <param name="word">The word as some adapter spelled it.</param>
    [Theory]
    [InlineData("failed")]
    [InlineData("Failed")]
    [InlineData("FaIlEd")]
    public void CaseIsIgnoredWhenAWordIsLookedUp(string word)
    {
        var row = BackendStates.Lookup(BackendVocabulary.RequestStatus, new BackendReport { Reported = word });

        Assert.NotNull(row);
        Assert.Equal(RequestState.Failed, row.MoveTo);
    }

    /// <summary>
    /// Every row that moves something names a move the transition table allows and admits the plugin
    /// as the caller for, out of the state a submitted request is in.
    /// <para>
    /// This is the row's own bound and it is checked rather than reviewed. A decline over there
    /// looks like a decline here and mapping it that way would produce a move only an administrator
    /// may make, so the reconciliation in #83 would throw on a state the service reports in the
    /// ordinary course of its work. Approved is the state to check from because nothing is handed to
    /// the service before an operator approves it.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryRowThatMovesSomethingNamesAMoveThePluginMayMake()
    {
        var refused = BackendStates.Table
            .Where(row => row.MoveTo is not null)
            .Select(row => new { row.Reported, Cell = RequestLifecycle.Cell(RequestState.Approved, row.MoveTo!.Value) })
            .Where(pair => !pair.Cell.IsLegal || (pair.Cell.Permitted & RequestActor.Plugin) == RequestActor.None)
            .Select(pair => string.Concat(pair.Reported, " to ", pair.Cell.To.ToString()))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], refused);
    }

    /// <summary>
    /// Every row carries a reason, and the reason is a sentence rather than a restatement of the
    /// row. A row whose reason is blank is the row a later reader cannot argue with.
    /// </summary>
    [Fact]
    public void EveryRowSaysWhyItReadsTheWayItDoes()
    {
        Assert.DoesNotContain(BackendStates.Table, row => string.IsNullOrWhiteSpace(row.Why));
    }

    /// <summary>
    /// The mapping printed in <c>docs/bridge.md</c> is the table in the code, row for row and in the
    /// table's own order.
    /// </summary>
    [Fact]
    public void TheDocumentedMappingIsTheTableInTheCode()
    {
        var rows = MarkedSection("mapping").Where(line => line.StartsWith('|')).ToArray();

        // The row of dashes under the header is the table's own furniture, and the header says what
        // the columns are rather than what the mapping is. Everything after them is a row, so a row
        // deleted from the document fails the comparison at the end rather than being skipped here.
        var documented = rows.Skip(2).Select(row => string.Join(' ', SplitRow(row))).ToArray();

        var expected = BackendStates.Table
            .Select(row => string.Concat(row.Vocabulary.ToString(), " ", row.Reported, " ", Move(row)))
            .ToArray();

        Assert.Equal(expected, documented);
    }

    /// <summary>
    /// The reasons printed in <c>docs/bridge.md</c> are the reasons in the code, one bullet per row,
    /// in the table's own order. The rows above say what happens and this says why, and the why is
    /// the half a reader arrives for when the two systems disagree.
    /// </summary>
    [Fact]
    public void TheDocumentedReasonsAreTheReasonsInTheCode()
    {
        var expected = BackendStates.Table
            .Select(row => string.Format(
                CultureInfo.InvariantCulture,
                "- **{0} {1}**: {2}. {3}",
                row.Vocabulary,
                row.Reported,
                Move(row),
                row.Why))
            .ToArray();

        var documented = MarkedSection("reasons")
            .Where(line => line.StartsWith("- **", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(expected, documented);
    }

    /// <summary>
    /// How a row's destination is written for a reader: the state's own name, or the word for a row
    /// that moves nothing.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>What the document prints in that column.</returns>
    private static string Move(BackendStateMapping row) => row.MoveTo?.ToString() ?? "none";

    private static string[] SplitRow(string row)
        => [.. row.Trim('|').Split('|').Select(cell => cell.Trim())];

    /// <summary>
    /// Reads the lines the document marks off for one section.
    /// </summary>
    /// <param name="name">The name in the marker comments.</param>
    /// <returns>The trimmed lines between the markers.</returns>
    private static string[] MarkedSection(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "docs", "bridge.md");
        Assert.True(File.Exists(path), FormattableString.Invariant($"{path} was not copied next to the suite."));

        var opening = FormattableString.Invariant($"<!-- {name} begins -->");
        var closing = FormattableString.Invariant($"<!-- {name} ends -->");
        var inside = false;
        var lines = new List<string>();

        foreach (var line in File.ReadLines(path))
        {
            if (line.Trim().Equals(opening, StringComparison.Ordinal))
            {
                inside = true;
                continue;
            }

            if (line.Trim().Equals(closing, StringComparison.Ordinal))
            {
                inside = false;
                continue;
            }

            if (inside && line.Trim().Length > 0)
            {
                lines.Add(line.Trim());
            }
        }

        Assert.True(lines.Count > 0, FormattableString.Invariant($"docs/bridge.md has no {name} section."));

        return [.. lines];
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Requests.Model;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Model;

/// <summary>
/// The transition table, cell by cell, and the two things that make it a table rather than a
/// comment: a move it refuses throws instead of happening, and the table printed in the
/// documentation is the one the code reads.
/// <para>
/// The expected verdicts below are written out by hand rather than derived from
/// <see cref="RequestLifecycle.Table"/>. Derived ones would compare the table with itself and pass
/// for whatever it happened to say, including the day somebody widens a cell by accident.
/// </para>
/// </summary>
public class RequestLifecycleTests
{
    /// <summary>
    /// Every ordered pair of states has a cell and no pair has two. This is what makes adding a
    /// value to the enumeration a visible piece of work: a sixth state arrives with eleven missing
    /// cells and reds here, instead of quietly being a state nothing can leave or reach.
    /// </summary>
    [Fact]
    public void EveryOrderedPairOfStatesHasExactlyOneCell()
    {
        var states = Enum.GetValues<RequestState>();
        var missing = new List<string>();
        var duplicated = new List<string>();

        foreach (var from in states)
        {
            foreach (var to in states)
            {
                var cells = RequestLifecycle.Table.Count(cell => cell.From == from && cell.To == to);

                if (cells == 0)
                {
                    missing.Add(Pair(from, to));
                }
                else if (cells > 1)
                {
                    duplicated.Add(Pair(from, to));
                }
            }
        }

        Assert.Empty(missing);
        Assert.Empty(duplicated);
        Assert.Equal(states.Length * states.Length, RequestLifecycle.Table.Count);
    }

    /// <summary>
    /// Every cell of the table, legal and refused, asserted against a verdict written here rather
    /// than read from the table. Twenty-five lines is the whole rule, and a change to any cell is a
    /// change to this list with a reason in the commit that made it.
    /// </summary>
    /// <param name="from">The state being moved out of.</param>
    /// <param name="to">The state being moved into.</param>
    /// <param name="expected">Whether that move is allowed.</param>
    [Theory]

    // From open: an operator decides, or the library turns out to hold it already. Nothing has been
    // sent anywhere, so it cannot have failed, and it is already open.
    [InlineData(RequestState.Open, RequestState.Open, false)]
    [InlineData(RequestState.Open, RequestState.Approved, true)]
    [InlineData(RequestState.Open, RequestState.Declined, true)]
    [InlineData(RequestState.Open, RequestState.Fulfilled, true)]
    [InlineData(RequestState.Open, RequestState.Failed, false)]

    // From approved: it arrives, it does not arrive, or the approval is taken back by declining it.
    // Not back to open, because undeciding hides that somebody decided.
    [InlineData(RequestState.Approved, RequestState.Open, false)]
    [InlineData(RequestState.Approved, RequestState.Approved, false)]
    [InlineData(RequestState.Approved, RequestState.Declined, true)]
    [InlineData(RequestState.Approved, RequestState.Fulfilled, true)]
    [InlineData(RequestState.Approved, RequestState.Failed, true)]

    // From declined: an operator may change their mind, which is one move. Straight to fulfilled is
    // refused, because a declined title appearing in the library is an availability observation.
    [InlineData(RequestState.Declined, RequestState.Open, false)]
    [InlineData(RequestState.Declined, RequestState.Approved, true)]
    [InlineData(RequestState.Declined, RequestState.Declined, false)]
    [InlineData(RequestState.Declined, RequestState.Fulfilled, false)]
    [InlineData(RequestState.Declined, RequestState.Failed, false)]

    // Fulfilled is terminal. The wrong file is a new request, and a library that no longer holds it
    // is an observation rather than a decision being undone.
    [InlineData(RequestState.Fulfilled, RequestState.Open, false)]
    [InlineData(RequestState.Fulfilled, RequestState.Approved, false)]
    [InlineData(RequestState.Fulfilled, RequestState.Declined, false)]
    [InlineData(RequestState.Fulfilled, RequestState.Fulfilled, false)]
    [InlineData(RequestState.Fulfilled, RequestState.Failed, false)]

    // From failed: try again, give up, or it arrives after all. A failure with no way out would be
    // the state this one was added to remove.
    [InlineData(RequestState.Failed, RequestState.Open, false)]
    [InlineData(RequestState.Failed, RequestState.Approved, true)]
    [InlineData(RequestState.Failed, RequestState.Declined, true)]
    [InlineData(RequestState.Failed, RequestState.Fulfilled, true)]
    [InlineData(RequestState.Failed, RequestState.Failed, false)]
    public void TheTableSaysWhatThisListSays(RequestState from, RequestState to, bool expected)
    {
        Assert.Equal(expected, RequestLifecycle.IsLegal(from, to));
    }

    /// <summary>
    /// A move the table allows produces a new request in the new state, with the time and the mover
    /// recorded, and changes nothing else. The last part is what stops a move from quietly
    /// rewriting the title snapshot or the identifiers a later fulfilment check matches on.
    /// </summary>
    [Fact]
    public void AnAllowedMoveChangesTheStateTheTimeAndTheMoverAndNothingElse()
    {
        var request = ARequest();
        var movedAt = new DateTimeOffset(2026, 8, 9, 11, 30, 0, TimeSpan.Zero);
        var operatorId = new Guid("2d8f4b16-3a5c-4d97-8e21-7c6b5a4f3e29");

        var moved = RequestLifecycle.Move(request, RequestState.Approved, movedAt, operatorId);

        Assert.Equal(RequestState.Approved, moved.State);
        Assert.Equal(movedAt, moved.StateChangedAt);
        Assert.Equal(operatorId, moved.StateChangedByUserId);

        // Everything the move is not about, compared as one value: the record's generated equality
        // covers every field, so a field added later is covered here without this test being
        // edited. The four the move is about are put back first. The history is one of them because
        // a move appends to it, which RequestHistoryTests is about.
        Assert.Equal(
            request,
            moved with
            {
                State = request.State,
                StateChangedAt = request.StateChangedAt,
                StateChangedByUserId = request.StateChangedByUserId,
                History = request.History
            });
    }

    /// <summary>
    /// The plugin moving a request on its own records no mover. A request the library check
    /// fulfilled and one an operator fulfilled read differently, and reading the plugin's own move
    /// as somebody's decision is what would make an operator answer for a decision they did not
    /// take.
    /// </summary>
    [Fact]
    public void TheMoverIsAbsentWhereNoPersonMovedIt()
    {
        var request = ARequest();

        var moved = RequestLifecycle.Move(
            request,
            RequestState.Fulfilled,
            new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero),
            movedByUserId: null);

        Assert.Equal(RequestState.Fulfilled, moved.State);
        Assert.Null(moved.StateChangedByUserId);
    }

    /// <summary>
    /// A refused move throws, and the refusal names both states. Both halves matter: a refusal that
    /// returns the request unchanged is a move that silently did not happen, and a refusal reading
    /// only "invalid transition" sends whoever finds it in a log back into the code to work out
    /// which pair it meant.
    /// </summary>
    [Fact]
    public void ARefusedMoveThrowsAndTheRefusalNamesBothStates()
    {
        foreach (var cell in RequestLifecycle.Table.Where(entry => !entry.IsLegal))
        {
            var request = ARequest() with { State = cell.From };

            var refusal = Assert.Throws<IllegalRequestTransitionException>(
                () => MoveThroughTheRightDoor(
                    request,
                    cell.To,
                    new DateTimeOffset(2026, 8, 9, 13, 0, 0, TimeSpan.Zero)));

            Assert.Equal(cell.From, refusal.From);
            Assert.Equal(cell.To, refusal.To);
            Assert.Contains(cell.From.ToString(), refusal.Message, StringComparison.Ordinal);
            Assert.Contains(cell.To.ToString(), refusal.Message, StringComparison.Ordinal);
            Assert.Contains(cell.Why, refusal.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every cell says why. A refused cell with no reason is an absence with extra steps, and the
    /// question a reader arrives with is why a move they expected is not allowed.
    /// </summary>
    [Fact]
    public void EveryCellCarriesAReason()
    {
        var silent = RequestLifecycle.Table
            .Where(cell => string.IsNullOrWhiteSpace(cell.Why) || !cell.Why.EndsWith('.'))
            .Select(cell => Pair(cell.From, cell.To))
            .ToArray();

        Assert.Empty(silent);
    }

    /// <summary>
    /// The grid printed in <c>docs/lifecycle.md</c> is the table in the code, cell for cell. This
    /// and the test below it are the condition on #39 that the two cannot drift: the document is
    /// not generated at build time, and what stands in place of generation is a comparison that
    /// reds in both directions, so a cell changed in either place fails until it is changed in the
    /// other.
    /// <para>
    /// The grid is a five by five matrix whose rows are the state being left and whose columns are
    /// the state being entered. Only the padding inside the pipes is ignored, because the
    /// formatting gate owns that and this suite should not have a second opinion about it.
    /// </para>
    /// </summary>
    [Fact]
    public void TheDocumentedGridIsTheTableInTheCode()
    {
        var rows = MarkedSection("grid").Where(line => line.StartsWith('|')).ToArray();
        var header = SplitRow(rows[0]);

        // The row of dashes under the header is the table's own furniture. Everything else is a
        // row of the matrix, so a row that was deleted from the document fails the comparison at
        // the end rather than being skipped here.
        var body = rows.Skip(2).Select(SplitRow).ToArray();

        var documented = new List<string>();

        foreach (var row in body)
        {
            for (var column = 1; column < row.Length; column++)
            {
                documented.Add(string.Concat(row[0], " ", header[column], " ", row[column]));
            }
        }

        var expected = RequestLifecycle.Table
            .Select(cell => string.Concat(
                cell.From.ToString(),
                " ",
                cell.To.ToString(),
                " ",
                Verdict(cell)))
            .ToArray();

        Assert.Equal(expected, documented);
    }

    /// <summary>
    /// The reasons printed in <c>docs/lifecycle.md</c> are the reasons in the code, one bullet per
    /// cell, in the table's own order. The grid above says what the answer is and this says why,
    /// and the why is the half a reader actually arrives for.
    /// </summary>
    [Fact]
    public void TheDocumentedReasonsAreTheReasonsInTheCode()
    {
        var expected = RequestLifecycle.Table
            .Select(cell => string.Format(
                CultureInfo.InvariantCulture,
                "- **{0} to {1}**: {2}. {3}",
                cell.From,
                cell.To,
                Verdict(cell),
                cell.Why))
            .ToArray();

        var documented = MarkedSection("reasons")
            .Where(line => line.StartsWith("- **", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(expected, documented);
    }

    /// <summary>
    /// Makes a move through whichever of the two methods takes it. A decline needs a reason and so
    /// has a door of its own, and a test walking the whole table has to knock on the right one or it
    /// measures the wrong refusal.
    /// </summary>
    /// <param name="request">The request to move.</param>
    /// <param name="to">The state to move it into.</param>
    /// <param name="at">When the move happens.</param>
    /// <returns>The moved request.</returns>
    private static MediaRequest MoveThroughTheRightDoor(MediaRequest request, RequestState to, DateTimeOffset at)
        => to == RequestState.Declined
            ? RequestLifecycle.Decline(request, DeclineReason.NotWanted, note: null, at, declinedByUserId: null)
            : RequestLifecycle.Move(request, to, at, movedByUserId: null);

    private static string Verdict(RequestTransition cell) => cell.IsLegal ? "allowed" : "refused";

    private static string[] SplitRow(string row)
        => [.. row.Trim('|').Split('|').Select(cell => cell.Trim())];

    /// <summary>
    /// Reads the lines the document marks off for one section.
    /// </summary>
    /// <param name="name">The name in the marker comments.</param>
    /// <returns>The trimmed lines between the markers.</returns>
    private static string[] MarkedSection(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "docs", "lifecycle.md");
        Assert.True(File.Exists(path), FormattableString.Invariant($"{path} was not copied next to the suite."));

        var opening = FormattableString.Invariant($"<!-- {name} begins -->");
        var closing = FormattableString.Invariant($"<!-- {name} ends -->");
        var inside = false;
        var lines = new List<string>();

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();

            if (trimmed.Equals(opening, StringComparison.Ordinal))
            {
                inside = true;
                continue;
            }

            if (trimmed.Equals(closing, StringComparison.Ordinal))
            {
                inside = false;
                continue;
            }

            if (inside && trimmed.Length > 0)
            {
                lines.Add(trimmed);
            }
        }

        Assert.False(inside, FormattableString.Invariant($"The document opens {name} and never closes it."));
        Assert.NotEmpty(lines);
        return [.. lines];
    }

    private static string Pair(RequestState from, RequestState to)
        => string.Format(CultureInfo.InvariantCulture, "{0} to {1}", from, to);

    /// <summary>
    /// A request with only the fields that have no default, in the state a new one is created in.
    /// </summary>
    /// <returns>A newly asked-for request.</returns>
    private static MediaRequest ARequest()
    {
        var asked = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);

        return new MediaRequest
        {
            Id = new Guid("41d7c0b2-9e3a-4f58-b6d1-8c2f5a0e7b93"),
            RequestedByUserId = new Guid("7a4e2f18-6b0c-4d39-95e7-1f8a3c6d2b40"),
            RequestedAt = asked,
            StateChangedAt = asked,
            Kind = RequestedItemKind.Series,
            DisplayTitle = "Tinker Tailor Soldier Spy"
        };
    }
}

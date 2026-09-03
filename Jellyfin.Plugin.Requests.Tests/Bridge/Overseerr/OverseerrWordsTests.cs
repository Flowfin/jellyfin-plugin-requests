using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Bridge.Overseerr;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Bridge.Overseerr;

/// <summary>
/// The number-to-word step and the mapping table are two tables about one alphabet, and these legs
/// are what keep them one alphabet: every word the step produces is a row the table holds, and every
/// request-status row the table holds has a number behind it.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class OverseerrWordsTests
{
    /// <summary>
    /// Every word the step produces is a request-status row of the mapping table, so a number can
    /// never be turned into a word the table has never seen and then fall to the unseen-word rule by
    /// way of a typo here.
    /// </summary>
    [Fact]
    public void EveryWordTheStepProducesIsARowTheMappingTableHolds()
    {
        var words = BackendStates.Table
            .Where(row => row.Vocabulary == BackendVocabulary.RequestStatus)
            .Select(row => row.Reported)
            .ToArray();

        Assert.All(
            OverseerrWords.RequestStatuses,
            entry => Assert.Contains(entry.Value, words));
    }

    /// <summary>
    /// Every request-status row of the mapping table has a number here, so a row added to the table
    /// for this form cannot be one this adapter can never report.
    /// </summary>
    [Fact]
    public void EveryRequestStatusRowTheMappingTableHoldsHasANumberHere()
    {
        var produced = OverseerrWords.RequestStatuses.Select(entry => entry.Value).ToArray();

        Assert.All(
            BackendStates.Table.Where(row => row.Vocabulary == BackendVocabulary.RequestStatus),
            row => Assert.Contains(row.Reported, produced));
    }

    /// <summary>
    /// The numbers are the ones the form's own enumeration declares, in its order, which
    /// <c>docs/bridge.md</c> quotes with the command that fetched it: one to five, PENDING first.
    /// </summary>
    [Fact]
    public void TheNumbersAreTheOnesTheFormsOwnEnumerationDeclares()
    {
        Assert.Equal(
            ["1 PENDING", "2 APPROVED", "3 DECLINED", "4 FAILED", "5 COMPLETED"],
            OverseerrWords.RequestStatuses.Select(entry => FormattableString.Invariant($"{entry.Key} {entry.Value}")).ToArray());
    }

    /// <summary>
    /// A number the step does not know is answered as its own digits, and the mapping table then
    /// answers with no row for it in either vocabulary, which is the unseen-word rule firing rather
    /// than a guess.
    /// </summary>
    [Fact]
    public void ANumberNothingKnowsIsItsOwnDigitsAndMovesNothing()
    {
        var report = new BackendReport { Reported = OverseerrWords.RequestStatus(9) };

        Assert.Equal("9", report.Reported);
        Assert.Null(BackendStates.Lookup(BackendVocabulary.RequestStatus, report));
        Assert.Null(BackendStates.Lookup(BackendVocabulary.MediaStatus, report));
    }
}

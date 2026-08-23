using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests;

/// <summary>
/// A near-miss written to make the weekly mutation run break, so that the run can be watched going
/// red for the reason its own file names.
/// <para>
/// This file exists only on this branch and is never merged. `mutation.yaml` passes `--break-at 0`,
/// so no score can red it; what reds it is a run that did not happen, and the mutation runner starts
/// by running the suite unmutated. A suite that is red there stops the run before a single mutant is
/// produced, and the weekly report then says nothing while looking like it ran.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class AMutationRunThatBreaksTests
{
    /// <summary>
    /// Fails on purpose. Nothing about the plugin is asserted here.
    /// </summary>
    [Fact]
    public void ThisFailsSoTheMutationRunCannotStart() => Assert.Equal(1, 2);
}

using Xunit;

namespace Jellyfin.Plugin.Requests.Tests;

/// <summary>
/// A deliberately failing test, added to show the gated check goes red on it and removed in the
/// next commit. It exists so the claim that a failing test reddens the pull request is a recorded
/// run rather than an expectation.
/// </summary>
public class DeliberateFailureTests
{
    /// <summary>
    /// Fails on purpose.
    /// </summary>
    [Fact]
    public void ThisTestFailsOnPurpose()
    {
        Assert.True(false, "Deliberate failure: proving the gate reddens on a failing test.");
    }
}

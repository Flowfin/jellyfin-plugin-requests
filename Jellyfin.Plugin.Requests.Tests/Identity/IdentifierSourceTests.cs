using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Jellyfin.Plugin.Requests.Identity;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Identity;

/// <summary>
/// The two identifier sources: the one a server gets and the one the suite gets. The first has to
/// be unpredictable and the second has to be predictable, and both have to avoid
/// <see cref="Guid.Empty"/>, which code elsewhere is entitled to read as "no identifier".
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public class IdentifierSourceTests
{
    /// <summary>
    /// The real source hands out a distinct, non-empty identifier every time.
    /// </summary>
    [Fact]
    public void GuidSourceHandsOutDistinctIdentifiers()
    {
        var source = new GuidIdentifierSource();

        var issued = Enumerable.Range(0, 100).Select(_ => source.NewId()).ToList();

        Assert.DoesNotContain(Guid.Empty, issued);
        Assert.Equal(issued.Count, issued.Distinct().Count());
    }

    /// <summary>
    /// The double hands out the same series on every run, and a test can say which identifier the
    /// next request will carry before anything has created one. That is the difference between
    /// asserting which request is being looked at and reading back whatever the code produced.
    /// </summary>
    [Fact]
    public void SequentialSourceIsPredictableBeforeItIsAsked()
    {
        var source = new SequentialIdentifierSource();

        Assert.Equal(0, source.Issued);
        Assert.Equal(SequentialIdentifierSource.At(1), source.NewId());
        Assert.Equal(SequentialIdentifierSource.At(2), source.NewId());
        Assert.Equal(2, source.Issued);
    }

    /// <summary>
    /// The double's values are distinct and none of them is the empty identifier, which is the one
    /// value code treating an identifier as optional is allowed to reject.
    /// </summary>
    [Fact]
    public void SequentialSourceAvoidsTheEmptyIdentifier()
    {
        var source = new SequentialIdentifierSource();

        var issued = Enumerable.Range(0, 50).Select(_ => source.NewId()).ToList();

        Assert.DoesNotContain(Guid.Empty, issued);
        Assert.Equal(issued.Count, issued.Distinct().Count());
    }
}

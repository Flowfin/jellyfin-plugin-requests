using System;
using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.Requests.Bridge;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Bridge;

/// <summary>
/// What a bridge is allowed to hand back. The two values here are what makes the two systems
/// reconcilable after the moment of the handover, so a half of one that says nothing is refused
/// where it is built rather than at whichever reader remembers to check.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public class BackendReferenceTests
{
    /// <summary>
    /// A reference names a service and what that service called the request, and neither may be
    /// empty or blank. Whitespace is refused with the empty string rather than trimmed away,
    /// because an adapter handing over a space has a defect and quietly storing it hides the day it
    /// started.
    /// </summary>
    /// <param name="service">The service half.</param>
    /// <param name="id">The identifier half.</param>
    [Theory]
    [InlineData("", "418")]
    [InlineData("   ", "418")]
    [InlineData("overseerr", "")]
    [InlineData("overseerr", "\t")]
    public void AReferenceWithAnEmptyHalfIsRefused(string service, string id)
        => Assert.Throws<ArgumentException>(() => new BackendReference { Service = service, Id = id });

    /// <summary>
    /// What a service reported is kept exactly as it arrived. Nothing here parses it, and the shape
    /// of an identifier is the service's business.
    /// </summary>
    [Fact]
    public void AReferenceKeepsBothHalvesAsTheyArrived()
    {
        var reference = new BackendReference { Service = "overseerr", Id = "media/418" };

        Assert.Equal("overseerr", reference.Service, StringComparer.Ordinal);
        Assert.Equal("media/418", reference.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// A report carries the service's own word. An empty one is refused, because nothing known is
    /// already expressed by there being no report, and two ways to say one thing is the pair that
    /// leaves half the readers checking for one of them.
    /// </summary>
    /// <param name="reported">The word as it arrived.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AReportWithNothingInItIsRefused(string reported)
        => Assert.Throws<ArgumentException>(() => new BackendReport { Reported = reported });

    /// <summary>
    /// The word is carried unmapped. Turning it into one of this plugin's states is #81, and an
    /// adapter that mapped on the way through would put that decision in every adapter.
    /// </summary>
    [Fact]
    public void AReportKeepsTheServicesOwnWord()
        => Assert.Equal(
            "PARTIALLY_AVAILABLE",
            new BackendReport { Reported = "PARTIALLY_AVAILABLE" }.Reported,
            StringComparer.Ordinal);
}

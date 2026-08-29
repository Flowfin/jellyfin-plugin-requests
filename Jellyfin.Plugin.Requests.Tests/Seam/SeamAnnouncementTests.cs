using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Seam;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Seam;

/// <summary>
/// #117's fourth condition: that a server with no sibling installed and a server whose sibling is
/// asking for something this side does not answer to do not look the same to the operator.
/// <para>
/// Under a compile-time contract the second of those is a build failure on the other board. Under
/// the shape #117 took on 2026-08-28 it is a lookup that returns nothing, which is exactly what the
/// first one returns, and the container says nothing about the difference because from where it
/// stands there is none. What separates them is this line.
/// </para>
/// <para>
/// What is asserted is that the two states produce different sentences and that the sentence carries
/// the names somebody would compare. Nothing here asserts that a mismatch is DETECTED, because it is
/// not: this side cannot see what another plugin asked the container for, and a test claiming
/// otherwise would be proving a sentence rather than a capability.
/// </para>
/// </summary>
public class SeamAnnouncementTests
{
    private static readonly string[] AServerWithNoSibling =
    [
        "System.Private.CoreLib",
        "Jellyfin.Server",
        "MediaBrowser.Controller",
        "Jellyfin.Plugin.Requests"
    ];

    private static readonly string[] AServerWithTheSibling =
    [
        "System.Private.CoreLib",
        "Jellyfin.Server",
        "MediaBrowser.Controller",
        "Jellyfin.Plugin.Requests",
        "Jellyfin.Plugin.Discover"
    ];

    /// <summary>
    /// The two states are two sentences. This is the condition itself, and it is asserted before
    /// anything about what either sentence says, because a pair of lines that happened to differ in
    /// a detail nobody reads would satisfy a weaker test and not the condition.
    /// </summary>
    [Fact]
    public void NoSiblingAndASiblingDoNotReadTheSame()
        => Assert.NotEqual(
            SeamAnnouncement.Compose(AServerWithNoSibling),
            SeamAnnouncement.Compose(AServerWithTheSibling),
            StringComparer.Ordinal);

    /// <summary>
    /// The ordinary server, which is most of them. It says there is nothing to expect, so an
    /// operator reading it is not left wondering whether something failed.
    /// </summary>
    [Fact]
    public void AServerWithNoSiblingIsToldThereIsNothingToExpect()
    {
        string said = SeamAnnouncement.Compose(AServerWithNoSibling);

        Assert.Contains("No other Jellyfin plugin is loaded on this server", said, StringComparison.Ordinal);
        Assert.DoesNotContain("Jellyfin.Plugin.Discover", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// The server this exists for. The other plugin is named, and the operator is told that a name
    /// that does not match is answered with nothing rather than with an error, which is the fact
    /// that makes the silence readable.
    /// </summary>
    [Fact]
    public void AServerWithASiblingIsToldWhichOneAndWhatToCompare()
    {
        string said = SeamAnnouncement.Compose(AServerWithTheSibling);

        Assert.Contains("Jellyfin.Plugin.Discover", said, StringComparison.Ordinal);
        Assert.Contains("a name that does not match is answered with nothing", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both sentences carry the four names and the version, because the line is only worth reading
    /// if what it prints is the thing to compare. They are taken from <see cref="SeamSurface"/>,
    /// which reads them off the types, so a rename moves the line with it.
    /// </summary>
    /// <param name="siblingLoaded">Whether the process has another plugin in it.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EitherWayTheNamesASiblingHasToGetRightAreInIt(bool siblingLoaded)
    {
        string said = SeamAnnouncement.Compose(siblingLoaded ? AServerWithTheSibling : AServerWithNoSibling);

        Assert.Contains(SeamSurface.TypeName, said, StringComparison.Ordinal);
        Assert.Contains(SeamSurface.AssemblyName, said, StringComparison.Ordinal);
        Assert.Contains(SeamSurface.MemberName, said, StringComparison.Ordinal);
        Assert.Contains(SeamSurface.WantTypeName, said, StringComparison.Ordinal);
        Assert.Contains(
            SeamSurface.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            said,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// This plugin's own assemblies are not siblings of it. The test project loads
    /// <c>Jellyfin.Plugin.Requests.Tests</c> beside the plugin, so a rule matching the plugin prefix
    /// alone would report the suite as a sibling on every run and would report a second assembly of
    /// this plugin's own as one on a server.
    /// </summary>
    [Fact]
    public void ThisPluginsOwnAssembliesAreNotSiblings()
    {
        string said = SeamAnnouncement.Compose(
            ["Jellyfin.Plugin.Requests", "Jellyfin.Plugin.Requests.Tests"]);

        Assert.Contains("No other Jellyfin plugin is loaded on this server", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// The line reaches the log the operator is reading, rather than being a sentence a test can
    /// compose and a server never writes.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheServerWritesItAtStartup()
    {
        var logger = new RecordingLogger();
        var announcement = new SeamAnnouncement(logger, () => AServerWithTheSibling);

        await announcement.StartAsync(CancellationToken.None);
        await announcement.StopAsync(CancellationToken.None);

        Assert.Contains(
            logger.Lines,
            line => line.Message.Contains(SeamSurface.TypeName, StringComparison.Ordinal)
                && line.Message.Contains("Jellyfin.Plugin.Discover", StringComparison.Ordinal));
    }
}

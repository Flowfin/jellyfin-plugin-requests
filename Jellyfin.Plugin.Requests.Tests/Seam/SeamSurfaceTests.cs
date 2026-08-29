using System;
using Jellyfin.Plugin.Requests.Seam;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Seam;

/// <summary>
/// The names a sibling gets no compiler help with.
/// <para>
/// #117 took the handover by name through reflection on 2026-08-28, after a run on both claimed
/// lines measured the two package options unavailable. What that buys is immunity to two types of
/// one name; what it costs is that the assembly, the type, the member and the want are strings on
/// the other side of the seam. Renaming any of them here compiles, passes every other test, ships,
/// and is met by an operator as a handover that silently stops arriving.
/// </para>
/// <para>
/// So the names are written down once, here, as literals, and compared against what the types
/// actually say. <see cref="SeamSurface"/> derives its side of that comparison and holds no literal
/// of its own, which is what makes this a pin rather than two copies agreeing with each other.
/// Changing a name means changing this file, and the commit that changes it is where the seam
/// version is raised and the sibling's board is told.
/// </para>
/// <para>
/// This is the half that reds without a server. The other half is the seam probe, which asks the
/// same questions of a running server from inside a second plugin, and neither stands in for the
/// other: this one cannot see a load context and that one cannot run on a machine with no container.
/// </para>
/// </summary>
public class SeamSurfaceTests
{
    /// <summary>
    /// The assembly a sibling resolves the seam out of. It is the plugin's own, which is the whole
    /// of the third option: nothing separate ships, so there is nothing to be a second copy of.
    /// </summary>
    [Fact]
    public void TheAssemblyIsNamedWhatTheSiblingIsToldItIs()
        => Assert.Equal("Jellyfin.Plugin.Requests", SeamSurface.AssemblyName, StringComparer.Ordinal);

    /// <summary>
    /// The type a sibling asks the server's container for.
    /// </summary>
    [Fact]
    public void TheSeamTypeIsNamedWhatTheSiblingIsToldItIs()
        => Assert.Equal("Jellyfin.Plugin.Requests.Seam.IWantHandover", SeamSurface.TypeName, StringComparer.Ordinal);

    /// <summary>
    /// The want a sibling has to build to make the call. It is named separately from its properties
    /// because the two fail at different moments: a wrong type name fails before anything is set.
    /// </summary>
    [Fact]
    public void TheWantIsNamedWhatTheSiblingIsToldItIs()
        => Assert.Equal("Jellyfin.Plugin.Requests.Seam.HandedOverWant", SeamSurface.WantTypeName, StringComparer.Ordinal);

    /// <summary>
    /// The seam version, which is what a sibling puts in the want and what this side checks it
    /// against. Raising it is the deliberate act that says the surface below moved.
    /// </summary>
    [Fact]
    public void TheSeamVersionIsTheOneWrittenDown()
        => Assert.Equal(1, SeamSurface.Version);

    /// <summary>
    /// The one member, with its parameters and its return type rather than only its name. A member
    /// that kept its name and changed its shape is met at the same moment and in the same silence as
    /// one that was renamed, and under reflection it is met one step later, after the sibling has
    /// already found the type and been handed the implementation.
    /// <para>
    /// Joined rather than compared as two sequences, because a collection failure prints the
    /// difference with the middle elided and the whole surface is what somebody repairing this needs
    /// to read.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSeamDeclaresExactlyTheMemberWrittenDown()
        => Assert.Equal(
            "System.Threading.Tasks.Task`1[System.Boolean] AcceptAsync(Jellyfin.Plugin.Requests.Seam.HandedOverWant, System.Threading.CancellationToken)",
            string.Join(" | ", SeamSurface.Members),
            StringComparer.Ordinal);

    /// <summary>
    /// Every property of the want, by type and name. A sibling with no compile-time reference sets
    /// each of these by name, so an addition, a removal or a rename is a runtime failure on a
    /// server and nothing before it.
    /// <para>
    /// This is a set of names rather than a copy of the contract. What each field MEANS is fixed on
    /// the sibling's board, in the contract issue <c>docs/seam.md</c> points at, and neither this
    /// file nor that document restates it: #11's second condition is what that rule comes from.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWantCarriesExactlyThePropertiesWrittenDown()
        => Assert.Equal(
            string.Join(
                " | ",
                "Jellyfin.Plugin.Requests.Model.RequestedItemKind Kind",
                "System.Collections.Generic.IReadOnlyDictionary`2[System.String,System.String] ProviderIds",
                "System.Guid RequestedByUserId",
                "System.Guid WantId",
                "System.Int32 ContractVersion",
                "System.Nullable`1[System.Int32] Year",
                "System.String Title"),
            string.Join(" | ", SeamSurface.WantProperties),
            StringComparer.Ordinal);
}

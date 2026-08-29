using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Jellyfin.Plugin.Requests.Seam;

/// <summary>
/// The names a sibling has to get right, derived from the types rather than typed out.
/// <para>
/// <b>This is what #117's third option costs, held in one place.</b> The handover is taken by name
/// through reflection, so nothing a sibling does is checked by a compiler: it names an assembly, a
/// type, a member and a want type as strings, and a rename on this side turns every one of those
/// into a lookup that finds nothing at runtime, on a server, silently. <c>docs/seam.md</c> is the
/// page both boards read those names off, and this class is the tree's copy of the same four
/// questions answered by the types themselves.
/// </para>
/// <para>
/// <b>Nothing here is a literal, and that is the whole design.</b> A string typed into this file
/// would go stale with a rename exactly as quietly as the sibling's copy does. Every member below
/// is read off a type, so a rename moves it; what refuses the rename is
/// <c>SeamSurfaceTests</c>, which holds the literals and compares them against these, and the seam
/// probe, which asks the same questions of a running server from another plugin's load context.
/// </para>
/// <para>
/// <b>It is not a second contract.</b> What crosses the seam is fixed on the sibling's board, in the
/// contract issue <c>docs/seam.md</c> points at. This says what this side answers to, which is a
/// different question and is this board's to answer.
/// </para>
/// </summary>
public static class SeamSurface
{
    /// <summary>
    /// Gets the assembly a sibling resolves the seam out of.
    /// </summary>
    public static string AssemblyName => typeof(IWantHandover).Assembly.GetName().Name ?? string.Empty;

    /// <summary>
    /// Gets the full name of the type a sibling asks the server's container for.
    /// </summary>
    public static string TypeName => typeof(IWantHandover).FullName ?? string.Empty;

    /// <summary>
    /// Gets the name of the one member a sibling calls.
    /// </summary>
    public static string MemberName => nameof(IWantHandover.AcceptAsync);

    /// <summary>
    /// Gets the full name of the type a sibling has to build to make that call.
    /// </summary>
    public static string WantTypeName => typeof(HandedOverWant).FullName ?? string.Empty;

    /// <summary>
    /// Gets the version of the contract this side answers to.
    /// <para>
    /// It is the same number a want carries and is checked against, read off the implementation
    /// rather than repeated, so the version an operator is told is the version a handover is judged
    /// by and there is no second place for one of them to move.
    /// </para>
    /// </summary>
    public static int Version => WantHandover.KnownContractVersion;

    /// <summary>
    /// Gets every member the seam type declares, rendered as the runtime renders it.
    /// <para>
    /// The parameter types and the return type are in it rather than only the name, because a member
    /// that kept its name and changed its shape is the same silent failure as one that was renamed,
    /// and under reflection it fails one step later rather than one step earlier.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Members
        => [.. typeof(IWantHandover)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(member => member.ToString() ?? string.Empty)
            .OrderBy(rendered => rendered, StringComparer.Ordinal)];

    /// <summary>
    /// Gets every property of the want, rendered as a type and a name.
    /// <para>
    /// A sibling with no compile-time reference sets these by name, so they are as much of the
    /// runtime surface as the member is. What each one MEANS is the sibling's contract and is not
    /// restated here or in <c>docs/seam.md</c>; what is here is the set of names a lookup can miss.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> WantProperties
        => [.. typeof(HandedOverWant)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1}",
                property.PropertyType.ToString(),
                property.Name))
            .OrderBy(rendered => rendered, StringComparer.Ordinal)];
}

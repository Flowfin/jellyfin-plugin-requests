using System;
using System.Globalization;
using System.IO;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// The one directory a test host process owns, and the only place in the suite that names the
/// machine's temporary root. Every double that needs somewhere to write takes a subdirectory of it.
/// <para>
/// Without this each double made its own directory beside everything else in the machine's
/// temporary root, so a run left as many siblings as it constructed doubles and there was no single
/// directory an assertion about the whole run could be made about. There is one per test host
/// process rather than one per <c>dotnet test</c>, because the suite runs a process per target
/// framework and the two do not share memory.
/// </para>
/// </summary>
internal static class TestRunDirectory
{
    private static readonly string RootPath = Path.Combine(
        Path.GetTempPath(),
        string.Create(CultureInfo.InvariantCulture, $"jellyfin-plugin-requests-tests-{Guid.NewGuid():N}"));

    /// <summary>
    /// Gets the directory this test host process owns. Created on first use so a run that
    /// constructs no double leaves nothing behind.
    /// </summary>
    public static string Root
    {
        get
        {
            Directory.CreateDirectory(RootPath);
            return RootPath;
        }
    }

    /// <summary>
    /// Creates a fresh subdirectory of <see cref="Root"/>.
    /// </summary>
    /// <returns>The full path of the new subdirectory.</returns>
    public static string CreateSubdirectory()
    {
        var path = Path.Combine(
            Root,
            string.Create(CultureInfo.InvariantCulture, $"{Guid.NewGuid():N}"));

        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Removes a subdirectory and everything under it, and then removes <see cref="Root"/> itself
    /// if nothing else is left in it. The last double disposed is what takes the run's directory
    /// with it; while others still hold subdirectories the delete is refused and the directory
    /// stays, which is why the failure is swallowed rather than reported.
    /// </summary>
    /// <param name="subdirectory">A path returned by <see cref="CreateSubdirectory"/>.</param>
    public static void Remove(string subdirectory)
    {
        if (Directory.Exists(subdirectory))
        {
            Directory.Delete(subdirectory, true);
        }

        try
        {
            Directory.Delete(RootPath);
        }
        catch (IOException)
        {
            // Something else in this process still has a subdirectory here.
        }
        catch (UnauthorizedAccessException)
        {
            // Same case, reported differently by the platform.
        }
    }
}

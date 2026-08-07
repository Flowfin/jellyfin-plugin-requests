using System;
using System.Collections.Generic;
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
/// <para>
/// The suite runs test classes in parallel, so taking a subdirectory and giving one back are held
/// apart by a lock and the live ones are counted rather than looked for on the disk. Asking the
/// disk whether the run's directory is empty answers about the moment it was asked: a double that
/// had already decided to create a subdirectory, and had not yet created it, is invisible to that
/// question, and the answer was used to delete the directory underneath it.
/// </para>
/// </summary>
internal static class TestRunDirectory
{
    private static readonly string RootPath = Path.Combine(
        Path.GetTempPath(),
        string.Create(CultureInfo.InvariantCulture, $"jellyfin-plugin-requests-tests-{Guid.NewGuid():N}"));

    /// <summary>
    /// Held across every create and every remove, so no double can be part way through taking a
    /// subdirectory while another is deciding whether the run's directory can go.
    /// </summary>
    private static readonly object Gate = new object();

    /// <summary>
    /// The subdirectories handed out and not yet given back. The count, rather than the state of
    /// the disk, is what decides whether the run's directory is still wanted.
    /// </summary>
    private static readonly HashSet<string> Live = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the directory this test host process owns. Created on first use so a run that
    /// constructs no double leaves nothing behind.
    /// </summary>
    public static string Root
    {
        get
        {
            lock (Gate)
            {
                Directory.CreateDirectory(RootPath);
                return RootPath;
            }
        }
    }

    /// <summary>
    /// Creates a fresh subdirectory of <see cref="Root"/>.
    /// </summary>
    /// <returns>The full path of the new subdirectory.</returns>
    public static string CreateSubdirectory()
    {
        var path = Path.Combine(
            RootPath,
            string.Create(CultureInfo.InvariantCulture, $"{Guid.NewGuid():N}"));

        lock (Gate)
        {
            Directory.CreateDirectory(RootPath);
            Directory.CreateDirectory(path);
            Live.Add(path);
        }

        return path;
    }

    /// <summary>
    /// Removes a subdirectory and everything under it, and then removes <see cref="Root"/> itself
    /// if it was the last one out. The last double disposed is what takes the run's directory with
    /// it. The delete is still allowed to fail without saying so, because something outside this
    /// process can hold a handle on the directory and that is not a thing the suite can repair.
    /// </summary>
    /// <param name="subdirectory">A path returned by <see cref="CreateSubdirectory"/>.</param>
    public static void Remove(string subdirectory)
    {
        lock (Gate)
        {
            if (Directory.Exists(subdirectory))
            {
                Directory.Delete(subdirectory, true);
            }

            Live.Remove(subdirectory);

            if (Live.Count > 0)
            {
                return;
            }

            try
            {
                Directory.Delete(RootPath);
            }
            catch (IOException)
            {
                // Something outside this process is holding the directory open.
            }
            catch (UnauthorizedAccessException)
            {
                // Same case, reported differently by the platform.
            }
        }
    }
}

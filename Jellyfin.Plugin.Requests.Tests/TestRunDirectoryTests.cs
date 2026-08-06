using System;
using System.IO;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests;

/// <summary>
/// The directory a test host process owns, and the doubles that take subdirectories of it. The
/// headless rule says a run writes only under a temporary directory given to it, and a run that
/// makes a fresh directory per double has no such directory: it has as many as it constructed,
/// each a sibling of everything else in the machine's temporary root. These tests are what refuses
/// the return of that shape.
/// </summary>
public class TestRunDirectoryTests
{
    /// <summary>
    /// Every paths double is rooted under the run's own directory, and two of them do not collide.
    /// A double that reached for the machine's temporary root directly would pass every per-instance
    /// assertion in the suite and fail here.
    /// </summary>
    [Fact]
    public void EveryDoubleIsRootedUnderTheRunDirectory()
    {
        var runDirectory = Path.GetFullPath(TestRunDirectory.Root);

        using var first = new FakeApplicationPaths();
        using var second = new FakeApplicationPaths();

        var firstRoot = Path.GetFullPath(first.ProgramDataPath);
        var secondRoot = Path.GetFullPath(second.ProgramDataPath);

        Assert.StartsWith(runDirectory, firstRoot, StringComparison.Ordinal);
        Assert.StartsWith(runDirectory, secondRoot, StringComparison.Ordinal);
        Assert.NotEqual(firstRoot, secondRoot, StringComparer.Ordinal);
    }

    /// <summary>
    /// The run's directory sits directly under the machine's temporary root rather than somewhere a
    /// test chose. This is the half that says where the run writes, and it is what a later check
    /// over the whole run would be made about.
    /// </summary>
    [Fact]
    public void TheRunDirectoryIsDirectlyUnderTheMachineTemporaryRoot()
    {
        var runDirectory = Path.GetFullPath(TestRunDirectory.Root);
        var parent = Path.GetFullPath(Path.GetDirectoryName(runDirectory)!);

        Assert.Equal(
            Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),
            parent.TrimEnd(Path.DirectorySeparatorChar),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Disposing a double removes its own subdirectory and leaves the run's directory for whatever
    /// else is still using it. The suite runs test classes in parallel, so a double disposing must
    /// not take the directory another one is writing into.
    /// </summary>
    [Fact]
    public void DisposingADoubleLeavesTheRunDirectoryForTheOthers()
    {
        using var held = new FakeApplicationPaths();

        string disposedRoot;
        using (var temporary = new FakeApplicationPaths())
        {
            disposedRoot = temporary.ProgramDataPath;
            Assert.True(Directory.Exists(disposedRoot));
        }

        Assert.False(Directory.Exists(disposedRoot));
        Assert.True(Directory.Exists(held.ProgramDataPath));
        Assert.True(Directory.Exists(TestRunDirectory.Root));
    }
}

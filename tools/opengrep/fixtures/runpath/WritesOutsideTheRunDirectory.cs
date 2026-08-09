// Fixture for suite-writes-only-under-the-run-directory. This file is in no
// project and is never compiled; it exists so the rule can be watched refusing
// the mistake it names.
//
// The near-miss is a test that wants somewhere to put a file and asks the
// machine for it, which is what every example on the subject does. It never goes
// through a double, so the assertions in TestRunDirectoryTests say nothing about
// it, it passes, and it leaves a file beside everything else the same user's
// processes wrote. On a shared machine that is somebody else's disk, and the
// suite is the only thing that knows the file is rubbish.

namespace Jellyfin.Plugin.Requests.Tests.Fixtures;

internal sealed class WritesOutsideTheRunDirectory
{
    // Legal neighbour, left here on purpose: this is how a test gets somewhere to
    // write, and the rule has to stay quiet on it.
    public static string SomewhereToWrite()
    {
        return TestRunDirectory.CreateSubdirectory();
    }

    // The regression, in the spellings that reach a shared location.
    public static string AskTheMachine()
    {
        Directory.SetCurrentDirectory(Environment.CurrentDirectory);
        var working = Directory.GetCurrentDirectory();
        var scratch = Path.GetTempPath();
        var scratchFile = Path.GetTempFileName();
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(scratch, scratchFile, documents, working);
    }
}

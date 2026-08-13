using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Configuration;

/// <summary>
/// What an uninstall takes with it.
/// <para>
/// The decision is that this plugin removes nothing it wrote, and the argument is in
/// <see cref="Jellyfin.Plugin.Requests.Plugin.OnUninstalling"/>. A decision recorded only in prose
/// is one the next person deletes a queue against by accident, so it is held here instead: the two
/// files are written, the server's call is made, and both are compared byte for byte afterwards.
/// </para>
/// <para>
/// What this cannot say is what the server itself removes. Nothing here runs one, and the
/// directories underneath are the host's; what is measured is this plugin's own behaviour when it
/// is told it is going away.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class UninstallTests
{
    /// <summary>
    /// An uninstall leaves the queue and the settings exactly as they were.
    /// <para>
    /// The bytes are compared rather than the existence of the files, because a file emptied is a
    /// queue lost as surely as a file deleted and it passes an existence check.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUninstallRemovesNothingThisPluginWrote()
    {
        using var host = new PluginHost();

        var queue = Path.Combine(host.Plugin.DataFolderPath, "requests.json");
        var settings = host.Plugin.ConfigurationFilePath;

        Directory.CreateDirectory(host.Plugin.DataFolderPath);
        File.WriteAllText(queue, "{\"Requests\":[]}", Encoding.UTF8);
        File.WriteAllText(settings, "<PluginConfiguration />", Encoding.UTF8);

        var queueBefore = File.ReadAllBytes(queue);
        var settingsBefore = File.ReadAllBytes(settings);

        host.Plugin.OnUninstalling();

        Assert.True(File.Exists(queue), "The queue is gone after an uninstall, and nobody asked for that.");
        Assert.True(File.Exists(settings), "The settings are gone after an uninstall, and nobody asked for that.");
        Assert.True(queueBefore.SequenceEqual(File.ReadAllBytes(queue)), "The queue was rewritten by an uninstall.");
        Assert.True(settingsBefore.SequenceEqual(File.ReadAllBytes(settings)), "The settings were rewritten by an uninstall.");
    }

    /// <summary>
    /// The command an operator is given removes what is left and nothing else.
    /// <para>
    /// The two paths in that command are read off the host rather than out of the document, so a
    /// data folder or a configuration file that moves reds this instead of leaving an operator
    /// running a command that deletes nothing, or worse, something else. The document is where the
    /// command is written for a person; this is what keeps it true.
    /// </para>
    /// </summary>
    [Fact]
    public void WhatTheDocumentedCommandRemovesIsWhatIsActuallyLeft()
    {
        using var host = new PluginHost();

        var written = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "configuration.md"), Encoding.UTF8);
        var removal = written[written.IndexOf("## What an uninstall leaves behind", StringComparison.Ordinal)..];

        var queue = Path.GetRelativePath(host.ApplicationPaths.ProgramDataPath, host.Plugin.DataFolderPath)
            .Replace(Path.DirectorySeparatorChar, '/');

        var settings = Path.GetRelativePath(host.ApplicationPaths.ProgramDataPath, host.Plugin.ConfigurationFilePath)
            .Replace(Path.DirectorySeparatorChar, '/');

        Assert.Contains(queue, removal, StringComparison.Ordinal);
        Assert.Contains(settings, removal, StringComparison.Ordinal);
    }
}

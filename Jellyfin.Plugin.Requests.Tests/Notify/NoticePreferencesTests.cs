using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Notify;

/// <summary>
/// What this plugin keeps about who does not want to be told, held to the three properties the
/// switch rests on: the default is on, a refusal survives a restart, and a file that cannot be read
/// is refused rather than read as an empty set.
/// <para>
/// The last of those is the one worth being careful about, and it is why this file exists at all
/// rather than only a test of the endpoint. An empty set here means everybody wants to be told, so a
/// store that answered empty on a file it could not parse would turn a disk fault into every person
/// on the server having their setting silently reversed.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class NoticePreferencesTests : IDisposable
{
    private static readonly Guid Asker = new Guid("f1000000-0000-0000-0000-000000000001");
    private static readonly Guid Somebody = new Guid("f1000000-0000-0000-0000-000000000002");

    private readonly string _directory = TestRunDirectory.CreateSubdirectory();

    /// <summary>
    /// A person who has never touched this is told, and an install nobody has touched holds no file
    /// at all.
    /// <para>
    /// The second half is what makes the third condition of #287 a property of the shape: the
    /// default is the absence of a value rather than a value somebody has to remember to write, so
    /// an install that upgrades into this behaves exactly as it did before it existed.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ANobodyHasTouchedIsToldAndNothingIsOnTheDisk()
    {
        using var preferences = new FileNoticePreferences(_directory);

        Assert.True(await preferences.TellsThemAsync(Asker, CancellationToken.None).ConfigureAwait(true));
        Assert.False(File.Exists(Path.Combine(_directory, FileNoticePreferences.FileName)));
    }

    /// <summary>
    /// Turning it off survives a restart, and turning it back on survives one too.
    /// <para>
    /// Read through a second instance over the same directory rather than through the one that
    /// wrote it, because the one that wrote it holds the set in memory and would answer from there:
    /// a write that never reached the disk would pass a test made against the writer.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task WhatSomebodySetIsWhatANewInstanceReads()
    {
        using (var setting = new FileNoticePreferences(_directory))
        {
            Assert.False(await setting.SetAsync(Asker, tellsThem: false, CancellationToken.None).ConfigureAwait(true));
        }

        using (var afterARestart = new FileNoticePreferences(_directory))
        {
            Assert.False(await afterARestart.TellsThemAsync(Asker, CancellationToken.None).ConfigureAwait(true));
            Assert.True(await afterARestart.TellsThemAsync(Somebody, CancellationToken.None).ConfigureAwait(true));
            Assert.True(await afterARestart.SetAsync(Asker, tellsThem: true, CancellationToken.None).ConfigureAwait(true));
        }

        using var afterAnother = new FileNoticePreferences(_directory);

        Assert.True(await afterAnother.TellsThemAsync(Asker, CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>
    /// One person's setting is not anybody else's, on the disk as well as in memory.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task OnePersonsRefusalLeavesEverybodyElseBeingTold()
    {
        using (var setting = new FileNoticePreferences(_directory))
        {
            await setting.SetAsync(Asker, tellsThem: false, CancellationToken.None).ConfigureAwait(true);
        }

        using var afterARestart = new FileNoticePreferences(_directory);

        Assert.False(await afterARestart.TellsThemAsync(Asker, CancellationToken.None).ConfigureAwait(true));
        Assert.True(await afterARestart.TellsThemAsync(Somebody, CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>
    /// A file that is not the document this keeps is refused, in both directions.
    /// <para>
    /// Refused rather than answered as an empty set, because an empty set here reads as everybody
    /// wanting to be told: a person who asked not to be told would start being told again, and
    /// nothing afterwards could tell that from a person who never asked.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AFileThisCannotReadIsRefusedRatherThanReadAsNobody()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, FileNoticePreferences.FileName),
            "{ this is not the document",
            Encoding.UTF8,
            CancellationToken.None).ConfigureAwait(true);

        using var preferences = new FileNoticePreferences(_directory);

        await Assert.ThrowsAsync<NoticePreferencesException>(
            () => preferences.TellsThemAsync(Asker, CancellationToken.None)).ConfigureAwait(true);

        await Assert.ThrowsAsync<NoticePreferencesException>(
            () => preferences.SetAsync(Asker, tellsThem: false, CancellationToken.None)).ConfigureAwait(true);
    }

    /// <summary>
    /// A file written by a later version of this plugin is refused and is left exactly as it was.
    /// <para>
    /// The same rule the request store keeps, for the same reason: a downgraded server cannot know
    /// what a field it has never heard of means, and writing its guess back replaces the only copy.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AFileFromALaterVersionIsRefusedAndLeftAlone()
    {
        var path = Path.Combine(_directory, FileNoticePreferences.FileName);
        var written = "{\"Version\":" + (FileNoticePreferences.OnDiskVersion + 1) + ",\"Quiet\":[\"" + Asker + "\"]}";

        await File.WriteAllTextAsync(path, written, Encoding.UTF8, CancellationToken.None).ConfigureAwait(true);

        using var preferences = new FileNoticePreferences(_directory);

        await Assert.ThrowsAsync<NoticePreferencesException>(
            () => preferences.TellsThemAsync(Asker, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(
            written,
            await File.ReadAllTextAsync(path, Encoding.UTF8, CancellationToken.None).ConfigureAwait(true),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// An entry naming nobody is refused.
    /// <para>
    /// The empty identifier is what a caller with no session would be recorded as, and a setting
    /// kept against it silences whoever that identifier is next taken for. It cannot be written
    /// through this plugin, which is what the endpoint's refusal of a call naming no person is for,
    /// so what this covers is a file that arrived some other way.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnEntryNamingNobodyIsRefused()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, FileNoticePreferences.FileName),
            "{\"Version\":" + FileNoticePreferences.OnDiskVersion + ",\"Quiet\":[\"" + Guid.Empty + "\"]}",
            Encoding.UTF8,
            CancellationToken.None).ConfigureAwait(true);

        using var preferences = new FileNoticePreferences(_directory);

        await Assert.ThrowsAsync<NoticePreferencesException>(
            () => preferences.TellsThemAsync(Somebody, CancellationToken.None)).ConfigureAwait(true);
    }

    /// <summary>
    /// A setting that is already what it is being set to writes nothing.
    /// <para>
    /// A person opening their page and leaving the control alone should not touch a file, and a
    /// write that replaces a file with the same bytes is a write that can fail for a reason nobody
    /// asked for. Measured by the file not appearing at all, which is the strongest form available
    /// here and is the shipped default's own case.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SettingItToWhatItAlreadyIsWritesNothing()
    {
        using var preferences = new FileNoticePreferences(_directory);

        Assert.True(await preferences.SetAsync(Asker, tellsThem: true, CancellationToken.None).ConfigureAwait(true));
        Assert.False(File.Exists(Path.Combine(_directory, FileNoticePreferences.FileName)));
    }

    /// <summary>
    /// Nothing is left on the disk when the test is done with it.
    /// </summary>
    public void Dispose() => TestRunDirectory.Remove(_directory);
}

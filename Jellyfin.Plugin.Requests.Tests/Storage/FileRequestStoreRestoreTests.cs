using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Storage;

/// <summary>
/// A backup taken off one machine and opened on another. This is a different question from the one
/// <see cref="FileRequestStoreVersionTests"/> asks, and the difference is the directory: those legs
/// read a file where it already sits, and an operator restoring a backup puts bytes into a data
/// folder that has never held a request and starts a plugin over them.
/// <para>
/// What has to hold for a backup to be worth taking. The file has to be somewhere a backup of the
/// server's data directory reaches, which is the first leg. Everything the store held when the bytes
/// were captured has to come back, and the restored directory has to be a working store afterwards
/// rather than a readable one. And the case an operator actually meets, restoring onto a plugin that
/// is not the version the backup came from, has to end in one of two places: read, or refused whole.
/// Never half read.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class FileRequestStoreRestoreTests : IDisposable
{
    /// <summary>
    /// The file of bytes the store wrote before it carried a version, which is the nearest thing
    /// this tree has to a capture from an older plugin. Where it came from, and why no shipped
    /// version's output exists to use instead, is in <c>docs/storage.md</c>.
    /// </summary>
    private const string UnversionedFixture = "Storage/Fixtures/unversioned-written-at-592e517.json";

    private readonly List<FileRequestStore> _stores = [];
    private readonly List<string> _directories = [];

    /// <summary>
    /// Removes every store this test made and the directory each one wrote in.
    /// </summary>
    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        foreach (var directory in _directories)
        {
            TestRunDirectory.Remove(directory);
        }
    }

    /// <summary>
    /// The store file sits inside the plugin's own data folder, and that folder sits under the
    /// directory the server keeps its own data in. This is the whole of what makes a backup of the
    /// server's data directory a backup of the queue, and it is asserted rather than described
    /// because the sentence in <c>docs/storage.md</c> is what an operator plans a backup from.
    /// </summary>
    [Fact]
    public void TheStoreFileIsInsideThePluginsOwnDataFolderUnderTheServersDataDirectory()
    {
        using var host = new PluginHost();

        var dataFolder = host.Plugin.DataFolderPath;
        var pluginsPath = host.ApplicationPaths.PluginsPath;
        var programData = host.ApplicationPaths.ProgramDataPath;

        Assert.Equal(pluginsPath, Path.GetDirectoryName(dataFolder));
        Assert.Equal(programData, Path.GetDirectoryName(pluginsPath));

        // The names are compared against literals rather than against the constants that produce
        // them. A comparison against the constant passes whatever the constant says, and what
        // docs/storage.md tells an operator to copy is a name rather than a symbol.
        Assert.Equal("Jellyfin.Plugin.Requests", Path.GetFileName(dataFolder));

        var store = NewStore(dataFolder, out _);
        Assert.Equal(dataFolder, Path.GetDirectoryName(store.FilePath));
        Assert.Equal("requests.json", Path.GetFileName(store.FilePath));
        Assert.Equal("requests.json.writing", Path.GetFileName(store.PendingFilePath));

        // The configuration is a second file in a second place. An operator told to back up the
        // queue and not the settings restores a queue onto defaults.
        Assert.Equal(
            host.ApplicationPaths.PluginConfigurationsPath,
            Path.GetDirectoryName(host.Plugin.ConfigurationFilePath));
        Assert.Equal("Jellyfin.Plugin.Requests.xml", Path.GetFileName(host.Plugin.ConfigurationFilePath));
        Assert.Equal(programData, Path.GetDirectoryName(host.ApplicationPaths.PluginConfigurationsPath));
    }

    /// <summary>
    /// Bytes captured off one data folder and put into another that has never held a request. Every
    /// request comes back at the revision it was captured at, and the directory is a store afterwards
    /// rather than a document: a write lands and survives the store being opened again.
    /// <para>
    /// The revisions are checked because they are what a write is accepted against. A restore that
    /// brought the requests back and reset their revisions would leave every caller holding a read
    /// that the store no longer agrees with, and the first approval after a restore would be refused
    /// for a reason nobody could see.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AStoreRestoredIntoAFreshDataFolderHoldsWhatItHeldWhenItWasCaptured()
    {
        var captured = ADirectory();
        var source = NewStore(captured, out _);

        await source.AddAsync(ARequest(1), CancellationToken.None).ConfigureAwait(true);
        var second = await source.AddAsync(ARequest(2), CancellationToken.None).ConfigureAwait(true);
        await source.AddAsync(ARequest(3), CancellationToken.None).ConfigureAwait(true);

        // One of them is moved, so the capture holds a request at a revision above the one an added
        // request starts at.
        var approved = await source.ReplaceAsync(
            second.Request with { State = RequestState.Approved },
            second.Revision,
            CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(2, approved.Revision);

        var bytes = await File.ReadAllBytesAsync(source.FilePath, CancellationToken.None).ConfigureAwait(true);

        var restored = ADirectory();
        await SeedAsync(restored, bytes).ConfigureAwait(true);
        var opened = NewStore(restored, out _);

        var held = await opened.GetAllAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(3, held.Count);

        foreach (var ordinal in new[] { 1, 2, 3 })
        {
            var back = await opened.GetAsync(Identifier(ordinal), CancellationToken.None).ConfigureAwait(true);
            Assert.NotNull(back);
            Assert.Equal(ARequest(ordinal).DisplayTitle, back.Value.Request.DisplayTitle);
            Assert.Equal(ordinal == 2 ? 2 : 1, back.Value.Revision);
            Assert.Equal(
                ordinal == 2 ? RequestState.Approved : RequestState.Open,
                back.Value.Request.State);
        }

        // A store rather than a document: it takes a write, and the write is there when it is
        // opened again over the same directory.
        await opened.AddAsync(ARequest(4), CancellationToken.None).ConfigureAwait(true);
        var reopened = NewStore(restored, out _);
        Assert.Equal(4, (await reopened.GetAllAsync(CancellationToken.None).ConfigureAwait(true)).Count);
    }

    /// <summary>
    /// The case this issue exists for: a backup restored onto a plugin that is not the version it
    /// was captured from, arriving from the direction that works. The shape written before the store
    /// carried a version is read into a fresh data folder, both requests come back, and the restored
    /// directory takes a write afterwards.
    /// <para>
    /// The forward migration itself is <see cref="FileRequestStoreVersionTests"/>'s. What is asserted
    /// here is that it survives the restore, which is the step where a file arrives in a directory
    /// holding nothing else.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AStoreCapturedFromAnOlderShapeIsReadableAndWritableAfterBeingRestored()
    {
        var restored = ADirectory();
        await SeedAsync(restored, await FixtureAsync().ConfigureAwait(true)).ConfigureAwait(true);

        var opened = NewStore(restored, out var log);
        var held = await opened.GetAllAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, held.Count);
        Assert.Contains(
            log.At(LogLevel.Information),
            line => line.Message.Contains("migrated to version", StringComparison.Ordinal));

        await opened.AddAsync(ARequest(9), CancellationToken.None).ConfigureAwait(true);

        var reopened = NewStore(restored, out _);
        Assert.Equal(3, (await reopened.GetAllAsync(CancellationToken.None).ConfigureAwait(true)).Count);
    }

    /// <summary>
    /// The same case arriving from the other direction: a backup taken off a newer plugin and
    /// restored onto an older one. It is refused, and the point of the leg is the word "partly" -
    /// the entry in that file is one this version would understand perfectly well, and it is still
    /// not served, because a set that is missing whatever the reader did not recognise is a set an
    /// operator would act on believing it whole.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AStoreCapturedFromANewerVersionIsRefusedWholeRatherThanPartlyRestored()
    {
        var restored = ADirectory();

        var newer = FileRequestStore.OnDiskVersion + 1;
        var readable = ARequest(1);

        var document = string.Format(
            CultureInfo.InvariantCulture,
            "{{\"Version\":{0},\"Requests\":[{{\"Revision\":1,\"Request\":{{\"Id\":\"{1}\",\"RequestedByUserId\":\"{2}\",\"RequestedAt\":\"2026-08-08T08:00:00+00:00\",\"Kind\":0,\"DisplayTitle\":\"{3}\",\"State\":0,\"StateChangedAt\":\"2026-08-08T08:00:00+00:00\"}}}}],\"SomethingThisVersionHasNeverHeardOf\":true}}",
            newer,
            readable.Id,
            readable.RequestedByUserId,
            readable.DisplayTitle);

        var written = Encoding.UTF8.GetBytes(document);
        var path = await SeedAsync(restored, written).ConfigureAwait(true);

        var opened = NewStore(restored, out var log);

        // Not "returns the one entry it could read". Every read refuses, including the one that
        // asks about the request the file plainly holds.
        await Assert.ThrowsAsync<RequestStoreLoadException>(
            () => opened.GetAllAsync(CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<RequestStoreLoadException>(
            () => opened.GetAsync(readable.Id, CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<RequestStoreLoadException>(
            () => opened.FindForUserAsync(readable.RequestedByUserId, CancellationToken.None)).ConfigureAwait(true);

        Assert.NotEmpty(log.At(LogLevel.Error));

        // And the backup is still the backup. An operator who reinstalls the newer plugin has lost
        // nothing by having tried the older one over these bytes.
        var onDisk = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
        Assert.True(written.SequenceEqual(onDisk));
    }

    /// <summary>
    /// A backup that swept up the file a write was being built in restores as the queue and not as
    /// the half-written thing beside it. This is what lets <c>docs/storage.md</c> say the pending
    /// file need not be in a backup and does no harm when it is.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task APendingFileCarriedIntoTheBackupIsNotWhatIsRestored()
    {
        var captured = ADirectory();
        var source = NewStore(captured, out _);
        await source.AddAsync(ARequest(1), CancellationToken.None).ConfigureAwait(true);

        var bytes = await File.ReadAllBytesAsync(source.FilePath, CancellationToken.None).ConfigureAwait(true);

        var restored = ADirectory();
        await SeedAsync(restored, bytes).ConfigureAwait(true);

        // A complete document holding a different set, sitting where a write in flight would leave
        // one. A restore that read it would answer with a queue the operator never had.
        var stale = NewStore(ADirectory(), out _);
        await stale.AddAsync(ARequest(2), CancellationToken.None).ConfigureAwait(true);
        await stale.AddAsync(ARequest(3), CancellationToken.None).ConfigureAwait(true);
        await File.WriteAllBytesAsync(
            Path.Combine(restored, FileRequestStore.PendingFileName),
            await File.ReadAllBytesAsync(stale.FilePath, CancellationToken.None).ConfigureAwait(true),
            CancellationToken.None).ConfigureAwait(true);

        var opened = NewStore(restored, out _);
        var held = await opened.GetAllAsync(CancellationToken.None).ConfigureAwait(true);

        var only = Assert.Single(held);
        Assert.Equal(Identifier(1), only.Request.Id);
    }

    /// <summary>
    /// A request with a predictable identifier, so a leg can say which records survived a restore
    /// rather than only how many.
    /// </summary>
    /// <param name="ordinal">Which one.</param>
    /// <returns>A newly asked-for request.</returns>
    private static MediaRequest ARequest(int ordinal)
    {
        var asked = new DateTimeOffset(2026, 8, 8, 8, 0, 0, TimeSpan.Zero);

        return new MediaRequest
        {
            Id = Identifier(ordinal),
            RequestedByUserId = new Guid("b31d0f9a-5c2e-4a71-8f6b-0d4c3e2a1b58"),
            RequestedAt = asked,
            StateChangedAt = asked,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = string.Format(CultureInfo.InvariantCulture, "The Conversation {0}", ordinal)
        };
    }

    /// <summary>
    /// The identifier the ordinal names.
    /// </summary>
    /// <param name="ordinal">Which one.</param>
    /// <returns>The identifier.</returns>
    private static Guid Identifier(int ordinal)
        => new Guid(string.Format(CultureInfo.InvariantCulture, "00000000-0000-0000-0000-{0:D12}", ordinal));

    /// <summary>
    /// The fixture's bytes, read from beside the suite.
    /// </summary>
    /// <returns>The bytes.</returns>
    private static Task<byte[]> FixtureAsync()
        => File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, UnversionedFixture),
            CancellationToken.None);

    /// <summary>
    /// Puts bytes where a store opened over that directory will find them, which is what restoring a
    /// backup is.
    /// </summary>
    /// <param name="directory">The data folder being restored into.</param>
    /// <param name="bytes">What the backup holds.</param>
    /// <returns>The path they were written to.</returns>
    private static async Task<string> SeedAsync(string directory, byte[] bytes)
    {
        var path = Path.Combine(directory, FileRequestStore.FileName);
        await File.WriteAllBytesAsync(path, bytes, CancellationToken.None).ConfigureAwait(false);
        return path;
    }

    private string ADirectory()
    {
        var directory = TestRunDirectory.CreateSubdirectory();
        _directories.Add(directory);
        return directory;
    }

    private FileRequestStore NewStore(string directory, out RecordingLogger log)
    {
        log = new RecordingLogger();
        var store = new FileRequestStore(directory, log);
        _stores.Add(store);
        return store;
    }
}

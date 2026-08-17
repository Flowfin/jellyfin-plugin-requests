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
/// The version on the file, and the two directions a version can differ in.
/// <para>
/// Forward: a file written before the version existed is read and its records survive, so an
/// install that already holds requests is not an install that loses them. Backward: a file written
/// by a later version of this plugin is refused, said so in the log, and left exactly as it was,
/// because a plugin that has been downgraded cannot know what a field it has never heard of means
/// and guessing writes the guess back over the only copy.
/// </para>
/// <para>
/// The fixture the forward direction starts from is bytes this tree's own store wrote, kept as a
/// file. A fixture typed by hand to look like what the older shape would have been agrees with the
/// migration by construction: both would be written from the same belief about the old shape, so
/// the test passes whether or not that belief is right. Where the fixture came from is in the
/// comment on <see cref="UnversionedFixture"/>.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class FileRequestStoreVersionTests : IDisposable
{
    /// <summary>
    /// The file of bytes the store wrote before it carried a version.
    /// <para>
    /// Produced by the store in this tree at <c>592e517</c>, which is the commit before the version
    /// landed, by adding the two requests below through <c>AddAsync</c> and copying
    /// <c>requests.json</c> out of the directory it wrote in. It is not a shipped version's output
    /// and could not be: the one release there is was built from a commit carrying the store
    /// contract and no implementation, so no released version of this plugin has ever written a
    /// request file. The commands are in <c>docs/storage.md</c>. What this is, is the shape a server
    /// running the mainline before this change has on its disk.
    /// </para>
    /// </summary>
    private const string UnversionedFixture = "Storage/Fixtures/unversioned-written-at-592e517.json";

    /// <summary>
    /// The request that carries something in every field the older shape could lose. Its identifier
    /// is the one in the fixture.
    /// </summary>
    private static readonly Guid TheFullRequest = new Guid("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// The request that carries nothing but what a new ask has to carry, so a loader that filled an
    /// absent field with something is caught as well as one that dropped a present one.
    /// </summary>
    private static readonly Guid TheBareRequest = new Guid("00000000-0000-0000-0000-000000000002");

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
    /// What the store writes carries the version, and it is the version the code says it writes.
    /// Read off the bytes rather than through the loader, because a loader that ignored the field
    /// would let a store that never wrote one pass.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheFileTheStoreWritesCarriesTheVersion()
    {
        var directory = ADirectory();
        var store = NewStore(directory, out _);
        await store.AddAsync(ARequest(1), CancellationToken.None).ConfigureAwait(true);

        var written = await File.ReadAllTextAsync(store.FilePath, Encoding.UTF8, CancellationToken.None).ConfigureAwait(true);

        Assert.Contains(
            string.Format(CultureInfo.InvariantCulture, "\"Version\":{0}", FileRequestStore.OnDiskVersion),
            written,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The file written before the version existed is read, every field of both requests survives,
    /// and the migration says in the log that it happened.
    /// <para>
    /// The fields are checked one at a time rather than by comparing a request built here, because a
    /// request built here would be built from the same belief about the shape that the reader is
    /// built from.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheShapeWrittenBeforeTheVersionExistedIsReadAndNothingInItIsLost()
    {
        var directory = ADirectory();
        await SeedAsync(directory, await FixtureAsync().ConfigureAwait(true)).ConfigureAwait(true);

        var store = NewStore(directory, out var log);
        var held = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, held.Count);

        var full = await store.GetAsync(TheFullRequest, CancellationToken.None).ConfigureAwait(true);
        Assert.NotNull(full);
        Assert.Equal(1, full.Value.Revision);
        Assert.Equal(RequestedItemKind.Series, full.Value.Request.Kind);
        Assert.Equal("The Conversation, with an accent é", full.Value.Request.DisplayTitle);
        Assert.Equal(1974, full.Value.Request.DisplayYear);
        Assert.Equal("603", full.Value.Request.ProviderIds["Tmdb"]);
        Assert.Equal("tt0071360", full.Value.Request.ProviderIds["Imdb"]);
        Assert.Equal([1, 2], full.Value.Request.Seasons);
        Assert.Equal([new Guid("5f0c8a26-3d17-4b94-8e05-9a1b7c2d6e38")], full.Value.Request.JoinedByUserIds);
        Assert.Equal(RequestState.Approved, full.Value.Request.State);
        Assert.Equal(new Guid("2b7b4f1d-4f0e-4a63-9a3d-0f5a1c9e77aa"), full.Value.Request.StateChangedByUserId);
        Assert.Equal("A note the requester wrote.", full.Value.Request.RequesterNote);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 8, 9, 0, 0, TimeSpan.Zero),
            full.Value.Request.StateChangedAt);

        // The history is the field a migration is most likely to lose, because it is the only one
        // that is a list of records rather than a value.
        Assert.Single(full.Value.Request.History);
        Assert.Equal(RequestState.Open, full.Value.Request.History[0].From);
        Assert.Equal(RequestState.Approved, full.Value.Request.History[0].To);

        var bare = await store.GetAsync(TheBareRequest, CancellationToken.None).ConfigureAwait(true);
        Assert.NotNull(bare);
        Assert.Null(bare.Value.Request.DisplayYear);
        Assert.Empty(bare.Value.Request.ProviderIds);
        Assert.Empty(bare.Value.Request.History);
        Assert.Equal(RequestState.Open, bare.Value.Request.State);

        Assert.Contains(
            log.At(LogLevel.Information),
            line => line.Message.Contains("migrated to version", StringComparison.Ordinal));
    }

    /// <summary>
    /// Reading an older file does not rewrite it. The migration is a read, so the bytes on the disk
    /// are the ones that were there until some later write replaces the file whole, and the write
    /// that does replace it writes the current version.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ReadingAnOlderFileLeavesItAloneAndTheNextWriteIsTheOneThatMovesIt()
    {
        var directory = ADirectory();
        var fixture = await FixtureAsync().ConfigureAwait(true);
        var path = await SeedAsync(directory, fixture).ConfigureAwait(true);

        var store = NewStore(directory, out _);
        await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);

        var onDisk = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
        Assert.True(fixture.SequenceEqual(onDisk));

        await store.AddAsync(ARequest(3), CancellationToken.None).ConfigureAwait(true);

        var afterTheWrite = await File.ReadAllTextAsync(path, Encoding.UTF8, CancellationToken.None).ConfigureAwait(true);
        Assert.Contains(
            string.Format(CultureInfo.InvariantCulture, "\"Version\":{0}", FileRequestStore.OnDiskVersion),
            afterTheWrite,
            StringComparison.Ordinal);

        var reopened = NewStore(directory, out _);
        Assert.Equal(3, (await reopened.GetAllAsync(CancellationToken.None).ConfigureAwait(true)).Count);
    }

    /// <summary>
    /// A file written by a later version of this plugin is refused, the refusal names both numbers,
    /// it is written to the log at a level an operator sees, and the file is byte for byte what it
    /// was afterwards.
    /// <para>
    /// The three are one property rather than three: a refusal that lost the file would be worse
    /// than the guess it was avoiding, and a refusal nobody is told about looks to an operator like
    /// a plugin that has forgotten every request.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AFileFromALaterVersionIsRefusedInTheLogAndLeftUntouched()
    {
        var directory = ADirectory();
        var newer = FileRequestStore.OnDiskVersion + 1;

        var written = Encoding.UTF8.GetBytes(string.Format(
            CultureInfo.InvariantCulture,
            "{{\"Version\":{0},\"Requests\":[],\"SomethingThisVersionHasNeverHeardOf\":true}}",
            newer));

        var path = await SeedAsync(directory, written).ConfigureAwait(true);
        var store = NewStore(directory, out var log);

        var refused = await Assert.ThrowsAsync<RequestStoreLoadException>(
            () => store.GetAllAsync(CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(store.FilePath, refused.FilePath);
        Assert.Contains(newer.ToString(CultureInfo.InvariantCulture), refused.Message, StringComparison.Ordinal);
        Assert.Contains(
            FileRequestStore.OnDiskVersion.ToString(CultureInfo.InvariantCulture),
            refused.Message,
            StringComparison.Ordinal);

        var complaint = Assert.Single(log.At(LogLevel.Error));
        Assert.Contains(store.FilePath, complaint.Message, StringComparison.Ordinal);
        Assert.Contains(newer.ToString(CultureInfo.InvariantCulture), complaint.Message, StringComparison.Ordinal);

        var onDisk = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
        Assert.True(written.SequenceEqual(onDisk));
    }

    /// <summary>
    /// The refusal above is refused on every call, not only the first. A store that answered the
    /// second call out of an empty set would be a store that had quietly decided the queue was
    /// empty, and the write after it would put that emptiness on the disk.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AFileFromALaterVersionIsStillRefusedOnEveryCallAfterTheFirst()
    {
        var directory = ADirectory();
        var written = Encoding.UTF8.GetBytes(string.Format(
            CultureInfo.InvariantCulture,
            "{{\"Version\":{0},\"Requests\":[]}}",
            FileRequestStore.OnDiskVersion + 1));

        var path = await SeedAsync(directory, written).ConfigureAwait(true);
        var store = NewStore(directory, out _);

        await Assert.ThrowsAsync<RequestStoreLoadException>(
            () => store.GetAllAsync(CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<RequestStoreLoadException>(
            () => store.GetAllAsync(CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<RequestStoreLoadException>(
            () => store.AddAsync(ARequest(1), CancellationToken.None)).ConfigureAwait(true);

        var onDisk = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
        Assert.True(written.SequenceEqual(onDisk));
    }

    /// <summary>
    /// A document with no version at all is refused. It is neither the shape written before the
    /// version existed, which is an array, nor a document this store wrote, so reading it would mean
    /// deciding on its behalf which of the two it meant to be.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData("{\"Requests\":[]}")]
    [InlineData("{\"Version\":0,\"Requests\":[]}")]
    [InlineData("{\"Version\":-1,\"Requests\":[]}")]
    [InlineData("{\"Version\":1}")]
    public async Task ADocumentThisStoreDidNotWriteIsRefused(string document)
    {
        var directory = ADirectory();
        var written = Encoding.UTF8.GetBytes(document);
        var path = await SeedAsync(directory, written).ConfigureAwait(true);

        var store = NewStore(directory, out var log);

        var refused = await Assert.ThrowsAsync<RequestStoreLoadException>(
            () => store.GetAllAsync(CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(store.FilePath, refused.FilePath);
        Assert.NotEmpty(log.At(LogLevel.Error));
        var onDisk = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
        Assert.True(written.SequenceEqual(onDisk));
    }

    /// <summary>
    /// A request with a predictable identifier, so a leg can say which records survived rather than
    /// only how many.
    /// </summary>
    /// <param name="ordinal">Which one.</param>
    /// <returns>A newly asked-for request.</returns>
    private static MediaRequest ARequest(int ordinal)
    {
        var asked = new DateTimeOffset(2026, 8, 8, 8, 0, 0, TimeSpan.Zero);

        return new MediaRequest
        {
            Id = new Guid(string.Format(CultureInfo.InvariantCulture, "00000000-0000-0000-0000-{0:D12}", ordinal)),
            RequestedByUserId = new Guid("b31d0f9a-5c2e-4a71-8f6b-0d4c3e2a1b58"),
            RequestedAt = asked,
            StateChangedAt = asked,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = string.Format(CultureInfo.InvariantCulture, "The Conversation {0}", ordinal)
        };
    }

    /// <summary>
    /// The fixture's bytes, read from beside the suite.
    /// </summary>
    /// <returns>The bytes.</returns>
    private static Task<byte[]> FixtureAsync()
        => File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, UnversionedFixture),
            CancellationToken.None);

    /// <summary>
    /// Puts bytes where the store will find them.
    /// </summary>
    /// <param name="directory">The store's directory.</param>
    /// <param name="bytes">What the file holds.</param>
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
        var store = new FileRequestStore(directory, log, TestClock.AtAFixedMoment());
        _stores.Add(store);
        return store;
    }
}

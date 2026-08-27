using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
/// The migration from the first on-disk shape to the second, which is where a history entry stops
/// naming the person who moved a request and says what kind of caller they were.
/// <para>
/// <b>It starts from bytes the shipped version wrote.</b> `0.2.0.0-stable` is the first release that
/// ships a store, and the fixture below was captured through its own <c>AddAsync</c> at
/// <c>60faf41</c>, the commit that package was built from. That is what <c>docs/storage.md</c> asks
/// for and it is why this test can say anything at all: a document typed here to look like the older
/// shape would agree with the migration by construction, because both would come out of the same
/// belief about what the older shape was.
/// </para>
/// <para>
/// <b>The request in it carries one row per kind of caller the older shape could record</b>: the ask,
/// a move an administrator made, and a move the plugin made after looking at the library. Those are
/// the three cases the migration has to tell apart, and each is checked by name rather than by count.
/// </para>
/// <para>
/// <b>What is not covered, said rather than left to be assumed.</b> An administrator acting on a
/// request they asked for themselves is one caller holding two roles, which this version records as
/// both and the older shape could not record at all. A migrated entry of that kind therefore reads as
/// an administrator, and no fixture can show otherwise because no older file ever held the second
/// role. The class being tested says the same thing where somebody reading the migration will be.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class HistoryWithoutPeopleTests : IDisposable
{
    /// <summary>
    /// A queue the shipped version wrote, holding one request whose history has all three kinds of
    /// row.
    /// </summary>
    private const string ThreeRolesFixture =
        "Storage/Fixtures/version-1-three-roles-written-by-0.2.0.0-at-60faf41.json";

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
    /// Every row of a history written by the older version comes back carrying what its mover was,
    /// and the three kinds are told apart.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EveryRowOfAnOlderHistoryComesBackSayingWhatTheMoverWas()
    {
        var directory = ADirectory();
        await SeedAsync(directory).ConfigureAwait(true);

        var store = NewStore(directory, out var log);
        var held = Assert.Single(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(3, held.Request.History.Count);

        Assert.Equal(RequestArrival.Endpoint, held.Request.History[0].Arrival);
        Assert.Equal(RequestActor.Requester, held.Request.History[0].By);

        Assert.Null(held.Request.History[1].Arrival);
        Assert.Equal(RequestActor.Administrator, held.Request.History[1].By);

        Assert.Null(held.Request.History[2].Arrival);
        Assert.Equal(RequestActor.Plugin, held.Request.History[2].By);

        // The migration says it happened, at a level an operator reading the server's log sees. A
        // shape change that moved a queue silently is one nobody can place afterwards.
        Assert.Contains(
            log.At(LogLevel.Information),
            line => line.Message.Contains("migrated to it", StringComparison.Ordinal));
    }

    /// <summary>
    /// Nothing else on the request moves. A migration that touched one field and quietly dropped
    /// another would pass the test above.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task NothingButTheHistoryRowsIsChangedByTheMigration()
    {
        var directory = ADirectory();
        await SeedAsync(directory).ConfigureAwait(true);

        var store = NewStore(directory, out _);
        var held = Assert.Single(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(1, held.Revision);
        Assert.Equal(new Guid("00000000-0000-0000-0000-000000000003"), held.Request.Id);
        Assert.Equal(new Guid("b31d0f9a-5c2e-4a71-8f6b-0d4c3e2a1b58"), held.Request.RequestedByUserId);
        Assert.Equal(RequestedItemKind.Movie, held.Request.Kind);
        Assert.Equal("A film three people decided about", held.Request.DisplayTitle);
        Assert.Equal(1974, held.Request.DisplayYear);
        Assert.Equal("603", held.Request.ProviderIds["Tmdb"]);
        Assert.Equal([new Guid("5f0c8a26-3d17-4b94-8e05-9a1b7c2d6e38")], held.Request.JoinedByUserIds);
        Assert.Equal(RequestState.Fulfilled, held.Request.State);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 27, 6, 30, 0, TimeSpan.Zero),
            held.Request.StateChangedAt);

        // The times and the states on the rows themselves, which are what makes a history worth
        // keeping and are the fields sitting next to the one the migration rewrites.
        Assert.Equal(new DateTimeOffset(2026, 8, 27, 5, 0, 0, TimeSpan.Zero), held.Request.History[0].At);
        Assert.Equal(new DateTimeOffset(2026, 8, 27, 6, 0, 0, TimeSpan.Zero), held.Request.History[1].At);
        Assert.Equal(new DateTimeOffset(2026, 8, 27, 6, 30, 0, TimeSpan.Zero), held.Request.History[2].At);
        Assert.Equal(RequestState.Open, held.Request.History[1].From);
        Assert.Equal(RequestState.Approved, held.Request.History[1].To);
        Assert.Equal(RequestState.Approved, held.Request.History[2].From);
        Assert.Equal(RequestState.Fulfilled, held.Request.History[2].To);
    }

    /// <summary>
    /// No identifier survives anywhere in the history. This is the property the whole shape change is
    /// for, so it is asserted over the bytes the store writes back rather than over the objects it
    /// hands out: an object graph that no longer exposes a field can still be serialising one.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheIdentifierOnAHistoryRowDoesNotSurviveTheWriteThatFollowsTheMigration()
    {
        var directory = ADirectory();
        await SeedAsync(directory).ConfigureAwait(true);

        var store = NewStore(directory, out _);
        var held = Assert.Single(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));

        // A write, because the migration is a read and the file is not touched until one happens.
        await store.ReplaceAsync(
            held.Request with { RequesterNote = "Something to make the store write." },
            held.Revision,
            CancellationToken.None).ConfigureAwait(true);

        var written = await File.ReadAllTextAsync(store.FilePath, Encoding.UTF8, CancellationToken.None).ConfigureAwait(true);

        Assert.DoesNotContain("\"ByUserId\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("2b7b4f1d-4f0e-4a63-9a3d-0f5a1c9e77aa", written, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"By\":", written, StringComparison.Ordinal);
    }

    /// <summary>
    /// The file is not touched by the read that migrates it, and the write that follows carries the
    /// new number. A migration that rewrote the file as it read it would leave a server that opened
    /// the newer plugin once unable to go back.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheOlderFileIsLeftAloneUntilAWriteReplacesIt()
    {
        var directory = ADirectory();
        var fixture = await SeedAsync(directory).ConfigureAwait(true);
        var path = Path.Combine(directory, FileRequestStore.FileName);

        var store = NewStore(directory, out _);
        var held = Assert.Single(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));

        var onDisk = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);

        Assert.True(fixture.SequenceEqual(onDisk));

        await store.ReplaceAsync(
            held.Request with { RequesterNote = "Something to make the store write." },
            held.Revision,
            CancellationToken.None).ConfigureAwait(true);

        var written = await File.ReadAllTextAsync(path, Encoding.UTF8, CancellationToken.None).ConfigureAwait(true);

        Assert.StartsWith(
            "{\"Version\":" + FileRequestStore.OnDiskVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            written,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The fixture is still the older shape. Without this every test here passes against a file
    /// somebody regenerated from the current store, which would migrate nothing and prove nothing.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task TheFixtureIsStillTheOlderShape()
    {
        var bytes = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, ThreeRolesFixture),
            CancellationToken.None).ConfigureAwait(true);

        Assert.StartsWith("{\"Version\":1,", bytes, StringComparison.Ordinal);
        Assert.Contains("\"ByUserId\"", bytes, StringComparison.Ordinal);
        Assert.DoesNotContain("\"By\":", bytes, StringComparison.Ordinal);
    }

    /// <summary>
    /// A directory of this test's own, removed afterwards.
    /// </summary>
    /// <returns>The directory.</returns>
    private string ADirectory()
    {
        var directory = TestRunDirectory.CreateSubdirectory();
        _directories.Add(directory);

        return directory;
    }

    /// <summary>
    /// A store over a directory, disposed afterwards.
    /// </summary>
    /// <param name="directory">Where it keeps its file.</param>
    /// <param name="log">What it wrote while it was opening.</param>
    /// <returns>The store.</returns>
    private FileRequestStore NewStore(string directory, out RecordingLogger log)
    {
        log = new RecordingLogger();

        var store = new FileRequestStore(
            directory,
            log,
            new TestClock(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero)));

        _stores.Add(store);

        return store;
    }

    /// <summary>
    /// Puts the fixture where a store over that directory will read it.
    /// </summary>
    /// <param name="directory">The store's directory.</param>
    /// <returns>The bytes that were put there.</returns>
    private static async Task<byte[]> SeedAsync(string directory)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, ThreeRolesFixture);

        if (!File.Exists(fixture))
        {
            throw new FileNotFoundException(
                "The migration fixture did not reach the test output, so this test would report on nothing.",
                fixture);
        }

        var bytes = await File.ReadAllBytesAsync(fixture, CancellationToken.None).ConfigureAwait(true);

        await File.WriteAllBytesAsync(
            Path.Combine(directory, FileRequestStore.FileName),
            bytes,
            CancellationToken.None).ConfigureAwait(true);

        return bytes;
    }
}

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
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Storage;

/// <summary>
/// What the durable store owes beyond the contract every store keeps: after an interruption at any
/// point the store loads, at most the record being written is lost, and bytes that are not the
/// document this store writes are refused rather than partly read.
/// <para>
/// Each proof here is made against real bytes on a real disk, read back by the loader that ships.
/// The interruption is a write actually stopped in the middle rather than a half file written by
/// hand, and the truncation legs walk every byte offset rather than a few chosen ones, because the
/// offsets somebody picks by hand are the ones they already believed were safe.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class FileRequestStoreDurabilityTests : IDisposable
{
    /// <summary>
    /// Enough requests that the serialiser has flushed part of the document to the disk before the
    /// interrupted record is reached. With a handful of records the whole document fits in the
    /// serialiser's buffer, nothing has reached the file when the write stops, and the leg would
    /// prove the easy half of the property while looking like it proved both.
    /// </summary>
    private const int EnoughToHaveReachedTheDisk = 60;

    private readonly List<FileRequestStore> _stores = [];
    private readonly List<string> _directories = [];

    /// <summary>
    /// Every field of a request comes back as itself from a store opened fresh over the same
    /// directory, including the three absences a hand-typed request arrives with. The comparison is
    /// field by field because <c>docs/storage.md</c> measured that the record's generated equality
    /// compares the provider identifiers by reference, which two dictionaries never satisfy once one
    /// of them has come off a disk. The history is compared the same way and for the same reason: it
    /// is held as an interface, so the generated equality compares the list itself and never its
    /// entries, and a list that has been read back is a different list whatever it holds.
    /// <para>
    /// The state here is reached by making the move rather than by writing the state onto the
    /// record, so the request carries a history and the round trip has one to lose. A request whose
    /// history is empty would let a store that drops the field entirely pass this.
    /// </para>
    /// <para>
    /// The history is two rows rather than one, and they are of the two different kinds there are:
    /// the arrival the surface wrote when the request came into existence, and the move an operator
    /// made afterwards. A round trip over one kind would let a store that drops the arrival back
    /// through, and the arrival is the row that carries the field this record gained last.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EveryFieldComesBackFromAStoreOpenedFreshOverTheSameDirectory()
    {
        var directory = ADirectory();
        var approvedBy = new Guid("2b7b4f1d-4f0e-4a63-9a3d-0f5a1c9e77aa");
        var asked = ARequest(1) with
        {
            DisplayTitle = "A title with a comma, an accent é and a quote \"",
            DisplayYear = 1999,
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "603", ["Imdb"] = "tt0133093" },
            Availability = LibraryAvailability.Partial,
            AvailabilityCheckedAt = new DateTimeOffset(2026, 3, 3, 0, 0, 0, TimeSpan.Zero)
        };

        var full = RequestLifecycle.Move(
            asked with
            {
                History = [RequestHistoryEntry.Arriving(RequestArrival.Seam, asked)]
            },
            RequestState.Approved,
            new DateTimeOffset(2026, 8, 8, 9, 0, 0, TimeSpan.Zero),
            RequestCaller.Administrator(approvedBy));

        // A second person waiting for the same thing, so the list that carries them has something
        // in it to lose on the way to the file and back.
        full = RequestLifecycle.Join(full, new Guid("5f0c8a26-3d17-4b94-8e05-9a1b7c2d6e38"));

        var bare = ARequest(2);

        var writing = NewStore(directory);
        await writing.AddAsync(full, CancellationToken.None).ConfigureAwait(true);
        await writing.AddAsync(bare, CancellationToken.None).ConfigureAwait(true);

        var reading = NewStore(directory);
        var readFull = await reading.GetAsync(full.Id, CancellationToken.None).ConfigureAwait(true);
        var readBare = await reading.GetAsync(bare.Id, CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(readFull);
        // Every collection on the record is put back before the comparison, for the reason in the
        // summary: they are declared as interfaces, so the generated equality compares each list or
        // map itself and never what is in it. Each one is then compared for its contents below or,
        // where it is empty on this request, by the field set test in MediaRequestTests refusing a
        // collection that arrived without a line here.
        Assert.Equal(
            full with
            {
                ProviderIds = readFull.Value.Request.ProviderIds,
                History = readFull.Value.Request.History,
                Seasons = readFull.Value.Request.Seasons,
                JoinedByUserIds = readFull.Value.Request.JoinedByUserIds
            },
            readFull.Value.Request);
        Assert.Equal(
            full.ProviderIds.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            readFull.Value.Request.ProviderIds.OrderBy(pair => pair.Key, StringComparer.Ordinal));

        // Entry by entry, and the entries are records of values, so this compares what each row
        // said and not which list it is in. The arrival and the approval above are the two entries
        // there are to lose, and the arrival is asserted by name because it is the only row
        // carrying an arrival at all.
        Assert.Equal(full.History, readFull.Value.Request.History);
        Assert.Equal(2, readFull.Value.Request.History.Count);
        Assert.Equal(RequestArrival.Seam, readFull.Value.Request.History[0].Arrival);
        Assert.Null(readFull.Value.Request.History[1].Arrival);

        Assert.Equal(full.JoinedByUserIds, readFull.Value.Request.JoinedByUserIds);
        Assert.Single(readFull.Value.Request.JoinedByUserIds);

        Assert.NotNull(readBare);
        Assert.Null(readBare.Value.Request.DisplayYear);
        Assert.Null(readBare.Value.Request.StateChangedByUserId);
        Assert.Null(readBare.Value.Request.AvailabilityCheckedAt);
        Assert.Empty(readBare.Value.Request.ProviderIds);
    }

    /// <summary>
    /// A write stopped in the middle of the bytes. The caller is told, the file the loader reads has
    /// not moved, and what is lost is the one record the write was adding and nothing else. The
    /// store keeps working afterwards, which is the half a store that refuses to open ever again
    /// would fail.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AWriteStoppedInTheMiddleReachesTheCallerAndCostsOnlyTheRecordItWasWriting()
    {
        var directory = ADirectory();
        var store = NewStore(directory);

        for (var ordinal = 1; ordinal <= EnoughToHaveReachedTheDisk; ordinal++)
        {
            await store.AddAsync(ABulkyRequest(ordinal), CancellationToken.None).ConfigureAwait(true);
        }

        var before = await File.ReadAllBytesAsync(store.FilePath, CancellationToken.None).ConfigureAwait(true);
        var interrupted = ABulkyRequest(EnoughToHaveReachedTheDisk + 1) with { ProviderIds = new InterruptingProviderIds() };

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            () => store.AddAsync(interrupted, CancellationToken.None)).ConfigureAwait(true);

        Assert.True(IsOrCarries<WriteInterruptedException>(thrown), thrown.ToString());

        // The file the loader reads is byte for byte the one it was before the write started. This
        // is the assertion the whole design is for, and it is made before anything else so that a
        // store writing in place fails here rather than somewhere further down.
        var after = await File.ReadAllBytesAsync(store.FilePath, CancellationToken.None).ConfigureAwait(true);
        Assert.True(before.SequenceEqual(after));

        var live = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(EnoughToHaveReachedTheDisk, live.Count);
        Assert.Null(await store.GetAsync(interrupted.Id, CancellationToken.None).ConfigureAwait(true));

        var reopened = NewStore(directory);
        var reloaded = await reopened.GetAllAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(EnoughToHaveReachedTheDisk, reloaded.Count);
        Assert.Null(await reopened.GetAsync(interrupted.Id, CancellationToken.None).ConfigureAwait(true));

        // The write had already put bytes on the disk when it stopped, so this was an interruption
        // in the middle of a document rather than one before it started. What a reader of that file
        // would find is a document that never closed, and nothing reads it. The two closing bytes
        // are checked rather than one, because a request object ends in a brace of its own and a
        // stop just after one would look like the end of the document if only the last byte were
        // read.
        var half = await File.ReadAllBytesAsync(store.PendingFilePath, CancellationToken.None).ConfigureAwait(true);
        Assert.NotEmpty(half);
        Assert.False(half.Length >= 2 && half[^2] == (byte)']' && half[^1] == (byte)'}');

        var added = await reopened.AddAsync(ABulkyRequest(EnoughToHaveReachedTheDisk + 2), CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1, added.Revision);

        var again = NewStore(directory);
        Assert.Equal(EnoughToHaveReachedTheDisk + 1, (await again.GetAllAsync(CancellationToken.None).ConfigureAwait(true)).Count);
    }

    /// <summary>
    /// The half file an interruption leaves, at every byte offset it could have stopped at. For each
    /// one the store loads and holds exactly the set it held before the write, so the record being
    /// written is the whole of what an interruption can cost.
    /// <para>
    /// Every offset rather than a sample, because the offsets a person chooses are the ones they
    /// already expect to be safe, and the interesting one is whichever byte happens to sit at the
    /// end of a buffer.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ThePendingFileStoppedAtEveryOffsetLeavesTheSetTheStoreHeldBeforeTheWrite()
    {
        var directory = ADirectory();
        var seeding = NewStore(directory);
        var kept = new List<Guid>();

        for (var ordinal = 1; ordinal <= 3; ordinal++)
        {
            var request = ARequest(ordinal);
            kept.Add(request.Id);
            await seeding.AddAsync(request, CancellationToken.None).ConfigureAwait(true);
        }

        var before = await File.ReadAllBytesAsync(seeding.FilePath, CancellationToken.None).ConfigureAwait(true);

        var fourth = ARequest(4);
        await seeding.AddAsync(fourth, CancellationToken.None).ConfigureAwait(true);
        var wholeWrite = await File.ReadAllBytesAsync(seeding.FilePath, CancellationToken.None).ConfigureAwait(true);

        var offsets = 0;

        for (var stopped = 0; stopped <= wholeWrite.Length; stopped++)
        {
            await File.WriteAllBytesAsync(seeding.FilePath, before, CancellationToken.None).ConfigureAwait(true);
            await File.WriteAllBytesAsync(seeding.PendingFilePath, wholeWrite[..stopped], CancellationToken.None).ConfigureAwait(true);

            var store = new FileRequestStore(directory, new RecordingLogger(), TestClock.AtAFixedMoment());

            try
            {
                var held = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);

                Assert.Equal(3, held.Count);
                Assert.Equal(kept.Order(), held.Select(stored => stored.Request.Id).Order());
                Assert.DoesNotContain(held, stored => stored.Request.Id == fourth.Id);
            }
            finally
            {
                store.Dispose();
            }

            offsets++;
        }

        Assert.Equal(wholeWrite.Length + 1, offsets);
    }

    /// <summary>
    /// The pending file is ignored rather than merely unreadable. Left behind whole, and holding a
    /// set that differs from the one the store keeps, it still changes nothing about what loads.
    /// <para>
    /// The truncation leg above cannot say this on its own: a loader that read the pending file and
    /// fell back to the other one whenever it failed to parse would pass every offset of it and
    /// would take this file as the truth.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ACompletePendingFileIsIgnoredRatherThanRead()
    {
        var directory = ADirectory();
        var seeding = NewStore(directory);
        await seeding.AddAsync(ARequest(1), CancellationToken.None).ConfigureAwait(true);

        var one = await File.ReadAllBytesAsync(seeding.FilePath, CancellationToken.None).ConfigureAwait(true);

        var fourth = ARequest(4);
        await seeding.AddAsync(fourth, CancellationToken.None).ConfigureAwait(true);
        var two = await File.ReadAllBytesAsync(seeding.FilePath, CancellationToken.None).ConfigureAwait(true);

        await File.WriteAllBytesAsync(seeding.FilePath, one, CancellationToken.None).ConfigureAwait(true);
        await File.WriteAllBytesAsync(seeding.PendingFilePath, two, CancellationToken.None).ConfigureAwait(true);

        var store = NewStore(directory);
        var held = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Single(held);
        Assert.DoesNotContain(held, stored => stored.Request.Id == fourth.Id);
    }

    /// <summary>
    /// The file the loader reads, cut short anywhere before its end. Every one of those is refused,
    /// naming the file, rather than loading the records that did parse.
    /// <para>
    /// A partial read is the failure this stands against and it is quiet: the plugin comes up with a
    /// shorter queue, nothing reports anything, and the first write afterwards puts the shorter
    /// queue over the file that still held the rest.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheStoreFileCutShortAnywhereIsRefusedRatherThanPartlyRead()
    {
        var directory = ADirectory();
        var seeding = NewStore(directory);

        for (var ordinal = 1; ordinal <= 3; ordinal++)
        {
            await seeding.AddAsync(ARequest(ordinal), CancellationToken.None).ConfigureAwait(true);
        }

        var whole = await File.ReadAllBytesAsync(seeding.FilePath, CancellationToken.None).ConfigureAwait(true);
        var refusals = 0;

        for (var cut = 0; cut < whole.Length; cut++)
        {
            await File.WriteAllBytesAsync(seeding.FilePath, whole[..cut], CancellationToken.None).ConfigureAwait(true);

            var store = new FileRequestStore(directory, new RecordingLogger(), TestClock.AtAFixedMoment());

            try
            {
                var refused = await Assert.ThrowsAsync<RequestStoreLoadException>(
                    () => store.GetAllAsync(CancellationToken.None)).ConfigureAwait(true);

                Assert.Equal(store.FilePath, refused.FilePath);
            }
            finally
            {
                store.Dispose();
            }

            refusals++;
        }

        Assert.Equal(whole.Length, refusals);

        await File.WriteAllBytesAsync(seeding.FilePath, whole, CancellationToken.None).ConfigureAwait(true);
        var reopened = NewStore(directory);
        Assert.Equal(3, (await reopened.GetAllAsync(CancellationToken.None).ConfigureAwait(true)).Count);
    }

    /// <summary>
    /// Bytes that parse but are not a set of requests this store could have written. Each is refused
    /// with the file named, because each one leaves the store unable to say what it holds, and
    /// answering anyway is the partial read under a different name.
    /// <para>
    /// What is deliberately not claimed here: a value changed inside a string is still a document
    /// this store could have written, and nothing in it would notice. Noticing would need a checksum
    /// over the bytes, which this store does not carry.
    /// </para>
    /// </summary>
    /// <param name="damage">Which of the shapes the file is replaced by.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData("garbage")]
    [InlineData("null")]
    [InlineData("null-entry")]
    [InlineData("no-request")]
    [InlineData("revision-below-one")]
    [InlineData("one-identifier-twice")]
    public async Task BytesThatAreNotASetOfRequestsAreRefusedWithTheFileNamed(string damage)
    {
        var directory = ADirectory();
        var seeding = NewStore(directory);
        await seeding.AddAsync(ARequest(1), CancellationToken.None).ConfigureAwait(true);

        var written = await File.ReadAllTextAsync(seeding.FilePath, Encoding.UTF8, CancellationToken.None).ConfigureAwait(true);
        var entry = written[(written.IndexOf('[', StringComparison.Ordinal) + 1)..written.LastIndexOf(']')];

        var damaged = damage switch
        {
            "garbage" => "this is not the document this store writes",
            "null" => "null",
            "null-entry" => Document("null"),
            "no-request" => Document("{\"Revision\":1}"),
            "revision-below-one" => written.Replace("\"Revision\":1", "\"Revision\":0", StringComparison.Ordinal),
            _ => Document(string.Format(CultureInfo.InvariantCulture, "{0},{0}", entry))
        };

        Assert.NotEqual(written, damaged);
        await File.WriteAllTextAsync(seeding.FilePath, damaged, Encoding.UTF8, CancellationToken.None).ConfigureAwait(true);

        var store = new FileRequestStore(directory, new RecordingLogger(), TestClock.AtAFixedMoment());

        try
        {
            var refused = await Assert.ThrowsAsync<RequestStoreLoadException>(
                () => store.GetAllAsync(CancellationToken.None)).ConfigureAwait(true);

            Assert.Equal(store.FilePath, refused.FilePath);
            Assert.Contains(store.FilePath, refused.Message, StringComparison.Ordinal);
        }
        finally
        {
            store.Dispose();
        }
    }

    /// <summary>
    /// A write the file system refuses. The caller is told, the store goes on reporting what it
    /// actually holds rather than what it was asked to hold, and the next write works once the
    /// obstruction is gone.
    /// <para>
    /// This is not a full disk and does not claim to be one: filling a volume needs a volume to
    /// fill, and making one needs the elevation the headless rule in <c>docs/testing.md</c> refuses.
    /// What is proven instead is the property a full disk is one instance of. The write path catches
    /// nothing, so every failure the file system raises reaches the caller by construction, and the
    /// runtime reports an exhausted disk as one of those.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AWriteTheFileSystemRefusesReachesTheCallerAndDropsNothingQuietly()
    {
        var directory = ADirectory();
        var store = NewStore(directory);
        await store.AddAsync(ARequest(1), CancellationToken.None).ConfigureAwait(true);
        await store.AddAsync(ARequest(2), CancellationToken.None).ConfigureAwait(true);

        var before = await File.ReadAllBytesAsync(store.FilePath, CancellationToken.None).ConfigureAwait(true);

        // Somewhere the write cannot put its file. What the platform raises differs between them and
        // neither is caught anywhere in the write path.
        Directory.CreateDirectory(store.PendingFilePath);

        var third = ARequest(3);
        var refused = await Assert.ThrowsAnyAsync<Exception>(
            () => store.AddAsync(third, CancellationToken.None)).ConfigureAwait(true);

        Assert.True(refused is IOException or UnauthorizedAccessException, refused.ToString());

        Assert.Equal(2, (await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true)).Count);
        Assert.Null(await store.GetAsync(third.Id, CancellationToken.None).ConfigureAwait(true));

        var after = await File.ReadAllBytesAsync(store.FilePath, CancellationToken.None).ConfigureAwait(true);
        Assert.True(before.SequenceEqual(after));

        var reopened = NewStore(directory);
        Assert.Equal(2, (await reopened.GetAllAsync(CancellationToken.None).ConfigureAwait(true)).Count);

        Directory.Delete(store.PendingFilePath);
        var added = await store.AddAsync(third, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1, added.Revision);
        Assert.Equal(3, (await NewStore(directory).GetAllAsync(CancellationToken.None).ConfigureAwait(true)).Count);
    }

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
    /// A document of the shape the store writes, around entries a test chose. It is built here
    /// rather than typed into each case, so a change to the document's own shape is one edit and
    /// the cases stay about the entry that is wrong.
    /// </summary>
    /// <param name="entries">What goes inside the list, already written as JSON.</param>
    /// <returns>The document.</returns>
    private static string Document(string entries)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{{\"Version\":{0},\"Requests\":[{1}]}}",
            FileRequestStore.OnDiskVersion,
            entries);

    /// <summary>
    /// Whether an exception is the one named or carries it underneath. The serialiser is free to
    /// wrap what a record throws, and the assertion is about what stopped the write rather than
    /// about how many layers it arrived through.
    /// </summary>
    /// <typeparam name="T">The exception looked for.</typeparam>
    /// <param name="thrown">What the caller was given.</param>
    /// <returns>Whether it is there.</returns>
    private static bool IsOrCarries<T>(Exception thrown)
        where T : Exception
    {
        for (var carried = thrown; carried is not null; carried = carried.InnerException)
        {
            if (carried is T)
            {
                return true;
            }
        }

        return false;
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
    /// The same request with a title long enough that a set of them outgrows the serialiser's
    /// buffer, which is what puts bytes on the disk before an interruption can happen.
    /// </summary>
    /// <param name="ordinal">Which one.</param>
    /// <returns>A newly asked-for request.</returns>
    private static MediaRequest ABulkyRequest(int ordinal)
        => ARequest(ordinal) with { DisplayTitle = new string('t', 400) };

    private string ADirectory()
    {
        var directory = TestRunDirectory.CreateSubdirectory();
        _directories.Add(directory);
        return directory;
    }

    private FileRequestStore NewStore(string directory)
    {
        var store = new FileRequestStore(directory, new RecordingLogger(), TestClock.AtAFixedMoment());
        _stores.Add(store);
        return store;
    }
}

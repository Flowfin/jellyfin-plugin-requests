using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Storage;

/// <summary>
/// The queue file a shipped version of this plugin writes, kept as the bytes that version produced.
/// <para>
/// <b>Why this exists before anything needs to migrate.</b> The rule in <c>docs/storage.md</c> is
/// that a migration test starts from bytes an older version actually wrote, and that a fixture
/// typed by hand to look like them agrees with the migration by construction. Bytes can only be
/// produced while the version that produces them is still the code in the tree. <c>0.2.0.0-stable</c>
/// is the first release that ships a store at all, it was built from <c>60faf41</c>, and this file
/// was captured through <c>AddAsync</c> at that commit. After a field moves there is no way to make
/// them again except by hand, which is the thing the rule is against.
/// </para>
/// <para>
/// <b>What it is worth today.</b> One shape version exists, so nothing migrates yet and this is a
/// guard on the shape instead: it fails on a change that drops a field out of what is written or
/// renames one, which is a change that silently empties a field on every server that already holds
/// requests. When a second shape version arrives this is the file its migration starts from, and
/// this test is what says the starting bytes are still the ones the shipped version wrote.
/// </para>
/// <para>
/// <b>The two requests are chosen the same way the older fixture's are.</b> One carries something in
/// every field the shape has, so a field lost in a write or in a read is a failure here. One carries
/// nothing but what a new ask has to carry, so a loader that filled an absent field with something
/// is caught as well as one that dropped a present one.
/// </para>
/// <para>
/// <b>What is not claimed.</b> No server wrote this. It was produced by this tree's own store at the
/// commit the package was built from, which is what the rule asks for and is not the same as an
/// installation's file. The commands are in <c>docs/storage.md</c>.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class WhatTheShippedVersionWroteTests : IDisposable
{
    /// <summary>
    /// The queue file the shipped <c>0.2.0.0</c> store wrote.
    /// </summary>
    private const string ShippedFixture = "Storage/Fixtures/version-1-written-by-0.2.0.0-at-60faf41.json";

    /// <summary>
    /// The request that carries something in every field of the shape.
    /// </summary>
    private static readonly Guid TheFullRequest = new Guid("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// The request that carries nothing but what a new ask has to carry.
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
    /// Every field of both requests comes back out of the file the shipped version wrote.
    /// <para>
    /// The fields are checked one at a time rather than by comparing a request built here, because a
    /// request built here would be built from the same belief about the shape that the reader is
    /// built from.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheQueueTheShippedVersionWroteIsReadAndNothingInItIsLost()
    {
        var directory = ADirectory();
        await SeedAsync(directory).ConfigureAwait(true);

        var store = NewStore(directory);
        var held = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, held.Count);

        var full = await store.GetAsync(TheFullRequest, CancellationToken.None).ConfigureAwait(true);
        Assert.NotNull(full);
        Assert.Equal(1, full.Value.Revision);
        Assert.Equal(new Guid("b31d0f9a-5c2e-4a71-8f6b-0d4c3e2a1b58"), full.Value.Request.RequestedByUserId);
        Assert.Equal(RequestedItemKind.Series, full.Value.Request.Kind);
        Assert.Equal("The Conversation, with an accent é", full.Value.Request.DisplayTitle);
        Assert.Equal(1974, full.Value.Request.DisplayYear);
        Assert.Equal("603", full.Value.Request.ProviderIds["Tmdb"]);
        Assert.Equal("tt0071360", full.Value.Request.ProviderIds["Imdb"]);
        Assert.Equal([1, 2], full.Value.Request.Seasons);
        Assert.Equal([new Guid("9c1e5f42-0a7b-4d63-8e19-2f6a3b8c5d70")], full.Value.Request.WantIds);
        Assert.Equal([new Guid("5f0c8a26-3d17-4b94-8e05-9a1b7c2d6e38")], full.Value.Request.JoinedByUserIds);
        Assert.Equal(RequestState.Approved, full.Value.Request.State);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 27, 6, 0, 0, TimeSpan.Zero),
            full.Value.Request.StateChangedAt);
        Assert.Equal(new Guid("2b7b4f1d-4f0e-4a63-9a3d-0f5a1c9e77aa"), full.Value.Request.StateChangedByUserId);
        Assert.Equal("A note the requester wrote.", full.Value.Request.RequesterNote);
        Assert.Equal(LibraryAvailability.Absent, full.Value.Request.Availability);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 27, 6, 30, 0, TimeSpan.Zero),
            full.Value.Request.AvailabilityCheckedAt);
        Assert.NotNull(full.Value.Request.Backend);
        Assert.Equal("overseerr", full.Value.Request.Backend.Service);
        Assert.Equal("4711", full.Value.Request.Backend.Id);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 27, 6, 40, 0, TimeSpan.Zero),
            full.Value.Request.HandoverFailedAt);

        // The history is the field a shape change is most likely to lose, because it is the only one
        // that is a list of records rather than a value. Both of its rows are checked: the arrival,
        // which is the only one carrying a surface, and the move, which is the only one carrying a
        // note.
        Assert.Equal(2, full.Value.Request.History.Count);
        Assert.Equal(RequestState.Open, full.Value.Request.History[0].From);
        Assert.Equal(RequestState.Open, full.Value.Request.History[0].To);
        Assert.Equal(RequestArrival.Seam, full.Value.Request.History[0].Arrival);
        Assert.Equal(RequestActor.Requester, full.Value.Request.History[0].By);
        Assert.Equal(RequestState.Approved, full.Value.Request.History[1].To);
        Assert.Null(full.Value.Request.History[1].Arrival);
        Assert.Equal("An operator's note.", full.Value.Request.History[1].Note);

        var bare = await store.GetAsync(TheBareRequest, CancellationToken.None).ConfigureAwait(true);
        Assert.NotNull(bare);
        Assert.Equal(RequestedItemKind.Movie, bare.Value.Request.Kind);
        Assert.Null(bare.Value.Request.DisplayYear);
        Assert.Empty(bare.Value.Request.ProviderIds);
        Assert.Empty(bare.Value.Request.Seasons);
        Assert.Empty(bare.Value.Request.WantIds);
        Assert.Empty(bare.Value.Request.JoinedByUserIds);
        Assert.Empty(bare.Value.Request.History);
        Assert.Equal(RequestState.Open, bare.Value.Request.State);
        Assert.Null(bare.Value.Request.StateChangedByUserId);
        Assert.Null(bare.Value.Request.RequesterNote);
        Assert.Null(bare.Value.Request.Backend);
        Assert.Null(bare.Value.Request.HandoverFailedAt);
        Assert.Null(bare.Value.Request.AvailabilityCheckedAt);
    }

    /// <summary>
    /// The fixture is the shape version the shipped release wrote and not a later one.
    /// <para>
    /// Without this, a shape change that also regenerated the fixture would leave the test above
    /// green while the bytes it starts from stopped being the shipped version's. The number in the
    /// file is what says which shape it is, so it is read off the bytes rather than through the
    /// loader.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task TheFixtureCarriesTheShapeVersionTheShippedReleaseWrote()
    {
        var bytes = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, ShippedFixture),
            CancellationToken.None).ConfigureAwait(true);

        Assert.StartsWith("{\"Version\":1,", bytes, StringComparison.Ordinal);
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
    /// <returns>The store.</returns>
    private FileRequestStore NewStore(string directory)
    {
        var store = new FileRequestStore(
            directory,
            new RecordingLogger(),
            new TestClock(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero)));

        _stores.Add(store);

        return store;
    }

    /// <summary>
    /// Puts the fixture where a store over that directory will read it.
    /// </summary>
    /// <param name="directory">The store's directory.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    private static async Task SeedAsync(string directory)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, ShippedFixture);

        if (!File.Exists(fixture))
        {
            throw new FileNotFoundException(
                "The queue fixture did not reach the test output, so this test would report on nothing.",
                fixture);
        }

        var bytes = await File.ReadAllBytesAsync(fixture, CancellationToken.None).ConfigureAwait(true);

        await File.WriteAllBytesAsync(
            Path.Combine(directory, FileRequestStore.FileName),
            bytes,
            CancellationToken.None).ConfigureAwait(true);
    }
}

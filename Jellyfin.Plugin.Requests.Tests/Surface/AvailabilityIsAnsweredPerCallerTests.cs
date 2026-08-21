using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Surface;

/// <summary>
/// What a person is told about whether the title they asked for has arrived, and who that answer is
/// computed for. This is #71.
/// <para>
/// The disclosure this guards is narrow, and saying which one it is matters because the wider one is
/// closed elsewhere. Nothing this plugin serves aggregates across people, and a row on somebody's
/// own page names a title they supplied themselves, so the title is not the leak. The leak is the
/// sentence beside it: "the server has this now" about a title in a library the reader cannot open
/// is a statement about that library, and about a rating somebody set for them.
/// </para>
/// <para>
/// So the row's availability is not read off the record. The value on the record is what the server
/// holds, which is what the fulfilment sweep needs and is not a fact this reader is entitled to. The
/// row carries a second answer, asked of the library on the reader's behalf.
/// </para>
/// <para>
/// <b>What no test here reaches is the server applying a rating.</b> The library seam is this
/// plugin's own, for the reason <see cref="Jellyfin.Plugin.Requests.Fulfilment.ILibrary"/> gives,
/// and what turns a user record into a narrower set of items is the server's own query in
/// <c>ServerLibrary</c>, which nothing in this repository runs. What is held here is that the
/// question is asked as the reader and that the answer is believed. <c>docs/surface.md</c> says the
/// same thing in the same words.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class AvailabilityIsAnsweredPerCallerTests
{
    private static readonly DateTimeOffset Started = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Grown = new Guid("71000000-0000-0000-0000-000000000001");
    private static readonly Guid Child = new Guid("71000000-0000-0000-0000-000000000002");

    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// The leg this issue exists for. Two people asked for the same film, the server has it, and one
    /// of them may not see it. The one who may is told it is there, and the one who may not is told
    /// exactly what somebody whose server does not have it is told.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ATitleTheReaderMayNotOpenReadsTheSameAsOneTheServerDoesNotHave()
    {
        var store = new InMemoryRequestStore();
        await AskAsync(store, Grown, "Tmdb", "603").ConfigureAwait(true);
        await AskAsync(store, Child, "Tmdb", "603").ConfigureAwait(true);

        var library = new FakeLibrary();
        library.Put(RequestedItemKind.Movie, "Tmdb", "603");
        library.Hide(Child, RequestedItemKind.Movie, "Tmdb", "603");

        Assert.Equal(LibraryAvailability.Present, await OnlyRowAsync(store, library, Grown).ConfigureAwait(true));
        Assert.Equal(LibraryAvailability.Absent, await OnlyRowAsync(store, library, Child).ConfigureAwait(true));
    }

    /// <summary>
    /// The question is asked on the reader's behalf rather than as the server. Without this leg, a
    /// controller calling the unrestricted lookup would pass the one above on a library that happened
    /// to hold nothing, and would leak on every library that holds something.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheLookupIsMadeAsTheReaderAndNeverAsTheServer()
    {
        var store = new InMemoryRequestStore();
        await AskAsync(store, Child, "Tmdb", "603").ConfigureAwait(true);

        var library = new FakeLibrary();
        library.Put(RequestedItemKind.Movie, "Tmdb", "603");

        await OnlyRowAsync(store, library, Child).ConfigureAwait(true);

        Assert.Equal([Child], library.AskedFor);
    }

    /// <summary>
    /// The row is not the record. A request the sweep has already written
    /// <see cref="LibraryAvailability.Present"/> on still reads as absent to a person who may not
    /// see the title, which is the case a controller trusting the stored value would fail.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheAnswerOnTheRecordIsNotTheAnswerTheReaderIsGiven()
    {
        var store = new InMemoryRequestStore();
        var asked = await AskAsync(store, Child, "Tmdb", "603").ConfigureAwait(true);

        await store.ReplaceAsync(
            asked with { Availability = LibraryAvailability.Present, AvailabilityCheckedAt = Started },
            1,
            CancellationToken.None).ConfigureAwait(true);

        var library = new FakeLibrary();
        library.Put(RequestedItemKind.Movie, "Tmdb", "603");
        library.Hide(Child, RequestedItemKind.Movie, "Tmdb", "603");

        Assert.Equal(LibraryAvailability.Absent, await OnlyRowAsync(store, library, Child).ConfigureAwait(true));
    }

    /// <summary>
    /// A request naming no provider is not looked up at all, and its row says nothing rather than
    /// saying the server has none of it. Nothing can be matched on, so
    /// <see cref="LibraryAvailability.Absent"/> would be the answer of something that looked, and
    /// nothing looked. It is the state the fulfilment sweep leaves such a request in.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARequestNamingNoProviderIsNotLookedUpAndSaysNothing()
    {
        var store = new InMemoryRequestStore();
        await AskAsync(store, Child, provider: null, value: null).ConfigureAwait(true);

        var library = new FakeLibrary();

        Assert.Equal(LibraryAvailability.Unknown, await OnlyRowAsync(store, library, Child).ConfigureAwait(true));
        Assert.Equal(0, library.Lookups);
    }

    /// <summary>
    /// The bound. The lookup is per row and per reader, so what stops it growing with a person's
    /// history is that it is made over the page being returned rather than over everything the store
    /// matched. Five requests, a page of two, two lookups.
    /// <para>
    /// This is the leg that fails if somebody resolves availability before the paging, which is the
    /// natural way to write it and is fine until an install has a year of finished requests in it.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task OnlyTheRowsOnThePageAreLookedUp()
    {
        var store = new InMemoryRequestStore();

        for (var i = 0; i < 5; i++)
        {
            await AskAsync(store, Child, "Tmdb", i.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(true);
        }

        var library = new FakeLibrary();
        var page = Page(await Controller(store, library, Child).MineAsync(
            null,
            null,
            RequestQueryOrder.RequestedAt,
            false,
            0,
            2,
            CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(2, page.Requests.Count);
        Assert.Equal(5, page.MatchCount);
        Assert.Equal(2, library.Lookups);
    }

    /// <summary>
    /// The page, with the status code checked.
    /// </summary>
    /// <param name="answered">What the action returned.</param>
    /// <returns>The page.</returns>
    private static RequestsPage<MyRequest> Page(ActionResult<RequestsPage<MyRequest>> answered)
    {
        var result = Assert.IsType<OkObjectResult>(answered.Result);
        Assert.Equal(200, result.StatusCode);
        return Assert.IsType<RequestsPage<MyRequest>>(result.Value);
    }

    /// <summary>
    /// Puts a request in the store.
    /// </summary>
    /// <param name="store">Where it goes.</param>
    /// <param name="asker">Who asked.</param>
    /// <param name="provider">The provider naming the title, or nothing where the ask carries none.</param>
    /// <param name="value">The identifier under that provider.</param>
    /// <returns>The request as it was stored.</returns>
    private async Task<MediaRequest> AskAsync(
        InMemoryRequestStore store,
        Guid asker,
        string? provider,
        string? value)
    {
        var request = new MediaRequest
        {
            Id = _identifiers.NewId(),
            RequestedByUserId = asker,
            RequestedAt = Started,
            StateChangedAt = Started,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = value ?? "A film nobody named",
            ProviderIds = provider is null || value is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal) { [provider] = value }
        };

        var stored = await store.AddAsync(request, CancellationToken.None).ConfigureAwait(true);
        return stored.Request;
    }

    /// <summary>
    /// What one reader is told about the single request of theirs in the store.
    /// </summary>
    /// <param name="store">Where the requests are.</param>
    /// <param name="library">The library to ask.</param>
    /// <param name="reader">Who is reading.</param>
    /// <returns>The availability on their one row.</returns>
    private async Task<LibraryAvailability> OnlyRowAsync(
        InMemoryRequestStore store,
        FakeLibrary library,
        Guid reader)
    {
        var page = Page(await Controller(store, library, reader).MineAsync(
            null,
            null,
            RequestQueryOrder.RequestedAt,
            false,
            0,
            RequestsController.DefaultPageSize,
            CancellationToken.None).ConfigureAwait(true));

        return page.Requests.Single().Availability;
    }

    /// <summary>
    /// A controller wired to one store, one library and one caller.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="library">The library it asks.</param>
    /// <param name="caller">Who the server says is calling.</param>
    /// <returns>The controller under test.</returns>
    private RequestsController Controller(IRequestStore store, FakeLibrary library, Guid caller)
        => new RequestsController(
            store,
            new TestClock(Started),
            _identifiers,
            new FakeCallerIdentity(caller),
            new FakeInstallSettings(),
            new RecordingJournal(),
            new RecordingSink(),
            new RecordingRequesterNotice(),
            library);
}

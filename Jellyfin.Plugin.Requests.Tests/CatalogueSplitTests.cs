using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Fulfilment;
using Jellyfin.Plugin.Requests.Identity;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Jellyfin.Plugin.Requests.Time;
using Microsoft.AspNetCore.Mvc;
using Xunit;

using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests;

/// <summary>
/// The catalogue is the sibling browsing plugin's, and what this side holds is a title and a year
/// taken when somebody asked, never refreshed. Decided in #92 and written down in
/// <c>docs/seam.md</c>.
/// <para>
/// What that has to survive is a server where nothing outbound resolves: no metadata source
/// reachable, none configured, or none this plugin was ever going to call. The queue is what an
/// operator opens every day, and a queue that needed a source to render would be blank on exactly
/// the server this plugin is most useful on.
/// </para>
/// <para>
/// What these tests do not do is block a socket while they run. Nothing here opens one, and what
/// says so is the exact reference list in <see cref="SiblingIndependenceTests"/>, which holds no
/// networking assembly and fails on any addition. These two legs are the other half: the rows come
/// from the store, and nothing that could fetch can reach the render.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class CatalogueSplitTests
{
    private static readonly Guid Operator = new Guid("d1000000-0000-0000-0000-000000000001");
    private static readonly Guid Asker = new Guid("d1000000-0000-0000-0000-000000000002");

    /// <summary>
    /// The queue renders the title and the year the request was stored with, exactly as stored. The
    /// request here carries a provider identifier and a title that no longer matches what a source
    /// would say about it, which is the case the snapshot exists for: the row reads as what the
    /// person asked for rather than as what anybody would fetch today.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheQueueRendersTheStoredSnapshotAndFetchesNothingToDoIt()
    {
        var store = new InMemoryRequestStore();
        var asked = new DateTimeOffset(2026, 2, 2, 9, 30, 0, TimeSpan.Zero);

        await store.AddAsync(
            new MediaRequest
            {
                Id = new Guid("d2000000-0000-0000-0000-00000000000a"),
                RequestedByUserId = Asker,
                RequestedAt = asked,
                StateChangedAt = asked,
                Kind = RequestedItemKind.Movie,
                DisplayTitle = "The Wages of Fear",
                DisplayYear = 1953,
                ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "1149" }
            },
            CancellationToken.None).ConfigureAwait(true);

        var answered = await Controller(store).QueueAsync(cancellationToken: CancellationToken.None)
            .ConfigureAwait(true);

        var page = Assert.IsType<RequestsPage<QueuedRequest>>(
            Assert.IsAssignableFrom<ObjectResult>(answered.Result).Value);
        var row = Assert.Single(page.Requests);

        Assert.Equal("The Wages of Fear", row.DisplayTitle, StringComparer.Ordinal);
        Assert.Equal(1953, row.DisplayYear);
    }

    /// <summary>
    /// The controller takes a fixed list of things and none of them can fetch anything. A metadata
    /// source arrives as something injected, so the constructor is where one would appear first, and
    /// one more parameter fails here before anybody writes the call.
    /// <para>
    /// Written as the exact list rather than as "nothing called a provider". A name test would pass
    /// the day somebody injects a fetcher under a name nobody predicted, which is the shape this
    /// board already refuses in its reference list.
    /// </para>
    /// </summary>
    [Fact]
    public void NothingThatCouldFetchATitleReachesTheQueue()
    {
        var taken = typeof(RequestsController)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name);

        Assert.Equal(
            string.Join(
                " | ",
                nameof(IRequestStore),
                nameof(IClock),
                nameof(IIdentifierSource),
                nameof(ICallerIdentity),
                nameof(IInstallSettings),
                nameof(IActivityJournal),
                nameof(IOutboundSink),
                nameof(IRequesterNotice),
                nameof(IArrivalNotice),
                nameof(ILibrary),
                nameof(BridgeSubmission)),
            string.Join(" | ", taken),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The plugin references no metadata provider assembly. The reference list in
    /// <see cref="SiblingIndependenceTests"/> would already refuse an addition and would refuse it
    /// as "an unexpected reference"; this one names the reason, and it also refuses the case where
    /// somebody adds the reference to that list and to the project in one change.
    /// </summary>
    [Fact]
    public void NothingThePluginReferencesIsAMetadataSource()
    {
        var sources = typeof(PluginUnderTest).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("MediaBrowser.Providers", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(sources);
    }

    /// <summary>
    /// A controller reading one store, as an administrator.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <returns>The controller under test.</returns>
    private static RequestsController Controller(IRequestStore store)
        => new RequestsController(
            store,
            new TestClock(new DateTimeOffset(2026, 8, 11, 8, 0, 0, TimeSpan.Zero)),
            new SequentialIdentifierSource(),
            new FakeCallerIdentity(Operator),
            new FakeInstallSettings(),
            new RecordingJournal(),
            new RecordingSink(),
            new RecordingRequesterNotice(),
            new RecordingArrivalNotice(),
            new FakeLibrary(),
            ABridgeSubmission.WithNothingBehindIt(store));
}

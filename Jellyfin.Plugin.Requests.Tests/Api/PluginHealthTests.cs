using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Fulfilment;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.AspNetCore.Mvc;
using Xunit;

using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests.Api;

/// <summary>
/// Whether an operator can tell a broken plugin from a quiet week without opening the server log.
/// <para>
/// The four facts are the ones #63 names: the counts, the last sweep, the bridge and the last store
/// write. Each is asserted against the endpoint, and the page is read as the assembly carries it,
/// which is the bound every check over these assets has: they read the page, they do not run it.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class PluginHealthTests
{
    private static readonly Guid Operator = new Guid("f1000000-0000-0000-0000-0000000000ad");
    private static readonly Guid Asker = new Guid("f1000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Started = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Every state is counted, and a state nothing is in is answered as a zero rather than left out.
    /// <para>
    /// The zero is the point. A page drawing only what the store answered would show a shorter list
    /// on a quieter server, and an operator comparing today against yesterday would be comparing two
    /// different tables.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EveryStateIsCountedAndAnEmptyOneIsAZero()
    {
        var store = new InMemoryRequestStore();

        await AddAsync(store, "a", "Solaris").ConfigureAwait(true);
        var second = await AddAsync(store, "b", "Stalker").ConfigureAwait(true);
        await MoveAsync(store, second, RequestState.Approved).ConfigureAwait(true);

        var health = Answered(await HealthFor(store).HealthAsync(CancellationToken.None).ConfigureAwait(true));

        Assert.True(health.StoreReadable);
        Assert.Equal(Enum.GetValues<RequestState>().Length, health.Counts.Count);
        Assert.Equal(1, health.Counts[RequestState.Open]);
        Assert.Equal(1, health.Counts[RequestState.Approved]);
        Assert.Equal(0, health.Counts[RequestState.Declined]);
        Assert.Equal(0, health.Counts[RequestState.Fulfilled]);
        Assert.Equal(0, health.Counts[RequestState.Failed]);
    }

    /// <summary>
    /// A store that cannot be read answers 200 with the flag turned off, rather than taking the
    /// panel down.
    /// <para>
    /// This is the fault that makes every other number here meaningless, so it has to be reported
    /// and cannot be a refusal: an endpoint that failed when the plugin is unhealthy would be silent
    /// at exactly the moment somebody is reading it to find out why.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AStoreThatCannotBeReadIsAFieldAndNeverARefusal()
    {
        var health = Answered(
            await HealthFor(new StoreThatCannotBeRead()).HealthAsync(CancellationToken.None).ConfigureAwait(true));

        Assert.False(health.StoreReadable);
        Assert.Equal(Enum.GetValues<RequestState>().Length, health.Counts.Count);
        Assert.All(health.Counts.Values, count => Assert.Equal(0, count));
    }

    /// <summary>
    /// The store says when it last wrote, and a store on which nothing has been written says so
    /// rather than naming a moment.
    /// <para>
    /// Asserted against the real store rather than against a double, because the double records
    /// nothing and would prove the endpoint reads a property somebody set in the test.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheStoreSaysWhenItLastWroteAndSaysNothingBeforeItHas()
    {
        var directory = TestRunDirectory.CreateSubdirectory();
        var clock = new TestClock(Started);

        try
        {
            using var store = new FileRequestStore(directory, new RecordingLogger(), clock);

            Assert.Null(store.LastWrittenAt);

            await store.AddAsync(AnAsk("a", "Solaris"), CancellationToken.None).ConfigureAwait(true);

            Assert.Equal(Started, store.LastWrittenAt);

            clock.Advance(TimeSpan.FromMinutes(5));
            await store.AddAsync(AnAsk("b", "Stalker"), CancellationToken.None).ConfigureAwait(true);

            Assert.Equal(Started.AddMinutes(5), store.LastWrittenAt);
        }
        finally
        {
            TestRunDirectory.Remove(directory);
        }
    }

    /// <summary>
    /// The sweep says what its last full run did, and says nothing before one has run.
    /// <para>
    /// A sweep that found nothing and a sweep that never ran look identical from the outside, and
    /// they are the two answers an operator most needs separated when requests have stopped moving.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheSweepSaysWhatItsLastRunDidAndSaysNothingBeforeOneHasRun()
    {
        var store = new InMemoryRequestStore();
        await AddAsync(store, "a", "Solaris").ConfigureAwait(true);
        await AddAsync(store, "b", "Stalker").ConfigureAwait(true);

        var sweep = SweepOver(store, new TestClock(Started));
        var health = HealthFor(store, sweep: sweep);

        Assert.Null(Answered(await health.HealthAsync(CancellationToken.None).ConfigureAwait(true)).LastSweepAt);

        await sweep.SweepAsync(CancellationToken.None).ConfigureAwait(true);

        var after = Answered(await health.HealthAsync(CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(Started, after.LastSweepAt);
        Assert.Equal(2, after.LastSweepExamined);
        Assert.Equal(0, after.LastSweepFulfilled);
    }

    /// <summary>
    /// A bridge that is not answering is on this answer as unreachable, with the moment this server
    /// last saw it answer.
    /// <para>
    /// This is the second condition of #63 and it is the one the shipped bridge cannot produce: the
    /// only implementation an install resolves is the one for a server with no service, so it
    /// answers <see cref="BackendReachability.NotConfigured"/> and can never be unreachable. What is
    /// measured here is that the endpoint carries the failure when something produces one, using a
    /// backend that does, and what is not measured is any real service refusing a connection.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ABridgeThatStoppedAnsweringSaysSoAndSaysWhenItLastDid()
    {
        var store = new InMemoryRequestStore();
        var backend = new ABridgeThatStopsAnswering(BackendReachability.Reachable);
        var clock = new TestClock(Started);
        var health = HealthFor(store, backend: backend, clock: clock);

        var reachable = Answered(await health.HealthAsync(CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(BackendReachability.Reachable, reachable.Bridge);
        Assert.Equal(Started, reachable.BridgeLastReachableAt);

        clock.Advance(TimeSpan.FromHours(2));
        backend.Answering(BackendReachability.Unreachable);

        var gone = Answered(await health.HealthAsync(CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(BackendReachability.Unreachable, gone.Bridge);
        Assert.Equal(Started, gone.BridgeLastReachableAt);
    }

    /// <summary>
    /// An install with no service is not a fault, and is answered as its own value rather than as an
    /// unreachable one.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnInstallWithNoServiceIsNotAFailure()
    {
        var health = Answered(
            await HealthFor(new InMemoryRequestStore()).HealthAsync(CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(BackendReachability.NotConfigured, health.Bridge);
        Assert.Null(health.BridgeLastReachableAt);
    }

    /// <summary>
    /// Nothing this answer can carry is a credential or a path.
    /// <para>
    /// Over the shape rather than over one answer's bytes. Every property is a count, a moment, a
    /// switch or the reachability enumeration, so there is no field a secret or a file name could
    /// arrive in, and a field of a type that could carry one fails here on the day it is added
    /// rather than on the day somebody reads a page.
    /// </para>
    /// </summary>
    [Fact]
    public void NothingOnThisAnswerCouldCarryACredentialOrAPath()
    {
        var carried = typeof(PluginHealth)
            .GetProperties()
            .Select(property => property.Name + ":" + Named(property.PropertyType))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "Bridge:BackendReachability",
                "BridgeLastReachableAt:DateTimeOffset?",
                "Counts:IReadOnlyDictionary<RequestState,Int32>",
                "LastStoreWriteAt:DateTimeOffset?",
                "LastSweepAt:DateTimeOffset?",
                "LastSweepExamined:Int32?",
                "LastSweepFulfilled:Int32?",
                "StoreReadable:Boolean"
            ],
            carried);
    }

    /// <summary>
    /// The page draws all four facts and asks for them from this plugin's own endpoint.
    /// <para>
    /// What this refuses is a health endpoint that lands and a page that goes on showing what it
    /// showed before, which builds clean, formats clean and leaves the operator reading the log.
    /// </para>
    /// </summary>
    [Fact]
    public void TheQueuePageDrawsAllFourFacts()
    {
        var page = Asset("Web.queue.html");

        foreach (var drawn in new[]
        {
            "RequestsShell.get(\"Health\"",
            "answer.Counts",
            "answer.LastStoreWriteAt",
            "answer.LastSweepAt",
            "answer.Bridge",
            "answer.BridgeLastReachableAt",
            "answer.StoreReadable",
            "queue.health.heading"
        })
        {
            Assert.Contains(drawn, page, StringComparison.Ordinal);
        }

        // And the panel is read on the turn that draws the rows. Without this the whole of the
        // above passes on a page that carries the code and never runs it, which is the shape a
        // change removing one line leaves behind.
        var reading = Between(page, "function read() {", "\n                    }");

        Assert.Contains("return health();", reading, StringComparison.Ordinal);
    }

    /// <summary>
    /// One block of the page, from a marker to the end of what it opens.
    /// </summary>
    /// <param name="body">The page.</param>
    /// <param name="opens">What the block starts with.</param>
    /// <param name="closes">What ends it.</param>
    /// <returns>The block, without either marker.</returns>
    private static string Between(string body, string opens, string closes)
    {
        var at = body.IndexOf(opens, StringComparison.Ordinal);

        Assert.True(at >= 0, opens + " is not in the page.");

        var from = at + opens.Length;
        var to = body.IndexOf(closes, from, StringComparison.Ordinal);

        Assert.True(to >= 0, closes + " does not close " + opens + " in the page.");

        return body[from..to];
    }

    /// <summary>
    /// Every sentence the panel draws comes from the catalogue, and none is written into the page.
    /// <para>
    /// The rule is #73's and it is asserted here for the words this change adds, because a panel is
    /// where a sentence is most tempting to type: the strings are prose rather than column headings
    /// and there are more of them than anywhere else on either page.
    /// </para>
    /// </summary>
    [Fact]
    public void EverySentenceThePanelDrawsIsAKeyRatherThanText()
    {
        var page = Asset("Web.queue.html");
        var words = Asset("Localisation.Strings.en.json");

        foreach (var key in new[]
        {
            "queue.health.heading",
            "queue.health.count",
            "queue.health.storeUnreadable",
            "queue.health.lastWrite",
            "queue.health.lastWriteNone",
            "queue.health.lastSweep",
            "queue.health.lastSweepNone",
            "queue.health.bridgeLastReachable",
            "queue.health.bridgeNeverReachable",
            "queue.health.notRead"
        })
        {
            Assert.Contains(key, page, StringComparison.Ordinal);
            Assert.Contains(key, words, StringComparison.Ordinal);
        }

        // The three bridge answers are drawn through the group lookup rather than named one by one,
        // so the keys are in the catalogue and the page holds only the prefix.
        Assert.Contains("RequestsShell.named(\"queue.health.bridge\"", page, StringComparison.Ordinal);

        foreach (var value in Enum.GetNames<BackendReachability>())
        {
            Assert.Contains("queue.health.bridge." + value, words, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The answer, with the status code checked.
    /// </summary>
    /// <param name="answered">What the action returned.</param>
    /// <returns>The answer.</returns>
    private static PluginHealth Answered(ActionResult<PluginHealth> answered)
    {
        var result = Assert.IsType<OkObjectResult>(answered.Result);
        Assert.Equal(200, result.StatusCode);
        return Assert.IsType<PluginHealth>(result.Value);
    }

    /// <summary>
    /// The health endpoint over one store.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="backend">The bridge, or the one with nothing behind it.</param>
    /// <param name="sweep">The sweep, or one that has not run.</param>
    /// <param name="clock">The clock, or one standing at the start.</param>
    /// <returns>The controller under test.</returns>
    private static HealthController HealthFor(
        IRequestStore store,
        IRequestBackend? backend = null,
        FulfilmentSweep? sweep = null,
        TestClock? clock = null)
        => new HealthController(
            store,
            backend ?? new NoRequestBackend(),
            sweep ?? SweepOver(store, new TestClock(Started)),
            new BridgeWatch(),
            clock ?? new TestClock(Started));

    /// <summary>
    /// A sweep over one store, looking at a library that holds nothing.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="clock">What the moment of a run is read from.</param>
    /// <returns>The sweep.</returns>
    private static FulfilmentSweep SweepOver(IRequestStore store, TestClock clock)
        => new FulfilmentSweep(store, new FakeLibrary(), clock, new RecordingJournal(), new RecordingSink(), new RecordingLogger());

    /// <summary>
    /// One request, added to the store.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="tail">What makes this request's identifier distinct.</param>
    /// <param name="title">The title.</param>
    /// <returns>The identifier of the request that was added.</returns>
    private static async Task<Guid> AddAsync(InMemoryRequestStore store, string tail, string title)
    {
        var stored = await store.AddAsync(AnAsk(tail, title), CancellationToken.None).ConfigureAwait(false);
        return stored.Request.Id;
    }

    /// <summary>
    /// A request as somebody asking builds one.
    /// </summary>
    /// <param name="tail">What makes the identifier distinct.</param>
    /// <param name="title">The title.</param>
    /// <returns>The request.</returns>
    private static MediaRequest AnAsk(string tail, string title) => new MediaRequest
    {
        Id = new Guid("f200000" + tail + "-0000-0000-0000-000000000000"),
        RequestedByUserId = Asker,
        RequestedAt = Started,
        StateChangedAt = Started,
        Kind = RequestedItemKind.Movie,
        DisplayTitle = title,
        ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = title }
    };

    /// <summary>
    /// Moves a request, as an operator would.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="id">The request being moved.</param>
    /// <param name="to">Where it is going.</param>
    /// <returns>A task that completes when the write has been made.</returns>
    private static async Task MoveAsync(InMemoryRequestStore store, Guid id, RequestState to)
    {
        var held = await store.GetAsync(id, CancellationToken.None).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The store holds no request under {id}.");

        var moved = RequestLifecycle.Move(held.Request, to, Started.AddMinutes(1), RequestCaller.Administrator(Operator));

        await store.ReplaceAsync(moved, held.Revision, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// A type as a reader would write it, so a property that started carrying something else is
    /// visible in the comparison rather than hidden behind a framework name.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The name.</returns>
    private static string Named(Type type)
    {
        var under = Nullable.GetUnderlyingType(type);

        if (under is not null)
        {
            return Named(under) + "?";
        }

        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];

        return name + "<" + string.Join(",", type.GetGenericArguments().Select(Named)) + ">";
    }

    /// <summary>
    /// One embedded asset, as the assembly carries it.
    /// </summary>
    /// <param name="ending">What the resource name ends with.</param>
    /// <returns>The asset.</returns>
    private static string Asset(string ending)
    {
        var assembly = typeof(PluginUnderTest).Assembly;

        var resource = assembly
            .GetManifestResourceNames()
            .Single(name => name.EndsWith(ending, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The assembly carries no resource named {resource}.");

        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }
}

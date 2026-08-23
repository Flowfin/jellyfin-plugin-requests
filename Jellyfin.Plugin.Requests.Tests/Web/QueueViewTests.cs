using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.AspNetCore.Mvc;
using Xunit;

using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests.Web;

/// <summary>
/// What the queue page asks the server for, read out of the page the assembly carries and then
/// asked of the endpoint itself.
/// <para>
/// The failure these exist against is a page that narrows a queue in the browser. It looks right on
/// a store with forty requests and is unusable on one with ten thousand, because the browser is
/// being handed the whole queue in order to show fifty rows of it, and nothing earlier says so: the
/// build is clean, the formatter is happy, and the page renders.
/// </para>
/// <para>
/// The page is read as the assembly carries it rather than as the working tree holds it, for the
/// reason <see cref="PageRulesTests"/> gives: what a server serves is the embedded copy.
/// </para>
/// <para>
/// The bound is worth stating. These read the page's source and drive the endpoint; they do not run
/// the page. Whether a browser draws what the answer holds is behaviour, and driving a browser to
/// ask is what <c>docs/testing.md</c> refuses.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class QueueViewTests
{
    /// <summary>
    /// How many requests the queue is asked to hold for the size leg. Ten thousand is the number
    /// the store's own cost bounds are stated at, so both sides of this plugin are measured against
    /// one size rather than against two nobody compared.
    /// </summary>
    private const int Records = 10_000;

    /// <summary>
    /// The view an operator gets before touching anything, as the four controls select it.
    /// <para>
    /// The kind opens on an empty value, which is the filter the page leaves out of the question
    /// rather than sends, and the endpoint reads an absent parameter as the queue not narrowed on
    /// that field.
    /// </para>
    /// </summary>
    private static readonly (string Control, string Value)[] Opening =
    [
        ("RequestsQueueState", "Open"),
        ("RequestsQueueKind", string.Empty),
        ("RequestsQueueOrder", "RequestedAt"),
        ("RequestsQueueDescending", "false")
    ];

    /// <summary>
    /// The ways a page does for itself what it asked the server to do. Each is written as it is
    /// called on an array, so the words in the prose around them are not what is being read.
    /// </summary>
    private static readonly string[] TheServersWork =
    [
        ".sort(",
        ".filter(",
        ".slice(",
        ".splice(",
        ".reverse("
    ];

    /// <summary>
    /// The two parts of the question the pager moves rather than an operator. They have no control
    /// on the page, and naming them is what stops the leg that looks for controls from passing on a
    /// page that lost one.
    /// </summary>
    private static readonly string[] ThePagersOwn = ["skip", "take"];

    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// The queue opens on open requests, oldest first.
    /// <para>
    /// Read off the options the page marks as selected, because that is what a browser starts the
    /// controls on and therefore what the first question is built from. The one-character mistake
    /// this is aimed at is <c>selected</c> moving one line, which turns the queue an operator opens
    /// every day into a list of things already dealt with and breaks nothing else.
    /// </para>
    /// </summary>
    [Fact]
    public void TheQueueOpensOnOpenRequestsOldestFirst()
    {
        var body = QueuePage();

        foreach (var (control, value) in Opening)
        {
            Assert.Equal(value, SelectedValueOf(body, control));
        }

        Assert.Equal(0, BoundOf(body, "skip"));
    }

    /// <summary>
    /// Every part of the question the page asks is a parameter the endpoint declares.
    /// <para>
    /// The endpoint's side is reflected rather than restated, so this is a comparison between the
    /// page and the API rather than between the page and a list somebody kept up to date. A control
    /// the endpoint cannot answer is a control whose only possible implementation is work done in
    /// the browser, and that is the shape this refuses before it is written.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryPartOfTheQuestionTheQueueAsksIsAParameterTheEndpointDeclares()
    {
        var declared = typeof(RequestsController)
            .GetMethod(nameof(RequestsController.QueueAsync))!
            .GetParameters()
            .Select(parameter => parameter.Name!)
            .Where(name => !string.Equals(name, "cancellationToken", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var page = AskedBy(QueuePage()).Order(StringComparer.Ordinal).ToArray();

        Assert.NotEmpty(declared);
        Assert.Equal(declared, page, StringComparer.Ordinal);
    }

    /// <summary>
    /// Each narrowing the page offers is read from a control on the page.
    /// <para>
    /// Without this the leg above passes on a page that names the endpoint's parameters and sends a
    /// value it decided itself, which is a filter an operator cannot see and cannot change.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryNarrowingTheQueueOffersIsReadFromAControlOnThePage()
    {
        var body = QueuePage();

        var withoutAControl = AskedBy(body)
            .Where(name => !body.Contains(IdOf(name), StringComparison.Ordinal))
            .ToArray();

        // The two the pager moves are the page's own and have no control: they are asserted here so
        // that a control disappearing does not leave this leg looking at a shorter list.
        Assert.Equal(ThePagersOwn, withoutAControl, StringComparer.Ordinal);
    }

    /// <summary>
    /// The page does none of the work it asked the server for.
    /// <para>
    /// Every one of these is how a queue gets narrowed, ordered or paged in a browser, and each is
    /// available on an array of rows the page has already been handed. What makes them refusable is
    /// that the page has no honest use for any of them: what it draws is the answer, in the order
    /// the answer came in.
    /// </para>
    /// </summary>
    [Fact]
    public void TheQueuePageDoesNoneOfTheWorkItAskedTheServerFor()
    {
        var body = QueuePage();

        var doneHere = TheServersWork
            .Where(work => body.Contains(work, StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(body);
        Assert.Empty(doneHere);
    }

    /// <summary>
    /// A queue of ten thousand requests is one page of rows to whoever is reading it.
    /// <para>
    /// The question is the page's own, parsed out of it rather than written here, so this measures
    /// what a browser opening the queue actually receives. What comes back is the page size and the
    /// count of everything that matched, which is what a pager is drawn from: the browser is told
    /// how many there are without being sent them.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AQueueOfTenThousandIsOnePageOfRowsToTheBrowser()
    {
        var body = QueuePage();
        var store = new InMemoryRequestStore();

        var asked = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        for (var made = 0; made < Records; made++)
        {
            await store.AddAsync(
                new MediaRequest
                {
                    Id = _identifiers.NewId(),
                    RequestedByUserId = new Guid("c1000000-0000-0000-0000-000000000001"),
                    RequestedAt = asked.AddMinutes(made),
                    StateChangedAt = asked.AddMinutes(made),
                    Kind = RequestedItemKind.Movie,
                    DisplayTitle = made.ToString(CultureInfo.InvariantCulture),
                    ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["Tmdb"] = made.ToString(CultureInfo.InvariantCulture)
                    }
                },
                CancellationToken.None).ConfigureAwait(true);
        }

        var take = BoundOf(body, "take");

        var controller = new RequestsController(
            store,
            new TestClock(asked),
            _identifiers,
            new FakeCallerIdentity(new Guid("c1000000-0000-0000-0000-0000000000ad")),
            new FakeInstallSettings(),
            new RecordingJournal(),
            new RecordingSink(),
            new RecordingRequesterNotice(),
            new RecordingArrivalNotice(),
            new FakeLibrary(),
            ABridgeSubmission.WithNothingBehindIt(store));

        var answered = await controller.QueueAsync(
            [Enum.Parse<RequestState>(SelectedValueOf(body, "RequestsQueueState"), false)],
            null,
            Enum.Parse<RequestQueryOrder>(SelectedValueOf(body, "RequestsQueueOrder"), false),
            bool.Parse(SelectedValueOf(body, "RequestsQueueDescending")),
            BoundOf(body, "skip"),
            take,
            CancellationToken.None).ConfigureAwait(true);

        var result = Assert.IsType<OkObjectResult>(answered.Result);
        var answer = Assert.IsType<RequestsPage<QueuedRequest>>(result.Value);

        Assert.Equal(take, answer.Requests.Count);
        Assert.Equal(Records, answer.MatchCount);
        Assert.Equal(take, answer.Take);

        // Oldest first, which is the half of the opening view no reading of the markup can show.
        Assert.Equal(asked.AddMinutes(0), answer.Requests[0].RequestedAt);
        Assert.Equal(asked.AddMinutes(take - 1), answer.Requests[take - 1].RequestedAt);
        Assert.All(answer.Requests, row => Assert.Equal(RequestState.Open, row.State));
    }

    /// <summary>
    /// The identifier the page gives the control that answers one part of the question.
    /// </summary>
    /// <param name="asked">The endpoint parameter's name.</param>
    /// <returns>The element identifier the page uses for it.</returns>
    private static string IdOf(string asked)
        => "RequestsQueue" + char.ToUpperInvariant(asked[0]) + asked[1..];

    /// <summary>
    /// The value of the option a select starts on.
    /// </summary>
    /// <param name="body">The page as it is embedded.</param>
    /// <param name="id">The select's identifier.</param>
    /// <returns>The value the browser would send before anybody touches it.</returns>
    private static string SelectedValueOf(string body, string id)
    {
        var at = body.IndexOf("id=\"" + id + "\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "The page carries no control with the identifier " + id + ".");

        var end = body.IndexOf("</select>", at, StringComparison.Ordinal);
        Assert.True(end >= 0, "The control " + id + " is not a select the page closes.");

        var options = body[at..end].Split("<option ", StringSplitOptions.RemoveEmptyEntries);

        var selected = options
            .Where(option => option.Contains("selected", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(selected);

        return ValueOf(selected[0]);
    }

    /// <summary>
    /// What one option carries as its value.
    /// </summary>
    /// <param name="option">The option, from its attributes onwards.</param>
    /// <returns>The value, which is empty for the option that narrows nothing.</returns>
    private static string ValueOf(string option)
    {
        const string Marker = "value=\"";

        var at = option.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at >= 0, "An option the page marks as selected carries no value.");

        var start = at + Marker.Length;

        return option[start..option.IndexOf('"', start)];
    }

    /// <summary>
    /// Every part of the question the page sends, read out of the page's own list of them.
    /// </summary>
    /// <param name="body">The page as it is embedded.</param>
    /// <returns>The parameter names, in the order the page writes them.</returns>
    private static IEnumerable<string> AskedBy(string body)
    {
        const string Marker = "var asked = [";

        var at = body.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at >= 0, "The page does not say what it asks for.");

        var start = at + Marker.Length;
        var list = body[start..body.IndexOf(']', start)];

        return list
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim().Trim('"'))
            .Where(name => name.Length > 0);
    }

    /// <summary>
    /// One of the two numbers the pager moves, read out of the page.
    /// </summary>
    /// <param name="body">The page as it is embedded.</param>
    /// <param name="name">Which of them.</param>
    /// <returns>What the page opens on.</returns>
    private static int BoundOf(string body, string name)
    {
        const string Marker = "var bounds = {";

        var at = body.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at >= 0, "The page does not say where its first page starts or how big it is.");

        var start = at + Marker.Length;
        var written = body[start..body.IndexOf('}', start)];

        var named = written.IndexOf(name + ":", StringComparison.Ordinal);
        Assert.True(named >= 0, "The page's bounds do not name " + name + ".");

        var digits = written[(named + name.Length + 1)..]
            .TrimStart()
            .TakeWhile(char.IsAsciiDigit)
            .ToArray();

        return int.Parse(new string(digits), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The queue page as the built assembly carries it.
    /// </summary>
    /// <returns>The page, inline script and all.</returns>
    private static string QueuePage()
    {
        var assembly = typeof(PluginUnderTest).Assembly;

        var resource = assembly
            .GetManifestResourceNames()
            .Single(name => name.EndsWith("Web.queue.html", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The assembly carries no resource named {resource}.");

        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Localisation;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.AspNetCore.Mvc;
using Xunit;

using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests.Surface;

/// <summary>
/// What a person is told when the answer is no, or not yet.
/// <para>
/// Three ordinary outcomes are most of what somebody experiences and all three are easy to leave
/// as a state name or a status code: the thing is waiting, the answer was no, and they are already
/// waiting for as many things as this install allows. None of those is an error, and a person who
/// meets one of them without a sentence goes and asks the operator, which is the message this
/// plugin exists to remove.
/// </para>
/// <para>
/// So the legs below hold two properties rather than the wording. Each outcome produces its own
/// sentence rather than a generic failure, and the sentence is written in one place: the catalogue
/// holds it, everything that shows it looks it up, and no asset carries a copy. The wording itself
/// is not asserted anywhere, because a sentence somebody improves would then be a red suite for a
/// reason nobody cares about.
/// </para>
/// <para>
/// <b>The bound, and it is the same one every check over these assets carries.</b> Nothing here
/// runs a browser, which the headless rule in <c>docs/testing.md</c> settles, so what is held of
/// the page is that it looks the sentence up and never that a person saw it rendered.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class WhatAPersonIsToldTests
{
    private static readonly Guid Asker = new Guid("70000000-0000-0000-0000-000000000001");

    /// <summary>
    /// The assets a person's words could be written into, relative to the plugin's own namespace.
    /// The catalogue is not among them on purpose: it is where the sentence belongs.
    /// </summary>
    private static readonly string[] Assets =
    [
        "Web.mine.html",
        "Web.queue.html",
        "Web.shell.js",
        "Configuration.configPage.html"
    ];

    private readonly SequentialIdentifierSource _identifiers = new SequentialIdentifierSource();

    /// <summary>
    /// Somebody at their quota is answered with the sentence the catalogue carries, filled in with
    /// their own two numbers, rather than with a status code and whatever a client makes of it.
    /// <para>
    /// The two numbers are the caller's own and say nothing about anybody else's queue. What the
    /// leg holds is that both of them reached the sentence, because a message that says a limit was
    /// reached without saying what the limit is leaves a person with nothing to do but try again.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SomebodyAtTheirQuotaIsToldTheSentenceRatherThanAGenericFailure()
    {
        var store = new InMemoryRequestStore();
        var controller = ControllerFor(store, allowed: 1);
        var sentence = Sentence(Sentences.AtTheirQuota);

        await controller.CreateAsync(AFilm("The Conversation", "1601"), CancellationToken.None).ConfigureAwait(true);

        var refused = Refusal(
            await controller.CreateAsync(AFilm("The Wages of Fear", "1149"), CancellationToken.None)
                .ConfigureAwait(true));

        // Both placeholders are asserted to be in the catalogue's own string before the comparison
        // below, which is otherwise satisfied by a sentence that had one of them dropped out of it.
        Assert.Contains("{0}", sentence, StringComparison.Ordinal);
        Assert.Contains("{1}", sentence, StringComparison.Ordinal);

        Assert.Equal(RequestFailureCode.TheyAreAtTheirQuota, refused.Code);
        Assert.Equal(
            sentence.Replace("{0}", "1", StringComparison.Ordinal).Replace("{1}", "1", StringComparison.Ordinal),
            refused.Message,
            StringComparer.Ordinal);

        // Nothing was written, so the sentence and the queue agree with each other.
        Assert.Single(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>
    /// The two outcomes a person meets while reading their own requests are looked up by key on the
    /// page that draws them.
    /// <para>
    /// A row shows where a request stands in one column and what happens next in another, and the
    /// second is the one that keeps somebody from asking the operator what the first one means. The
    /// third outcome is met while asking rather than while reading, so it has no row and is the leg
    /// above.
    /// </para>
    /// </summary>
    [Fact]
    public void ThePageLooksUpTheSentenceForEachOutcomeARowCanBeIn()
    {
        var body = Asset("Web.mine.html");

        foreach (var key in new[] { Sentences.Waiting, Sentences.Declined })
        {
            Assert.Matches(
                new Regex(@"\bword\(""" + Regex.Escape(key) + @"""\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5)),
                body);
        }
    }

    /// <summary>
    /// No asset carries one of these sentences itself.
    /// <para>
    /// This is the half that keeps "written once" true after the change that introduces it. A
    /// sentence pasted into a page is invisible to a translator and reaches whoever is reading that
    /// one surface in English however much else has been translated, and the two copies then move
    /// apart quietly, because nothing about a page and an endpoint saying slightly different things
    /// looks like a fault.
    /// </para>
    /// </summary>
    [Fact]
    public void NoAssetCarriesOneOfTheseSentencesItself()
    {
        var sentences = Declared().Select(Sentence).ToArray();

        var written = Assets
            .SelectMany(asset => sentences
                .Where(sentence => Asset(asset).Contains(sentence, StringComparison.Ordinal))
                .Select(sentence => asset + ": " + sentence))
            .ToArray();

        Assert.NotEmpty(sentences);
        Assert.Empty(written);
    }

    /// <summary>
    /// One sentence, as the English catalogue carries it.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The sentence.</returns>
    private static string Sentence(string key) => StringCatalogue.Shipped.Get(key, culture: null);

    /// <summary>
    /// The keys of the three sentences, read off the one place they are declared rather than
    /// listed here, so a fourth is covered the first time the suite runs.
    /// </summary>
    /// <returns>The keys.</returns>
    private static string[] Declared()
        => [.. typeof(Sentences)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)];

    /// <summary>
    /// One asset as the built assembly carries it, which is the copy a server serves.
    /// </summary>
    /// <param name="resource">The resource, relative to the plugin's namespace.</param>
    /// <returns>The asset.</returns>
    private static string Asset(string resource)
    {
        var assembly = typeof(PluginUnderTest).Assembly;
        var name = typeof(PluginUnderTest).Namespace + "." + resource;

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The assembly carries no resource named {name}.");

        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// The refusal, with the status code checked, so a leg cannot pass on a body that came back
    /// under the wrong one.
    /// </summary>
    /// <param name="answered">What the action returned.</param>
    /// <returns>The refusal.</returns>
    private static RequestFailure Refusal(ActionResult<CreatedRequest> answered)
    {
        var result = Assert.IsAssignableFrom<ObjectResult>(answered.Result);
        var failure = Assert.IsType<RequestFailure>(result.Value);

        Assert.Equal(RequestFailure.StatusFor(failure.Code), result.StatusCode);

        return failure;
    }

    /// <summary>
    /// A film, named by one provider so it has an identity of its own.
    /// </summary>
    /// <param name="title">What it is called.</param>
    /// <param name="tmdb">Its identifier at the one source used here.</param>
    /// <returns>The body.</returns>
    private static CreateRequestBody AFilm(string title, string tmdb)
        => new CreateRequestBody
        {
            Kind = RequestedItemKind.Movie,
            Title = title,
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = tmdb }
        };

    /// <summary>
    /// A controller for one person, on an install that allows them a given number of open requests.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="allowed">How many they may be waiting for at once.</param>
    /// <returns>The controller under test.</returns>
    private RequestsController ControllerFor(IRequestStore store, int allowed)
        => new RequestsController(
            store,
            new TestClock(new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero)),
            _identifiers,
            new FakeCallerIdentity(Asker),
            new FakeInstallSettings(new PluginConfiguration { OpenRequestsPerUser = allowed }),
            new RecordingJournal(),
            new RecordingSink(),
            new RecordingRequesterNotice(),
            new RecordingArrivalNotice(),
            new FakeLibrary(),
            ABridgeSubmission.WithNothingBehindIt(store));
}

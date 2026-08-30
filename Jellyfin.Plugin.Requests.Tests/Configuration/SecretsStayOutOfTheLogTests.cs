using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Configuration;

/// <summary>
/// A setting marked <see cref="SecretAttribute"/> against everything this plugin writes for somebody
/// else to read.
/// <para>
/// Operators paste logs into issue trackers, so the log has to be safe to paste, and since #318 the
/// settings page cannot show the outbound address back at all - a log line would be the only place
/// it could be read. The two ways it left were measured on #100 and they are different in kind: one
/// is a sentence this tree writes, and one is a sentence the platform writes, which no wording here
/// can fix.
/// </para>
/// <para>
/// The address used below is not the one the other suites use. It carries a host nothing else in
/// this repository contains, so a leg that finds it in a log has found the value rather than a word
/// that happens to appear.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class SecretsStayOutOfTheLogTests
{
    private const string Host = "sink-nobody-else-names.invalid";
    private const string Address = "https://" + Host + "/hook?token=THE-PART-THAT-AUTHENTICATES";

    private static readonly Guid Asker = new Guid("7a000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A send that fails writes nothing of the address, at any level, in a message or in an
    /// exception carried beside one.
    /// <para>
    /// This is the half no sentence in this tree could fix. Nothing on the sink's failure path names
    /// the address; the platform writes the host and the port into the exception it raises, and
    /// handing that object to the logger writes it into the log. The endpoint double refuses the
    /// connection the same way, naming the destination in its own message, so this leg asserts
    /// against the shape the field produces rather than against a friendlier one.
    /// </para>
    /// <para>
    /// The logger takes every level, so what is read here is the log at its most verbose rather than
    /// what a default level would have kept.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AFailedSendWritesNoPartOfTheAddressAtAnyLevel()
    {
        var endpoint = ASinkEndpoint.ThatRefusesTheConnection();
        var log = new RecordingLogger();
        using var sink = Sink(endpoint, Address, log);

        sink.Announce(Notice());
        await sink.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        // The failure did reach the log. Without this the leg passes on a sink that reported nothing
        // at all, which is the wrong way to keep an address out of one.
        Assert.Contains(log.Lines, line => line.Message.Contains("could not deliver", StringComparison.Ordinal));

        Assert.All(log.Lines, line => Assert.DoesNotContain(Host, Written(line), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The class of the failure is what reaches the log instead, because that is what an operator
    /// acts on: a refused connection, a name that does not resolve and a certificate that did not
    /// verify are three different things to go and look at.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AFailedSendSaysWhatKindOfFailureItWas()
    {
        var endpoint = ASinkEndpoint.ThatRefusesTheConnection();
        var log = new RecordingLogger();
        using var sink = Sink(endpoint, Address, log);

        sink.Announce(Notice());
        await sink.QuietAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Contains(
            log.Lines,
            line => line.Message.Contains(nameof(HttpRequestException), StringComparison.Ordinal));
    }

    /// <summary>
    /// A refusal of the configuration names the setting and never quotes the value back.
    /// <para>
    /// That sentence becomes the message of the exception thrown out of a save and out of every read
    /// of a stored configuration, and where the host takes such an exception is not something this
    /// repository can read. The setting name has to survive, because it is how an operator is told
    /// which box is wrong, and <c>ConfigurationRulesTests</c> holds that half.
    /// </para>
    /// </summary>
    [Fact]
    public void ARefusedAddressIsNamedAsASettingAndNeverQuotedBack()
    {
        var configuration = new PluginConfiguration { OutboundNoticeAddress = Host + "/hook" };

        var problems = ConfigurationRules.Problems(configuration);

        Assert.Contains(problems, problem => problem.Setting == nameof(PluginConfiguration.OutboundNoticeAddress));
        Assert.All(problems, problem => Assert.DoesNotContain(Host, problem.Why, StringComparison.OrdinalIgnoreCase));

        var refused = Assert.Throws<InvalidConfigurationException>(
            () => ConfigurationRules.RefuseWhatCannotWork(configuration));

        Assert.DoesNotContain(Host, refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every property carrying the mark is named by a pattern in the invariant lint.
    /// <para>
    /// The mark lives on the property and the name it refuses lives in the rule file, which is two
    /// homes for one fact. Neither file can see the other, so this is what stops them drifting: a
    /// second setting marked as a secret with no rule pointed at it reds the suite here rather than
    /// being a mark that refuses nothing.
    /// </para>
    /// <para>
    /// It reads the marks off the type rather than out of the source, so a mark added by any route
    /// is in the population.
    /// </para>
    /// <para>
    /// <b>It reads the patterns of that file and not the whole of it, and the difference was
    /// measured rather than supposed.</b> Until #85 this leg searched the file as one string, and
    /// with the entire <c>no-marked-setting-in-a-message</c> rule deleted it still passed, because
    /// the prose above that rule spells the marked property twice. A guard satisfied by a comment
    /// about a refusal is a guard that does not bite for the reason it names, and this is the
    /// narrowing that makes it bite.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryMarkedSettingIsNamedByARuleInTheInvariantLint()
    {
        var marked = typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<SecretAttribute>() is not null)
            .Select(property => property.Name)
            .ToList();

        Assert.NotEmpty(marked);

        var path = Path.Combine(AppContext.BaseDirectory, "tools", "opengrep", "rules.yaml");
        Assert.True(File.Exists(path), FormattableString.Invariant($"{path} was not copied next to the suite."));

        var refusing = RefusingLines(File.ReadAllLines(path));

        Assert.All(
            marked,
            name => Assert.True(
                refusing.Any(line => line.Contains(name, StringComparison.Ordinal)),
                FormattableString.Invariant(
                    $"{name} carries [Secret] and no pattern in tools/opengrep/rules.yaml names it. A comment naming it is not a refusal.")));
    }

    /// <summary>
    /// The lines of the rule file that decide what is refused.
    /// <para>
    /// A comment is not a refusal, so it is not in the population a marked setting has to be named
    /// by. What is left is the pattern entries, which are the lines opengrep matches source against.
    /// </para>
    /// <para>
    /// <b>The bound, written rather than discovered.</b> It reads a line at a time, so a pattern
    /// broken across lines would hide a name from it. Nothing in that file is written that way
    /// today, and a rule that was would fail this leg loudly rather than passing it quietly, which
    /// is the direction to fail in.
    /// </para>
    /// </summary>
    /// <param name="lines">The rule file, line by line.</param>
    /// <returns>The pattern lines, with their leading space removed.</returns>
    private static List<string> RefusingLines(IEnumerable<string> lines)
        => lines
            .Select(line => line.TrimStart())
            .Where(line => line.StartsWith("pattern", StringComparison.Ordinal)
                || line.StartsWith("- pattern", StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// Everything one line of the log carries that a reader would end up with.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>The message and whatever was reported beside it.</returns>
    private static string Written(RecordingLogger.Line line)
        => line.Message + (line.Exception?.ToString() ?? string.Empty);

    private static OutboundSink Sink(HttpMessageHandler endpoint, string address, RecordingLogger log)
        => new OutboundSink(
            new FakeInstallSettings(new PluginConfiguration { OutboundNoticeAddress = address }),
            endpoint,
            log,
            OutboundSink.DefaultAnswerWithin);

    private static OutboundNotice Notice()
        => OutboundNotice.For(
            new MediaRequest
            {
                Id = new Guid("7a000000-0000-0000-0000-0000000000aa"),
                RequestedByUserId = Asker,
                RequestedAt = Noon,
                Kind = RequestedItemKind.Movie,
                DisplayTitle = "Stalker",
                DisplayYear = 1979,
                ProviderIds = new Dictionary<string, string> { ["Imdb"] = "tt0079944" },
                State = RequestState.Approved,
                StateChangedAt = Noon
            },
            NoticeEvent.Approved);
}

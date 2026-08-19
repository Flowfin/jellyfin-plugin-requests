using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests;

/// <summary>
/// The documents somebody is pointed at, against the tree that has to hold them.
/// <para>
/// The failure this stands for happened here. The sign-off check refuses a commit and tells whoever
/// wrote it to read <c>CONTRIBUTING.md</c> and <c>DCO</c>, and neither file was in the tree, so the
/// only message that gate ever produced named two things a contributor could not open. Nothing
/// refused it, because a workflow that names a file never opens it: the string is on an error path
/// a green run does not reach, and a red run is read by the person who is already stuck.
/// </para>
/// <para>
/// The documents are read next to the suite rather than out of the source tree, which is this
/// project's existing way of reading a file the release path reads instead of a second copy of it.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class RepositoryDocumentsTests
{
    /// <summary>
    /// The sign-off check, copied next to the suite so this reads the file the gate runs rather than
    /// a description of it.
    /// </summary>
    private const string SignOffCheck = "dco.yml";

    /// <summary>
    /// A markdown link to something in this repository. The scheme test below is what keeps it off
    /// the ones that point at the internet, which this cannot resolve and should not try to.
    /// </summary>
    private static readonly Regex Link = new Regex(@"\]\(([^)\s]+)\)", RegexOptions.Compiled, TimeSpan.FromSeconds(2));

    /// <summary>
    /// The documents the sign-off check names, which a contributor it turns away has to be able to
    /// open. Each is the spelling the check uses beside the path it resolves to.
    /// <para>
    /// The spelling matters. <c>DCO</c> on its own is three letters that appear in the check's own
    /// name and in half its comments, so a leg looking for that would pass over a message that had
    /// stopped naming the file. <c>./DCO</c> is the message.
    /// </para>
    /// </summary>
    /// <returns>How the check spells it, and the path in the tree.</returns>
    public static TheoryData<string, string> DocumentsTheSignOffCheckNames()
        => new TheoryData<string, string> { { "CONTRIBUTING.md", "CONTRIBUTING.md" }, { "./DCO", "DCO" } };

    /// <summary>
    /// The documents somebody arriving from outside this repository looks for by name.
    /// </summary>
    /// <returns>One document per case.</returns>
    public static TheoryData<string> DocumentsAnArrivalLooksFor()
        => new TheoryData<string> { "CONTRIBUTING.md", "SECURITY.md", "CODE_OF_CONDUCT.md" };

    /// <summary>
    /// Every document the sign-off check names is in the tree, and the check still names it.
    /// <para>
    /// Both directions, because they are the same gap arriving from two sides. A document deleted
    /// while the check points at it is what was here; a check that stopped pointing at a document
    /// reads as fixed, because the file is present and nothing sends anybody to it.
    /// </para>
    /// </summary>
    /// <param name="named">The document as the check spells it.</param>
    /// <param name="path">Where that resolves to in the tree.</param>
    [Theory]
    [MemberData(nameof(DocumentsTheSignOffCheckNames))]
    public void EveryDocumentTheSignOffCheckNamesIsInTheTree(string named, string path)
    {
        Assert.True(
            File.Exists(Beside(path)),
            FormattableString.Invariant(
                $"{path} is not in the tree, and the sign-off check tells a refused contributor to read it."));

        Assert.NotEmpty(File.ReadAllText(Beside(path)).Trim());
        Assert.Contains(named, File.ReadAllText(Beside(SignOffCheck)), StringComparison.Ordinal);
    }

    /// <summary>
    /// The documents a contributor and a reporter look for are there and say something.
    /// </summary>
    /// <param name="document">The document.</param>
    [Theory]
    [MemberData(nameof(DocumentsAnArrivalLooksFor))]
    public void TheDocumentsAnArrivalLooksForAreInTheTree(string document)
    {
        Assert.True(File.Exists(Beside(document)), FormattableString.Invariant($"{document} is not in the tree."));
        Assert.NotEmpty(File.ReadAllText(Beside(document)).Trim());
    }

    /// <summary>
    /// Every repository path these documents link to resolves to a file that is there.
    /// <para>
    /// This is the same defect as the one above, generalised: a document that sends somebody to a
    /// file which does not exist is worse than one that says nothing, because the reader spends
    /// their time looking. It is also what holds the contributing document to pointing at
    /// <c>docs/quality-parity.md</c> rather than at a path it invented.
    /// </para>
    /// </summary>
    /// <param name="document">The document whose links are followed.</param>
    [Theory]
    [MemberData(nameof(DocumentsAnArrivalLooksFor))]
    public void EveryRepositoryPathTheseDocumentsLinkToIsInTheTree(string document)
    {
        var links = Link.Matches(File.ReadAllText(Beside(document)))
            .Select(match => match.Groups[1].Value)
            .Where(target => !target.Contains("://", StringComparison.Ordinal))
            .Where(target => !target.StartsWith('#'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // No assertion that there are any. Two of these three documents send a reader to a place
        // rather than to a file, and a leg demanding a link would be satisfied by adding one.
        var dangling = links
            .Where(target => !File.Exists(Beside(target.Replace('/', Path.DirectorySeparatorChar))))
            .OrderBy(target => target, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], dangling);
    }

    /// <summary>
    /// The contributing document sends a reader to the parity document for what the gate requires,
    /// and that document is there.
    /// <para>
    /// It points rather than restates because a list of checks written twice is two answers, and the
    /// copy nobody runs is the one that goes stale. Whether the prose around the link restates it
    /// anyway is a judgement the review makes; what is checkable is that the link is there and
    /// resolves, and this is that half.
    /// </para>
    /// </summary>
    [Fact]
    public void TheContributingDocumentSendsAReaderToTheParityDocument()
    {
        const string Parity = "docs/quality-parity.md";

        Assert.Contains(Parity, File.ReadAllText(Beside("CONTRIBUTING.md")), StringComparison.Ordinal);
        Assert.True(File.Exists(Beside(Parity.Replace('/', Path.DirectorySeparatorChar))));
    }

    /// <summary>
    /// The refusal list names the double that replaces a real outbound call, and the double is still
    /// called that.
    /// <para>
    /// The document went on saying that replacement did not exist yet, long after it did, because it
    /// named the issue that would build it rather than the file that had been built. An issue number
    /// stops being an address the moment the thing arrives, and nothing read the sentence. The repair
    /// was to name the type, and this is what holds that name true: rename the double and
    /// <c>nameof</c> follows it while the document does not, so the two disagree here rather than in
    /// front of somebody looking for what to write instead of a real HTTPS call.
    /// </para>
    /// <para>
    /// It reads the name and not the sentence around it. Prose naming the double and saying the wrong
    /// thing about it passes, which is stated in the document rather than hidden by this leg.
    /// </para>
    /// </summary>
    [Fact]
    public void TheRefusalListNamesTheDoubleThatReplacesARealOutboundCall()
    {
        const string Testing = "docs/testing.md";

        var document = File.ReadAllText(Beside(Testing.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains(nameof(Doubles.ASinkEndpoint), document, StringComparison.Ordinal);
    }

    /// <summary>
    /// One file as it sits next to the suite.
    /// </summary>
    /// <param name="name">Its path, relative to the repository root.</param>
    /// <returns>The path to read.</returns>
    private static string Beside(string name) => Path.Combine(AppContext.BaseDirectory, name);
}

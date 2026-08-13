using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Xunit;

using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests.Web;

/// <summary>
/// The rule the pages are held to about other people's writing.
/// <para>
/// A request carries a note somebody typed and a decline reason an operator wrote, and both are
/// shown to a person who did not type them. Putting either on the page as markup is a scripting
/// hole in the dashboard of a media server, reachable by any user who can file a request, and the
/// mistake that opens it is one character wide: <c>innerHTML</c> where <c>textContent</c> was
/// meant. Nothing else in this tree refuses it. The formatting gate reads these files and would
/// reformat that line without comment, and no analyzer reaches them at all, which
/// <c>docs/testing.md</c> says in the paragraph that points here.
/// </para>
/// <para>
/// What this reads is the assets as the assembly carries them rather than as the working tree
/// holds them, because what a server serves is the embedded copy and a file left out of the
/// packaging is a file this would otherwise judge and the dashboard would never see.
/// </para>
/// <para>
/// The bound is worth stating: this refuses the sinks, it does not run the page. Whether a string
/// that reaches <c>textContent</c> was the right string is a question about the page's behaviour,
/// and driving a browser to ask it is what <c>docs/testing.md</c> refuses.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public class PageRulesTests
{
    /// <summary>
    /// The ways a string becomes markup in a browser, spelled as they are written.
    /// <para>
    /// Each entry is a sink that parses what it is given. <c>textContent</c>,
    /// <c>insertAdjacentText</c>, <c>createTextNode</c> and <c>setAttribute</c> are not here and are
    /// not an oversight: they carry a string as a string, which is the whole of what this rule
    /// asks for.
    /// </para>
    /// </summary>
    private static readonly string[] MarkupSinks =
    [
        "innerHTML",
        "outerHTML",
        "insertAdjacentHTML",
        "document.write",
        "createContextualFragment",
        "parseFromString",
        "srcdoc"
    ];

    /// <summary>
    /// Nothing this plugin embeds turns a string into markup.
    /// <para>
    /// The count of assets read is asserted as well as the offenders, because the failure that
    /// would otherwise pass in silence is not a page using the wrong sink: it is this reading
    /// nothing at all, after a rename moves the resources or the filter stops matching them, and
    /// then reporting that no page does the thing it never looked for.
    /// </para>
    /// </summary>
    [Fact]
    public void NothingThePagesShipTurnsAStringIntoMarkup()
    {
        var assets = Assets().ToArray();

        var offenders = assets
            .SelectMany(asset => MarkupSinks
                .Where(sink => asset.Body.Contains(sink, StringComparison.Ordinal))
                .Select(sink => asset.Name + ": " + sink))
            .ToArray();

        Assert.NotEmpty(assets);
        Assert.Empty(offenders);
    }

    /// <summary>
    /// Everything the plugin registers, except the stylesheet, is one of the assets read above.
    /// <para>
    /// The two sides are built from different things on purpose, and the first shape of this test
    /// got that wrong in a way worth recording. It selected the registrations with the same
    /// predicate the scan uses, so narrowing that predicate to scripts alone dropped both pages
    /// from both sides at once and the suite stayed green. What decides the side below is what a
    /// resource is not, so a filter that stops matching pages leaves them registered, unread, and
    /// named here.
    /// </para>
    /// </summary>
    [Fact]
    public void EverythingRegisteredExceptTheStylesheetIsOneOfTheAssetsRead()
    {
        using var host = new PluginHost();

        var read = Assets().Select(asset => asset.Name).ToArray();

        var registered = host.Plugin
            .GetPages()
            .Select(page => page.EmbeddedResourcePath)
            .Where(path => !path.EndsWith(".css", StringComparison.Ordinal))
            .ToArray();

        var unread = registered.Where(path => !read.Contains(path, StringComparer.Ordinal)).ToArray();

        Assert.NotEmpty(registered);
        Assert.Empty(unread);
    }

    /// <summary>
    /// Whether a resource is one of the two kinds a browser executes. The stylesheet is out because
    /// no sink above exists in CSS, and everything else the assembly carries is not served to a
    /// page at all.
    /// </summary>
    /// <param name="resource">The manifest resource name.</param>
    /// <returns><c>true</c> where the resource is a page or a script.</returns>
    private static bool IsPageOrScript(string resource)
        => resource.EndsWith(".html", StringComparison.Ordinal)
            || resource.EndsWith(".js", StringComparison.Ordinal);

    /// <summary>
    /// Every page and script the built assembly carries, as text. The inline script inside a page
    /// is read with the page, which is where a row drawn from a request will be built.
    /// </summary>
    /// <returns>The name and body of each.</returns>
    private static IEnumerable<(string Name, string Body)> Assets()
    {
        var assembly = typeof(PluginUnderTest).Assembly;

        foreach (var resource in assembly.GetManifestResourceNames().Where(IsPageOrScript))
        {
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"The assembly carries no resource named {resource}.");

            using var reader = new StreamReader(stream, Encoding.UTF8);

            yield return (resource, reader.ReadToEnd());
        }
    }
}

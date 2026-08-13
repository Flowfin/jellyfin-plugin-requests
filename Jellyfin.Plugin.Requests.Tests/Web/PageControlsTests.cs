using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests.Web;

/// <summary>
/// What an operator working the queue without a mouse meets.
/// <para>
/// Two things decide it and both are readable off the page. A control with no label is a control a
/// screen reader announces as its own type and nothing else, so an operator hears "combo box" four
/// times and has to guess which is the state. And a control that is a styled element with a click
/// handler on it is one the keyboard never reaches at all: focus does not stop there, the space bar
/// does nothing, and the page looks identical to one that works.
/// </para>
/// <para>
/// The bound is worth stating plainly. This reads the page rather than driving a browser through
/// it, so what it holds is that every control is a real control and that each carries a name. That
/// somebody can complete the work with a keyboard is a claim about behaviour, and running a browser
/// to make it a measurement is what <c>docs/testing.md</c> refuses.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class PageControlsTests
{
    /// <summary>
    /// The element the dashboard hands the script, which carries an identifier for that reason
    /// rather than because anybody operates it.
    /// </summary>
    private const string ThePageItself = "RequestsQueuePage";

    /// <summary>
    /// The elements a browser gives keyboard behaviour to without being asked. Anything else the
    /// page attaches behaviour to is a control only a pointer can work.
    /// </summary>
    private static readonly string[] RealControls = ["select", "button", "input", "textarea"];

    /// <summary>
    /// Every control the queue carries has a name something can read out.
    /// <para>
    /// A select is named by the label that points at it, and a button by the words between its
    /// tags. Both sides are read off the page rather than listed here, so a control added without
    /// either is named by this the first time the suite runs.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryControlOnTheQueueCarriesANameSomethingCanReadOut()
    {
        var body = Queue();

        var unlabelled = IdentifiersOf(body, "<select ")
            .Where(id => !body.Contains("for=\"" + id + "\"", StringComparison.Ordinal))
            .ToArray();

        var silent = Buttons(body)
            .Where(button => button.Words.Length == 0)
            .Select(button => button.Id)
            .ToArray();

        Assert.NotEmpty(IdentifiersOf(body, "<select "));
        Assert.NotEmpty(Buttons(body));
        Assert.Empty(unlabelled);
        Assert.Empty(silent);
    }

    /// <summary>
    /// Everything the queue reaches for by name is a control the keyboard already reaches.
    /// <para>
    /// An identifier on this page exists so the script can find an element and give it something to
    /// do, so every one of them is required to sit on an element a browser makes focusable on its
    /// own. The mistake this refuses is the cheap one: drawing a pager out of two styled elements
    /// with click handlers, which looks right, reads right, and is unusable without a pointer.
    /// </para>
    /// <para>
    /// The page's own element is the one exception, and it is named rather than filtered by shape.
    /// It is not a control: it is what the dashboard hands the script, and the event on it is the
    /// dashboard's own rather than anything an operator does.
    /// </para>
    /// </summary>
    [Fact]
    public void EverythingTheQueueReachesForByNameIsAControlAKeyboardReaches()
    {
        var body = Queue();

        var named = Identifiers(body)
            .Where(id => !string.Equals(id, ThePageItself, StringComparison.Ordinal))
            .ToArray();

        var unreachable = named
            .Where(id => !RealControls.Contains(ElementCarrying(body, id), StringComparer.Ordinal))
            .ToArray();

        Assert.NotEmpty(named);
        Assert.Empty(unreachable);

        // A page that orders focus by hand is a page whose order stops matching what is drawn the
        // first time a control moves, so the order is the document's own or it is nobody's.
        Assert.DoesNotContain("tabindex", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Which element carries an identifier.
    /// </summary>
    /// <param name="body">The page.</param>
    /// <param name="id">The identifier.</param>
    /// <returns>The element's name, in lower case as the page writes it.</returns>
    private static string ElementCarrying(string body, string id)
    {
        var at = body.IndexOf("id=\"" + id + "\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "The script reaches for #" + id + " and the page declares nothing under it.");

        var opens = body.LastIndexOf('<', at);
        Assert.True(opens >= 0, "The identifier " + id + " sits inside no element.");

        var name = body[(opens + 1)..];

        return new string([.. name.TakeWhile(char.IsAsciiLetter)]);
    }

    /// <summary>
    /// The identifiers carried by one kind of element.
    /// </summary>
    /// <param name="body">The page.</param>
    /// <param name="element">The element, as it is opened.</param>
    /// <returns>Each identifier, in the order the page declares them.</returns>
    private static IEnumerable<string> IdentifiersOf(string body, string element)
    {
        var at = body.IndexOf(element, StringComparison.Ordinal);

        while (at >= 0)
        {
            var opens = body.IndexOf('>', at);

            if (opens < 0)
            {
                yield break;
            }

            var declared = body[at..opens];
            const string Marker = "id=\"";
            var named = declared.IndexOf(Marker, StringComparison.Ordinal);

            if (named >= 0)
            {
                var start = named + Marker.Length;
                yield return declared[start..declared.IndexOf('"', start)];
            }

            at = body.IndexOf(element, opens, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every identifier the page declares, whatever carries it.
    /// </summary>
    /// <param name="body">The page.</param>
    /// <returns>Each identifier, in the order the page declares them.</returns>
    private static IEnumerable<string> Identifiers(string body)
    {
        const string Marker = "id=\"";

        var at = body.IndexOf(Marker, StringComparison.Ordinal);

        while (at >= 0)
        {
            var start = at + Marker.Length;
            var end = body.IndexOf('"', start);

            if (end < 0)
            {
                yield break;
            }

            yield return body[start..end];

            at = body.IndexOf(Marker, end, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The buttons the page draws, with what a person hears when they reach one.
    /// </summary>
    /// <param name="body">The page.</param>
    /// <returns>The identifier and the words of each.</returns>
    private static (string Id, string Words)[] Buttons(string body)
    {
        var buttons = new List<(string Id, string Words)>();

        var at = body.IndexOf("<button ", StringComparison.Ordinal);

        while (at >= 0)
        {
            var opens = body.IndexOf('>', at);
            var closes = body.IndexOf("</button>", at, StringComparison.Ordinal);

            if (opens < 0 || closes < 0)
            {
                break;
            }

            var declared = body[at..opens];
            const string Marker = "id=\"";
            var named = declared.IndexOf(Marker, StringComparison.Ordinal);
            var start = named + Marker.Length;

            buttons.Add((
                named >= 0 ? declared[start..declared.IndexOf('"', start)] : declared,
                body[(opens + 1)..closes].Trim()));

            at = body.IndexOf("<button ", closes, StringComparison.Ordinal);
        }

        return [.. buttons];
    }

    /// <summary>
    /// The queue page as the built assembly carries it, which is the copy a server serves.
    /// </summary>
    /// <returns>The page.</returns>
    private static string Queue()
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

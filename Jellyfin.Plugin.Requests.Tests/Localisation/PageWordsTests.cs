using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Requests.Localisation;
using Jellyfin.Plugin.Requests.Model;
using Xunit;

using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests.Localisation;

/// <summary>
/// The pages and the catalogue, held to each other.
/// <para>
/// Moving every word out of the markup buys a plugin that can be translated and costs a failure
/// nothing else here would catch: a key with a letter wrong. It compiles, it ships, the lint is
/// green because the page carries no English, and what an operator meets is a blank cell or a
/// button with no label. Nothing about it looks like a mistake.
/// </para>
/// <para>
/// So the two halves are checked in both directions. Every key a page names is one the English
/// catalogue carries, and every key the catalogue carries is one something names. The second half
/// is the one that keeps the catalogue from filling with strings nobody shows.
/// </para>
/// <para>
/// The bound is the same one every check over these assets carries: this reads the files rather
/// than running a browser, which the headless rule in <c>docs/testing.md</c> settles. The keys a
/// page builds from a value at run time are the four groups below, and they are expanded here from
/// the model's own enumerations rather than listed, so a state added to the model is a red suite
/// rather than a blank cell.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class PageWordsTests
{
    /// <summary>
    /// The assets a person's words can be drawn from, relative to the plugin's own namespace.
    /// </summary>
    private static readonly string[] Assets =
    [
        "Web.mine.html",
        "Web.queue.html",
        "Web.shell.js",
        "Configuration.configPage.html"
    ];

    /// <summary>
    /// The groups a page turns a value into a word through, and the enumeration each one's values
    /// come from. The prefixes differ between the two pages on purpose: an operator's queue says
    /// <c>Open</c> and a person's own list says what waiting for an answer means to them.
    /// </summary>
    private static readonly (string Group, Type Values)[] Groups =
    [
        ("kind", typeof(RequestedItemKind)),
        ("mine.state", typeof(RequestState)),
        ("queue.state", typeof(RequestState)),
        ("declineReason", typeof(DeclineReason)),
        ("availability", typeof(LibraryAvailability))
    ];

    /// <summary>
    /// Every key a page names is one the English catalogue carries.
    /// </summary>
    [Fact]
    public void EveryKeyThePagesNameIsOneTheEnglishCatalogueCarries()
    {
        var carried = StringCatalogue.Shipped.For(culture: null);

        var missing = Named()
            .Where(key => !carried.ContainsKey(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(Named());
        Assert.Empty(missing);
    }

    /// <summary>
    /// Every key the English catalogue carries is one something names.
    /// <para>
    /// The direction nobody thinks to check. A key left behind by a page that stopped drawing it is
    /// a sentence a translator is asked to translate and nobody will ever read, and the cost lands
    /// on whoever is doing the translating rather than on this repository.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryKeyTheCatalogueCarriesIsOneSomethingNames()
    {
        var named = Named();

        var unused = StringCatalogue.Shipped
            .For(culture: null)
            .Keys
            .Where(key => !named.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unused);
    }

    /// <summary>
    /// Every key anything in the assets names, the run-time ones expanded.
    /// </summary>
    /// <returns>The keys.</returns>
    private static HashSet<string> Named()
    {
        var named = new HashSet<string>(StringComparer.Ordinal);

        foreach (var asset in Assets)
        {
            var body = Asset(asset);

            Take(named, body, @"data-i18n=""(?<key>[^""]+)""");
            Take(named, body, @"\b(?:word|fill|tell)\(""(?<key>[^""]+)""");
            Take(named, body, @"\bsay\((?:\s*page\s*,)?\s*""(?<key>[^""]+)""");

            // The strip's own two, which are properties of a list rather than arguments to a call:
            // the shell walks that list and looks each key up, so the literal never reaches `word`.
            Take(named, body, @"\blabel: ""(?<key>[^""]+)""");

            foreach (Match found in Regex.Matches(body, @"\bnamed\(""(?<group>[^""]+)"""))
            {
                var group = found.Groups["group"].Value;
                var values = Groups.SingleOrDefault(known => string.Equals(known.Group, group, StringComparison.Ordinal));

                Assert.NotNull(values.Values);

                foreach (var value in Enum.GetNames(values.Values))
                {
                    named.Add(group + "." + value);
                }
            }
        }

        foreach (var key in Declared())
        {
            named.Add(key);
        }

        return named;
    }

    /// <summary>
    /// The keys named on this side of the API rather than in an asset.
    /// <para>
    /// One of the three sentences #70 owns is met while asking for something rather than while
    /// reading a page, so no page draws it and the second leg above would read it as a string
    /// nobody shows. It is named by <see cref="Sentences"/>, which is the one place the three are
    /// declared, and it is read off that class rather than repeated here so a fourth sentence is
    /// covered the first time the suite runs.
    /// </para>
    /// </summary>
    /// <returns>The keys.</returns>
    private static string[] Declared()
    {
        var declared = typeof(Sentences)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string?)field.GetRawConstantValue())
            .Where(key => !string.IsNullOrEmpty(key))
            .Select(key => key!)
            .ToArray();

        Assert.NotEmpty(declared);

        return declared;
    }

    /// <summary>
    /// Every non-empty capture of one pattern, added to the set.
    /// </summary>
    /// <param name="named">The set so far.</param>
    /// <param name="body">The asset.</param>
    /// <param name="pattern">The pattern, capturing a group called <c>key</c>.</param>
    private static void Take(HashSet<string> named, string body, string pattern)
    {
        foreach (Match found in Regex.Matches(body, pattern))
        {
            var key = found.Groups["key"].Value;

            if (key.Length > 0)
            {
                named.Add(key);
            }
        }
    }

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
}

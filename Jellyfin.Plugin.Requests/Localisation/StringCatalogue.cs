using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.Requests.Localisation;

/// <summary>
/// The words a person reads, per culture, read out of the assembly rather than written into the
/// code that shows them.
/// <para>
/// <b>A catalogue is a file and nothing else.</b> Every file matching <c>Localisation/Strings/*.json</c>
/// is embedded by a glob in the project file and found here by walking the manifest, so a language
/// arrives by adding a file and by changing no code at all. Nothing in this tree holds a list of the
/// cultures that ship, which is what stops such a list going stale against the files beside it.
/// </para>
/// <para>
/// <b>A missing key falls back to English and never to the key.</b> Showing a key to a person is
/// showing them the inside of the plugin, and a half-translated catalogue is the ordinary state of
/// one: a language arrives with the strings somebody had time for. So a lookup walks the culture,
/// then each parent of it, then English, and a key absent from English as well is a packaging fault
/// rather than anything a caller did.
/// </para>
/// </summary>
public sealed class StringCatalogue
{
    /// <summary>
    /// The culture every other one falls back to, and the one this plugin ships complete.
    /// </summary>
    public const string English = "en";

    /// <summary>
    /// Where a catalogue sits inside the assembly, relative to the root namespace. The build turns
    /// the directory separators of <c>Localisation/Strings</c> into dots, so this is what a
    /// manifest name carries.
    /// </summary>
    private const string Folder = ".Localisation.Strings.";

    /// <summary>
    /// The suffix a catalogue file carries.
    /// </summary>
    private const string Suffix = ".json";

    /// <summary>
    /// One dictionary per culture, keyed by the culture name exactly as the file names it.
    /// </summary>
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _byCulture;

    /// <summary>
    /// Initializes a new instance of the <see cref="StringCatalogue"/> class from the catalogues one
    /// assembly carries.
    /// </summary>
    /// <param name="carrying">The assembly whose embedded catalogues are read.</param>
    /// <exception cref="ArgumentNullException">Where no assembly is given.</exception>
    /// <exception cref="InvalidOperationException">
    /// Where the assembly carries no English catalogue. Every other culture is optional and English
    /// is not, because it is what a missing key falls back to: without it a lookup has nowhere to
    /// end and the fallback rule this class exists for cannot be kept.
    /// </exception>
    public StringCatalogue(Assembly carrying)
    {
        ArgumentNullException.ThrowIfNull(carrying);

        var found = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var root = carrying.GetName().Name + Folder;

        foreach (var name in carrying.GetManifestResourceNames())
        {
            if (!name.StartsWith(root, StringComparison.Ordinal) || !name.EndsWith(Suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var culture = name.Substring(root.Length, name.Length - root.Length - Suffix.Length);

            found[culture] = Read(carrying, name);
        }

        if (!found.ContainsKey(English))
        {
            throw new InvalidOperationException(
                "This plugin was built without its English catalogue, so there is nothing for a missing key to fall back to. The resource was expected under " + root + English + Suffix + ".");
        }

        _byCulture = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(found);
    }

    /// <summary>
    /// Gets the catalogues this plugin ships, built once because an assembly's resources cannot
    /// change while the server is running.
    /// </summary>
    public static StringCatalogue Shipped { get; } = new StringCatalogue(typeof(Plugin).Assembly);

    /// <summary>
    /// Gets the cultures a catalogue exists for, derived from the files rather than declared.
    /// </summary>
    public IReadOnlyCollection<string> Cultures => (IReadOnlyCollection<string>)_byCulture.Keys;

    /// <summary>
    /// One string, for one culture.
    /// </summary>
    /// <param name="key">The key, which is what the pages and the endpoints name a string by.</param>
    /// <param name="culture">
    /// The culture asked for, or <see langword="null"/> for English. A culture no catalogue exists
    /// for is not an error: it falls back like a missing key does.
    /// </param>
    /// <returns>The string, in the nearest culture that carries the key.</returns>
    /// <exception cref="ArgumentNullException">Where no key is given.</exception>
    /// <exception cref="InvalidOperationException">
    /// Where English does not carry the key either. That is a key somebody wrote at a call site and
    /// never added to the catalogue, which is a packaging fault: it is raised rather than answered
    /// with the key, because a page showing <c>queue.column.title</c> to an operator is worse than
    /// one that fails where the mistake was made.
    /// </exception>
    public string Get(string key, CultureInfo? culture)
    {
        ArgumentNullException.ThrowIfNull(key);

        foreach (var name in Chain(culture))
        {
            if (_byCulture.TryGetValue(name, out var strings) && strings.TryGetValue(key, out var written))
            {
                return written;
            }
        }

        throw new InvalidOperationException(
            "There is no string under the key " + key + " in this plugin's English catalogue, and English is what every other culture falls back to.");
    }

    /// <summary>
    /// Every string, for one culture, with English underneath it.
    /// <para>
    /// The answer is complete whatever the culture's own file holds, because the fallback is applied
    /// here rather than left to whoever reads it. A page handed a partial set would have to carry
    /// the fallback rule a second time, and two copies of one rule is how a half-translated
    /// catalogue starts showing keys on one surface and not the other.
    /// </para>
    /// </summary>
    /// <param name="culture">The culture asked for, or <see langword="null"/> for English.</param>
    /// <returns>The whole catalogue as that culture reads it.</returns>
    public IReadOnlyDictionary<string, string> For(CultureInfo? culture)
    {
        var answer = new Dictionary<string, string>(StringComparer.Ordinal);
        var chain = Chain(culture);

        // Walked from English outwards, so a culture that carries a key overwrites the fallback
        // rather than being overwritten by it.
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (!_byCulture.TryGetValue(chain[i], out var strings))
            {
                continue;
            }

            foreach (var pair in strings)
            {
                answer[pair.Key] = pair.Value;
            }
        }

        return new ReadOnlyDictionary<string, string>(answer);
    }

    /// <summary>
    /// The cultures a lookup tries, nearest first, ending at English.
    /// </summary>
    /// <param name="culture">The culture asked for, or <see langword="null"/>.</param>
    /// <returns>The chain, which always ends with English and never repeats a name.</returns>
    private static List<string> Chain(CultureInfo? culture)
    {
        var chain = new List<string>();

        for (var walk = culture; walk is not null && walk.Name.Length > 0; walk = walk.Parent)
        {
            if (!chain.Contains(walk.Name, StringComparer.OrdinalIgnoreCase))
            {
                chain.Add(walk.Name);
            }

            // CultureInfo.Parent of an already-neutral culture answers with the invariant culture,
            // whose name is empty, and the loop condition above ends there. Guarding on reference
            // equality as well keeps a culture whose parent is itself from spinning.
            if (ReferenceEquals(walk.Parent, walk))
            {
                break;
            }
        }

        if (!chain.Contains(English, StringComparer.OrdinalIgnoreCase))
        {
            chain.Add(English);
        }

        return chain;
    }

    /// <summary>
    /// One catalogue file, as the assembly carries it.
    /// </summary>
    /// <param name="carrying">The assembly.</param>
    /// <param name="name">The manifest resource name.</param>
    /// <returns>The strings in it.</returns>
    /// <exception cref="InvalidOperationException">
    /// Where the resource cannot be read or is not a flat object of strings. A catalogue that
    /// parsed to something else would leave a page with fewer words than it asked for and no
    /// account of why.
    /// </exception>
    private static ReadOnlyDictionary<string, string> Read(Assembly carrying, string name)
    {
        using var stream = carrying.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                "The assembly lists " + name + " among its resources and then does not carry it.");

        using var reader = new StreamReader(stream, Encoding.UTF8);

        Dictionary<string, string>? parsed;

        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
        }
        catch (JsonException reason)
        {
            throw new InvalidOperationException(
                "The catalogue " + name + " is not a flat object of keys and strings.",
                reason);
        }

        return new ReadOnlyDictionary<string, string>(
            parsed ?? throw new InvalidOperationException("The catalogue " + name + " is empty."));
    }
}

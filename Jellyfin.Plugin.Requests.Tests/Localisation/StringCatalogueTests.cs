using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Requests.Localisation;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Localisation;

/// <summary>
/// The catalogue behind every word a person reads, held to the two rules #73 asks for: a missing key
/// falls back to English rather than showing the key, and adding a language is adding a file.
/// <para>
/// The catalogues this reads are the suite's own, embedded beside it under the same wildcard the
/// plugin uses. They carry keys that appear nowhere in the shipped set on purpose: a fixture sharing
/// a key with the real catalogue would pass whichever of the two the loader happened to read, which
/// is the one mistake a test of a resource loader can make and not notice.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class StringCatalogueTests
{
    /// <summary>
    /// A culture the suite ships a catalogue for and the shipped plugin does not.
    /// </summary>
    private static readonly CultureInfo Regional = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// A key the regional catalogue does not carry and English does.
    /// </summary>
    [Fact]
    public void AKeyOnlyEnglishCarriesIsAnsweredInEnglish()
    {
        Assert.Equal(
            "what only English says",
            Suite().Get("a.key.only.english.carries", Regional));
    }

    /// <summary>
    /// A key the culture does carry is answered in that culture, so the fallback above is a
    /// fallback rather than the only thing that ever happens.
    /// <para>
    /// This is the leg that makes the one above mean something. Without it a loader that ignored
    /// every catalogue but English would pass, and every string on every install would be English
    /// while the suite reported a working fallback.
    /// </para>
    /// </summary>
    [Fact]
    public void AKeyTheCultureCarriesIsAnsweredInThatCulture()
    {
        Assert.Equal(
            "what the regional catalogue says",
            Suite().Get("a.key.both.carry", Regional));
    }

    /// <summary>
    /// A key the region does not carry and its language does is answered in the language, before
    /// English is reached.
    /// <para>
    /// This is what makes a language file worth adding at all. A translator writes <c>de</c>, and a
    /// server set to <c>de-DE</c>, <c>de-AT</c> or <c>de-CH</c> reads it; without this step each
    /// region would fall past it to English and the file would serve nobody.
    /// </para>
    /// </summary>
    [Fact]
    public void AKeyTheRegionDoesNotCarryIsAnsweredByItsLanguage()
    {
        Assert.Equal(
            "what the neutral catalogue says",
            Suite().Get("a.key.only.the.neutral.carries", Regional));
    }

    /// <summary>
    /// A key nothing carries is refused where it was written rather than shown to a person.
    /// </summary>
    [Fact]
    public void AKeyNothingCarriesIsAFailureAndNeverAWord()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => Suite().Get("a.key.nobody.wrote", Regional));

        Assert.Contains("a.key.nobody.wrote", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole set a page is handed is complete, whatever its own catalogue holds.
    /// <para>
    /// The pages fetch the set rather than one key at a time, so the fallback has to be applied
    /// before it is answered. A page handed only what the culture's own file carries would have to
    /// keep the fallback rule a second time, and two copies of one rule is how a half-translated
    /// install starts showing keys on one surface and words on the other.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSetAPageIsHandedIsCompleteHoweverLittleIsTranslated()
    {
        var whole = Suite().For(Regional);

        Assert.Equal("what the regional catalogue says", whole["a.key.both.carry"]);
        Assert.Equal("what only English says", whole["a.key.only.english.carries"]);
        Assert.Equal("what the neutral catalogue says", whole["a.key.only.the.neutral.carries"]);
    }

    /// <summary>
    /// A culture no catalogue exists for reads English.
    /// </summary>
    [Fact]
    public void ACultureNoCatalogueExistsForReadsEnglish()
    {
        Assert.Equal(
            "what English says",
            Suite().Get("a.key.both.carry", CultureInfo.GetCultureInfo("fr-FR")));
    }

    /// <summary>
    /// Adding a language is adding a file, in the two halves that claim needs.
    /// <para>
    /// The first half is that the loader finds a catalogue nothing names. The suite's own three are
    /// found, and no source file here or in the plugin names <c>de</c> or <c>de-DE</c>: they became
    /// cultures by being dropped in a directory. The second half is that the project takes them by
    /// a wildcard, because a project file naming each catalogue would make adding a language a file
    /// plus a build edit, and the claim would be false in exactly the way nobody notices until a
    /// language ships missing.
    /// </para>
    /// </summary>
    [Fact]
    public void AddingALanguageIsAddingAFile()
    {
        Assert.Equal(
            ["de", "de-DE", "en"],
            Suite().Cultures.OrderBy(culture => culture, StringComparer.Ordinal).ToArray());

        Assert.Contains(
            @"<EmbeddedResource Include=""Localisation\Strings\*.json"" />",
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Jellyfin.Plugin.Requests.csproj.txt")),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The plugin ships English and nothing else, which is what #73 asks for: a catalogue that can
    /// be translated rather than translations nobody has checked.
    /// </summary>
    [Fact]
    public void ThePluginShipsEnglishAndNothingElse()
    {
        Assert.Equal(["en"], StringCatalogue.Shipped.Cultures.ToArray());
    }

    /// <summary>
    /// An assembly with no English catalogue is refused when it is loaded rather than when somebody
    /// looks at a page.
    /// </summary>
    [Fact]
    public void AnAssemblyCarryingNoEnglishCatalogueIsRefused()
    {
        Assert.Throws<InvalidOperationException>(
            () => new StringCatalogue(typeof(Xunit.FactAttribute).Assembly));
    }

    /// <summary>
    /// The catalogues this suite carries.
    /// </summary>
    /// <returns>The catalogue.</returns>
    private static StringCatalogue Suite()
        => new StringCatalogue(typeof(StringCatalogueTests).Assembly);
}

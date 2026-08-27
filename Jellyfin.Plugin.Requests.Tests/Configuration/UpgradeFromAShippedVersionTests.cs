using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using Jellyfin.Plugin.Requests.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Configuration;

/// <summary>
/// The hop between two shipped versions of this plugin, which is the upgrade an operator actually
/// performs.
/// <para>
/// <b>What the pair is.</b> Two versions have shipped, <c>0.1.0.0-stable</c> and
/// <c>0.2.0.0-stable</c>, so there is one pair and this is its test. The older of the two carried
/// the store contract and no implementation of it, so an install of it wrote no requests anywhere
/// and there is no queue in this hop to migrate. What it did leave on a disk is its settings file,
/// which the server writes from the plugin's own configuration class, and that file is what a
/// server running the newer version reads on the first start after the upgrade.
/// </para>
/// <para>
/// <b>Why that is worth a test rather than an assumption.</b> The older class carried no settings
/// at all and the newer one carries ten, four of which decide whether the install can run: a quota
/// below one refuses every ask, neither kind accepted means nothing can be asked for, and a
/// retention period below the floor is refused outright. If the reader filled the absent elements
/// with the type default instead of leaving the class's own values alone, every upgraded install
/// would come up refusing to work, and an operator would read that as the new version being broken
/// rather than as an upgrade that lost their settings.
/// </para>
/// <para>
/// <b>The fixture is what the older version produced.</b> It is the XML the shipped <c>0.1.0.0</c>
/// assembly's own configuration type serialises to, taken from the type inside
/// <c>requests_0.1.0.0.zip</c> rather than typed here to look like it. The commands are in
/// <c>docs/compatibility.md</c>. What is not claimed: no server wrote it. A running Jellyfin was
/// not available where it was captured, so what stands behind the bytes is the shipped type and the
/// serialiser the host uses, and not an installation.
/// </para>
/// <para>
/// <b>What this does not cover, because something else already does.</b> A migration that fails has
/// to leave the old data intact and the plugin refusing to run rather than half migrated. For the
/// settings that is <c>ConfigurationRulesTests.SavingAValueThisInstallCannotRunOnIsRefusedAndWritesNothing</c>
/// and <c>ConfigurationRulesTests.ReadingAStoredConfigurationThisInstallCannotRunOnIsRefused</c>;
/// for the queue it is <c>FileRequestStoreVersionTests.AFileFromALaterVersionIsRefusedInTheLogAndLeftUntouched</c>.
/// Nothing is rebuilt here beside them.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class UpgradeFromAShippedVersionTests
{
    /// <summary>
    /// The settings file an install of <c>0.1.0.0-stable</c> left behind.
    /// </summary>
    private const string SettingsTheFirstShippedVersionLeft =
        "Configuration/Fixtures/plugin-configuration-written-by-0.1.0.0.xml";

    /// <summary>
    /// Reading that file with this version's class leaves every setting at the value a fresh
    /// install runs, because the older file names none of them.
    /// <para>
    /// Every property is walked rather than a chosen few, so a setting added tomorrow is covered on
    /// the day it is added rather than on the day somebody remembers to extend a list.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSettingsTheFirstShippedVersionLeftAreReadAndEverySettingKeepsItsFreshInstallValue()
    {
        var carried = Read(SettingsTheFirstShippedVersionLeft);
        var fresh = new PluginConfiguration();

        foreach (var setting in Settings())
        {
            Assert.Equal(setting.GetValue(fresh), setting.GetValue(carried));
        }
    }

    /// <summary>
    /// The four settings the install cannot run without, named one at a time.
    /// <para>
    /// The walk above would pass if the class itself stopped setting them, because it compares the
    /// upgraded value against whatever a fresh instance holds. These say what the values have to
    /// be, so an upgrade that lands a quota of zero fails here rather than passing there as a pair
    /// of equal zeroes.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUpgradeFromTheFirstShippedVersionDoesNotLandAnInstallThatRefusesEveryAsk()
    {
        var carried = Read(SettingsTheFirstShippedVersionLeft);

        Assert.Equal(10, carried.OpenRequestsPerUser);
        Assert.True(carried.AcceptsMovies);
        Assert.True(carried.AcceptsSeries);
        Assert.Equal(365, carried.FinishedRequestRetentionDays);
    }

    /// <summary>
    /// The upgraded install runs. This is what the test above is evidence for, and it is asserted
    /// rather than inferred: the rules are what the plugin applies on every read and on every save,
    /// and an upgrade producing something they refuse would be a server coming back up with this
    /// plugin declining to work.
    /// </summary>
    [Fact]
    public void AnInstallUpgradedFromTheFirstShippedVersionCanBeHonoured()
    {
        var carried = Read(SettingsTheFirstShippedVersionLeft);

        Assert.Empty(ConfigurationRules.Problems(carried));
        Assert.True(ConfigurationRules.CanBeHonoured(carried));

        // The call the plugin makes, rather than only the predicate behind it: the predicate being
        // true and the refusal not firing are two statements, and an operator meets the second.
        ConfigurationRules.RefuseWhatCannotWork(carried);
    }

    /// <summary>
    /// The fixture is the older shape rather than this one.
    /// <para>
    /// Without this the three tests above pass against a fixture somebody regenerated from the
    /// current class, which would carry every setting as an element and prove nothing about an
    /// upgrade. The older class carried no settings, so the older file names none: the document is
    /// one empty element.
    /// </para>
    /// </summary>
    [Fact]
    public void TheFixtureIsTheShapeTheOlderVersionWroteAndNotTheShapeThisOneWrites()
    {
        var document = new XmlDocument { XmlResolver = null };

        using (var reader = Reader(SettingsTheFirstShippedVersionLeft))
        {
            document.Load(reader);
        }

        Assert.NotNull(document.DocumentElement);
        Assert.Equal(nameof(PluginConfiguration), document.DocumentElement.Name);
        Assert.False(document.DocumentElement.HasChildNodes);

        // And this version would write something else, so the two shapes are not accidentally one
        // document. Named here rather than assumed, because the whole test rests on it.
        Assert.NotEmpty(Settings());
    }

    /// <summary>
    /// Every setting this version's configuration class holds.
    /// </summary>
    /// <returns>The settings, in a stable order.</returns>
    private static PropertyInfo[] Settings()
        => [.. typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.CanRead && property.CanWrite)
            .OrderBy(property => property.Name, StringComparer.Ordinal)];

    /// <summary>
    /// Reads a stored settings file with this version's class, the way the host does.
    /// </summary>
    /// <param name="fixture">The file, relative to the test output.</param>
    /// <returns>What this version makes of those bytes.</returns>
    private static PluginConfiguration Read(string fixture)
    {
        using var reader = Reader(fixture);

        var read = new XmlSerializer(typeof(PluginConfiguration)).Deserialize(reader);

        // A null here would be a reader that produced nothing, which every assertion downstream
        // would then report as a missing setting rather than as a file that was never read.
        return Assert.IsType<PluginConfiguration>(read);
    }

    /// <summary>
    /// A reader over a fixture, with entity resolution off.
    /// </summary>
    /// <param name="fixture">The file, relative to the test output.</param>
    /// <returns>The reader, which the caller disposes.</returns>
    /// <exception cref="FileNotFoundException">Where the fixture did not reach the test output.</exception>
    private static XmlReader Reader(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fixture);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The fixture {0} did not reach the test output, so this test would report on nothing.",
                    fixture),
                path);
        }

        return XmlReader.Create(
            path,
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
    }
}

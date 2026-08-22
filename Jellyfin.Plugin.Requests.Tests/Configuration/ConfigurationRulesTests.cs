using System;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Requests.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Configuration;

/// <summary>
/// What this plugin refuses to run on, and the two moments it refuses it: a save from the settings
/// page and a read of what is stored.
/// <para>
/// The values below are written as a boundary and the value one step outside it, rather than as a
/// number somewhere in the middle. A test asserting that -50 is refused passes for an
/// implementation that refuses everything below 40, and the mistake somebody actually makes is an
/// off-by-one in the comparison rather than a rule pointed the wrong way.
/// </para>
/// </summary>
public class ConfigurationRulesTests
{
    /// <summary>
    /// An address a notice could be posted to, for the rows whose subject is what happens once an
    /// operator has typed one.
    /// </summary>
    private const string SomewhereToPostTo = "https://example.invalid/hook";

    /// <summary>
    /// The settings no value of which can be refused, written out by hand.
    /// <para>
    /// A rule refuses a configuration this plugin cannot run on. A switch over a path that does
    /// nothing but the thing it names has no such value: on is a working install and off is a
    /// working install, and a rule invented for it would be a refusal with no failure behind it,
    /// which is worse than an absence because a reader takes it for one that bites.
    /// </para>
    /// <para>
    /// The list is here rather than nowhere so that the leg below still catches the case it exists
    /// for. A setting added later is a name in the class that is neither refused by a rule nor on
    /// this list, and whoever adds it has to say which of the two it is. Both directions are closed:
    /// a name here that no longer exists fails as well, so the exemption cannot outlive the setting.
    /// </para>
    /// </summary>
    private static readonly string[] SettingsNoValueOfWhichCanBeRefused =
        ["TellsAdministratorsAboutArrivals"];

    /// <summary>
    /// The last value each rule accepts, and the first one it refuses, with the setting the refusal
    /// has to name. One row per rule, and the row for the accepted kinds names both switches because
    /// either of them fixes it.
    /// </summary>
    /// <returns>A configuration just inside the rule, one just outside it, and the settings named.</returns>
    public static TheoryData<string, PluginConfiguration, PluginConfiguration, string[]> EachRuleAtItsBoundary()
        => new TheoryData<string, PluginConfiguration, PluginConfiguration, string[]>
        {
            {
                "the quota",
                Fresh(configuration => configuration.OpenRequestsPerUser = ConfigurationRules.SmallestQuota),
                Fresh(configuration => configuration.OpenRequestsPerUser = ConfigurationRules.SmallestQuota - 1),
                [nameof(PluginConfiguration.OpenRequestsPerUser)]
            },
            {
                "the retention period",
                Fresh(configuration => configuration.FinishedRequestRetentionDays = PluginConfiguration.MinimumRetentionDays),
                Fresh(configuration => configuration.FinishedRequestRetentionDays = PluginConfiguration.MinimumRetentionDays - 1),
                [nameof(PluginConfiguration.FinishedRequestRetentionDays)]
            },
            {
                "the accepted kinds",
                Fresh(configuration =>
                {
                    configuration.AcceptsMovies = false;
                    configuration.AcceptsSeries = true;
                }),
                Fresh(configuration =>
                {
                    configuration.AcceptsMovies = false;
                    configuration.AcceptsSeries = false;
                }),
                [nameof(PluginConfiguration.AcceptsMovies), nameof(PluginConfiguration.AcceptsSeries)]
            },
            {
                "the announcements",
                Fresh(configuration =>
                {
                    configuration.OutboundNoticeAddress = SomewhereToPostTo;
                    configuration.AnnouncesApprovals = false;
                    configuration.AnnouncesDeclines = false;
                    configuration.AnnouncesFulfilments = true;
                }),
                Fresh(configuration =>
                {
                    configuration.OutboundNoticeAddress = SomewhereToPostTo;
                    configuration.AnnouncesApprovals = false;
                    configuration.AnnouncesDeclines = false;
                    configuration.AnnouncesFulfilments = false;
                }),
                [
                    nameof(PluginConfiguration.AnnouncesApprovals),
                    nameof(PluginConfiguration.AnnouncesDeclines),
                    nameof(PluginConfiguration.AnnouncesFulfilments)
                ]
            }
        };

    /// <summary>
    /// A fresh install is a configuration this plugin runs on. Without this leg every other one
    /// below passes for a rule set that refuses everything.
    /// </summary>
    [Fact]
    public void AFreshInstallIsAConfigurationThisPluginCanRunOn()
    {
        Assert.Empty(ConfigurationRules.Problems(new PluginConfiguration()));
        Assert.True(ConfigurationRules.CanBeHonoured(new PluginConfiguration()));
    }

    /// <summary>
    /// The last value a rule accepts is accepted.
    /// </summary>
    /// <param name="rule">Which rule this row is about, so a failure says which.</param>
    /// <param name="inside">A configuration at the last value the rule allows.</param>
    /// <param name="outside">The first value it refuses, which this leg does not read.</param>
    /// <param name="named">The settings the refusal names, which this leg does not read.</param>
    [Theory]
    [MemberData(nameof(EachRuleAtItsBoundary))]
    public void TheLastValueEachRuleAllowsIsAccepted(
        string rule,
        PluginConfiguration inside,
        PluginConfiguration outside,
        string[] named)
    {
        Assert.NotEmpty(rule);
        Assert.NotNull(outside);
        Assert.NotEmpty(named);

        Assert.Empty(ConfigurationRules.Problems(inside));
    }

    /// <summary>
    /// The first value outside a rule is refused, and the refusal names the field rather than saying
    /// only that something is wrong. A refusal an operator cannot act on is a refusal that sends
    /// them to the source.
    /// </summary>
    /// <param name="rule">Which rule this row is about, so a failure says which.</param>
    /// <param name="inside">The last value it allows, which this leg does not read.</param>
    /// <param name="outside">A configuration one step outside the rule.</param>
    /// <param name="named">The settings the refusal has to name.</param>
    [Theory]
    [MemberData(nameof(EachRuleAtItsBoundary))]
    public void TheFirstValueOutsideEachRuleIsRefusedAndNamesTheField(
        string rule,
        PluginConfiguration inside,
        PluginConfiguration outside,
        string[] named)
    {
        Assert.NotEmpty(rule);
        Assert.NotNull(inside);

        var problems = ConfigurationRules.Problems(outside);

        Assert.Equal(named, Settings(problems.Select(problem => problem.Setting)));
        Assert.All(problems, problem => Assert.Contains(problem.Setting, problem.Why, StringComparison.Ordinal));
        Assert.False(ConfigurationRules.CanBeHonoured(outside));
    }

    /// <summary>
    /// Every setting is reached by a rule, or is named above as one no value of which can be
    /// refused. This is the leg that catches a setting added later with nothing judging it: the
    /// hostile configuration below is written by hand, so a new setting is a name in the class that
    /// is in neither answer, and whoever adds it has to say what value of it cannot work or why
    /// none can.
    /// </summary>
    [Fact]
    public void EverySettingIsNamedByARuleOrDeclaredUnrefusable()
    {
        var hostile = new PluginConfiguration
        {
            OpenRequestsPerUser = 0,
            AcceptsMovies = false,
            AcceptsSeries = false,
            FinishedRequestRetentionDays = 0,
            OutboundNoticeAddress = "example.invalid/hook",
            AnnouncesApprovals = false,
            AnnouncesDeclines = false,
            AnnouncesFulfilments = false
        };

        var declared = typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(setting => setting.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var named = Settings(ConfigurationRules.Problems(hostile).Select(problem => problem.Setting));

        // The exemption cannot outlive the setting it exempts, so it is checked against the class
        // before it is allowed to make up the difference.
        Assert.Equal(
            SettingsNoValueOfWhichCanBeRefused.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            SettingsNoValueOfWhichCanBeRefused
                .Where(declared.Contains)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        Assert.Equal(
            declared,
            Settings(named.Concat(SettingsNoValueOfWhichCanBeRefused)));
    }

    /// <summary>
    /// Nothing is corrected on the way through. The rules answer about the configuration they were
    /// given and hand back no second copy, so there is no shape in which a caller receives a value
    /// nobody chose without being able to tell.
    /// </summary>
    [Fact]
    public void AConfigurationThatIsRefusedIsNotChanged()
    {
        var configuration = new PluginConfiguration { OpenRequestsPerUser = -4 };

        Assert.NotEmpty(ConfigurationRules.Problems(configuration));
        Assert.Equal(-4, configuration.OpenRequestsPerUser);
    }

    /// <summary>
    /// The refusal carries every problem rather than the first. An operator who hand-edited the file
    /// and is told one mistake at a time restarts the server once per mistake.
    /// </summary>
    [Fact]
    public void ARefusalCarriesEveryProblemRatherThanTheFirst()
    {
        var configuration = new PluginConfiguration
        {
            OpenRequestsPerUser = 0,
            FinishedRequestRetentionDays = 1
        };

        var refused = Assert.Throws<InvalidConfigurationException>(
            () => ConfigurationRules.RefuseWhatCannotWork(configuration));

        string[] expected =
            [nameof(PluginConfiguration.FinishedRequestRetentionDays), nameof(PluginConfiguration.OpenRequestsPerUser)];

        Assert.Equal(expected, Settings(refused.Problems.Select(problem => problem.Setting)));

        Assert.All(
            refused.Problems,
            problem => Assert.Contains(problem.Why, refused.Message, StringComparison.Ordinal));
    }

    /// <summary>
    /// Saving a value this install cannot run on is refused, and nothing reaches the disk. The half
    /// that matters is the second one: a save that is refused after the file has been replaced has
    /// cost the operator the configuration that was working.
    /// </summary>
    [Fact]
    public void SavingAValueThisInstallCannotRunOnIsRefusedAndWritesNothing()
    {
        using var host = new Doubles.PluginHost();

        // A configuration this install can run on first, so what the refused save is measured
        // against is a stored one rather than the absence of a file.
        host.Plugin.UpdateConfiguration(new PluginConfiguration { OpenRequestsPerUser = 7 });

        var before = host.XmlSerializer.WriteCount;

        var refused = Assert.Throws<InvalidConfigurationException>(
            () => host.Plugin.UpdateConfiguration(new PluginConfiguration { OpenRequestsPerUser = 0 }));

        Assert.Equal(nameof(PluginConfiguration.OpenRequestsPerUser), Assert.Single(refused.Problems).Setting);
        Assert.Equal(before, host.XmlSerializer.WriteCount);
        Assert.Equal(7, host.Plugin.Configuration.OpenRequestsPerUser);
    }

    /// <summary>
    /// A save this install can run on is taken. Without this the leg above passes for a plugin that
    /// refuses every save.
    /// </summary>
    [Fact]
    public void ASaveThisInstallCanRunOnIsTaken()
    {
        using var host = new Doubles.PluginHost();

        var before = host.XmlSerializer.WriteCount;

        host.Plugin.UpdateConfiguration(new PluginConfiguration { OpenRequestsPerUser = 3 });

        Assert.Equal(3, host.Plugin.Configuration.OpenRequestsPerUser);
        Assert.True(host.XmlSerializer.WriteCount > before);
    }

    /// <summary>
    /// Reading a stored configuration this install cannot run on is a refusal rather than a value
    /// somebody else chose. This is the half the settings page cannot cover: the file is editable by
    /// hand, and a plugin that starts on a silently corrected number does something other than what
    /// its own settings page shows.
    /// </summary>
    [Fact]
    public void ReadingAStoredConfigurationThisInstallCannotRunOnIsRefused()
    {
        var stored = new PluginConfiguration { AcceptsMovies = false, AcceptsSeries = false };
        var settings = new ServerInstallSettings(() => stored);

        var refused = Assert.Throws<InvalidConfigurationException>(() => settings.Current);

        string[] expected = [nameof(PluginConfiguration.AcceptsMovies), nameof(PluginConfiguration.AcceptsSeries)];

        Assert.Equal(expected, Settings(refused.Problems.Select(problem => problem.Setting)));
    }

    /// <summary>
    /// A stored configuration this install can run on is handed back as it is. The object itself,
    /// rather than a copy, because a copy is where a corrected value would be able to hide.
    /// </summary>
    [Fact]
    public void AStoredConfigurationThisInstallCanRunOnIsHandedBackAsItIs()
    {
        var stored = new PluginConfiguration { OpenRequestsPerUser = 2 };

        Assert.Same(stored, new ServerInstallSettings(() => stored).Current);
    }

    /// <summary>
    /// Asking what an install is set to before the plugin was loaded says that, rather than
    /// answering with the defaults. An answer built out of nothing is one a caller cannot tell from
    /// a fresh install.
    /// </summary>
    [Fact]
    public void AskingBeforeThePluginWasLoadedSaysSoRatherThanAnsweringWithDefaults()
    {
        var settings = new ServerInstallSettings(() => null);

        Assert.Throws<InvalidOperationException>(() => settings.Current);
    }

    /// <summary>
    /// The settings a set of problems names, deduplicated and in name order, which is the shape
    /// every comparison above is written in.
    /// </summary>
    /// <param name="named">The settings the problems named.</param>
    /// <returns>The distinct names, in order.</returns>
    private static string[] Settings(System.Collections.Generic.IEnumerable<string> named)
        => [.. named.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal)];

    /// <summary>
    /// One configuration, with whatever this row changes about a fresh install.
    /// </summary>
    /// <param name="change">What this row is about.</param>
    /// <returns>The configuration.</returns>
    private static PluginConfiguration Fresh(Action<PluginConfiguration> change)
    {
        var configuration = new PluginConfiguration();
        change(configuration);

        return configuration;
    }
}

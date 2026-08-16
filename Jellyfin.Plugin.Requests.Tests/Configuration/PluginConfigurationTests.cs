using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Jellyfin.Plugin.Requests.Configuration;
using Xunit;

using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests.Configuration;

/// <summary>
/// The settings an operator can change, what they are when nobody changes them, and the two ways a
/// setting stops being usable: the page cannot reach it, or the document says something else about
/// it.
/// <para>
/// The expected list below is written out by hand rather than read from the class. A list derived
/// from the class would agree with whatever the class happened to say, including on the day a
/// setting is added with a default nobody argued for, which is the failure this whole page is
/// against: almost every install runs the defaults.
/// </para>
/// </summary>
public class PluginConfigurationTests
{
    /// <summary>
    /// Every setting this plugin has, with the value a fresh install runs. This is the whole
    /// configuration surface, and a change to any of them is a change to this list with a reason in
    /// the commit that made it.
    /// </summary>
    /// <returns>Each setting and its default, as a reader of the document would write it.</returns>
    public static TheoryData<string, string> TheSettingsAndTheirDefaults()
        => new TheoryData<string, string>
        {
            { "AcceptsMovies", "true" },
            { "AcceptsSeries", "true" },
            { "FinishedRequestRetentionDays", "365" },
            { "OpenRequestsPerUser", "10" },
            { "OutboundNoticeAddress", string.Empty }
        };

    /// <summary>
    /// The class carries those settings and no others. Without the second half a setting added
    /// later is invisible here: the theory below would still pass, because it only asks about the
    /// settings it names.
    /// </summary>
    [Fact]
    public void TheConfigurationIsExactlyTheSettingsOnThatList()
    {
        var expected = TheSettingsAndTheirDefaults()
            .Select(row => (string)row[0])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, Names());
    }

    /// <summary>
    /// A fresh install runs the value written on that list. This is the half that catches a default
    /// changed while the setting stays, which no reader of a diff notices and every install feels.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <param name="expected">What it is when nobody has changed anything.</param>
    [Theory]
    [MemberData(nameof(TheSettingsAndTheirDefaults))]
    public void AFreshInstallRunsTheValueOnThatList(string setting, string expected)
    {
        Assert.Equal(expected, Printed(Setting(setting).GetValue(new PluginConfiguration())));
    }

    /// <summary>
    /// A fresh install is usable: somebody can ask for something, and one person cannot fill the
    /// disk while doing it. Written as the properties rather than as the numbers, so it still says
    /// something the day the numbers move.
    /// </summary>
    [Fact]
    public void AFreshInstallAcceptsSomethingAndBoundsWhatOnePersonCanOpen()
    {
        var fresh = new PluginConfiguration();

        Assert.True(fresh.AcceptsMovies || fresh.AcceptsSeries);
        Assert.True(fresh.OpenRequestsPerUser > 0);
        Assert.True(fresh.FinishedRequestRetentionDays >= PluginConfiguration.MinimumRetentionDays);
    }

    /// <summary>
    /// One setting holds text and it is the address a notice is posted to. Every other setting is a
    /// number or a switch, so there is nowhere else here for an address or a credential to be typed.
    /// <para>
    /// This used to say that no setting could hold either, which was the guard on a plugin that made
    /// no outbound call at all. The outbound sink is that call, and it needs somewhere for an
    /// operator to say where, so the guard narrows to the one field rather than being deleted: what
    /// it is really for is a second one arriving quietly, and a credential is the second one it is
    /// most against. Where a credential lives and what may honestly be claimed about it is #85's
    /// decision rather than a field added while doing something else.
    /// </para>
    /// <para>
    /// It is written over the shape rather than over the names, because a name check passes for
    /// whatever somebody called the field.
    /// </para>
    /// </summary>
    [Fact]
    public void TheOnlySettingThatHoldsTextIsTheAddressANoticeIsPostedTo()
    {
        var carrying = Settings()
            .Where(setting => setting.PropertyType != typeof(int) && setting.PropertyType != typeof(bool))
            .Select(setting => string.Concat(setting.Name, " is ", setting.PropertyType.Name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["OutboundNoticeAddress is String"], carrying);
    }

    /// <summary>
    /// Every setting is on the settings page, under a field named after it.
    /// <para>
    /// This is the condition that keeps a setting from existing where nobody can change it. A
    /// setting an operator cannot reach is worse than one that does not exist: the document promises
    /// it, and the only way to set it is to write the plugin's configuration file by hand on the
    /// server.
    /// </para>
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <param name="expected">
    /// What it is on a fresh install, which this leg does not read. It is not asserted to be
    /// anything here, because one setting's default is deliberately nothing at all: an install with
    /// no address typed into it sends nothing anywhere.
    /// </param>
    [Theory]
    [MemberData(nameof(TheSettingsAndTheirDefaults))]
    public void EverySettingIsReachableFromTheSettingsPage(string setting, string expected)
    {
        Assert.NotNull(expected);

        var page = SettingsPage();

        Assert.Contains(string.Concat("id=\"Requests", setting, "\""), page, StringComparison.Ordinal);
        Assert.Contains(string.Concat(setting, ": \"#Requests", setting, "\""), page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The settings printed in <c>docs/configuration.md</c> are the settings in the class, with the
    /// defaults the class runs.
    /// </summary>
    [Fact]
    public void TheDocumentedSettingsAreTheSettingsInTheCode()
    {
        var rows = MarkedSection("settings").Where(line => line.StartsWith('|')).ToArray();

        // The header names the columns and the dashes under it are the table's own furniture.
        // Everything after them is a setting, so one deleted from the document fails the comparison
        // at the end rather than being skipped here.
        var documented = rows.Skip(2).Select(row => string.Join(' ', SplitRow(row))).ToArray();

        var fresh = new PluginConfiguration();
        var expected = Settings()
            .OrderBy(setting => setting.Name, StringComparer.Ordinal)
            .Select(setting => string.Concat(setting.Name, " ", Printed(setting.GetValue(fresh))))
            .ToArray();

        Assert.Equal(expected, documented);
    }

    /// <summary>
    /// The settings this class declares, in name order. The base class the server supplies is not
    /// part of this plugin's surface and is left out of every leg above.
    /// </summary>
    /// <returns>One entry per setting.</returns>
    private static PropertyInfo[] Settings()
        => [.. typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(setting => setting.Name, StringComparer.Ordinal)];

    private static string[] Names() => [.. Settings().Select(setting => setting.Name)];

    private static PropertyInfo Setting(string name)
        => Settings().Single(setting => string.Equals(setting.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// One value as the document writes it. A switch is written the way the page and the stored
    /// configuration write it rather than the way this language prints it, because the document is
    /// read by somebody looking at one of those two.
    /// </summary>
    /// <param name="value">The value a fresh install carries.</param>
    /// <returns>What the document holds for it.</returns>
    private static string Printed(object? value)
        => value switch
        {
            bool switched => switched ? "true" : "false",
            null => throw new InvalidOperationException("A setting with no value has no default to document."),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };

    /// <summary>
    /// The settings page as the assembly carries it, rather than as the file sits in the tree. A
    /// page that is not embedded is a page the dashboard cannot serve, whatever the tree holds.
    /// </summary>
    /// <returns>The page.</returns>
    private static string SettingsPage()
    {
        using var host = new Doubles.PluginHost();

        var resource = host.Plugin
            .GetPages()
            .Single(page => string.Equals(page.Name, host.Plugin.Name, StringComparison.Ordinal))
            .EmbeddedResourcePath;

        using var stream = typeof(PluginUnderTest).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The assembly carries no resource named {resource}.");

        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    private static string[] SplitRow(string row)
        => [.. row.Trim('|').Split('|').Select(cell => cell.Trim())];

    /// <summary>
    /// Reads the lines the document marks off for one section.
    /// </summary>
    /// <param name="name">The name in the marker comments.</param>
    /// <returns>The trimmed lines between the markers.</returns>
    private static string[] MarkedSection(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "docs", "configuration.md");
        Assert.True(File.Exists(path), FormattableString.Invariant($"{path} was not copied next to the suite."));

        var opening = FormattableString.Invariant($"<!-- {name} begins -->");
        var closing = FormattableString.Invariant($"<!-- {name} ends -->");
        var inside = false;
        var lines = new List<string>();

        foreach (var line in File.ReadLines(path))
        {
            if (line.Trim().Equals(opening, StringComparison.Ordinal))
            {
                inside = true;
                continue;
            }

            if (line.Trim().Equals(closing, StringComparison.Ordinal))
            {
                inside = false;
                continue;
            }

            if (inside && line.Trim().Length > 0)
            {
                lines.Add(line.Trim());
            }
        }

        Assert.True(lines.Count > 0, FormattableString.Invariant($"docs/configuration.md has no {name} section."));

        return [.. lines];
    }
}

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Localisation;
using Xunit;
using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests.Web;

/// <summary>
/// The column the operator's queue draws about an external request service, and the rule that it is
/// drawn only where one is configured.
/// <para>
/// Everything here is read off the page the assembly ships rather than out of a browser. The headless
/// rule in <c>docs/testing.md</c> is why, and what it costs is stated at the leg it costs something:
/// nothing here proves a column appeared on a screen, only that the page names the three answers, and
/// that what it reads them from is a field the endpoint actually answers.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class HandoverColumnTests
{
    /// <summary>
    /// The three answers, one per state a request can be in about a handover.
    /// </summary>
    private static readonly string[] Answers =
    [
        "queue.handover.accepted",
        "queue.handover.failed",
        "queue.handover.notTried"
    ];

    /// <summary>
    /// Three states are named by three different keys, so none of them is drawn as an empty cell.
    /// <para>
    /// This is the condition of #283 that is easiest to lose. A column that spoke only where there
    /// was a reference would leave the two states that carry none - nothing tried, and tried and
    /// refused - reading as the same blank, and the blank one an operator has to act on is the one
    /// that looks like the ordinary case.
    /// </para>
    /// </summary>
    [Fact]
    public void TheThreeStatesAreThreeSentencesRatherThanTwoAndAnEmptyCell()
    {
        var body = QueuePage();

        foreach (var key in Answers)
        {
            Assert.Contains(key, body, StringComparison.Ordinal);
        }

        Assert.Equal(3, Answers.Distinct(StringComparer.Ordinal).Count());

        var shipped = StringCatalogue.Shipped.For(null);

        foreach (var key in Answers)
        {
            Assert.True(shipped.ContainsKey(key), key);
        }
    }

    /// <summary>
    /// The page decides whether to draw the column from a field the capabilities answer declares.
    /// <para>
    /// The near-miss this leg exists for is silent in the worst way. A page reading a name the
    /// endpoint does not answer gets <c>undefined</c>, which is not true, so the column is never
    /// drawn and every server looks like a server with no external service. Nothing would be red and
    /// nothing would be logged.
    /// </para>
    /// </summary>
    [Fact]
    public void WhatThePageAsksTheInstallAboutIsAFieldTheAnswerCarries()
    {
        var body = QueuePage();

        Assert.Contains("BridgeConfigured", body, StringComparison.Ordinal);

        var declared = typeof(InstallCapabilities)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        Assert.Contains("BridgeConfigured", declared, StringComparer.Ordinal);
    }

    /// <summary>
    /// A server with no external service configured has the column taken out of the page rather than
    /// hidden in it.
    /// <para>
    /// Removed and not hidden, because "says nothing about a bridge at all" is a claim about what is
    /// in the page rather than about what is painted. A hidden header is still a heading a screen
    /// reader reaches and still a cell in every row, which is the page telling an operator about a
    /// bridge they never configured.
    /// </para>
    /// </summary>
    [Fact]
    public void WhereNoServiceIsConfiguredTheColumnIsRemovedFromThePage()
    {
        var body = QueuePage();

        Assert.Contains("requestsQueueHandoverColumn", body, StringComparison.Ordinal);
        Assert.Contains("column.remove()", body, StringComparison.Ordinal);

        // The flag starts false, so a capabilities answer that never arrives draws no column rather
        // than a column of sentences about a service this page could not ask about.
        Assert.Contains("var bridged = false;", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The queue page as the built assembly carries it.
    /// </summary>
    /// <returns>The page, inline script and all.</returns>
    private static string QueuePage()
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

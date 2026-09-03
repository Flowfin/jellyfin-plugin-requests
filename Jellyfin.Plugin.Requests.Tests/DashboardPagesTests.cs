using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using MediaBrowser.Model.Plugins;
using Xunit;

using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests;

/// <summary>
/// The pages this plugin puts in the dashboard, checked against the assembly they are served out
/// of.
/// <para>
/// The failure these exist against is silent in every earlier route. A registration naming a
/// resource the assembly does not carry builds clean, installs clean, and is a blank page in the
/// dashboard of somebody's server. So is a page asking for a shared file under a name nothing
/// registered.
/// </para>
/// </summary>
public class DashboardPagesTests
{
    /// <summary>
    /// Every registration names a resource the built assembly actually carries. This is the check
    /// the namespace rename broke once and would break again: the path is derived from the
    /// namespace at run time and the resource name is derived from it by the build, so the two
    /// agree only while nothing has moved.
    /// </summary>
    [Fact]
    public void PagesAreServedFromResourcesTheAssemblyCarries()
    {
        using var host = new PluginHost();

        var embedded = typeof(PluginUnderTest).Assembly.GetManifestResourceNames();
        var missing = host.Plugin
            .GetPages()
            .Select(page => page.EmbeddedResourcePath)
            .Where(path => !embedded.Contains(path, StringComparer.Ordinal))
            .ToArray();

        Assert.Empty(missing);
    }

    /// <summary>
    /// The queue and the settings are two pages rather than one, and each is registered once. Two
    /// registrations under one name is a page the dashboard picks one of, and which one it picks is
    /// not something this repository decides.
    /// </summary>
    [Fact]
    public void TheQueueAndTheSettingsAreSeparatePagesAndEveryNameIsRegisteredOnce()
    {
        using var host = new PluginHost();

        var pages = host.Plugin.GetPages().ToArray();
        var names = pages.Select(page => page.Name).ToArray();

        Assert.Contains(PluginUnderTest.QueuePageName, names, StringComparer.Ordinal);
        Assert.Contains(host.Plugin.Name, names, StringComparer.Ordinal);
        Assert.NotEqual(PluginUnderTest.QueuePageName, host.Plugin.Name, StringComparer.Ordinal);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The queue asks to be in the dashboard's own menu and the settings do not. An operator opens
    /// the queue every day, and a queue reachable only by finding the plugin in a list is a queue
    /// they open through the settings page they were not looking for.
    /// </summary>
    [Fact]
    public void OnlyTheQueueAsksForAPlaceInTheDashboardMenu()
    {
        using var host = new PluginHost();

        var inMenu = host.Plugin
            .GetPages()
            .Where(page => page.EnableInMainMenu)
            .Select(page => page.Name)
            .ToArray();

        Assert.Equal([PluginUnderTest.QueuePageName], inMenu);
    }

    /// <summary>
    /// Every shared file a page fetches by name is registered under that name. A page asking for
    /// one that is not is a page whose stylesheet or script is a 404, which leaves the page loading
    /// and doing nothing.
    /// </summary>
    [Fact]
    public void EveryNameThePagesAskForIsARegisteredPage()
    {
        using var host = new PluginHost();

        var registered = host.Plugin.GetPages().Select(page => page.Name).ToArray();
        var asked = host.Plugin
            .GetPages()
            .Where(page => page.EmbeddedResourcePath.EndsWith(".html", StringComparison.Ordinal))
            .SelectMany(page => NamesFetchedBy(Text(page.EmbeddedResourcePath)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var unregistered = asked.Where(name => !registered.Contains(name, StringComparer.Ordinal)).ToArray();

        Assert.NotEmpty(asked);
        Assert.Empty(unregistered);
    }

    /// <summary>
    /// The shared script names the same pages the plugin registered. The strip of links is drawn
    /// from that list, so a page renamed in one place and not the other is a link to nothing, and
    /// the page that link was for highlights none of its entries.
    /// </summary>
    [Fact]
    public void TheSharedScriptNamesThePagesThatAreRegistered()
    {
        using var host = new PluginHost();

        var script = Text(host.Plugin
            .GetPages()
            .Single(page => string.Equals(page.Name, "Requests.js", StringComparison.Ordinal))
            .EmbeddedResourcePath);

        var registered = host.Plugin
            .GetPages()
            .Where(page => page.EmbeddedResourcePath.EndsWith(".html", StringComparison.Ordinal))
            .Select(page => page.Name);

        foreach (var name in registered)
        {
            Assert.Contains("name: \"" + name + "\"", script, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every link the strip draws is a hash route.
    /// <para>
    /// That is the half of "navigation without a full page reload" this repository can check. A link
    /// to a document address is what makes the dashboard load itself again and lose whatever the
    /// operator had open; a hash route is a change of fragment, which is what the dashboard's own
    /// router answers by swapping the content. What the router then does is the dashboard's
    /// behaviour and is not measured here, because measuring it means driving a browser, which
    /// <c>docs/testing.md</c> refuses.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryLinkTheStripDrawsIsAHashRoute()
    {
        using var host = new PluginHost();

        var script = Text(host.Plugin
            .GetPages()
            .Single(page => string.Equals(page.Name, "Requests.js", StringComparison.Ordinal))
            .EmbeddedResourcePath);

        var assignments = script
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("link.href", StringComparison.Ordinal))
            .ToArray();

        var elsewhere = assignments
            .Where(line => !line.StartsWith("link.href = \"#/", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(assignments);
        Assert.Empty(elsewhere);
    }

    /// <summary>
    /// Nothing the pages carry loads anything from outside this server. A page that fetches a
    /// framework from somewhere else puts the dashboard of a media server behind whoever is serving
    /// it that day, and this milestone's rule is that the pages use only what the server already
    /// provides.
    /// </summary>
    [Fact]
    public void NothingThePagesLoadComesFromOutsideThisServer()
    {
        using var host = new PluginHost();

        var offSite = host.Plugin
            .GetPages()
            .Select(page => new { page.Name, Body = Text(page.EmbeddedResourcePath) })
            .Where(page => page.Body.Contains("//", StringComparison.Ordinal))
            .Where(page => page.Body.Contains("http://", StringComparison.OrdinalIgnoreCase)
                || page.Body.Contains("https://", StringComparison.OrdinalIgnoreCase))
            .Select(page => page.Name)
            .ToArray();

        Assert.Empty(offSite);
    }

    /// <summary>
    /// The names the pages fetch through the dashboard's configuration-page endpoint, read out of
    /// the page itself rather than restated here.
    /// </summary>
    /// <param name="body">The page as it is embedded.</param>
    /// <returns>Every name it asks for.</returns>
    private static IEnumerable<string> NamesFetchedBy(string body)
    {
        const string Marker = "\"web/ConfigurationPage\", { name: \"";

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
    /// The settings page never puts the stored outbound notice address into an element.
    /// <para>
    /// The field is write-only, decided on #113 as the answer to #100, and the failure this
    /// exists against is the shape the page had before: a value the page fetched sat in the markup
    /// of an administrator's screen, in whatever they photographed of it, and was sent back on
    /// every save made for any other setting. Both halves are removed by the same change, and this
    /// holds the first of them.
    /// </para>
    /// <para>
    /// What it reads is every assignment into a <c>value</c> on the page, and what it refuses is
    /// one that reaches the address. The bound: an assignment written across two lines is invisible
    /// to it, and so is a value put into an element some other way. It catches the write somebody
    /// makes when the field comes back, which is the one that happened.
    /// </para>
    /// <para>
    /// It also asserts the page still writes the setting, so a page that dropped the field
    /// altogether cannot pass by carrying nothing to refuse.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSettingsPageNeverDrawsAStoredSecret()
    {
        using var host = new PluginHost();

        var page = Text(host.Plugin
            .GetPages()
            .Single(entry => string.Equals(entry.Name, host.Plugin.Name, StringComparison.Ordinal))
            .EmbeddedResourcePath);

        // Every marked setting, read off the mark, so a secret added later is held to the same
        // shape as the two that exist rather than to a name written here.
        var marked = typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(setting => setting.GetCustomAttribute<SecretAttribute>() is not null)
            .Select(setting => setting.Name)
            .ToArray();

        Assert.NotEmpty(marked);

        foreach (var setting in marked)
        {
            var drawn = page
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Contains(".value =", StringComparison.Ordinal))
                .Where(line => line.Contains(setting, StringComparison.Ordinal))
                .ToArray();

            Assert.Contains(string.Concat("config.", setting, " ="), page, StringComparison.Ordinal);
            Assert.Empty(drawn);
        }
    }

    /// <summary>
    /// One embedded file, as text.
    /// </summary>
    /// <param name="resource">The manifest resource name.</param>
    /// <returns>What the assembly carries under it.</returns>
    private static string Text(string resource)
    {
        using var stream = typeof(PluginUnderTest).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The assembly carries no resource named {resource}.");

        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }
}

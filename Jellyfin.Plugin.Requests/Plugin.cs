using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Requests.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Requests;

/// <summary>
/// The main plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// The name the queue page is registered and fetched under. It is a constant because the page
    /// itself names it when it marks which link in the shared strip is the current one, and two
    /// spellings of one page name is a strip that highlights nothing.
    /// </summary>
    public const string QueuePageName = "RequestsQueue";

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Requests";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("0f9c9107-b31b-459e-81fa-6d35dac25e79");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// The pages this plugin puts in the dashboard, and the two files they share.
    /// <para>
    /// Two pages rather than one, because an operator opens the queue every day and the settings
    /// twice, and one page carrying both would put the settings in the way of the work. The queue
    /// asks to be in the dashboard's own menu for the same reason; the settings stay where the
    /// dashboard already puts a plugin's settings, which is the plugin list.
    /// </para>
    /// <para>
    /// The stylesheet and the script are registered as pages as well. An embedded resource is
    /// reachable only under a name registered here, so a shared file that is not in this list is a
    /// file neither page can fetch. Their registered names carry an extension because that is what
    /// the two pages ask for them by, and nothing else reads them.
    /// </para>
    /// <para>
    /// <see cref="Name"/> stays the name of the settings page. The dashboard sends somebody who
    /// presses a plugin's settings to the page registered under the plugin's own name, so moving it
    /// to the queue would put the queue behind a button that says settings.
    /// </para>
    /// </summary>
    /// <returns>The pages, in the order they were registered.</returns>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            Page(Name, "Configuration.configPage.html"),
            new PluginPageInfo
            {
                Name = QueuePageName,
                EmbeddedResourcePath = Resource("Web.queue.html"),
                DisplayName = "Requests",
                EnableInMainMenu = true,
                MenuIcon = "playlist_add_check"
            },
            Page("Requests.js", "Web.shell.js"),
            Page("Requests.css", "Web.shell.css")
        ];
    }

    /// <summary>
    /// One registered page, built from the resource path the assembly actually carries.
    /// </summary>
    /// <param name="name">The name the dashboard fetches it under.</param>
    /// <param name="resource">The resource, relative to this plugin's namespace.</param>
    /// <returns>The page.</returns>
    private PluginPageInfo Page(string name, string resource)
        => new PluginPageInfo { Name = name, EmbeddedResourcePath = Resource(resource) };

    /// <summary>
    /// The manifest resource name for one embedded file. Built from the namespace at run time
    /// rather than written out, because the path is derived from the namespace by the build and a
    /// copy of it here would be a second spelling that a rename leaves behind.
    /// </summary>
    /// <param name="resource">The resource, relative to this plugin's namespace.</param>
    /// <returns>The full resource name.</returns>
    private string Resource(string resource)
        => string.Format(CultureInfo.InvariantCulture, "{0}.{1}", GetType().Namespace, resource);
}

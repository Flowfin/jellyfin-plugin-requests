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

    /// <inheritdoc />
    /// <exception cref="InvalidConfigurationException">
    /// Where the settings that arrived are ones this plugin cannot run on. The save is refused
    /// before anything is written, so the file on disk still holds the configuration that was
    /// working, and an operator who typed a number by mistake has not lost the one they had.
    /// <para>
    /// The dashboard is not the only way a configuration arrives, so this is half of the check
    /// rather than the whole of it. <see cref="ServerInstallSettings"/> is the other half, on the
    /// read, and both call <see cref="ConfigurationRules"/> so the page and the file cannot be
    /// judged by two different lists.
    /// </para>
    /// </exception>
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Only this plugin's own shape is judged. Anything else is a caller handing the wrong type
        // to the wrong plugin, which the base class already answers, and a second answer here would
        // be this plugin deciding what that failure looks like.
        if (configuration is PluginConfiguration settings)
        {
            ConfigurationRules.RefuseWhatCannotWork(settings);
        }

        base.UpdateConfiguration(configuration);
    }

    /// <summary>
    /// What an uninstall removes, which is nothing this plugin wrote.
    /// <para>
    /// Two files are on the disk and both are the operator's own: the queue is what people asked
    /// for and the settings are what the operator chose. Deleting either here is a delete with no
    /// undo, no copy and no warning, taken on a click somebody may have made by mistake.
    /// </para>
    /// <para>
    /// <b>Nothing says which kind of uninstall this is.</b> The server calls this with no argument,
    /// so a removal meant to be final and one that is a step in putting the plugin back are the
    /// same call, and this side cannot tell them apart. A decision that is right for one of them
    /// and destroys data on the other is not a decision, it is a coin toss on somebody else's
    /// queue.
    /// </para>
    /// <para>
    /// The other half of the answer is not code. What stays, and the command that removes it, are
    /// in <c>docs/configuration.md</c>, because a plugin that leaves data behind and does not say
    /// so is the reason people distrust uninstalling anything.
    /// </para>
    /// </summary>
    public override void OnUninstalling()
    {
        base.OnUninstalling();
    }

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

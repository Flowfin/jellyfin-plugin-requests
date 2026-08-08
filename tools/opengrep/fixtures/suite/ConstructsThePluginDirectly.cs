// Fixture for plugin-constructed-only-by-the-host-double. This file is in no
// project and is never compiled; it exists so the rule can be watched refusing
// the mistake it names.
//
// The near-miss is a second call site, written by somebody who wanted one more
// assertion and had the two doubles already to hand. It reads as harmless. What
// it costs is the property PluginHost exists for: with two call sites, adding a
// host service to the constructor no longer stops the suite from building, and
// the test that was never updated goes on passing against a plugin the server
// would construct differently.

namespace Jellyfin.Plugin.Requests.Tests.Fixtures;

internal sealed class ConstructsThePluginDirectly
{
    // Legal neighbour, left here on purpose: this is how the suite reaches the
    // plugin and the rule has to stay quiet on it.
    public static void ThroughTheHost()
    {
        using var host = new PluginHost();
        _ = host.Plugin.Name;
    }

    // The regression, in both spellings the suite could write it.
    public static void ByHand()
    {
        var plugin = new Plugin(new FakeApplicationPaths(), new FakeXmlSerializer());
        var aliased = new PluginUnderTest(new FakeApplicationPaths(), new FakeXmlSerializer());
        _ = plugin.Name;
        _ = aliased.Name;
    }
}

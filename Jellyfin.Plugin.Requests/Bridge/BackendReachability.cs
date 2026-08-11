namespace Jellyfin.Plugin.Requests.Bridge;

/// <summary>
/// Whether there is an external request service behind this plugin, and whether it answered.
/// <para>
/// Three values rather than a yes or a no, because "nothing is configured" and "something is
/// configured and did not answer" are opposite facts about a server and a caller that reduces them
/// to one tells an operator their bridge is down on an install that never had one.
/// </para>
/// </summary>
public enum BackendReachability
{
    /// <summary>
    /// There is no external service behind this plugin. The ordinary state of most installs, and
    /// nothing here is wrong: the plugin is the whole system on such a server.
    /// </summary>
    NotConfigured = 0,

    /// <summary>
    /// An external service is configured and answered.
    /// </summary>
    Reachable = 1,

    /// <summary>
    /// An external service is configured and did not answer. What follows from that is #86, which
    /// decides what a bound retry is and what stops rather than retrying.
    /// </summary>
    Unreachable = 2
}

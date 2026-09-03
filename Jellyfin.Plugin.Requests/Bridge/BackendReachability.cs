namespace Jellyfin.Plugin.Requests.Bridge;

/// <summary>
/// Whether there is an external request service behind this plugin, and whether it answered.
/// <para>
/// More than a yes or a no, because "nothing is configured" and "something is configured and did
/// not answer" are opposite facts about a server and a caller that reduces them to one tells an
/// operator their bridge is down on an install that never had one.
/// </para>
/// <para>
/// <b>Three of the values are failures, and they are told apart by what an operator does next.</b>
/// #86 decided them. A service that did not answer is temporary and is asked again on the next call,
/// with every call bounded so that a service which hangs costs a run ten seconds and not the night.
/// A service that refused this server's key is not temporary: retrying it hides the problem, so the
/// bridge stops and says so until the key is corrected. A service reporting a version this plugin
/// does not know stops it the same way, because no retry fixes a version. Each of the three is its
/// own sentence on the operator's page, and <c>docs/bridge.md</c> carries what every caller does with
/// each of them.
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
    /// An external service is configured, answered, reports a version this plugin knows how to speak
    /// to, and accepted this server's key.
    /// </summary>
    Reachable = 1,

    /// <summary>
    /// An external service is configured and did not answer, or answered a failure to being asked
    /// whether it is up. Temporary: nothing is walked, nothing remembers it as down, and the next
    /// call asks again.
    /// </summary>
    Unreachable = 2,

    /// <summary>
    /// An external service is configured, answered, and refused this server's key. The bridge is
    /// stopped: nothing is reconciled until the key is corrected, and the operator is told rather
    /// than the key being tried again until somebody notices.
    /// </summary>
    CredentialRefused = 3,

    /// <summary>
    /// An external service is configured, answered, and reports a version this plugin does not know
    /// how to speak to. The bridge is stopped the way it is for a refused key, because a form this
    /// adapter was never read or measured against is not one to send a request in.
    /// </summary>
    Incompatible = 4
}

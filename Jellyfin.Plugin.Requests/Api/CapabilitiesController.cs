using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// The one endpoint something asks before it asks anything else.
/// <para>
/// Without it, a caller finds out what this plugin allows by calling and reading the refusals, and a
/// caller that has to tell a 404 for "no such plugin" from a 404 for "no such request" is a caller
/// that gets it wrong. A page, a script and a third-party client all need the same three facts
/// first, and none of them is worth a round of guessing.
/// </para>
/// <para>
/// <b>This is not the seam.</b> The sibling discover plugin runs in the same server process and
/// finds this one through the server's container, decided in #89, so it never calls this and this
/// endpoint says nothing about the seam. <c>docs/seam.md</c> is where the difference is argued.
/// </para>
/// <para>
/// A controller of its own rather than another action beside the queue. It reads no request, needs
/// no store and no clock, and putting it there would give every capability answer the store's
/// dependencies and every store test a capability to carry.
/// </para>
/// </summary>
[Authorize]
public sealed class CapabilitiesController : RequestsControllerBase
{
    private readonly IInstallSettings _settings;
    private readonly IRequestBackend _backend;

    /// <summary>
    /// Initializes a new instance of the <see cref="CapabilitiesController"/> class.
    /// </summary>
    /// <param name="settings">What this install is set to.</param>
    /// <param name="backend">The bridge, which on most servers is the one with nothing behind it.</param>
    /// <exception cref="ArgumentNullException">Where either was not given.</exception>
    public CapabilitiesController(IInstallSettings settings, IRequestBackend backend)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(backend);

        _settings = settings;
        _backend = backend;
    }

    /// <summary>
    /// What this install is and what it allows.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The API version, the kinds this install accepts, whether an operator decides, and whether
    /// there is a service behind the plugin. It answers on a fresh install, because every one of
    /// those has an answer before anybody has configured anything.
    /// </returns>
    [HttpGet("Capabilities")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<InstallCapabilities>> CapabilitiesAsync(CancellationToken cancellationToken)
    {
        var settings = _settings.Current;

        // Asked of the bridge rather than decided from which implementation the container handed
        // back, because "nothing is configured" is the interface's own answer to this question and a
        // type comparison here would be a second way of asking it. On every install today this is
        // the null bridge answering without leaving the process until an address is written. With
        // one, the adapter bounds every call it makes and answers a failure as a value rather than
        // throwing, which #86 decided, and this endpoint follows that rather than swallowing
        // anything: an answer that claimed no bridge because the bridge failed would be a lie about
        // the install, and the operator would read it as proof their configuration never took.
        var reachability = await _backend.CheckReachableAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new InstallCapabilities
        {
            ApiVersion = VersionSegment,
            AcceptedKinds = Accepted(settings),
            AutomaticApproval = false,
            BridgeConfigured = reachability != BackendReachability.NotConfigured
        });
    }

    /// <summary>
    /// The kinds this install accepts, built from the settings rather than from the enumeration, so
    /// a kind an operator switched off is one a caller is never offered.
    /// </summary>
    /// <param name="settings">What this install is set to.</param>
    /// <returns>The kinds, in the enumeration's own order.</returns>
    private static List<RequestedItemKind> Accepted(PluginConfiguration settings)
    {
        var kinds = new List<RequestedItemKind>();

        if (settings.AcceptsMovies)
        {
            kinds.Add(RequestedItemKind.Movie);
        }

        if (settings.AcceptsSeries)
        {
            kinds.Add(RequestedItemKind.Series);
        }

        return kinds;
    }
}

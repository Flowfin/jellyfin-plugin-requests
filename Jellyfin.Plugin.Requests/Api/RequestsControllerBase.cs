using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// Where this plugin's API lives, declared once so every endpoint inherits it.
/// <para>
/// A plugin controller is mounted on the server's own API, beside the server's routes and beside
/// every other plugin's. The prefix is permanent in the only sense that matters: a script an
/// operator wrote, the sibling discover plugin and anything anybody built against it all break the
/// day it changes. The rule for changing it, what counts as a breaking change and what does not,
/// is <c>docs/api.md</c>, and it is written down rather than left to whoever ships the second
/// version.
/// </para>
/// <para>
/// Every endpoint sits under <see cref="RoutePrefix"/> because it is declared here and inherited,
/// and the two ways out of that are refused by <c>every-endpoint-sits-under-the-versioned-prefix</c>
/// in the invariant lint: a second route attribute anywhere else, and a method template beginning
/// with a slash, which silently replaces the controller's route rather than extending it.
/// </para>
/// <para>
/// <b>A policy is either one the server registers a name for, taken from the server's own constant,
/// or it is the server's default.</b> The names it registers are <c>Policies</c> in
/// <c>MediaBrowser.Common</c>, an assembly this plugin already references, so an endpoint naming one
/// of them cannot name a policy that does not exist. There is no registered name for "any signed-in
/// person": the server builds that requirement into the unnamed default policy, so an endpoint open
/// to any signed-in caller carries <c>[Authorize]</c> with nothing after it, which is what the
/// server's own controllers carry for the same thing. A policy written here as a string is refused
/// by <c>policy-is-named-by-the-servers-own-constant</c> in the invariant lint: a name the server
/// does not register does not make an endpoint narrower, it makes the endpoint answer 500 to
/// everybody, which is what this plugin shipped until <c>docs/api.md</c> recorded the repair.
/// </para>
/// </summary>
[ApiController]
[Route(RoutePrefix)]
[Produces(MediaTypeNames.Application.Json)]
public abstract class RequestsControllerBase : ControllerBase
{
    /// <summary>
    /// The version segment every endpoint under <see cref="RoutePrefix"/> answers as.
    /// <para>
    /// It is in the path rather than in a header. A caller that has to set a header to get the shape
    /// it expects is a caller that gets some other shape the first time somebody forgets, and a
    /// version in the path is visible in a log, in a browser and in the script an operator wrote.
    /// </para>
    /// </summary>
    public const string VersionSegment = "v1";

    /// <summary>
    /// The prefix every endpoint sits under, version segment included. Built from
    /// <see cref="VersionSegment"/> rather than written out again, so the two cannot say different
    /// numbers.
    /// <para>
    /// <c>MediaRequests</c> rather than <c>Requests</c>. The word on its own is a generic noun in an
    /// API whose neighbouring segments are <c>Items</c>, <c>Users</c> and <c>Sessions</c>, and a
    /// plugin taking it is a plugin betting that neither the server nor any other plugin ever wants
    /// it. The server's route table was not enumerated to check, so this lowers the chance of a
    /// collision rather than proving there is none. The compound noun costs nothing today, and a
    /// rename later is exactly the breaking change the version segment exists to make survivable.
    /// </para>
    /// </summary>
    public const string RoutePrefix = "MediaRequests/" + VersionSegment;
}

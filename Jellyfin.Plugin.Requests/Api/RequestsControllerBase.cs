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

    /// <summary>
    /// The server's policy for a call that has to come from a signed-in user. Named as a literal
    /// because the constant that holds it lives in the server's own web assembly, which a plugin
    /// does not reference; the string is the contract either way.
    /// <para>
    /// It is here rather than on one controller because there is more than one, and a second copy of
    /// the literal beside the second controller would be a second spelling of a contract this
    /// repository does not own.
    /// </para>
    /// </summary>
    protected const string AuthenticatedUserPolicy = "DefaultAuthorization";

    /// <summary>
    /// The server's policy for a call that has to come from an administrator, named as a literal for
    /// the same reason as the one above. It is what the server's own dashboard endpoints carry, so an
    /// endpoint under it is reachable by exactly the people who can already administer the server.
    /// </summary>
    protected const string AdministratorPolicy = "RequiresElevation";
}

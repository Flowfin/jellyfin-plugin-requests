// Fixture for no-anonymous-endpoint and authorize-names-a-policy. This file is in
// no project and is never compiled; it exists so both rules can be watched
// refusing the mistakes they name.
//
// Both near-misses are one keystroke away from correct code and neither is a
// build failure. The first is what somebody writes while testing an endpoint by
// hand and then does not take out again. The second reads as though it says
// "this needs authorisation", and it does: it says only that the caller is
// authenticated, which on a family server is everybody, and it says nothing
// about which of them.

namespace Jellyfin.Plugin.Requests.Fixtures;

public sealed class ReachableWithoutASession : RequestsControllerBase
{
    // Legal neighbours, left here on purpose: this is how an endpoint is written
    // and neither rule may fire on either of them.
    [HttpGet("Requests")]
    [Authorize(Policy = "DefaultAuthorization")]
    public IActionResult Mine() => Ok();

    [HttpGet("Requests/Queue")]
    [Authorize(Policy = "RequiresElevation")]
    public IActionResult Queue() => Ok();

    // The regression: an endpoint anybody can reach with no session at all.
    [HttpGet("Requests/Anything")]
    [AllowAnonymous]
    public IActionResult Anything() => Ok();

    // And the one that looks like it decided something. Every signed-in person on
    // the server passes it, which is the whole server's household.
    [HttpGet("Requests/Everybody")]
    [Authorize]
    public IActionResult Everybody() => Ok();
}

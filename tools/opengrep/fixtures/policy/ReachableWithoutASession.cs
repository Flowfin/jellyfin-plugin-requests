// Fixture for no-anonymous-endpoint and policy-is-named-by-the-servers-own-constant.
// This file is in no project and is never compiled; it exists so both rules can
// be watched refusing the mistakes they name.
//
// Both near-misses are one keystroke away from correct code and neither is a
// build failure. The first is what somebody writes while testing an endpoint by
// hand and then does not take out again.
//
// The second is the one that shipped. A policy written out as a string reads as
// the narrowest thing on the page and is the only line here that cannot be
// checked by anything: the name below is the one this plugin carried, the server
// registers it on neither claimed line, and an endpoint under a policy nothing
// registers answers 500 to every caller rather than admitting fewer of them.

namespace Jellyfin.Plugin.Requests.Fixtures;

public sealed class ReachableWithoutASession : RequestsControllerBase
{
    // Legal neighbours, left here on purpose: this is how an endpoint is written
    // and neither rule may fire on either of them. The first carries the server's
    // default policy, which is spelled by naming none; the second carries a name
    // the server registers, taken from the constant it registers it under.
    [HttpGet("Requests")]
    [Authorize]
    public IActionResult Mine() => Ok();

    [HttpGet("Requests/Queue")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public IActionResult Queue() => Ok();

    // The regression: an endpoint anybody can reach with no session at all.
    [HttpGet("Requests/Anything")]
    [AllowAnonymous]
    public IActionResult Anything() => Ok();

    // And the name nothing checks, which is how every endpoint here came to
    // answer 500 on both server lines.
    [HttpGet("Requests/Everybody")]
    [Authorize(Policy = "DefaultAuthorization")]
    public IActionResult Everybody() => Ok();
}

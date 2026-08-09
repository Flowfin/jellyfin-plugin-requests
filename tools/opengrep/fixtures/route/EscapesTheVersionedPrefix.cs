// Fixture for every-endpoint-sits-under-the-versioned-prefix. This file is in no
// project and is never compiled; it exists so the rule can be watched refusing
// the mistake it names.
//
// The near-miss is the second one below. Somebody adds an endpoint, writes the
// template the way every other framework writes a path, and the leading slash
// turns it from a segment under the controller's prefix into a route of its own
// at the root of the server's API. It compiles, it answers, and it is outside
// the version every compatibility promise in docs/api.md is written about.

namespace Jellyfin.Plugin.Requests.Fixtures;

public sealed class EscapesTheVersionedPrefix : RequestsControllerBase
{
    // Legal neighbours, left here on purpose: this is how an endpoint is written
    // and the rule has to stay quiet on both. The template is relative, so it
    // extends the prefix the base class declares instead of replacing it.
    [HttpGet("Requests")]
    public IActionResult List() => Ok();

    [HttpPost("Requests")]
    public IActionResult Create() => Ok();

    // The regression, in both spellings. A route attribute of its own replaces
    // the inherited prefix for the whole controller.
    [Route("Requests")]
    public IActionResult ListSomewhereElse() => Ok();

    // And the leading slash, once per verb the plugin will use, because a rule
    // that covers the verb somebody used and not the one they will use next is a
    // rule that passes on the day it matters.
    [HttpGet("/Requests")]
    public IActionResult ReadAtTheRoot() => Ok();

    [HttpPost("/Requests")]
    public IActionResult CreateAtTheRoot() => Ok();

    [HttpPut("/Requests/1")]
    public IActionResult ReplaceAtTheRoot() => Ok();

    [HttpPatch("/Requests/1")]
    public IActionResult AmendAtTheRoot() => Ok();

    [HttpDelete("/Requests/1")]
    public IActionResult RemoveAtTheRoot() => Ok();
}

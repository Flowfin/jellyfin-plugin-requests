using System;
using System.Globalization;
using System.IO;
using System.Net.Mime;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// The page a person opens in a browser to see what they asked for.
/// <para>
/// <b>The dashboard cannot be it.</b> A plugin's pages live in the dashboard, the dashboard is the
/// administrator's, and the queue page this plugin registers there is elevated by construction. So a
/// user on a browser has no page at all unless this plugin serves one, which is what this endpoint
/// does: a document returned from the same API the rest of this plugin answers on, under the same
/// versioned prefix, behind the same authentication.
/// </para>
/// <para>
/// <b>It is refused rather than served empty to a caller with no session.</b> The endpoint carries
/// the server's default policy, so a caller the server has not authenticated never reaches the
/// document. A shell served to anybody and left to fail on its first call would put this plugin's
/// existence, its version and its shape in front of somebody who has not signed in, and would look
/// to a person like a broken page rather than a closed door.
/// </para>
/// <para>
/// <b>A browser navigating to an address sends no Jellyfin session.</b> A session here is a header
/// or a query value and not a cookie, so a person opening this page in a new tab reaches it with the
/// credential in the address. The server reads <c>api_key</c> from the query string on both claimed
/// lines. What that costs, and it is a real cost, is in <c>docs/surface.md</c>: the value lands in
/// the browser's history and in whatever log sits in front of the server. This endpoint neither
/// creates that credential nor extends it, and the page it returns carries it no further than the
/// one call it makes for the caller's own requests.
/// </para>
/// <para>
/// A controller of its own rather than another action beside the queue, for the reason
/// <see cref="CapabilitiesController"/> is one: it reads no request, needs no store and no clock,
/// and putting it there would give the page the store's dependencies and give every store test a
/// page to carry.
/// </para>
/// </summary>
[Authorize]
public sealed class MyRequestsPageController : RequestsControllerBase
{
    /// <summary>
    /// The embedded document this endpoint answers with, relative to the plugin's own namespace.
    /// <para>
    /// It is a constant because the suite reads the same resource to hold the page to what it may
    /// and may not do, and two spellings of one resource name is a check that passes over a
    /// document nobody serves.
    /// </para>
    /// </summary>
    public const string PageResource = "Web.mine.html";

    /// <summary>
    /// The page as the assembly carries it, read once.
    /// <para>
    /// Once rather than per call, because it is a file inside the assembly and cannot change while
    /// the server is running, and a page read from the manifest on every request is a stream opened
    /// once per person looking at their requests.
    /// </para>
    /// </summary>
    private static readonly Lazy<string> Document = new Lazy<string>(Read);

    /// <summary>
    /// The page, for the person who is signed in.
    /// </summary>
    /// <returns>
    /// The document, as HTML. It carries no request in itself: what it holds is the markup and the
    /// script that asks this plugin for the caller's own requests, so the answer to who may see what
    /// is decided by that endpoint rather than a second time here.
    /// </returns>
    [HttpGet("Page")]
    [Authorize]
    [Produces(MediaTypeNames.Text.Html)]
    [ProducesResponseType<string>(StatusCodes.Status200OK)]
    public ContentResult Page()
    {
        return new ContentResult
        {
            Content = Document.Value,
            ContentType = MediaTypeNames.Text.Html,
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// The document out of the assembly.
    /// </summary>
    /// <returns>The page.</returns>
    /// <exception cref="InvalidOperationException">
    /// Where the assembly carries no such resource, which is a packaging failure rather than
    /// anything a caller did. It is raised rather than answered as an empty page, because a blank
    /// document with a 200 on it is the shape somebody spends an afternoon on.
    /// </exception>
    private static string Read()
    {
        var assembly = typeof(Plugin).Assembly;
        var name = string.Format(
            CultureInfo.InvariantCulture,
            "{0}.{1}",
            typeof(Plugin).Namespace,
            PageResource);

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                "This plugin was built without the page it serves to a browser, so there is nothing to return. The resource is " + name + ".");

        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }
}

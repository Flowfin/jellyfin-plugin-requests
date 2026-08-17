using System;
using System.Globalization;
using Jellyfin.Plugin.Requests.Localisation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// The words this plugin's pages draw, served rather than written into them.
/// <para>
/// <b>A page has to fetch them, because nothing can put them in on the way out.</b> The dashboard
/// serves a plugin's pages itself, straight out of the assembly's resources under the name
/// <c>GetPages</c> registers, so this plugin never sees the request and has no moment at which it
/// could substitute anything. That leaves one shape: the markup ships with keys and the words
/// arrive over the API, which is this endpoint.
/// </para>
/// <para>
/// A controller of its own for the reason <see cref="CapabilitiesController"/> is one: it reads no
/// request, needs no store and no clock, and putting it beside the queue would give every store
/// test a catalogue to carry.
/// </para>
/// </summary>
[Authorize]
public sealed class StringsController : RequestsControllerBase
{
    /// <summary>
    /// The catalogue this endpoint answers out of.
    /// </summary>
    private readonly StringCatalogue _catalogue;

    /// <summary>
    /// Initializes a new instance of the <see cref="StringsController"/> class.
    /// <para>
    /// The catalogue is injected rather than reached for, so the suite can hand it one that is not
    /// the shipped set and watch the fallback rule from the outside. What the container holds is
    /// <see cref="StringCatalogue.Shipped"/>, registered once in <c>PluginServiceRegistrator</c>.
    /// </para>
    /// </summary>
    /// <param name="catalogue">The catalogue.</param>
    /// <exception cref="ArgumentNullException">Where no catalogue was given.</exception>
    public StringsController(StringCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        _catalogue = catalogue;
    }

    /// <summary>
    /// Every string a page draws.
    /// </summary>
    /// <param name="culture">
    /// The culture asked for, as a name such as <c>de-DE</c>. Where it is absent the answer is
    /// English.
    /// <para>
    /// <c>Accept-Language</c> is deliberately not read. Reading it needs the typed-header
    /// extensions, which pull three assemblies into this plugin that nothing else here uses, and
    /// what a browser sends in that header is not what the person changed when they changed the
    /// language in Jellyfin. The pages pass <c>navigator.language</c>, which is.
    /// </para>
    /// </param>
    /// <returns>
    /// The catalogue for that culture with English merged underneath it, so the set is complete
    /// however much of it has been translated.
    /// </returns>
    [HttpGet("Strings")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PageStrings> Strings([FromQuery] string? culture)
    {
        var asked = Asked(culture);

        return Ok(new PageStrings
        {
            Culture = asked?.Name ?? StringCatalogue.English,
            Strings = _catalogue.For(asked)
        });
    }

    /// <summary>
    /// The culture this call is answered in.
    /// <para>
    /// A name nothing recognises is not refused. A caller sending a culture this runtime has never
    /// heard of wants words, and the honest answer to an unknown name is the fallback rather than a
    /// 400: the catalogue falls back per key already, and an unknown culture is that same rule with
    /// nothing matching at any step.
    /// </para>
    /// </summary>
    /// <param name="culture">What the query asked for, if anything.</param>
    /// <returns>The culture, or <see langword="null"/> for English.</returns>
    private static CultureInfo? Asked(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(culture);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}

using System.Collections.Generic;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// What this install is and what it allows, for something outside the server process that has to
/// know before it calls anything.
/// <para>
/// Four facts and no more. A browser page, a script an operator wrote and a third-party client all
/// arrive with the same three questions: is this plugin here, which shape of the API does it speak,
/// and is there any point offering the thing I was going to offer. Anything else here would be a
/// fact about the install that a caller did not need and could not have learned another way.
/// </para>
/// <para>
/// <b>Nothing here is a credential, an address, or anything about another person.</b> That is
/// checked over the shape rather than reviewed: every field is a version string, a switch or the set
/// of kinds, so there is nothing this answer could carry that a caller may not already ask for
/// directly.
/// </para>
/// </summary>
public sealed record InstallCapabilities
{
    /// <summary>
    /// Gets the version of the API this install speaks, which is the segment its routes sit under.
    /// A caller that finds a version it does not know stops rather than guessing at the shape.
    /// </summary>
    public required string ApiVersion { get; init; }

    /// <summary>
    /// Gets the media kinds this install accepts a request for.
    /// <para>
    /// A caller offering a button for a kind the install refuses is a caller producing a refusal the
    /// person reading it cannot act on. What decides this is the configuration, so it is an answer
    /// about this server rather than about this version.
    /// </para>
    /// </summary>
    public required IReadOnlyList<RequestedItemKind> AcceptedKinds { get; init; }

    /// <summary>
    /// Gets a value indicating whether a request is approved without an operator looking at it.
    /// <para>
    /// It is here because it changes what a caller should say to the person asking: "an
    /// administrator will look at this" is wrong on a server where nobody will. It is false on every
    /// install today, and automatic approval arrives as a per-user setting rather than a switch for
    /// the whole server, decided on #113.
    /// </para>
    /// </summary>
    public required bool AutomaticApproval { get; init; }

    /// <summary>
    /// Gets a value indicating whether an external request service sits behind this plugin.
    /// <para>
    /// Whether one is configured and nothing else. Not which service, not where it is, and not
    /// whether it answered when it was last asked: the first two are the operator's business and the
    /// third is a fact about somebody else's system that a caller here cannot act on.
    /// </para>
    /// </summary>
    public required bool BridgeConfigured { get; init; }
}

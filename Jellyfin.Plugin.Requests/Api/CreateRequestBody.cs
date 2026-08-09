using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// What a caller sends to ask for something.
/// <para>
/// <b>There is no field naming the requester, and its absence is the design.</b> Who asked is taken
/// from the authenticated caller, so a body carrying <c>requestedByUserId</c> has nothing to bind to
/// and is dropped by the serialiser. A field that existed and was ignored would be a field somebody
/// eventually honours; a field that does not exist cannot be. Filing a request as somebody else is
/// the attack this shape refuses, and it is refused by there being no way to express it.
/// </para>
/// <para>
/// Every property is nullable, including the ones that are required, so that "absent" and "empty"
/// reach the validation as different values. A non-nullable string arriving as the empty string
/// cannot be told from one nobody sent, and the caller then gets a message about the wrong mistake.
/// </para>
/// </summary>
public sealed record CreateRequestBody
{
    /// <summary>
    /// The name of the field a caller would use to file a request as somebody else, so that the
    /// test proving it cannot names the same string this type is checked against rather than a copy
    /// of it.
    /// </summary>
    internal const string RequesterFieldThatDoesNotExist = "requestedByUserId";

    /// <summary>
    /// Gets what sort of thing is wanted.
    /// </summary>
    public RequestedItemKind? Kind { get; init; }

    /// <summary>
    /// Gets the title as the caller's client reads it. Stored as the snapshot on the request, which
    /// is what an operator decides from when nothing else is reachable.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the release year, or <see langword="null"/> where the caller has none. This is the field
    /// that separates two films sharing a title.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Gets the external identifiers naming the thing, keyed by provider. Absent and empty are both
    /// legal: a request typed by a person carries a title and nothing else, and what such a request
    /// may then do is narrow and is <see cref="RequestLifecycle"/>'s answer.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ProviderIds { get; init; }

    /// <summary>
    /// Gets the seasons wanted, where the thing is a series. Absent or empty means the whole series,
    /// which is the convention on the record and is what a client sends when somebody asks for a
    /// programme rather than part of one.
    /// </summary>
    public IReadOnlyList<int>? Seasons { get; init; }

    /// <summary>
    /// Gets what the person wanted to say about it, or <see langword="null"/> where they said
    /// nothing.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>
    /// Gets the provider identifiers as a map that is never null, so every reader downstream is
    /// spared the absent case.
    /// </summary>
    /// <returns>What the caller sent, or an empty map.</returns>
    internal IReadOnlyDictionary<string, string> IdentifiersOrEmpty()
        => ProviderIds ?? new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the seasons as a list that is never null.
    /// </summary>
    /// <returns>What the caller sent, or an empty list.</returns>
    internal IReadOnlyList<int> SeasonsOrEmpty() => Seasons ?? [];
}

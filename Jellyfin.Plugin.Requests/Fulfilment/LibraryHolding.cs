using System.Collections.Generic;

namespace Jellyfin.Plugin.Requests.Fulfilment;

/// <summary>
/// What the server holds of one title, as an answer rather than as a list of library items.
/// <para>
/// It is this shape rather than a library item because everything downstream of it decides a rule,
/// and a rule written against a library item is a rule that cannot be tested without a library. Two
/// facts are enough for every rule in <see cref="FulfilmentRule"/>: whether the title is there at
/// all, and which seasons of it are.
/// </para>
/// <para>
/// <see cref="SeasonsHeld"/> is what the server has files for, and it is never what the series is
/// known to have upstream. This plugin calls no metadata source, decided in #92, so nothing here
/// can count the seasons that exist; it can only count the ones that arrived.
/// </para>
/// </summary>
public sealed record LibraryHolding
{
    /// <summary>
    /// Gets the answer for a title the server does not have. Named rather than written out at each
    /// call site, because "not held" is the answer most lookups give and a shape repeated at every
    /// one of them is a shape one of them will get wrong.
    /// </summary>
    public static LibraryHolding Nothing { get; } = new LibraryHolding();

    /// <summary>
    /// Gets a value indicating whether the server holds the title at all. False is the whole answer:
    /// where it is false, <see cref="SeasonsHeld"/> is empty and says nothing.
    /// </summary>
    public bool Held { get; init; }

    /// <summary>
    /// Gets the season numbers the server has files for, in no particular order and without repeats.
    /// Empty on a film, which has no seasons, and empty on a series the server holds with nothing
    /// under it yet.
    /// </summary>
    public IReadOnlyList<int> SeasonsHeld { get; init; } = [];
}

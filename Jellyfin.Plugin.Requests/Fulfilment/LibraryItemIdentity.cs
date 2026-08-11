using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Requests.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.Requests.Fulfilment;

/// <summary>
/// What one library item is, in the vocabulary a request is written in. This is the translation
/// between the server's model and this plugin's, and it is a function of its own so that it can be
/// tested against the server's real library types with no server running.
/// <para>
/// Only the two kinds a request can name are answered, because <see cref="RequestedItemKind"/> has
/// two values and inventing a third here would produce a change nothing downstream can match. Which
/// kinds an install accepts is a separate question and is #95's; this says what the server just
/// told us about.
/// </para>
/// <para>
/// A season and an episode answer nothing, and that is deliberate rather than an omission. What a
/// request names is the series, so an episode has to be resolved to its parent before it means
/// anything here, and reaching a parent is a question for the server's library rather than for a
/// function. <see cref="ServerLibrary"/> does that resolution and hands the series in.
/// </para>
/// <para>
/// The identifiers are copied rather than referenced. A library item is a live object the server
/// goes on writing to, and this answer is put in a queue and read later on another thread.
/// </para>
/// </summary>
public static class LibraryItemIdentity
{
    /// <summary>
    /// What a library item is, or <see langword="null"/> where it is nothing a request can name.
    /// </summary>
    /// <param name="item">The library item the server raised an event about.</param>
    /// <returns>The kind and the identifiers, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">Where no item was given.</exception>
    public static LibraryChangeEventArgs? Of(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item switch
        {
            Movie => new LibraryChangeEventArgs(RequestedItemKind.Movie, Identifiers(item)),
            Series => new LibraryChangeEventArgs(RequestedItemKind.Series, Identifiers(item)),
            _ => null
        };
    }

    /// <summary>
    /// The item's external identifiers, as a map nothing else can change afterwards.
    /// <para>
    /// Absent and empty are one answer. An item nobody has identified matches no request, and the
    /// caller learns that by getting an empty map rather than by testing for a missing one. The map
    /// ignores case in the provider name, which is the rule <see cref="RequestIdentity"/> states and
    /// the reason it states it: the same provider is spelled two ways by two callers and neither is
    /// wrong.
    /// </para>
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <returns>The identifiers.</returns>
    private static Dictionary<string, string> Identifiers(BaseItem item)
        => item.ProviderIds is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(item.ProviderIds, StringComparer.OrdinalIgnoreCase);
}

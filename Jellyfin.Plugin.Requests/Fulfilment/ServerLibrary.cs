using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Requests.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Requests.Fulfilment;

/// <summary>
/// <see cref="ILibrary"/> over the server's own library, and the one part of #42 the suite does not
/// reach.
/// <para>
/// That is stated rather than left to be noticed. The server's library interface differs between the
/// two server lines this plugin is built for, so anything standing in for it would have to be
/// written twice and would prove a different thing on each. This class is therefore held to as
/// little as it can be: it translates a query and an event and decides nothing, and every rule that
/// could be got wrong lives on the other side of <see cref="ILibrary"/> where the suite reaches it.
/// What is left here is checked by reading it and by the plugin loading on a server of each claimed
/// line, which is #20's procedure.
/// </para>
/// </summary>
public sealed class ServerLibrary : ILibrary, IDisposable
{
    private readonly ILibraryManager _library;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerLibrary"/> class.
    /// </summary>
    /// <param name="library">The server's library.</param>
    /// <exception cref="ArgumentNullException">Where no library was given.</exception>
    public ServerLibrary(ILibraryManager library)
    {
        ArgumentNullException.ThrowIfNull(library);

        _library = library;
        _library.ItemAdded += OnItemChanged;
        _library.ItemRemoved += OnItemChanged;
    }

    /// <inheritdoc />
    public event EventHandler<LibraryChangeEventArgs>? Changed;

    /// <inheritdoc />
    public Task<LibraryHolding> HoldingOfAsync(
        RequestedItemKind kind,
        IReadOnlyDictionary<string, string> providerIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerIds);
        cancellationToken.ThrowIfCancellationRequested();

        if (providerIds.Count == 0)
        {
            return Task.FromResult(LibraryHolding.Nothing);
        }

        var wanted = kind == RequestedItemKind.Series ? BaseItemKind.Series : BaseItemKind.Movie;

        var found = _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [wanted],
            HasAnyProviderId = new Dictionary<string, string>(providerIds, StringComparer.OrdinalIgnoreCase),

            // Everything under every library rather than one folder, because a request names a title
            // and not a place somebody keeps it.
            Recursive = true,

            // The server creates rows for media it knows about and does not have, and counting one
            // of those as arrived is the exact failure this whole path exists to avoid.
            IsVirtualItem = false
        });

        if (found.Count == 0)
        {
            return Task.FromResult(LibraryHolding.Nothing);
        }

        return Task.FromResult(new LibraryHolding
        {
            Held = true,
            SeasonsHeld = kind == RequestedItemKind.Series ? SeasonsUnder(found) : []
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _library.ItemAdded -= OnItemChanged;
        _library.ItemRemoved -= OnItemChanged;
    }

    /// <summary>
    /// The season numbers the server has files for, across every library item that answered to the
    /// identifiers. Several can answer where the same programme sits in two libraries, and the union
    /// is the honest answer: the person who asked can watch a season that is in either of them.
    /// </summary>
    /// <param name="series">The series items that matched.</param>
    /// <returns>The season numbers, without repeats.</returns>
    private IReadOnlyList<int> SeasonsUnder(IReadOnlyList<BaseItem> series)
    {
        var numbers = new HashSet<int>();

        foreach (var one in series)
        {
            var seasons = _library.GetItemList(new InternalItemsQuery
            {
                ParentId = one.Id,
                IncludeItemTypes = [BaseItemKind.Season],
                IsVirtualItem = false
            });

            foreach (var season in seasons.OfType<Season>())
            {
                if (season.IndexNumber is int number)
                {
                    numbers.Add(number);
                }
            }
        }

        return [.. numbers];
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        if (e?.Item is null)
        {
            return;
        }

        // A season or an episode is resolved to the programme it belongs to, because that is what a
        // request names. A removal can leave the parent unreachable, and then there is nothing to
        // raise and the scheduled run is what notices.
        var item = e.Item switch
        {
            Season season => _library.GetItemById(season.SeriesId),
            Episode episode => _library.GetItemById(episode.SeriesId),
            _ => e.Item
        };

        if (item is null)
        {
            return;
        }

        var change = LibraryItemIdentity.Of(item);

        if (change is not null)
        {
            Changed?.Invoke(this, change);
        }
    }
}

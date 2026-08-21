using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
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
    private readonly IUserManager _users;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerLibrary"/> class.
    /// </summary>
    /// <param name="library">The server's library.</param>
    /// <param name="users">
    /// The server's users, so a lookup made on somebody's behalf can be made as them. It is the
    /// user record the server's own query takes, which is why this reference is here rather than a
    /// user identifier being enough.
    /// </param>
    /// <exception cref="ArgumentNullException">Where no library or no users were given.</exception>
    public ServerLibrary(ILibraryManager library, IUserManager users)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(users);

        _library = library;
        _users = users;
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
        => Holding(kind, providerIds, null, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// A user identifier the server does not have is answered with nothing rather than with the
    /// unrestricted answer. Falling back to the wider lookup is the failure this whole member exists
    /// to prevent, and it would happen exactly when the caller is least known.
    /// </remarks>
    public Task<LibraryHolding> HoldingSeenByAsync(
        Guid userId,
        RequestedItemKind kind,
        IReadOnlyDictionary<string, string> providerIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerIds);
        cancellationToken.ThrowIfCancellationRequested();

        var reader = userId == Guid.Empty ? null : _users.GetUserById(userId);

        return reader is null
            ? Task.FromResult(LibraryHolding.Nothing)
            : Holding(kind, providerIds, reader, cancellationToken);
    }

    /// <summary>
    /// The one lookup, made as the server or as one of its users.
    /// </summary>
    /// <param name="kind">What sort of thing is being asked about.</param>
    /// <param name="providerIds">The identifiers to match on.</param>
    /// <param name="reader">
    /// The person to ask as, or <see langword="null"/> to ask as the server. The server's own query
    /// carries what it applies to a user, so handing it the record is what applies the rating and
    /// the library access rather than anything decided here.
    /// </param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>What is held.</returns>
    private Task<LibraryHolding> Holding(
        RequestedItemKind kind,
        IReadOnlyDictionary<string, string> providerIds,
        User? reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerIds);
        cancellationToken.ThrowIfCancellationRequested();

        if (providerIds.Count == 0)
        {
            return Task.FromResult(LibraryHolding.Nothing);
        }

        var wanted = kind == RequestedItemKind.Series ? BaseItemKind.Series : BaseItemKind.Movie;

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [wanted],
            HasAnyProviderId = new Dictionary<string, string>(providerIds, StringComparer.OrdinalIgnoreCase),

            // Everything under every library rather than one folder, because a request names a title
            // and not a place somebody keeps it.
            Recursive = true,

            // The server creates rows for media it knows about and does not have, and counting one
            // of those as arrived is the exact failure this whole path exists to avoid.
            IsVirtualItem = false
        };

        if (reader is not null)
        {
            query.SetUser(reader);
        }

        var found = _library.GetItemList(query);

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

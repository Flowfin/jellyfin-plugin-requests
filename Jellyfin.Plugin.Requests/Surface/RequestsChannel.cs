using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Localisation;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Surface;

/// <summary>
/// What a person asked for and what happened to it, as a folder tree the server puts beside their
/// libraries.
/// <para>
/// <b>Why this and not a page.</b> A page reaches a browser. Most people using a media server are
/// on a television client or a set-top box this project will never change, and a feature that needs
/// a client change does not exist for them. A channel is the one extension point that reaches an
/// unmodified client, and <c>docs/surface.md</c> is where that was decided together with the three
/// shapes it was decided against.
/// </para>
/// <para>
/// <b>What it renders.</b> One folder per state the person actually has something in, named by the
/// same words the page uses, and inside each folder the titles they asked for. Nothing else. It is
/// a status view rather than a second dashboard, and it says nothing about anybody but the person
/// asking: no row carries a user identifier, a requester name or a note somebody else typed.
/// </para>
/// <para>
/// <b>Somebody who has asked for nothing gets a sentence rather than nothing.</b> An empty tree is
/// indistinguishable from a plugin that is broken, so the root answers with one folder carrying the
/// catalogue's own sentence for that case. It is the same sentence the page shows, looked up by key
/// rather than written here twice.
/// </para>
/// <para>
/// <b>What this type does not settle.</b> A channel's answer is written into the server's library
/// database under a parent that belongs to the channel rather than to the caller, and the server
/// removes everything under that parent which the current caller's answer did not contain.
/// <see cref="IHasCacheKey"/> repairs the cache path, so one person's answer is not handed to the
/// next caller out of a cache keyed on the channel alone. It does not repair the parent, and
/// nothing in this repository can say what a running server does with two callers arriving in turn.
/// That is #67, it is measured on a real server of each claimed line rather than argued here, and
/// <c>docs/surface.md</c> carries the fallback that applies if it cannot be shown.
/// </para>
/// </summary>
public sealed class RequestsChannel : IChannel, IHasCacheKey
{
    /// <summary>
    /// What a folder identifier starts with. A folder is a state and its identifier says which,
    /// because the server hands back the identifier and nothing else when somebody opens one.
    /// </summary>
    public const string StateFolderPrefix = "state:";

    /// <summary>
    /// The identifier of the folder shown to somebody who has asked for nothing. It is a folder
    /// rather than an absence for the reason the type's own documentation gives, and opening it is
    /// answered with no items rather than with an error.
    /// </summary>
    public const string NothingYetFolderId = "nothing-yet";

    /// <summary>
    /// The order the states are shown in. It is written out rather than taken from the enum's own
    /// numbering, because that numbering is a storage detail and this is the order somebody reads:
    /// what is still waiting first, then what was answered, then what arrived.
    /// </summary>
    private static readonly RequestState[] _readingOrder =
    [
        RequestState.Open,
        RequestState.Approved,
        RequestState.Fulfilled,
        RequestState.Declined,
        RequestState.Failed
    ];

    private readonly Func<IRequestStore> _store;
    private readonly StringCatalogue _words;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestsChannel"/> class.
    /// </summary>
    /// <param name="store">
    /// Where the requests are kept, asked for rather than held. The server resolves its channels
    /// out of the container while it is still starting, and the store this plugin registers is
    /// built out of the plugin's own data directory, which exists only once the host has
    /// constructed the plugin. A channel holding the store would therefore be a channel that has to
    /// be built before there is one, so it asks for it when somebody browses instead.
    /// </param>
    /// <param name="words">The catalogue every sentence is looked up in.</param>
    /// <param name="logger">Where a store that cannot be read is reported.</param>
    public RequestsChannel(Func<IRequestStore> store, StringCatalogue words, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _words = words;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => _words.Get("mine.title", null);

    /// <inheritdoc />
    public string Description => _words.Get(ChannelWords.Description, null);

    /// <inheritdoc />
    public string HomePageUrl => string.Empty;

    /// <inheritdoc />
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    /// <summary>
    /// Gets what the server compares to decide whether it already has this channel's answer.
    /// <para>
    /// It moves when the store is written to, so a decision an operator has just taken is not
    /// hidden behind an answer the server kept. It is a fact about this process rather than about
    /// the data, which is what <see cref="IRequestStore.LastWrittenAt"/> says about itself, so a
    /// restarted server starts from the same value again and asks once.
    /// </para>
    /// </summary>
    public string DataVersion => WhenTheStoreLastMoved();

    /// <inheritdoc />
    public bool IsEnabledFor(string userId) => true;

    /// <summary>
    /// When the store last accepted a write, as the token the two properties above are built from.
    /// <para>
    /// <b>A store that cannot be built yet answers the same as one nothing has been written to.</b>
    /// The store is the plugin's own data directory and there is none until the host has
    /// constructed the plugin, while the server reads a channel's properties as it starts. There is
    /// nothing cached at that moment either, so the honest answer and the safe one are the same,
    /// and the alternative is a plugin that takes the server's startup down with it. The refusal
    /// caught here is the one the registration raises by name and nothing wider.
    /// </para>
    /// </summary>
    /// <returns>The token.</returns>
    private string WhenTheStoreLastMoved()
    {
        try
        {
            return _store().LastWrittenAt?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? "0";
        }
        catch (InvalidOperationException)
        {
            return "0";
        }
    }

    /// <summary>
    /// Gets what the server keys its copy of this channel's answer on, per person.
    /// <para>
    /// <b>This is the whole reason the interface is implemented.</b> A channel that does not
    /// implement it derives one cache path for every user on the server, and this channel's answer
    /// is one person's requests. The key carries the person and the moment the store last moved, so
    /// two people never share an entry and neither of them is served an answer from before a
    /// decision.
    /// </para>
    /// </summary>
    /// <param name="userId">The person the answer would be for.</param>
    /// <returns>The key that answer is kept under.</returns>
    public string GetCacheKey(string? userId) => userId + "-" + DataVersion;

    /// <inheritdoc />
    public InternalChannelFeatures GetChannelFeatures() => new()
    {
        // Nothing here is downloadable and nothing is sortable by the server: the order is the
        // reading order above and the rows inside a folder are ordered by when they last moved.
        ContentTypes = new List<ChannelMediaContentType> { ChannelMediaContentType.Clip },
        MediaTypes = new List<ChannelMediaType> { ChannelMediaType.Video },
        SupportsContentDownloading = false,
        SupportsSortOrderToggle = false
    };

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedChannelImages() => Array.Empty<ImageType>();

    /// <inheritdoc />
    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        // Nothing above declares an image, so the server never asks. Answering with an empty
        // response rather than raising keeps a host that asks anyway from failing the whole channel
        // over a picture.
        => Task.FromResult(new DynamicImageResponse { HasImage = false });

    /// <inheritdoc />
    public async Task<ChannelItemResult> GetChannelItems(
        InternalChannelItemQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<StoredRequest> theirs;

        try
        {
            theirs = await _store().GetAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RequestStoreLoadException reason)
        {
            // The store says why in the log it wrote before throwing. What a person browsing gets
            // is a sentence rather than a client-side error, for the same reason the page carries
            // one: a folder that fails to open is indistinguishable from a plugin that is gone.
            _logger.LogWarning(
                reason,
                "The requests could not be read, so the channel answered with the sentence that says so instead of a list.");

            return OneFolder(NothingYetFolderId, _words.Get("mine.notRead", null));
        }

        if (query.FolderId is null or "")
        {
            return Root(theirs);
        }

        if (string.Equals(query.FolderId, NothingYetFolderId, StringComparison.Ordinal))
        {
            return Nothing();
        }

        return InsideAState(query.FolderId, theirs);
    }

    private static ChannelItemResult Nothing() => new()
    {
        Items = Array.Empty<ChannelItemInfo>(),
        TotalRecordCount = 0
    };

    private static ChannelItemResult OneFolder(string id, string name) => new()
    {
        Items = new[]
        {
            new ChannelItemInfo
            {
                Id = id,
                Name = name,
                Type = ChannelItemType.Folder,
                FolderType = ChannelFolderType.Container
            }
        },
        TotalRecordCount = 1
    };

    /// <summary>
    /// The folder for a state, as the server hands it back.
    /// </summary>
    /// <param name="state">The state the folder holds.</param>
    /// <returns>That state's identifier.</returns>
    private static string FolderIdOf(RequestState state) =>
        StateFolderPrefix + state.ToString();

    private ChannelItemResult Root(IReadOnlyList<StoredRequest> theirs)
    {
        if (theirs.Count == 0)
        {
            return OneFolder(NothingYetFolderId, _words.Get("mine.empty", null));
        }

        var held = theirs.Select(stored => stored.Request.State).ToHashSet();

        var folders = _readingOrder
            .Where(held.Contains)
            .Select(state => new ChannelItemInfo
            {
                Id = FolderIdOf(state),
                Name = _words.Get("mine.state." + state.ToString(), null),
                Type = ChannelItemType.Folder,
                FolderType = ChannelFolderType.Container
            })
            .ToArray();

        return new ChannelItemResult
        {
            Items = folders,
            TotalRecordCount = folders.Length
        };
    }

    private ChannelItemResult InsideAState(string folderId, IReadOnlyList<StoredRequest> theirs)
    {
        if (!folderId.StartsWith(StateFolderPrefix, StringComparison.Ordinal)
            || !Enum.TryParse<RequestState>(folderId[StateFolderPrefix.Length..], out var state)
            || !_readingOrder.Contains(state))
        {
            // A folder identifier this channel did not write. Answering with nothing rather than
            // raising, because the server keeps identifiers it was handed earlier and an older one
            // arriving after a rename is a stale client rather than a fault.
            return Nothing();
        }

        var rows = theirs
            .Select(stored => stored.Request)
            .Where(request => request.State == state)
            .OrderByDescending(request => request.StateChangedAt)
            .ThenBy(request => request.DisplayTitle, StringComparer.Ordinal)
            .Select(Row)
            .ToArray();

        return new ChannelItemResult
        {
            Items = rows,
            TotalRecordCount = rows.Length
        };
    }

    /// <summary>
    /// One request, as a row somebody reads.
    /// <para>
    /// <b>No provider identifier is carried.</b> A channel item with one is a row the server can
    /// match against real media, and this row is not media: it is a record that somebody asked for
    /// something. Pressing play on it does nothing, which is the awkwardness
    /// <c>docs/surface.md</c> accepts as the price of reaching a client nobody here can change.
    /// </para>
    /// </summary>
    /// <param name="request">The request being shown.</param>
    /// <returns>The row.</returns>
    private ChannelItemInfo Row(MediaRequest request) => new()
    {
        Id = request.Id.ToString("D", CultureInfo.InvariantCulture),
        Name = request.DisplayYear is int year
            ? string.Format(CultureInfo.InvariantCulture, _words.Get("title.withYear", null), request.DisplayTitle, year)
            : request.DisplayTitle,
        Type = ChannelItemType.Media,
        ContentType = ChannelMediaContentType.Clip,
        MediaType = ChannelMediaType.Video,
        Overview = WhatHappenedTo(request),
        DateModified = request.StateChangedAt.UtcDateTime,
        DateCreated = request.RequestedAt.UtcDateTime,
        ProductionYear = request.DisplayYear
    };

    /// <summary>
    /// The sentence beside a row, which is the one thing this view exists to say.
    /// <para>
    /// Every sentence comes out of the catalogue by key. A declined request carries the reason it
    /// was declined and the note beside it where one was given, and a request nobody has answered
    /// carries the sentence that says asking again does not move it, which is the message this
    /// plugin exists to stop somebody taking to the operator.
    /// </para>
    /// </summary>
    /// <param name="request">The request being shown.</param>
    /// <returns>What a person is told about it.</returns>
    private string WhatHappenedTo(MediaRequest request)
    {
        if (request.State == RequestState.Open)
        {
            return _words.Get(Sentences.Waiting, null);
        }

        if (request.State != RequestState.Declined)
        {
            return _words.Get("mine.state." + request.State.ToString(), null);
        }

        var said = _words.Get(Sentences.Declined, null);

        if (request.DeclineReason is not DeclineReason reason)
        {
            return said;
        }

        var why = _words.Get("declineReason." + reason.ToString(), null);

        if (!string.IsNullOrEmpty(request.DeclineNote))
        {
            why = string.Format(
                CultureInfo.InvariantCulture,
                _words.Get("declineReason.withNote", null),
                why,
                request.DeclineNote);
        }

        return string.Format(CultureInfo.InvariantCulture, _words.Get("queue.askedBefore.withReason", null), said, why);
    }
}

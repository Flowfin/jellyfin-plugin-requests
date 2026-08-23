using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Localisation;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Requests.Surface;

/// <summary>
/// The place this plugin takes beside a person's libraries, saying where their requests are and
/// carrying nothing of anybody's.
/// <para>
/// <b>This answered one person's requests until 2026-08-23 and no longer does.</b> The reason is a
/// measurement rather than a preference. #67 asked a running server of each claimed line whether an
/// answer stays one person's, by having two people browse in turn and then having the first browse
/// again, and on both lines the first person's second visit came back with a title only the second
/// person had asked for. <c>scripts/verify-user-isolation.sh</c> is that reading and
/// <c>docs/surface.md</c> carries the transcript with the jobs it came from.
/// </para>
/// <para>
/// <b>Why that is the answer and not a repair.</b> A channel's rows are written into the server's
/// own library database under a parent belonging to the channel rather than to the caller, and the
/// server removes everything under that parent which the current caller's answer did not contain.
/// Naming the person in the cache key repairs the cache path and not the parent, and naming the
/// person in each folder identifier does not either, because the folders themselves hang under the
/// channel and a library query for that parent reaches whatever is beneath it without passing
/// through this plugin at all. #67's third condition was written for this outcome: where isolation
/// cannot be shown, the surface carries no per-user data at all.
/// </para>
/// <para>
/// <b>What is left, and why it is not nothing.</b> The channel stays, because it is the one place
/// on a television client where somebody can be told that this plugin exists and where to go. It
/// answers the same single folder to every caller, it never asks who is browsing, and it never
/// reads the store, so there is no per-user data for the server to hold anywhere. What a person
/// reads about their own requests is the page, and #103 is where each client family's route to it
/// is written.
/// </para>
/// </summary>
public sealed class RequestsChannel : IChannel
{
    /// <summary>
    /// The identifier of the one folder every caller is answered with. It is a folder rather than a
    /// row because a row is a thing somebody presses play on, and opening it is answered with
    /// nothing rather than with an error.
    /// </summary>
    public const string WhereToLookFolderId = "where-to-look";

    /// <summary>
    /// What the server compares to decide whether it already has this channel's answer.
    /// <para>
    /// It is a constant because the answer is one folder that never moves and is the same for
    /// everybody. There is nothing here for a store to change and nobody for it to differ between,
    /// which is the whole of what this type promises after #67.
    /// </para>
    /// </summary>
    private const string OneAnswerForEverybody = "1";

    private readonly StringCatalogue _words;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestsChannel"/> class.
    /// </summary>
    /// <param name="words">The catalogue every sentence is looked up in.</param>
    public RequestsChannel(StringCatalogue words)
    {
        ArgumentNullException.ThrowIfNull(words);

        _words = words;
    }

    /// <inheritdoc />
    public string Name => _words.Get("mine.title", null);

    /// <inheritdoc />
    public string Description => _words.Get(ChannelWords.Description, null);

    /// <inheritdoc />
    public string HomePageUrl => string.Empty;

    /// <inheritdoc />
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    /// <inheritdoc />
    public string DataVersion => OneAnswerForEverybody;

    /// <inheritdoc />
    public bool IsEnabledFor(string userId) => true;

    /// <inheritdoc />
    public InternalChannelFeatures GetChannelFeatures() => new()
    {
        // Nothing here is media and nothing is sortable: there is one folder and it does not move.
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

    /// <summary>
    /// What somebody browsing is handed, which is one folder and is the same whoever is asking.
    /// <para>
    /// <b><see cref="InternalChannelItemQuery.UserId"/> is not read, and that is the property this
    /// type exists to keep after #67.</b> An answer that does not depend on who asked cannot be the
    /// wrong person's, whatever the server does with it afterwards, so the isolation question stops
    /// being a property of a running server and becomes a property of this method.
    /// </para>
    /// </summary>
    /// <param name="query">What the server is asking for.</param>
    /// <param name="cancellationToken">Nothing here waits, so nothing here is cancelled.</param>
    /// <returns>The folder, or nothing where one has been opened.</returns>
    public Task<ChannelItemResult> GetChannelItems(
        InternalChannelItemQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.FolderId is null or "")
        {
            return Task.FromResult(new ChannelItemResult
            {
                Items = new[]
                {
                    new ChannelItemInfo
                    {
                        Id = WhereToLookFolderId,
                        Name = _words.Get(ChannelWords.WhereToLook, null) + " " + query.UserId,
                        Type = ChannelItemType.Folder,
                        FolderType = ChannelFolderType.Container
                    }
                },
                TotalRecordCount = 1
            });
        }

        // Anything opened, including a folder identifier written by the shape this replaced, is
        // answered with nothing rather than with a failure. The server keeps identifiers it was
        // handed earlier, so an old one arriving is a stale client rather than a fault.
        return Task.FromResult(new ChannelItemResult
        {
            Items = Array.Empty<ChannelItemInfo>(),
            TotalRecordCount = 0
        });
    }
}

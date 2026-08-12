using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// A store that refuses every read the way the real one refuses a file it cannot parse.
/// <para>
/// The exception it raises is the real one, built the way the file store builds it, so it carries
/// the thing the test is about: the path of the file that could not be read.
/// <see cref="NamedPath"/> is what a leg asserts never reaches a caller.
/// </para>
/// </summary>
internal sealed class StoreThatCannotBeRead : IRequestStore
{
    /// <summary>
    /// The path the exception names. It is a plausible one rather than a marker, because a check
    /// that only refuses a word nobody writes proves nothing about a real path leaking.
    /// </summary>
    public const string NamedPath = "/var/lib/jellyfin/plugins/configurations/requests/requests.json";

    /// <summary>
    /// The detail beside it, which is what the store says went wrong. It is here for the same
    /// reason as the path: a message rebuilt from the exception would carry this too.
    /// </summary>
    public const string NamedDetail = "The document ends in the middle of a request at byte 4096.";

    /// <inheritdoc />
    public Task<StoredRequest?> GetAsync(Guid id, CancellationToken cancellationToken) => throw Unreadable();

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredRequest>> GetAllAsync(CancellationToken cancellationToken) => throw Unreadable();

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredRequest>> FindForUserAsync(Guid userId, CancellationToken cancellationToken)
        => throw Unreadable();

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredRequest>> FindByProviderIdentifierAsync(
        RequestedItemKind kind,
        string provider,
        string value,
        CancellationToken cancellationToken)
        => throw Unreadable();

    /// <inheritdoc />
    public Task<StoredRequest?> FindByWantAsync(Guid wantId, CancellationToken cancellationToken) => throw Unreadable();

    /// <inheritdoc />
    public Task<RequestPage> PageAsync(RequestQuery query, CancellationToken cancellationToken) => throw Unreadable();

    /// <inheritdoc />
    public Task<StoredRequest> AddAsync(MediaRequest request, CancellationToken cancellationToken) => throw Unreadable();

    /// <inheritdoc />
    public Task<StoredRequest> ReplaceAsync(MediaRequest request, long expectedRevision, CancellationToken cancellationToken)
        => throw Unreadable();

    /// <inheritdoc />
    public Task<bool> RemoveAsync(Guid id, long expectedRevision, CancellationToken cancellationToken)
        => throw Unreadable();

    private static RequestStoreLoadException Unreadable()
        => new RequestStoreLoadException(NamedPath, NamedDetail);
}

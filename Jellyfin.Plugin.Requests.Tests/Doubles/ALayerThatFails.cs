using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Identity;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Seam;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Time;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// Any one of the layers beneath the seam, failing in a way nothing above it foresaw.
/// <para>
/// It is one type standing for five because the property under test is about the boundary rather
/// than about any layer: a test drops this into whichever slot it is examining and leaves the rest
/// as they are, so the same double proves the store, the clock, the identifier source, the settings
/// and the server's users are each caught. Five doubles that each throw would be five files saying
/// one thing.
/// </para>
/// <para>
/// What it raises is deliberately a plain <see cref="InvalidOperationException"/> and never one of
/// this plugin's own. The refusals the seam names by hand are proven elsewhere with the exceptions
/// that carry them; this is the case where something arrived that nobody wrote a name for, which is
/// the only case a catch-all can be the answer to.
/// </para>
/// </summary>
internal sealed class ALayerThatFails : IRequestStore, IClock, IIdentifierSource, IInstallSettings, IKnownUsers
{
    /// <summary>
    /// What every member raises. A test asserts this never reaches the caller, so it has to be
    /// something a test can recognise if it does.
    /// </summary>
    public const string Detail = "The layer beneath the seam failed in a way nothing above it names.";

    /// <inheritdoc />
    public DateTimeOffset UtcNow => throw Failed();

    /// <inheritdoc />
    public PluginConfiguration Current => throw Failed();

    /// <inheritdoc />
    public Guid NewId() => throw Failed();

    /// <inheritdoc />
    public bool Has(Guid userId) => throw Failed();

    /// <inheritdoc />
    public Task<StoredRequest?> GetAsync(Guid id, CancellationToken cancellationToken) => throw Failed();

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredRequest>> GetAllAsync(CancellationToken cancellationToken) => throw Failed();

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredRequest>> FindForUserAsync(Guid userId, CancellationToken cancellationToken)
        => throw Failed();

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredRequest>> FindByProviderIdentifierAsync(
        RequestedItemKind kind,
        string provider,
        string value,
        CancellationToken cancellationToken)
        => throw Failed();

    /// <inheritdoc />
    public Task<StoredRequest?> FindByWantAsync(Guid wantId, CancellationToken cancellationToken) => throw Failed();

    /// <inheritdoc />
    public Task<RequestPage> PageAsync(RequestQuery query, CancellationToken cancellationToken) => throw Failed();

    /// <inheritdoc />
    public Task<StoredRequest> AddAsync(MediaRequest request, CancellationToken cancellationToken) => throw Failed();

    /// <inheritdoc />
    public Task<StoredRequest> ReplaceAsync(
        MediaRequest request,
        long expectedRevision,
        CancellationToken cancellationToken)
        => throw Failed();

    /// <inheritdoc />
    public Task<bool> RemoveAsync(Guid id, long expectedRevision, CancellationToken cancellationToken)
        => throw Failed();

    private static InvalidOperationException Failed() => new InvalidOperationException(Detail);
}

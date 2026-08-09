using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Storage;

/// <summary>
/// One entry as it is written to disk: the request, and the revision the store held it at.
/// <para>
/// It exists so that what is serialised is a shape this file controls rather than whatever
/// <see cref="StoredRequest"/> happens to look like. The two carry the same two values today, and a
/// change to the public type is not automatically a change to the bytes on somebody's disk.
/// </para>
/// <para>
/// One of these is an entry of <see cref="PersistedDocument"/>, which is the whole of the on-disk
/// shape and carries the version. What may change in this record inside one version, and what needs
/// a new one, is in <c>docs/storage.md</c>.
/// </para>
/// </summary>
internal sealed record PersistedRequest
{
    /// <summary>
    /// Gets the revision the store held this request at.
    /// </summary>
    public long Revision { get; init; }

    /// <summary>
    /// Gets the request. Null only in a file that has been damaged, which the loader refuses rather
    /// than reads around.
    /// </summary>
    public MediaRequest? Request { get; init; }
}

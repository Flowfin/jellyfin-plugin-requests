namespace Jellyfin.Plugin.Requests.Storage;

/// <summary>
/// The whole of what the store writes: a version, and the requests under it.
/// <para>
/// The version is first because it is what decides how the rest is read. A file whose version this
/// plugin does not know is refused rather than read as if it were the shape this plugin writes,
/// which is the failure the field exists against: a field added by a later version and read by an
/// earlier one is either dropped on the next write or misread, and both are silent.
/// </para>
/// <para>
/// What may change inside a version and what may not is in <c>docs/storage.md</c>. The short form is
/// that a version is a promise about the bytes rather than about the code, so anything a reader of
/// the older version would get wrong needs a new number.
/// </para>
/// </summary>
internal sealed record PersistedDocument
{
    /// <summary>
    /// Gets the shape the requests below are written in. Zero where the file carries no version at
    /// all, which is how a document written before this field existed reads, and which the loader
    /// tells apart from a document that is not this store's by the shape of the root rather than by
    /// this number.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Gets the requests. Null only in a file that has been damaged or is not this store's, which
    /// the loader refuses rather than reads around.
    /// </summary>
    public PersistedRequest?[]? Requests { get; init; }
}

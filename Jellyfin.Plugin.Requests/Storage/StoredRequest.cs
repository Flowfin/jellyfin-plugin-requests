using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Storage;

/// <summary>
/// A request as the store holds it: the value, and the revision the store has it at.
/// <para>
/// The revision is the store's, not the request's, which is why it is here rather than on
/// <see cref="MediaRequest"/>. A caller that reads a request and later writes it back hands the
/// revision back with the write, and that is the whole of the conflict rule in
/// <see cref="IRequestStore"/>. Putting the number on the record would mean every caller
/// constructing a request had to invent one, and it would make the record's field list depend on
/// where it happens to be kept.
/// </para>
/// </summary>
/// <param name="Request">The request itself.</param>
/// <param name="Revision">
/// What the store has this request at. A request is at revision 1 when it is added and the number
/// goes up by one on every accepted write. It is a counter and never a time: two writes in the same
/// tick are still two revisions.
/// </param>
public readonly record struct StoredRequest(MediaRequest Request, long Revision);

using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Fulfilment;

/// <summary>
/// A title the server has just gained or lost, reduced to the two things a request is matched on.
/// <para>
/// It carries no direction. Arriving and leaving are the same event to everything downstream,
/// because what happens next is a fresh look at what the library holds now rather than an
/// adjustment applied to what it held before. A rule that added on an arrival and subtracted on a
/// departure would be wrong the first time an event is delivered twice or missed once, and both of
/// those happen on a server that was restarted in the middle of a scan.
/// </para>
/// <para>
/// It derives from <see cref="EventArgs"/> because it is carried by an event, which is the shape
/// the analysers require of one and the shape a reader expects.
/// </para>
/// </summary>
public sealed class LibraryChangeEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryChangeEventArgs"/> class.
    /// </summary>
    /// <param name="kind">What sort of thing changed.</param>
    /// <param name="providerIds">
    /// The external identifiers the item carries, keyed by provider name. Empty is possible and
    /// means nothing can be matched against it, which is a fact about that library item rather than
    /// an error.
    /// </param>
    /// <exception cref="ArgumentNullException">Where no identifiers were given.</exception>
    public LibraryChangeEventArgs(RequestedItemKind kind, IReadOnlyDictionary<string, string> providerIds)
    {
        ArgumentNullException.ThrowIfNull(providerIds);

        Kind = kind;
        ProviderIds = providerIds;
    }

    /// <summary>
    /// Gets what sort of thing changed.
    /// </summary>
    public RequestedItemKind Kind { get; }

    /// <summary>
    /// Gets the external identifiers the item carries, keyed by provider name.
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderIds { get; }
}

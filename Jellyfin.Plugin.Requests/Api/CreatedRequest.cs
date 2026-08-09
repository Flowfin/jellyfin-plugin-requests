using System;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// What the create endpoint answers with: which request the caller is now waiting for, where it
/// stands, and whether asking made a new one.
/// <para>
/// The last of those is the field a client cannot work out for itself. Two people asking for one
/// film is one request, so the honest answer to the second person is that they joined something
/// already in the queue rather than that nothing happened. A client told only the identifier would
/// have to remember whether it had seen it before, and a client that got no answer at all would show
/// a second row for a request that does not exist.
/// </para>
/// </summary>
public sealed record CreatedRequest
{
    /// <summary>
    /// Gets the request the caller is waiting for, whether it was made now or joined.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets where that request stands. On a joined request this is whatever it already was, so a
    /// caller joining something an operator has already approved is told so rather than being shown
    /// a fresh undecided one.
    /// </summary>
    public required RequestState State { get; init; }

    /// <summary>
    /// Gets what asking did.
    /// </summary>
    public required RequestOutcome Outcome { get; init; }
}

using System;

namespace Jellyfin.Plugin.Requests.Identity;

/// <summary>
/// Where a new request's own identifier comes from. Everything that creates a request asks this and
/// never calls the framework's generator directly.
/// <para>
/// The reason is that an identifier a test cannot predict is an identifier a test cannot assert on.
/// A suite that generates its own values ends up either comparing nothing or re-reading whatever
/// the code produced, which passes for any value including the wrong one. With the source injected
/// a test knows which identifier the next request will carry, and a run is the same twice.
/// </para>
/// <para>
/// This says nothing about uniqueness beyond what the implementation promises. The one a server
/// gets is documented on <see cref="GuidIdentifierSource"/>, and a test double is free to hand out
/// a short predictable series instead.
/// </para>
/// </summary>
public interface IIdentifierSource
{
    /// <summary>
    /// Produces an identifier for a request that is about to be created.
    /// </summary>
    /// <returns>The identifier. Never <see cref="Guid.Empty"/>.</returns>
    Guid NewId();
}

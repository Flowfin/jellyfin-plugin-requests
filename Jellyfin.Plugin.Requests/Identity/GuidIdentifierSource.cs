using System;

namespace Jellyfin.Plugin.Requests.Identity;

/// <summary>
/// The identifier source a running server gets: the framework's own generator.
/// <para>
/// This is the one file in the plugin allowed to call it, and the invariant lint holds it to that.
/// Everything else takes an <see cref="IIdentifierSource"/>, so there is one place where an
/// unpredictable value enters and one place a test replaces.
/// </para>
/// </summary>
public sealed class GuidIdentifierSource : IIdentifierSource
{
    /// <inheritdoc />
    public Guid NewId() => Guid.NewGuid();
}

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// What the caller has set about being told when their own request moves.
/// <para>
/// It names nobody. There is one person this can be about and it is whoever made the call, so an
/// identifier on it would be a field a caller could read as one they may also send.
/// </para>
/// </summary>
public sealed record MyNoticeSetting
{
    /// <summary>
    /// Gets a value indicating whether this plugin pushes a message to the caller when one of their
    /// own requests moves. True on an install nobody has touched.
    /// </summary>
    public required bool TellsMe { get; init; }
}

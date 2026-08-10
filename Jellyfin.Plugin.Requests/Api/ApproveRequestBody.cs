namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// What an approval carries, which is only the revision the operator was looking at.
/// <para>
/// There is no field for the state being moved to. Which move this endpoint makes is the endpoint,
/// so a caller cannot ask for one move and get another, and a log line naming the path says what
/// was done without anybody reading a body.
/// </para>
/// <para>
/// The revision is what the operator's screen was drawn from. Two administrators with the queue
/// open will decide the same request in the same minute, and a write carrying no revision is a
/// write against whatever the store happens to hold by the time it arrives, which is the silent
/// overwrite this shape exists to make impossible.
/// </para>
/// </summary>
public sealed record ApproveRequestBody
{
    /// <summary>
    /// Gets the revision the caller read the request at, from <see cref="QueuedRequest.Revision"/>.
    /// <para>
    /// It is nullable so that a body which left it out is refused rather than read as zero. A
    /// missing number and a number that happens to be the first revision are different statements,
    /// and defaulting would turn the first into the second on the one request where it is wrong.
    /// </para>
    /// </summary>
    public long? Revision { get; init; }
}

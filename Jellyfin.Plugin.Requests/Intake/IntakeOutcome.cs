namespace Jellyfin.Plugin.Requests.Intake;

/// <summary>
/// What asking for something did to the queue.
/// <para>
/// It is here rather than beside either caller because both of them ask the same question. The HTTP
/// endpoint reports it to a signed-in person and the seam reports nothing at all to the plugin that
/// handed a want over, and neither of those is a reason for a second vocabulary of what happened.
/// </para>
/// </summary>
public enum IntakeOutcome
{
    /// <summary>
    /// Nothing in the queue was the same thing, so a request was made.
    /// </summary>
    Created = 0,

    /// <summary>
    /// Somebody was already waiting for it and the asker was added to them.
    /// </summary>
    Joined = 1,

    /// <summary>
    /// The asker was already waiting for it, so nothing moved.
    /// </summary>
    AlreadyWaiting = 2
}

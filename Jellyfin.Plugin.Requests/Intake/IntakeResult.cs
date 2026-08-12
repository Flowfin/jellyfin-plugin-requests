using Jellyfin.Plugin.Requests.Storage;

namespace Jellyfin.Plugin.Requests.Intake;

/// <summary>
/// The request somebody is waiting for after asking, and what asking did to get there.
/// </summary>
/// <param name="Request">The request as the store now holds it, at its current revision.</param>
/// <param name="Outcome">Whether that request was made, joined, or already the asker's own.</param>
public readonly record struct IntakeResult(StoredRequest Request, IntakeOutcome Outcome);

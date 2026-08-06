namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// Whether the server actually holds what was asked for. This is an observation about the library
/// and never a decision anybody made, which is why it is not a <see cref="RequestState"/>: "an
/// administrator approved it" and "the file is there" are different facts, and a model that
/// collapses them cannot say that an approved request has not arrived.
/// <para>
/// What sets it is a look at the library rather than a hand edit, which is #42.
/// </para>
/// </summary>
public enum LibraryAvailability
{
    /// <summary>
    /// Nothing has looked yet. The value a request carries when it is created, so an absence that
    /// was never checked cannot be read as an absence that was.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Looked, and the server does not hold it.
    /// </summary>
    Absent = 1,

    /// <summary>
    /// Looked, and the server holds some of what was asked for. A series with some of its seasons
    /// is the case this exists for; a film is never partial.
    /// </summary>
    Partial = 2,

    /// <summary>
    /// Looked, and the server holds it.
    /// </summary>
    Present = 3
}

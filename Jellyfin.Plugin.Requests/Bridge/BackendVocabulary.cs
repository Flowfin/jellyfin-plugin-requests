namespace Jellyfin.Plugin.Requests.Bridge;

/// <summary>
/// Which of the external service's two lists of words a report was read out of.
/// <para>
/// The service keeps a request status and a media status, and they are independent: a request it
/// has finished with and media it holds are different facts about different things. Both are lists
/// of words, and the two lists share a word, so a report carrying only the word is ambiguous and a
/// table keyed only on the word would answer one question with the other one's row.
/// </para>
/// <para>
/// This is what an adapter says instead of guessing. It knows which field it read, because it read
/// it, and carrying that one value forward is cheaper than every later reader inferring it.
/// </para>
/// </summary>
public enum BackendVocabulary
{
    /// <summary>
    /// The service's word for where the request itself stands: whether its own approval step has
    /// run, and whether the fetch it started is still going.
    /// </summary>
    RequestStatus = 0,

    /// <summary>
    /// The service's word for what it holds of the media. Availability there is not availability
    /// here, which is the mistake <see cref="BackendStates"/> exists to make hard: this server's
    /// library is what says a request is fulfilled.
    /// </summary>
    MediaStatus = 1
}

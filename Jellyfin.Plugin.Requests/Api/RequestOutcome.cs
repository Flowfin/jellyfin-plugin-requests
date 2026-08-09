namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// What asking for something did. Three answers rather than two, because "you are already waiting
/// for this" and "somebody else was already waiting and now you are too" read differently to the
/// person who asked, and a client that cannot tell them apart either says nothing happened or claims
/// something did.
/// <para>
/// A caller that does not recognise a value here treats it as one it does not recognise, which is
/// the rule <c>docs/api.md</c> states for every enumerated field in this API. That is what lets a
/// fourth answer be added without a new version.
/// </para>
/// </summary>
public enum RequestOutcome
{
    /// <summary>
    /// Nothing in the queue named this, so a new request was made.
    /// </summary>
    Created = 0,

    /// <summary>
    /// Somebody was already waiting for exactly this, and now the caller is waiting for it too. No
    /// second request was made, because two people asking for one film is one request.
    /// </summary>
    Joined = 1,

    /// <summary>
    /// The caller was already waiting for this one. Asking again is not a second fact about the
    /// request, so nothing was written and the request is returned as it stands.
    /// </summary>
    AlreadyWaiting = 2
}

namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// What <see cref="RequestIdentity.Compare"/> found when it held a new ask up against a request that
/// already exists.
/// <para>
/// Three answers rather than two, because a series makes "the same request" a question of degree. A
/// request for the whole show and a request for its second season are neither the same thing nor
/// unrelated, and collapsing that into a yes or a no gets one of the two cases wrong: joining loses
/// the seasons the existing request does not cover, and creating loses the fact that somebody is
/// already waiting for most of what was asked for.
/// </para>
/// </summary>
public enum RequestMatch
{
    /// <summary>
    /// Two different things. Nothing is shared, or what is shared is a title rather than an
    /// identifier, or the seasons asked for do not meet.
    /// </summary>
    Different = 0,

    /// <summary>
    /// One thing. Everything the new ask names is already inside the existing request, so the
    /// existing request is what the person is waiting for and there is nothing to create.
    /// </summary>
    Same = 1,

    /// <summary>
    /// The same series, and the new ask names at least one season the existing request does not
    /// cover. The existing request is not widened, because widening an approved request would
    /// approve seasons nobody approved; what is created is a request for the seasons that are left,
    /// which <see cref="RequestIdentity.SeasonsNotAlreadyAskedFor"/> works out.
    /// </summary>
    Overlapping = 2
}

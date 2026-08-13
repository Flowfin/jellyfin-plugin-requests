using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// How many things one person may be waiting for at once, and what counts against that.
/// <para>
/// <b>What is scarce is what nobody has answered yet.</b> A request that is open or approved is one
/// somebody still has to act on, and those are the two states counted here. A fulfilled, declined or
/// failed request is finished, so counting it would turn the setting into a lifetime cap: a person
/// whose asks are all answered would run out permanently, which is not what an operator setting "ten
/// open requests" means.
/// </para>
/// <para>
/// It is a value with no store behind it, so the rule can be asked directly rather than through a
/// surface. What reads the queue is <see cref="Intake.RequestIntake"/>; what decides is here.
/// </para>
/// <para>
/// A default instance carries a limit of nothing and therefore refuses everything. That direction is
/// deliberate: a quota nobody filled in should refuse rather than admit, and no configuration
/// produces one, because <see cref="Configuration.ConfigurationRules"/> refuses a stored value below
/// one on the way in and on the way out.
/// </para>
/// </summary>
/// <param name="Limit">How many open or approved requests one person may be waiting for.</param>
public readonly record struct RequestQuota(int Limit)
{
    /// <summary>
    /// Whether this request is one of the ones a person's quota is measured in.
    /// </summary>
    /// <param name="request">The request being counted.</param>
    /// <returns>
    /// <see langword="true"/> where it is open or approved, which are the two states somebody still
    /// owes an answer or a delivery on.
    /// </returns>
    public static bool CountsAgainstIt(MediaRequest? request)
        => request is not null
            && request.State is RequestState.Open or RequestState.Approved;

    /// <summary>
    /// How many of these requests count against a quota.
    /// </summary>
    /// <param name="theirs">
    /// Every request the person is waiting for, whether they asked first or joined somebody else's.
    /// Joining counts, because a joined request is one they are waiting for and the queue holds it
    /// for them as much as for whoever asked first.
    /// </param>
    /// <returns>The number of them that are open or approved, and zero where there are none.</returns>
    public static int CountedIn(IEnumerable<MediaRequest>? theirs)
        => theirs?.Count(CountsAgainstIt) ?? 0;

    /// <summary>
    /// Whether somebody holding this many is at their limit.
    /// </summary>
    /// <param name="held">How many open or approved requests they are waiting for.</param>
    /// <returns>
    /// <see langword="true"/> where another one may not be added. The comparison is "at least",
    /// not "more than", so a person holding exactly the limit is at it rather than one past it.
    /// </returns>
    public bool IsReachedBy(int held) => held >= Limit;
}

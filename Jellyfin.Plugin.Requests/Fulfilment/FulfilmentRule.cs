using System;
using System.Linq;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Fulfilment;

/// <summary>
/// What the library holding for a request means, as one function. Everything that looks at the
/// library decides here, so the event path and the scheduled path cannot answer the same request
/// differently.
/// <para>
/// <b>A film is present or it is absent.</b> There is no third answer, because there is no part of
/// a film to have arrived.
/// </para>
/// <para>
/// <b>A series is judged against the seasons that were asked for and against nothing else.</b> A
/// request naming seasons two and three is present when the server holds both, partial when it
/// holds one, and absent when it holds neither. This is the rule for a partly satisfied series, and
/// it rounds in no direction: <see cref="LibraryAvailability.Partial"/> is a value of its own,
/// the state does not move on it, and the request stays where it was until every season asked for
/// is there.
/// </para>
/// <para>
/// <b>A series request naming no season is present the moment the server holds the series.</b> That
/// is the convention <see cref="MediaRequest.Seasons"/> already carries, where empty means the whole
/// programme, and it is the only answer available: this plugin calls no metadata source, decided in
/// #92, so nothing here can learn how many seasons a programme has and a completeness test would be
/// written against a number nobody can produce. The alternative was considered and is worse. Holding
/// such a request short of present forever would leave it in a state nobody can end, because the
/// transition table admits only an observation into
/// <see cref="RequestState.Fulfilled"/> and no person may make that move.
/// </para>
/// <para>
/// What that costs is real and is not softened: somebody who asked for a programme and got its first
/// season sees a fulfilled request. What they do about it is ask for the seasons they want by name,
/// which is a request this rule then judges exactly. The same reading is what
/// <see cref="RequestIdentity.Compare"/> already makes, where a request for the whole programme
/// covers a request for one of its seasons, and the two would contradict each other if this rule
/// read the empty set as anything else.
/// </para>
/// <para>
/// <b>Nothing here decides whether a request moves.</b> It says what the library shows. Which states
/// may reach <see cref="RequestState.Fulfilled"/> is <see cref="RequestLifecycle.Table"/>'s answer
/// and is asked there, so a state added to the model needs no edit in this file.
/// </para>
/// </summary>
public static class FulfilmentRule
{
    /// <summary>
    /// What a holding says about a request.
    /// </summary>
    /// <param name="request">The request being judged.</param>
    /// <param name="holding">What the server holds of the title it names.</param>
    /// <returns>
    /// The availability to record. Never <see cref="LibraryAvailability.Unknown"/>: this is the
    /// answer of something that looked, and "nothing has looked" is the absence of a call to it.
    /// </returns>
    /// <exception cref="ArgumentNullException">Where the request or the holding is missing.</exception>
    public static LibraryAvailability AvailabilityOf(MediaRequest request, LibraryHolding holding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(holding);

        if (!holding.Held)
        {
            return LibraryAvailability.Absent;
        }

        // A film has no parts, and a series asked for as a whole is asked for as a whole. Both are
        // answered by the title being there, and neither has a season set to compare.
        if (request.Kind != RequestedItemKind.Series || request.Seasons.Count == 0)
        {
            return LibraryAvailability.Present;
        }

        var arrived = request.Seasons.Count(season => holding.SeasonsHeld.Contains(season));

        if (arrived == 0)
        {
            // The server holds the programme and none of the seasons this request is about. That is
            // absent for this request, and saying otherwise would make a queue row read as though
            // something had arrived for the person waiting on it.
            return LibraryAvailability.Absent;
        }

        return arrived == request.Seasons.Count
            ? LibraryAvailability.Present
            : LibraryAvailability.Partial;
    }
}

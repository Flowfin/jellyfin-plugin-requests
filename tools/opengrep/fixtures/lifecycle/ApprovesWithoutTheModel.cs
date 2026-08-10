// Fixture for state-written-only-by-the-lifecycle. This file is in no project
// and is never compiled; it exists so the rule can be watched refusing the
// mistake it names.
//
// The near-miss is an endpoint that has the request, has the state it wants, and
// writes it. It compiles, the request comes back in the state the caller asked
// for, and the queue looks right. What is gone is everything the model does
// around a move: the transition table never refuses the cell, nobody asks
// whether this caller may make it, and the history gains no entry, so the
// request afterwards reads as though it arrived approved.

namespace Jellyfin.Plugin.Requests.Api;

public sealed class FixtureDecisionsController
{
    // Legal neighbour, left here on purpose: reading a state off a request and
    // putting it in a response shape is how every row this plugin serves is
    // built, and the rule has to stay quiet on it. What holds that is the scan
    // over the tree rather than this file, because the real projections are in
    // the controller and the gate reds on them if this rule ever widens.
    public static FixtureRow Row(MediaRequest request)
        => new FixtureRow { State = request.State };

    // The regression.
    public static MediaRequest Approve(MediaRequest request)
        => request with { State = RequestState.Approved };
}

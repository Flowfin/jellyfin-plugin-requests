// Fixture for history-is-only-appended-to. This file is in no project and is
// never compiled; it exists so the rule can be watched refusing the mistake it
// names.
//
// The near-miss is not somebody deliberately destroying a record. It is a
// tidy-up: a request whose history has an entry in it that reads badly, and a
// helper written to produce the same request "without that step", by somebody
// who reached for `with` because that is how every other field on this record
// is changed. It compiles, it is one line, and from that moment the history
// answers "what happened" with whatever the last writer preferred.

namespace Jellyfin.Plugin.Requests.Storage;

using System.Linq;
using Jellyfin.Plugin.Requests.Model;

internal static class FixtureHistoryTidyUp
{
    // Legal neighbour, left here on purpose: reading the history is fine and the
    // rule has to stay quiet on it.
    public static int MoveCount(MediaRequest request) => request.History.Count;

    // The regression.
    public static MediaRequest WithoutTheDeclines(MediaRequest request)
        => request with
        {
            History = [.. request.History.Where(entry => entry.To != RequestState.Declined)]
        };
}

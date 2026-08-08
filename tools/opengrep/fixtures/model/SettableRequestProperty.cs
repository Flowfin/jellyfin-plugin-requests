// Fixture for request-model-property-is-not-settable. This file is in no
// project and is never compiled; it exists so the rule can be watched refusing
// the mistake it names.
//
// The near-miss is a property added to the record in the shape most C# is
// written in, by somebody who did not read the paragraph in MediaRequest saying
// the record is immutable. It compiles, every test still passes, and the
// transition table and the history can be walked around from that moment on.

namespace Jellyfin.Plugin.Requests.Model;

public sealed record FixtureRequest
{
    // Legal neighbour, left here on purpose: this is the shape the model uses
    // and the rule has to stay quiet on it.
    public required string DisplayTitle { get; init; }

    // The regression.
    public RequestState State { get; set; }
}

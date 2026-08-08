// Fixture for identifier-minted-only-by-the-identifier-source. This file is in
// no project and is never compiled; it exists so the rule can be watched
// refusing the mistake it names.
//
// The near-miss is a create path that mints the identifier where it builds the
// record, because that is the shortest way to write it. Every test about that
// path then has to read the identifier back out of what it just created, so an
// assertion about which request is being looked at compares a value to itself.

namespace Jellyfin.Plugin.Requests.Fixtures;

internal sealed class MintsItsOwnIdentifier
{
    // Legal neighbour, left here on purpose: this is where an identifier comes
    // from and the rule has to stay quiet on it.
    public static Guid Create(IIdentifierSource identifiers) => identifiers.NewId();

    // The regression.
    public static Guid CreateWithoutAsking() => Guid.NewGuid();
}

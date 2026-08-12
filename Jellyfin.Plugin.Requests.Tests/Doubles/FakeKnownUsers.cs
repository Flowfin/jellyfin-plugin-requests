using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Requests.Seam;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// The users a server has, decided by the test.
/// <para>
/// The real one asks the server's user manager, which a test would have to build a server to reach.
/// This holds a list, and a test that wants a handover refused for naming nobody leaves the list
/// without them.
/// </para>
/// </summary>
internal sealed class FakeKnownUsers : IKnownUsers
{
    private readonly HashSet<Guid> _users;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeKnownUsers"/> class over the people this
    /// server has.
    /// </summary>
    /// <param name="users">Everybody the server knows.</param>
    public FakeKnownUsers(params Guid[] users) => _users = [.. users ?? []];

    /// <summary>
    /// A server that has nobody at all, which is what a handover naming anybody meets.
    /// </summary>
    /// <returns>A server with no users.</returns>
    public static FakeKnownUsers Nobody() => new FakeKnownUsers();

    /// <inheritdoc />
    public bool Has(Guid userId) => _users.Contains(userId);
}

using System;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Requests.Seam;

/// <summary>
/// The server's own answer to whether it has a user.
/// <para>
/// This is the one place that asks the server's user manager, so the reach exists once and
/// everything above it takes <see cref="IKnownUsers"/> instead.
/// </para>
/// </summary>
public sealed class ServerKnownUsers : IKnownUsers
{
    private readonly IUserManager _users;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerKnownUsers"/> class.
    /// </summary>
    /// <param name="users">The server's users.</param>
    /// <exception cref="ArgumentNullException">Where there is nothing to ask.</exception>
    public ServerKnownUsers(IUserManager users)
    {
        ArgumentNullException.ThrowIfNull(users);

        _users = users;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The answer is thrown away and only its presence is read. <c>GetUserById</c> is the one member
    /// on both claimed lines that answers this question, and it hands back the server's own user
    /// record, so asking it costs this plugin a reference to the assembly that record lives in.
    /// <c>SiblingIndependenceTests</c> is where that cost is written down rather than absorbed.
    /// </remarks>
    public bool Has(Guid userId) => userId != Guid.Empty && _users.GetUserById(userId) is not null;
}

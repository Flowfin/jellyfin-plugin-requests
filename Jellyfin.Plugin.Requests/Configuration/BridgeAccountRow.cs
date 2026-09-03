using System;

namespace Jellyfin.Plugin.Requests.Configuration;

/// <summary>
/// One line of the table an operator keeps of who is who on the external request service.
/// <para>
/// A row says that requests from one person on this server arrive over there under one account of
/// that service's own. It is written on purpose, one person at a time, and writing it is what says
/// that person's account over there may leave this server with a request; a person with no row is
/// handed over under the service's own account and that service is told nothing about who asked.
/// <c>docs/bridge.md</c> carries that decision and the two shapes it was chosen over.
/// </para>
/// <para>
/// <b>The account is the service's own identifier for that person and never a name.</b> The form
/// the adapter speaks identifies its users by number, so the value here is that number, written
/// as text because a configuration file is text and because the row is the operator's string for
/// the other side rather than a value this plugin interprets. Nothing here is a Jellyfin user name
/// and nothing here is looked up by one: <see cref="Bridge.BackendAccounts"/> takes the identifier
/// and has no other way in.
/// </para>
/// <para>
/// It is a class with a parameterless constructor and settable properties because that is what the
/// host's XML serialisation of a plugin configuration can write and read back, and a row that
/// serialised as nothing would be an operator's mapping silently lost on the next restart.
/// </para>
/// </summary>
public class BridgeAccountRow
{
    /// <summary>
    /// Gets or sets the person on this server, by the server's own user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the account on the external request service that person's requests arrive under,
    /// exactly as the operator wrote it.
    /// </summary>
    public string Account { get; set; } = string.Empty;
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.Requests.Bridge;

/// <summary>
/// The table an operator keeps of who is who on the external service, and the one rule for reading
/// it.
/// <para>
/// It is empty on a fresh install, which is the shipping answer rather than a state on the way to
/// one: an operator who configures a bridge and stops there has every request arrive over there
/// under the service's own account, and that service is told nothing about the people on this
/// server. Attribution is a thing they turn on per person, by writing a line, and the line says what
/// leaves.
/// </para>
/// <para>
/// <b>A person is found by their identifier and never by their name.</b> The lookup takes a user
/// identifier and there is no overload that takes anything else, so matching by name is not a
/// behaviour that can be switched on here; it is a shape this type does not have.
/// <c>NothingIsResolvedFromWhatAPersonIsCalled</c> is the guard on that. Two people with similar
/// names would otherwise be attributed each other's requests, and nobody finds out until one of them
/// sees something that is not theirs.
/// </para>
/// <para>
/// Where the table is kept, and how an operator edits it, arrives with the adapter that reads it.
/// There is no adapter in this tree and no issue on this board asks for one, so nothing on this side
/// reads the table yet and a settings field for it now would be somewhere to type something nothing
/// uses. <c>docs/bridge.md</c> carries the decision, the two shapes it was chosen over, what each of
/// them costs, and where the question of an adapter stands.
/// </para>
/// </summary>
public sealed class BackendAccounts
{
    private readonly Dictionary<Guid, string> _byUser;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackendAccounts"/> class.
    /// </summary>
    /// <param name="byUser">
    /// The account on the external service for each person the operator has mapped, by Jellyfin user
    /// identifier.
    /// </param>
    /// <exception cref="ArgumentNullException">Where no table was given.</exception>
    /// <exception cref="ArgumentException">
    /// Where a row names no user, or names an account that is nothing but space. Both are a row
    /// somebody meant to fill in, and honouring either would attribute a request to an account
    /// nobody chose.
    /// </exception>
    public BackendAccounts(IReadOnlyDictionary<Guid, string> byUser)
    {
        ArgumentNullException.ThrowIfNull(byUser);

        _byUser = new Dictionary<Guid, string>(byUser.Count);

        foreach (var row in byUser)
        {
            if (row.Key == Guid.Empty)
            {
                throw new ArgumentException(
                    "A row of the account mapping names no user. A row that names nobody would be an account every unmapped person is attributed to.",
                    nameof(byUser));
            }

            if (string.IsNullOrWhiteSpace(row.Value))
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "The account mapping has a row with no account on it, for user {0}. Removing the row is how somebody is left unmapped; a blank account is a row that was started and not finished.",
                        row.Key),
                    nameof(byUser));
            }

            _byUser[row.Key] = row.Value;
        }
    }

    /// <summary>
    /// Gets the table an install runs before an operator has written anything in it, which is what
    /// every install runs today.
    /// </summary>
    public static BackendAccounts Empty { get; } = new BackendAccounts(new Dictionary<Guid, string>());

    /// <summary>
    /// Gets how many people the operator has mapped.
    /// </summary>
    public int Count => _byUser.Count;

    /// <summary>
    /// Whose name a request from one person carries on the external service.
    /// </summary>
    /// <param name="user">The Jellyfin user who asked, by identifier.</param>
    /// <returns>
    /// The account the operator mapped them to, or <see cref="BackendAccount.TheServiceAccount"/>
    /// where they mapped nobody. The second is an answer rather than a failure: it is what every
    /// install gives until somebody writes a row.
    /// </returns>
    public BackendAccount For(Guid user)
        => _byUser.TryGetValue(user, out var account)
            ? BackendAccount.Named(account)
            : BackendAccount.TheServiceAccount;
}

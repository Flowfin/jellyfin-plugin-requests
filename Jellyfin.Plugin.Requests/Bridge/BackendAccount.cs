namespace Jellyfin.Plugin.Requests.Bridge;

/// <summary>
/// Whose name a request carries on the external service.
/// <para>
/// Two answers and no third. Either the operator mapped this person to an account over there, in
/// which case the submission carries the name the operator wrote, or they did not, in which case it
/// goes under the service's own account and that service is told nothing about who asked.
/// </para>
/// <para>
/// <b>Nothing on this record comes from the Jellyfin user.</b> The only string it can hold is one an
/// operator typed into the mapping, which is what keeps a Jellyfin display name out of a system
/// somebody never signed up to. <c>TheAccountCarriesNothingButWhatTheOperatorTyped</c> refuses a
/// field added later that would carry one.
/// </para>
/// </summary>
public sealed record BackendAccount
{
    /// <summary>
    /// Gets the account every submission goes under where the person who asked is not mapped to one.
    /// <para>
    /// It is a value rather than the absence of one. "Unmapped" is a case an operator can read in
    /// the documentation and predict, and a null handed around instead would be a fallback nobody
    /// chose reached by whatever each caller did with it.
    /// </para>
    /// </summary>
    public static BackendAccount TheServiceAccount { get; } = new BackendAccount();

    /// <summary>
    /// Gets the account on the external service, exactly as the operator wrote it in the mapping,
    /// or <see langword="null"/> where this person is not mapped and the service's own account is
    /// what carries the request.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets a value indicating whether the service is told who asked. Where it is false the request
    /// arrives over there under the service's own account, so that side knows a request was made and
    /// not by whom.
    /// </summary>
    public bool CarriesWhoAsked => Name is not null;

    /// <summary>
    /// One account on the external service.
    /// </summary>
    /// <param name="name">The account, as the operator wrote it.</param>
    /// <returns>The account.</returns>
    public static BackendAccount Named(string name) => new BackendAccount { Name = name };
}

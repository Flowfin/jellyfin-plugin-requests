namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// Where a request stands, as far as the people using this plugin are concerned. This says nothing
/// about whether the server holds the media: that is <see cref="LibraryAvailability"/>, and the two
/// are separate because an approved request that has not arrived yet is the ordinary case rather
/// than an edge one.
/// <para>
/// Which moves between these values are legal is <see cref="RequestLifecycle.Table"/>, which is
/// data rather than a set of conditionals. Adding a value here is an entry there, and the table has
/// a cell for every pair whether it is legal or not, so a new value cannot be added without saying
/// what may reach it and what it may reach.
/// </para>
/// </summary>
public enum RequestState
{
    /// <summary>
    /// Asked for, and nothing has been decided. The state a request is created in, and the only
    /// state nothing moves back to: undecided is a fact about a request nobody has looked at, and
    /// once somebody has looked, the honest record of that is the decision they made.
    /// </summary>
    Open = 0,

    /// <summary>
    /// An operator said yes. It does not follow that the server has the media, or ever will.
    /// </summary>
    Approved = 1,

    /// <summary>
    /// An operator said no. The reason is #41 and is required, decided on #113.
    /// </summary>
    Declined = 2,

    /// <summary>
    /// The thing that was asked for is in the library and the person who asked can watch it.
    /// </summary>
    Fulfilled = 3,

    /// <summary>
    /// Approved, sent onward, and it did not arrive. This exists because the alternative is a
    /// request that sits in <see cref="Approved"/> forever looking like an operator forgot, when
    /// what happened is that the thing doing the fetching gave up. Added on the decision recorded
    /// on #113; a cancelled value was considered there and refused, because a user withdrawing is
    /// a second road to "finished" that carries nothing an operator acts on differently.
    /// </summary>
    Failed = 4
}

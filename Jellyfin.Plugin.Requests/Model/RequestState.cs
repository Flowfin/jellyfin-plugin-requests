namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// Where a request stands, as far as the people using this plugin are concerned. This says nothing
/// about whether the server holds the media: that is <see cref="LibraryAvailability"/>, and the two
/// are separate because an approved request that has not arrived yet is the ordinary case rather
/// than an edge one.
/// <para>
/// Which moves between these values are legal is a table rather than a set of conditionals, and it
/// is #39. Whether four values are enough, or whether a cancelled or failed value is also needed,
/// is decision 3 on #113; adding one is an entry here and a row there.
/// </para>
/// </summary>
public enum RequestState
{
    /// <summary>
    /// Asked for, and nothing has been decided. The state a request is created in.
    /// </summary>
    Open = 0,

    /// <summary>
    /// An operator said yes. It does not follow that the server has the media, or ever will.
    /// </summary>
    Approved = 1,

    /// <summary>
    /// An operator said no. The reason, where there is one, is #41.
    /// </summary>
    Declined = 2,

    /// <summary>
    /// The thing that was asked for is in the library and the person who asked can watch it.
    /// </summary>
    Fulfilled = 3
}

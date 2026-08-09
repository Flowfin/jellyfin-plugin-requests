using System;

namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// Who is asking for a move, and with what authority. Every call into
/// <see cref="RequestLifecycle"/> carries one, so a new calling surface cannot make a move without
/// having said on whose behalf it is making it.
/// <para>
/// It is a parameter rather than something the lifecycle looks up, because the lifecycle has no way
/// to reach a session and should not grow one. The surface that has the session builds one of these
/// and hands it in; what a caller of a given authority may do is then decided in one place, beside
/// the table, and not in each surface.
/// </para>
/// <para>
/// It is a class with three named doors rather than a struct with fields, so that there is no
/// default value. A defaulted struct here would carry no user and no authority, and the shape that
/// carries no user is the plugin itself, which is the most privileged caller there is. A caller
/// that forgot to say who it was would have become the one caller that may make an observation.
/// </para>
/// <para>
/// The authority is taken as given. Whether the session really belongs to an administrator is the
/// server's answer and it is asked at the endpoint; handing in
/// <see cref="Administrator(System.Guid)"/> for a session that is not one is a defect in that
/// surface, and nothing here can detect it. The endpoint half of that is #51.
/// </para>
/// </summary>
public sealed record RequestCaller
{
    private readonly RequestActor _authority;

    private RequestCaller(Guid? userId, RequestActor authority)
    {
        UserId = userId;
        _authority = authority;
    }

    /// <summary>
    /// Gets the caller that is the plugin itself, moving a request on something it observed rather
    /// than on a decision anybody took. It carries no user, which is what makes
    /// <see cref="MediaRequest.StateChangedByUserId"/> and
    /// <see cref="RequestHistoryEntry.ByUserId"/> read as absent for a move no person made.
    /// </summary>
    public static RequestCaller Plugin { get; } = new(userId: null, RequestActor.Plugin);

    /// <summary>
    /// Gets the Jellyfin user this call is made on behalf of, or <see langword="null"/> where the
    /// plugin is making the call itself. It is the server's user identifier rather than a name,
    /// because names are renamed and because two similar names must never end up deciding each
    /// other's requests.
    /// </summary>
    public Guid? UserId { get; }

    /// <summary>
    /// A caller that is an administrator of this server.
    /// </summary>
    /// <param name="userId">The administrator's Jellyfin user identifier.</param>
    /// <returns>A caller holding <see cref="RequestActor.Administrator"/> on every request, and
    /// <see cref="RequestActor.Requester"/> as well on one they asked for themselves.</returns>
    public static RequestCaller Administrator(Guid userId) => new(userId, RequestActor.Administrator);

    /// <summary>
    /// A caller that is an ordinary user of this server.
    /// <para>
    /// This is also the door for an administrator who is to be treated as an ordinary user for one
    /// call, which is how a configuration that keeps administrators from deciding on their own
    /// requests would be expressed without the table changing.
    /// </para>
    /// </summary>
    /// <param name="userId">The user's Jellyfin user identifier.</param>
    /// <returns>A caller holding <see cref="RequestActor.Requester"/> on a request they asked for
    /// and nothing at all on anybody else's.</returns>
    public static RequestCaller User(Guid userId) => new(userId, RequestActor.None);

    /// <summary>
    /// What this caller is, on one particular request.
    /// </summary>
    /// <param name="request">The request being moved.</param>
    /// <returns>
    /// The authority this caller was built with, together with
    /// <see cref="RequestActor.Requester"/> where the request is one they asked for.
    /// </returns>
    /// <exception cref="ArgumentNullException">Where no request was given.</exception>
    public RequestActor RolesOn(MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The plugin carries no user, so it can never match a requester, and the comparison below
        // does not have to special-case it: no identifier equals a request's requester by accident.
        return UserId is Guid caller && caller == request.RequestedByUserId
            ? _authority | RequestActor.Requester
            : _authority;
    }
}

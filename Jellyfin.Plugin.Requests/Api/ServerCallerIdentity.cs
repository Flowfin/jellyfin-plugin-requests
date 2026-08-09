using System;
using System.Threading.Tasks;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// The server's own answer to who is calling, which is the only answer this plugin will accept.
/// <para>
/// It is the whole of the implementation behind <see cref="ICallerIdentity"/> and it decides
/// nothing: the token, the session and the user behind them are the server's, and asking it is the
/// point. A plugin that read an identity out of anything else would be a plugin that can be told who
/// is calling.
/// </para>
/// <para>
/// Nothing in the suite exercises this type, and it is one call for exactly that reason. The
/// server's authorisation context can only answer on a running server, which the headless rule
/// refuses, so what is testable is what the endpoint does with the answer and that is tested against
/// the interface. What is not tested is that this passes the call through, which is why there is
/// nothing else in it.
/// </para>
/// </summary>
public sealed class ServerCallerIdentity : ICallerIdentity
{
    private readonly IAuthorizationContext _authorization;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerCallerIdentity"/> class.
    /// </summary>
    /// <param name="authorization">The server's authorisation context.</param>
    public ServerCallerIdentity(IAuthorizationContext authorization) => _authorization = authorization;

    /// <inheritdoc />
    public async Task<Guid?> UserIdAsync(HttpContext? context)
    {
        if (context is null)
        {
            return null;
        }

        var info = await _authorization.GetAuthorizationInfo(context).ConfigureAwait(false);

        // The empty identifier is what the server reports for a call that carries no user, so it is
        // turned into an absence here rather than handed on as a value that looks like one.
        return info.UserId == Guid.Empty ? null : info.UserId;
    }
}

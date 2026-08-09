using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// Who the server says is calling, as one question with one answer.
/// <para>
/// It exists for the same reason the clock and the identifier source do: the server's own answer is
/// reachable only from a running server, and an endpoint that asks the server directly can only be
/// tested by starting one, which the headless rule in <c>docs/testing.md</c> refuses. This is the
/// seam, and the implementation behind it is one call.
/// </para>
/// <para>
/// It answers an identifier or nothing, and never a name. A call can authenticate and name no
/// person, which is what an API key looks like from an endpoint, and the two have to be
/// distinguishable: a request filed against nobody is worse than a request refused.
/// </para>
/// </summary>
public interface ICallerIdentity
{
    /// <summary>
    /// The Jellyfin user this call is made by.
    /// </summary>
    /// <param name="context">
    /// The call. It is nullable because the shape of the parameter should not promise more than the
    /// implementations need: the one that ships reads the call, and a caller with none has no
    /// identity, which is the same answer as a call that names nobody.
    /// </param>
    /// <returns>
    /// Their identifier, or <see langword="null"/> where the call names no person.
    /// </returns>
    Task<Guid?> UserIdAsync(HttpContext? context);
}

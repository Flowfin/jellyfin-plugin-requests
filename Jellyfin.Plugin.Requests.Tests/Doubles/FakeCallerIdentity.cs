using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// Who the server says is calling, decided by the test.
/// <para>
/// It ignores the call it is handed, and that is the whole reason the seam exists. The real
/// implementation reads a token off the request and asks the server to resolve it; there is no
/// server here to hold a session, and inventing a request carrying a token would be testing the
/// server's authentication rather than what this plugin does with its answer.
/// </para>
/// </summary>
internal sealed class FakeCallerIdentity : ICallerIdentity
{
    private readonly Guid? _userId;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeCallerIdentity"/> class.
    /// </summary>
    /// <param name="userId">
    /// Who the server says is calling, or <see langword="null"/> for a call that authenticated and
    /// names no person, which is what an API key looks like from an endpoint.
    /// </param>
    public FakeCallerIdentity(Guid? userId) => _userId = userId;

    /// <inheritdoc />
    public Task<Guid?> UserIdAsync(HttpContext? context) => Task.FromResult(_userId);
}

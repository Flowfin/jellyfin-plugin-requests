using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// An external request service that accepts what it is handed and gives it a name.
/// <para>
/// The two bridges the suite already has answer questions about reachability and hand nothing over,
/// which is the honest behaviour of a server with no service and useless for watching a submission.
/// This is the other half: it records every request it was given, in order, and answers with a
/// reference the way a service does.
/// </para>
/// <para>
/// The names it issues are its own and mean nothing here. They are sequential so a test can say
/// which submission it is looking at, and they are strings rather than numbers because the shape of
/// an identifier is the service's business.
/// </para>
/// </summary>
internal sealed class AServiceThatKeepsWhatItIsHanded : IRequestBackend
{
    /// <summary>
    /// What this service calls itself when it issues a reference.
    /// </summary>
    public const string Name = "a-service";

    private readonly List<MediaRequest> _handed = [];

    /// <summary>
    /// Gets every request handed to this service, in the order it arrived.
    /// </summary>
    public IReadOnlyList<MediaRequest> Handed => _handed;

    /// <inheritdoc />
    public Task<BackendReachability> CheckReachableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(BackendReachability.Reachable);
    }

    /// <inheritdoc />
    public Task<BackendReference?> SubmitAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _handed.Add(request);

        return Task.FromResult<BackendReference?>(new BackendReference
        {
            Service = Name,
            Id = string.Format(CultureInfo.InvariantCulture, "svc-{0}", _handed.Count)
        });
    }

    /// <inheritdoc />
    public Task<BackendReport?> ReportAsync(BackendReference reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<BackendReport?>(null);
    }

    /// <inheritdoc />
    public Task WithdrawAsync(BackendReference reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }
}

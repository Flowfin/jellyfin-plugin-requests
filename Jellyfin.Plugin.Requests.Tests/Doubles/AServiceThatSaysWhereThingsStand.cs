using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// An external request service that answers where the things it was handed stand.
/// <para>
/// The bridges the suite already has answer nothing when asked about a reference, which is the
/// honest behaviour of a server with no service and useless for watching a reconciliation. This is
/// the other half: a test says what the service reports for each reference it issued, and this
/// answers exactly that.
/// </para>
/// <para>
/// It records what it was asked about, in order, so a test can assert the shape of a run rather than
/// only its outcome: that a declined request was never asked about at all is a stronger claim than
/// that it did not move, because the second could be true by accident.
/// </para>
/// </summary>
internal sealed class AServiceThatSaysWhereThingsStand : IRequestBackend
{
    /// <summary>
    /// What this service calls itself when it issues a reference.
    /// </summary>
    public const string Name = "a-service";

    private readonly Dictionary<string, string?> _says;
    private readonly HashSet<string> _cannotAnswerAbout;
    private readonly List<string> _askedAbout = [];
    private readonly BackendReachability _reachability;
    private readonly bool _refusesTheCheck;

    /// <summary>
    /// Initializes a new instance of the <see cref="AServiceThatSaysWhereThingsStand"/> class.
    /// </summary>
    /// <param name="says">
    /// What it reports per reference identifier. A null value is a reference it knows nothing about,
    /// and a reference not in the dictionary at all is the same answer.
    /// </param>
    /// <param name="reachability">What it says when it is asked whether it is there.</param>
    /// <param name="cannotAnswerAbout">References asking about raises on rather than answers.</param>
    /// <param name="refusesTheCheck">Whether asking whether it is there raises rather than answers.</param>
    public AServiceThatSaysWhereThingsStand(
        IReadOnlyDictionary<string, string?>? says = null,
        BackendReachability reachability = BackendReachability.Reachable,
        IReadOnlyCollection<string>? cannotAnswerAbout = null,
        bool refusesTheCheck = false)
    {
        _says = says is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(says, StringComparer.Ordinal);
        _reachability = reachability;
        _cannotAnswerAbout = cannotAnswerAbout is null ? [] : [.. cannotAnswerAbout];
        _refusesTheCheck = refusesTheCheck;
    }

    /// <summary>
    /// Gets every reference this service was asked about, in the order it was asked.
    /// </summary>
    public IReadOnlyList<string> AskedAbout => _askedAbout;

    /// <summary>
    /// A reference this service would have issued.
    /// </summary>
    /// <param name="id">What it calls the request.</param>
    /// <returns>The reference.</returns>
    public static BackendReference Reference(string id) => new BackendReference { Service = Name, Id = id };

    /// <inheritdoc />
    public Task<BackendReachability> CheckReachableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _refusesTheCheck
            ? throw new InvalidOperationException("This service could not be asked whether it is there.")
            : Task.FromResult(_reachability);
    }

    /// <inheritdoc />
    public Task<BackendReference?> SubmitAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<BackendReference?>(null);
    }

    /// <inheritdoc />
    public Task<BackendReport?> ReportAsync(BackendReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        _askedAbout.Add(reference.Id);

        if (_cannotAnswerAbout.Contains(reference.Id))
        {
            throw new InvalidOperationException("This service could not be asked about that reference.");
        }

        return Task.FromResult(
            _says.TryGetValue(reference.Id, out var said) && said is not null
                ? new BackendReport { Reported = said }
                : null);
    }

    /// <inheritdoc />
    public Task WithdrawAsync(BackendReference reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }
}

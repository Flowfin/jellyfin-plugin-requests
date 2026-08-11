using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Fulfilment;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// A library the suite puts titles into, standing in for the server's.
/// <para>
/// It answers the same two questions <see cref="ILibrary"/> declares and nothing else, which is the
/// whole reason that interface exists: the server's own library interface is not the same interface
/// on the two server lines this plugin builds for, so a double for it would be two doubles proving
/// two different things.
/// </para>
/// <para>
/// A title is held under one provider identifier and matches a request that names it under any
/// spelling of the provider name, which is the rule the real lookup is written to.
/// </para>
/// </summary>
internal sealed class FakeLibrary : ILibrary
{
    private readonly Dictionary<(RequestedItemKind Kind, string Provider, string Value), List<int>> _held = [];

    /// <inheritdoc />
    public event EventHandler<LibraryChangeEventArgs>? Changed;

    /// <summary>
    /// Gets how many times the library has been asked what it holds. A sweep that writes nothing
    /// still looks, and a test about writes has to be able to tell the two apart.
    /// </summary>
    public int Lookups { get; private set; }

    /// <summary>
    /// Puts a title in the library.
    /// </summary>
    /// <param name="kind">What sort of thing it is.</param>
    /// <param name="provider">The provider naming it.</param>
    /// <param name="value">The identifier under that provider.</param>
    /// <param name="seasons">
    /// The seasons the server has files for, on a series. Empty on a film, and empty on a series
    /// with nothing under it yet.
    /// </param>
    public void Put(RequestedItemKind kind, string provider, string value, params int[] seasons)
        => _held[(kind, provider.ToUpperInvariant(), value)] = [.. seasons];

    /// <summary>
    /// Takes a title back out, which is what an operator deleting a file leaves behind.
    /// </summary>
    /// <param name="kind">What sort of thing it was.</param>
    /// <param name="provider">The provider naming it.</param>
    /// <param name="value">The identifier under that provider.</param>
    public void Remove(RequestedItemKind kind, string provider, string value)
        => _held.Remove((kind, provider.ToUpperInvariant(), value));

    /// <summary>
    /// Raises what the server raises when the library gains or loses something.
    /// </summary>
    /// <param name="kind">What sort of thing changed.</param>
    /// <param name="providerIds">The identifiers the item carries.</param>
    public void Raise(RequestedItemKind kind, IReadOnlyDictionary<string, string> providerIds)
        => Changed?.Invoke(this, new LibraryChangeEventArgs(kind, providerIds));

    /// <inheritdoc />
    public Task<LibraryHolding> HoldingOfAsync(
        RequestedItemKind kind,
        IReadOnlyDictionary<string, string> providerIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerIds);
        cancellationToken.ThrowIfCancellationRequested();

        Lookups++;

        var seasons = providerIds
            .Select(identifier => _held.TryGetValue(
                (kind, identifier.Key.ToUpperInvariant(), identifier.Value), out var found) ? found : null)
            .FirstOrDefault(found => found is not null);

        return Task.FromResult(seasons is null
            ? LibraryHolding.Nothing
            : new LibraryHolding { Held = true, SeasonsHeld = seasons });
    }
}

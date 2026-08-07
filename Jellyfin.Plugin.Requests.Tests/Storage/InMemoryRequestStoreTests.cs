using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;

namespace Jellyfin.Plugin.Requests.Tests.Storage;

/// <summary>
/// The conformance suite run against the one implementation that exists. It adds no assertion of
/// its own on purpose: an implementation is judged by the contract or it is judged by nothing, and
/// a test written here rather than in the base class would be a promise only this store keeps.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "The rule exempts a class that declares a test method and this one only inherits them, so it reads as an unused public type. xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public class InMemoryRequestStoreTests : RequestStoreContract
{
    /// <inheritdoc />
    protected override IRequestStore NewStore() => new InMemoryRequestStore();
}

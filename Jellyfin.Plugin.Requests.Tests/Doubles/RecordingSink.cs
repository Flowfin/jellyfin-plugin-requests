using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Notify;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// An outbound sink that keeps what was announced to it instead of sending it anywhere.
/// <para>
/// It is the double for every test whose subject is a path rather than the sending, which is most of
/// them: what a test of an endpoint or of the sweep can assert is which movements reached the sink
/// and what document each one carried. Whether a document actually leaves the machine is
/// <c>OutboundSink</c>'s own, tested against the handler double in <c>ASinkEndpoint</c>.
/// </para>
/// <para>
/// <b>It announces everything it is given and drops nothing.</b> Which events an install wants is
/// the real sink's decision, read from the settings, and a double that repeated that rule would let
/// the rule pass a test while being absent from the thing that ships.
/// </para>
/// </summary>
internal sealed class RecordingSink : IOutboundSink
{
    private readonly List<OutboundNotice> _announced = [];

    /// <summary>
    /// Gets every notice announced, in the order it was announced.
    /// </summary>
    public IReadOnlyList<OutboundNotice> Announced => _announced;

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    public void Announce(OutboundNotice notice) => _announced.Add(notice);

    /// <inheritdoc />
    public Task QuietAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

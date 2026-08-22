using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Notify;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// A path to the administrators that keeps what it was told instead of pushing it anywhere.
/// <para>
/// It is the double for every test whose subject is a path rather than the pushing, which is most of
/// them: what a test of an endpoint or of the seam can assert is how many arrivals were announced
/// and what each document said. Whether a message actually reaches a client is
/// <c>ServerArrivalNotice</c>'s own, tested against the session manager double in
/// <see cref="ASessionManagerThatOnlyDelivers"/>.
/// </para>
/// <para>
/// <b>It keeps everything it is given and drops nothing, and it reads no setting.</b> Whether an
/// install says anything at all is <c>ServerArrivalNotice</c>'s decision, and a double that repeated
/// that rule would let the rule pass a test while being absent from the thing that ships.
/// </para>
/// </summary>
internal sealed class RecordingArrivalNotice : IArrivalNotice
{
    private readonly List<OutboundNotice> _told = [];

    /// <summary>
    /// Gets every document, in the order it was given.
    /// </summary>
    public IReadOnlyList<OutboundNotice> Told => _told;

    /// <inheritdoc />
    public void Tell(OutboundNotice notice) => _told.Add(notice);

    /// <inheritdoc />
    public Task QuietAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

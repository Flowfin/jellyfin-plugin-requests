using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Notify;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// A path to the requester that keeps what it was told instead of pushing it anywhere.
/// <para>
/// It is the double for every test whose subject is a path rather than the pushing, which is most of
/// them: what a test of an endpoint or of the sweep can assert is who was told, how many times, and
/// what the message said. Whether a message actually reaches a client is
/// <c>ServerRequesterNotice</c>'s own, tested against the session manager double in
/// <see cref="ASessionManagerThatOnlyDelivers"/>.
/// </para>
/// <para>
/// <b>It keeps everything it is given and drops nothing.</b> Which movements produce a message is
/// <c>RequesterMessage.ForMove</c>'s decision, and a double that repeated that rule would let the
/// rule pass a test while being absent from the thing that ships.
/// </para>
/// </summary>
internal sealed class RecordingRequesterNotice : IRequesterNotice
{
    private readonly List<RequesterMessage> _told = [];

    /// <summary>
    /// Gets every message, in the order it was given.
    /// </summary>
    public IReadOnlyList<RequesterMessage> Told => _told;

    /// <inheritdoc />
    public void Tell(RequesterMessage message) => _told.Add(message);

    /// <inheritdoc />
    public Task QuietAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

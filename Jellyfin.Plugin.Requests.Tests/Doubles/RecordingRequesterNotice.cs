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
/// <para>
/// <b>Several threads may tell it at once.</b> <c>QuietedRequesterNotice</c> decides each message on
/// a task of its own and calls the path underneath from whichever one finishes, so this is written
/// to from several threads with nothing above it serialising them. An unguarded
/// <see cref="List{T}"/> under that traffic does not merely reorder: an append racing another can
/// lose an entry, keep one twice or leave a hole, so a test asserting who was told could fail for a
/// defect in this file rather than in its subject. The whole of what the lock buys is that every
/// message given to this double is kept exactly once.
/// </para>
/// <para>
/// <b>It promises no order across threads, because nothing above it does.</b>
/// <see cref="IRequesterNotice.Tell"/> hands back nothing and starts no ordering, so the sequence
/// two concurrently decided messages arrive here in is not decided by anything and is not a fact a
/// test may rest on. Where the messages were handed over one after another by one thread, the order
/// kept here is that order, which is what the legs asserting a sequence over a synchronous caller
/// read.
/// </para>
/// </summary>
internal sealed class RecordingRequesterNotice : IRequesterNotice
{
    private readonly object _gate = new object();
    private readonly List<RequesterMessage> _told = [];

    /// <summary>
    /// Gets every message, in the order it arrived here, as a copy taken under the lock. A caller
    /// reading this while something is still in flight gets a whole answer of some moment rather
    /// than a list changing under its own enumeration.
    /// </summary>
    public IReadOnlyList<RequesterMessage> Told
    {
        get
        {
            lock (_gate)
            {
                return _told.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public void Tell(RequesterMessage message)
    {
        lock (_gate)
        {
            _told.Add(message);
        }
    }

    /// <inheritdoc />
    public Task QuietAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

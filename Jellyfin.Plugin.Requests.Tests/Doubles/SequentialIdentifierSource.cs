using System;
using System.Globalization;
using System.Threading;
using Jellyfin.Plugin.Requests.Identity;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// An identifier source the suite can predict. The nth identifier it hands out is the same value on
/// every run and on every machine, so a test can assert which request it is looking at instead of
/// reading back whatever the code produced and comparing it to itself.
/// <para>
/// The values are real, distinct <see cref="Guid"/>s and they are not
/// <see cref="Guid.Empty"/>, because code that treats the empty identifier as "none" would
/// otherwise be tested against the one value it is entitled to reject.
/// </para>
/// <para>
/// The counter is incremented atomically, so two threads asking at once get two identifiers rather
/// than one value twice. The store contract admits concurrent callers, and a double that cannot be
/// used from two of them would decide what the suite is allowed to test.
/// </para>
/// </summary>
internal sealed class SequentialIdentifierSource : IIdentifierSource
{
    private int issued;

    /// <summary>
    /// Gets how many identifiers have been handed out.
    /// </summary>
    public int Issued => Volatile.Read(ref issued);

    /// <summary>
    /// The identifier this source hands out at a given position, without asking it for one.
    /// A test asserts against this rather than against a literal, so the shape lives in one place.
    /// </summary>
    /// <param name="position">Which one, counting from one.</param>
    /// <returns>The identifier the nth call to <see cref="NewId"/> returns.</returns>
    public static Guid At(int position)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, 1);

        return Guid.ParseExact(
            position.ToString("D32", CultureInfo.InvariantCulture),
            "N");
    }

    /// <inheritdoc />
    public Guid NewId() => At(Interlocked.Increment(ref issued));
}

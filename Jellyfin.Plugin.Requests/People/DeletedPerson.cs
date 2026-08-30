using System;

namespace Jellyfin.Plugin.Requests.People;

/// <summary>
/// The identifier a request carries where the person who asked for it has been deleted.
/// <para>
/// <b>One fixed constant, and deliberately not a derivation.</b> A pseudonym computed from the
/// identifier that was there, however it is computed, is the same person written down differently:
/// two records carrying it say the same account asked for both, and anybody holding the original
/// identifier can confirm a match by running the computation. This value carries no information
/// about who was replaced, so the only thing it says is that somebody who is gone asked, which is
/// exactly what the decision of 2026-08-28 on #49 asks a surviving record to say.
/// </para>
/// <para>
/// <b>It is not any account's identifier.</b> Jellyfin mints a user identifier as a version 4
/// value, and this is not one: its version nibble is zero, which no minted identifier has. That is
/// asserted rather than argued, in <c>TheTombstoneIsNotAnIdentifierAServerCouldMint</c>.
/// </para>
/// <para>
/// <b>What it does to a surface is nothing new.</b> The queue page draws a name for an identifier
/// out of the user list the dashboard already reads, and shows an identifier that list does not
/// hold as a person this server no longer has. The tombstone is such an identifier, so a
/// tombstoned request reads as asked for by somebody who is gone without any page being taught a
/// second rule.
/// </para>
/// </summary>
public static class DeletedPerson
{
    /// <summary>
    /// Gets the identifier that stands in a request for a person the server has deleted.
    /// </summary>
    public static Guid Tombstone { get; } = new Guid("00000000-0000-0000-0000-000000000049");

    /// <summary>
    /// Whether an identifier is the tombstone rather than somebody.
    /// </summary>
    /// <param name="userId">The identifier a record carries.</param>
    /// <returns><see langword="true"/> where it stands for a deleted person.</returns>
    public static bool Is(Guid userId) => userId == Tombstone;
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Seam;

/// <summary>
/// One want, as it arrives from the sibling discover plugin.
/// <para>
/// <b>The field set is the contract's and this type is not a second copy of it.</b> What crosses is
/// fixed on the sibling's board, in the contract issue <c>docs/seam.md</c> points at, and this is
/// that field set expressed in the only way an in-process call can express one. Nothing is added
/// here that the contract does not carry: a field this side invented would be a field the other side
/// never fills, and it would read to somebody implementing against this type as part of the
/// agreement.
/// </para>
/// <para>
/// <b>It is not a request.</b> A want is what somebody expressed on a browsing surface this plugin
/// does not own; a request is what this plugin makes of it, with an identifier of its own, a state,
/// a history, and a place in a queue. Keeping the two types apart is what stops the contract's shape
/// leaking into the model, and it is why the fields here are named the way the contract names them
/// rather than the way <see cref="MediaRequest"/> does.
/// </para>
/// <para>
/// <b>Nothing here is stored as it stands.</b> The title and the year become the display snapshot
/// this plugin keeps and never refreshes, which is what lets the queue render with no metadata
/// source reachable. No image crosses and none is fetched.
/// </para>
/// </summary>
public sealed record HandedOverWant
{
    private readonly IReadOnlyDictionary<string, string> _providerIds = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>
    /// Gets which version of the contract this field set was built against.
    /// <para>
    /// Required, and read before anything else in the record. What happens where this side does not
    /// know the version is <see cref="WantHandover.KnownContractVersion"/> and the rule is in
    /// <c>docs/seam.md</c>.
    /// </para>
    /// </summary>
    public required int ContractVersion { get; init; }

    /// <summary>
    /// Gets the sibling's own identifier for this want, which is stable across a refresh that
    /// recreated the item and across a restart.
    /// <para>
    /// It is stored on the request it becomes, which is what makes a repeat recognisable as one
    /// whatever else changed and across a restart, and it is carried through to the log line a
    /// refusal leaves, so an operator asked about a want by the other side's identifier can find
    /// what this side did with it.
    /// </para>
    /// </summary>
    public required Guid WantId { get; init; }

    /// <summary>
    /// Gets the user the sibling says asked for this.
    /// <para>
    /// This side cannot verify it. There is no session on a call from another plugin in the same
    /// process, so what stands behind this field is the sibling's own permission check; what that
    /// means and what this side checks anyway is #118.
    /// </para>
    /// </summary>
    public required Guid RequestedByUserId { get; init; }

    /// <summary>
    /// Gets what sort of thing was wanted.
    /// </summary>
    public required RequestedItemKind Kind { get; init; }

    /// <summary>
    /// Gets the title as it read on the browsing surface at the moment somebody asked.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the release year, where the sibling had one.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Gets the external identifiers naming the thing that was wanted.
    /// <para>
    /// These are what the identity rule compares, so a want carrying none reaches no existing
    /// request and is joined by none: it has no identity, which makes it different from everything
    /// including another copy of itself. That is <see cref="RequestIdentity"/>'s answer rather than
    /// anything this seam decides.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderIds
    {
        get => _providerIds;
        init => _providerIds = value ?? ReadOnlyDictionary<string, string>.Empty;
    }

    /// <summary>
    /// Gets <see langword="true"/> where the other side is replaying a want it recorded before this
    /// plugin was installed, and nothing where somebody is expressing one now.
    /// <para>
    /// <b>Absence is live, and it is the value every older build sends.</b> The marker says the
    /// unusual thing rather than the ordinary one, so a sibling built before the field existed hands
    /// wants over without it and each of them reads as what it is. That is the contract's own
    /// spelling and not a reading invented here.
    /// </para>
    /// <para>
    /// <b><see langword="false"/> is read as live rather than refused.</b> The sending type refuses
    /// it, and the reason it gives is that a false and an absence are the same want; a receiver that
    /// then threw the want away over the redundant spelling would cost somebody their request to
    /// make a point the sender has already conceded. So this side treats anything that is not
    /// <see langword="true"/> as a want being expressed now.
    /// </para>
    /// <para>
    /// <b>A field a receiver may ignore does not move the contract version.</b> This one arrived at
    /// version one on the sibling's board rather than minting a second, which is that board's rule
    /// applied on that board; <see cref="WantHandover.KnownContractVersion"/> is unchanged for it
    /// and a want carrying the marker is read by the same version this side already knew.
    /// </para>
    /// </summary>
    public bool? Replay { get; init; }
}

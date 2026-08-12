using System.Collections.Generic;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// What a decline of several requests carries: the requests, and the one reason the whole action is
/// made for.
/// <para>
/// <b>One reason for the action rather than one per request.</b> The gesture this exists for is an
/// operator who has come back to forty requests and is answering a batch of them the same way. A
/// reason per row would be a form rather than a batch, and the operator who wants to say something
/// different about one request makes that one decision on its own, where the single endpoint already
/// carries a reason and a note of its own.
/// </para>
/// <para>
/// The consequence is worth stating rather than discovering: every person whose request is in this
/// action is told the same thing. An operator declining twenty requests for one reason is saying one
/// thing twenty times, which is what they meant, and an operator who wanted to say twenty things
/// makes twenty decisions.
/// </para>
/// </summary>
public sealed record DeclineManyBody
{
    /// <summary>
    /// Gets the requests being declined, in the order the caller chose them. Nullable for the reason
    /// it is on an approval of several: a body with no list is refused rather than read as an action
    /// on nothing.
    /// </summary>
    public IReadOnlyList<RequestToDecide>? Requests { get; init; }

    /// <summary>
    /// Gets why the requests are being declined. Required, and the same rule as on the single
    /// decline: a decline with no reason reads as arbitrary to the person who asked, and what they
    /// do next is ask for the same title again.
    /// </summary>
    public DeclineReason? Reason { get; init; }

    /// <summary>
    /// Gets what the operator wants to say about it, which every request in the action carries.
    /// Required beside <see cref="DeclineReason.Other"/> and optional beside every other reason,
    /// again as on the single decline.
    /// </summary>
    public string? Note { get; init; }
}

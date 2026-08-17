using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// Whether this plugin is working, for the operator whose requests have stopped moving.
/// <para>
/// The question underneath is always the same one: is the plugin broken, or is there simply nothing
/// to do. Without an answer on the page the next step is the server log, which is the second system
/// this plugin exists to remove. Every field here is one of the few facts that separate those two.
/// </para>
/// <para>
/// <b>Nothing here is a credential, a path or anything about a person.</b> It is counts, moments and
/// one enumeration, which is checkable over the shape rather than by reading it: there is no field a
/// secret or a file name could arrive in. The bridge is reported as reachable, unreachable or not
/// configured, and never as an address.
/// </para>
/// <para>
/// <b>Every moment here is about this process rather than about the install.</b> Nothing is
/// persisted, so a server restarted a minute ago answers that it has swept nothing and written
/// nothing, which is true of the process and is not a claim that the plugin has never done either.
/// What draws it says so in those words.
/// </para>
/// </summary>
public sealed record PluginHealth
{
    /// <summary>
    /// Gets how many requests are in each state, with a zero for every state nothing is in.
    /// <para>
    /// The states with nothing in them are here rather than left out, because a page that drew only
    /// what the store answered would show an operator a shorter list on a quieter server and give
    /// them nothing to compare against.
    /// </para>
    /// </summary>
    public required IReadOnlyDictionary<RequestState, int> Counts { get; init; }

    /// <summary>
    /// Gets a value indicating whether the store answered at all.
    /// <para>
    /// This is the one fault that makes every other number on the page meaningless, so it is a field
    /// rather than a refusal. An endpoint that answered 503 because the store is unreadable would
    /// take the health panel down at exactly the moment it is the thing being read.
    /// </para>
    /// </summary>
    public required bool StoreReadable { get; init; }

    /// <summary>
    /// Gets when the store last accepted a write in this process, or <see langword="null"/> where it
    /// has accepted none.
    /// </summary>
    public DateTimeOffset? LastStoreWriteAt { get; init; }

    /// <summary>
    /// Gets when the fulfilment sweep last finished a full run in this process.
    /// </summary>
    public DateTimeOffset? LastSweepAt { get; init; }

    /// <summary>
    /// Gets how many requests that run looked at.
    /// </summary>
    public int? LastSweepExamined { get; init; }

    /// <summary>
    /// Gets how many of them it moved to fulfilled.
    /// </summary>
    public int? LastSweepFulfilled { get; init; }

    /// <summary>
    /// Gets whether an external request service is configured and whether it answered when it was
    /// asked for this answer.
    /// <para>
    /// Asked at the moment this is read rather than remembered, because a bridge that was answering
    /// five minutes ago is not the question an operator is asking when they open this page.
    /// </para>
    /// </summary>
    public required BackendReachability Bridge { get; init; }

    /// <summary>
    /// Gets when the bridge was last seen answering in this process, or <see langword="null"/> where
    /// nothing has seen it answer.
    /// <para>
    /// It advances when something asks, and the thing that asks is this endpoint, so on an install
    /// where nobody opens this page it stays where it was. That is the honest bound and it is worth
    /// knowing before somebody reads a stale moment as an outage that started then: what it says is
    /// when this plugin last had evidence, not when the other system last worked.
    /// </para>
    /// </summary>
    public DateTimeOffset? BridgeLastReachableAt { get; init; }
}

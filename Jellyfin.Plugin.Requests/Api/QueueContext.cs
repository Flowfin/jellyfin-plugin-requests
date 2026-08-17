using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// The two things an operator needs beside a row that are not on the request itself: what was
/// decided about the same work before, and how much the person asking is already waiting for.
/// <para>
/// Both are named in <c>docs/queue.md</c> as items a decision is worse without, and both were the
/// items the queue answer could not carry. They are worked out from the whole set rather than read
/// off the request, which is why they are a separate shape: a request knows nothing about its
/// neighbours, and putting these on <see cref="Model.MediaRequest"/> would put a fact about the
/// store on a record the store holds.
/// </para>
/// <para>
/// It is answered on the queue read and nowhere else. A single request handed back after a move
/// carries <see langword="null"/> here rather than an empty context, because "nothing was decided
/// about this before" and "this route did not look" are different statements and a page that read
/// them as one would tell an operator a title is new when nobody asked.
/// </para>
/// </summary>
public sealed record QueueContext
{
    /// <summary>
    /// Gets what was already decided about the same work, most recent first.
    /// <para>
    /// Bounded by the store, which <see cref="Storage.RetentionSweep"/> now bounds in turn: a
    /// finished request is removed once it has been finished for longer than this install keeps
    /// them, so this is every decision inside that period rather than every decision ever made about
    /// that work.
    /// </para>
    /// </summary>
    public IReadOnlyList<EarlierDecision> EarlierDecisions { get; init; } = [];

    /// <summary>
    /// Gets how many requests the person who asked for this one is waiting for, counted the way the
    /// quota counts them.
    /// <para>
    /// This row is one of them. An operator looking at it is looking at one of the things that
    /// number counts, and subtracting it would make the column disagree with the limit the same
    /// person is refused against in <see cref="Intake.RequestIntake"/>.
    /// </para>
    /// </summary>
    public required int OpenRequestsByRequester { get; init; }

    /// <summary>
    /// Works out the context for each of the requests on a page, against everything the store holds.
    /// <para>
    /// One walk per fact rather than one per row. The open counts are built once for every person in
    /// the store, and the decisions are gathered once per work on the page, so a page of fifty rows
    /// does not read the queue fifty times.
    /// </para>
    /// <para>
    /// <b>Both arguments come from one read.</b> A page taken from one snapshot and a context taken
    /// from another can disagree, and the disagreement is the kind nobody reports: a count beside a
    /// row that is one out, or an earlier decision on a request the page no longer shows. The caller
    /// is what holds that, and <see cref="RequestsController"/> pages the same list it hands here.
    /// </para>
    /// </summary>
    /// <param name="rows">The requests on the page being answered.</param>
    /// <param name="everything">Every request the store holds, from the same read as the page.</param>
    /// <returns>The context for each row, by the request's identifier.</returns>
    /// <exception cref="ArgumentNullException">Where either set is missing.</exception>
    public static IReadOnlyDictionary<Guid, QueueContext> For(
        IEnumerable<StoredRequest> rows,
        IEnumerable<StoredRequest> everything)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(everything);

        var held = everything.ToArray();
        var waitingFor = OpenCounts(held);
        var answered = new Dictionary<Guid, QueueContext>();

        foreach (var row in rows)
        {
            answered[row.Request.Id] = new QueueContext
            {
                EarlierDecisions = DecidedBefore(row.Request, held),
                OpenRequestsByRequester = waitingFor.TryGetValue(row.Request.RequestedByUserId, out var counted)
                    ? counted
                    : 0
            };
        }

        return answered;
    }

    /// <summary>
    /// How many open or approved requests each person in the store is waiting for.
    /// <para>
    /// Somebody who joined a request is waiting for it exactly as the person who asked first is,
    /// which is what <see cref="Storage.IRequestStore.FindForUserAsync"/> answers and what the quota
    /// is measured in. Counting only the first asker would show an operator a small number for
    /// somebody who has joined ten things.
    /// </para>
    /// </summary>
    /// <param name="held">Every request the store holds.</param>
    /// <returns>The count per person, with nobody who is waiting for nothing in it.</returns>
    private static Dictionary<Guid, int> OpenCounts(IReadOnlyList<StoredRequest> held)
    {
        var counts = new Dictionary<Guid, int>();

        foreach (var stored in held)
        {
            if (!RequestQuota.CountsAgainstIt(stored.Request))
            {
                continue;
            }

            // Distinct for the reason the store's own index is: the record says the asker is not
            // among the joiners and nothing in the type refuses one that carries them twice.
            foreach (var person in stored.Request.JoinedByUserIds
                .Append(stored.Request.RequestedByUserId)
                .Distinct())
            {
                counts[person] = counts.TryGetValue(person, out var so_far) ? so_far + 1 : 1;
            }
        }

        return counts;
    }

    /// <summary>
    /// Every decision already made about the same work as this request, most recent first.
    /// <para>
    /// <b>The work rather than the ask.</b> Two requests are compared with
    /// <see cref="RequestIdentity.NameTheSameWork"/>, which is the kind and one shared provider
    /// identifier, and deliberately not with <see cref="RequestIdentity.Compare"/>. That comparison
    /// answers whether a new asker joins an existing request, and it calls a request for season five
    /// different from a request for seasons one and two. For this column those two are the same
    /// series and the earlier answer is exactly what the operator wants in front of them; the
    /// seasons are carried on the decision so they can see what it covered.
    /// </para>
    /// <para>
    /// <b>Only what was answered.</b> Open and approved requests are left out. An open one is
    /// nothing decided, and an approved one for the same work would have been joined rather than
    /// made a second time, so a row for it is a state to look at rather than a decision to weigh.
    /// </para>
    /// </summary>
    /// <param name="request">The request being decided now.</param>
    /// <param name="held">Every request the store holds.</param>
    /// <returns>The decisions, newest first, and empty where there are none.</returns>
    private static IReadOnlyList<EarlierDecision> DecidedBefore(
        MediaRequest request,
        IReadOnlyList<StoredRequest> held)
    {
        var found = new List<EarlierDecision>();

        foreach (var stored in held)
        {
            var other = stored.Request;

            if (other.Id == request.Id
                || other.State is RequestState.Open or RequestState.Approved
                || !RequestIdentity.NameTheSameWork(other, request))
            {
                continue;
            }

            found.Add(new EarlierDecision
            {
                Id = other.Id,
                State = other.State,
                DecidedAt = other.StateChangedAt,
                Seasons = other.Seasons,
                DeclineReason = other.DeclineReason,
                DeclineNote = other.DeclineNote
            });
        }

        // Newest first, and the identifier as the last key for the reason the queue's own order
        // carries one: two decisions made in the same tick would otherwise sit in whatever order the
        // set was walked in, and that order changes on every write.
        return [.. found.OrderByDescending(decision => decision.DecidedAt).ThenBy(decision => decision.Id)];
    }
}

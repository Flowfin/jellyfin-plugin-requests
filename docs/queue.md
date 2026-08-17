# What the queue must show for a decision to be possible

This plugin exists so that answering a request does not need a second system. That is a claim about
one page, and this document is what the claim means: the things an operator has to be able to read
before approving or declining, each with the reason a decision is worse without it.

It is written before the rows are built, so the page is measured against a list somebody argued for
rather than against whatever fitted in the width. The rows themselves are #60, acting on them is #61
and #62, and the health panel beside them is #63. None of those decide what is on this list.

## What earns a place

An item is here because a decision is wrong or arbitrary without it, not because it is available. A
queue that shows everything the store holds is as unusable as one that shows a title and a name, and
the failure is the same in both directions: the operator goes somewhere else to find the answer.

Each item below says what it is, why the decision needs it, and where it comes from in this tree
today. The last part is what makes the list checkable rather than aspirational. An item nothing here
can answer is work that has to be named, and two of the six were exactly that until the answer was
widened to carry them.

The measurements are read at `11aa09a`, which is the commit that widened it and is the parent of the
one carrying this text. Every command below was re-run at that commit rather than carried over from
the reading the list was first written at.

## Who asked, and when

An operator does not decide a request, they decide a request from a person. The same title asked for
by somebody who asks once a month and by somebody who asked for four other things yesterday is two
different decisions, and the date is what separates a request that has been waiting from one that
arrived while the page was open.

The queue answer carries the identifier and both timestamps:

    git grep -n "RequestedByUserId\|RequestedAt\|StateChangedAt" -- Jellyfin.Plugin.Requests/Api/QueuedRequest.cs
    Jellyfin.Plugin.Requests/Api/QueuedRequest.cs:45:    public required Guid RequestedByUserId { get; init; }
    Jellyfin.Plugin.Requests/Api/QueuedRequest.cs:85:    public required DateTimeOffset RequestedAt { get; init; }
    Jellyfin.Plugin.Requests/Api/QueuedRequest.cs:90:    public required DateTimeOffset StateChangedAt { get; init; }

What it carries is an identifier and not a name. Turning one into the other is the server's own user
list, which the dashboard page reaches without leaving the server, so the name is a rendering
question rather than a second system. This plugin holds no copy of anybody's name and nothing here
asks it to start.

The page does that turning:

    git grep -n "people\[request.RequestedByUserId\]" -- Jellyfin.Plugin.Requests/Web/queue.html
    Jellyfin.Plugin.Requests/Web/queue.html:430:                        return people[request.RequestedByUserId] || request.RequestedByUserId;

The list behind `people` is asked for once when the page opens:

    git grep -n "getUsers" -- Jellyfin.Plugin.Requests/Web/
    Jellyfin.Plugin.Requests/Web/queue.html:651:                        return ApiClient.getUsers()

A list that cannot be read leaves the identifier in the cell, which is worse to read rather than
wrong, and it leaves the queue readable. This is the only call either page makes that is not to this
plugin's own API, and it is on the queue alone: the page a person opens for their own requests has no
business asking this server who everybody is.

The page is administrators only, which is where a name belongs and stops. What a user may learn
about another user's request is `docs/api.md`, and it is nothing.

## What was asked for, including which seasons

Approving a series without knowing whether it is one season or nine is approving an unknown quantity
of somebody's disk. The kind, the title, the year and the seasons are the request, and the note the
requester wrote is the only place they get to say why.

    git grep -n "Kind\|DisplayTitle\|DisplayYear\|Seasons\|RequesterNote" -- Jellyfin.Plugin.Requests/Api/QueuedRequest.cs
    Jellyfin.Plugin.Requests/Api/QueuedRequest.cs:55:    public required RequestedItemKind Kind { get; init; }
    Jellyfin.Plugin.Requests/Api/QueuedRequest.cs:60:    public required string DisplayTitle { get; init; }
    Jellyfin.Plugin.Requests/Api/QueuedRequest.cs:65:    public int? DisplayYear { get; init; }
    Jellyfin.Plugin.Requests/Api/QueuedRequest.cs:75:    public IReadOnlyList<int> Seasons { get; init; } = [];
    Jellyfin.Plugin.Requests/Api/QueuedRequest.cs:100:    public string? RequesterNote { get; init; }

The year is what stops two films with one title being one decision. The provider identifiers are on
the same answer and are not for the operator to read: they are what the fulfilment check matches on,
and a page that printed them would be showing plumbing.

There is no poster, no synopsis and no rating here, and there will not be. This plugin makes no call
to a metadata source, decided in #92, so what the page can show about a title is the snapshot the
request carried when it was made. An operator who wants more than that is looking something up, and
this document is not going to pretend otherwise by leaving the limit out.

## Whether the server already holds it, in whole or in part

The commonest correct answer to a request is that the thing is already on the server and the person
did not find it. A queue that does not say so sends the operator into their own library to look,
which is the trip this plugin is meant to remove, and the answer is worth more for a series than for
a film because half of one is a real state.

    git grep -n "Availability" -- Jellyfin.Plugin.Requests/Api/QueuedRequest.cs
    Jellyfin.Plugin.Requests/Api/QueuedRequest.cs:115:    public required LibraryAvailability Availability { get; init; }
    Jellyfin.Plugin.Requests/Api/QueuedRequest.cs:120:    public DateTimeOffset? AvailabilityCheckedAt { get; init; }

`LibraryAvailability` has four values and `Partial` is the one this item exists for. `Unknown` is a
fourth answer rather than a missing one, and the page has to show it as what it is: nothing has
looked yet. The time of the check is on the answer for the same reason, because an availability read
a week ago and one read a minute ago are different facts and a page that renders them identically is
telling the operator something it does not know.

## Whether the same title has been asked for before, and what was decided

This is the item an operator most notices the absence of, and it was the first of two on this list
the queue answer could not carry.

A title that was declined once and is asked for again is the case where the first decision matters
most. Without it in front of them the operator either declines from memory, which fails the first
time it is somebody else's memory, or approves something that was refused for a reason that has not
changed. The reason is in the store: a decline carries one, and a finished request is kept.

    git grep -n "FinishedRequestRetentionDays" -- Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:99:    public int FinishedRequestRetentionDays { get; set; } = 365;

`JoinedByUserIds` on the queue answer is not this: it is the several people standing behind one
request that is still open, decided in #38, and it says nothing about a request that was finished
before this one was made. What answers it is a shape of its own on the row:

    git grep -n "get; init;" -- Jellyfin.Plugin.Requests/Api/EarlierDecision.cs
    Jellyfin.Plugin.Requests/Api/EarlierDecision.cs:28:    public required Guid Id { get; init; }
    Jellyfin.Plugin.Requests/Api/EarlierDecision.cs:33:    public required RequestState State { get; init; }
    Jellyfin.Plugin.Requests/Api/EarlierDecision.cs:38:    public required DateTimeOffset DecidedAt { get; init; }
    Jellyfin.Plugin.Requests/Api/EarlierDecision.cs:44:    public IReadOnlyList<int> Seasons { get; init; } = [];
    Jellyfin.Plugin.Requests/Api/EarlierDecision.cs:49:    public DeclineReason? DeclineReason { get; init; }
    Jellyfin.Plugin.Requests/Api/EarlierDecision.cs:54:    public string? DeclineNote { get; init; }

The seasons are there because a series decision is not one answer. Declining seasons one and two says
nothing certain about season five, and a row reading `Declined` against a series would otherwise be
taken as covering the show.

Only what somebody answered is here. An open request for the same title is nothing decided, and an
approved one would have been joined rather than made a second time, so neither is shown as a decision
anybody made.

**Two things this deliberately does not carry.** It never says who asked for the earlier one or who
decided it: the operator can read that on the request itself, and repeating it here would put a
person's name beside a title they did not ask for this time. And it is bounded by nothing but the
store. Enforcing the retention period is #49 and is not built, so this is every decision ever made
about that work rather than the ones inside the period an operator configured:

    git grep -n "FinishedRequestRetentionDays" -- Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:99:    public int FinishedRequestRetentionDays { get; set; } = 365;

## Whether this person is asking for a lot

Requests are approved one at a time and a disk fills up all at once. An operator looking at a single
row cannot see that it is the eleventh from the same person this week, and the ceiling that exists
is a number in the configuration rather than something the queue shows:

    git grep -n "OpenRequestsPerUser" -- Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:67:    public int OpenRequestsPerUser { get; set; } = 10;

The store can count a person's requests, in `IRequestStore.FindForUserAsync`, so the fact was
available and was not on the queue answer. It is now, beside the decisions:

    git grep -n "get; init;" -- Jellyfin.Plugin.Requests/Api/QueueContext.cs
    Jellyfin.Plugin.Requests/Api/QueueContext.cs:36:    public IReadOnlyList<EarlierDecision> EarlierDecisions { get; init; } = [];
    Jellyfin.Plugin.Requests/Api/QueueContext.cs:47:    public required int OpenRequestsByRequester { get; init; }

It counts what the quota counts, which is open and approved requests, joined ones included, and it
counts the row being read as one of them. Anything else would put a number beside a row that
disagrees with the limit the same person is refused against, and an operator comparing the two would
be right to trust neither.

Enforcing the ceiling at the moment a request is created is #114 and is a different thing from
showing it here. A quota that refuses silently and a queue that shows the operator who is near it
answer different questions, and the second is the one that lets an operator decide before the
refusal rather than explain it afterwards.

## What approving will do next

Approving means one of two things and the operator has to know which. On a server with no external
service behind this plugin, approval is a state and the fetching is somebody's own business.
With a bridge configured, approval hands the request to something that will start downloading. An
operator who does not know which install they are in cannot know whether they are finishing their
work or starting somebody else's.

This is one answer per install rather than one per row, so it belongs beside the queue and not in it:

    git grep -n "BridgeConfigured" -- Jellyfin.Plugin.Requests/Api/InstallCapabilities.cs
    Jellyfin.Plugin.Requests/Api/InstallCapabilities.cs:59:    public required bool BridgeConfigured { get; init; }

Whether the configured service is answering right now is `BackendReachability`, which separates
`NotConfigured` from `Unreachable`, and showing that is #63 rather than this list. The distinction
matters here because approving into a service that has stopped answering is a decision an operator
would take differently, and a page that shows only "a bridge exists" hides it.

## Nothing on this list needs a second system

That is the third thing this issue asks for, and it is a property of the list rather than of the
page. Five of the six items are answered from this plugin's own store and configuration. The sixth,
the requester as a name rather than an identifier, is answered from the server this plugin is
running inside, by the dashboard the operator already has open.

None of the six asks the operator to open a metadata site, a chat log, or the external service's own
queue.

One question is deliberately not on the list because it would need one: whether the external service
can actually fetch the title. That answer lives on the other side of the bridge, it changes without
anybody here being told, and an operator who wants it is looking at the other system by definition.
What this plugin owes instead is that a submission which fails is visible as a failure rather than as
a request sitting in approved forever, which is the `Failed` state and is #82 and #86.

## What the page shows today

The page was a shell when this list was written and it is not one now. It draws rows, from #60, each
row carries the decisions its state admits, from #61, and the two items above are columns on it:

    git grep -n "cell(row, " -- Jellyfin.Plugin.Requests/Web/queue.html
    Jellyfin.Plugin.Requests/Web/queue.html:321:                    function cell(row, text) {
    Jellyfin.Plugin.Requests/Web/queue.html:610:                            cell(row, title(request));
    Jellyfin.Plugin.Requests/Web/queue.html:611:                            cell(row, RequestsShell.named("kind", request.Kind));
    Jellyfin.Plugin.Requests/Web/queue.html:612:                            cell(row, RequestsShell.named("queue.state", request.State));
    Jellyfin.Plugin.Requests/Web/queue.html:613:                            cell(row, moment(request.RequestedAt));
    Jellyfin.Plugin.Requests/Web/queue.html:614:                            cell(row, moment(request.StateChangedAt));
    Jellyfin.Plugin.Requests/Web/queue.html:615:                            cell(row, who(request));
    Jellyfin.Plugin.Requests/Web/queue.html:616:                            cell(row, waitingFor(request));
    Jellyfin.Plugin.Requests/Web/queue.html:617:                            cell(row, held(request));
    Jellyfin.Plugin.Requests/Web/queue.html:618:                            cell(row, askedBefore(request)).className = "requestsQueueAskedBefore";
    Jellyfin.Plugin.Requests/Web/queue.html:619:                            cell(row, request.RequesterNote || "");
    Jellyfin.Plugin.Requests/Web/queue.html:620:                            cell(row, decided(request));

The first of those twelve lines is the function that writes a cell and the other eleven are the cells
one row carries. The twelfth column beside them is the decisions that row admits, which is #61:

    git grep -n "decide(row, request)" -- Jellyfin.Plugin.Requests/Web/queue.html
    Jellyfin.Plugin.Requests/Web/queue.html:442:                    function decide(row, request) {
    Jellyfin.Plugin.Requests/Web/queue.html:621:                            decide(row, request);

Read against the six items above, that is all of them.

What is asked for is there, with the seasons and the requester's note. Whether the server already
holds it is there, with the time the answer was read, which is the `held` call. Who asked is there as
a name, which is the `who` call and the user list behind it. What was decided before is the
`askedBefore` call, one decision to a line, and how much this person is waiting for is `waitingFor`.
What approving will do next is answered per install rather than per row and is the panel beside the
queue, which is #63.

The two items that needed the answer widened are on it now:

    git grep -c "get; init;" -- Jellyfin.Plugin.Requests/Api/QueuedRequest.cs
    Jellyfin.Plugin.Requests/Api/QueuedRequest.cs:19

Nineteen properties, and the nineteenth is the `Context` the two arrive under. It is one shape rather
than two fields because neither is a property of the request: both are worked out from everything the
store holds, and a request knows nothing about its neighbours.

**What is short here is a cell an operator cannot read rather than an item nobody answered.** A row
for a title decided a dozen times carries a dozen lines in one cell, because nothing bounds the set
and #49 is what would. That is a shape somebody should look at on a real queue before it is called
finished, and it is not the absence this section was written about.

Nothing here was read from a running dashboard. What is measured is what the page draws, read out of
the file, which is the bound every check over these assets carries.

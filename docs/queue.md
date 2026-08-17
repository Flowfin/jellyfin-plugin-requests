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
can answer is work that has to be named, and two of the six are exactly that.

The measurements are read at `8e60f101df6c05815c24fbf083ffe8c2eab6b89e`, which is `master`. Every
command below was re-run at that commit rather than carried over from the reading the list was first
written at.

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

This is the item an operator most notices the absence of, and it is the first of two on this list
that the queue answer cannot carry today.

A title that was declined once and is asked for again is the case where the first decision matters
most. Without it in front of them the operator either declines from memory, which fails the first
time it is somebody else's memory, or approves something that was refused for a reason that has not
changed. The reason is in the store: a decline carries one, and a finished request is kept.

    git grep -n "FinishedRequestRetentionDays" -- Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:99:    public int FinishedRequestRetentionDays { get; set; } = 365;

Nothing on the queue path reads it. `JoinedByUserIds` on the queue answer is not this: it is the
several people standing behind one request that is still open, decided in #38, and it says nothing
about a request that was finished before this one was made.
Enforcing the retention period is #49 and is not built, so today the records are all still there.

So this item is work on the answer rather than on the page, and naming it here is the point of
writing the list before the rows.

## Whether this person is asking for a lot

Requests are approved one at a time and a disk fills up all at once. An operator looking at a single
row cannot see that it is the eleventh from the same person this week, and the ceiling that exists
is a number in the configuration rather than something the queue shows:

    git grep -n "OpenRequestsPerUser" -- Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:67:    public int OpenRequestsPerUser { get; set; } = 10;

The store can count a person's requests, in `IRequestStore.FindForUserAsync`, so the fact is
available and is not on the queue answer. This is the second of the two items that need the answer
widened, and it is the cheaper of them.

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
running inside.

None of the six asks the operator to open a metadata site, a chat log, or the external service's own
queue.

One question is deliberately not on the list because it would need one: whether the external service
can actually fetch the title. That answer lives on the other side of the bridge, it changes without
anybody here being told, and an operator who wants it is looking at the other system by definition.
What this plugin owes instead is that a submission which fails is visible as a failure rather than as
a request sitting in approved forever, which is the `Failed` state and is #82 and #86.

## What the page shows today

The page was a shell when this list was written and it is not one now. It draws rows, from #60, and
each row carries the decisions its state admits, from #61:

    git grep -n "cell(row, " -- Jellyfin.Plugin.Requests/Web/queue.html
    Jellyfin.Plugin.Requests/Web/queue.html:304:                    function cell(row, text) {
    Jellyfin.Plugin.Requests/Web/queue.html:527:                            cell(row, title(request));
    Jellyfin.Plugin.Requests/Web/queue.html:528:                            cell(row, RequestsShell.named("kind", request.Kind));
    Jellyfin.Plugin.Requests/Web/queue.html:529:                            cell(row, RequestsShell.named("queue.state", request.State));
    Jellyfin.Plugin.Requests/Web/queue.html:530:                            cell(row, moment(request.RequestedAt));
    Jellyfin.Plugin.Requests/Web/queue.html:531:                            cell(row, moment(request.StateChangedAt));
    Jellyfin.Plugin.Requests/Web/queue.html:532:                            cell(row, request.RequestedByUserId);
    Jellyfin.Plugin.Requests/Web/queue.html:533:                            cell(row, held(request));
    Jellyfin.Plugin.Requests/Web/queue.html:534:                            cell(row, request.RequesterNote || "");
    Jellyfin.Plugin.Requests/Web/queue.html:535:                            cell(row, decided(request));

The first of those ten lines is the function that writes a cell and the other nine are the cells one
row carries. The tenth column beside them is the decisions that row admits, which is #61:

    git grep -n "decide(row, request)" -- Jellyfin.Plugin.Requests/Web/queue.html
    Jellyfin.Plugin.Requests/Web/queue.html:359:                    function decide(row, request) {
    Jellyfin.Plugin.Requests/Web/queue.html:536:                            decide(row, request);

Read against the six items above, that is four of them and part of a fifth.

What is asked for is there, with the seasons and the requester's note. Whether the server already
holds it is there, with the time the answer was read, which is the `held` call. What approving will
do next is answered per install rather than per row and is the panel beside the queue, which is #63.

Who asked is on the page as the identifier the answer carries and not as a name:

    git grep -n "cell(row, request.RequestedByUserId)" -- Jellyfin.Plugin.Requests/Web/queue.html
    Jellyfin.Plugin.Requests/Web/queue.html:532:                            cell(row, request.RequestedByUserId);

The item above says turning one into the other is the server's own user list, which the dashboard
reaches without leaving the server. Nothing on the page does that turning, so an operator reads a
column of identifiers where the argument for the item was that a decision is about a person. That is
work on the page rather than on the answer, and it is the one part of this list that is short for a
reason no endpoint has to fix.

Two items cannot be rendered at all until the queue answer carries them, which is what it carries:

    git grep -c "get; init;" -- Jellyfin.Plugin.Requests/Api/QueuedRequest.cs
    Jellyfin.Plugin.Requests/Api/QueuedRequest.cs:18

Eighteen properties and none of them is a prior decision on the same title or a count of what this
person already has open. Both facts are in the store and neither reaches the queue answer, so both
are work on the endpoint. A page built without them would meet the list only by dropping the two
hardest items from it.

Nothing here was read from a running dashboard. What is measured is what the page draws, read out of
the file, which is the bound every check over these assets carries.

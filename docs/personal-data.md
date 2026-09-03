# What this plugin holds about a person

A request record says that a named person asked for a named title on a date. That is more revealing
than most of what a media server holds, and an operator running this for other people has to be able
to answer for it without reading the source.

This page is the account. It collects what other issues on this board decided rather than deciding
anything itself, and each section says where the decision lives. Where something is not decided or
not built, this page says so in those words instead of describing an intention as a behaviour.

## Every field that can name a person

The record is one type, and everything below is a property of it or of an entry in its history. The
identifiers are derived rather than listed from memory:

    git grep -nE 'public (required )?(Guid|Guid\?|IReadOnlyList<Guid>) ' -- Jellyfin.Plugin.Requests/Model/
    Jellyfin.Plugin.Requests/Model/MediaRequest.cs:61:    public required Guid Id { get; init; }
    Jellyfin.Plugin.Requests/Model/MediaRequest.cs:68:    public required Guid RequestedByUserId { get; init; }
    Jellyfin.Plugin.Requests/Model/MediaRequest.cs:145:    public IReadOnlyList<Guid> JoinedByUserIds { get; init; } = [];
    Jellyfin.Plugin.Requests/Model/MediaRequest.cs:166:    public IReadOnlyList<Guid> WantIds
    Jellyfin.Plugin.Requests/Model/MediaRequest.cs:190:    public Guid? StateChangedByUserId { get; init; }
    Jellyfin.Plugin.Requests/Model/RequestCaller.cs:53:    public Guid? UserId { get; }

Three of those six name a person. What each one is, and what it is for:

| Field                               | Who it names                                | Where it is written |
| ----------------------------------- | ------------------------------------------- | ------------------- |
| `MediaRequest.RequestedByUserId`    | the person who asked                        | the queue file      |
| `MediaRequest.JoinedByUserIds`      | everybody else who asked for the same title | the queue file      |
| `MediaRequest.StateChangedByUserId` | whoever last moved it, usually an operator  | the queue file      |

One more identifier is held outside the record, so the derivation above does not reach it and it is
named here rather than left to be found:

| Field                       | Who it names                                   | Where it is written |
| --------------------------- | ---------------------------------------------- | ------------------- |
| `notices.json` -> `Quiet[]` | everybody who has turned their own notices off | the notices file    |

It is the switch [notifications.md](notifications.md) describes. What it holds is a list of
identifiers and nothing else: no title, no date, no request. The list is the people who said no,
because the default is on, so a person who has never touched it is not in the file and an install
nobody has touched has no file.

The other three name nothing about a person. `MediaRequest.Id` and `MediaRequest.WantIds` are this
plugin's own identifier for a request and the browsing sibling's identifiers for the asks it handed
over. `RequestCaller.UserId` is who is making the call being handled right now, and it is an argument
passed between methods rather than a stored field: what reaches the disk is the record above, and the
store writes that record whole.

    git grep -n 'public MediaRequest? Request' -- Jellyfin.Plugin.Requests/Storage/PersistedRequest.cs
    Jellyfin.Plugin.Requests/Storage/PersistedRequest.cs:29:    public MediaRequest? Request { get; init; }

**The history names nobody, and it used to.** Every request carries a row from the moment it is made
and gains one on every move, and each row says what kind of caller made it rather than which person:

    git grep -n 'public required RequestActor By' -- Jellyfin.Plugin.Requests/Model/RequestHistoryEntry.cs
    Jellyfin.Plugin.Requests/Model/RequestHistoryEntry.cs:69:    public required RequestActor By { get; init; }

The first on-disk shape wrote an identifier there. The second writes the role, and a file in the older
shape is migrated as it is read, so an install upgrading from it stops holding those identifiers at
the next write rather than at some sweep somebody has to remember. What that costs is stated here as
well as at the field: nothing in this plugin can attribute a past decision to an individual any more,
which is the trade taken deliberately on #49 in exchange for not keeping identifiers for people who
have gone.

    git grep -n 'public RequestArrival? Arrival' -- Jellyfin.Plugin.Requests/Model/RequestHistoryEntry.cs
    Jellyfin.Plugin.Requests/Model/RequestHistoryEntry.cs:97:    public RequestArrival? Arrival { get; init; }

The value says which surface, and never which caller. A request handed over the seam is filed against
whoever the calling plugin said asked for it, with no session behind the name, and one asked for over
the endpoint is filed against the person the server authenticated. `docs/seam.md` argues why the
plugin that handed it over is not recorded.

**A person is held as the server's user identifier and never as a name.** No field carries a user
name, a display name, an email address or an external account. Whoever the identifier belongs to is a
question the server answers, and this plugin does not copy the answer into its own file.

### A third identifier goes somewhere this plugin keeps nothing of its own

**Every move a request makes writes a line into the server's activity log, and that line names the
person who made the move.** The entry is built here and handed to the host, and the identifier is one
of the four fields it carries:

    git grep -n 'new ActivityLog(note.Name, note.Type, note.UserId)' -- Jellyfin.Plugin.Requests/Notify/ServerActivityJournal.cs
    Jellyfin.Plugin.Requests/Notify/ServerActivityJournal.cs:50:        var entry = new ActivityLog(note.Name, note.Type, note.UserId)

| Field                 | Who it names                                              | Where it is written       |
| --------------------- | --------------------------------------------------------- | ------------------------- |
| `ActivityNote.UserId` | whoever made the move, or nobody where the plugin made it | the server's activity log |

Beside the identifier the entry carries the request's identifier and as much of the title as fits in
sixty characters, so one row says who moved which request about which title, and when.
[notifications.md](notifications.md) writes an entry out field by field and carries entries read off
a running server of each claimed line.

**The empty identifier there means nobody rather than somebody.** A move the fulfilment sweep makes
on its own carries no person, the server's entity has no nullable user, and the entry's second line
says in words that the plugin rather than a person made the move.

**This store is the server's rather than this plugin's**, which is why it is not among the three
files below and why neither of the two rules further down reaches it. It is named here because a
page listing where a person is named cannot leave out the one place this plugin writes an identifier
and cannot take it back.

### Two fields carry free text somebody typed

    git grep -nE 'public string\? (RequesterNote|DeclineNote)' -- Jellyfin.Plugin.Requests/Model/MediaRequest.cs
    Jellyfin.Plugin.Requests/Model/MediaRequest.cs:210:    public string? RequesterNote
    Jellyfin.Plugin.Requests/Model/MediaRequest.cs:243:    public string? DeclineNote

`RequesterNote` is what the person asking wrote, and `DeclineNote` is what the operator wrote back.
Both are bounded in length and neither is bounded in content. Anybody can write a name, an address or
anything else into one, and nothing in this plugin reads them for that or could. They are listed here
because a document about personal data that only lists the identifiers would be describing the fields
that are easy to reason about.

### What is stored beside a person is the revealing part

`DisplayTitle`, `DisplayYear`, `ProviderIds`, `Kind` and `Seasons` say what was asked for.
`RequestedAt`, `StateChangedAt` and every `At` in the history say when. None of them names anybody,
and all of them sit on the same record as the identifier of the person who asked. What the file holds
is therefore not a list of titles and not a list of people, it is a list of who wanted what, and
when.

## Where it is

Three files, all under the server's own data directory, and the table of them is in
[storage.md](storage.md) under "What is on the disk, and where". This page does not repeat the paths,
because two copies of a path are two answers the day one of them moves.

The activity entries above are in none of the three. They are rows in the server's own log, reached
through `IActivityManager`, and where that log lives is the server's answer rather than this
plugin's.

Everything in the first table above is in the queue file, and the identifier in the second is in the
notices file. The settings file holds no person at all, which is the whole
of that class rather than a sample:

    git grep -nE '^    public (const )?(int|bool|string) ' -- Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:53:    public const int MinimumRetentionDays = 30;
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:68:    public int OpenRequestsPerUser { get; set; } = 10;
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:73:    public bool AcceptsMovies { get; set; } = true;
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:84:    public bool AcceptsSeries { get; set; } = true;
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:100:    public int FinishedRequestRetentionDays { get; set; } = 365;
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:120:    public bool TellsAdministratorsAboutArrivals { get; set; }
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:152:    public string OutboundNoticeAddress { get; set; } = string.Empty;
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:167:    public bool AnnouncesApprovals { get; set; } = true;
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:181:    public bool AnnouncesDeclines { get; set; } = true;
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:191:    public bool AnnouncesFulfilments { get; set; } = true;

Three numbers, one of them the floor under another, six switches, and one address. Nothing there is
about anybody. Two of the switches say what kinds of thing may be asked for, three say which
movements are announced outward, and one says whether a live administrator is told that a request
arrived; [configuration.md](configuration.md) is the authority for each. The address is where a
notice is posted and is empty until an operator types one; it names a machine rather than a person,
and what typing one into it sends is the section on what leaves the server below.

`PluginConfigurationTests` refuses a second setting of that shape, so a credential arriving beside
it is a red suite rather than a thing to notice.

## Who on the machine can read it

**This plugin sets no permission on either file.** Nothing in it asks for one:

    git grep -n 'UnixFileMode\|SetUnixFileMode\|FileSecurity\|SetAccessControl' -- Jellyfin.Plugin.Requests/ ; echo "exit=$?"
    exit=1

So the queue is readable by whoever can read the server's data directory, under whatever the server
process and the operating system give a file created there. An operator who wants it narrower
narrows the directory, and that is a property of their installation rather than something this plugin
can promise.

**Anything running inside the server process can read it too**, and that is not a defect this plugin
could repair. It is the same boundary the seam is written against, and the section below says what
follows from it.

**What was not measured.** No permission was read off a running server on either claimed line.
Whether a container image, a package or a manual install leaves that directory readable to other
accounts on the machine is a fact about the installation, and no run on this board has asked one.

## How long it is kept

`FinishedRequestRetentionDays` is a setting, 365 by default, with a floor of 30.
[configuration.md](configuration.md) carries the number, the reason it is a field and the reason the
floor exists, and this page does not restate the argument.

**A request that has been finished for longer than that is deleted.** The setting is read where it
is validated and by the thing that acts on it:

    git grep -ln 'FinishedRequestRetentionDays' -- Jellyfin.Plugin.Requests/
    Jellyfin.Plugin.Requests/Configuration/ConfigurationRules.cs
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs
    Jellyfin.Plugin.Requests/Configuration/configPage.html
    Jellyfin.Plugin.Requests/Storage/RetentionSweep.cs

The class that declares it, the rule that refuses a value under the floor, the field on the settings
page, and the sweep. `RetentionTask` is what runs the sweep without anybody remembering to: daily and
at startup, in the server's own task list under `Requests`.

Deleted rather than anonymised. A record stripped of its requester still says a title was asked for
on this server on that date, and keeping that is not what a retention period is for.

**That was asked to change and the ask is withdrawn.** The decision of 2026-08-28 on #49 said
age-based stripping uses the same tombstone as an account deletion, which is the opposite of the
sentence above and of the argument `RetentionSweep` carries for it. #337 answered it on 2026-08-30,
against the ruling and for the tree: the sweep goes on removing, and the sentence putting the two on
one code path is withdrawn.

The reason is what the period is for rather than how it is spelled. A stripped row is never removed,
so a queue file that only ever gains rows would hold one row per request ever made for the life of
the install, each of them saying that a title was asked for on this server on that date. The two
paths are two on purpose: the tombstone is what a finished record carries between an account
deletion and the end of its period, removal is what the period is for, and nothing outlives it. No
issue builds this half, because it is the behaviour that already ships.

**Finished means fulfilled, declined or failed**, which is the same partition the quota already
draws, and the suite asserts the two agree over every state rather than leaving them to drift. An
open or approved request is never removed by age, because those are the two somebody still owes an
answer or a delivery on.

**The period runs from the move that finished the request**, `StateChangedAt`, and not from the day
it was asked for. A request answered after a year open is kept for the whole period after the
answer, and a declined request an operator later approves has left the finished set and starts again
if it comes back to it.

**An install whose settings this plugin will not run on deletes nothing.** The period is read through
the same seam that refuses a stored configuration rather than correcting it, so a file holding a
retention below the floor stops the run with the refusal instead of deleting against a number nobody
chose.

**What was not measured.** No run of this task on a server has been watched. What is measured is the
sweep, against the store the plugin ships, on both claimed target frameworks; that the server lists
and starts the task on either line is the same unrun procedure every other scheduled behaviour here
carries, in [testing.md](testing.md).

**Nothing removes an entry from the notices file by age, and nothing should.** It is a setting rather
than an event: it has no date on it, it stops meaning anything the moment it is dropped, and a sweep
that removed it after a year would turn a person's own choice back on without telling them. What it
holds about somebody is that they said no, and the way it goes is that they say yes or that their
account goes, which is the section below.

**Nothing removes an activity entry either, and that is an absence of reach rather than a decision.**
The sweep reads the queue file and writes the queue file, and nothing in it can see the server's
activity log:

    git grep -n 'IActivityJournal' -- Jellyfin.Plugin.Requests/Storage/ ; echo "exit=$?"
    exit=1

So a row saying who moved which request about which title outlives the request it describes. How long
the server keeps its own activity entries is the server's business rather than this plugin's, and no
reading on this board has measured it.

## What happens when a Jellyfin user is deleted

**Their records go, and the plugin is told rather than asked to notice.** The server raises an event
when an account is deleted and this plugin consumes it:

    git grep -n 'IEventConsumer<UserDeletedEventArgs>' -- Jellyfin.Plugin.Requests/
    Jellyfin.Plugin.Requests/People/RemovedAccounts.cs:30:public sealed class RemovedAccounts : IEventConsumer<UserDeletedEventArgs>
    Jellyfin.Plugin.Requests/PluginServiceRegistrator.cs:247:        serviceCollection.AddSingleton<IEventConsumer<UserDeletedEventArgs>, RemovedAccounts>();

There are three rules and they are not one rule.

**A finished request they asked for stays, with a tombstone where they were.** The record keeps its
title, its date and its answer, and `MediaRequest.RequestedByUserId` holds one fixed constant instead
of the person:

    git grep -n 'public static Guid Tombstone' -- Jellyfin.Plugin.Requests/People/DeletedPerson.cs
    Jellyfin.Plugin.Requests/People/DeletedPerson.cs:33:    public static Guid Tombstone { get; } = new Guid("00000000-0000-0000-0000-000000000049");

It is a constant rather than anything computed from the identifier it replaces. A pseudonym derived
from that identifier is the same person written down differently: two records carrying it say the
same account asked for both, and anybody holding the original can confirm a match by running the
derivation. This value carries nothing about who was replaced, so what the record says afterwards is
that somebody who is gone asked for this title on this date. Deletion-by-record was the alternative
and loses the administrator's history of what was asked and answered along with the person, which is
the trade taken on #49 on 28 August.

**An unfinished request they asked for is declined, and then carries the tombstone too.** The same
decision asks for an open request to be closed rather than removed, and there is no separate state
for that: a withdrawn-shaped value was considered and refused on #113, and the refusal is written
into the model in two places:

    git grep -n 'Cancelled' -- Jellyfin.Plugin.Requests/
    Jellyfin.Plugin.Requests/Model/RequestActor.cs:43:    /// is no state for a user withdrawing, refused with the <c>Cancelled</c> state on #113. The
    Jellyfin.Plugin.Requests/Model/RequestLifecycle.cs:82:/// user withdrawing has no state to move to because <c>Cancelled</c> was refused on #113. An

**#337 answered that on 2026-08-30, and the refusal stands.** Closed means the terminal state that
already exists, carrying a reason that says the requester is gone, rather than a sixth `RequestState`
value. The argument #113 made is still true - a user withdrawing is a second road to finished that an
operator does nothing different about - and a landed decision is not re-taken by a later sentence
that did not name it. What the answer costs is one `DeclineReason` value instead of eleven new cells
in the lifecycle table and everything that reads it.

**The behaviour moved in #361 and this page said it had not.** The reason is on the list:

    git grep -n 'TheRequesterIsGone = ' -- Jellyfin.Plugin.Requests/Model/DeclineReason.cs
    Jellyfin.Plugin.Requests/Model/DeclineReason.cs:82:    TheRequesterIsGone = 6

So an open or approved request of a deleted person is now declined for that reason and the record
stays, with the tombstone where the person was, because declining it makes it finished and a finished
request of a deleted person carries the tombstone by the rule above. Nothing about a deleted account
is removed from the queue file any more; what changes is what the record says.

**No person is named as having made that decline, because nobody made it.** The move goes through
the lifecycle rather than being written into the store, so it is checked and it leaves a history
entry like every other state change here, and the entry says the mover was the plugin.
`MediaRequest.StateChangedByUserId` is left empty on it, which is the same field the paragraph below
is about and the opposite case: there it holds an identifier because a person really did move the
request, and here it holds nothing because none did.

**The lifecycle admits the plugin into exactly that one move.** Two cells were widened for it, and
the reason and the mover are paired so the widening is not a general permission to decline: a caller
that is not an administrator may give only this reason, and an administrator may not give it at all.
The second half is what keeps the record honest, because an operator choosing it from a list would be
writing down a fact about somebody's account that nothing established.

**A request somebody else asked for that they had joined stays, and they come off its list.** The
request is not theirs, and taking a third party's request away because somebody else deleted their
account is a worse answer to a narrower problem.

**The switch in the notices file goes with them.** Somebody who turned their own notices off is an
identifier in that file, and it is set back to the shipping value, which is what takes the identifier
out of the list. It is the least revealing thing either file holds and it is still an identifier for
somebody who is gone.

**One identifier is deliberately left standing, and it is the one to read carefully.**
`MediaRequest.StateChangedByUserId` on a request that stays keeps whatever it held, including a
deleted administrator's identifier. Clearing it would say something false rather than nothing: an
empty value there means no person moved the request, so a cleared field would read as this plugin
having decided somebody else's request on its own. That was answered on #49 on 27 August, and the
answer is that the value stays and the queue is what shows an identifier nothing resolves as a person
who is gone.

**That rendering is built, and this page said it was not.** #307 closed as completed on 2026-08-27
and the queue page drew it the same day, while the sentence here went on saying an administrator sees
a raw identifier. It was found by reading the page against the tree rather than by anything failing:

    git log --format='%h %ad %s' --date=short -S 'queue.movedBy.deleted' --reverse -- Jellyfin.Plugin.Requests/Web/queue.html | head -1
    211b90c 2026-08-27 Draw who last moved a request, and keep a deleted account apart from an unanswered call

    git grep -n 'queue.movedBy.deleted' -- Jellyfin.Plugin.Requests/
    Jellyfin.Plugin.Requests/Localisation/Strings/en.json:135:  "queue.movedBy.deleted": "A person who has been deleted",
    Jellyfin.Plugin.Requests/Web/queue.html:564:                        return RequestsShell.word("queue.movedBy.deleted");

What the page draws is an identifier the dashboard's user list does not hold, and it says so only
where that list was actually read, so a failed call is never reported as a deleted account. The
tombstone above is such an identifier, which is why a tombstoned request reads as asked for by
somebody who is gone without the page being taught a second rule.

**A second identifier is left standing, and this one is a boundary rather than an answer.** Every
activity entry written for a move that person made keeps their identifier, and the consumer that
removes their records cannot reach one:

    git grep -n 'IActivityJournal' -- Jellyfin.Plugin.Requests/People/ ; echo "exit=$?"
    exit=1

The entries are rows in the server's own log and this plugin has no call that reads or deletes one,
so what an operator can do about them is what the dashboard offers for any entry, whichever plugin
wrote it. The field above stays because clearing it would say something false; this one stays because
nothing here can take it away, and the two are not the same statement.

**What the sweep cannot promise, said as a negative.** A request that keeps being decided on while the
removal runs is retried a bounded number of times and then left as it is, with a line in the log at a
level an operator sees. Nothing looks at such a record again on its own, because the account it names
no longer exists for anything to start a search from.

**And no server has been watched doing any of this.** What is measured is the sweep against the store
this plugin ships and the registration the server would resolve, on both claimed target frameworks. No
Jellyfin was running where that was measured.

The retention period above is a different rule and does not stand in for this one. It reaches a record
when the record is old, whoever it names.

## What leaves the server

**Nothing goes to anybody else's machine on a fresh install, and one path can be turned on.** What
does travel on a fresh install is a person's own request, to that person, over the connection the
server already holds to whatever they are signed in on; the table at the end of this section is
where that sits. Beyond that the plugin has exactly two outbound paths, the notification sink and the
bridge to an external request service, and each is a client in one file:

    git grep -ln 'HttpClient\|IHttpClientFactory\|WebRequest' -- Jellyfin.Plugin.Requests/
    Jellyfin.Plugin.Requests/Bridge/Overseerr/OverseerrBackend.cs
    Jellyfin.Plugin.Requests/Notify/OutboundSink.cs

The sink sends nothing until an operator sets `OutboundNoticeAddress`, which is empty on every
install where nobody has decided otherwise, and there is no other way to turn it on. What it sends
when it is on is a small JSON document per movement in the queue: the request's identifier, what
happened to it, when, the title and year, and the identifiers of the person who asked and the person
who answered. It carries neither note, no provider identifiers, nobody else waiting on the request,
and no user name of any kind, because this plugin holds none. [notifications.md](notifications.md) is
where that document is written out field by field.

The bridge sends nothing until an operator sets `BridgeAddress`, which is empty on every install for
the same reason, and there is no other way to turn it on. It has two implementations, the one for a
server that has no backend and the adapter that speaks the Overseerr form, and the adapter hands
every call to the first until an address is written:

    git grep -n ': IRequestBackend' -- Jellyfin.Plugin.Requests/
    Jellyfin.Plugin.Requests/Bridge/NoRequestBackend.cs:23:public sealed class NoRequestBackend : IRequestBackend
    Jellyfin.Plugin.Requests/Bridge/Overseerr/OverseerrBackend.cs:70:public sealed class OverseerrBackend : IRequestBackend, IDisposable

What it sends when it is on is one submission per approval, in the form's own field names: the kind
of thing as `movie` or `tv`, the request's TMDB identifier and no other provider's, the seasons asked
for where it is a series, and, only for a person the operator has written a row for, the number the
service knows that person by. No title, no year, no Jellyfin user identifier and no name of any kind
are on the wire, and a person with no row is not named to the service at all. After that it asks the
service, hourly, where each handed-over request stands, sending the number the service issued and
nothing about the person. [bridge.md](bridge.md) is where the submission is written out field by
field with the legs that read it back off an in-process service.

There is no metadata lookup either. This plugin calls no metadata source at all, which is a lint rule
rather than a habit:

    git grep -n 'id: no-call-to-a-metadata-source' -- tools/opengrep/rules.yaml
    tools/opengrep/rules.yaml:529:  - id: no-call-to-a-metadata-source

And nothing reports anything to this project, at any setting, by design and with no opt-in. That is
recorded in [notifications.md](notifications.md) with the decision behind it.

Four paths carry something outward, and each is named here with what turning it on means, so that an
operator can find that out before typing a value rather than after:

| Path                  | Issue | What leaves, or would                                                                            | Off until          | Built |
| --------------------- | ----- | ------------------------------------------------------------------------------------------------ | ------------------ | ----- |
| The outbound sink     | #78   | the identifiers of the asker and the answerer, the title, the year, the request                  | an address is set  | yes   |
| The bridge            | #315  | the TMDB identifier, the kind, the seasons, and the service's own number for a person with a row | an address is set  | yes   |
| The session message   | #77   | nothing off the machine, a message to the asker's own signed-in clients                          | never off          | yes   |
| The arrival to admins | #76   | nothing off the machine, one arrival to whoever administers the server                           | a switch is set on | yes   |

The bridge names a person to the external service by an account the operator wrote into a table, and
never by their name. That is the decision in [bridge.md](bridge.md), and the table is empty on a
fresh install, so a bridge configured and nothing else sends no attribution at all. The activity log
in #75 is the fourth path and it is not in the table because it writes into the server's own log,
which does not leave the machine.

The two session rows are in the table for what they are rather than for what they send off the
machine, which is nothing: both go down connections the server already holds to clients already
signed in. The one to the person who asked carries the title they asked for and what happened to it.
The one to the administrators carries the same document the outbound sink would post about an
arrival, which names the person who asked by the server's own identifier and nobody else, and it
reaches only sessions the server itself counts as administering it. It is off on a fresh install:
`TellsAdministratorsAboutArrivals` in [configuration.md](configuration.md) is what turns it on, and
[notifications.md](notifications.md) says why nothing reads it today.

**Every row in that table is built now.** The bridge row was the last, and the table's fourth column
says what has to be set before each path carries anything.

## What arrives from another plugin, and what this side trusts

A request can arrive from the browsing sibling instead of from a person using the API, and that path
has no session behind it. It carries a user identifier that this plugin cannot verify.

**The sibling's own permission check is the only check on that path, and this side is trusting it.** A
want that arrives over the seam is attributed to whoever the caller says asked for it. Anybody
evaluating this plugin should read that as it stands rather than as a check that is implied
somewhere.

That is a boundary rather than a hole. The caller is another plugin inside the same server process,
which can already read this plugin's file and write it, so a check here would refuse nothing that is
not already possible and would read afterwards as protection.

What this side does check is that the identifier names somebody the server has, and a handover naming
a user it does not have is refused with nothing stored.
[seam.md](seam.md) carries the same conclusion at length, in the section on what this side trusts and
what it checks anyway, and #118 is where it was stated.

## What this page does not say

**It states no legal position.** What a given operator has to do under the law they run under depends
on where they are and who their users are, and this page is an account of what the software holds so
that somebody who has to answer that question can.

**Nothing here was measured on a running server.** Every command above reads this repository. File
permissions on a real install, and what a real queue holds after a year, are not in it.

**It carries no list of the request states or of the transitions.** Those are in
[lifecycle.md](lifecycle.md), printed from the code that decides them, and a copy here would drift
against it.

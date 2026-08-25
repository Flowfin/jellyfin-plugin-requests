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
    Jellyfin.Plugin.Requests/Model/RequestCaller.cs:52:    public Guid? UserId { get; }
    Jellyfin.Plugin.Requests/Model/RequestHistoryEntry.cs:50:    public Guid? ByUserId { get; init; }

Four of those seven name a person. What each one is, and what it is for:

| Field                               | Who it names                                                         | Where it is written |
| ----------------------------------- | -------------------------------------------------------------------- | ------------------- |
| `MediaRequest.RequestedByUserId`    | the person who asked                                                 | the queue file      |
| `MediaRequest.JoinedByUserIds`      | everybody else who asked for the same title                          | the queue file      |
| `MediaRequest.StateChangedByUserId` | whoever last moved it, usually an operator                           | the queue file      |
| `RequestHistoryEntry.ByUserId`      | the person who asked, on the arrival row, and whoever moved it after | the queue file      |

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

**Every request carries one of those history rows from the moment it is made.** The first entry on a
request is not a move: it says how the ask reached this server, and it names the person it is filed
against, so `RequestHistoryEntry.ByUserId` is written for every request rather than only for those
somebody has decided on. It is the same identifier as `MediaRequest.RequestedByUserId` on the same
record, so it names nobody the file did not already name, and what it adds is provenance rather than
a person.

    git grep -n 'public RequestArrival? Arrival' -- Jellyfin.Plugin.Requests/Model/RequestHistoryEntry.cs

The value says which surface, and never which caller. A request handed over the seam is filed against
whoever the calling plugin said asked for it, with no session behind the name, and one asked for over
the endpoint is filed against the person the server authenticated. `docs/seam.md` argues why the
plugin that handed it over is not recorded.

**A person is held as the server's user identifier and never as a name.** No field carries a user
name, a display name, an email address or an external account. Whoever the identifier belongs to is a
question the server answers, and this plugin does not copy the answer into its own file.

### Two fields carry free text somebody typed

    git grep -nE 'public string\? (RequesterNote|DeclineNote)' -- Jellyfin.Plugin.Requests/Model/MediaRequest.cs
    Jellyfin.Plugin.Requests/Model/MediaRequest.cs:209:    public string? RequesterNote
    Jellyfin.Plugin.Requests/Model/MediaRequest.cs:242:    public string? DeclineNote

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

Everything in the first table above is in the queue file, and the identifier in the second is in the
notices file. The settings file holds no person at all, which is the whole
of that class rather than a sample:

    git grep -nE '^    public (const )?(int|bool|string) ' -- Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:52:    public const int MinimumRetentionDays = 30;
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:67:    public int OpenRequestsPerUser { get; set; } = 10;
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:72:    public bool AcceptsMovies { get; set; } = true;
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:83:    public bool AcceptsSeries { get; set; } = true;
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:99:    public int FinishedRequestRetentionDays { get; set; } = 365;
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:123:    public string OutboundNoticeAddress { get; set; } = string.Empty;

Three numbers, one of them the floor under another, two switches, and one address. Nothing there is
about anybody. The address is where a notice is posted and is empty until an operator types one; it
names a machine rather than a person, and what typing one into it sends is the section on what
leaves the server below.

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

## What happens when a Jellyfin user is deleted

**Nothing.** No part of this plugin is told that a user was removed, and nothing looks:

    git grep -n 'IUserManager' -- Jellyfin.Plugin.Requests/
    Jellyfin.Plugin.Requests/Seam/ServerKnownUsers.cs:15:    private readonly IUserManager _users;
    Jellyfin.Plugin.Requests/Seam/ServerKnownUsers.cs:22:    public ServerKnownUsers(IUserManager users)

That one reference asks whether a user exists when a want arrives over the seam. It is a question
about the present, not a subscription to a deletion, and nothing in this plugin acts on an account
going away.

So a request record outlives the account it names. The identifier stays in the file, in up to four
places for one deleted person: their own requests, requests they joined that somebody else asked for,
and every decision they made as an operator, which is written into the record and into its history.

**And there is now a fifth place, in the other file.** Somebody who turned their own notices off is
an identifier in the notices file, and a deleted account leaves it there exactly as it leaves the
four above. It is the least revealing of the five - it says that a person on this server once said
no and nothing more - and it is still an identifier for somebody who is gone. #49 is where the rule
for all of them is decided, and this one is deliberately not answered ahead of the other four: a
plugin that swept one file on a deletion it is not told about would be describing a behaviour it does
not have.

**What the rule should be is open, and it is open for a reason rather than by neglect.** The history
a decision is written into is append-only and a lint rule refuses any other writer, so stripping an
identifier out of past entries is not a small change to make while implementing a sweep. #49 holds
the question and states the three answers that are available, each of which leaves something
different behind. This page cannot state a behaviour that has not been decided, and describing the
current absence as a retention choice would be exactly that.

The retention period above is not that rule and does not stand in for it. It reaches a record when
the record is old, whoever it names, and a deleted person's identifier sits in the file until then.

## What leaves the server

**Nothing goes to anybody else's machine on a fresh install, and one path can be turned on.** What
does travel on a fresh install is a person's own request, to that person, over the connection the
server already holds to whatever they are signed in on; the table at the end of this section is
where that sits. Beyond that the plugin makes exactly one outbound call, and it is the notification
sink:

    git grep -ln 'HttpClient\|IHttpClientFactory\|WebRequest' -- Jellyfin.Plugin.Requests/
    Jellyfin.Plugin.Requests/Notify/OutboundSink.cs

It sends nothing until an operator sets `OutboundNoticeAddress`, which is empty on every install
where nobody has decided otherwise, and there is no other way to turn it on. What it sends when it is
on is a small JSON document per movement in the queue: the request's identifier, what happened to it,
when, the title and year, and the identifiers of the person who asked and the person who answered. It
carries neither note, no provider identifiers, nobody else waiting on the request, and no user name of
any kind, because this plugin holds none. [notifications.md](notifications.md) is where that document
is written out field by field.

The bridge to an external request service has exactly one implementation in this tree, and it is the
one for a server that has no backend:

    git grep -n ': IRequestBackend' -- Jellyfin.Plugin.Requests/
    Jellyfin.Plugin.Requests/Bridge/NoRequestBackend.cs:23:public sealed class NoRequestBackend : IRequestBackend

There is no metadata lookup either. This plugin calls no metadata source at all, which is a lint rule
rather than a habit:

    git grep -n 'id: no-call-to-a-metadata-source' -- tools/opengrep/rules.yaml
    tools/opengrep/rules.yaml:479:  - id: no-call-to-a-metadata-source

And nothing reports anything to this project, at any setting, by design and with no opt-in. That is
recorded in [notifications.md](notifications.md) with the decision behind it.

Four paths would carry something outward once they are built, and each is named here so that an
operator can find out what turning one on would mean before it exists:

| Path                  | Issue | What leaves, or would                                                           | Off until          | Built |
| --------------------- | ----- | ------------------------------------------------------------------------------- | ------------------ | ----- |
| The outbound sink     | #78   | the identifiers of the asker and the answerer, the title, the year, the request | an address is set  | yes   |
| The bridge            | #82   | a title, and an external account for the person who asked                       | a backend is set   | no    |
| The session message   | #77   | nothing off the machine, a message to the asker's own signed-in clients         | never off          | yes   |
| The arrival to admins | #76   | nothing off the machine, one arrival to whoever administers the server          | a switch is set on | yes   |

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

**The bridge row is the one that is not built.** The other two say what happens today.

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

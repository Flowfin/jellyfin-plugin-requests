# Telling somebody a request moved

There is no notification extension point on either supported server line. A plugin author looking for
one finds a type called `NotificationType` and stops looking, and what that type turns out to be is an
enumeration with nothing behind it. Anything this plugin sends, it sends itself.

This page records that, names the three paths this plugin uses instead, and says why there is no
fourth.

## The absence, measured

Run against the server's own tree. `release-10.11.z` is the 10.11 line and `master` is the 12.0 line.

    git ls-tree -r --name-only origin/release-10.11.z | grep -i notification
    MediaBrowser.Model/Notifications/NotificationType.cs

    git ls-tree -r --name-only origin/master | grep -i notification
    MediaBrowser.Model/Notifications/NotificationType.cs

    git rev-parse origin/release-10.11.z origin/master
    1fbd8739292cce610231be93daf43368733edf63
    fb763c47bfc88b1661f8dd1f3f7a4340d140380e

One file on each line, and it is an enumeration of names such as `PluginInstalled` and
`NewLibraryContent`. There is no interface to implement, no service to register and nothing to call.
A value from that enumeration is a label with no delivery behind it, so this plugin does not use it.

That measurement is of the server source at the two commits above. It is not a claim about what a
future line offers, and a line that grows an extension point is a reason to read this page again
rather than a reason it was wrong.

## The three paths

Each one is the server's own, and each already exists on both lines.

**The activity log.** Every transition is written there. It is where an operator already looks when
they want to know what the server did, it survives a restart, and it appears in the dashboard with no
work from this plugin. The interface is `IActivityManager`:

    git ls-tree -r --name-only origin/release-10.11.z | grep IActivityManager
    MediaBrowser.Model/Activity/IActivityManager.cs

    git ls-tree -r --name-only origin/master | grep IActivityManager
    MediaBrowser.Model/Activity/IActivityManager.cs

This is the path that is always on, because it is a record rather than a message: nobody is
interrupted by it and it is the thing an operator reads afterwards when somebody asks what happened.
It is built, and what an entry says is the section below.

**A message to a live session.** Somebody with a client open is told something through the
connection the server already holds to it. It reaches nobody who is not signed in at that moment and
it leaves nothing behind, which is exactly right for news and exactly wrong for anything that matters
after the tab is closed. The server carries it twice, once per audience:

    git grep -n "Task SendMessageToAdminSessions" origin/release-10.11.z -- MediaBrowser.Controller/Session/ISessionManager.cs
    origin/release-10.11.z:MediaBrowser.Controller/Session/ISessionManager.cs:196:        Task SendMessageToAdminSessions<T>(SessionMessageType name, T data, CancellationToken cancellationToken);

    git grep -n "Task SendMessageToAdminSessions" origin/master -- MediaBrowser.Controller/Session/ISessionManager.cs
    origin/master:MediaBrowser.Controller/Session/ISessionManager.cs:196:        Task SendMessageToAdminSessions<T>(SessionMessageType name, T data, CancellationToken cancellationToken);

The second one names people rather than a role, and it is the one this plugin uses. Read at the same
two commits as the section above, `1fbd873` and `ae87230`, the latter being where the 12.0 line
stands now rather than where it stood when the measurement above was taken:

    git grep -n "Task SendMessageToUserSessions<T>(List<Guid> userIds, SessionMessageType name, T data" origin/release-10.11.z -- MediaBrowser.Controller/Session/ISessionManager.cs
    origin/release-10.11.z:MediaBrowser.Controller/Session/ISessionManager.cs:207:        Task SendMessageToUserSessions<T>(List<Guid> userIds, SessionMessageType name, T data, CancellationToken cancellationToken);

    git grep -n "Task SendMessageToUserSessions<T>(List<Guid> userIds, SessionMessageType name, T data" origin/master -- MediaBrowser.Controller/Session/ISessionManager.cs
    origin/master:MediaBrowser.Controller/Session/ISessionManager.cs:207:        Task SendMessageToUserSessions<T>(List<Guid> userIds, SessionMessageType name, T data, CancellationToken cancellationToken);

Telling the person who asked that their own request moved is built, and the section below is what
they are told. Telling a live administrator that something arrived is built too, on the other of the
two calls above, and the section for it says why an operator sees nothing today.

**One outbound sink.** An operator who wants a request to reach something outside the server points
this at whatever they already run. It is one sink with a defined payload rather than a service this
plugin knows the name of, so the plugin carries no vocabulary for anybody's product and nothing has
to be updated here when one of them changes. It is built, it is off on every install where nobody
has typed an address, and the section below is what it sends.

## What the sink sends, and what it does not

This is the only thing in this plugin that sends anything off the machine, and it is off until an
operator sets `OutboundNoticeAddress` in the settings. Empty is the whole of how it is off; there is
no second switch that means off, for the reason in [configuration.md](configuration.md).

With an address set, each movement the operator has left switched on is posted to it as one JSON
document:

```json
{
    "version": 1,
    "event": "Approved",
    "requestId": "0f9c9107-b31b-459e-81fa-6d35dac25e79",
    "at": "2026-08-14T17:30:00+00:00",
    "state": "Approved",
    "requestedByUserId": "6b1f2c40-1f5a-4a53-9d0e-2b7a3c9d5e11",
    "movedByUserId": "9c2d8b71-4e0c-4a1f-8f3d-11a2b3c4d5e6",
    "kind": "Movie",
    "title": "Solaris",
    "year": 1972
}
```

`event` is one of `Asked`, `Approved`, `Declined` and `Fulfilled`. It is a word rather than a number
so that a value inserted into the middle of that list later cannot silently change what every past
document meant, and `state` and `kind` are words for the same reason. Three of the four are sent:
`Asked` is in the vocabulary and nothing here announces one, which the section below says why.

`movedByUserId` is absent where nobody moved it. An arrival was not moved by anybody, and fulfilment
is decided by the library rather than by a person.

`at` is when the request moved, not when the message was sent. A sink that was unreachable for an
hour delivers nothing that reads as having just happened when it comes back.

### What it deliberately does not carry

Neither note. `RequesterNote` and `DeclineNote` are free text somebody typed, and either can hold a
name, an address or anything else. This plugin has no business forwarding that to a service an
operator pointed it at, and a message a person reads does not need it.

The provider identifiers, which place the title in third-party catalogues. The title and the year
are what a person reads, and the request identifier is what a machine matches on.

Everybody else waiting. A notice about one movement is not a roster, and a request several people
joined would otherwise post all of them to somebody's chat service on every move.

The history. That is the record, and this is a message.

**No user name of any kind.** A person is held here as the server's own user identifier and never as
a name, which is [personal-data.md](personal-data.md)'s account of the whole plugin, so this document
has no name to send. A reader that wants one resolves the identifier against the server it is already
talking to.

### The version, and what moves it

`version` is on every document rather than on the ones that needed it, because a reader that has to
infer which shape it is holding breaks on the first change.

It moves when a reader that understood the old document would misread the new one, which is a field
removed, renamed, or given a different meaning. A field added beside the existing ones does not move
it: a reader that ignores what it does not recognise is unharmed, and bumping for an addition trains
readers to treat the number as noise.

### What happens when the endpoint misbehaves

**Nothing that reaches a request.** An approval must not fail because somebody's chat service is
having a bad day, so the sink is handed the notice and the caller carries on; there is no task to
await and therefore no way for a transition to end up waiting on one. An endpoint that refuses the
connection, one that answers with a failure and one that accepts the connection and then says
nothing all cost the same: a line in the server's log and nothing else.

**A send is given ten seconds and then abandoned.** An endpoint that takes a connection and holds it
would otherwise accumulate one of those per movement in the queue.

**Nothing is retried and nothing is queued.** A message sent while the endpoint was down is lost, and
this plugin does not remember that it was. That is the right trade for a courtesy and the wrong one
for a record, which is why the record is the server's activity log and not this. An operator who
needs to know what happened reads that log.

## What an activity entry says

One entry per transition and none for anything else. Asking for something is not a transition:
nothing has been decided, the model appends no history entry for it, and an entry there would be this
plugin announcing its own arrival in a list an operator reads for what the server did. Telling an
administrator that something arrived is a live message and is the section further down. An observation that changed a
title's availability without moving the request writes nothing here either, because a line per
re-observation is the wall of entries this page's own rule refuses.

Each entry is three pieces of text and a user, and they are built from four fields of the request:
the two states, the identifier, the title snapshot, and who made the move.

| Piece           | What it holds                                                                       |
| --------------- | ----------------------------------------------------------------------------------- |
| `Name`          | `Request approved: <title>`, with the title cut to a line                           |
| `ShortOverview` | the two states, whether a person or the plugin moved it, and the request identifier |
| `Type`          | `MediaRequest` followed by the state moved into, which is what a filter matches on  |
| `UserId`        | the administrator who decided, or the empty identifier where the plugin observed    |

The title is cut because it is a snapshot of what whoever asked typed and nothing caps it on the way
here, so an entry built without the cut is one row of the activity list as long as somebody wanted it
to be. What is cut is replaced by an ellipsis, so a reader can see that something was.

**A move the plugin made says so in words as well as by the empty identifier.** The server's entity
has no nullable user, so an entry that only left the identifier empty reads in the dashboard as an
entry whose user could not be resolved, which is a different statement from nobody having decided
anything.

### What an entry never carries

The note an operator types with a decline, and the note the requester wrote. Both can be five hundred
characters, both are a message to one person, and the activity list is read by every administrator on
the server.

A credential and a path on the server's disk. Neither is reachable from where an entry is built: the
only input is the request, and the configuration and the file system are not in scope there.

The request identifier is in the text rather than in the entity's `ItemId`. That field is a library
item on the server's side and the dashboard offers it as a link, so a request identifier there is a
link to an item that does not exist.

### Read back on a real server of each claimed line

Nothing in the suite runs a server, which the headless rule in [`docs/testing.md`](testing.md)
settles, so what the suite asserts is what this plugin asked to be written. Whether the server kept
it, and whether it comes back where an operator looks, is a different question and is answered by a
job rather than by a test.

`.github/workflows/activity-entries.yaml` starts a Jellyfin of each line, installs the plugin, asks
for something over this plugin's own API, approves it and declines it, then reads
`GET /System/ActivityLog/Entries`, which is the endpoint the dashboard's activity page draws. It runs
`scripts/verify-activity-entries.sh`, on every pull request and nightly.

Run `32490605857` at `64bc924ea5650becf44f1e56537b237378f25d24`. On `jellyfin/jellyfin:10.11.11`:

    == read the activity entries the dashboard draws
    Type=MediaRequestDeclined  Name=Request declined: A film for the activity check  ShortOverview=Approved to Declined. Request 2d03fdb9-d113-452e-95ff-94e58d437e77.  UserId=3ce3b30b838944cab978503c3732d199
    Type=MediaRequestApproved  Name=Request approved: A film for the activity check  ShortOverview=Open to Approved. Request 2d03fdb9-d113-452e-95ff-94e58d437e77.  UserId=3ce3b30b838944cab978503c3732d199
    Type=AuthenticationSucceeded  Name=verify successfully authenticated  ShortOverview=IP address: 172.17.0.1  UserId=3ce3b30b838944cab978503c3732d199
    Type=SessionStarted  Name=verify is online from load-check  ShortOverview=IP address: 172.17.0.1  UserId=3ce3b30b838944cab978503c3732d199
    Type=UserPasswordChanged  Name=Password has been changed for user verify  ShortOverview=None  UserId=3ce3b30b838944cab978503c3732d199

    == done
    two transitions, two entries, read back from jellyfin/jellyfin:10.11.11 (net9.0)

And on `jellyfin/jellyfin:12.0-rc4`:

    == read the activity entries the dashboard draws
    Type=MediaRequestDeclined  Name=Request declined: A film for the activity check  ShortOverview=Approved to Declined. Request cb9afc31-7ca3-415d-83e3-37d38ddaae75.  UserId=2b4870e65ab3463d91b3b5e890897ed8
    Type=MediaRequestApproved  Name=Request approved: A film for the activity check  ShortOverview=Open to Approved. Request cb9afc31-7ca3-415d-83e3-37d38ddaae75.  UserId=2b4870e65ab3463d91b3b5e890897ed8
    Type=AuthenticationSucceeded  Name=verify successfully authenticated  ShortOverview=IP address: 172.17.0.1  UserId=2b4870e65ab3463d91b3b5e890897ed8
    Type=SessionStarted  Name=verify is online from load-check  ShortOverview=IP address: 172.17.0.1  UserId=2b4870e65ab3463d91b3b5e890897ed8
    Type=UserPasswordChanged  Name=Password has been changed for user verify  ShortOverview=None  UserId=2b4870e65ab3463d91b3b5e890897ed8

The three entries under the plugin's two are the server's own, from the same run, and they are left
in the paste rather than cut: what the check asserts is that exactly two entries name the request,
and a paste showing only those two would read as a server that logged nothing else.

The check bites. Its own first run went red twice for two different reasons, neither of them about
the plugin: one line reset the connection while the server was still coming up, and the other read
both entries correctly and matched neither, because the identifier is handed back without dashes and
written into an entry with them. Both are repaired and both are why the transcript above is a second
run rather than a first.

### What the entry is still not proof of

That the dashboard draws it. Nothing above opens a browser, which is the first refusal in
[`docs/testing.md`](testing.md), so what is proven is that the entries reach the endpoint the
dashboard reads and not that the page renders them.

### When the activity log itself refuses

The move is in the store before the entry is attempted, so a decision is never undone by a log that
would not take a line about it. The failure is reported to the server's log with the move in it and
the call carries on. What is lost is the line an operator would have read in the dashboard, and this
plugin holds nothing that would let it be written later.

## What the person who asked is told

The one message this plugin pushes at a person is about their own request, and it is sent when that
request is approved, declined or fulfilled.

**It reaches whoever is signed in at that moment and nobody else, and nothing remembers who was
missed.** Somebody whose client is closed when an operator answers them is told nothing here, and no
second attempt is made later. That is the right trade for a courtesy and the wrong one for an answer
somebody is waiting on, which is why the answer they can rely on is their own page in
[surface.md](surface.md): it shows the state of every request they made whenever they next look, it
is there after a restart, and it is where the operator's note beside a decline is. Nothing about a
request depends on the message arriving.

**One person is named and there is nowhere to put a second.** The message carries a single user
identifier, it is read off the request rather than passed in by whoever moved it, and the only thing
it can be is whoever asked. So an operator answering one person's request cannot reach anybody else
from here, and the person who is told learns nothing about anybody else's queue.

### What it says

| Movement  | What the person reads                          |
| --------- | ---------------------------------------------- |
| Approved  | that their request for that title was approved |
| Declined  | that it was declined, and the reason           |
| Fulfilled | that the title is in the library now           |

The reason on a decline is read out of the same catalogue entry a surface draws it under, so somebody
who sees the message and then opens their own page is told one thing twice rather than two things.

The title is cut to a line, because it is a snapshot of what whoever asked typed and nothing caps it
on the way here. What is cut is replaced by an ellipsis so the reader can see that something was.

**It carries neither note.** The operator's note beside a decline can be five hundred characters and
this is one line that goes away by itself, so that note is on the page rather than here. The
requester's own note tells them nothing they do not already know. Nobody else waiting for the same
title is in it either, and no name of any person appears anywhere, for the reason in
[personal-data.md](personal-data.md).

**A movement nobody wrote a sentence for sends nothing.** A state added to the model arrives here
with nobody having decided what a person should read, and a decline that carries no reason would
produce a sentence with a hole where the reason goes. Both send nothing, because a message withheld
is recoverable by opening the page and a wrong one is not.

### How it reaches a client, and what that is not proof of

A plugin cannot add a name to the server's own list of session message types, so this borrows the one
a client already acts on: the message goes out as a `GeneralCommand` carrying `DisplayMessage`, which
is the shape the server itself builds when something asks it to show somebody a message. Read at
`1fbd873` and `ae87230`:

    git grep -n 'generalCommand.Arguments\["Header"\]' origin/release-10.11.z -- Emby.Server.Implementations/Session/SessionManager.cs
    origin/release-10.11.z:Emby.Server.Implementations/Session/SessionManager.cs:1225:            generalCommand.Arguments["Header"] = command.Header;

    git grep -n 'generalCommand.Arguments\["Header"\]' origin/master -- Emby.Server.Implementations/Session/SessionManager.cs
    origin/master:Emby.Server.Implementations/Session/SessionManager.cs:1279:            generalCommand.Arguments["Header"] = command.Header;

The web client subscribes to that name and draws the message, and whether it draws a notice or a
dialog depends on one argument. Read at `5389bba` in `jellyfin/jellyfin-web`:

    ref=5389bbad37d178ef5ebeaac8860403527c0e4121
    gh api "repos/jellyfin/jellyfin-web/contents/src/scripts/serverNotifications.js?ref=$ref" -H "Accept: application/vnd.github.raw" |
      grep -nE "TimeoutMs|toast|alert|case 'DisplayMessage'|OutboundWebSocketMessageType.GeneralCommand"
    3:import alert from 'components/alert';
    8:import toast from 'components/toast/toast';
    22:    if (args.TimeoutMs) {
    23:        toast({ title: args.Header, text: args.Text });
    25:        alert({ title: args.Header, text: args.Text });
    125:        case 'DisplayMessage':
    197:        apiClient.subscribe([OutboundWebSocketMessageType.GeneralCommand], ({ Data }) => processGeneralCommand(Data, apiClient)),

So the timeout this plugin always sets is not decoration: without one that client shows a dialog
somebody has to dismiss, and a courtesy that sits over what a person was doing until they click it is
worse than not sending it.

**What that is not.** One file of one client was read, at one commit, and no client was run. It is
not a claim about what any other Jellyfin client does with the same message, and it is not a claim
that anybody saw anything. The suite asserts what this plugin asked the server to send, to whom, and
that no other way of reaching anybody was used; the headless rule in [testing.md](testing.md) is why
it stops there.

### When the push fails

**Nothing that reaches a request.** The move is in the store before anybody is told, telling somebody
hands nothing back for a caller to check, and every way a push can fail costs the same: a line in the
server's log and nothing else. Nothing is retried and nothing is queued.

**There is no setting for it, and who one would belong to is no longer an open question.** The three
switches below narrow the outbound sink and none of them reaches this, and the activity log has no
switch either. Whether an operator or the person themself should be the one to turn this off was
taken on #9 on 2026-08-24: the switch belongs to the person who made the request, default on, because
a person controls what they are told about their own request and nobody else does.

**The answer is written down and the switch is not built**, which are two states rather than one. An
operator setting is refused by the answer rather than missing from it, so no field is coming beside
the three below; what a person's own switch needs is somewhere per person to keep a preference, and
this plugin keeps nothing per person today. That is #287. Until it lands, the message goes to the
person on every movement it has a sentence for, which is the default the answer names, and nobody -
the person included - can turn it off.

## What a live administrator is told when something arrives

The other message this plugin pushes is about a request that has just come into existence, and it
goes to whoever administers the server rather than to a person this plugin names.

**Nothing on either claimed line reads it, and that is the first thing to know about it.** An
operator who installs this plugin, switches it on and then watches the dashboard sees nothing,
because the dashboard is `jellyfin-web` and it does not subscribe to the name this goes out under.
That is measured in the section below rather than assumed. What this path is for is a client written
against the document, the same way the outbound sink is for a service an operator already runs. What
reaches an operator today is the activity entries above, which are in a page the dashboard already
draws, and the queue, which holds every open request whenever they next open it.

It is off on a fresh install. `TellsAdministratorsAboutArrivals` in the settings is what turns it on,
and off is a supported way to run rather than a degraded one: a path nothing reads is traffic on
every administrator's connection for nobody.

### When it is sent, and when it is not

One document per request that came into existence, on both surfaces an ask arrives over: the endpoint
a person asks through and the seam a sibling plugin hands a want across. A path wired at one of the
two would carry some arrivals while reading as though it carried all of them, which is the same
failure the sink's own switches are written against.

A second person joining a request that is already open sends nothing, and neither does somebody
asking again for their own. A join is another person on a row an operator has already been shown, so
announcing it would put one title in front of them once per person waiting for it.

### What it sends

The same document the outbound sink posts, with `event` set to `Asked`, which is the word the sink's
vocabulary has always carried and nothing has ever sent. So there is one shape leaving this plugin
whichever carrier took it, one version number, and one place a field is added. What it carries and
what it deliberately does not is the sink's own section above, without exception: neither note, no
provider identifiers, nobody else waiting, no history, and no user name of any kind.

`state` on an arrival is `Open` and `movedByUserId` is absent, because nobody has moved it. `at` is
when the request was asked for.

### How it reaches a client, and why nothing acts on it

The server carries one call that addresses administrators rather than named people, and it takes a
name out of an enumeration a plugin cannot add to. The enumeration is closed and the two claimed
lines carry the same members, compared rather than assumed:

    for r in release-10.11.z master; do gh api \
      "repos/jellyfin/jellyfin/contents/MediaBrowser.Model/Session/SessionMessageType.cs?ref=$r" \
      -H "Accept: application/vnd.github.raw" | grep -oE '^\s{8}[A-Za-z]+,' | tr -d ' ,' ; done \
      | sort | uniq -c | awk '$1 != 2'
    (nothing)

Not one of those members means "a plugin has something to say", so a name has to be borrowed, and the
members are not equally safe to borrow. Some are commands a client is expected to obey and the rest
are notices it may read. A document sent under a command name is acted on as a command, which is a
worse outcome than not being read at all. `ActivityLogEntry` is the notice closest in meaning to what
this sends, which is that something happened a view of the server may want to react to:

    gh api "repos/jellyfin/jellyfin/contents/MediaBrowser.Model/Session/SessionMessageType.cs?ref=release-10.11.z" \
      -H "Accept: application/vnd.github.raw" | grep -nE '^\s{8}(ActivityLogEntry|GeneralCommand),'
    12:        GeneralCommand,
    36:        ActivityLogEntry,

The web client subscribes to five names and that is not one of them. Read at `5389bba` in
`jellyfin/jellyfin-web`, which is the same commit the section above reads:

    ref=5389bbad37d178ef5ebeaac8860403527c0e4121
    gh api "repos/jellyfin/jellyfin-web/contents/src/scripts/serverNotifications.js?ref=$ref" \
      -H "Accept: application/vnd.github.raw" | grep -oE 'OutboundWebSocketMessageType\.[A-Za-z]+' | sort -u
    OutboundWebSocketMessageType.GeneralCommand
    OutboundWebSocketMessageType.Play
    OutboundWebSocketMessageType.Playstate
    OutboundWebSocketMessageType.SyncPlayCommand
    OutboundWebSocketMessageType.SyncPlayGroupUpdate

    gh api "repos/jellyfin/jellyfin-web/contents/src/scripts/serverNotifications.js?ref=$ref" \
      -H "Accept: application/vnd.github.raw" | grep -c 'ActivityLogEntry'
    0

Three of the five are commands a client obeys and two belong to sync play, so a document sent under
any of them would be obeyed or discarded rather than read. The name this plugin uses is ignored
there, which is the outcome to prefer of the two available.

**The price of the borrowing, written down rather than discovered.** A client that subscribes to
`ActivityLogEntry` expecting the server's own entry shape is handed this plugin's document instead
and does not recognise it. That is the cost of an enumeration a plugin cannot add to, and it is why
`version` is on the document: a reader can tell what it is holding without guessing from which fields
it found.

### What this is not proof of

One file of one client was read, at one commit, and no client was run. It is not a claim about what
any other Jellyfin client does with the same name, and it is not a claim that anybody saw anything.
Nothing here was run against a server either: the suite asserts what this plugin asked the server to
send, that it addressed the administrators, that nobody was named, and that no other way of reaching
anybody was used, and the headless rule in [testing.md](testing.md) is why it stops there.

### When the push fails

**Nothing that reaches a request.** The request is in the store before anybody is told, telling hands
nothing back for a caller to check, and every way a push can fail costs the same: a line in the
server's log and nothing else. Nothing is retried and nothing is queued. An operator who was not
reached finds the request in the queue, which is where they would look anyway.

## Why there is no fourth

Because the fourth is the first of six. Growing an integration per messaging service is how a plugin
ends up with several, each written for one operator's setup, each holding a token shape and an
endpoint and a retry rule of its own, and each maintained by whoever asked for it until they stop
using it. The outbound sink exists so that adding a service is the operator's configuration rather
than this repository's code.

Two things follow from that and are worth writing down here rather than being rediscovered.

Nothing here reports anything to this project. No path sends anything anywhere by default and none of
them sends anything to the maintainer at all, at any setting. That is not a property of the sink's
design, it is a standing decision recorded on #113, and it has no opt-in.

Nothing leaves the machine until an operator turns it on. The outbound sink is off on a fresh
install by having nowhere to send to, and what an operator narrows it with once it has somewhere is
the section below. The other paths put nothing on a wire out of this machine: the activity log is a
record in the server's own database, and both session messages go down connections that server
already holds to clients. The activity log has no switch and the message to the person who asked has
none either; the message to a live administrator has one, and it is off until somebody sets it.

## Which movements are announced, and which are not

`AnnouncesApprovals`, `AnnouncesDeclines` and `AnnouncesFulfilments` are three settings, each on
until an operator turns it off, and they are read in the sink rather than at the paths that move a
request. A path announces every movement it makes and the sink drops what this install does not
want, so a fourth path added later is covered by the switches without anybody remembering to ask
them.

They are not a second way of expressing off. An install with no address sends nothing whatever they
say, and they are on by default so that an operator who has just typed an address gets what they
turned on rather than silence with three more fields to find.

**An arrival is announced by nothing, and that is a decision rather than an omission.** A request is
made over this plugin's endpoint and also over the seam a sibling plugin hands a want across, so a
switch wired at the endpoint would forward some arrivals and read as though it forwarded all of them.
The vocabulary carries `Asked` because the document's shape is a contract with somebody else's
machine and removing a word from it is a change to that contract; nothing here sends one, and the
sink refuses any movement it has no switch for rather than sending it under a default.

Telling an administrator that something arrived is a message rather than a post off the machine, it
is `TellsAdministratorsAboutArrivals` rather than one of these three, and the section below it is
where what it sends and what reads it are written down. What the person who asked is told when their
own request moves is the section above, and it is not narrowed by these three either.

## What this page does not do

It does not say what a fourth path would be switched with. The three settings above name the three
movements the sink announces, and a setting for a path nothing sends on is a field an operator can
change with no effect.

It does not decide what happens when a path fails. An outbound sink pointed at something that has
stopped answering is #78's and #86's, and nothing here says a failure to notify may move a request.

It carries no list of the events. A list here would drift against the transitions in
[`docs/lifecycle.md`](lifecycle.md), which is where the moves a request can make are written down and
printed from the code.

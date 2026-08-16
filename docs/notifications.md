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
Issue #75 writes it.

**A message to a live session.** An administrator with the dashboard open is told that something
arrived, through the connection the server already holds to that session. It reaches nobody who is
not looking and it leaves nothing behind, which is exactly right for "there is something in the
queue" and exactly wrong for anything that matters after the tab is closed. The server carries it:

    git grep -n "Task SendMessageToAdminSessions" origin/release-10.11.z -- MediaBrowser.Controller/Session/ISessionManager.cs
    origin/release-10.11.z:MediaBrowser.Controller/Session/ISessionManager.cs:196:        Task SendMessageToAdminSessions<T>(SessionMessageType name, T data, CancellationToken cancellationToken);

    git grep -n "Task SendMessageToAdminSessions" origin/master -- MediaBrowser.Controller/Session/ISessionManager.cs
    origin/master:MediaBrowser.Controller/Session/ISessionManager.cs:196:        Task SendMessageToAdminSessions<T>(SessionMessageType name, T data, CancellationToken cancellationToken);

Issue #76 writes it, and telling the person who asked that their own request moved is #77.

**One outbound sink.** An operator who wants a request to reach something outside the server points
this at whatever they already run. It is one sink with a defined payload rather than a service this
plugin knows the name of, so the plugin carries no vocabulary for anybody's product and nothing has
to be updated here when one of them changes. It is built, it is off on every install where nobody
has typed an address, and the section below is what it sends.

## What the sink sends, and what it does not

This is the only thing in this plugin that sends anything off the machine, and it is off until an
operator sets `OutboundNoticeAddress` in the settings. Empty is the whole of how it is off; there is
no second switch, for the reason in [configuration.md](configuration.md).

With an address set, every movement in the queue is posted to it as one JSON document:

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
document meant, and `state` and `kind` are words for the same reason.

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

No path is meant to send anything until an operator turns it on, the activity log excepted because it
is a record rather than a message. The outbound sink holds that today by having nowhere to send to on
a fresh install. Making it a property of every path, switchable per event, is #79, and until that
lands it is a plan for the other paths rather than a property of the code.

## What this page does not do

It does not say what the other two paths send. The activity log's wording is #75's and the session
message's shape is #76's, and each of those is where the text a person actually reads is decided. The
sink's payload is above, because it is the one path that is built.

It does not decide what happens when a path fails. An outbound sink pointed at something that has
stopped answering is #78's and #86's, and nothing here says a failure to notify may move a request.

It carries no list of the events. A list here would drift against the transitions in
[`docs/lifecycle.md`](lifecycle.md), which is where the moves a request can make are written down and
printed from the code.

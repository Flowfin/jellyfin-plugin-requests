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
to be updated here when one of them changes. Issue #78 defines it and its payload.

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
is a record rather than a message. That is #79's condition rather than something this page builds,
and until #79 lands it is a plan and not a property of the code.

## What this page does not do

It does not say what any path sends. The activity log's wording is #75's, the session message's shape
is #76's, and the sink's payload is #78's, and each of those is where the text a person actually
reads is decided.

It does not decide what happens when a path fails. An outbound sink pointed at something that has
stopped answering is #78's and #86's, and nothing here says a failure to notify may move a request.

It carries no list of the events. A list here would drift against the transitions in
[`docs/lifecycle.md`](lifecycle.md), which is where the moves a request can make are written down and
printed from the code.

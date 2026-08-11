# The seam to the discover sibling

## Which document is authoritative

[Flowfin/jellyfin-plugin-discover#94](https://github.com/Flowfin/jellyfin-plugin-discover/issues/94)
is the contract, and it is the only one. It says so itself, it fixes what crosses, and it stays open
until this board points at it. This file is that pointer.

This repository writes no second contract and copies no field list out of that issue. A copy is a
second authority from the moment the first one is edited, and the copy is the one a reader of this
repository would find first, so it would win an argument it has no right to win. What crosses is
read there. What this side owes against it is read here.

That is also why this document names no fields. If you are looking for the field set, follow the
link; if the link is wrong, fix the link rather than writing the fields down here.

## The HTTP API is not the seam

`docs/api.md` describes routes a signed-in caller reaches over HTTP. Nothing in it is the seam, and
there is no route to hand a want over on.

The two differ in every way that matters to somebody deciding which rules apply.

The caller is a different thing. An API caller is a person or a script the server authenticated, and
the request it files is attributed to that session. A seam caller is another plugin in the same
server process, which the server does not authenticate at all because there is nothing to
authenticate. What that costs, and what this side does about it, is #118 rather than this section.

The compatibility rule is a different thing. The API carries a version in its route prefix and is a
promise to callers outside this process, so what may change and when is decided here, in
`docs/api.md`. The seam's shape is decided on the sibling's board, in the contract issue above, and
a version there moves on that board's schedule and not on this one's. Reading the API's version rule
as covering the seam would have this repository promising something it does not control.

The reach is a different thing. Every endpoint sits under a policy and none is anonymous, which is
the whole of what stands between a queue and whoever asks for it. The seam has no policy because it
has no session. Neither statement is true of the other surface.

So a change to `docs/api.md` is not a change to the seam, and a change to the contract is not a
change to the API. Somebody who conflates them will either look for a route that does not exist or
apply the API's rules to a call that has none of them.

## Both sides watch the library, and neither tells the other

This plugin decides that a request is fulfilled by watching the server's library for the thing
arriving, in #42. The sibling watches the same library for its own reasons, on its own schedule.
Neither one notifies the other when it sees something.

That is deliberate and it is what keeps the handover one way. One message crosses, when a user asks
for something on a surface this plugin does not own. Nothing crosses back to say the thing turned
up, because the side that cares can see it for itself, and a notification that is not needed is a
second thing to keep in step and a second thing to be wrong.

The consequence worth stating is that an arrival is observed twice on a server running both
plugins. That is not duplicated work anybody has to remove: each side is watching for a different
reason, one to move a request out of the queue and one for whatever that board decides, and neither
observation depends on the other having happened.

## The catalogue is the sibling's, and this side holds a snapshot

Both plugins hold something that looks like a title, and left unsaid that grows into two
catalogues, two refresh schedules and two sets of source terms to comply with.

The catalogue is the sibling's, and its board says so: the source adapters, the fetched records,
when they expire, and the surface every client browses. What this plugin holds is a title and a
year per request, taken at the moment somebody asked for it and never refreshed from anywhere. That
is not a small catalogue. It is a different thing, a record of what was asked for as it appeared
when it was asked for.

Two consequences, and both are the point rather than a cost of it.

**A snapshot goes stale, and that is correct.** A film renamed upstream afterwards still reads in
the queue and in the history as the name the person actually asked for. A queue that silently
followed the rename would be a record of what a source says today rather than of what somebody
wanted.

**This plugin calls no metadata source at all.** It has nothing to ask and nothing to ask with, so
the queue renders on a server where nothing outbound resolves, and the terms a source imposes on
whoever fetches from it never reach this repository.

The second consequence is refused rather than written down here. `no-call-to-a-metadata-source` in
`tools/opengrep/rules.yaml` refuses the server's provider interfaces and the addresses of the
sources a plugin would otherwise call directly, anywhere under `Jellyfin.Plugin.Requests/`, and it
carries the fixture it is watched refusing. What it deliberately does not refuse is an outbound call
in general: the notification sink in #78 and the bridge in #82 are outbound by design, and a rule
that had to be narrowed to let those land is a rule nobody trusts afterwards.

`CatalogueSplitTests` holds the rendering half. The queue is rendered from a store holding one
request and the rows carry the stored title and year, and the same test reads the controller's
dependencies and refuses a fifth one, because a fetch would arrive as something injected. What the
suite cannot say is that a socket was blocked while it ran; what it says instead is that the
assembly references nothing that could open one, which is the exact reference list in
`SiblingIndependenceTests` and fails on any addition.

## What this document does not yet hold

Named here so the absence is read as absence rather than as a decision nobody wrote down. Each is
the closing condition of the issue beside it.

- What happens to a field set carrying a contract version this plugin does not know. #90.
- The trust position of an in-process handover, and what this side checks before it believes a user
  identifier it cannot verify. #118.
- How each side finds the other. #89.
- What an undone gesture does, which today is nothing, because the contract carries no message for
  it. #68.

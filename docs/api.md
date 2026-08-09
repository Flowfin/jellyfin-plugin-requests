# The API

## The prefix

Every endpoint this plugin serves sits under one prefix, version segment included:

    MediaRequests/v1

It is mounted on the server's own API, so a caller reaches it at the server's address and nothing
about it is on a port or a host of this plugin's own.

`MediaRequests` rather than `Requests`. A plugin controller sits beside the server's routes and
beside every other plugin's, and `Requests` on its own is a generic noun in an API whose neighbouring
segments are `Items`, `Users` and `Sessions`. Taking it is a bet that neither the server nor any
other plugin ever wants it. The server's route table was not enumerated to check, so the compound
noun lowers the chance of a collision rather than proving there is none. It costs nothing today, and
a rename later is exactly the breaking change the version segment exists to make survivable.

The prefix is declared once, as `RequestsControllerBase.RoutePrefix`, and every controller inherits
it. `RoutePrefix` is built from `VersionSegment` rather than written out again, so the path and the
version cannot come to say different numbers.

## The version rule

The version is a whole number in the path. It is not a header and not a query parameter: a caller
that has to set a header to get the shape it expects is a caller that gets some other shape the first
time somebody forgets, and a version in a path is visible in a log, in a browser and in the script an
operator wrote.

**A breaking change ships as a new version segment beside the old one, never as an edit to the
existing one.** `v2` appears, `v1` keeps answering as it did, and callers move when they move. What
`v1` may never do is change under a caller who did not ask for it.

Nothing here says how long an old version is kept. That is a decision for the release the first one
is retired in, and it needs an install base to be decided against.

### What counts as breaking

A change is breaking when a caller written against the current version, and behaving correctly, can
start getting an answer it cannot use.

- Removing an endpoint, or moving one to a different path.
- Removing a field from a response, or renaming one.
- Changing the type of a field, or narrowing what a field accepts.
- Adding a required field to a request body, or making an optional one required.
- Changing which status code an outcome is reported with, where a caller branches on it.
- Changing what a value means while keeping its name.

Two examples of the same size, one on each side of the line.

**Breaking.** `state` on a request is renamed to `status`. Every caller reading the field gets
nothing, and nothing tells them why: the response still parses, the field they want is simply absent.
This ships as `v2`.

**Not breaking.** A `note` field is added to the response for a request. A caller that does not know
about it ignores it, and a caller that wants it asks for the same endpoint and finds it there. This
ships inside `v1`.

### What is not breaking, and is written down here because it looks like it is

**A new value in an enumerated field.** `state` grew a fifth value when a failed state was decided,
and a sixth is possible. This is not breaking, and the reason it is not is a promise made here rather
than a fact about the change: **a caller must treat a value it does not recognise as one it does not
recognise**, and must not fail, and must not map it onto a value it does know. A caller that switches
exhaustively over the values that existed the day it was written is a caller this contract does not
promise anything to.

That rule is stated now, in the first version, because stating it later would itself be the breaking
change. A contract that starts by promising a closed set cannot open it afterwards without a new
version, and every enumerated field in this API would be frozen for as long as `v1` answers.

**A new optional field in a request body**, and **a new endpoint under the same prefix**. Neither
changes anything a caller already sends or already reads.

## What holds the prefix

`every-endpoint-sits-under-the-versioned-prefix`, in `tools/opengrep/rules.yaml`, refuses the two
ways out of it:

- a second route attribute anywhere in the plugin, which replaces the inherited prefix for that
  controller;
- a method template beginning with a slash, which is the quieter of the two. It reads as a relative
  path with a stray character and ASP.NET treats it as an absolute route, discarding the controller's
  prefix entirely.

The one file excluded from the rule is the one that declares the prefix. The rule is watched refusing
both spellings on a fixture, in `tools/opengrep/fixtures/route/`, so a pattern that stopped matching
reds the gate rather than passing quietly:

    ./tools/opengrep/prove-rules-fire.sh

`EveryControllerInheritsTheVersionedPrefix` in the suite is the other half, and it is the half that
reads the built assembly rather than the source text: every controller type in the plugin derives
from `RequestsControllerBase` and declares no route of its own. A controller written in a file the
rule's path list does not reach would pass the lint and fail there.

## Asking for something

    POST MediaRequests/v1/Requests

The body carries the kind, the title, the release year where there is one, the external identifiers
that name the thing, the seasons where it is a series, and a note. **It carries no requester and
there is no field for one.** Who asked is the authenticated caller, read from the server's own answer
to who is calling, so filing a request as somebody else is not something this endpoint declines to
do: it is something the shape has no way to express.

Asking for something already in the queue joins it rather than making a second one, and the answer
says which happened. `Created` is a new request, `Joined` is somebody else's request the caller is now
waiting for too, and `AlreadyWaiting` is the caller's own, which writes nothing. A new request
answers `201`, and both of the others answer `200`, because nothing was created.

There is no `Location` header on the `201`. Nothing reads one request back yet, so the header would
point at something that answers `404`, and a header that lies is worse than one that is absent.
Adding it when that endpoint exists is not a breaking change under the rule above.

What may be joined is a question about state rather than about identity, and it is answered here
rather than in the identity comparison: only a request that is still open or approved. A declined
request is an answer somebody gave, and joining it would make a new asker inherit a refusal they
never saw; a fulfilled one is finished; a failed one has been given up on. In each of those a new ask
is a new request, which is also what puts it back in front of an operator.

A body that cannot become a request is refused with `400` and a shape naming the field that is wrong,
so a client can put the message beside the box somebody typed in rather than having to read English
to work out which one. That shape is the smallest thing that carries it and is expected to be
replaced by whatever #56 decides.

## What is not decided here

- Which policy each endpoint carries, and what a user may see of somebody else's request, is #51.
- The shape of an error and which status code each failure gets is #56.
- What each endpoint does is #52, #53, #54 and #55.
- Whether these endpoints appear in the server's published API documentation is #57.

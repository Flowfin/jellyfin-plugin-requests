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

## Reading your own requests

    GET MediaRequests/v1/Requests

Everything the caller is waiting for: the requests they asked for and the ones they joined.

**Nothing else can come back from it, whatever is asked for.** The narrowing is the read rather than
a filter over a wider one. The store is asked for this person's requests through its own lookup, and
the filter, the order and the page are applied to what that returns, so there is no parameter that
widens it and nothing wider for one to widen to.

The rows carry no identifier of any person, the caller included. A request somebody else asked for
and the caller joined would otherwise name the first asker and everybody else waiting alongside them,
and a count of how many people are waiting is the same disclosure made smaller, so there is none of
that either. `AskedByYou` says whether the caller asked first, which is a fact about the caller. A
note is the writing of whoever asked, so it comes back only on a request the caller asked for
themselves.

The history is not in the answer. Every entry names the administrator who made the move, and a list a
user reads is not an audit trail.

## Reading the queue

    GET MediaRequests/v1/Requests/Queue

Every request on the server, for an administrator deciding on them. It carries the server's elevation
policy on top of the controller's, so it is reachable by the people who can already administer the
server and by nobody else.

Each row carries the revision the store has the request at, because the next thing an operator does
is move it and the store refuses a write made against a revision that has moved underneath it.

What a queue must show for a decision to be possible is #59, so this is what the queue can show
rather than a settled answer to what it should.

## The parameters both reads take

| Parameter    | Meaning                                            | Default       |
| ------------ | -------------------------------------------------- | ------------- |
| `state`      | Which states to show. Repeatable. None means all.  | all           |
| `kind`       | Which kinds to show. Repeatable. None means all.   | all           |
| `order`      | `RequestedAt`, `StateChangedAt` or `DisplayTitle`. | `RequestedAt` |
| `descending` | Whether that order runs the other way.             | `false`       |
| `skip`       | How many matches to step over.                     | `0`           |
| `take`       | How many rows the page holds at most.              | `50`          |

The answer carries the rows, how many matched the filter before the page was taken, and the slice
that was asked for. The count is what a pager is drawn from: a page and a count taken from two reads
can disagree, and a surface saying "1 to 50 of 49" is one an operator stops trusting for the rest of
the numbers on the screen.

`take` is capped at 200 and a larger one is **refused** rather than answered with fewer. A caller that
asked for a thousand, was given two hundred and was not told has just decided it has seen everything.

A value outside an enumeration is refused with the parameter named. Such a value binds as a number
and would otherwise match nothing, and an empty page reads exactly like an empty queue.

## Who may reach what

Every endpoint carries a policy of its own, and the controller carries one as the floor under all of
them. An endpoint with no policy of its own is reachable under whatever its class happens to declare
on the day it is added, and a class attribute is edited by somebody who is not reading the endpoint.

| Endpoint             | Policy                 | Who that is          |
| -------------------- | ---------------------- | -------------------- |
| `POST Requests`      | `DefaultAuthorization` | any signed-in person |
| `GET Requests`       | `DefaultAuthorization` | any signed-in person |
| `GET Requests/Queue` | `RequiresElevation`    | an administrator     |

**Nothing here is anonymous.** A request has to be attributable to somebody to exist at all, and a
queue is a list of who asked for what, so there is no answer this plugin gives that is safe to hand a
caller the server has not authenticated.

The policies are the server's own, named as literals because the constants that hold them live in the
server's web assembly and a plugin does not reference it. The string is the contract either way.

What holds this. `EndpointPolicyTests` reads the built assembly and refuses an endpoint whose policy
is not the one written down for it, an endpoint carrying no policy of its own, and an anonymous one.
The invariant lint refuses the two source shapes that take a policy away: `no-anonymous-endpoint` and
`authorize-names-a-policy`, the second of which is about a bare `[Authorize]`, which reads as though
it decided something and admits every signed-in person on the server.

What none of that holds is the server turning a caller away, which is the server's own evaluation of
the policy and needs a running one. `docs/testing.md` carries that as a refused test with what
replaces it.

The rule underneath the table is narrower than the table. **A user sees their own requests in full
and learns nothing at all about anybody else's**, which is what `GET Requests` returns and why its
rows carry no identifier of any person. Whether a user may ever be told that a title has already been
asked for, which is a weaker disclosure than a row and still a disclosure, is open between #51 and
#71 and is deliberately not taken here: nothing this plugin serves aggregates across people today.

## What is not decided here

- Whether a user may ever be told that a title has already been asked for is open between #51 and #71.
- The shape of an error and which status code each failure gets is #56.
- What each endpoint does is #52, #54 and #55.
- Whether these endpoints appear in the server's published API documentation is #57.

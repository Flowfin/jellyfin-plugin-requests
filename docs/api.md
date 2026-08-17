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

## The published document

The server generates one API document from every controller it has loaded, this plugin's included,
and serves it beside its own endpoints:

    api-docs/openapi.json

with `api-docs/swagger` and `api-docs/redoc` as the two browsable forms of the same thing. Those
paths are the server's rather than this plugin's, and they are the same on both claimed lines:

    gh api repos/jellyfin/jellyfin/contents/Jellyfin.Server/Extensions/ApiApplicationBuilderExtensions.cs?ref=v10.11.0 --jq .content | base64 -d | grep -n "RouteTemplate\|RoutePrefix"
    39:                    c.RouteTemplate = "{documentName}/openapi.json";
    50:                    c.RoutePrefix = "api-docs/swagger";
    57:                    c.RoutePrefix = "api-docs/redoc";

**That document is the reference, and this one is the reasons.** Every path, every parameter and
where it is read from, and the shape and status code of every answer are in it, generated from the
endpoints rather than typed by somebody. What is written here instead is why each of them is the way
it is, which is the half a generator cannot produce. Where the two disagree the document is right and
this file is stale, which is the whole reason for not restating it.

### Every failure carries this API's own shape

A response type that names no shape is not an undocumented failure. Under `[ApiController]` the
framework fills one in, and what it fills in is `ProblemDetails`, which nothing here returns. A client
generated from a document saying that parses every refusal into the wrong type and finds out at the
first refusal rather than at the first call. So every status at or above 400 is published as
`RequestFailure`, which is the shape the table above describes.

### What holds it

`PublishedApiDocumentTests` reads the description set a generator is given and holds four things: the
operations are exactly the ones written down, with their parameters and their answers; every endpoint
the assembly carries is in that set, so one hidden from the document and still reachable reds; every
failure is published as `RequestFailure`; and the status codes published for an endpoint are the ones
it answers with, taken from calls that produce them rather than from a list, in both directions.

**What that is not.** It is the input a document is generated from and not the document, derived from
the plugin assembly by itself. Two things sit between it and what a caller fetches: whether the server
loads these controllers into its own application parts, which is the subject of the recorded first-load
run in `docs/testing.md`, and what its generator writes from them. No route in this tree fetches a
generated document from a running server, and nothing here claims one.

### The summaries do not reach it, and this is why

Every endpoint here carries an XML summary and every parameter has one, and none of them is expected
to reach the generated document. That is read off the server's own source rather than out of a
document, because no route in this tree fetches one, so it is where the prose is taken from rather
than an observation of a document without it:

    gh api repos/jellyfin/jellyfin/contents/Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs --jq .content | base64 -d | sed -n '221,230p'
                    // Add all xml doc files to swagger generator.
                    var xmlFiles = Directory.EnumerateFiles(
                        AppContext.BaseDirectory,
                        "*.xml",
                        SearchOption.TopDirectoryOnly);

                    foreach (var xmlFile in xmlFiles)
                    {
                        c.IncludeXmlComments(xmlFile);
                    }

The generator is handed the XML files sitting in the server's own directory, and a plugin's assembly
is not in that directory. So the prose a caller gets about these endpoints is what is written here,
and the document gives them the shapes. That is a limit of the route rather than a decision, and it is
written down so nobody spends an afternoon wondering why a summary they wrote is not showing up.

## Asking what this install allows

    GET MediaRequests/v1/Capabilities

The endpoint something calls before it calls anything else. Without it a caller learns what this
plugin allows by calling and reading the refusals, and a caller that has to tell a `404` for "no such
plugin" from a `404` for "no such request" is a caller that gets it wrong.

Four facts and no more. `apiVersion` is the segment the routes sit under, so a caller that finds a
version it does not know stops instead of guessing at the shape. `acceptedKinds` is what an operator
has switched on, so nothing offers a button for a kind this server refuses. `automaticApproval` says
whether anybody will look at a request, because "an administrator will look at this" is the wrong
thing to tell somebody on a server where nobody will. `bridgeConfigured` says whether an external
request service sits behind the plugin.

**It carries no credential, no address and nothing about any other person.** Whether a bridge exists
is the whole of what it says about one: not which service, not where it is, and not whether it
answered when it was last asked. The first two are the operator's business and the third is the state
of a system the caller does not administer. `CapabilityEndpointTests` holds the shape to those four
fields, so a fifth is a red suite rather than a review somebody might not run.

It answers on a fresh install, because every one of those facts has an answer before anybody has
configured anything. It reads no store, so nothing it says depends on the queue being readable, and
it publishes one status code where the other endpoints publish five.

**This is not the seam.** The sibling discover plugin runs in the same server process and finds this
one through the server's container, so it never calls this. `docs/seam.md` is where that difference
is argued.

## Asking for the words a page draws

    GET MediaRequests/v1/Strings

The catalogue behind every word this plugin's pages show, as a flat object of keys and strings. One
parameter, `culture`, naming what the caller wants, such as `de-DE`. With none, the answer is
English.

**It exists because nothing can put the words in on the way out.** The dashboard serves a plugin's
pages itself, out of the assembly's resources under the name the plugin registers, so this plugin
never sees that request and has no moment at which it could substitute anything. The markup
therefore ships with keys and the words arrive here.

The answer is complete whatever the asked-for culture has been translated to, because English is
merged underneath it before it is sent. A page never has to know that a fallback rule exists, and a
half-translated language shows translated words where it has them and English everywhere else,
rather than a key.

**A culture nothing recognises is answered rather than refused.** A caller asking for words wants
words, and the catalogue already falls back per key; an unrecognised name is that same rule with
nothing matching at any step. So this endpoint publishes one status code and no failure shape.

It says nothing about anybody. An administrator's queue and a person's own list get the identical
answer to it, which is why it carries the server's default policy rather than elevation: the page a
browser opens needs it, and a catalogue of English sentences is not a fact about anybody's server.
Nothing anonymous, for the reason nothing else here is.

`Accept-Language` is deliberately not read. What a browser sends in that header is not what a person
changed when they changed the language in Jellyfin, and reading it pulls three assemblies into this
plugin that nothing else here uses, which `ThePluginReferencesExactlyTheAssembliesWrittenDown` in the
suite would refuse. The pages pass `navigator.language`.

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

A body that cannot become a request is refused with `400` and the field that is wrong is named, so a
client can put the message beside the box somebody typed in rather than having to read English to
work out which one. The shape is the one every failure of this API comes back in, below.

**How many things one person may be waiting for is bounded, and this is where the bound is applied.**
Somebody already waiting for as many open or approved requests as the install allows is refused with
`409` and `TheyAreAtTheirQuota`, carrying how many they hold and what the limit is. It is a `409`
rather than a `403` because nothing about the call is wrong and the same call works as soon as one of
the things they asked for is answered. Joining somebody else's request counts, because it is one more
thing they are waiting for; asking again for something they are already on writes nothing and is
therefore never refused for this. A finished request frees the place, so the setting is a bound on
what is open rather than a lifetime allowance. Which requests count and what the limit is are
`OpenRequestsPerUser` in [configuration.md](configuration.md).

Reading the setting is also a way this call can fail. Where the stored configuration is something the
plugin cannot run on, the install is refused on the read rather than corrected, so the ask answers
`503` and `ThisInstallCannotRun` and nothing is written. That is a fault on the server, the log says
which setting, and the message here does not.

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

## The page a browser opens

    GET MediaRequests/v1/Page

The one endpoint here that answers with a document rather than a record. It returns the page a person
opens to see what they asked for, which exists because a plugin's own pages live in the dashboard and
the dashboard is the administrator's. What the page is and what it deliberately does not do are in
[surface.md](surface.md); this section is what a caller sees.

It answers `text/html` and nothing else, and the document carries no request in itself: what it holds
is markup and a script that calls `GET MediaRequests/v1/Requests` for whoever is reading it. So who
may see what is decided by that endpoint and not a second time here.

**It is refused rather than served empty to a caller with no session.** A shell handed to anybody and
left to fail on its first call would put this plugin's existence and shape in front of somebody who
has not signed in, and would read to a person as a broken page rather than as a closed door.

**A browser navigating to an address sends no Jellyfin session.** A session here is a header or a
query value, never a cookie, so a person opening this page in a tab reaches it with the credential in
the address: `?api_key=` is read out of the query string by the server on both claimed lines. That is
a real cost and it is stated here and in [surface.md](surface.md) rather than left to be discovered.
This endpoint neither creates such a credential nor extends one, and the page carries what it was
opened with no further than the one call it makes.

## Reading the queue

    GET MediaRequests/v1/Requests/Queue

Every request on the server, for an administrator deciding on them. It carries the server's elevation
policy on top of the controller's, so it is reachable by the people who can already administer the
server and by nobody else.

Each row carries the revision the store has the request at, because the next thing an operator does
is move it and the store refuses a write made against a revision that has moved underneath it.

What a queue must show for a decision to be possible is [queue.md](queue.md), and every item on that
list is on this answer. Two of them are not properties of the request and arrive under `Context`:
what was already decided about the same work, and how many requests the person asking is waiting for.
Both are worked out from the whole store, which is why they are a shape of their own rather than
fields beside the title.

`Context` is absent on every other answer that carries a row. Approving or declining hands one
request back so a page can redraw what it just changed, and reading the whole store to answer a
question nobody asked would be a read of everything per decision. An empty context and a context
nobody built are different statements, so the field is absent rather than empty: a page reading the
second as the first would tell an operator that nothing has ever been decided about a title on a
route that never looked.

**`Context.EarlierDecisions` is bounded by the store, and the store is bounded by the retention
period.** `RetentionSweep` removes a finished request once it has been finished for longer than
`FinishedRequestRetentionDays`, so this is every decision inside that period rather than every
decision ever made about that work.

**Nothing under `Context` says who.** An earlier decision carries the answer, when it was made, the
seasons it covered and the reason, and never the person who asked for it or the person who decided
it. The count is a number and not a list. What a user may learn about another user's request is
unchanged by any of it and is still nothing: this endpoint is administrators only.

## Asking whether the plugin is working

    GET MediaRequests/v1/Health

The few facts that separate a broken plugin from a quiet week: how many requests are in each state,
when the store last accepted a write, what the last full sweep of the library did, and whether an
external request service is configured and answering. It carries the same elevation as the queue,
and for the same reason: a count of requests is a disclosure about other people's requests, and what
one person learns about another's is nothing.

**It answers `200` on a broken install and publishes no failure shape.** A store that cannot be read
is a field on the answer, `StoreReadable`, with every count left at zero. An endpoint that refused
here would be silent at exactly the moment somebody is reading it to find out what is wrong, and an
empty queue and an unreadable one produce the same numbers while being opposite answers.

**Every state is counted, including the ones nothing is in.** A caller drawing only what the store
held would show a shorter list on a quieter server, and a reader comparing two days would be
comparing two different tables.

**Every moment on this answer is about the server process and not about the install.** Nothing here
is persisted, so a server restarted a minute ago answers that it has swept nothing and written
nothing. That is true of the process and is not a claim that the plugin has never done either, and
anything drawing it has to say so in those words. Persisting them would mean this plugin writing a
file to record when it last read one, and when somebody else's system last answered.

**`BridgeLastReachableAt` advances when something asks, and nothing asks on its own.** There is no
timer behind it: it records what a caller already had to find out, which today is this endpoint. On
an install where nobody reads this, it stays where it was. What it says is when this plugin last had
evidence, never when the other system stopped working.

**Nothing on this answer is a credential, a path, or anything about a person.** It is counts,
moments, one switch and one enumeration, which is a property of the shape rather than of a filter:
there is no field a secret or a file name could arrive in, and `NothingOnThisAnswerCouldCarryA`
`CredentialOrAPath` reds on the day one is added.

## Deciding on one

    POST MediaRequests/v1/Requests/{id}/Approve
    POST MediaRequests/v1/Requests/{id}/Decline

One endpoint per operation rather than one taking a target state. The path says in a log what was
done, a caller cannot ask for one move and get another, and the decline reason is a required field on
exactly the operation that needs it rather than a field that is required when another field holds a
particular value.

There are two of them and not four. Marking something fulfilled is not here because the table makes
that the plugin's own move, on something it observed in the library, and a person marking it would be
making the state say something about the library that the library does not say; #42 is where it is
detected instead. Cancelling is not here because there is no state to cancel into, refused on #113.

**The move is `RequestLifecycle`'s and this API decides none of it.** Which states can be approved or
declined from, who may make each move, and the single history entry a move appends are the model's,
so the endpoint cannot be the surface that knows one rule fewer than the page or the bridge does.

Both bodies carry the revision the caller read the request at, from the queue row. It is required,
and a body without one is refused rather than read as a revision nobody sent: two administrators with
the queue open will decide the same request in the same minute, and a write against whatever the
store holds by the time it arrives is exactly the decision silently lost.

A decline carries a reason from a short list, decided on #113, and `Other` requires the note beside
it. A decline with no reason reads as arbitrary to the person who asked, and what they do next is ask
for the same title again.

### What comes back

A move that was made answers `200` with the queue row at its new revision, so a page can redraw that
row without reading the queue again. A move that was refused answers with the failure shape below,
and on the three codes about a request that is there it carries the row as the store holds it now, so
the operator decides again against what is actually there rather than reading the queue a second
time.

`MovedSinceItWasRead` is answered for a request that moved into a state the move is illegal from,
rather than `TheTableRefusesTheMove`. Both are refusals and they tell an operator different things:
one says somebody else got there first and here is what they did, the other says this request can
never be approved. The second would be false.

## Deciding on several

    POST MediaRequests/v1/Requests/Approve
    POST MediaRequests/v1/Requests/Decline

The same two operations over a selection rather than over one request. An operator who has been away
comes back to forty requests, and one call per request is why people stop working a queue.

Each entry carries its own revision, because the rows were read at whatever revision each one was at
and one of them can move while the others do not. A decline carries one reason and one note for the
whole action: the gesture is answering a batch the same way, and an operator who wants to say
something different about one request makes that one decision on its own.

An action carries at most 200 requests, which is the page they are selected from. More is **refused**
rather than acted on as far as the cap, for the reason a large `take` is refused rather than
answered with fewer.

### What comes back

`200`, with one entry per request, in the order they were sent. Every request that was sent has an
entry. Each entry carries the row at its new revision, or the failure that request got, and never
both.

    {
      "requests": [
        { "id": "...", "request": { "state": "Approved", "revision": 4 } },
        { "id": "...", "failure": { "code": "MovedSinceItWasRead", "current": { ... } } }
      ]
    }

**A refusal about one request is in that request's entry rather than in the status of the call.** By
the time one of them is refused another may already be written, so a call answering with a failure
would be saying nothing happened while something had. The successful ones stay done and are not
rolled back: a decision an operator made is not something this API takes away because the next row
in the list had moved.

So a client cannot read the status code alone. `200` here means the action was carried out, not that
every request in it moved, and a surface that draws it as done without reading the entries is the
failure this shape exists against.

What stays a refusal of the whole call is what is decided before anything is written: a body that
cannot be read, and a call that names no person. Both answer exactly as they do on a single
decision, and neither leaves anything written.

A body is refused whole rather than in part. An entry naming no request, an entry with no revision,
an empty selection and the same request twice are all `InvalidBody` with the position named, and
nothing in the action is attempted. The last of those would otherwise report that a request had
moved since it was read, against a move the same call had just made.

There is no status code on an entry. A status code is an answer to a call and several entries come
back under one; the failure carries the code, and the table below is where a code and a status are
paired, once.

## When a call fails

Every failure of this API comes back in one shape, whichever endpoint it came from:

    {
      "code": "MovedSinceItWasRead",
      "message": "This request has moved since it was read, ...",
      "field": null,
      "current": { ... }
    }

`code` is what a client branches on. `message` is the sentence for the person who will read it.
`field` names the part of the body that is wrong and is present only on `InvalidBody`. `current` is
the request as the store holds it and is present only where the failure is about a request that is
there and the caller is one who may read it in full.

One shape rather than one per endpoint. A client writes the handling once, and a failure it has never
seen before still parses and still says which class it is. What that costs is two fields that are
absent most of the time, which is cheaper than a client reading five shapes, four of them from an
example rather than from a contract.

| Code                          | Status | What happened                                                 |
| ----------------------------- | ------ | ------------------------------------------------------------- |
| `InvalidBody`                 | `400`  | The body cannot become what it is for. `field` says where.    |
| `NoUserOnTheCall`             | `403`  | Authenticated, and no person behind it.                       |
| `NoSuchRequest`               | `404`  | The store holds no request with that identifier.              |
| `MovedSinceItWasRead`         | `409`  | Somebody moved it between the read and this call.             |
| `TheTableRefusesTheMove`      | `409`  | The transition table has no such move from where it is.       |
| `TheRequestNamesNothing`      | `409`  | It carries no identifier, so only a decline is available.     |
| `TheCallerMayNotMakeThisMove` | `403`  | The table allows the move and does not admit this caller.     |
| `TheStoreCouldNotBeRead`      | `503`  | The queue could not be read. Nothing was changed.             |
| `TheyAreAtTheirQuota`         | `409`  | They are waiting for as many requests as this install allows. |
| `ThisInstallCannotRun`        | `503`  | The settings are something the plugin cannot run on.          |

Each code has exactly one status code, decided in one place, so a client may branch on either and
never find them disagreeing. A client that does not know a code falls back on the status, which is
why every code has one that is right on its own.

`NoUserOnTheCall` is `403` rather than `400`. Nothing the caller puts in the body changes it: the
call authenticated and names no person, which is what an API key looks like from an endpoint, and a
request has to be attributable to somebody to exist at all.

`TheStoreCouldNotBeRead` is `503` rather than `500`. A store that cannot be read is usually a disk or
a file rather than this plugin being broken, and telling an operator to try again is the true
statement. Nothing was changed when it is answered.

`ThisInstallCannotRun` is `503` for the same reason and is the same answer the seam gives a sibling
plugin for the same cause. The call was fine, this server is set to something that cannot be honoured,
and an operator changing one field makes the identical call work.

**Nothing in a message names a person, a path on the server disk, or an exception.** The three are
one rule with three shapes: a user identifier tells a caller about somebody else, a path tells
anybody who can reach an endpoint how the server is laid out, and an exception is the plugin
describing its own internals to whoever asked. `ErrorSurfaceTests` walks every failure a call can
produce and refuses all three, and the store failure is the one it exists for, because the exception
behind it names the file it could not read.

`current` is the one field that carries anything about people, and it comes back only from the
endpoints an administrator reaches. The same test holds that.

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

Every endpoint carries an authorisation attribute of its own, and the controller carries one as the
floor under all of them. An endpoint with none of its own is reachable under whatever its class
happens to declare on the day it is added, and a class attribute is edited by somebody who is not
reading the endpoint.

| Endpoint                     | Policy                       | Who that is          |
| ---------------------------- | ---------------------------- | -------------------- |
| `GET Capabilities`           | the server's default         | any signed-in person |
| `GET Page`                   | the server's default         | any signed-in person |
| `GET Strings`                | the server's default         | any signed-in person |
| `POST Requests`              | the server's default         | any signed-in person |
| `GET Requests`               | the server's default         | any signed-in person |
| `GET Requests/Queue`         | `Policies.RequiresElevation` | an administrator     |
| `POST Requests/{id}/Approve` | `Policies.RequiresElevation` | an administrator     |
| `POST Requests/{id}/Decline` | `Policies.RequiresElevation` | an administrator     |
| `POST Requests/Approve`      | `Policies.RequiresElevation` | an administrator     |
| `POST Requests/Decline`      | `Policies.RequiresElevation` | an administrator     |

The four decisions carry the same policy as the queue, and that is the endpoint agreeing with the
model rather than deciding anything: every cell of the table these two can reach admits an
administrator and nobody else, so an endpoint open to any signed-in person would refuse each such
call one layer down. A permission answered in two places is a permission that comes to be answered
two ways, and `TheOnlyCallerTheseEndpointsBuildIsAdmittedByEveryMoveTheyCanMake` in the suite is what
reds if the table stops agreeing.

**Nothing here is anonymous.** A request has to be attributable to somebody to exist at all, and a
queue is a list of who asked for what, so there is no answer this plugin gives that is safe to hand a
caller the server has not authenticated.

### The name in the middle column, and the defect it is the repair for

The policies are the server's own and the name comes from the server's own constant, `Policies` in
`MediaBrowser.Common`, which this plugin already references. There is no registered name for "any
signed-in person": the server builds that requirement into its unnamed default policy, so the three
endpoints open to anybody with a session carry `[Authorize]` with nothing after it, which is what the
server's own controllers carry for the same thing.

This table said `DefaultAuthorization` in those three rows and the controllers carried it as a
string, and **every endpoint this plugin serves answered 500 to every caller, on both claimed
lines**. A policy name the server does not register does not admit fewer people; it throws inside the
authorisation middleware before the endpoint is reached. The run that measured it is in the pull
request for #58, on `jellyfin/jellyfin:10.11.11` and on `jellyfin/jellyfin:12.0-rc4`:

    [ERR] Jellyfin.Api.Middleware.ExceptionMiddleware: Error processing request. URL GET /MediaRequests/v1/Requests/Queue.
    System.InvalidOperationException: The AuthorizationPolicy named: 'DefaultAuthorization' was not found.

That the name is absent from the server assembly of each line is readable without a server, from the
package each target compiles against:

    tr -d '\000' < ~/.nuget/packages/jellyfin.common/10.11.11/lib/net9.0/MediaBrowser.Common.dll \
        | grep -oE 'DefaultAuthorization|RequiresElevation' | sort | uniq -c
          3 RequiresElevation
    tr -d '\000' < ~/.nuget/packages/jellyfin.common/12.0.0-rc4/lib/net10.0/MediaBrowser.Common.dll \
        | grep -oE 'DefaultAuthorization|RequiresElevation' | sort | uniq -c
          3 RequiresElevation

So the string is where the defect lived, rather than the particular name that was in it: a name
written here is a name nothing checks, and one taken from the constant is a compile error the day it
stops existing.

What holds this. `EndpointPolicyTests` reads the built assembly and refuses an endpoint whose policy
is not the one written down for it, an endpoint carrying no attribute of its own, and an anonymous
one. The invariant lint refuses the two source shapes that take a policy away:
`no-anonymous-endpoint`, and `policy-is-named-by-the-servers-own-constant`, which is about a policy
written as a string.

What is weaker than before, said rather than left to be discovered: an endpoint carrying the default
policy and an endpoint whose author meant to name one and did not are the same bytes, so no check
here tells them apart. What is checked is that the attribute is there and that the policy is the one
this table names, the default included.

What none of that holds is the server turning a caller away, which is the server's own evaluation of
the policy and needs a running one. `docs/testing.md` carries that as a refused test with what
replaces it. **That applies to the repair above as well: no run on a server records these endpoints
answering anything other than 500.** What is measured is that the name they used exists on neither
line and that the name they use now exists on both. The first-load procedure in `docs/testing.md` is
where a run against a server would go, and it has not been made since this changed.

The rule underneath the table is narrower than the table. **A user sees their own requests in full
and learns nothing at all about anybody else's**, which is what `GET Requests` returns and why its
rows carry no identifier of any person. Whether a user may ever be told that a title has already been
asked for, which is a weaker disclosure than a row and still a disclosure, is open between #51 and
#71 and is deliberately not taken here: nothing this plugin serves aggregates across people today.

## What is not decided here

- Whether a user may ever be told that a title has already been asked for is open between #51 and #71.
- The capability endpoint is #55.
- Which of these a page calls, and what an operator sees while an action on several is running, is
  the administrator surface rather than this document. The endpoints promise what is written above
  and nothing about a screen.

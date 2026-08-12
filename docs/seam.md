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

## How each side finds the other

Neither side goes looking. The sibling declares the contract, asks the server's container for every
implementation of it, and treats none at all as a complete and supported state rather than an error.
This side's whole job is to be one of those implementations, put there when this plugin loads:

    git grep -n 'AddSingleton<IWantHandover>' -- Jellyfin.Plugin.Requests/PluginServiceRegistrator.cs

That is the registration Jellyfin gives a plugin for exactly this, so there is no discovery
mechanism here, no probe, and nothing on either side that has to be told the other exists.

**Exactly one implementation is registered, and that is a rule rather than an accident.** Several
would be a thing the sibling has to define the meaning of, and this plugin should not be what forces
that question. `ExactlyOneSinkIsRegistered` refuses a second one.

**Registering costs a server with no sibling installed nothing.** That is the ordinary server and
the one most people run. Nothing here reaches for the other plugin, nothing starts, and a handover
that never arrives leaves an object nobody asks. `TheSinkWorksOnAServerWithNoSiblingInstalled` is
that state, asserted while `SiblingIndependenceTests` holds that no sibling assembly is loaded at
all.

**Being reachable is a different claim and is not made here.** Naming a type means having the type,
and whether a second plugin in one server process can name this one is #117, which nobody has
measured on either claimed line. Resolving the sink from inside this assembly, which is what the
suite does, says the registration is there and says nothing about who else can ask for it.

### Two obligations that come with being a sink

Both are about the caller rather than about this plugin, because the thing on the other end of the
call is a user's gesture on a surface this plugin does not own.

**Nothing leaves the call except the answer.** No exception crosses the boundary, including one
raised by something nothing here foresaw, because a defect in this plugin arriving there fails that
gesture for a reason nobody on that side can act on. The refusals this side can name are in
`HandoverRefusal`; everything else is caught at the boundary, written to this server's log at error
level with the fault itself, and answered as the same one bit. A cancelled call answers the same
way, because no request was made.

**The call does not wait on this side's queue for longer than it gives itself.** A sink that hangs
stalls the gesture behind it just as surely as one that throws. The bound is
`WantHandover.DefaultAnswerWithin` and it is raced against the call rather than handed down as a
cancellation token, because the case worth bounding is the one a token cannot reach: a write holding
a lock nothing will release leaves a task that never completes however politely it is asked to stop.

Giving up is safe here for the same reason a repeat is safe. The want carries an identifier, the
other side hands it over again, and a request the abandoned call still managed to write is
recognised as the repeat it is rather than made twice.

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

## No read crosses back, and that is a refusal rather than a gap

There is an obvious feature on the other side of this seam that is not going to be built. A browsing
surface showing that a title has already been asked for would stop a user asking twice, and it reads
well. It needs the browsing side to read request state out of this one.

The contract is one way. A handover carries a want across and nothing is learned back except that it
was accepted. A query in the other direction would make each side hold a piece of the other's state,
which is the arrangement both boards are avoiding and the reason the contract is shaped this way at
all. So this side offers the seam no way to read request state, and the duplicate case is answered
where the handover happens instead: the same want arriving twice produces one request, and the
sibling's own repeat handling does the rest on its side.

That is a rule of its own and not the identity rule wearing another name. The want identifier is an
idempotency key, looked up before anything is built, over every request the store holds and in every
state, so a want whose request was declined is still a want that has been taken. The identity rule
answers a different question, whether two asks are the same thing, and it answers it against the
provider identifiers, so a want carrying none is different from every other want including another
copy of itself. Each rule catches what the other cannot:

    git grep -n 'Task<StoredRequest?> FindByWantAsync' -- Jellyfin.Plugin.Requests/Storage/IRequestStore.cs

The user's answer to what happened to their request comes from this plugin's own surface, decided in
[`docs/surface.md`](surface.md) and reaching the same clients: the channel for a client that renders
one, the page for a browser. Not from the seam, and not from anything the sibling draws.

### Refusing a read over the seam is not the same as nothing being able to read

The second statement is the wider one and it is false today, so it is written here rather than left
for somebody to infer from the first.

`IRequestStore` is public, and the plugin registers it into the container the server hands it:

    git grep -n 'AddSingleton<IRequestStore>' -- Jellyfin.Plugin.Requests/PluginServiceRegistrator.cs
    Jellyfin.Plugin.Requests/PluginServiceRegistrator.cs:52:        serviceCollection.AddSingleton<IRequestStore>(provider => new FileRequestStore(

That collection is the server's own, not one this plugin keeps to itself, so the registration sits
where everything else in the process can be resolved from. What the interface answers is not
narrowed by who is asking: `GetAllAsync` returns every request in the store and `PageAsync` answers
a query over all of them.

What this does not say is that another plugin can reach it. Naming a type means having the type, and
whether a second plugin in one process can name this one is the assembly-loading question #117 asks
and nobody here has measured. So what is written down is an exposure of unmeasured reachability,
which is neither a leak that has been shown nor a safety anybody has earned. The rule the API keeps,
that a caller reads their own requests and never another person's, is enforced at the endpoints in
`docs/api.md` and not by the store beneath them.

## What happens to the wants recorded before this plugin was installed

The sibling records every want locally whether or not anything accepted it, so a server that ran it
first already has a list of people who asked for things. Installing this plugin has to mean
something for them, or they ask again, or worse, they believe they already have.

**The replay is the sibling's to initiate and this side needs no new message for it.** That follows
from the contract rather than from a preference: it is one way, so this side cannot pull that list
and cannot even ask whether one exists. What the other side does is hand each recorded want over
with the same call a live one arrives on, which is why nothing here is added for it and why nothing
here can tell a replay from a first arrival at the moment it happens.

**What this side owes against that is that a replay is safe to run, and safe to run again.** Both
are the want identifier doing its job under another name. Replaying a want that already became a
request creates nothing, and a replay that stopped halfway and was started again from the beginning
finishes the set without making the part that landed a second time. The second is the one worth
naming: a replay of somebody's whole history ends halfway often, because a server restarted or
somebody closed a browser, and a replay that can only be run once is one nobody can safely start.

    git grep -n 'TheWholeSetReplayedTwiceIsTheQueueItMadeTheFirstTime\|AReplayThatStoppedHalfwayIsSafeToRunAgainFromTheStart' -- Jellyfin.Plugin.Requests.Tests/Seam/WantHandoverTests.cs

**What is not answered, and it is the rest of #93.** An adopted request is not distinguishable from
one that arrived live. A request carries no history entry when it is made, and
`RequestHistoryEntry` has no field that could say how the ask reached this side:

    git grep -n 'public required RequestState From\|public Guid? ByUserId' -- Jellyfin.Plugin.Requests/Model/RequestHistoryEntry.cs

So an operator who finds a sudden queue cannot see where it came from. What a request records about
having arrived over the seam is the entry below that belongs to #118, and it has to be settled there
rather than added afterwards, because the history is append-only and entries written without it
cannot be corrected.

## What this side trusts, and what it checks anyway

The HTTP API takes the requester from the authenticated session and refuses a body that names
somebody else, so filing a request as another person is not something it declines to do, it is
something the call has no way to express. The seam is the opposite shape: it carries a user
identifier, there is no session behind it, and this plugin cannot verify that the person named is
the person who asked.

**That is a boundary rather than a hole, and the reason is worth stating plainly.** The caller is
another plugin running inside the same server process. Anything in that process can already read
this plugin's store, write its files, and do a great deal more than file a request in somebody's
name. A check here would therefore protect against nothing that is not already possible, and it
would read afterwards as a check that was made.

**So the sibling's own permission check is the only check on that path, and this side is trusting
it.** Whoever is evaluating this plugin should read that sentence as it stands: a want that arrives
over the seam is attributed to whoever the caller says asked for it.

**What this side checks anyway is that the identifier names somebody this server has.** That is a
different question from whether they asked, and it is the one this side can answer. A handover
naming a user the server does not have is refused and nothing is stored, because a request against
a user nobody has is a row no surface can ever show to anyone and a person nothing can ever notify.

    git grep -n 'UserNotOnThisServer' -- Jellyfin.Plugin.Requests/Seam/

The check costs one reference, which is written down in `SiblingIndependenceTests` with the reason:
the server's user manager answers this question with the user record, and this plugin reads nothing
out of that record.

## A field set this side does not understand is refused whole

The contract carries a version. What this side does with one it does not know is a stated rule
rather than whatever the code happens to do, because the two plausible behaviours differ in a way
nobody notices until it has already gone wrong.

**The rule.** This plugin implements exactly the contract versions in its known set, which today is
one version and is `WantHandover.KnownContractVersion`. A field set carrying anything else is
refused whole. Not one field is read out of it, and no request is made from it.

    git grep -n 'public const int KnownContractVersion' -- Jellyfin.Plugin.Requests/Seam/WantHandover.cs

**Why not read the fields it recognises.** Because that behaviour cannot tell the two kinds of
version change apart. A version that added a field and a version that changed what an existing field
means look identical to a reader that takes what it knows and ignores the rest, and the second one
turns into a want filed against the wrong title, the wrong person or the wrong kind. A refusal is a
want that did not arrive, which the other side can see; a misread is a request in somebody's queue
that nobody can trace back.

**What the number is, today.** It is what this side believes it implements rather than a number read
off the contract, because the contract's own version rule is still open on the sibling's board. That
is cheap to correct: the seam is an in-process call, it serialises nothing, nothing on disk carries
the number, and no caller outside this process can be pinned to it. When the contract mints its
rule, the constant moves to match it and the behaviour above does not change.

## What a refusal looks like from each side

The contract lets this side answer one thing, which is whether the handover was accepted. So a
refusal reaches the sibling as exactly that and carries no reason, and this is the shape rather than
an omission: a reason travelling back would be a second thing the contract has to define and keep in
step.

The reason is written on this side instead, to the server's log, with the sibling's own identifier
for the want so an operator asked about a want by that identifier can find what happened to it. The
line names the refusal and the version that arrived. It carries neither the title nor the person,
because a log is pasted into issue trackers and what somebody asked for is the thing in this plugin
worth being careful with.

The refusals that exist are in `HandoverRefusal`, and reading them from there rather than from a
list here is deliberate:

    git grep -n '^    [A-Z][A-Za-z]* = [0-9]' -- Jellyfin.Plugin.Requests/Seam/HandoverRefusal.cs

Where an operator reads it is the server's log and nowhere else today. A page that says what this
install is refusing and why is the diagnostics view in #63.

## What this document does not yet hold

Named here so the absence is read as absence rather than as a decision nobody wrote down. Each is
the closing condition of the issue beside it.

- What a request records about having arrived over the seam, and which caller handed it over. The
  contract carries no field naming the caller, so the second half of that is a question for the
  contract rather than for this side. #118.
- What an undone gesture does, which today is nothing, because the contract carries no message for
  it. #68.

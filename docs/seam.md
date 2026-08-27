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

**Being reachable is a different claim and it is measured rather than assumed.** Naming a type
means having the type, and whether a second plugin in one server process can name this one was the
open half of #117. A second plugin installed beside this one, shipping no copy of anything this
plugin declares, finds the type and is handed the registration by the container, on a server of each
claimed line. The answer and the commands are under "Where the shared type comes from" below.
Resolving the sink from inside this assembly, which is what the suite does, is still a different
claim and still says nothing about who else can ask for it.

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

## Where the shared type comes from

Implementing somebody's interface means having the type, so the type has to arrive from somewhere,
and where it arrives from is the whole of whether this seam works at all. The failure is quiet:
two assemblies of the same simple name in one process can declare two different types with the same
full name, nothing fails at build time, and what happens instead is that the container returns no
implementations, which looks exactly like the sibling not being installed and is a supported state.

**The choice is a contract-only package both sides compile against, with exactly one copy shipped.**
Taken on #117 on 2026-08-21, and taken before the sibling writes its half, because once the other
side has written against a type declared in this assembly, moving it is a migration across two
boards rather than a choice on one.

What it costs, stated rather than implied. It is another artefact with its own version and its own
publishing route. It is versioned independently and changed rarely: a contract package that moves
often is two plugins that have to be upgraded together, which is the thing a shared type was meant to
avoid. `Jellyfin.Plugin.Requests.Contract` carries its own version properties for exactly that
reason, so a plugin release does not move it:

    git grep -n 'ContractVersion' -- Jellyfin.Plugin.Requests.Contract/Jellyfin.Plugin.Requests.Contract.csproj

WHERE THE OTHER SIDE OBTAINS IT was the open half of that cost and was answered on #117 on
2026-08-24: the organisation's GitHub Packages feed, `nuget.pkg.github.com/Flowfin`, which is a
resolvable permanent address that adds no infrastructure. THE PUBLISH STEP IS NOT BUILT AND NOTHING
HERE CLAIMS IT IS. What exists in this tree is the project, the one shipped copy and the measurement;
what pushes the package to that feed hangs off the release workflow and is named as outstanding under
"What is still not done" below.

WHAT THE PACKAGE HOLDS, AND THE ONE ENTRY THAT IS NOT A SEAM TYPE. The interface, the field set that
crosses, and the enumeration that field set names:

    git ls-tree -r --name-only HEAD -- Jellyfin.Plugin.Requests.Contract/Seam Jellyfin.Plugin.Requests.Contract/Model

`RequestedItemKind` is in there because `HandedOverWant.Kind` is of that type, and the alternative was
for the want to carry something else - a string, or an enumeration of the contract's own - which puts
two vocabularies in the tree that have to be kept in step. That is the defect class the mapping table
in M10 exists against, and it is worse than the cost of moving the enumeration, which is that an
ordinary property of this plugin becomes part of an agreement with another board: adding a third kind
of thing a person can ask for is now a contract change rather than a plugin change. The move costs
nothing at the call sites, because a namespace is not an assembly and this one did not move with the
file.

`HandoverRefusal` IS NOT IN THE PACKAGE, AND THAT IS DELIBERATE RATHER THAN AN OVERSIGHT. The note on
#117 of 2026-08-25 lists it among the types a contract package would hold. It never crosses: the
contract lets this side answer one thing, which is whether the handover was accepted, and the refusal
is this side's own reason, written to this server's log. A type the other side can never see is not
part of an agreement with it, and putting it in the package would version this plugin's internal
vocabulary against another board.

### The two that were rejected

**A contract-only package both sides compile against and both ship, with the loader deduplicating
it.** Rejected. It rests on the loader actually merging two copies, and that is the assumption the
entire handover would then depend on. Nobody has measured it on either claimed line, so the cost of
this option is an unmeasured premise underneath every call that crosses. One shipped copy removes
the question instead of answering it.

**No shared type at all, with the handover taken by name through reflection.** Rejected, and it is
the honest fallback if the package turns out to be impractical rather than a bad idea. It is immune
to this whole error class, because there is no second type to be a different type. What it costs is
the thing #117's fourth condition is about: a mismatch stops being a compile error and becomes a
runtime one, so "no sibling installed" and "sibling installed, type did not match" collapse back
into the same silence. A compile-time contract is what keeps those two states different.

### What a server of each line actually does

The choice above rests on a plugin being able to name a type whose assembly ships in another
plugin's directory. That is a fact about the host rather than about either tree, and the two claimed
lines are different major versions of it, so an answer taken from one is a claim about the other.
Both were asked, at `5b96f57`, by `.github/workflows/seam-probe.yaml`:

    gh api repos/Flowfin/jellyfin-plugin-requests/actions/jobs/96989583236/logs | grep -a "SEAM-PROBE" | sed -E 's/.*ContainerReport: //' | sort -u
    SEAM-PROBE assemblies loaded under the name Jellyfin.Plugin.Requests: 1
    SEAM-PROBE one of them is at /config/plugins/Jellyfin.Plugin.Requests/Jellyfin.Plugin.Requests.dll
    SEAM-PROBE the container returned 1 implementation(s) of it
    SEAM-PROBE the type Jellyfin.Plugin.Requests.Seam.IWantHandover is reachable from this plugin

That job is the 10.11 line. The 12.0 line is job `96989583387` and the same command returns the same
four lines. Both servers held two plugins while answering, this one and the probe.

So one shipped copy is available on both lines. The assembly is loaded once, in a context a second
plugin can see, and the container hands that second plugin the implementation this one registered.

What it does not say, because the distance between the two is where this would be misread. The probe
finds the type by name through reflection, so what is measured is that the assembly is loaded once
and that its type resolves and answers a lookup from elsewhere in the process. Whether the runtime
binds a compile-time reference to that same loaded assembly is a further step and is not measured
here. And nothing above installs two copies of one contract assembly, so the premise the first
rejected option rests on is still unmeasured, which is what that option's own paragraph says.

### That answer is refused rather than reported, since 2026-08-26

The run that took the measurement above refused one thing only: a probe that wrote nothing. Both
answers were results while the three options were open, because each of them decided which options
were available. They are not both results any more. The choice above rests on the answer the two
lines gave, so a run that comes back with the other one is a defect and a run that prints it and
passes tells nobody.

`scripts/read-seam-probe-answer.sh` reads the one line the probe writes as a verdict and refuses
seven answers, each for its own reason. Four are about the lookup by name: no assembly of that name
loaded, more than one of them, a contract type a second plugin cannot reach, and a container that
handed back nothing. Three are about the compile-time reference, which is the shape a sibling
actually ships in: a reference the runtime would not bind, a reference bound to a different type of
the same full name, and a container that answered the reflected lookup and not that one. The last of
each group is the silence #117's fourth condition is about, and both are refused here rather than
printed.

What made that worth doing before the package existed was that moving the type is what breaks it.
The move has happened and the constants moved with it, which is the case that rule was written for
arriving rather than a hypothetical:

    git grep -n 'OtherAssembly = \|Contract = ' -- tools/seam-probe/ContainerReport.cs

The trigger carries the same reasoning: the contract project's whole directory, the plugin's `Seam/`
and the service registrator are in the paths that start the job, because the change that breaks the
measurement arrives through them rather than through the probe's own files. What that no longer
leaves uncovered is a rename of the contract ASSEMBLY, which now lives in a project file inside a
listed directory. A rename of the plugin assembly is outside it, and the probe no longer names that
one.

Every refusal above is watched biting in `scripts/prove-seam-probe-refusals.sh`, over one log per
answer, with the answer both lines gave beside them as the case that has to pass. It needs no
container and no server, so the reader that decides a probe run is checked on machines that cannot
run one.

### What the tree holds today, which is now the choice above

THIS SECTION SAID THE TREE WAS NOT IN THE CHOSEN SHAPE AND IT IS. What stood here read the plugin's
project file, found two references and both of them the host's, and said the decision was a thing to
build rather than a description of what ships. It is built. The type the sibling names is declared in
an assembly of its own, and the plugin references it:

    git grep -n 'public interface IWantHandover' -- Jellyfin.Plugin.Requests.Contract/Seam/IWantHandover.cs
    git grep -n 'ProjectReference' -- Jellyfin.Plugin.Requests/Jellyfin.Plugin.Requests.csproj

EXACTLY ONE COPY SHIPS, AND WHICH SIDE SHIPS IT IS THE WHOLE ARRANGEMENT. The plugin's reference
carries no `ExcludeAssets`, so the assembly lands in the plugin's own directory and `build.yaml` names
it in the artifact list beside the plugin's assembly; the Package check compares that list against a
publish in both directions, so a package that left it out is refused rather than installed. A
consumer of the contract does the opposite - compile against it, ship nothing - which is what the
probe's own reference says:

    git grep -n -A3 'ProjectReference' -- tools/seam-probe/SeamProbe.csproj
    git grep -n 'artifacts:' -A3 -- build.yaml

WHAT REFUSES A RETURN TO THE OLD SHAPE. Copying the three types back into the plugin assembly
compiles, ships one assembly again, and breaks nothing this repository builds - what it breaks is a
sibling nobody here compiles. `TheSharedTypesAreDeclaredInTheContractAssembly` reads the assembly each
of them is declared in and refuses that, `TheContractReferencesNothingButTheFramework` refuses a host
assembly being put on the surface the other side inherits, and the exact reference list refuses the
contract arriving under a neighbouring name.

    git grep -n 'public void TheSharedTypesAreDeclaredInTheContractAssembly\|public void TheContractReferencesNothingButTheFramework' -- Jellyfin.Plugin.Requests.Tests/SiblingIndependenceTests.cs

### What is still not done

**The publish step.** The address is decided and nothing pushes to it. A sibling on another board
cannot resolve `Jellyfin.Plugin.Requests.Contract` from `nuget.pkg.github.com/Flowfin` until the
release workflow gains a step that puts it there, and no workflow in this tree packs or pushes it:

    git grep -rn 'nuget push\|dotnet pack' -- .github/workflows/

**The fourth condition of #117, which is the silent case.** With one shipped copy there is no second
type to be a different type, so a sibling built against a contract version this plugin does not ship
fails to bind rather than resolving nothing. The reader refuses that answer by name, and what has not
been done is producing it: nothing in this tree builds a consumer against a divergent contract,
installs it, and watches an operator being able to tell that from a sibling that was never installed
at all. Until that exists, the refusal is a rule nobody has watched bite on a server.

### What the package actually contains, read out of the package

The paragraphs above read the project file. #11's third condition refuses that reading as sufficient
on purpose: it asks that this plugin builds, installs, runs and passes its suite with no sibling
present, proven by the package's dependency list rather than by the project file. The Package check
builds the package on every push to `master` and keeps the archive, so that list can be read out of
the thing that ships.

Taken from the run of `037a664567acbd9eb0defa88118ddf6331ff3bed`:

    gh run download 32888209248 --repo Flowfin/jellyfin-plugin-requests --dir pkg
    unzip -l pkg/package-10.11.0.0/requests_0.1.0.0.zip
          900  2026-08-25 19:13   meta.json
       380928  2026-08-25 19:13   Jellyfin.Plugin.Requests.dll
    unzip -l pkg/package-12.0.0.0/requests_0.1.0.0.zip
          899  2026-08-25 19:12   meta.json
       380928  2026-08-25 19:12   Jellyfin.Plugin.Requests.dll

Two files on each claimed line: this plugin's own assembly and the metadata a server reads. Nothing
of the other board ships, and nothing that could be a second copy of a shared type ships either,
which is the baseline the choice above changes: when the contract package exists, exactly one side
carries it and this listing is where that becomes visible.

That list is derived rather than asserted. The same job publishes the plugin and compares what the
publish leaves behind against the `artifacts:` list in `build.yaml`, ending non-zero on a name in
either set and not the other, so an assembly the host does not provide cannot be absent from both:

    gh api repos/Flowfin/jellyfin-plugin-requests/actions/jobs/97933626171/logs | grep -a -A3 "published assemblies:"
    published assemblies:
    Jellyfin.Plugin.Requests.dll
    named in artifacts:
    Jellyfin.Plugin.Requests.dll

It then installs the package's own bytes into a server of the line it was built for and requires that
server to report the plugin active, which is the reading no file in this repository can make on its
own.

**What this does not say.** Neither of those servers held the sibling, so nothing here measures the
two plugins in one process; that is the section above, which was measured separately and by a probe
built for it. The archive read here is the one the check keeps for fourteen days rather than a
released asset, so the download command stops resolving after that and the reading has to be retaken
against a newer run; `0.1.0.0-stable` is no substitute, because it predates every line of this seam.

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
[`docs/surface.md`](surface.md), and today that is the page and a browser. The channel carried it
for a client that renders one until #67 measured on a running server that the answer did not stay
one person's. Not from the seam either way, and not from anything the sibling draws.

### Refusing a read over the seam is not the same as nothing being able to read

The second statement is the wider one and it is false today, so it is written here rather than left
for somebody to infer from the first.

`IRequestStore` is public, and the plugin registers it into the container the server hands it:

    git grep -n 'AddSingleton<IRequestStore>' -- Jellyfin.Plugin.Requests/PluginServiceRegistrator.cs
    Jellyfin.Plugin.Requests/PluginServiceRegistrator.cs:63:        serviceCollection.AddSingleton<IRequestStore>(provider => new FileRequestStore(

That collection is the server's own, not one this plugin keeps to itself, so the registration sits
where everything else in the process can be resolved from. What the interface answers is not
narrowed by who is asking: `GetAllAsync` returns every request in the store and `PageAsync` answers
a query over all of them.

What this does not say is that another plugin can reach it. Naming a type means having the type, and
whether a second plugin in one process can name this one is the assembly-loading question #117 asks
and nobody here has measured. So what is written down is an exposure of unmeasured reachability,
which is neither a leak that has been shown nor a safety anybody has earned.

**The boundary is the endpoints, and the store beneath them is not one.** The rule the API keeps,
that a caller reads their own requests and never another person's, is enforced where those calls
arrive and is written down in `docs/api.md`. A caller already inside the server process is outside
that rule, and this document says so instead of leaving it to be worked out from the registration
above.

That sentence is here in its own right because it is wider than the position it follows from. What
"What this side trusts, and what it checks anyway" argues below is about one handover: a caller in
this process can read this plugin's store and write its files whatever the seam does, so a check on
the seam would protect against nothing that is not already possible. Reading that as covering
everything this plugin puts into the server's container is an extension of it, and an extension
nobody wrote down is the kind somebody discovers later and disagrees with.

What taking it gives up is the narrower answer, which is to make the store's contract internal to
the assembly or to hand the container something smaller. Either removes an exposure nobody has
shown is reachable, and either reaches through `Jellyfin.Plugin.Requests/Storage/` and the whole
suite derived from `RequestStoreContract`. It becomes the right change the day somebody measures
the reachability and finds it, and that measurement is cheap: a second assembly in the same process
trying to resolve the type answers it. Until then this rests on an argument rather than on a fact,
and it should read that way.

## An undone gesture does not cross the seam

A gesture that expresses a want can be taken back. Somebody unmarks a favourite, or marks one by
accident and unmarks it a second later. Nothing about that reaches this side.

The contract carries one message and it is the handover:

    git grep -n 'Task<bool> AcceptAsync' -- Jellyfin.Plugin.Requests/Seam/IWantHandover.cs
    Jellyfin.Plugin.Requests/Seam/IWantHandover.cs:44:    Task<bool> AcceptAsync(HandedOverWant want, CancellationToken cancellationToken);

There is no second call and no field on the want that says one was withdrawn, so a request made from
a handover stays exactly as it was when the gesture behind it is undone. That is a statement about
the contract rather than about either implementation: this side could not act on an undone gesture
today even if it wanted to, because nothing tells it one happened.

What would have to change for it to cross is a message on the contract, and that is the sibling
board's to add rather than this one's. This side would implement it when the contract carries it,
and inventing the field here instead would be half an agreement, which is the same argument
[the trust section](#what-this-side-trusts-and-what-it-checks-anyway) makes about a caller identity
nobody agreed. Adding it would also be an argument rather than a field: a want undone a second after
it was expressed and one undone a week after an operator has already acted on it are not the same
event, and the second is not something a gesture on a browsing surface can decide alone.

Taking back an ask on this side's own surfaces is a separate thing, and the answer is that a user
cannot. There is no state a request can be withdrawn into, and the model records why:

    git grep -n 'Cancelled' -- Jellyfin.Plugin.Requests/Model/
    Jellyfin.Plugin.Requests/Model/RequestActor.cs:43:    /// is no state for a user withdrawing, refused with the <c>Cancelled</c> state on #113. The
    Jellyfin.Plugin.Requests/Model/RequestLifecycle.cs:69:/// user withdrawing has no state to move to because <c>Cancelled</c> was refused on #113. An

**What a person does instead is ask an operator, who declines the request.** That is written here
rather than left to be found, because it is the one errand this plugin exists to remove, and an
absence a user meets without warning is worse than one they were told about.

The cheaper-looking alternative is worse than the absence. Routing a withdrawal through
`Open -> Declined` would leave the history saying an operator declined a request the person
withdrew, which is false about both of them. Giving a user a state of their own costs rows in the
transition table, cells in the mapping table and a case on every surface, and it reopens a decision
already taken on #113. So the absence stands, and it is the first thing to revisit if the state set
is ever reopened for another reason rather than a reason to reopen it.

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
one that arrived live. Both cross on the same call, so the entry a request now carries says the seam
for either of them, and the moment on it is the moment the replay ran rather than the moment the
person asked, because that is the only moment this side ever sees:

    git grep -n 'At = request.RequestedAt' -- Jellyfin.Plugin.Requests/Model/RequestHistoryEntry.cs

So an operator who finds a sudden queue can see that it came over the seam and cannot see that it is
a replay. Telling the two apart needs something the contract does not carry, which is #93's question
rather than this section's. What a request does record about having arrived is the section below.

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

## What a request records about having arrived over the seam

A request made from a want carries one history entry saying so. It is written when the request comes
into existence, it is the head of that request's history, and every decision made on the request
afterwards appends beneath it.

    git grep -n 'RequestLifecycle.Arriving(incoming, RequestArrival.Seam)' -- Jellyfin.Plugin.Requests/Seam/WantHandover.cs

It is written through the lifecycle rather than onto the record, because that is the one place a
request's history grows and a surface assigning the list itself is refused by name:

    git grep -n 'id: history-is-only-appended-to' -- tools/opengrep/rules.yaml

**What it says is how, and never who.** The entry names the surface and not the caller. The contract
grows no field naming the plugin that handed the want over, decided on #118, because a field carrying
it would be the sender saying who they are, and a history that records an unverified self-declaration
as fact is worse than one that records less. Reading a caller off an assembly name or a call stack
instead is the same invented value with a different excuse.

**That cost is permanent in one direction, and it is written here rather than only on the issue.** If
a second handing sibling ever ships, the rows written before it do not carry the distinction and
cannot be backfilled, because the history is append-only. The question "which plugin filed this"
therefore becomes unanswerable for everything already landed, on the day somebody first asks it.

**What the entry is for is the trust position above.** A request that arrived here is filed against
whoever the caller said asked for it, with no session behind the name. A request asked for over this
plugin's own endpoint carries the other value and means the opposite: the server authenticated the
person it is filed against. An operator answering for a request can tell those two apart from the
record now, which was not possible before this entry existed.

    git grep -n '^    Seam = 0,\|^    Endpoint = 1' -- Jellyfin.Plugin.Requests/Model/RequestArrival.cs

**One arrival per request, not one per person.** A want naming something already in the queue joins
the request that is there, and a want handed over a second time writes nothing at all, so neither
adds a row. What the entry records is how the request reached this server and not how each person
waiting on it did.

    git grep -n 'SomebodyJoiningOverTheSeamAddsNoSecondArrival\|TheSameWantHandedOverAgainRecordsNoSecondArrival' -- Jellyfin.Plugin.Requests.Tests/Seam/WantHandoverTests.cs

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

Named here so an absence is read as absence rather than as a decision nobody wrote down. Each entry
is the closing condition of the issue beside it.

**Nothing today.** The one entry that stood here was what a request records about having arrived over
the seam, and that is a section of this document now rather than a gap in it. The permanent cost it
carried has moved with it and is not softened on the way: which plugin handed a want over is not
recorded, and for everything already landed it never can be. #118.

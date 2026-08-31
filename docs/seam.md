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
claimed line. The answer and the commands are under "Where the seam type comes from" below.
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

## Where the seam type comes from

Implementing somebody's interface means having the type, so the type has to arrive from somewhere,
and where it arrives from is the whole of whether this seam works at all. The failure is quiet:
two assemblies of the same simple name in one process can declare two different types with the same
full name, nothing fails at build time, and what happens instead is that the container returns no
implementations, which looks exactly like the sibling not being installed and is a supported state.

**The choice is that no type is shared at all. The sibling names this one by string and takes the
handover through reflection.** Taken on #117 on 2026-08-28, and taken against the option this
document carried until that day, because the two options that would have shared a type were both
measured on a running server of each claimed line and neither of them works. The measurements are
below, with the commands, before the argument that rests on them.

**Ugly is the fair word for it and immune is the one that matters.** There is no second assembly, so
there is no second type of the same name, so the failure class this section opens with cannot arise.
Nothing has to be published, versioned or upgraded in step across two boards, and a server that
installs one plugin and not the other is unremarkable rather than a case somebody has to have
thought about.

**What it costs is the compiler, and the price is paid at runtime on somebody's server.** A sibling
that names an assembly, a type, a member or a field this side does not declare compiles perfectly,
installs perfectly, and is answered with nothing. So does this side after a rename nobody thought of
as a breaking change. That is not a residual risk to be noted and left; it is the whole reason for
the three paragraphs that follow, and for the two guards that stand behind them.

### This page is the ABI, and a rename is a version

Both boards read the names off this section and treat them as fixed. A change to any of them is a
breaking change on both boards at once and moves the seam version with it; it is never discovered by
an operator.

The names are not written out here, because a list in a document drifts against the thing it
describes and the thing it describes is one `git grep` away. They are derived:

    git grep -n 'public static string AssemblyName\|public static string TypeName\|public static string MemberName\|public static string WantTypeName' -- Jellyfin.Plugin.Requests/Seam/SeamSurface.cs
    git grep -n 'public const int KnownContractVersion' -- Jellyfin.Plugin.Requests/Seam/WantHandover.cs

`SeamSurface` holds no literal of its own: every member reads its answer off a type, so a rename
moves what it says instead of leaving it describing a seam that is no longer there. What refuses the
rename is `SeamSurfaceTests`, which holds the literals and compares them against those types. That is
the pin, and changing a name means changing that file, in the commit that raises the version and
tells the other board.

The field set the want carries is NOT fixed here and this document does not restate it. It is the
sibling's, in the contract issue named at the top of this page, which is the rule #11's second
condition imposes and which the choice above does not weaken. What the surface test pins is the set
of NAMES a reflected lookup can miss, which is a different question from what any of them means:

    git grep -n 'TheWantCarriesExactlyThePropertiesWrittenDown' -- Jellyfin.Plugin.Requests.Tests/Seam/SeamSurfaceTests.cs

### The two that were rejected, and the run that killed each

Both were rejected on evidence rather than on taste, and each had been an open option until the run
that closed it. Both readings come from `.github/workflows/seam-probe.yaml` on
`seam/117-the-contract-package`, which built a contract-only package for exactly this purpose and is
not merged.

**A contract-only package both sides compile against, with exactly one copy shipped.** This was the
choice this document carried from 2026-08-21 until 2026-08-28, and the premise underneath it - that a
compile-time reference resolves an assembly shipping in another plugin's directory - had never been
measured. Run `33125497741`, job `98702538786` for the 10.11 line, job `98702538479` for 12.0,
identical verdicts:

    gh api --allow-escape-sequences repos/Flowfin/jellyfin-plugin-requests/actions/jobs/98702538786/logs \
      | sed 's/\x1b\[[0-9;]*m//g' | grep -a "SEAM-PROBE" | sed -E 's/.*ContainerReport: //' | sort -u
    SEAM-PROBE assemblies loaded under the name Jellyfin.Plugin.Requests.Contract: 1
    SEAM-PROBE one of them is at /config/plugins/Jellyfin.Plugin.Requests/Jellyfin.Plugin.Requests.Contract.dll
    SEAM-PROBE the compile-time reference to Jellyfin.Plugin.Requests.Seam.IWantHandover did not bind: System.IO.FileNotFoundException: Could not load file or assembly 'Jellyfin.Plugin.Requests.Contract, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'. The system cannot find the file specified.
    SEAM-PROBE the container returned 1 implementation(s) of it
    SEAM-PROBE the type Jellyfin.Plugin.Requests.Seam.IWantHandover is reachable from this plugin
    SEAM-PROBE result assemblies=1 contract=reachable implementations=1 binding=unbound bound-implementations=0 same-type=no

The type is visible to the second plugin and the reference to it is unresolvable, in one run, at the
same moment. Neither half cancels the other, and the second half is the option gone: a sibling that
ships no copy cannot bind what it compiled against.

**A contract-only package both sides compile against and both ship, with the loader deduplicating
it.** Rejected on 2026-08-21 on the grounds that its premise was unmeasured, and the premise is false.
It is also what a package reference gives a sibling by default, rather than a shape anybody has to
construct, which is why it had to be measured rather than assumed away. Run `33126052099`, job
`98704350874` for 10.11, job `98704350837` for 12.0, identical verdicts:

    SEAM-PROBE assemblies loaded under the name Jellyfin.Plugin.Requests.Contract: 2
    SEAM-PROBE one of them is at /config/plugins/Jellyfin.Plugin.Requests/Jellyfin.Plugin.Requests.Contract.dll
    SEAM-PROBE one of them is at /config/plugins/SeamProbe/Jellyfin.Plugin.Requests.Contract.dll
    SEAM-PROBE the compile-time reference to Jellyfin.Plugin.Requests.Seam.IWantHandover bound and the container returned 0 implementation(s) for it
    SEAM-PROBE the container returned 1 implementation(s) of it
    SEAM-PROBE the type it bound to and the type found by name are two different types
    SEAM-PROBE result assemblies=2 contract=reachable implementations=1 binding=bound bound-implementations=0 same-type=no

The loader does not merge them. The second plugin binds to its own copy, that copy declares a
different type of the same full name, and the container hands it nothing. That is not a near-miss of
the failure the top of this section describes. It is that failure, reproduced on a running server of
each claimed line, with the same silence an operator meets.

**One cause sits under both, which is why the third option is not a coin toss.** A plugin gets a load
context of its own. Resolving an assembly by name looks in that plugin's own directory and in what
the host provides, and never in another plugin's. Ship the contract once and the reference is
unresolvable; ship it twice and each side resolves its own. Enumerating what the process has loaded
reaches every plugin's assemblies whatever context they arrived in, which is why the reflected lookup
answered in both runs and in every run before them.

Whoever reopens this in a year: the experiment is `tools/seam-probe`, it is one job away, and both
readings above came out of it in one evening. Re-run it rather than re-arguing it.

### What a server of each line actually does

The choice above rests on a plugin being able to name a type declared in another plugin's assembly,
and to call the member it finds. That is a fact about the host rather than about either tree, and the
two claimed lines are different major versions of it, so an answer taken from one is a claim about
the other. Both are asked by `.github/workflows/seam-probe.yaml`, on every change to the files the
measurement is made of, and the reading is the last line each run writes:

    gh api repos/Flowfin/jellyfin-plugin-requests/actions/jobs/96989583236/logs | grep -a "SEAM-PROBE" | sed -E 's/.*ContainerReport: //' | sort -u
    SEAM-PROBE assemblies loaded under the name Jellyfin.Plugin.Requests: 1
    SEAM-PROBE one of them is at /config/plugins/Jellyfin.Plugin.Requests/Jellyfin.Plugin.Requests.dll
    SEAM-PROBE the container returned 1 implementation(s) of it
    SEAM-PROBE the type Jellyfin.Plugin.Requests.Seam.IWantHandover is reachable from this plugin

That job is the 10.11 line, taken at `5b96f57`; job `96989583387` is 12.0 and returns the same four
lines. Both servers held two plugins while answering, this one and the probe, and the probe ships no
copy of anything this plugin declares.

**The probe now goes as far as a sibling goes, and that is what the choice made necessary.** Finding
the type and being handed an implementation says the lookup works. It says nothing about whether the
call can be made, and under this shape the call is where the remaining risk sits: the member is found
by name, the want is built out of this plugin's own type by reflection, its properties are set by
name, and every one of those steps can fail at runtime with nothing failing at build time. So the
probe makes the call, with a want that names no user - so the implementation runs its own path and
writes nothing into the queue of a server it does not own - and the verdict carries what became of
it. Being refused is the answer being measured; a request being made is not.

    git grep -n 'private async Task<string> CallAsync' -- tools/seam-probe/ContainerReport.cs

That reading was taken at `a90529d`, in run `33244878101`, job `99080522638` for the 10.11 line and
job `99080522583` for 12.0. The two are identical line for line:

    gh api --allow-escape-sequences repos/Flowfin/jellyfin-plugin-requests/actions/jobs/99080522638/logs \
      | sed 's/\x1b\[[0-9;]*m//g' | grep -a "SEAM-PROBE" | sed -E 's/.*ContainerReport: //' | sort -u
    SEAM-PROBE assemblies loaded under the name Jellyfin.Plugin.Requests: 1
    SEAM-PROBE one of them is at /config/plugins/Jellyfin.Plugin.Requests/Jellyfin.Plugin.Requests.dll
    SEAM-PROBE result assemblies=1 contract=reachable implementations=1 call=answered
    SEAM-PROBE the call crossed the boundary and answered False
    SEAM-PROBE the container returned 1 implementation(s) of it
    SEAM-PROBE the member is System.Threading.Tasks.Task`1[System.Boolean] AcceptAsync(Jellyfin.Plugin.Requests.Seam.HandedOverWant, System.Threading.CancellationToken)
    SEAM-PROBE the seam version this side declares, read out of Jellyfin.Plugin.Requests.Seam.WantHandover.KnownContractVersion: 1
    SEAM-PROBE the type Jellyfin.Plugin.Requests.Seam.IWantHandover is reachable from this plugin

Every step a sibling takes is in those eight lines: a second plugin that compiles against nothing of
this one reaches the type, is handed the registration by the container, reads the seam version off
the constant, finds the member with the signature the surface test pins, and makes the call.
`answered False` is the want being refused for naming no user, which is this side's own path running
to its own conclusion. **What is measured is that the call crossed and came back carrying the answer
the contract says it carries.** That a request was made is not measured and is not claimed; the probe
deliberately hands over a want this side turns down.

### That answer is refused rather than reported, since 2026-08-26

The run that took the first measurement refused one thing only: a probe that wrote nothing. Every
answer was a result while the three options were open, because each of them decided which options
were available. They are not results any more. The choice above rests on the answer the two lines
give, so a run that comes back with another one is a defect and a run that prints it and passes tells
nobody.

`scripts/read-seam-probe-answer.sh` reads the one line the probe writes as a verdict and refuses five
answers, each for its own reason: no assembly of that name loaded, more than one of them, a seam type
a second plugin cannot reach, a container that handed back nothing, and a lookup that worked with a
call that did not. The fourth of those is the silence #117's fourth condition is about. The fifth
arrived with this choice and is where a rename lands, in four spellings the reader names one by one.

Every refusal is watched biting in `scripts/prove-seam-probe-refusals.sh`, over one log per answer,
with the answer the chosen shape produces beside them as the case that has to pass. Two of the
fixtures are near-misses rather than plain wrong answers: a restart whose SECOND verdict is the bad
one, which is what a reader using `head` where this one uses `tail` would pass, and the result line
of the reader before this one, word for word, which is what a reader that matched the first three
fields and stopped would pass as a working seam. It needs no container and no server, so the reader
that decides a probe run is checked on machines that cannot run one.

### What the tree does today is the choice, and that is a change

This section said the opposite until 2026-08-29, and said it deliberately: the registration had
landed before the question was decided, and reading the paragraphs above as a description of what
ships was the one misreading the document could produce. There is nothing left to build. The type the
sibling names is declared in this plugin's own assembly, and this project references nothing but the
host:

    git grep -n 'public interface IWantHandover' -- Jellyfin.Plugin.Requests/Seam/IWantHandover.cs
    git grep -n 'PackageReference Include\|ProjectReference' -- Jellyfin.Plugin.Requests/Jellyfin.Plugin.Requests.csproj

What used to be the gap is closed rather than softened. The seam probe resolves this plugin's
registration from another plugin's load context and calls it, on a running server of each claimed
line, in the shape that ships - which is the shape, because under this choice there is no second
shape a package would have introduced.

### The silence an operator meets, and what is put beside it

This is what the rejected options bought and this one does not, so it is stated as a cost rather than
as a feature. Under a compile-time contract a sibling naming the wrong type fails to build. Under
this one it is handed nothing by the container - and a server with no sibling installed is handed
nothing too, because nobody asked. The container cannot tell those apart, because from where it
stands there is no difference.

What separates them is one line this plugin writes at startup, in `SeamAnnouncement`. It prints the
names above, so an operator has the exact strings to compare against what the other plugin asks for,
and it says whether any other Jellyfin plugin is loaded in this process at all. A server with no
sibling is told there is nothing to expect. A server that has one is told which one, and that a name
that does not match is answered with nothing rather than with an error.

**It does not detect a mismatch and does not claim to.** Nothing on this side can see what another
plugin asked the container for; there is no callback and no read back across this seam, for the
reasons under "No read crosses back" below. What the line buys is that the two states read
differently and that the operator holds the strings. That is what #117's fourth condition asks for
and it is the whole of what is delivered.

    git grep -n 'NoSiblingAndASiblingDoNotReadTheSame' -- Jellyfin.Plugin.Requests.Tests/Seam/SeamAnnouncementTests.cs

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
of the other board ships, and nothing that could be a second copy of a shared type ships either.
That was a baseline about to change while the choice above was a package; since 2026-08-28 it is the
permanent shape, and this listing is where a shared assembly appearing in the install would be seen.
A second name in it is the failure the section above measured, not a step towards the seam working.

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
    Jellyfin.Plugin.Requests/PluginServiceRegistrator.cs:68:        serviceCollection.AddSingleton<IRequestStore>(provider => new FileRequestStore(

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

**An adopted request is distinguishable from one that arrived live, and what separates them is a
field the contract now carries.** Both still cross on the same call, so this side infers nothing: the
sibling marks a want it is replaying, and a request made from a marked want records the arrival that
says so.

    git grep -n 'want.Replay == true ? RequestArrival.SeamReplay : RequestArrival.Seam' -- Jellyfin.Plugin.Requests/Seam/WantHandover.cs
    git grep -n 'SeamReplay = 2' -- Jellyfin.Plugin.Requests/Model/RequestArrival.cs

**Absence is live and the marker says the unusual thing, which is what makes an older sibling
harmless rather than wrong.** A build from before the field existed hands every want over without it
and each of them is recorded as a want somebody expressed now. The reverse spelling would have made
every one of those read as a replay. A marker spelled `false` is read as live too rather than
refused, because the sending side's own type refuses that spelling on the grounds that a false and an
absence are the same want, and throwing the want away over the redundant spelling would cost somebody
their request to make a point the sender has already conceded.

    git grep -n 'AMarkerSpelledFalseIsReadAsLiveRatherThanRefused\|AReplayedWantIsRecordedAsAReplayRatherThanAsALiveHandover\|AWholeReplayedSetIsMarkedAsAReplayThroughout' -- Jellyfin.Plugin.Requests.Tests/Seam/WantHandoverTests.cs

**The seam version did not move for it, and that is the sibling's rule rather than a concession made
here.** That contract counts breaking changes only and says that a field a receiver may ignore does
not raise the number; the field arrived there at version one for that reason and because nothing has
shipped from that repository yet. Raising it on this side would refuse every want the sibling writes,
because a field set whose version this side does not know is refused whole.

**What the marker does not buy is the moment somebody asked.** The moment on the entry is when the
replay ran, because that is the only moment this side ever sees and the contract carries no field for
the other one:

    git grep -n 'At = request.RequestedAt' -- Jellyfin.Plugin.Requests/Model/RequestHistoryEntry.cs

So a queue that filled up at once still reads as a queue that filled up at once. What the operator
gains is the account of why, which is what this condition was for. What a request records about
having arrived is the section below.

**A want somebody expressed live and the sibling later replays keeps the live arrival it already
had.** The request exists, so the replay writes nothing at all - the same rule that makes a replay
safe to run twice - and the history is append-only, so there is no second row to disagree with the
first.

    git grep -n 'AReplayOfAWantSomebodyAlreadyExpressedLiveLeavesTheLiveArrivalStanding' -- Jellyfin.Plugin.Requests.Tests/Seam/WantHandoverTests.cs

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

    git grep -n '^    Seam = 0,\|^    Endpoint = 1,\|^    SeamReplay = 2' -- Jellyfin.Plugin.Requests/Model/RequestArrival.cs

**Three values over two surfaces.** The seam carries two of them, because a want the sibling recorded
before this plugin was installed and one somebody is expressing now are different things to an
operator meeting a queue that filled up overnight. Which of the two a request got comes from the
marker the other side sends and from nothing this side infers, and the section above is where that
is argued.

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

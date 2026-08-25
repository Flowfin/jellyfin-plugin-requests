# Storage

## The decision

Requests are kept in a file this plugin writes itself, in the plugin's own data directory, serialised
with the JSON support the runtime already provides.

Three media were plausible. The two that were not chosen are below with the cost that decided each,
so the next person can see this was a choice and can argue with the cost rather than with the
outcome.

## What was rejected, and why

### The plugin configuration file

The cheapest option, and wrong for a reason that has nothing to do with size.

The dashboard rewrites the configuration file wholesale when an administrator saves any setting. It
reads the whole document when the page loads and writes the whole document back on save, so anything
added to that file between the two is overwritten. A request created while an administrator had the
settings page open would be lost, and nothing would report it: the save succeeds, the file is valid,
and the request is simply not in it.

The second cost is shape rather than correctness. A settings file is a small document a person edits
by hand when something has gone wrong. An unbounded list of records inside one is a file nobody can
read at the moment they most need to.

### A database file of the plugin's own

The obvious answer, and the one that costs the most here, because this plugin claims two server
lines.

The database library is supplied by the host, and the two lines supply different major versions of
it. Compiling against the host's copy means compiling against a different API per line. Shipping the
plugin's own copy instead means shipping native components inside the package, and a native
component that resolves on one line and not on the other is the failure class this repository
already spends two ABI floor jobs on: it builds, it packages, it installs, and it throws at first
use on one of the two lines.

That cost is real rather than hypothetical for this tree. `build.yaml` and `build-jf12.yaml` claim
`10.11` and `12.0` separately, and `docs/testing.md` records a run where a package built for one line
was installed on the other and the server reported `Status=NotSupported`.

## What the chosen medium costs

The crash safety, the indexing and the concurrency are this plugin's to write. That is the whole of
what a database would have supplied.

At the size a request queue reaches, hundreds to a few thousand records, that is a small amount of
code and it is the same code on both server lines, which is what the other two options could not
offer. `IRequestStore` already states the concurrency the callers may rely on, so the promises exist
before the implementation does.

What is not decided here, and where it is:

- What happens to a write interrupted halfway is #46.
- The on-disk shape, its version, and the rules for changing it are below.
- How long a finished request is kept is `FinishedRequestRetentionDays`, and what acts on it is
  `RetentionSweep` beside this store, driven by a scheduled task. What happens to a record when the
  person it names is deleted is still open and is #49.

## The three questions the store is asked

There are three, and naming them is the point: a store built for three questions is a different store
from one that answers all of them by walking everything it holds.

**One person's own requests.** The user surface asks it, once per page view, for every person on the
server. Answered by a lookup keyed on the user, holding the requests they asked for and the ones they
joined, because a surface that showed only the first would show somebody nothing for the request they
are most likely to be looking for.

**One external identifier.** Fulfilment detection asks it once per library item, which is where a
walk becomes ten thousand walks. Answered by a lookup keyed on the kind, the provider name and the
value, compared the way `RequestIdentity` compares them: the name without case, the value exactly.
The lookup answers identity and never policy. Which states a match may move, and whether a series
with some of its seasons counts, stay in the model, and what comes back is at most a handful of
requests so a caller filtering them costs nothing.

**A page of the queue.** The administrator surface asks it, filtered by state and kind, ordered by
one of three columns, and paged. This one is a walk of the whole set and is meant to be: a filter and
an order chosen at the call cannot be served by a lookup built before the call. The count of matches
comes back with the page, from the same walk, so a pager cannot disagree with the rows above it.

Both lookups are built beside the set each time the set is replaced, which costs one pass over it per
write. That is the same order as serialising the set, which the write has just done, so a write does
not change shape for having them. Keeping them up to date incrementally instead would mean editing
the held set in place, which is the thing the whole store is built not to do.

The order every page is taken in ends with the request's own identifier, so requests that compare
equal under the chosen column still hold one position. Without that, their order is the order the set
happens to be enumerated in, and the set is rebuilt on every write: one request created between two
page turns can reorder rows that have nothing to do with it.

## What those three cost at ten thousand records

The bounds are in the suite rather than here, in `FileRequestStoreQueryCostTests`, so a change that
breaks one fails rather than being noticed by somebody with a large queue. Each leg measures the
workload the path carries rather than one call of it, because one call answered by walking ten
thousand records takes under a millisecond and a bound over one call would pass whatever shape sat
underneath it.

The numbers below were read by setting each bound to zero, so the assertion fails and prints what it
measured, and running the leg on an ordinary desktop:

    dotnet test Jellyfin.Plugin.Requests.sln --configuration Release --no-build -f net9.0 \
        --filter "FullyQualifiedName~FileRequestStoreQueryCostTests"
    a hundred filtered and ordered pages over 10000 records took 153 ms, past the bound of 0 ms.
    one identifier lookup for each of ten thousand records over 10000 records took 10 ms, past the bound of 0 ms.
    ten thousand user lookups over 10000 records took 5 ms, past the bound of 0 ms.

The third column is the same command over a tree with the two lookups replaced by a walk of the set,
which is the change each bound exists to refuse:

| Path                    | Workload measured           | As it ships | With the lookup replaced by a walk | Bound in the suite |
| ----------------------- | --------------------------- | ----------: | ---------------------------------: | -----------------: |
| One person's requests   | 10,000 lookups              |        5 ms |                           2,610 ms |             400 ms |
| One external identifier | 10,000 lookups              |       10 ms |                           3,070 ms |             600 ms |
| A page of the queue     | 100 filtered, ordered pages |      153 ms |                              98 ms |           8,000 ms |

The second column of numbers is how the first two bounds were placed. Each sits far above the run it
passes and far below the failure it exists for, so a machine several times slower than this one still
passes and a store that lost a lookup still fails.

The third row is the one to read carefully. Turning the two lookups into walks left it where it was,
so that bound does not separate a walk from a lookup and nothing here claims it does. What it catches
is a page that stops being one walk: a read that goes back to the file per call, or a count taken by
a second pass over the set.

None of the three bounds is a benchmark, and none of them catches a path that is merely slower than
it could be.

## The version on the file, and what may change under it

The file is one document, and the first field in it is the version of the shape:

    {"Version":1,"Requests":[{"Revision":1,"Request":{ ... }}]}

The version is a number about the bytes and not about the plugin. It says how a reader is to
understand what follows, so it moves when the understanding changes and stays where it is when the
plugin's own version moves for a reason that leaves the file alone.

### Reading a version the plugin does not know

Refused, and nothing is written. The file is left byte for byte as it was, the refusal is written to
the server's log naming both numbers, and every call refuses rather than only the first.

This is the downgrade case: an operator installs a newer plugin, it writes a shape this one has never
seen, and the older plugin is put back. The alternative to refusing is to read what is recognised and
ignore the rest, and the cost of that is not a failed read but a successful one: the first write
afterwards puts the understood half back over the file and the rest is gone with no error anywhere.
An operator who sees the refusal can install the newer version again and has lost nothing.

### Reading an older version

Migrated forward as the file is read, and the file itself is not touched. The document this plugin
writes reaches the disk when some later write replaces the file whole, which is the one step this
store ever makes to it. So an install that is opened by a newer plugin and then put back to the older
one finds the file it left, until something writes.

There is one older shape today. Before the version existed the file was a bare array of entries with
no document around it, and that is what the root being an array means. It is read as version 0 and
its entries are exactly the entries of version 1, so the migration is the wrapper and nothing else.
A file in that shape is read, and the line saying so is in the log.

### What needs a new version

A change needs a new number wherever the version before it would read the new bytes and be wrong.
Adding a field the older reader ignores does not, because ignoring it is what it would have done
with a field that was absent. Changing what an existing field means does, renaming one does, and
changing the shape around the entries does.

Nothing here makes that judgement for anybody. What the tree holds is the refusal in the other
direction, which is what stops a wrong judgement from costing the data.

### The fixture rule

A migration is tested from bytes the older shape actually produced, kept as a file under
`Jellyfin.Plugin.Requests.Tests/Storage/Fixtures/`. A fixture typed by hand to look like what the
older version would have written agrees with the migration by construction: both come out of the
same belief about the old shape, so the test passes whether or not that belief is right, which is
the same trap #97 names for the hop between shipped plugin versions.

The fixture that is there was produced by this tree's own store at `592e517`, the commit before the
version landed, by adding two requests through `AddAsync` and copying `requests.json` out.

It is not a shipped version's output, and it could not be. One release exists, and the commit it was
built from carries no store to write anything:

    gh release list --repo Flowfin/jellyfin-plugin-requests --json tagName,createdAt
    [{"createdAt":"2026-08-08T09:38:30Z","tagName":"0.1.0.0-stable"}]
    git rev-parse 0.1.0.0-stable^{commit}
    c44552645f0dba120c49599deedbc0244b59dcec
    git ls-tree -r --name-only c445526 -- Jellyfin.Plugin.Requests/Storage
    Jellyfin.Plugin.Requests/Storage/DuplicateRequestException.cs
    Jellyfin.Plugin.Requests/Storage/IRequestStore.cs
    Jellyfin.Plugin.Requests/Storage/RequestConcurrencyException.cs
    Jellyfin.Plugin.Requests/Storage/StoredRequest.cs

The contract and its two exceptions, and no implementation. So the shape the fixture holds is what a
server running the mainline between `7b62877` and this change has on its disk, and no released
version of this plugin has ever written a request file at all.

Whoever builds the next package should keep that package's own output as the next fixture at the
moment it is built. After a field has been added there is no way to produce those bytes again except
by hand, which is the thing this rule is against.

## Backing up, and restoring

### What is on the disk, and where

Everything is under the directory the server keeps its own data in, so a backup of that directory is
a backup of this plugin and an operator has nothing extra to configure:

| What                 | Where                                                    | In a backup |
| -------------------- | -------------------------------------------------------- | ----------- |
| The queue            | `plugins/Jellyfin.Plugin.Requests/requests.json`         | Required    |
| The settings         | `plugin-configurations/Jellyfin.Plugin.Requests.xml`     | Required    |
| Who wants no notices | `plugins/Jellyfin.Plugin.Requests/notices.json`          | Required    |
| A write in flight    | `plugins/Jellyfin.Plugin.Requests/requests.json.writing` | Not needed  |
| A write in flight    | `plugins/Jellyfin.Plugin.Requests/notices.json.writing`  | Not needed  |

The third row is absent on a server where nobody has turned their own notices off, which is what a
fresh install is: the default is the absence of a value rather than a value, so the file appears the
first time somebody says no. A backup taken before that carries nothing about anybody, and that is
correct rather than a gap. It is `Required` because a restore without it turns everybody's own
setting back on, and the person who set it would find out by being told something they had asked not
to be told. [`notifications.md`](notifications.md) is where the switch itself is written down.

The paths are relative to the server's data directory. Where that directory is differs by
installation and by operating system, and this document does not name it: the server is the
authority for its own paths.

The layout under it is not a guess. The first leg of `FileRequestStoreRestoreTests` reads the
plugin's data folder and its configuration path off the host, and compares each name against the
literal in the table above rather than against the constant that produces it, so a rename reds
rather than agreeing with itself:

    DOTNET_CLI_UI_LANGUAGE=en dotnet test Jellyfin.Plugin.Requests.sln -f net9.0 \
        --filter "FullyQualifiedName~FileRequestStoreRestoreTests"
    Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5, Duration: 200 ms

One step in that chain is read by nothing. That the store is built over the plugin's own data folder
at all is one line in `PluginServiceRegistrator`, and no test asserts it, because the plugin instance
that line reads is a static that any other test class can replace while a leg is running and a leg
built on it would fail for reasons nobody caused. It is a line to read rather than a property the
suite holds.

The last two rows are the files a write is built in before it replaces the one beside it. They are
listed so a backup that swept one up is not read as a problem: a restore that carries one ignores it
and reads the file it was going to replace, which for the queue is
`APendingFileCarriedIntoTheBackupIsNotWhatIsRestored`.

### Restoring

Putting the file back into a data folder that holds nothing is the whole procedure, and the store is
opened over it as though it had always been there. What comes back is what was captured, revisions
included, and the directory is a store afterwards rather than a document: it takes writes, and they
survive the next restart. `AStoreRestoredIntoAFreshDataFolderHoldsWhatItHeldWhenItWasCaptured` is
that leg, and it checks the revisions because a restore that reset them would refuse the first
approval afterwards against a read no caller could see was stale.

The case worth planning for is a restore onto a plugin that is not the version the backup came from,
and the two directions are not symmetric.

**A restore onto a newer plugin works.** An older shape is migrated forward as the file is read, under the
rule above, and the restored directory is writable immediately.
`AStoreCapturedFromAnOlderShapeIsReadableAndWritableAfterBeingRestored` restores the fixture into an
empty data folder, reads both requests and writes a third.

**A restore onto an older plugin is refused, whole.** A file carrying a version this plugin does not read is
not partly restored, and the point is the word partly: entries that this version would understand
perfectly well are still not served, because a queue missing whatever the reader did not recognise is
a queue an operator decides from believing it complete.
`AStoreCapturedFromANewerVersionIsRefusedWholeRatherThanPartlyRestored` puts a readable entry inside
an unreadable document and asserts that every read refuses it, that the refusal is in the log, and
that the backup is byte for byte what it was. Install the newer plugin again and nothing has been
lost.

Each of the five legs was proven to bite by a one-line change to the store, one at a time, with the
other four left passing:

| The change made to the store                                           | What went red               |
| ---------------------------------------------------------------------- | --------------------------- |
| `FileName` renamed to `queue.json`                                     | the paths leg               |
| every entry loaded at revision 1 rather than the one it was written at | the capture-and-restore leg |
| the shape written before the version existed read as an empty queue    | the older-shape leg         |
| `document.Version > OnDiskVersion` off by one                          | the refused-whole leg       |
| a load resuming the file a write was being built in                    | the pending-file leg        |

### What this does not cover

Nothing here restores across two shipped versions of the plugin, because there are none to restore
across: no released version has ever written a request file, which is the same absence the fixture
rule above names. The hop between shipped versions is #97, and the fixture it needs is captured at
release time or not at all.

Nothing here has been run against a server's own backup and restore feature. What is asserted is
about the files and the store that reads them, and an operator who takes their backup some other way
is covered by the same table.

## It adds no package reference

The chosen medium needs nothing that is not already in the runtime. The plugin project's package
references are the two Jellyfin assemblies and nothing else, and neither is added by this decision:

    git grep -n 'PackageReference Include' -- Jellyfin.Plugin.Requests/Jellyfin.Plugin.Requests.csproj Directory.Build.props
    Directory.Build.props:54:        <PackageReference Include="SerilogAnalyzer" Version="0.15.0" PrivateAssets="All" />
    Directory.Build.props:55:        <PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" PrivateAssets="All" />
    Directory.Build.props:56:        <PackageReference Include="SmartAnalyzers.MultithreadingAnalyzer" Version="1.1.31" PrivateAssets="All" />
    Jellyfin.Plugin.Requests/Jellyfin.Plugin.Requests.csproj:23:    <PackageReference Include="Jellyfin.Controller" Version="$(JellyfinVersion)">
    Jellyfin.Plugin.Requests/Jellyfin.Plugin.Requests.csproj:26:    <PackageReference Include="Jellyfin.Model" Version="$(JellyfinVersion)">

The three analyzers are `PrivateAssets="All"` and are build-time only. The two Jellyfin references
differ in version between the lines, which is what `JellyfinVersion` is for and what makes the point:
the set of references is the same on both lines and this decision keeps it that way, where a
database library would have made it differ.

## The record fits without knowing where it is kept

`MediaRequest` is a value with no behaviour and no dependencies, and `IRequestStore` names no file,
no database and no serialisation. Nothing in either has to change for this medium, and nothing in
either would have to change for a different one.

That was measured rather than assumed. A request carrying every field, including a title with a
comma, an accent and a quote in it, was serialised with `System.Text.Json` and read back, against the
plugin as the tree has it:

    every field back: True
    no provider ids, no year, no mover: True

    {"Id":"00000000-0000-0000-0000-000000000001","RequestedByUserId":"00000000-0000-0000-0000-000000000002","RequestedAt":"2026-03-01T12:00:00+00:00","Kind":1,"DisplayTitle":"A title with a comma, an accent \u00E9 and a quote \u0022","DisplayYear":1999,"ProviderIds":{"Tmdb":"603","Imdb":"tt0133093"},"State":1,"StateChangedAt":"2026-03-02T09:30:00+00:00","StateChangedByUserId":"00000000-0000-0000-0000-000000000003","Availability":2,"AvailabilityCheckedAt":"2026-03-03T00:00:00+00:00"}

The second line is the case a store will meet constantly and is easy to get wrong: a request typed by
hand carries no provider identifiers, no release year and nobody who moved it, and all three come
back as the absence they were rather than as a default.

One thing came out of that run which whoever writes the store needs, because it is not what the type
looks like. The generated equality on the record is not value equality:

    record == record: False
    same value, two dictionaries, == says: False

`ProviderIds` is an `IReadOnlyDictionary`, and a record compares that member by reference. So two
requests holding the same values compare unequal whenever the dictionaries are two objects, which is
every time one of them came off disk. A store deciding whether a write changed anything, or a test
asserting that what was read is what was written, cannot use `==` for it and has to compare the
dictionary itself. Nothing in the tree does that today; this is written here so the first thing that
needs to does not discover it as a bug.

The harness for those runs is not tracked. It is a console project referencing the plugin, and adding
one to a tree whose only test project is the suite is a means decision this issue did not take. The
round trip belongs in the suite when the store exists, which is #46.

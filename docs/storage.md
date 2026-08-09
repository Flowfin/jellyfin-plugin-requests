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
- The on-disk shape, its version, and the rules for changing it are #47.
- How long a finished request is kept is #49, and the number itself is decision 5 on #113.

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

# Compatibility

What this plugin promises to somebody who installs it, to somebody who calls its API, and to
somebody whose server already holds requests. Each of those is a promise to a different person, and a
promise nobody wrote down gets broken without anybody deciding to break it.

## The server lines

Two generations are claimed, and each gets its own package because a plugin compiled for one runtime
does not load on the other.

| Line           | Runtime | Packaging metadata | Oldest server claimed |
| -------------- | ------- | ------------------ | --------------------- |
| Jellyfin 10.11 | .NET 9  | `build.yaml`       | 10.11.0.0             |
| Jellyfin 12.0  | .NET 10 | `build-jf12.yaml`  | 12.0.0.0              |

Those numbers are read out of the packaging files rather than restated here:

    git grep -nE "^(targetAbi|framework):" -- build.yaml build-jf12.yaml
    build-jf12.yaml:15:targetAbi: "12.0.0.0"
    build-jf12.yaml:16:framework: "net10.0"
    build.yaml:10:targetAbi: "10.11.0.0"
    build.yaml:11:framework: "net9.0"

## Tested is three different things here, and they are not the same strength

Collapsing them is how an untested combination gets recommended, so they are separated.

**Compiled and unit-tested on both runtimes, on every change.** The plugin and the suite multi-target
both frameworks and the gate builds and runs them:

    git grep -n "TargetFrameworks" -- '*.csproj'
    Jellyfin.Plugin.Requests.Tests/Jellyfin.Plugin.Requests.Tests.csproj:6:    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    Jellyfin.Plugin.Requests/Jellyfin.Plugin.Requests.csproj:10:    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>

**Built against the declared floor of each line, on every change.** A job per line, with the lines
read out of the packaging metadata rather than listed in the workflow, so the plugin is compiled
against the oldest server it claims rather than against the newest one available.

**Observed loading on a running server of each line, on every change.** The transcript in
[docs/testing.md](testing.md) is one hand run on
`e574918775c69640139ee1ecc1f2202efeff27aa`, 2026-08-06, against two images by digest, with the
server itself reporting the plugin `Active`. That was the whole of this row until #75 and #119: the
same procedure now runs on every pull request and nightly, ahead of the two checks that need the
same server.

**Observed running beside the supported sibling set, on both lines, on every change.** A server of
each line is started with this plugin alone and again with the set installed, and what the two runs
declare is compared for collisions over routes, scheduled task names and plugin configuration. Read
at run `32492328763`, `f4889fc`:

    == verdict
    the set, per board:
      Flowfin/jellyfin-plugin-sso	4.3.0-beta.40	Community SSO for Jellyfin
    no collision over routes, scheduled task names and keys, or plugin configuration

    == done
    this plugin alone and beside the set, on jellyfin/jellyfin:10.11.11 (net9.0)

and on the other line, where the set resolves to that board's package for that ABI:

    == verdict
    the set, per board:
      Flowfin/jellyfin-plugin-sso	5.0.0-JF12-beta.54	Community SSO for Jellyfin
    no collision over routes, scheduled task names and keys, or plugin configuration

    == done
    this plugin alone and beside the set, on jellyfin/jellyfin:12.0-rc4 (net10.0)

The scan is watched refusing something before it is trusted to pass. It is
`scripts/sibling-collision-scan.py`, and `scripts/prove-collision-scan.sh` runs it over one fixture
per collision kind and over a clean set, in a job of its own with no container:

    == verdict
    the clean set passes and every collision kind was refused for its own reason

The first two are what a check reports. The names are read off a run rather than off the workflow
files:

    gh api repos/Flowfin/jellyfin-plugin-requests/commits/9a0f5851789b57be0244f3d7eef66405470838f1/check-runs --jq '.check_runs[] | "\(.name)  \(.conclusion)"' | sort -u
    call / build  success
    call / test  success
    floor 10.11.0.0  success
    floor 12.0.0.0  success
    lines  success

### What that leaves untested, and it is more than it sounds

- **One server version per line has ever run this.** `10.11.11` and `12.0-rc4`. Every other patch
  release of either line is expected to work because the floor build says the plugin calls nothing
  newer than the oldest claimed server offers, and expected is not tested.
- **The 12.0 evidence is against a release candidate.** The image was `jellyfin/jellyfin:12.0-rc4`
  and the server reported `12.0.0`. Listing 12.0 as supported without saying that would be a
  statement about a server nobody has run this on.
- **A published release has been installed once, and not the way a server installs.** This bullet
  said no run recorded here had installed from either release. One has since #152:
  `.github/workflows/release-install.yaml` downloads the newest release of every claimed line,
  checks the archive against the digest published beside it, and puts the unpacked bytes on a server
  of that line. Run `33357925338` at `501a943` did it for `0.2.0.0-stable` on `10.11.11`, and the
  server answered `Requests is Active at 0.2.0.0`. What that still does not cover: the archive is
  unpacked by the run rather than fetched and unpacked by the server, and the 12.0 line has no
  release to install at all. The sibling in the set arrives as its own published package and is
  unpacked whole, so the set half of the run exercises the server's own path and the half about this
  plugin still does not.
- **The floor server of the 10.11 line refuses the published release, and this is the bullet above
  it read against the claim.** `build.yaml` declares `targetAbi: "10.11.0.0"`, which names the
  server `10.11.0`. The same archive on that server ends the run at its verdict step with
  `Requests is NotSupported rather than Active`, in run `33358848505`. Jellyfin sets that status in
  exactly one place, catching a `TypeLoadException` or a `ReflectionTypeLoadException` while loading
  the assembly's types, so the shipped build references something `10.11.0`'s shared libraries do
  not carry. **The floor build does not cover this and the two are easy to read as one guard.**
  `abi-floor.yaml` compiles the SOURCE against the floor SDK and was green on the same day; what
  ships is compiled against the current SDK, so a green floor build says the source could be built
  against the floor and nothing about the artefact that went out. #152 holds which of the two
  moves.
- **The supported set is one board, and it is not the one the seam is written against.** A board
  joins the set for a line on the day it publishes a release for that line, and every candidate
  besides `jellyfin-plugin-sso` has published nothing. So a green run says nothing about the sibling
  browsing plugin, which is the one this repository's whole seam is for. The run prints the boards it
  installed and the boards it skipped on every run, and reading that line is the difference between
  a matrix over the family and a matrix over one board.
- **A machine with only the .NET 9 SDK has not been tried.** The .NET 10 SDK builds both targets and
  that is what every recorded build used.

## What a caller of the API may rely on

Every endpoint sits under one prefix with the version in the path:

    MediaRequests/v1

**A breaking change ships as a new version segment beside the old one and never as an edit to this
one.** `v2` would appear, `v1` would keep answering as it does, and callers would move when they
move. What counts as breaking, and the changes that look breaking and are not, are in
[docs/api.md](api.md) rather than repeated here, because two lists of one rule drift.

Nothing here says how long an old version would be kept. That is a decision for the release the
first one is retired in, and it needs an install base to be decided against.

Two things a caller should not read into the version. It is the API's and not the plugin's: the
plugin's own number moves for reasons that leave the API alone, and what each part of that number
means is [docs/versioning.md](versioning.md). And a policy is not part of the shape: which callers
may reach an endpoint is stated in `docs/api.md`, and tightening one is a change to who may call
rather than to what the answer looks like.

## What happens to stored requests

The requests a server holds are one file this plugin writes, and it carries the version of its own
shape. What may change under a version and what needs a new one is
[docs/storage.md](storage.md). What matters to a reader deciding whether an upgrade is safe is the
two directions:

**A newer version's file is refused, and nothing is written.** Downgrade the plugin after a later
version has written its shape and the store refuses to open, names the file and both version numbers
in the server's log, and leaves the bytes exactly as they were. The queue is unreadable to the older
plugin and it is not lost: installing the newer version again returns it. The alternative, reading
the fields an older version recognises and ignoring the rest, is not a failed read but a successful
one, and the first write afterwards puts the understood half back over the file with the rest gone.

**An older version's file is read and migrated forward as it is read.** The file itself is not
touched until some later write replaces it whole, so a server opened by a newer plugin and then put
back to the older one finds the file it left. Two older shapes are read today, and what each step
does is in [storage.md](storage.md). The one worth knowing before an upgrade is version 1 to version
2: a history entry stops naming the person who made each move and says what kind of caller they were,
so an install that upgrades stops holding those identifiers at its next write. Nothing on this server
can attribute a past decision to an individual afterwards, which is deliberate and is not reversible
by going back to the older plugin.

That is the on-disk shape and nothing else. What happens to the settings across the same upgrade is
the section below.

## Upgrading from one shipped version to the next

Two versions have shipped, so there is one hop an operator can actually perform:

```
gh release list --repo Flowfin/jellyfin-plugin-requests --json tagName --jq '.[].tagName'
0.2.0.0-stable
0.1.0.0-stable
```

**What `0.1.0.0` left on a disk is its settings file and nothing else.** That version carried the
store contract and no implementation of it, so an install of it wrote no requests anywhere:

```
git ls-tree -r --name-only 0.1.0.0-stable -- Jellyfin.Plugin.Requests/Storage/
Jellyfin.Plugin.Requests/Storage/DuplicateRequestException.cs
Jellyfin.Plugin.Requests/Storage/IRequestStore.cs
Jellyfin.Plugin.Requests/Storage/RequestConcurrencyException.cs
Jellyfin.Plugin.Requests/Storage/StoredRequest.cs
```

There is therefore no queue in this hop to carry forward, and an upgraded server starts with an
empty one because it never had another.

**The settings carry, and every one of them comes up at the value a fresh install runs.** The older
configuration class held no settings at all, so the file the server wrote for it names none, and
this version's class supplies its own values for everything the file does not mention. That is
worth a test rather than an assumption: four of the ten settings decide whether the install can run,
and a reader that filled the absent elements with the type default instead would bring every
upgraded server up with a quota of zero, neither media kind accepted and a retention period below
the floor, which this plugin refuses to run on. The hop is tested in
`Jellyfin.Plugin.Requests.Tests/Configuration/UpgradeFromAShippedVersionTests.cs`.

**The fixture that test starts from is what the older version produced**, not a document written to
look like it. It is the XML the shipped `0.1.0.0` assembly's own configuration type serialises to,
taken out of the released package:

```
gh release download 0.1.0.0-stable --repo Flowfin/jellyfin-plugin-requests \
  --pattern 'requests_0.1.0.0.zip'
unzip -q requests_0.1.0.0.zip -d shipped
```

and then serialised with the type inside `shipped/Jellyfin.Plugin.Requests.dll`, using
`System.Xml.Serialization.XmlSerializer`, which is what the host serialises a plugin configuration
with. The result is one empty element and it is kept at
`Jellyfin.Plugin.Requests.Tests/Configuration/Fixtures/plugin-configuration-written-by-0.1.0.0.xml`.

**No server wrote it, and no server performed this upgrade.** A running Jellyfin was not available
where this was captured, so what stands behind those bytes is the shipped type and the serialiser
the host uses, and not an installation. Read this section as a statement about what the two versions
do with the same file, never as a report of an upgrade that was watched.

### Whether a version can be skipped

**None so far, and the rule rather than the count is the thing to read.** Neither of the two things
that cross an upgrade needs a particular version to have run:

- The settings file is read by element name, and an element this version's class does not know is
  ignored while one it knows and the file omits keeps the class's own value. Nothing accumulates
  across versions, so going from any shipped version to any later one reads the same file the same
  way.
- The queue file carries the version of its own shape, and whichever plugin version opens it
  migrates it forward as it reads it. The migration is from a shape number to a shape number and
  not from a plugin version to a plugin version, so it does not matter which releases were installed
  in between.

**What would make a version a required stop, so that this section is a promise and not a
description.** A change that reads the settings file for something other than the settings, or one
that moves the queue forward in steps rather than in one read, makes the version carrying it a
version nobody may skip. Such a change names itself here and in `CHANGELOG.md`; a change that says
nothing here is one that can be skipped over.

## What is not promised at all

- **The seam to the sibling browsing plugin.** Which side owns what, and how each finds the other,
  are #88, #89 and #92, and none of them has landed. Nothing about that surface is stable and nothing
  should be built against it yet.
- **Anything about a bridge to an external request service.** There is none; #80 is where the
  interface behind it is defined.
- **What a server does with a version it reads from a manifest.** No manifest is published, which is
  #110, and how a server compares versions in one is not measured by this repository.
- **An upgrade watched on a running server.** The hop from `0.1.0.0-stable` is set out above and is
  measured against what the older version's own type writes, not against an installation. Nothing in
  this repository has installed one version of this plugin on a server and then replaced it with the
  next.

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

**Observed loading on a running server of each line: once, by hand.** One recorded run, on
`e574918775c69640139ee1ecc1f2202efeff27aa`, 2026-08-06, against two images by digest, with the
server itself reporting the plugin `Active`. The transcript is in
[docs/testing.md](testing.md), and that document says of itself that nothing on a merge route runs
it.

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
- **The packaged install path has not been tried.** What was installed in the recorded run is the
  built assembly copied into the container, not a package fetched and unpacked by the server. A
  release exists, `0.1.0.0-stable`, and no run recorded in this repository installed from it.
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
back to the older one finds the file it left.

That is the on-disk shape and nothing else. **Configuration carried between plugin versions is not
implemented**, which is #97, so nothing in this section is a promise about settings.

## What is not promised at all

- **The seam to the sibling browsing plugin.** Which side owns what, and how each finds the other,
  are #88, #89 and #92, and none of them has landed. Nothing about that surface is stable and nothing
  should be built against it yet.
- **Anything about a bridge to an external request service.** There is none; #80 is where the
  interface behind it is defined.
- **What a server does with a version it reads from a manifest.** No manifest is published, which is
  #110, and how a server compares versions in one is not measured by this repository.
- **Any upgrade path from any released version.** One release exists and it was built from a commit
  carrying no store and no API, so there is no shipped shape for this version to be compatible with.

> [!NOTE]
>
> **Part of [Flowfin](https://github.com/Flowfin).** It works with any Jellyfin
> server, and with the Flowfin clients.

# Jellyfin Requests

Media requests for Jellyfin as first-class server objects. A user asks the
server for a film or a series it does not have, an administrator sees the ask in
a queue and answers it, and the request keeps its own state and history on the
server rather than in somebody's chat log.

## What it does not do

Three things this is regularly expected to do and does not. Each was decided
rather than left out, which is why they are here rather than in an issue nobody
finds.

It does not fetch media. Approving a request moves a record from one state to
another and reaches nothing that downloads anything. What makes the film turn up
is whatever the operator already runs, and this plugin notices it arriving by
watching the server's own library.

It does not manage a download client. There is no connection to one and none is
planned. Where an operator runs an external request service, an approved request
can be handed to that service and the service is what talks to whatever fetches;
that handover, and the words the two sides have to agree on, are in
[docs/bridge.md](docs/bridge.md).

It ships no way to find a title the server does not have. On a server with no
browsing sibling plugin installed there is no gesture on a television client
that creates a request at all. The reason is that the sibling owns the catalogue
and this plugin calls no metadata source, and that is refused rather than
promised: `no-call-to-a-metadata-source` in `tools/opengrep/rules.yaml` refuses
the server's provider interfaces and the addresses a plugin would otherwise call
directly, and carries the fixture it is watched refusing.

## This is not finished

Two releases exist, and a manifest under Flowfin's control serves both:

    gh release list --repo Flowfin/jellyfin-plugin-requests --json tagName --jq '.[].tagName'
    0.2.0.0-stable
    0.1.0.0-stable

The address an operator adds, what the entries carry and the checksums read back
against the archives are in [docs/catalogue.md](docs/catalogue.md), which is the
authority for all of it. Nothing on this board writes that document: the release
route here publishes a GitHub release and feeds no catalogue, which
[docs/RELEASING.md](docs/RELEASING.md) says about itself, and what this tree does
with the published manifest is read it back against the releases once a day.

Three things belong here rather than behind the link, because they decide whether
this is worth installing.

**The official Jellyfin catalogue does not carry this and will not.** That list is
filled by enumerating one organisation's repositories, this repository does not
move into it, and the price of that decision is that an operator reaches this
plugin only by being told the address.

**Both published entries carry the 10.11 line's package.** The 12.0 line has none,
because the release route builds the one server line `build.yaml` names, so a
server on the 12.0 line is offered the `net9.0` build rather than nothing. That
comparison is read at the server's own source rather than assumed, on the same
page.

**A published release has been installed on a server once, on the 10.11 line
only.** Run `33357925338`, at `501a943`, downloaded `requests_0.2.0.0.zip`,
checked it against the digest published beside it, and the server answered that
the plugin was `Active` at `0.2.0.0`. The server was `10.11.11`.

**On the floor server it claims, the same release does not load.** `build.yaml`
says `targetAbi: "10.11.0.0"`, which names the server `10.11.0`, and putting
`requests_0.2.0.0.zip` on that server reports
`Requests is NotSupported rather than Active` - run `33358848505`. Jellyfin sets
that status in one place, on a failure to load the assembly's types. What that
server does not carry is not a member, it is the version: the archive's five
Jellyfin references are stamped `10.11.11.0` and `10.11.0` carries all five at
`10.11.0.0`, and a reference above what the host carries does not bind. Which of
the two moves, the SDK the package is built against or the floor it claims, is
answered on #360: the package is built against the floor from now on and the
floor stands, because moving the claim would drop every server below `10.11.11`
without saying so. That answer does not reach a release that already exists.
**Do not install this on a `10.11.0` server: it will not run.**

Two further things neither run covers: the bytes are copied into the plugin
directory rather than fetched and unpacked by the server itself, and the 12.0
line has no release for anybody to install.

Nothing here should be pointed at a server you care about.

The plan is on the issue tracker, cut into milestones. Each issue says what is
wrong, what the evidence is and what has to be true for it to be closed.

## Server lines

Two server generations are claimed, and each gets its own package because a
plugin compiled for one runtime does not load on the other. Claimed is not
published: the section above says which of the two the releases carry.

| Line           | Runtime | Packaging metadata | Oldest server claimed |
| -------------- | ------- | ------------------ | --------------------- |
| Jellyfin 10.11 | .NET 9  | `build.yaml`       | 10.11.0.0             |
| Jellyfin 12.0  | .NET 10 | `build-jf12.yaml`  | 12.0.0.0              |

Those numbers are read out of the two files rather than restated from memory, so
an edit to either shows up as a difference between this table and what the
command prints:

    git grep -nE '^(version|targetAbi|framework):' -- build.yaml build-jf12.yaml
    build-jf12.yaml:13:version: "0.3.0.0"
    build-jf12.yaml:15:targetAbi: "12.0.0.0"
    build-jf12.yaml:16:framework: "net10.0"
    build.yaml:5:version: "0.3.0.0"
    build.yaml:10:targetAbi: "10.11.0.0"
    build.yaml:11:framework: "net9.0"

The `framework` of each line is what the project file multi-targets against. The
`version` both files carry is one number held in one place, and a test refuses a
disagreement between it and the assembly rather than trusting three copies to be
edited together:

    git grep -n '<PluginVersion>' -- Directory.Build.props
    Directory.Build.props:42:        <PluginVersion>0.3.0.0</PluginVersion>

That test is `PluginVersionMatchesThePackagingMetadata` in
`Jellyfin.Plugin.Requests.Tests/PackagingMetadataTests.cs`, and
[docs/versioning.md](docs/versioning.md) is where the scheme and what each field
means to somebody who already has this installed are written down.

An assembly built for each line has been installed on a server of that line and
reported `Active` by the server itself. The transcript of that run, the images
it ran against and what it does not cover are in
[docs/testing.md](docs/testing.md). What has not been tried is the packaged
install path an operator would use, which is a later milestone.

What is supported, what is only expected to work, and what a downgrade does to
the requests a server already holds are in
[docs/compatibility.md](docs/compatibility.md).

## Which clients this reaches

No cell of the reach matrix in docs/surface.md has been checked against a real
client, and the channel now on the mainline has not been browsed from one.

That sentence is the whole claim this file makes about client reach, and it is
the sentence the matrix opens with. The matrix itself, a row per client family
and what a user could do there, is in [docs/surface.md](docs/surface.md). It is
not repeated here, because two copies of a table drift and the reader then has
two answers.

One line of it is worth having before installing anything. On a server with no
browsing sibling plugin there is no way to ask for a title the server does not
have from a television client at all. This plugin ships no title search of its
own, and that was decided rather than overlooked.

## Building

```
dotnet build
```

With no framework argument that builds both targets. The .NET 10 SDK builds
both, and a machine with only the .NET 9 SDK has not been tried.

## License

GPL-3.0. The full text is in [LICENSE](LICENSE), and it is the authority for the
terms, the warranty disclaimer and the limitation of liability.

A plugin is compiled against the server's libraries and ships as an assembly
that links them, so the terms those libraries carry reach the compiled result
whatever the source says. The packages this one compiles against declare
GPL-3.0-only, which is what makes GPL-3.0 the licence here rather than a
preference:

    grep -h '<license type' ~/.nuget/packages/jellyfin.controller/*/jellyfin.controller.nuspec | sort -u
        <license type="expression">GPL-3.0-only</license>

The server's own repository carries GPL-2.0 in its licence file, so those two do
not agree, and [docs/catalogue.md](docs/catalogue.md) is where both are measured
and where what follows from them is written out.

See [NOTICE.md](NOTICE.md) for the intended-use notice.

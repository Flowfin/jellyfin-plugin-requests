# The official catalogue, and what it asks for

A Jellyfin server offers its operator a list of plugins without anybody adding a repository to it.
That list is the official catalogue, and reaching it is the difference between a plugin people find
and one they have to be told about. This page says what that route actually is, checks this
repository against it, and states the licence position for a compiled plugin.

## There is no submission form, and no published list of requirements

The catalogue is built from a repository of git submodules. What decides which repositories are in
it is a script that enumerates one organisation and takes the names that start with a prefix:

    gh api repos/jellyfin/jellyfin-meta-plugins/contents/update_submodules.py --jq '.content' \
      | base64 -d | grep -n 'PAGINATION_URL = \|startswith'
    43:PAGINATION_URL = "https://api.github.com/orgs/jellyfin/repos?sort=created&per_page={per}&page={page}"
    59:        if _name.startswith("jellyfin-plugin-"):
    75:    if not repo.startswith("jellyfin-plugin-"):

Read at `0e692e6ad050dbad63ef25089a97de6f5e85ee45`, which is that file's blob today:

    gh api repos/jellyfin/jellyfin-meta-plugins/contents/update_submodules.py --jq '.sha'
    0e692e6ad050dbad63ef25089a97de6f5e85ee45

The building is one script per plugin directory, run over every submodule:

    gh api repos/jellyfin/jellyfin-meta-plugins/contents/build_all.sh --jq '.content' \
      | base64 -d | grep -n "find . -maxdepth"
    18:for plugin in $(find . -maxdepth 1 -mindepth 1 -type d -name 'jellyfin-plugin-*' | sort); do

So there is nothing to submit and nothing to satisfy in the sense of a checklist. What that
repository publishes about itself is a set of build and release tools rather than a set of
conditions a candidate is measured against:

    gh api repos/jellyfin/jellyfin-meta-plugins/contents/README.md --jq '.content' | base64 -d | head -2
    Plugin tools
    ============

**The list below is therefore derived from the tooling that would build this repository, not from a
document anybody publishes.** Anything offered here as a requirement that is not read off that
tooling would be a claim about a process nobody outside it can see.

## Checked against this repository

| What the route needs                               | Where that is decided                        | Here                      |
| -------------------------------------------------- | -------------------------------------------- | ------------------------- |
| A name beginning `jellyfin-plugin-`                | `update_submodules.py:59`, `build_all.sh:18` | met                       |
| A repository in the `jellyfin` organisation        | `update_submodules.py:43`                    | not met, declined on #110 |
| A `build.yaml` at the root carrying `version`      | `build_plugin.sh`                            | met                       |
| One plugin per repository, built from `build.yaml` | `build_plugin.sh`                            | not met, #110             |

The name is met, and it is met by accident rather than by decision: this repository is called
`jellyfin-plugin-requests` because that is what a Jellyfin plugin repository is called.

    gh repo view Flowfin/jellyfin-plugin-requests --json name,owner --jq '"\(.owner.login)/\(.name)"'
    Flowfin/jellyfin-plugin-requests

The organisation is the whole of the gate and it is not something a change in this tree can meet. It
is the submission decision, and the section at the end of this page says where that stands.

The `build.yaml` requirement is met and the command that reads it is worth quoting, because it reads
one file by name:

    gh api repos/jellyfin/jellyfin-meta-plugins/contents/build_plugin.sh --jq '.content' \
      | base64 -d | grep -n 'meta_version=\|JPRM --verbosity'
    53:meta_version=$(grep -Po '^ *version: * "*\K[^"$]+' "${PLUGIN}/build.yaml")
    61:zipfile=$($JPRM --verbosity=debug plugin build "${PLUGIN}" --output="${ARTIFACT_DIR}" --version="${VERSION}") && {
    62:    $JPRM --verbosity=debug repo add --url=${JELLYFIN_REPO_URL} "${JELLYFIN_REPO}" "${zipfile}"

**That route builds one package per repository, and this repository claims two server lines.**
`build.yaml` is the 10.11 line and `build-jf12.yaml` is the 12.0 line, and nothing in that script
looks for a second file. A repository built by it offers the 10.11 package and nothing for the other
line. Carrying both lines into one manifest is #110, and it is named here because it is the same
problem seen from the catalogue's side rather than a new one.

One more property of that route, which is a fact rather than a requirement. The version in the
package is not the version in `build.yaml`:

    gh api repos/jellyfin/jellyfin-meta-plugins/contents/build_plugin.sh --jq '.content' \
      | base64 -d | grep -n 'VERSION_SUFFIX:-\|VERSION IS OVERWRITTEN'
    51:VERSION_SUFFIX=${VERSION_SUFFIX:-$(date -u +%y%m.%d%H.%M%S)}
    56:# !!! VERSION IS OVERWRITTEN HERE

The last three segments are replaced by a timestamp of the build. An operator installing from the
official catalogue therefore sees a version this repository never minted, which is worth knowing
before reading [versioning.md](versioning.md) as though it described what such a user would see.

## The licence position for a compiled plugin

This is not a free choice and it is not a formality. A plugin is compiled against the server's
libraries and shipped as an assembly that links them, so the terms those libraries carry reach the
compiled result whatever a source file says.

Three facts, and they do not agree with each other.

This repository is GPL-3.0:

    head -2 LICENSE
                        GNU GENERAL PUBLIC LICENSE
                           Version 3, 29 June 2007

The packages this plugin compiles against declare GPL-3.0-only in their own metadata, on both
claimed lines:

    grep -h '<license type' ~/.nuget/packages/jellyfin.controller/10.11.11/jellyfin.controller.nuspec \
        ~/.nuget/packages/jellyfin.controller/12.0.0-rc4/jellyfin.controller.nuspec
        <license type="expression">GPL-3.0-only</license>
        <license type="expression">GPL-3.0-only</license>

The server's own repository carries the second version of that licence rather than the third:

    gh api repos/jellyfin/jellyfin/license --jq '{spdx: .license.spdx_id, name: .license.name}'
    {"name":"GNU General Public License v2.0","spdx":"GPL-2.0"}

    gh api repos/jellyfin/jellyfin/contents/LICENSE --jq '.content' | base64 -d | sed -n '2p'
                           Version 2, June 1991

**What this repository does follows from the packages, because they are what it links.** They say
GPL-3.0-only, this repository is GPL-3.0, and a compiled plugin distributed to anybody is under
those terms rather than under something chosen here. A permissive licence on the source would not
change what the compiled result may be distributed under.

**What is not resolved here is the disagreement between the package metadata and the server
repository's own licence file.** Whether the GPL-2.0 in that file carries an "or later" election is
not answerable from the file itself: the sentence that offers the choice appears in the appendix
that the text of GPL-2.0 carries in every copy of it, and an election is a statement a project makes
about its own program rather than a line of that appendix. If the operative terms were GPL-2.0 with
no such election, a GPL-3.0 work could not be distributed as one with it, and that is a question for
whoever distributes rather than something this tree can measure.

Nothing in the catalogue tooling checks a licence at all, which is why this section is here rather
than in the table above.

## The submission decision

**Declined.** This repository does not move into the `jellyfin` organisation, and the plugin is
distributed from a manifest under Flowfin's control instead. That was settled on #110, together with
the shape of that manifest, and the two are one answer rather than two: the enumeration above is the
only route into the official catalogue, so declining the move is declining the catalogue.

The decision was never whether to send an entry somewhere, because there is nowhere to send one. It
is whether this repository moves, which carries who owns it, who can publish a release from it and
under whose rules it is maintained. None of those is a packaging question, which is why the answer
is recorded here rather than read off the table above.

**What it costs is that most people never find this plugin.** The list every Jellyfin server already
shows will not carry it, so an operator reaches it only by being told the manifest URL and adding it
by hand. That price is accepted rather than argued away, and it is the larger half of what the
decision buys.

What it keeps is the repository, the release route already in [RELEASING.md](RELEASING.md), and
version numbers minted here rather than replaced by the timestamp the catalogue's build script
writes over them. The two-line packaging problem in the table above stays this board's own, which it
would have been under either answer.

## The address an operator adds, and what it carries today

    https://flowfin.dev/manifest.json

That is the manifest the decision above distributes from. Until this section no file on this board
named it: the decision said self-hosted under Flowfin's control and stopped there, so the one thing
an operator has to type had nowhere to be read from, and the price accepted above -- that somebody
reaches this plugin only by being told the URL -- was being paid without the URL being written down.

Read back rather than asserted, on 2026-08-28:

    curl -sS -o manifest.json -w '%{http_code}\n' https://flowfin.dev/manifest.json
    200

    jq -r '.[] | select(.name == "Requests") | .versions[] | [.version, .targetAbi] | @tsv' manifest.json
    0.2.0.0 10.11.0.0
    0.1.0.0 10.11.0.0

### The checksums are the ones the packages hash to

The one field of an entry that describes bytes rather than metadata, checked against the archives
the entries name rather than against the `.md5` published beside them:

    curl -sSL -O https://github.com/Flowfin/jellyfin-plugin-requests/releases/download/0.1.0.0-stable/requests_0.1.0.0.zip
    curl -sSL -O https://github.com/Flowfin/jellyfin-plugin-requests/releases/download/0.2.0.0-stable/requests_0.2.0.0.zip
    md5sum requests_0.1.0.0.zip requests_0.2.0.0.zip
    1167d5e454c800bc024d98a6899cdb4c *requests_0.1.0.0.zip
    76c0a82e31d04228e7daf1f67383182d *requests_0.2.0.0.zip

Both equal the `checksum` of their entry, so a server that downloads either one and hashes it gets
the value the manifest promised.

### Both entries claim the 10.11 line, and the 12.0 line has none

The scheme is one entry per server line, each carrying its line's `targetAbi`. What is published is
two entries for one line. This board claims two:

    grep -nE '^(version|targetAbi|framework):' build.yaml build-jf12.yaml
    build.yaml:5:version: "0.2.0.0"
    build.yaml:10:targetAbi: "10.11.0.0"
    build.yaml:11:framework: "net9.0"
    build-jf12.yaml:13:version: "0.2.0.0"
    build-jf12.yaml:15:targetAbi: "12.0.0.0"
    build-jf12.yaml:16:framework: "net10.0"

and the release route builds the one `build.yaml` names, which is why there is no second package for
an entry to point at. `publish.yaml` says so about itself in its own header and refuses a `framework`
that is not the one it builds.

**So a server on the 12.0 line is offered the `net9.0` build.** A server keeps every entry whose
`targetAbi` is at or below its own version and then takes the highest version number of what is
left, read at the 10.11 line's own source on 2026-08-28:

    gh api "repos/jellyfin/jellyfin/contents/Emby.Server.Implementations/Updates/InstallationManager.cs?ref=release-10.11.z" \
      -H "Accept: application/vnd.github.raw" \
      | grep -nE 'Version.Parse\(x.TargetAbi\) <= appVer|OrderByDescending\(x => x.VersionNumber\)'
    266:                .Where(x => string.IsNullOrEmpty(x.TargetAbi) || Version.Parse(x.TargetAbi) <= appVer);
    277:            foreach (var v in availableVersions.OrderByDescending(x => x.VersionNumber))

`10.11.0.0` is at or below `12.0.0.0`, so the filter keeps it and there is nothing else for the
ordering to prefer.

**Nothing here was installed into a server.** The address was fetched and the archives were hashed;
no Jellyfin was started, no repository was added to one, and no install was attempted on either
line. What the paragraph above describes is the comparison the server's source makes, not an install
anybody watched take the wrong package.

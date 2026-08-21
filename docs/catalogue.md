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

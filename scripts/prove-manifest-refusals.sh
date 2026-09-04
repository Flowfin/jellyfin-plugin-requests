#!/usr/bin/env bash
# Every refusal the manifest generator claims, watched refusing once, over a manifest written to
# carry the defect it names.
#
# `scripts/build-manifest.sh` writes the document a Jellyfin server fetches to find out that a
# version of this plugin exists. Nothing downstream of it checks that document, so a generator that
# has never said no is a claim: it writes a manifest for a clean pair of packages, which is also
# what a generator with no rules in it does.
#
# The two rules worth the harness are the ordering rules, because they are the ones a reader has to
# take on trust. A server keeps every entry whose targetAbi is at or below its own version and then
# picks the highest version number of what is left, so a pair of packages the manifest cannot
# separate installs the wrong build on a real server and reports success. Neither shape is visible
# in the JSON, and both are one edit away from the correct manifest beside them.
#
# The fixtures are written here rather than committed. An entry is described by its `.meta.json`
# and the archive is read only to be hashed, so a fixture archive is a few bytes of text and its
# contents mean nothing: what is being proved is the rule over the metadata, and a real zip would
# add a binary to the tree that proves nothing extra.
#
# It needs no container, no server and no network, so it runs in seconds.
#
# usage: scripts/prove-manifest-refusals.sh

set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
generator="$here/build-manifest.sh"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

prefix="https://github.com/Flowfin/jellyfin-plugin-requests/releases/download/0.1.0.0-stable/"

# One package: a file to hash and the metadata the packaging tool writes beside it.
# usage: package <name> <version> <targetAbi> [<guid>]
package() {
    local name=$1 version=$2 abi=$3 guid=${4:-0f9c9107-b31b-459e-81fa-6d35dac25e79}
    printf 'not a real archive, and nothing here reads it as one\n' > "$work/$name"
    cat > "$work/$name.meta.json" <<META
{
    "category": "General",
    "changelog": "changelog\n",
    "description": "A user who cannot find something asks for it from inside Jellyfin.",
    "guid": "$guid",
    "imageUrl": "https://raw.githubusercontent.com/Flowfin/jellyfin-plugin-requests/master/img/logo.png",
    "name": "Requests",
    "overview": "A user asks the server for a film or a series.",
    "owner": "Flowfin",
    "targetAbi": "$abi",
    "timestamp": "2026-08-08T09:39:09Z",
    "version": "$version"
}
META
}

failures=0

# usage: refuses <heading> <sentence the refusal must carry> -- <command...>
refuses() {
    local heading=$1 sentence=$2
    shift 3
    printf '\n== %s\n' "$heading"
    local out status
    set +e
    out=$("$@" 2>&1)
    status=$?
    set -e
    printf '%s\n' "$out" | sed 's/^/  /'
    if [ "$status" -eq 0 ]; then
        echo "  ACCEPTED it, so the rule does not bite."
        failures=$((failures + 1))
        return
    fi
    case "$out" in
        *"$sentence"*) ;;
        *)
            echo "  refused, but for something other than: $sentence"
            failures=$((failures + 1))
            ;;
    esac
}

printf '\n== a pair of packages the server can tell apart passes\n'
package requests_0.1.0.0.zip 0.1.0.0 10.11.0.0
package requests_0.2.0.0.zip 0.2.0.0 12.0.0.0
if out=$(cd "$work" && SOURCE_URL_PREFIX="$prefix" "$generator" clean.json \
    requests_0.1.0.0.zip requests_0.2.0.0.zip 2>&1); then
    printf '%s\n' "$out" | sed 's/^/  /'
    # The newer version has to be the one for the newer line, otherwise the pair only passed
    # because the rule was not reached.
    if [ "$(jq -r '.[0].versions[0].targetAbi' "$work/clean.json")" != "12.0.0.0" ]; then
        echo "  the newest entry is not the 12.0 one, so this fixture is not the clean case."
        failures=$((failures + 1))
    fi
else
    printf '%s\n' "$out" | sed 's/^/  /'
    echo "  REFUSED the clean pair, which is a generator that refuses everything."
    failures=$((failures + 1))
fi

printf '\n== a release adds its versions to what is already published\n'
package requests_0.3.0.0.zip 0.3.0.0 12.0.0.0
if out=$(cd "$work" && MANIFEST_BASE=clean.json SOURCE_URL_PREFIX="$prefix" "$generator" \
    merged.json requests_0.3.0.0.zip 2>&1); then
    printf '%s\n' "$out" | sed 's/^/  /'
    if [ "$(jq '.[0].versions | length' "$work/merged.json")" != "3" ]; then
        echo "  the already published versions did not survive the merge."
        failures=$((failures + 1))
    fi
else
    printf '%s\n' "$out" | sed 's/^/  /'
    echo "  REFUSED a release added to a published manifest."
    failures=$((failures + 1))
fi

# The state this repository is in today: both packaging files carry one version number, so the two
# packages are one entry to every server that accepts both.
package requests_a.zip 0.1.0.0 10.11.0.0
package requests_b.zip 0.1.0.0 12.0.0.0
refuses "two packages at one version number" \
    "is carried by two entries" -- \
    env -C "$work" SOURCE_URL_PREFIX="$prefix" "$generator" out.json requests_a.zip requests_b.zip

# The near miss: System.Version reads an absent fourth field as zero, so these two strings are one
# version. Comparing the strings would let the pair through.
package requests_c.zip 0.2.0 10.11.0.0
package requests_d.zip 0.2.0.0 12.0.0.0
refuses "one version number written with a different number of parts" \
    "is carried by two entries" -- \
    env -C "$work" SOURCE_URL_PREFIX="$prefix" "$generator" out.json requests_c.zip requests_d.zip

# The other direction, and the expensive one: each entry is fine on its own and the pair sends the
# newer server the older line's build.
package requests_e.zip 0.3.0.0 10.11.0.0
package requests_f.zip 0.2.0.0 12.0.0.0
refuses "the newer version claims the older server line" \
    "is the one for the older server line" -- \
    env -C "$work" SOURCE_URL_PREFIX="$prefix" "$generator" out.json requests_e.zip requests_f.zip

package requests_g.zip 0.4.0.0 12.0.0.0 11111111-2222-3333-4444-555555555555
refuses "the two packages claim different plugins" \
    "holds one entry per plugin" -- \
    env -C "$work" SOURCE_URL_PREFIX="$prefix" "$generator" out.json requests_a.zip requests_g.zip

# The same refusal reached through a field nobody would call an identity, and this is the shape it
# arrives in. `guid` above is a divergence somebody has to mean; a per-line sentence in the
# packaging prose is an ordinary documentation edit, and the generator holds seven fields once per
# plugin rather than the two a reader remembers. Written as its own case because a proof that only
# ever drives `guid` says nothing about the other six.
#
# The pair is otherwise legal - two version numbers, the newer one on the newer line - so the
# only thing wrong with it is the sentence, and the refusal cannot be the duplicate-version rule
# arriving first under another name.
package requests_k.zip 0.6.0.0 10.11.0.0
package requests_l.zip 0.7.0.0 12.0.0.0
python3 - "$work/requests_l.zip.meta.json" <<'DIVERGE'
import json
import sys

path = sys.argv[1]
with open(path, encoding="utf-8") as handle:
    document = json.load(handle)
document["description"] += " On the 12.0 line, which is the sentence that arrives per line."
with open(path, "w", encoding="utf-8", newline="\n") as handle:
    json.dump(document, handle, indent=4, sort_keys=True)
DIVERGE
refuses "two packages whose prose diverges on one line" \
    "holds one entry per plugin" -- \
    env -C "$work" SOURCE_URL_PREFIX="$prefix" "$generator" out.json requests_k.zip requests_l.zip

package requests_h.zip 0.5.0.0 12.0.0.0
python3 - "$work/requests_h.zip.meta.json" <<'PY'
import json
import sys

path = sys.argv[1]
with open(path, encoding="utf-8") as handle:
    document = json.load(handle)
del document["targetAbi"]
with open(path, "w", encoding="utf-8", newline="\n") as handle:
    json.dump(document, handle, indent=4, sort_keys=True)
PY
refuses "a package whose metadata names no server line" \
    "declares no 'targetAbi'" -- \
    env -C "$work" SOURCE_URL_PREFIX="$prefix" "$generator" out.json requests_h.zip

package requests_i.zip 0.6.0.rc1 12.0.0.0
refuses "a version a server cannot parse" \
    "has a part that is not a number" -- \
    env -C "$work" SOURCE_URL_PREFIX="$prefix" "$generator" out.json requests_i.zip

package requests_j.zip 0.7.0.0 12.0.0.0
rm -f "$work/requests_j.zip.meta.json"
refuses "an archive with no metadata beside it" \
    "meta.json does not exist" -- \
    env -C "$work" SOURCE_URL_PREFIX="$prefix" "$generator" out.json requests_j.zip

refuses "an archive that was never built" \
    "does not exist" -- \
    env -C "$work" SOURCE_URL_PREFIX="$prefix" "$generator" out.json requests_never_built.zip

refuses "a download URL that would name a sibling of the directory" \
    "does not end in a slash" -- \
    env -C "$work" SOURCE_URL_PREFIX="https://example.invalid/download" "$generator" \
        out.json requests_a.zip

refuses "a published manifest read back as missing" \
    "which does not exist" -- \
    env -C "$work" MANIFEST_BASE=nothing-here.json SOURCE_URL_PREFIX="$prefix" "$generator" \
        out.json requests_a.zip

refuses "a manifest written from no packages at all" \
    "no package was named" -- \
    env -C "$work" SOURCE_URL_PREFIX="$prefix" "$generator" out.json

printf '\n'
if [ "$failures" -ne 0 ]; then
    echo "$failures rule(s) did not bite."
    exit 1
fi
echo "Every rule bit, for the reason it names."

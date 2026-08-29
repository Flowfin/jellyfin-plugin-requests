#!/usr/bin/env bash
# Every refusal the freshness check claims, watched refusing once, over a manifest and a release
# listing written to carry the answer it names.
#
# `scripts/check-manifest-is-fresh.sh` is what decides whether the document a server fetches still
# matches the releases that exist. It runs on a schedule against the live manifest, which is a thing
# nobody can produce on demand and which is green on the day it lands - so a reader that had never
# said no would look exactly the same as one that says yes to everything, for as long as nothing goes
# wrong. That is precisely the situation the incident behind #111 arose in.
#
# It takes its inputs as files, so every answer it can give is handed to it here directly, with no
# network, no release and no manifest of anybody's.
#
# THE ONE IT EXISTS FOR IS THE STALE MANIFEST. A publish that created the release and failed to write
# the document leaves a manifest that parses, carries this plugin's entry, and offers the version
# before the one that just went out. Nothing about it looks broken. It is the fifth case below.
#
# usage: scripts/prove-manifest-freshness-refusals.sh

set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/.." && pwd)
checker="$here/check-manifest-is-fresh.sh"

cd "$root"

guid=$(sed -n 's/^guid: *"\([^"]*\)".*/\1/p' build.yaml | head -1)
base_abi=$(sed -n 's/^targetAbi: *"\([^"]*\)".*/\1/p' build.yaml | head -1)
jf12_abi=$(sed -n 's/^targetAbi: *"\([^"]*\)".*/\1/p' build-jf12.yaml | head -1)

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# usage: manifest <file> <json for the versions array>
manifest() {
    local file=$1 versions=$2
    cat > "$work/$file" <<JSON
[
  {
    "name": "Something Else",
    "guid": "11111111-2222-3333-4444-555555555555",
    "versions": []
  },
  {
    "name": "Requests",
    "guid": "${guid}",
    "versions": ${versions}
  }
]
JSON
}

# usage: at <version> <targetAbi>
at() {
    printf '{"version":"%s","targetAbi":"%s","checksum":"0","sourceUrl":"https://example.invalid/x.zip"}' "$1" "$2"
}

releases() {
    local file=$1
    shift
    printf '%s\n' "$@" > "$work/$file"
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

# usage: accepts <heading> <sentence the pass must carry> -- <command...>
accepts() {
    local heading=$1 sentence=$2
    shift 3
    printf '\n== %s\n' "$heading"
    local out status
    set +e
    out=$("$@" 2>&1)
    status=$?
    set -e
    printf '%s\n' "$out" | sed 's/^/  /'
    if [ "$status" -ne 0 ]; then
        echo "  REFUSED it, which is a reader that refuses everything."
        failures=$((failures + 1))
        return
    fi
    case "$out" in
        *"$sentence"*) ;;
        *)
            echo "  accepted, but said nothing about: $sentence"
            failures=$((failures + 1))
            ;;
    esac
}

# The state of the world this check has to pass: one line with releases and a manifest that offers
# the newest of them, one claimed line with no release at all.
manifest good.json "[$(at 0.2.0.0 "$base_abi"),$(at 0.1.0.0 "$base_abi")]"
releases two.txt '0.2.0.0-stable' '0.1.0.0-stable'
accepts "the manifest offers the newest release of every line that has one" \
    "Every claimed line with a release offers that release in the manifest a server would fetch" \
    -- "$checker" "$work/good.json" "$work/two.txt"

accepts "a claimed line with no release is reported rather than passed over in silence" \
    "has no release, so nothing about it is compared" \
    -- "$checker" "$work/good.json" "$work/two.txt"

# The trap. The release went out, the manifest write did not, and everything about the document still
# parses and still carries this plugin.
manifest stale.json "[$(at 0.1.0.0 "$base_abi")]"
refuses "the release went out and the manifest still offers the one before it" \
    "the newest the manifest offers at targetAbi ${base_abi} is 0.1.0.0" \
    -- "$checker" "$work/stale.json" "$work/two.txt"

manifest empty.json "[]"
refuses "the manifest carries the entry and nothing under it" \
    "the manifest carries no entry at targetAbi ${base_abi} at all" \
    -- "$checker" "$work/empty.json" "$work/two.txt"

# The entry is gone rather than stale, which is what the publish that shipped nothing installable
# left behind on the sibling board.
cat > "$work/absent.json" <<JSON
[{"name":"Something Else","guid":"11111111-2222-3333-4444-555555555555","versions":[]}]
JSON
refuses "the manifest has no entry for this plugin at all" \
    "the manifest carries no entry for ${guid}" \
    -- "$checker" "$work/absent.json" "$work/two.txt"

# The other direction: the document offers a line a release never went out for. A server would be
# handed a download URL for something that does not exist.
manifest phantom.json "[$(at 0.2.0.0 "$base_abi"),$(at 0.9.0.0 "$jf12_abi")]"
refuses "the manifest offers a line that has no release behind it" \
    "and no release exists for the jf12 line" \
    -- "$checker" "$work/phantom.json" "$work/two.txt"

# A release naming a line the packaging files do not claim. Either a line was dropped with its
# releases still standing, or a tag named something that never existed.
releases unclaimed.txt '0.2.0.0-stable' '0.3.0.0-jf99-stable'
refuses "a release names a server line this repository does not claim" \
    "this repository claims no such line" \
    -- "$checker" "$work/good.json" "$work/unclaimed.txt"

# A fetch that returned something rather than nothing. A proxy error page and a 404 body both arrive
# here as a file that is not the document.
printf '<html><title>404</title></html>\n' > "$work/notjson.json"
refuses "the fetch returned a page rather than a manifest" \
    "is not a JSON array" \
    -- "$checker" "$work/notjson.json" "$work/two.txt"

# JSON, but not the shape a manifest is. This is the near-miss beside the one above: a reader that
# only asked whether the bytes parse would pass it.
printf '{"versions":[]}\n' > "$work/object.json"
refuses "the fetch returned JSON that is not a manifest" \
    "is not a JSON array" \
    -- "$checker" "$work/object.json" "$work/object.json"

refuses "the manifest was never fetched" \
    "does not exist" \
    -- "$checker" "$work/no-such.json" "$work/two.txt"

refuses "the release listing was never taken" \
    "does not exist" \
    -- "$checker" "$work/good.json" "$work/no-such.txt"

# The near-miss on the ordering, and the reason `sort -V` is not what compares two versions here. Ten
# is above nine everywhere except in a comparison made character by character, and a check that got
# this wrong would report a fresh manifest as stale on the tenth release of a line.
manifest ten.json "[$(at 0.10.0.0 "$base_abi"),$(at 0.9.0.0 "$base_abi")]"
releases ten.txt '0.9.0.0-stable' '0.10.0.0-stable'
accepts "0.10.0.0 is newer than 0.9.0.0, in the listing and in the manifest" \
    "newest release 0.10.0.0" \
    -- "$checker" "$work/ten.json" "$work/ten.txt"

printf '\n'
if [ "$failures" -ne 0 ]; then
    echo "$failures of the rules above did not bite." >&2
    exit 1
fi
echo "Every refusal the freshness check claims was watched refusing, and the state the board is in passes."

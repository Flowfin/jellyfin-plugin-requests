#!/usr/bin/env bash
# Every refusal `scripts/check-released-package.sh` claims, watched refusing once, over archives and
# sidecars written here to carry the answer each one names.
#
# The reader it proves runs against what a publish actually attached to a release, which is a thing
# nobody can produce on demand and which is green for as long as nothing goes wrong with a publish.
# A reader that had never said no would look exactly like one that says yes to everything, for
# exactly as long as that lasts.
#
# It takes its inputs as files, so every answer is handed to it directly here: no network, no
# release, and no server.
#
# THE ONE IT EXISTS FOR IS THE THIRD CASE. An archive that unpacks, beside a sidecar promising a
# different digest, is a release where the attestation and the bytes disagree - and an operator who
# checks the download is the only party who finds out, because everything else about such a release
# reads as correct.
#
# usage: scripts/prove-released-install-refusals.sh

set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/.." && pwd)
reader="$here/check-released-package.sh"

cd "$root"

# The line this repository claims under build.yaml, read rather than written, so a fixture that
# should be accepted stays accepted on the day a floor moves.
base_abi=$(sed -n 's/^targetAbi: *"\([^"]*\)".*/\1/p' build.yaml | head -1)
base_framework=$(sed -n 's/^framework: *"\([^"]*\)".*/\1/p' build.yaml | head -1)
# The floor server that claim names, derived the same way the reader derives it. Written out here so
# the expectation moves with the claim rather than naming a version this file would have to be
# edited for.
base_floor=${base_abi%.*}

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# An archive a server could unpack, built rather than committed: what matters about it is that it is
# a real zip, and a committed one would be bytes nobody can see the shape of in a review.
archive() {
    python3 - "$work/$1" <<'PY'
import sys, zipfile
with zipfile.ZipFile(sys.argv[1], "w") as bundle:
    bundle.writestr("Jellyfin.Plugin.Requests.dll", "not an assembly, and nothing here loads it")
    bundle.writestr("meta.json", "{}")
PY
}

# usage: sidecar <file> <archive-it-names>
sidecar() {
    sha256sum "$work/$2" | sed "s@$work/@@" > "$work/$1"
}

# usage: metadata <file> <json>
metadata() {
    printf '%s\n' "$2" > "$work/$1"
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

archive good.zip
sidecar good.sha256 good.zip
metadata good.json "{\"version\":\"1.2.3.4\",\"targetAbi\":\"${base_abi}\"}"

accepts "the shape a publish attaches is read and answers which line it is for and which server is its floor" \
    "which is the line ${base_framework} packages for and the floor server ${base_floor}" \
    -- "$reader" "$work/good.zip" "$work/good.sha256" "$work/good.json" "$work/answers.txt"

# The answers are what the install step is handed, so a reader that passed and wrote nothing usable
# would leave the step after it installing at a floor nobody derived. `floor=` is the one the install
# step cannot recover on its own: it decides which server the package is put on, and a missing line
# there is an install that silently runs at the newest server of the line and reads afterwards as one
# that ran at the oldest.
printf '\n== what the accepted run wrote for the step after it\n'
if [ ! -f "$work/answers.txt" ]; then
    echo "  it wrote no answers file at all, so the step after it has no floor to install at."
    failures=$((failures + 1))
else
    sed 's/^/  /' "$work/answers.txt"
    for expected in "version=1.2.3.4" "targetAbi=${base_abi}" "framework=${base_framework}" "floor=${base_floor}"; do
        if ! grep -qxF "$expected" "$work/answers.txt"; then
            echo "  the answers file does not carry ${expected}."
            failures=$((failures + 1))
        fi
    done
fi

# The trap. The bytes and the attestation published beside them disagree, and nothing else about the
# release looks wrong.
metadata other.json "{\"version\":\"1.2.3.4\",\"targetAbi\":\"${base_abi}\"}"
printf '%s  good.zip\n' "0000000000000000000000000000000000000000000000000000000000000000" > "$work/wrong.sha256"
refuses "the archive is not what the release publishes a digest for" \
    "and the release publishes 0000000000000000000000000000000000000000000000000000000000000000 beside it" \
    -- "$reader" "$work/good.zip" "$work/wrong.sha256" "$work/other.json" "$work/answers.txt"

printf 'not a zip, and no server unpacks it\n' > "$work/plain.zip"
sidecar plain.sha256 plain.zip
refuses "the asset attached is not an archive at all" \
    "is not an archive that unpacks" \
    -- "$reader" "$work/plain.zip" "$work/plain.sha256" "$work/good.json" "$work/answers.txt"

metadata no-abi.json '{"version":"1.2.3.4"}'
refuses "the release metadata names no server line" \
    "names no targetAbi" \
    -- "$reader" "$work/good.zip" "$work/good.sha256" "$work/no-abi.json" "$work/answers.txt"

# Three parts rather than four, which is the shape a person writes when they mean a server version
# instead of an assembly version. It is refused for the floor rather than for the line, because the
# floor is what cannot be derived from it and the message a reader gets has to say which.
metadata short-abi.json '{"version":"1.2.3.4","targetAbi":"10.11.0"}'
refuses "the release metadata names a targetAbi with no floor server in it" \
    "which is not four numeric parts" \
    -- "$reader" "$work/good.zip" "$work/good.sha256" "$work/short-abi.json" "$work/answers.txt"

metadata unclaimed.json '{"version":"1.2.3.4","targetAbi":"99.9.9.9"}'
refuses "the release names a line no packaging file at the root claims" \
    "no packaging file at the root claims it" \
    -- "$reader" "$work/good.zip" "$work/good.sha256" "$work/unclaimed.json" "$work/answers.txt"

metadata no-version.json "{\"targetAbi\":\"${base_abi}\"}"
refuses "the release metadata names no version" \
    "names no version" \
    -- "$reader" "$work/good.zip" "$work/good.sha256" "$work/no-version.json" "$work/answers.txt"

printf '' > "$work/empty.sha256"
refuses "the sidecar carries no digest to compare against" \
    "carries no digest on its first line" \
    -- "$reader" "$work/good.zip" "$work/empty.sha256" "$work/good.json" "$work/answers.txt"

refuses "the archive was never downloaded" \
    "does not exist" \
    -- "$reader" "$work/no-such.zip" "$work/good.sha256" "$work/good.json" "$work/answers.txt"

refuses "the sidecar was never downloaded" \
    "does not exist" \
    -- "$reader" "$work/good.zip" "$work/no-such.sha256" "$work/good.json" "$work/answers.txt"

refuses "the metadata was never downloaded" \
    "does not exist" \
    -- "$reader" "$work/good.zip" "$work/good.sha256" "$work/no-such.json" "$work/answers.txt"

printf '\n'
if [ "$failures" -ne 0 ]; then
    echo "$failures of the rules above did not bite." >&2
    exit 1
fi
echo "Every refusal the released-package reader claims was watched refusing, and the shape a publish attaches passes."

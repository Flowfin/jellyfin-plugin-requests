#!/usr/bin/env bash
# Does this package ask a server for more than its targetAbi claims?
#
# WHY THIS EXISTS. `abi-floor.yaml` compiles the SOURCE against the floor SDK and says nothing about
# the artefact. `package.yaml` and `publish.yaml` build the artefact and never compared what they
# produced against the floor the packaging file claims. Between the two sat the class that shipped:
# `0.2.0.0` went out asking a `10.11.0` server for five assemblies at `10.11.11.0`, that server
# carries them at `10.11.0.0`, a reference above what the host carries does not bind, and the server
# reported the plugin `NotSupported`. Nothing has to be called for that to happen, so every route was
# green while it shipped. The measurement is on #152 and the refusal is #360.
#
# WHAT IT READS. The assembly reference table of every assembly the package carries, printed by
# `tools/package-abi`, against the `targetAbi` of the packaging file the package was built from.
# Both are in hand at package time. It reaches no network, starts no server and needs no container.
#
# WHICH REFERENCES IT JUDGES. The host's own assemblies, which are the ones a server supplies and a
# package must not out-ask: names beginning `MediaBrowser.` or `Jellyfin.`. A reference to an
# assembly the package itself carries is not one of those, however it is named, so a plugin that
# ships a helper assembly of its own is judged on what it asks the SERVER for.
#
# A SET IT COULD NOT READ IS A REFUSAL AND NEVER A PASS. An archive with no assembly in it, an
# assembly the reader cannot open, and an assembly carrying no host reference at all each print the
# same nothing as a package that is within its claim. A reader that passes on all three is a reader
# that passes on a broken build, which is the failure this whole check is about arriving one level
# up.
#
# usage: scripts/check-package-abi.sh <package> <targetAbi>
#   <package> is the built archive or the directory it unpacks to
#   <targetAbi> is the four part number the packaging file for that line declares

set -euo pipefail

package=${1:?path to the package archive or the directory it unpacks to}
claim=${2:?the targetAbi the packaging file for this line declares}

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/.." && pwd)

tab=$(printf '\t')

refuse() {
    echo "check-package-abi: $1" >&2
    exit 1
}

case "$claim" in
    [0-9]*.[0-9]*.[0-9]*.[0-9]*) ;;
    *) refuse "targetAbi '${claim}' is not four numeric parts, so there is no version for a reference to be compared against." ;;
esac

if [ ! -e "$package" ]; then
    refuse "${package} does not exist. This reads what a packaging step actually produced, never what it was expected to produce."
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

if [ -d "$package" ]; then
    contents="$package"
else
    if ! unzip -q -o "$package" -d "$work/unpacked" 2>/dev/null; then
        refuse "${package} is not an archive that unpacks, so nothing in it can be read. A server handed this asset cannot install it either."
    fi
    contents="$work/unpacked"
fi

# Depth is not bounded. The packaging tool puts the artefacts at the root of the archive today, and a
# reader that only looked there would pass a package whose assemblies moved one directory down.
find "$contents" -type f -name '*.dll' | sort > "$work/assemblies.txt"
if [ ! -s "$work/assemblies.txt" ]; then
    refuse "${package} carries no assembly, so it has no reference table to read. A package with nothing in it installs and does nothing, and this check cannot tell that apart from one that is within its claim."
fi

# What the package brings with it. A reference to one of these is answered by the package itself and
# is not a demand on the server, whatever the assembly is called.
while read -r assembly; do
    basename "$assembly" .dll
done < "$work/assemblies.txt" | sort -u > "$work/carried.txt"

: > "$work/references.txt"
while read -r assembly; do
    if ! dotnet run --project "$root/tools/package-abi" -- "$assembly" > "$work/one.txt" 2> "$work/reader.txt"; then
        sed 's/^/  /' "$work/reader.txt" >&2
        refuse "the reference table of ${assembly} could not be read, so what this package asks a server for is unknown. An unknown answer is refused rather than passed."
    fi
    awk -v carrier="$(basename "$assembly")" 'BEGIN { OFS = "\t" } { print carrier, $0 }' "$work/one.txt" >> "$work/references.txt"
done < "$work/assemblies.txt"

# The host's own assemblies, minus anything the package carries under one of those names.
awk -F'\t' '
    NR == FNR { carried[$0] = 1; next }
    $2 ~ /^(MediaBrowser|Jellyfin)\./ && !($2 in carried) { print }
' "$work/carried.txt" "$work/references.txt" > "$work/host.txt"

if [ ! -s "$work/host.txt" ]; then
    refuse "no assembly in ${package} references a Jellyfin or MediaBrowser assembly at all. A plugin that asks the server for nothing is a build that went wrong somewhere earlier, and its reference set cannot be compared with a claim of ${claim}."
fi

# Component by component as integers, never as strings: 10.11.9.0 sorts above 10.11.11.0 in a string
# comparison and below it in the comparison a runtime makes.
awk -F'\t' -v claim="$claim" '
    function part(v, i,   a) { split(v, a, "."); return a[i] + 0 }
    {
        for (i = 1; i <= 4; i++) {
            mine = part($3, i); theirs = part(claim, i)
            if (mine > theirs) { print; next }
            if (mine < theirs) { next }
        }
    }
' "$work/host.txt" > "$work/above.txt"

echo "What ${package} asks a server for, against the targetAbi ${claim} it claims:"
sort -u -t"$tab" -k2,3 "$work/host.txt" | awk -F'\t' '{ printf "  %s references %s at %s\n", $1, $2, $3 }'

if [ -s "$work/above.txt" ]; then
    while IFS="$tab" read -r carrier name version; do
        echo "check-package-abi: ${carrier} references ${name} at ${version}, and this package claims targetAbi ${claim}. A server of the claimed floor carries ${name} below ${version}, the reference does not bind, and the server reports the plugin NotSupported after a download that looked like it worked." >&2
    done < "$work/above.txt"
    refuse "this package asks a server for more than its targetAbi claims. Build the package against the floor the packaging file declares, or move the claim - and moving the claim narrows every server this plugin reaches."
fi

echo "check-package-abi: every host reference is at or below ${claim}, so a server of the claimed floor carries all of them."

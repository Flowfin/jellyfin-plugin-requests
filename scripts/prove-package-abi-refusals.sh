#!/usr/bin/env bash
# Every answer `scripts/check-package-abi.sh` gives, watched being given, over packages built here.
#
# THE TWO IT EXISTS FOR ARE THE FIRST TWO. A package built against the floor its packaging file
# claims is accepted, and the same source built one published version above that floor is refused.
# Those are the two states the class this check is about actually took: `0.2.0.0` shipped as the
# second while every route was green, and the reading that found it is on #152.
#
# A CHECK THAT HAS NEVER SAID NO IS INDISTINGUISHABLE FROM ONE THAT SAYS YES TO EVERYTHING, which is
# why the accepting cases are here beside the refusing ones. Two of them are the direction a careless
# comparison gets wrong: a build BELOW the claim is fine, and a build below the claim that a string
# comparison would call higher is still fine.
#
# WHAT IT COSTS AND WHAT IT REACHES. Two builds of the plugin project and one restore each, so it
# reaches the package feed, and it resolves the version above the floor from that feed rather than
# carrying a number that goes stale. It starts no server and downloads no release. Neither build
# touches the checkout: the outputs and the lock file both go to a temporary directory.
#
# usage: scripts/prove-package-abi-refusals.sh

set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/.." && pwd)
reader="$here/check-package-abi.sh"

cd "$root"

# The line build.yaml claims, read rather than written, so this harness follows a floor that moves.
claim=$(sed -n 's/^targetAbi: *"\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' build.yaml | head -1)
framework=$(sed -n 's/^framework: *"\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' build.yaml | head -1)
if [ -z "$claim" ] || [ -z "$framework" ]; then
    echo "prove-package-abi-refusals: build.yaml declares no targetAbi or no framework, so there is no floor to build against." >&2
    exit 1
fi
floor="${claim%.*}"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# The next published release of the same line, resolved rather than written down. A number in this
# file would be a second place that line's history is recorded, and it would go stale the first time
# the line publishes again.
published=$(curl -sSfL "https://api.nuget.org/v3-flatcontainer/jellyfin.controller/index.json" | jq -r '.versions[]')
above=$(printf '%s\n' "$published" | awk -v floor="$floor" '
    function part(v, i,   a) { split(v, a, "."); return a[i] + 0 }
    /-/ { next }
    {
        if (part($0, 1) != part(floor, 1) || part($0, 2) != part(floor, 2)) { next }
        if (part($0, 3) > part(floor, 3)) { print; exit }
    }
')
if [ -z "$above" ]; then
    echo "prove-package-abi-refusals: the line ${floor} has published nothing above it, so the refusing case cannot be built. This harness proves nothing in that state and says so rather than passing." >&2
    exit 1
fi

# usage: build <name> <jellyfin-version>
build() {
    local name=$1 version=$2
    echo "  building the plugin against ${version} into ${name}"
    dotnet build "$root/Jellyfin.Plugin.Requests/Jellyfin.Plugin.Requests.csproj" \
        --configuration Release \
        -p:TargetFrameworks="$framework" \
        -p:JellyfinVersion="$version" \
        -p:NuGetLockFilePath="$work/$name.lock.json" \
        -p:BaseOutputPath="$work/$name/" \
        --verbosity quiet --nologo
    local built
    built=$(find "$work/$name" -type f -name 'Jellyfin.Plugin.Requests.dll' | head -1)
    if [ -z "$built" ]; then
        echo "prove-package-abi-refusals: the build against ${version} produced no assembly, so there is nothing to judge." >&2
        exit 1
    fi
    mkdir -p "$work/package-$name"
    cp "$built" "$work/package-$name/"
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

printf '== the two packages this proof turns on\n'
printf '  build.yaml claims %s, so the floor is %s, and the next published release of that line is %s\n' \
    "$claim" "$floor" "$above"
build floor "$floor"
build above "$above"

accepts "a package built against the floor its packaging file claims" \
    "every host reference is at or below ${claim}" \
    -- "$reader" "$work/package-floor" "$claim"

refuses "the same source built one published version above that floor" \
    "and this package claims targetAbi ${claim}" \
    -- "$reader" "$work/package-above" "$claim"

accepts "a package built below a claim that sits above it" \
    "every host reference is at or below" \
    -- "$reader" "$work/package-floor" "12.0.0.0"

# The pair a string comparison gets backwards. The patch of the build above the floor is one number,
# and the claim below is that number plus eight, so the claim's patch has two digits where the
# build's has one: as strings the build sorts higher, as integers it does not. Both are derived, so
# this stays the case it is named for while the line keeps publishing.
accepts "a build below a claim that a string comparison would call higher" \
    "every host reference is at or below" \
    -- "$reader" "$work/package-above" "${floor%.*}.$(( ${above##*.} + 8 )).0"

printf '\n== the cases where the answer could not be read, which are refusals rather than passes\n'

printf 'not an archive and not a directory of assemblies\n' > "$work/not-an-archive.zip"
refuses "an asset that is not an archive" \
    "is not an archive that unpacks" \
    -- "$reader" "$work/not-an-archive.zip" "$claim"

mkdir -p "$work/empty"
printf 'a readme and nothing else\n' > "$work/empty/README.txt"
refuses "a package with no assembly in it" \
    "carries no assembly, so it has no reference table to read" \
    -- "$reader" "$work/empty" "$claim"

mkdir -p "$work/not-an-assembly"
printf 'this is not a PE image\n' > "$work/not-an-assembly/Jellyfin.Plugin.Requests.dll"
refuses "an assembly the reader cannot open" \
    "could not be read, so what this package asks a server for is unknown" \
    -- "$reader" "$work/not-an-assembly" "$claim"

# An assembly that is real and asks the server for nothing. The reader tool is one: it references the
# shared framework and no Jellyfin package at all.
dotnet build "$root/tools/package-abi/PackageAbi.csproj" --configuration Release \
    -p:BaseOutputPath="$work/reader-build/" --verbosity quiet --nologo
mkdir -p "$work/no-host-reference"
cp "$(find "$work/reader-build" -type f -name 'Jellyfin.Plugin.Requests.PackageAbi.dll' | head -1)" \
    "$work/no-host-reference/"
refuses "an assembly that asks the server for nothing at all" \
    "references a Jellyfin or MediaBrowser assembly at all" \
    -- "$reader" "$work/no-host-reference" "$claim"

refuses "a claim that is not four numeric parts" \
    "is not four numeric parts" \
    -- "$reader" "$work/package-floor" "10.11"

printf '\n== a reference the package answers itself is not a demand on the server\n'
mkdir -p "$work/carries-a-host-name"
cp "$work/package-above/Jellyfin.Plugin.Requests.dll" "$work/carries-a-host-name/"
cp "$work/no-host-reference/Jellyfin.Plugin.Requests.PackageAbi.dll" \
    "$work/carries-a-host-name/MediaBrowser.Common.dll"
set +e
carried=$("$reader" "$work/carries-a-host-name" "$claim" 2>&1)
set -e
printf '%s\n' "$carried" | sed 's/^/  /'
case "$carried" in
    *"check-package-abi: Jellyfin.Plugin.Requests.dll references MediaBrowser.Common at"*)
        echo "  it still demanded MediaBrowser.Common of the server, and the package carries it."
        failures=$((failures + 1))
        ;;
esac
case "$carried" in
    *"references MediaBrowser.Model at"*) ;;
    *)
        echo "  it stopped naming MediaBrowser.Model as well, so the exclusion is wider than the one claimed."
        failures=$((failures + 1))
        ;;
esac

printf '\n'
if [ "$failures" -ne 0 ]; then
    echo "prove-package-abi-refusals: ${failures} case(s) did not answer as claimed." >&2
    exit 1
fi
echo "prove-package-abi-refusals: every case answered as claimed."

#!/usr/bin/env bash
# Compare the newest release of each claimed server line against the manifest a server would fetch,
# and refuse a disagreement.
#
# WHY THIS EXISTS RATHER THAN TRUSTING THE PUBLISH. #111 is written against an incident on the
# sibling SSO board: the release was created, the manifest commit failed, and the successful publish
# shipped nothing installable. The run was green, so nobody looked. A check inside the publish cannot
# catch that, because the thing that failed is the thing that would have to report it. This one does
# not trust the workflow that published: it fetches the document an operator's server fetches and
# reads it against the releases that exist.
#
# WHAT IT COMPARES, AND WHY PER LINE. This board claims one server line per packaging file at the
# root, and since #110 on 2026-08-28 the tag names the line: `X.Y.Z.W-stable` is the line
# `build.yaml` carries and `X.Y.Z.W-<line>-stable` is `build-<line>.yaml`. The derivation below is
# the one `.github/workflows/publish.yaml` makes, because a second reading of a tag is a second rule.
# A server keeps the entries whose `targetAbi` it can take and picks the highest version among them,
# so a line whose newest release never reached the manifest is a line whose servers are offered an
# older build, or none - and nothing else in this repository would say so.
#
# IT TAKES ITS INPUTS AS FILES AND REACHES NO NETWORK. The fetch is the workflow's, so every answer
# this script can give is one `scripts/prove-manifest-freshness-refusals.sh` can hand it directly. A
# reader nobody has watched saying no is a reader that might say yes to everything.
#
# WHAT IT DOES NOT DO. It does not install anything and it does not hash an archive. Whether the
# bytes behind an entry are the bytes the entry promises is the manifest generator's own rule, proven
# in `scripts/prove-manifest-refusals.sh`, and whether a released asset installs into a server is
# `scripts/check-released-package.sh` and the workflow that runs it, which arrived for #152.
#
# usage: scripts/check-manifest-is-fresh.sh <manifest-json> <releases-file>
#   the releases file holds one release tag per line, in any order - the shape
#   `gh release list --json tagName --jq '.[].tagName'` produces.

set -euo pipefail

manifest=${1:?path to a file holding the manifest a server would fetch}
releases=${2:?path to a file holding one release tag per line}

for f in "$manifest" "$releases"; do
    if [ ! -f "$f" ]; then
        echo "check-manifest-is-fresh: ${f} does not exist. This reads what a fetch and a listing actually returned, never what they were expected to return." >&2
        exit 1
    fi
done

if ! jq -e 'type == "array"' "$manifest" >/dev/null 2>&1; then
    echo "check-manifest-is-fresh: ${manifest} is not a JSON array. A manifest a server cannot parse is a manifest nobody can install from, and it reaches here as a fetch that returned an error page rather than as a network failure." >&2
    exit 1
fi

# The identity is the minted identifier rather than the display name. A catalogue holds one entry per
# plugin keyed on it, the name is prose an operator may see changed, and both packaging files carry
# the same identifier for exactly this reason.
guid=$(sed -n 's/^guid: *"\([^"]*\)".*/\1/p' build.yaml | head -1)
if [ -z "$guid" ]; then
    echo "check-manifest-is-fresh: build.yaml declares no guid, so there is no identity to look the manifest entry up by." >&2
    exit 1
fi

entry=$(jq --arg guid "$guid" '[.[] | select((.guid // "") | ascii_downcase == ($guid | ascii_downcase))] | first' "$manifest" | tr -d '\r')
if [ "$entry" = "null" ] || [ -z "$entry" ]; then
    echo "check-manifest-is-fresh: the manifest carries no entry for ${guid}. A server that added this repository is offered nothing at all, which is the shape the publish that shipped nothing installable left behind." >&2
    exit 1
fi

# Every line this repository claims, derived from the packaging files rather than listed here. Adding
# a line is adding a packaging file, which is the rule publish.yaml, abi-floor.yaml and package.yaml
# already run on, and a list in this script would be the copy that disagrees first.
# The line `build.yaml` names has no suffix, and an empty string is not a subscript bash accepts, so
# it is keyed under a sentinel a filename cannot produce: `build-<line>.yaml` gives a line made of
# the characters a file name is made of, and a slash is not one of them.
base_line='/base'

declare -A abi_of_line=()
for metadata in build.yaml build-*.yaml; do
    [ -f "$metadata" ] || continue
    abi=$(sed -n 's/^targetAbi: *"\([^"]*\)".*/\1/p' "$metadata" | head -1)
    if [ -z "$abi" ]; then
        echo "check-manifest-is-fresh: ${metadata} declares no targetAbi, so the line it packages for cannot be told from any other." >&2
        exit 1
    fi
    case "$metadata" in
        build.yaml) line=$base_line ;;
        *) line=${metadata#build-} ; line=${line%.yaml} ;;
    esac
    abi_of_line["$line"]=$abi
done

# usage: newest <version> <version> -> prints the higher of the two, comparing position by position
# so 0.10.0.0 is above 0.9.0.0. `sort -V` is not used: it is a GNU extension and this has to give the
# same answer on whatever runs it.
higher() {
    local a=$1 b=$2 i
    local -a as bs
    IFS=. read -r -a as <<< "$a"
    IFS=. read -r -a bs <<< "$b"
    for i in 0 1 2 3; do
        local x=${as[i]:-0} y=${bs[i]:-0}
        if [ "$x" -gt "$y" ]; then echo "$a"; return; fi
        if [ "$x" -lt "$y" ]; then echo "$b"; return; fi
    done
    echo "$a"
}

declare -A newest_release=()
while read -r tag; do
    tag=${tag%$'\r'}
    [ -n "$tag" ] || continue
    case "$tag" in
        *-stable) rest=${tag%-stable} ;;
        *)
            # Not a release this route published. It is reported rather than refused, because a tag
            # somebody made by hand is not evidence about the manifest either way.
            echo "note: ${tag} does not end in -stable, so it names no line and is not compared."
            continue
            ;;
    esac

    case "$rest" in
        *-*) version=${rest%%-*} ; line=${rest#*-} ;;
        *)   version=$rest       ; line=$base_line ;;
    esac

    if [ -z "${abi_of_line[$line]+set}" ]; then
        metadata=build.yaml
        [ "$line" != "$base_line" ] && metadata="build-${line}.yaml"
        echo "check-manifest-is-fresh: release ${tag} names the server line whose packaging metadata would be ${metadata}, and this repository claims no such line. Either a release went out for a line that was dropped, or the packaging file was removed while its releases still stand." >&2
        exit 1
    fi

    if [ -z "${newest_release[$line]+set}" ]; then
        newest_release["$line"]=$version
    else
        newest_release["$line"]=$(higher "${newest_release[$line]}" "$version")
    fi
done < "$releases"

failures=0

for line in "${!abi_of_line[@]}"; do
    abi=${abi_of_line[$line]}
    if [ "$line" = "$base_line" ]; then named="the line build.yaml names"; else named="the ${line} line"; fi

    # The carriage return is deleted before the newlines become spaces, and not for tidiness: a jq
    # built for Windows ends its lines with one, and a version carrying an invisible carriage return
    # compares unequal to the identical version read out of a tag. Measured on this tree, where it
    # reported a manifest that agreed as one that did not - the direction that wastes somebody's
    # evening rather than the one that hides a defect, and still wrong.
    # The carriage return is deleted before the newlines become spaces, and not for tidiness: a jq
    # built for Windows ends its lines with one, and a version carrying an invisible carriage return
    # compares unequal to the identical version read out of a tag. Measured on this tree, where it
    # reported a manifest that agreed as one that did not - the direction that wastes somebody's
    # evening rather than the one that hides a defect, and still wrong.
    published=$(printf '%s' "$entry" | jq -r --arg abi "$abi" '[.versions[] | select(.targetAbi == $abi) | .version] | .[]' | tr -d '\r' | tr '\n' ' ')

    if [ -z "${newest_release[$line]+set}" ]; then
        # SILENT ABOUT THE LINE RATHER THAN GREEN ABOUT IT. A claimed line with no release cannot
        # disagree with a manifest, so there is nothing here to refuse; saying so is the difference
        # between a check that covered the line and a check that had nothing to cover.
        echo "note: ${named} (targetAbi ${abi}) has no release, so nothing about it is compared. What the manifest carries for it: ${published:-nothing}."
        if [ -n "$published" ]; then
            echo "check-manifest-is-fresh: the manifest offers ${published}at targetAbi ${abi} and no release exists for ${named}. A server would be handed a download for something that was never published." >&2
            failures=$((failures + 1))
        fi
        continue
    fi

    want=${newest_release[$line]}

    if [ -z "$published" ]; then
        echo "check-manifest-is-fresh: the newest release for ${named} is ${want} and the manifest carries no entry at targetAbi ${abi} at all. That is a release nobody can install: the publish succeeded and the document a server reads never learned of it." >&2
        failures=$((failures + 1))
        continue
    fi

    top=""
    for v in $published; do
        if [ -z "$top" ]; then top=$v; else top=$(higher "$top" "$v"); fi
    done

    if [ "$top" != "$want" ]; then
        echo "check-manifest-is-fresh: the newest release for ${named} is ${want} and the newest the manifest offers at targetAbi ${abi} is ${top}. A server on that line takes ${top} and there is no way for it to learn that ${want} exists." >&2
        failures=$((failures + 1))
        continue
    fi

    echo "${named}: newest release ${want}, manifest offers ${published}at targetAbi ${abi}, newest ${top}. They agree."
done

if [ "$failures" -ne 0 ]; then
    echo "check-manifest-is-fresh: ${failures} claimed line(s) disagree with the manifest a server would fetch." >&2
    exit 1
fi

echo "Every claimed line with a release offers that release in the manifest a server would fetch."

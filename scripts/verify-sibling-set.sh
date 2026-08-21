#!/usr/bin/env bash
# Boot a Jellyfin of one claimed line twice: once with this plugin alone, once with the supported
# sibling set installed beside it. Then scan for collisions between what the plugins declare.
#
# This is the family rule held by a machine rather than by care. A plugin that works alone and a
# plugin that works beside its siblings are two different claims, and the second one fails in ways
# nothing in a single-plugin suite can see: two plugins answering the same route, two scheduled
# tasks with the same key, two plugins writing the same configuration file.
#
# WHAT IS SCANNED, AND WHERE EACH COMES FROM ON A RUNNING SERVER:
#
#   routes                 the server's own OpenAPI document, which is what its clients read
#   scheduled task names   GET /ScheduledTasks, which is the list an operator sees
#   configuration files    /config/plugins/configurations, which is where a plugin's settings land
#
# WHAT THE SET IS. `scripts/siblings.txt` lists candidate boards. A board joins the set for a line on
# the day it publishes a release for that line's ABI, and is skipped on the lines it has published
# nothing for. The run prints both lists, because a green matrix that covered one board and does not
# say so is read as a matrix over the family.
#
# usage: scripts/verify-sibling-set.sh <image> <target-framework> <target-abi> [host-port]
#   scripts/verify-sibling-set.sh jellyfin/jellyfin:10.11.11 net9.0  10.11.0.0 18100
#   scripts/verify-sibling-set.sh jellyfin/jellyfin:12.0-rc4  net10.0 12.0.0.0 18101

set -euo pipefail

image=${1:?server image, for example jellyfin/jellyfin:10.11.11}
framework=${2:?target framework, for example net9.0}
abi=${3:?target ABI, for example 10.11.0.0}
port=${4:-18100}

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=scripts/server-under-test.sh
. "$here/server-under-test.sh"

work=$(mktemp -d)
cleanup() {
    server_stop
    rm -rf "$work"
}
trap cleanup EXIT

server_start "$image" "$framework" "$port" "jellyfin-sibling-set-$framework"

# What one server declares, printed and kept. Called once with this plugin alone and once with the
# set installed, so the two runs are comparable line by line.
facts() { # $1 a label for the files
    local into="$work/$1"
    mkdir -p "$into"

    step "routes the server serves ($1)"
    # The OpenAPI document is the server's own and covers every controller mounted on it, this
    # plugin's included. It is fetched unauthenticated because that is how a client reads it.
    curl --silent --fail-with-body --max-time 60 "$BASE/api-docs/openapi.json" > "$into/openapi.json"
    python3 -c '
import io, json, sys
paths = sorted(json.load(io.open(sys.argv[1], encoding="utf-8")).get("paths", {}))
mine = [p for p in paths if "MediaRequests" in p]
print("{0} paths in the document, {1} under this plugin".format(len(paths), len(mine)))
for p in mine:
    print("  " + p)
io.open(sys.argv[2], "w", encoding="utf-8").write("\n".join(paths))
' "$into/openapi.json" "$into/paths.txt"

    step "scheduled tasks ($1)"
    api GET /ScheduledTasks > "$into/tasks.json"
    python3 -c '
import io, json, sys
tasks = json.load(io.open(sys.argv[1], encoding="utf-8"))
rows = ["{0}\t{1}".format(t.get("Key"), t.get("Name")) for t in tasks]
for row in sorted(rows):
    print("  " + row)
io.open(sys.argv[2], "w", encoding="utf-8").write("\n".join(rows))
' "$into/tasks.json" "$into/tasks.txt"

    step "configuration files on disk ($1)"
    # An empty directory is an ordinary state: a plugin writes its file when its configuration is
    # first saved, so this says what has been written rather than what could be.
    dk exec "$CONTAINER" sh -c 'ls -1 /config/plugins/configurations 2>/dev/null || true' > "$into/configs.txt"
    sed 's/^/  /' "$into/configs.txt"

    step "plugins the server lists ($1)"
    api GET /Plugins > "$into/plugins.json"
    python3 -c '
import io, json, sys
plugins = json.load(io.open(sys.argv[1], encoding="utf-8"))
rows = ["{0}\t{1}\t{2}\t{3}".format(p.get("Name"), p.get("Version"), p.get("Status"), p.get("Id")) for p in plugins]
for row in sorted(rows):
    print("  " + row)
io.open(sys.argv[2], "w", encoding="utf-8").write("\n".join(rows))
' "$into/plugins.json" "$into/plugins.txt"
}

step "run one: this plugin alone"
facts alone

step "the supported set for ABI $abi"
installed="$work/installed.txt"
skipped="$work/skipped.txt"
: > "$installed"
: > "$skipped"

while read -r board directory; do
    case "${board:-}" in ''|'#'*) continue ;; esac

    echo "-- $board"
    found=""
    # Newest first, and the first release carrying a package for this ABI wins. Reading the ABI out
    # of each release's own metadata rather than off the tag name, because a tag is a string
    # somebody typed and the metadata is what the server compares against.
    for tag in $(gh release list --repo "$board" --limit 12 --json tagName --jq '.[].tagName'); do
        if ! gh release download "$tag" --repo "$board" --pattern 'build.yaml' --output "$work/candidate.yaml" --clobber >/dev/null 2>&1; then
            continue
        fi
        if [ "$(awk -F': *' '/^targetAbi:/ {gsub(/"/, "", $2); print $2; exit}' "$work/candidate.yaml")" != "$abi" ]; then
            continue
        fi
        found="$tag"
        break
    done

    if [ -z "$found" ]; then
        echo "   skipped: no release carrying a package for $abi in the last twelve"
        printf '%s\n' "$board" >> "$skipped"
        continue
    fi

    echo "   $found"
    rm -rf "$work/package"
    mkdir -p "$work/package"
    gh release download "$found" --repo "$board" --pattern '*.zip' --dir "$work/package" >/dev/null
    zip=$(find "$work/package" -name '*.zip' | head -1)
    test -n "$zip"
    rm -rf "$work/unpacked"
    mkdir -p "$work/unpacked"
    unzip -q "$zip" -d "$work/unpacked"

    dk exec "$CONTAINER" mkdir -p "/config/plugins/$directory"
    # The whole package rather than one assembly: a sibling decides what it ships and this is a
    # check of what an operator would actually install.
    for file in "$work/unpacked"/*; do
        dk cp "$file" "$CONTAINER:/config/plugins/$directory/"
    done
    printf '%s\t%s\t%s\n' "$board" "$found" "$directory" >> "$installed"
done < "$here/siblings.txt"

step "what the set is, and what it is not"
echo "installed:"
sed 's/^/  /' "$installed"
echo "skipped for having no package on this line:"
sed 's/^/  /' "$skipped"
test -s "$installed"

step "start again with the set in place"
dk restart "$CONTAINER" >/dev/null
settled=0
for _ in $(seq 1 120); do
    if curl --silent --fail --max-time 5 "$BASE/System/Info/Public" >/dev/null 2>&1; then
        settled=$((settled + 1))
        if [ "$settled" -ge 3 ]; then
            break
        fi
        sleep 1
        continue
    fi
    settled=0
    sleep 2
done
test "$settled" -ge 3

step "run two: this plugin and the set"
facts together

step "verdict"
# The scan is a file of its own and `scripts/prove-collision-scan.sh` runs it over one fixture per
# collision kind. That is the difference between a guard and a claim: this call passes on a clean
# server, which is also what a scan that does nothing does, and the fixtures are where each rule is
# watched refusing something.
python3 "$here/sibling-collision-scan.py" "$work/alone" "$work/together" "$installed"

step "done"
echo "this plugin alone and beside the set, on $image ($framework)"

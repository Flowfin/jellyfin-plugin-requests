#!/usr/bin/env bash
# Add the manifest an operator adds to a Jellyfin server of one claimed line, let the server choose
# which entry to install, and refuse the choice if it is not this line's build.
#
# WHY THIS EXISTS AND WHAT IT IS THE ONLY READING OF. Three routes come close to #110's conditions
# and none of them is a server installing. `scripts/build-manifest.sh` refuses a manifest it cannot
# write correctly, which is the generator judging its own output. `scripts/check-manifest-is-fresh.sh`
# reads the published document against the releases that exist and says of itself that it installs
# nothing. `scripts/check-released-package.sh` puts the published bytes in a plugin directory, which
# is the unpacked archive rather than the server's own download-and-unpack path, and its own header
# says so. What none of them touches is the selection: a manifest carries one entry per server line
# and the server decides which of them it takes.
#
# THE SELECTION IS THE SUBJECT AND IT IS THE THING THAT WENT WRONG TWICE. On 2026-08-17 both
# packaging files carried one version at two `targetAbi` values, and a server keeps every entry whose
# `targetAbi` is at or below its own and then orders by version number, so two entries at one version
# were indistinguishable to a server of the newer line. On 2026-08-28 the newer line had no entry at
# all, so a server of that line was offered the older line's build for want of anything else. Both
# were read off the server's source rather than watched. This watches it.
#
# THE CHOICE IS THE SERVER'S AND THIS ASKS FOR NOTHING. `POST /Packages/Installed/{name}` takes an
# optional version and an optional repository, and passing either would make the answer this script's
# rather than the server's. Both are left out, so what installs is what `GetCompatibleVersions` puts
# first, which is the code the readings above quoted.
#
# WHAT IT READS BACK IS THE MANIFEST THE SERVER WROTE, NOT THE ONE IT FETCHED. The install writes
# `meta.json` beside the extracted files with the entry's own `version` and `targetAbi`, so the
# question "which entry did it take" is answered out of the server's own directory rather than
# inferred from a version number the two lines share.
#
# THE CHECKSUM IS THE SERVER'S TO REFUSE AND THIS DOES NOT RE-DO IT. A Jellyfin server hashes the
# archive it downloaded and refuses an entry whose `checksum` does not match, so an install that
# completed is an entry whose checksum verified against its own bytes, on the server's terms rather
# than on this repository's.
#
# WHAT IT DOES NOT DO. It does not decide whether the entry it took is the newest one for the line -
# that is `scripts/check-manifest-is-fresh.sh`, which reads the published document against the
# release listing. It reaches the network on purpose, so it cannot be driven from fixtures the way
# the readers beside it are; what stands in place of that is a run against a manifest carrying only
# the other line's entry, which is the shape both incidents above had.
#
# This needs no display, no administrator rights and no trusted certificate. The server runs in a
# container, is reached over plain HTTP on the loopback interface, and is removed when the run ends.
#
# usage: scripts/verify-manifest-install.sh <image> <target-framework> <target-abi> <manifest-url> [host-port]
#   scripts/verify-manifest-install.sh jellyfin/jellyfin:10.11.11 net9.0  10.11.0.0 https://flowfin.dev/manifest.json 18096
#   scripts/verify-manifest-install.sh jellyfin/jellyfin:12.0-rc4  net10.0 12.0.0.0  https://flowfin.dev/manifest.json 18097

set -euo pipefail

image=${1:?server image, for example jellyfin/jellyfin:10.11.11}
framework=${2:?target framework, for example net9.0}
target_abi=${3:?the targetAbi this line claims, for example 10.11.0.0}
manifest_url=${4:?the manifest address an operator adds}
port=${5:-18096}

# shellcheck source=scripts/server-under-test.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/server-under-test.sh"

# The server installs it. Nothing is published, nothing is copied in, and the plugin directory is
# empty when the wizard finishes, which is what makes the install below the server's own.
export PLUGIN_INSTALLED_BY_THE_SERVER=1

offered=$(mktemp)
installed_manifest=$(mktemp)
trap 'server_stop; rm -f "$offered" "$installed_manifest"' EXIT

server_start "$image" "$framework" "$port" "jellyfin-manifest-install-$framework"

step "point the server at $manifest_url"
# The whole repository list, because this endpoint replaces it rather than appending to it. A server
# an operator set up would carry the official list beside this one; what is under test is this
# manifest, and a second repository would let an entry from somewhere else answer for it.
api POST /Repositories "[{\"Name\":\"Flowfin\",\"Url\":\"$manifest_url\",\"Enabled\":true}]" >/dev/null
api GET /Repositories
printf '\n'

step "what the server lists for $PLUGIN_NAME"
# Unfiltered: this endpoint hands back what the manifest carries rather than what this server can
# take. It is printed so that a refusal below can be read against what the server had to choose
# from, and nothing here decides anything.
api GET /Packages >"$offered"
python3 -c '
import json, sys
name = sys.argv[2]
with open(sys.argv[1], encoding="utf-8") as handle:
    packages = json.load(handle)
mine = [p for p in packages if p.get("name") == name]
if not mine:
    sys.exit("the server lists no package called {0}. It was handed a manifest and found nothing in it under that name, so there is no entry for any line to choose between.".format(name))
if len(mine) != 1:
    sys.exit("the server lists {0} packages called {1}. Which one an install would take is not a thing this check can name.".format(len(mine), name))
for version in mine[0].get("versions", []):
    print("version={0}  targetAbi={1}  {2}".format(
        version.get("version"), version.get("targetAbi"), version.get("sourceUrl")))
' "$offered" "$PLUGIN_NAME"

step "ask the server to install $PLUGIN_NAME, and let it choose"
# No version and no repository. What this measures is the entry the server picks out of the manifest
# for a server of this line, and naming either would answer the question instead of asking it.
install_status=$(curl --silent --output /dev/null --write-out '%{http_code}' --max-time 300 \
    -X POST "$BASE/Packages/Installed/$PLUGIN_NAME" \
    -H "Authorization: $AUTH_HEADER, Token=\"$TOKEN\"")
echo "POST /Packages/Installed/$PLUGIN_NAME -> $install_status"
if [ "$install_status" = "404" ]; then
    echo "verify-manifest-install: the server answered 404, which is this server finding no entry it can take. A manifest that offers this line nothing is a line whose servers cannot install at all." >&2
    exit 1
fi
test "$install_status" = "204"

step "what the server unpacked"
# The directory the server chose the name of, rather than one this script constructed: the install
# appends the version it took, so guessing the name here would be guessing the answer.
installed_dir=""
for _ in $(seq 1 120); do
    installed_dir=$(dk exec "$CONTAINER" sh -c "ls -1d /config/plugins/${PLUGIN_NAME}_* 2>/dev/null" | tr -d '\r' || true)
    if [ -n "$installed_dir" ]; then
        break
    fi
    sleep 2
done
if [ -z "$installed_dir" ]; then
    echo "verify-manifest-install: the install answered 204 and /config/plugins holds no ${PLUGIN_NAME}_* directory. A status code is not an install." >&2
    dk exec "$CONTAINER" ls -1A /config/plugins >&2 || true
    exit 1
fi
if [ "$(printf '%s\n' "$installed_dir" | grep -c '^')" != "1" ]; then
    echo "verify-manifest-install: the server holds more than one ${PLUGIN_NAME}_* directory, so which entry it took is a guess:" >&2
    printf '%s\n' "$installed_dir" >&2
    exit 1
fi
echo "$installed_dir"
dk exec "$CONTAINER" ls -1 "$installed_dir"

step "which entry the server took"
# `meta.json` is the manifest entry the server wrote out beside the files it unpacked, so this is the
# server's own record of what it chose rather than a version number read off a filename. The two
# lines carry one version between them, so the filename could not tell them apart.
dk exec "$CONTAINER" cat "$installed_dir/meta.json" >"$installed_manifest"
cat "$installed_manifest"
printf '\n'
installed_abi=$(python3 -c '
import json, sys
with open(sys.argv[1], encoding="utf-8") as handle:
    manifest = json.load(handle)
print(manifest.get("targetAbi", ""))
' "$installed_manifest")
installed_version=$(python3 -c '
import json, sys
with open(sys.argv[1], encoding="utf-8") as handle:
    manifest = json.load(handle)
print(manifest.get("version", ""))
' "$installed_manifest")

step "verdict on the selection"
if [ -z "$installed_abi" ]; then
    echo "verify-manifest-install: the manifest the server wrote names no targetAbi, so which line's build it took cannot be read." >&2
    exit 1
fi
if [ "$installed_abi" != "$target_abi" ]; then
    echo "verify-manifest-install: this server is of the line claiming targetAbi $target_abi and it installed the entry claiming $installed_abi, at version ${installed_version:-unknown}. A server offered another line's build is exactly what #110's third condition is written against." >&2
    exit 1
fi
echo "the server of the $target_abi line took the $installed_abi entry at $installed_version"

step "does what it installed load"
# The selection being right is half of it. A server that installed the correct entry and cannot load
# it has still left an operator without a plugin, and #110's first condition is that installing from
# the manifest works rather than that it resolves.
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
api GET /Plugins | python3 -c '
import json, sys
name, version = sys.argv[1], sys.argv[2]
mine = [p for p in json.load(sys.stdin) if p.get("Name") == name]
if not mine:
    sys.exit("{0} is not in the plugin list after the install: the server unpacked it and did not load it.".format(name))
if len(mine) != 1:
    sys.exit("{0} appears {1} times in the plugin list.".format(name, len(mine)))
status, reported = mine[0].get("Status"), mine[0].get("Version")
if status != "Active":
    sys.exit("{0} is {1} rather than Active: the entry the server chose does not run on this server.".format(name, status))
if reported != version:
    sys.exit("{0} reports {1!r} and the entry the server took is {2!r}.".format(name, reported, version))
print("{0} is Active at {1}".format(name, reported))
' "$PLUGIN_NAME" "$installed_version"

step "done"
echo "$image ($framework, targetAbi $target_abi) installed $PLUGIN_NAME $installed_version from $manifest_url and loaded it"

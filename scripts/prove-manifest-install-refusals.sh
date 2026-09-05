#!/usr/bin/env bash
# Watch `scripts/verify-manifest-install.sh` refuse, for each reason it names.
#
# A check nobody has watched saying no is a check that might say yes to everything, and this one is
# green for as long as the catalogue is correct - which is every day until the day it matters. The
# two cases below are not invented: each is a state this board has actually been in, and both were
# read off the server's source rather than watched happening.
#
# THE MANIFEST IS DOCTORED AND THE ARCHIVES ARE REAL. What each case changes is the document a server
# fetches; the entries it keeps still point at the releases that exist, so the server downloads,
# hashes and unpacks exactly what an operator's server would. Rewriting the archives too would prove
# something about a fixture rather than about the selection.
#
# IT IS SERVED FROM A CONTAINER RATHER THAN FROM A PORT ON THIS MACHINE. The server under test has to
# fetch the address, so the address has to be one a container can reach; a port opened on the host
# would be a firewall question on some of the machines this runs on, and there is nothing here that
# needs one.
#
# WHAT IT DOES NOT PROVE. The verdict it watches is the one about the selection and the one about the
# listing. Whether the plugin the server installed then loads is the last step of the check itself,
# and no fixture here makes a correct entry fail to load - that would be a doctored archive, which is
# the case above that proves something about a fixture.
#
# usage: scripts/prove-manifest-install-refusals.sh <manifest-json> <image> <target-framework> <target-abi> [host-port]
#   scripts/prove-manifest-install-refusals.sh manifest.json jellyfin/jellyfin:12.0-rc4 net10.0 12.0.0.0

set -euo pipefail

manifest=${1:?path to a file holding the manifest a server would fetch}
image=${2:?server image, for example jellyfin/jellyfin:12.0-rc4}
framework=${3:?target framework, for example net10.0}
target_abi=${4:?the targetAbi the line under test claims, for example 12.0.0.0}
port=${5:-18099}

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
PLUGIN_NAME=Requests
FIXTURE_CONTAINER=manifest-install-fixture

if [ ! -f "$manifest" ]; then
    echo "prove-manifest-install-refusals: $manifest does not exist. This doctors the document a fetch actually returned, never one written here." >&2
    exit 1
fi

dk() {
    MSYS_NO_PATHCONV=1 docker "$@"
}

work=$(mktemp -d)
cleanup() {
    dk rm --force "$FIXTURE_CONTAINER" >/dev/null 2>&1 || true
    rm -rf "$work"
}
trap cleanup EXIT

# The manifest under test has to carry the entry each case removes, or the case removes nothing and
# passes for the wrong reason.
offered=$(jq --arg name "$PLUGIN_NAME" --arg abi "$target_abi" \
    '[.[] | select(.name == $name) | .versions[] | select(.targetAbi == $abi)] | length' "$manifest")
if [ "$offered" -lt 1 ]; then
    echo "prove-manifest-install-refusals: $manifest carries no $PLUGIN_NAME entry at targetAbi $target_abi, so removing one proves nothing. Either the fetch returned something else or this line has no entry, which is the defect rather than the fixture." >&2
    exit 1
fi

serve() {
    local document=$1
    dk rm --force "$FIXTURE_CONTAINER" >/dev/null 2>&1 || true
    dk run --detach --name "$FIXTURE_CONTAINER" nginx:alpine >/dev/null
    # The host spelling of the path, because Git Bash and docker disagree about what an absolute
    # path looks like on one of the platforms this runs on, and there the copy fails while the
    # fixture goes on serving the image's own index page.
    local host_document
    host_document=$(cygpath --windows "$document" 2>/dev/null || printf '%s' "$document")
    dk cp "$host_document" "$FIXTURE_CONTAINER:/usr/share/nginx/html/manifest.json"
    # READ BACK WHAT IS BEING SERVED. A fixture that did not arrive serves a page the server under
    # test finds no package in, and every case below would then be refused for that reason instead
    # of its own. A proof that passes when its fixture is missing proves nothing.
    if ! dk exec "$FIXTURE_CONTAINER" cat /usr/share/nginx/html/manifest.json | jq -e 'type == "array"' >/dev/null; then
        echo "prove-manifest-install-refusals: the fixture container is not serving the doctored manifest, so nothing below would be refused for its own reason." >&2
        exit 1
    fi
    local address
    address=$(dk inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "$FIXTURE_CONTAINER" | tr -d '\r')
    if [ -z "$address" ]; then
        echo "prove-manifest-install-refusals: the fixture container has no address, so nothing can fetch from it." >&2
        exit 1
    fi
    printf 'http://%s/manifest.json' "$address"
}

# Runs the check against a doctored manifest and refuses if the check does not refuse, or refuses
# without saying the thing this case is about. A check that says no for the wrong reason is a check
# that will say no to the right input for the wrong reason too.
expect_refusal() {
    local case_name=$1 document=$2 wanted=$3
    local url output status

    printf '\n======== %s\n' "$case_name"
    url=$(serve "$document")
    echo "serving the doctored manifest at $url"

    status=0
    output=$("$here/verify-manifest-install.sh" "$image" "$framework" "$target_abi" "$url" "$port" 2>&1) || status=$?
    printf '%s\n' "$output" | tail -20

    if [ "$status" -eq 0 ]; then
        echo "prove-manifest-install-refusals: $case_name was accepted. The check passed a manifest it exists to refuse." >&2
        exit 1
    fi
    if ! printf '%s' "$output" | grep -qF "$wanted"; then
        echo "prove-manifest-install-refusals: $case_name was refused with exit $status and not for its own reason. Expected to read: $wanted" >&2
        exit 1
    fi
    echo "refused, exit $status, for its own reason"
}

# CASE ONE: this line has no entry and the other line's does. That is the state this board published
# in on 2026-08-28 - the 12.0 line had no release and the manifest carried the 10.11 line twice - and
# a server keeps every entry whose targetAbi is at or below its own, so the newer line's server takes
# the older line's build rather than nothing.
jq --arg name "$PLUGIN_NAME" --arg abi "$target_abi" \
    '[.[] | if .name == $name then .versions |= map(select(.targetAbi != $abi)) else . end]' \
    "$manifest" >"$work/no-entry-for-this-line.json"
expect_refusal \
    "the manifest offers this line nothing and the other line something" \
    "$work/no-entry-for-this-line.json" \
    "installed the entry claiming"

# CASE TWO: the package is not in the manifest at all. That is a publish that reported success and
# left the document untouched, which is the incident #111 is written against, and it reaches a server
# as a plugin that is simply not there to install.
jq --arg name "$PLUGIN_NAME" '[.[] | select(.name != $name)]' "$manifest" >"$work/no-package.json"
expect_refusal \
    "the manifest carries no entry for this plugin at all" \
    "$work/no-package.json" \
    "the server lists no package called"

printf '\n======== done\n'
echo "verify-manifest-install.sh refused both, each for its own reason, on $image ($framework, targetAbi $target_abi)"

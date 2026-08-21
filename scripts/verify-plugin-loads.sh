#!/usr/bin/env bash
# Start a Jellyfin server of one claimed line, install this plugin into it, and print what the
# server itself says about the plugin.
#
# A plugin that builds is not a plugin that loads. An ABI mismatch, an embedded resource path that
# stopped resolving after a rename, and a dependency the host does not provide all build clean and
# fail at server start or on first use. Nothing short of a server answering about the plugin is
# evidence that it loaded, and a dashboard somebody looked at is not evidence anybody else can
# repeat.
#
# The server, the install, the wizard and the account are `scripts/server-under-test.sh`, which this
# and the activity check beside it both source. What is left here is what this check is about: the
# configuration page the dashboard fetches, and the configuration it reads and writes.
#
# This needs no display, no administrator rights and no trusted certificate. The server runs in a
# container, is reached over plain HTTP on the loopback interface, and is removed when the run
# ends. The one credential it creates lives for the length of that container.
#
# usage: scripts/verify-plugin-loads.sh <image> <target-framework> [host-port]
#   scripts/verify-plugin-loads.sh jellyfin/jellyfin:10.11.11 net9.0  18096
#   scripts/verify-plugin-loads.sh jellyfin/jellyfin:12.0-rc4  net10.0 18097

set -euo pipefail

image=${1:?server image, for example jellyfin/jellyfin:10.11.11}
framework=${2:?target framework, for example net9.0}
port=${3:-18096}

# shellcheck source=scripts/server-under-test.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/server-under-test.sh"

trap server_stop EXIT

server_start "$image" "$framework" "$port" "jellyfin-plugin-load-check-$framework"

step "the configuration page the dashboard would fetch"
# The embedded resource path is built at run time from the plugin's namespace. A half rename leaves
# a build that is green and a page that is empty, and this is where that shows.
page_status=$(curl --silent --output /dev/null --write-out '%{http_code}' --max-time 30 \
    "$BASE/web/ConfigurationPage?name=$PLUGIN_NAME")
echo "GET /web/ConfigurationPage?name=$PLUGIN_NAME -> $page_status"
test "$page_status" = "200"
curl --silent --fail --max-time 30 "$BASE/web/ConfigurationPage?name=$PLUGIN_NAME" | head -5

step "the configuration the page reads and writes"
api GET "/Plugins/$PLUGIN_ID/Configuration"
printf '\n'
write_status=$(curl --silent --output /dev/null --write-out '%{http_code}' --max-time 30 \
    -X POST "$BASE/Plugins/$PLUGIN_ID/Configuration" \
    -H 'Content-Type: application/json' \
    -H "Authorization: $AUTH_HEADER, Token=\"$TOKEN\"" \
    --data '{}')
echo "POST /Plugins/<id>/Configuration -> $write_status"
test "$write_status" = "204"

step "done"
echo "$PLUGIN_NAME loaded and answered on $image ($framework)"

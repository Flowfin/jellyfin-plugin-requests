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
# configuration page the dashboard fetches, and a setting written through the endpoint the page
# writes to and read back out of the server afterwards.
#
# THE READ BACK IS THE HALF THAT IS EASY TO LEAVE OUT. A server that stored nothing answers a write
# 204 all the same, so a check that stops at the status code says the endpoint exists and nothing
# about whether a setting survives being saved.
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

# The answer the server gives after the save is read twice, once by a person reading this output and
# once by the check under it, so it is kept in a file rather than in a variable a pipeline would eat.
stored_configuration=$(mktemp)
# The configuration page's own bytes, for the same reason and read once.
page_body=$(mktemp)
trap 'server_stop; rm -f "$stored_configuration" "$page_body"' EXIT

server_start "$image" "$framework" "$port" "jellyfin-plugin-load-check-$framework"

step "the configuration page the dashboard would fetch"
# The embedded resource path is built at run time from the plugin's namespace. A half rename leaves
# a build that is green and a page that is empty, and this is where that shows.
page_status=$(curl --silent --output /dev/null --write-out '%{http_code}' --max-time 30 \
    "$BASE/web/ConfigurationPage?name=${PLUGIN_NAME}s")
echo "GET /web/ConfigurationPage?name=$PLUGIN_NAME -> $page_status"
test "$page_status" = "200"
# THE HEAD IS PRINTED OUT OF A FILE AND NEVER OUT OF A PIPE, and it is the same reason the stored
# configuration above is kept in one. `head` closes the pipe after five lines, and where the body is
# still being written at that moment curl cannot finish writing it, exits 23, and `pipefail` ends a
# check whose subject answered 200 one line earlier. Which of the two happens is a race between two
# processes and says nothing about the plugin, the server or the page. It was watched happening,
# once, on a head carrying two markdown files and nothing this script reads; the same head passed on
# a second attempt with nothing changed between them (#263). Out of a file there is no pipe to close,
# and curl failing to fetch at all still ends the check, which is what --fail is here for.
curl --silent --fail --max-time 30 --output "$page_body" \
    "$BASE/web/ConfigurationPage?name=$PLUGIN_NAME"
head -5 "$page_body"

step "the configuration the page reads and writes"
api GET "/Plugins/$PLUGIN_ID/Configuration"
printf '\n'

# A SETTING WITH A VALUE IN IT, RATHER THAN AN EMPTY DOCUMENT. Writing `{}` is answered 204 by a
# server that stored nothing, so it says the endpoint is there and not that a setting survives being
# saved. Neither number below is that field's default, so a server that ignored the write and
# answered with what it already held is refused instead of agreeing with itself. Both are inside what
# `ConfigurationRules` accepts, because a refused write would be this plugin working correctly rather
# than the round trip being shown.
saved_quota=7
saved_retention=90
write_status=$(curl --silent --output /dev/null --write-out '%{http_code}' --max-time 30 \
    -X POST "$BASE/Plugins/$PLUGIN_ID/Configuration" \
    -H 'Content-Type: application/json' \
    -H "Authorization: $AUTH_HEADER, Token=\"$TOKEN\"" \
    --data "{\"OpenRequestsPerUser\":$saved_quota,\"AcceptsMovies\":true,\"AcceptsSeries\":true,\"FinishedRequestRetentionDays\":$saved_retention,\"OutboundNoticeAddress\":\"\",\"AnnouncesApprovals\":true,\"AnnouncesDeclines\":true,\"AnnouncesFulfilments\":true}")
echo "POST /Plugins/<id>/Configuration -> $write_status"
test "$write_status" = "204"

step "what the server hands back after the save"
# Read out of the server rather than out of the request that was just sent. What this condition is
# about is that the value left the page and came back, and a check comparing the body it posted
# against itself would pass on a server that dropped it.
api GET "/Plugins/$PLUGIN_ID/Configuration" | tee "$stored_configuration"
printf '\n'
python3 -c '
import json, sys
wanted = {"OpenRequestsPerUser": int(sys.argv[2]), "FinishedRequestRetentionDays": int(sys.argv[3])}
with open(sys.argv[1], encoding="utf-8") as handle:
    stored = json.load(handle)
for field, value in wanted.items():
    if stored.get(field) != value:
        sys.exit("{0} came back as {1!r} rather than {2}: the save did not survive.".format(
            field, stored.get(field), value))
print("OpenRequestsPerUser={0} and FinishedRequestRetentionDays={1} read back after the save".format(
    wanted["OpenRequestsPerUser"], wanted["FinishedRequestRetentionDays"]))
' "$stored_configuration" "$saved_quota" "$saved_retention"

step "done"
echo "$PLUGIN_NAME loaded and answered on $image ($framework)"

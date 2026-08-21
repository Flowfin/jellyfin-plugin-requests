#!/usr/bin/env bash
# Walk a request through its life on a real Jellyfin server of one claimed line, then read the
# activity entries back out of that server.
#
# The suite already asserts that every transition appends one entry, against a double over the
# server's activity manager. What a double cannot say is that the entry is visible where an operator
# looks. The dashboard's activity page draws `GET /System/ActivityLog/Entries`, so reading the
# entries back through that endpoint is reading what the dashboard renders, one layer under the
# markup.
#
# What this does not do is render anything. Nothing here opens a browser, which is the headless rule
# in docs/testing.md, so what is checked is the entries the dashboard's own source reaches and not
# the page it draws from them.
#
# The server, the install, the wizard and the account are `scripts/server-under-test.sh`, which the
# plugin-load check beside this one also sources.
#
# usage: scripts/verify-activity-entries.sh <image> <target-framework> [host-port]
#   scripts/verify-activity-entries.sh jellyfin/jellyfin:10.11.11 net9.0  18098
#   scripts/verify-activity-entries.sh jellyfin/jellyfin:12.0-rc4  net10.0 18099

set -euo pipefail

image=${1:?server image, for example jellyfin/jellyfin:10.11.11}
framework=${2:?target framework, for example net9.0}
port=${3:-18098}

# shellcheck source=scripts/server-under-test.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/server-under-test.sh"

trap server_stop EXIT

server_start "$image" "$framework" "$port" "jellyfin-activity-check-$framework"

# The title is distinctive so that an entry naming it cannot be some other server activity that
# happened to be logged in the same second.
title="A film for the activity check"
prefix=/MediaRequests/v1

# One field out of a JSON object on stdin, matched without regard to case, so a reader here does not
# fix a spelling this check is not about.
FIELD='
import json, sys

name = sys.argv[1]
holder = json.load(sys.stdin)
for key, value in holder.items():
    if key.lower() == name.lower():
        print(value)
        break
else:
    sys.exit("no {0} in {1}".format(name, sorted(holder)))
'

step "ask for something"
created=$(api POST "$prefix/Requests" "$(printf '%s' '{
  "kind": "Movie",
  "title": "'"$title"'",
  "year": 1999,
  "providerIds": { "Tmdb": "activity-check-603" },
  "note": "The requester typed this and no entry may carry it."
}')")
printf '%s\n' "$created"
# The keys are matched without regard to case. What this check is about is the entries a server
# kept, and which casing the server's serialiser hands a plugin's own shapes back in is a property
# the suite asserts against the bytes; a reader here that fixed one spelling would fail for a reason
# this script is not about.
request_id=$(printf '%s' "$created" | python3 -c "$FIELD" id)
echo "request $request_id"

step "read the revision an operator would decide against"
queue=$(api GET "$prefix/Requests/Queue?take=200")
revision=$(printf '%s' "$queue" | python3 -c '
import json, sys


def field(holder, name):
    for key, value in holder.items():
        if key.lower() == name.lower():
            return value
    sys.exit("no {0} in {1}".format(name, sorted(holder)))


wanted = sys.argv[1]
rows = [r for r in field(json.load(sys.stdin), "requests") if field(r, "id") == wanted]
if len(rows) != 1:
    sys.exit("the queue holds {0} rows for {1}".format(len(rows), wanted))
print(field(rows[0], "revision"))
' "$request_id")
echo "revision $revision"

step "approve it"
approved=$(api POST "$prefix/Requests/$request_id/Approve" "{\"revision\": $revision}")
printf '%s\n' "$approved"
revision=$(printf '%s' "$approved" | python3 -c "$FIELD" revision)

step "decline it"
declined=$(api POST "$prefix/Requests/$request_id/Decline" \
    "{\"revision\": $revision, \"reason\": \"NotWanted\", \"note\": \"The operator typed this.\"}")
printf '%s\n' "$declined"

step "read the activity entries the dashboard draws"
# The dashboard's activity page calls this endpoint. A limit rather than every entry, because a
# server that has just started its libraries writes entries of its own and the ones this check is
# about are the most recent.
entries=$(api GET "/System/ActivityLog/Entries?limit=50")
printf '%s' "$entries" | python3 -c '
import json, sys
for entry in json.load(sys.stdin)["Items"]:
    print("Type={0}  Name={1}  ShortOverview={2}  UserId={3}".format(
        entry.get("Type"), entry.get("Name"), entry.get("ShortOverview"), entry.get("UserId")))
'

step "verdict"
printf '%s' "$entries" | python3 -c '
import json, sys

request_id, title, admin_id = sys.argv[1], sys.argv[2], sys.argv[3]
items = json.load(sys.stdin)["Items"]


def plain(value):
    """An identifier with the dashes and the case taken off it.

    The server hands a plugin its own shapes back with the dashes stripped and writes them into an
    entry with the dashes in, so the two spellings of one identifier have to be compared as one
    thing. The first run of this check compared them as strings and found no entries for a request
    that had two.
    """
    return (value or "").replace("-", "").lower()


mine = [e for e in items if plain(request_id) in plain(e.get("ShortOverview"))]

wanted = {
    "MediaRequestApproved": "Request approved: " + title,
    "MediaRequestDeclined": "Request declined: " + title,
}

for kind, name in sorted(wanted.items()):
    found = [e for e in mine if e.get("Type") == kind]
    if len(found) != 1:
        sys.exit("{0} entries of type {1} for this request, expected exactly one.".format(len(found), kind))
    entry = found[0]
    if entry.get("Name") != name:
        sys.exit("the {0} entry is named {1!r} rather than {2!r}.".format(kind, entry.get("Name"), name))
    if plain(entry.get("UserId")) != plain(admin_id):
        sys.exit("the {0} entry names {1} rather than the operator who decided it.".format(kind, entry.get("UserId")))
    print("{0}: {1} / {2}".format(kind, entry.get("Name"), entry.get("ShortOverview")))

if len(mine) != len(wanted):
    sys.exit("{0} entries name this request, expected {1}: one per transition and no more.".format(len(mine), len(wanted)))
' "$request_id" "$title" "$ADMIN_ID"

step "done"
echo "two transitions, two entries, read back from $image ($framework)"

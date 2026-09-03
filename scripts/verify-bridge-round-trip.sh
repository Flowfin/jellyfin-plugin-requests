#!/usr/bin/env bash
# A request made here arrives on a running service that speaks the Overseerr form, and what that
# service says about it comes back through the mapping table.
#
# #315 asks for an adapter against the Overseerr form and, as its last clause, for a round trip
# against a real instance rather than a stub alone. The suite drives the adapter through an
# in-process double whose answers are the shapes docs/bridge.md quotes out of the form's description,
# which is the headless rule in docs/testing.md and is exactly as strong as that description. This is
# the other reading: a Jellyfin of one claimed line with this plugin installed, a service of the form
# in a second container beside it, and one request walked from a person here to a row over there and
# back.
#
# THE SERVICE IS JELLYSEERR, PINNED BY TAG AND DIGEST BELOW, AND NOT OVERSEERR PROPER. Both speak
# the form; the difference is the door. Overseerr's only described route that creates the first user
# takes a Plex account token, which is a credential somebody would have to hold as a secret on this
# board. Jellyseerr's `/auth/jellyfin` creates it from a Jellyfin username and password instead, so
# the account the service signs in with is one this script makes on the throwaway server and forgets
# with it. Nothing here holds a credential of anybody's. Whether Overseerr proper behaves the same is
# not measured by this and is not claimed.
#
# WHAT IS ASSERTED, in the order it is walked:
#
#   - the service is up, and the plugin's health endpoint reports the bridge reachable once pointed
#     at it;
#   - an approval here answers with the service's own number for the request, which is what the
#     adapter keeps to ask about later;
#   - the service holds a request under that number, of the kind and the TMDB identifier that were
#     asked for here, and its status is the number the mapping table's `APPROVED` row is written
#     for. The request arrives under the service's own account, which holds `ADMIN` there, and the
#     form's description says such a request is approved on arrival;
#   - the reconciliation task, run on the server, asks the service and leaves the request approved,
#     which is what the table says `APPROVED` does.
#
# WHAT IS NOT. Nothing downstream of the service: it has no download client configured, so its own
# log says the request is skipped rather than fetched, and no library here ever fills. The failure
# paths - a refused key, a service that goes away, a word the table has not seen - are the suite's
# and #86's, not this script's.
#
# ONE SERVER SETTING IS TURNED ON FOR THE SERVICE, AND THE TRANSCRIPT SAYS WHETHER IT WAS OFF. The
# pinned service names its client in the `X-Emby-Authorization` header, which a 12.0 server reads
# only while `EnableLegacyAuthorization` is on, and it is off there by default. The first run of this
# met that as a 400 on the service's sign-in. The step that turns it on is the same thing an operator
# on that line does, and it is a fact about the pair of versions rather than about this plugin.
#
# The server, the install, the wizard and the administrator account are scripts/server-under-test.sh,
# which the other container checks source too. Everything else is this script's own, lives for the
# length of two containers, and is reachable only from loopback ports.
#
# usage: scripts/verify-bridge-round-trip.sh <image> <target-framework> [host-port] [service-port]
#   scripts/verify-bridge-round-trip.sh jellyfin/jellyfin:10.11.11 net9.0  18102 15055
#   scripts/verify-bridge-round-trip.sh jellyfin/jellyfin:12.0-rc4  net10.0 18103 15056

set -euo pipefail

image=${1:?server image, for example jellyfin/jellyfin:10.11.11}
framework=${2:?target framework, for example net9.0}
port=${3:-18102}
service_port=${4:-15055}

# The service, by tag and by digest. The tag says which release a reader should expect and the
# digest is what a run actually pulls, so a tag moved underneath this check is a pull that fails
# rather than a different service measured under the same name.
SERVICE_IMAGE="fallenbagel/jellyseerr:2.7.3@sha256:4538137bc5af902dece165f2bf73776d9cf4eafb6dd714670724af8f3eb77764"

# The two containers find each other by these names on a network of their own. Names without dots,
# because the target framework has one and a name with a dot is a name a resolver may treat as fully
# qualified.
SERVER_ALIAS=jellyfin
SERVICE_ALIAS=jellyseerr

# One film the form's own TMDB client can look up, because the form fetches the title's metadata
# from TMDB before it holds a request for it. The number is what the adapter sends and what the
# service is asked to hold; the title here is only what a person would have typed.
TMDB_ID=550
TITLE="Fight Club"
YEAR=1999

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)

# shellcheck source=scripts/server-under-test.sh
. "$repo_root/scripts/server-under-test.sh"

NETWORK="bridge-check-$framework"
SERVICE="jellyseerr-bridge-check-$framework"
SERVICE_BASE="http://127.0.0.1:$service_port"
SERVICE_KEY=""

cookies=$(mktemp)

# What the two sides wrote about the handover, read before either container is removed, so a red
# run carries the reason and a green one carries the service's own account of what it did. The
# plugin's lines name the address and never the key, which docs/bridge.md claims and the suite holds.
cleanup() {
    set +e
    step "what the plugin wrote about the bridge"
    dk exec "${CONTAINER:-}" sh -c 'grep -h "external request service\|handed to\|econciliation" /config/log/*.log' 2>/dev/null | tail -n 40
    step "what the service wrote"
    dk logs --tail 60 "$SERVICE" 2>&1 | grep -v 'Warning: '
    server_stop
    dk rm --force "$SERVICE" >/dev/null 2>&1
    dk network rm "$NETWORK" >/dev/null 2>&1
    rm -f "$cookies"
}
trap cleanup EXIT

# One field out of a JSON object on stdin, matched without regard to case, so a reader here does not
# fix a spelling this check is not about. Which casing the server's serialiser hands a plugin's own
# shapes back in is a property the suite asserts against the bytes.
FIELD='
import json, sys

name = sys.argv[1]
holder = json.load(sys.stdin)
for key, value in holder.items():
    if key.lower() == name.lower():
        print(value if not isinstance(value, (dict, list)) else json.dumps(value))
        break
else:
    sys.exit("no {0} in {1}".format(name, sorted(holder)))
'

# The same lookup, as a prelude the verdicts below start with.
PYFIELD='
def field(holder, name):
    for key, value in holder.items():
        if key.lower() == name.lower():
            return value
    raise SystemExit("no {0} in {1}".format(name, sorted(holder)))
'

# One call to the service with the key it issued. The key is a header and nothing else, which is the
# one shape the adapter is held to as well; --fail-with-body so a refusal prints what it refused.
service() {
    local method=${1:?method} path=${2:?path} body=${3:-}

    if [ -n "$body" ]; then
        curl --silent --fail-with-body --max-time 60 -X "$method" "$SERVICE_BASE/api/v1$path" \
            -H 'Content-Type: application/json' \
            -H "X-Api-Key: $SERVICE_KEY" \
            -d "$body"
    else
        curl --silent --fail-with-body --max-time 60 -X "$method" "$SERVICE_BASE/api/v1$path" \
            -H "X-Api-Key: $SERVICE_KEY"
    fi
}

# One call to the service as the signed-in administrator, before a key is known. Only the setup
# uses it: the session is what the sign-in route hands out, and the key is read through it once.
service_session() {
    local method=${1:?method} path=${2:?path} body=${3:-}

    if [ -n "$body" ]; then
        curl --silent --fail-with-body --max-time 60 -X "$method" "$SERVICE_BASE/api/v1$path" \
            -H 'Content-Type: application/json' \
            -b "$cookies" -c "$cookies" \
            -d "$body"
    else
        curl --silent --fail-with-body --max-time 60 -X "$method" "$SERVICE_BASE/api/v1$path" \
            -b "$cookies" -c "$cookies"
    fi
}

# One authenticated call as somebody other than the administrator.
as() {
    local token=${1:?token} method=${2:?method} path=${3:?path} body=${4:-}

    if [ -n "$body" ]; then
        curl --silent --fail-with-body --max-time 30 -X "$method" "$BASE$path" \
            -H 'Content-Type: application/json' \
            -H "Authorization: $AUTH_HEADER, Token=\"$token\"" \
            -d "$body"
    else
        curl --silent --fail-with-body --max-time 30 -X "$method" "$BASE$path" \
            -H "Authorization: $AUTH_HEADER, Token=\"$token\""
    fi
}

# An account on the server, created by the administrator and authenticated as itself. Prints the
# identifier and the token, separated by a space. The password is generated by the caller rather
# than written here: it exists for the length of one container on a loopback port.
account() {
    local name=${1:?account name} password=${2:?password} authenticated

    api POST /Users/New "$(printf '{"Name": "%s", "Password": "%s"}' "$name" "$password")" >/dev/null

    authenticated=$(curl --silent --fail --max-time 30 -X POST "$BASE/Users/AuthenticateByName" \
        -H 'Content-Type: application/json' \
        -H "Authorization: $AUTH_HEADER" \
        -d "$(printf '{"Username": "%s", "Pw": "%s"}' "$name" "$password")")

    printf '%s' "$authenticated" | python3 -c '
import json, sys

answer = json.load(sys.stdin)
token = answer.get("AccessToken")
identifier = (answer.get("User") or {}).get("Id")
if not token or not identifier:
    sys.exit("the server authenticated that account and named no token or no user.")
print(identifier, token)
'
}

server_start "$image" "$framework" "$port" "jellyfin-bridge-check-$framework"

prefix=/MediaRequests/v1

step "put the server on a network the service can reach it on"
# A network of its own rather than the default one, because on the default network containers
# reach each other by address only, and the address the service is told has to survive a restart.
dk network rm "$NETWORK" >/dev/null 2>&1 || true
dk network create "$NETWORK" >/dev/null
dk network connect --alias "$SERVER_ALIAS" "$NETWORK" "$CONTAINER"
echo "$CONTAINER is $SERVER_ALIAS on $NETWORK"

step "start the service from $SERVICE_IMAGE"
dk rm --force "$SERVICE" >/dev/null 2>&1 || true
dk run --detach --name "$SERVICE" \
    --network "$NETWORK" --network-alias "$SERVICE_ALIAS" \
    --publish "127.0.0.1:$service_port:5055" \
    --env LOG_LEVEL=info \
    "$SERVICE_IMAGE" >/dev/null

step "wait for the service to answer"
# Three answers in a row, for the reason the server is waited for the same way: a port that accepts
# while the process is still coming up is not a service that is ready.
status="" settled=0
for _ in $(seq 1 120); do
    if status=$(curl --silent --fail --max-time 5 "$SERVICE_BASE/api/v1/status" 2>/dev/null); then
        settled=$((settled + 1))
        if [ "$settled" -ge 3 ]; then
            break
        fi
        sleep 1
        continue
    fi
    settled=0
    status=""
    sleep 2
done
test "$settled" -ge 3
printf '%s\n' "$status"
SERVICE_VERSION=$(printf '%s' "$status" | python3 -c "$FIELD" version)
echo "the service is up at version $SERVICE_VERSION"

step "an administrator account on the server for the service to sign in with"
# The service's first sign-in has to be a Jellyfin administrator, which its own sign-in route checks,
# and the account the wizard made belongs to the shared script and keeps its password to itself. So
# a second administrator is made here: an ordinary account, then its policy read back, changed in
# the one field, and written again, which is what the dashboard does.
SERVICE_ADMIN=bridge-check-service
service_admin_password=$(python3 -c 'import secrets; print(secrets.token_urlsafe(24))')
read -r SERVICE_ADMIN_ID _ <<<"$(account "$SERVICE_ADMIN" "$service_admin_password")"
test -n "$SERVICE_ADMIN_ID"
policy=$(api GET "/Users/$SERVICE_ADMIN_ID" | python3 -c '
import json, sys
policy = json.load(sys.stdin)["Policy"]
policy["IsAdministrator"] = True
print(json.dumps(policy))
')
api POST "/Users/$SERVICE_ADMIN_ID/Policy" "$policy"
api GET "/Users/$SERVICE_ADMIN_ID" | python3 -c '
import json, sys
user = json.load(sys.stdin)
if not user["Policy"].get("IsAdministrator"):
    sys.exit("{0} is not an administrator after its policy was written, so the service could not sign in as it.".format(user.get("Name")))
print("{0} ({1}) is an administrator".format(user.get("Name"), user.get("Id")))
'

step "let the server read the header the service signs in with"
# Jellyseerr 2.7.3 names its client in `X-Emby-Authorization`, which a 10.11 server reads and a 12.0
# server reads only while `EnableLegacyAuthorization` is on; it is off by default there, and a
# sign-in with no client named is refused with 400 before a password is looked at. So the switch is
# turned on where the server has it, and the transcript says which it found. An operator on the 12.0
# line meets the same wall with this version of the service and turns the same switch.
system=$(api GET /System/Configuration)
printf '%s' "$system" | python3 -c '
import json, sys
configuration = json.load(sys.stdin)
if "EnableLegacyAuthorization" not in configuration:
    print("the server has no EnableLegacyAuthorization setting; nothing to turn on")
else:
    print("EnableLegacyAuthorization was {0}".format(configuration["EnableLegacyAuthorization"]))
'
if printf '%s' "$system" | python3 -c 'import json, sys; sys.exit(0 if json.load(sys.stdin).get("EnableLegacyAuthorization") is False else 1)'; then
    api POST /System/Configuration "$(printf '%s' "$system" | python3 -c '
import json, sys
configuration = json.load(sys.stdin)
configuration["EnableLegacyAuthorization"] = True
print(json.dumps(configuration))
')"
    api GET /System/Configuration | python3 -c '
import json, sys
if json.load(sys.stdin).get("EnableLegacyAuthorization") is not True:
    sys.exit("EnableLegacyAuthorization is still off after being written.")
print("EnableLegacyAuthorization is on now")
'
fi

step "the service's first sign-in, which creates its administrator"
# Its own route: username and password against the server it is told about, and the first user it
# ever sees becomes its administrator. The port and the empty base path are written out rather than
# left to default, because the route builds the address by pasting the four parts together.
signed_in=$(service_session POST /auth/jellyfin "$(printf '{"username": "%s", "password": "%s", "hostname": "%s", "port": 8096, "urlBase": "", "useSsl": false, "serverType": 2}' \
    "$SERVICE_ADMIN" "$service_admin_password" "$SERVER_ALIAS")")
# Only the fields that say who was made. The whole answer is that service's user object and what it
# carries is its business, not this transcript's.
printf '%s' "$signed_in" | python3 -c '
import json, sys
user = json.load(sys.stdin)
print("the service made user id={0} permissions={1} for Jellyfin user {2!r}".format(
    user.get("id"), user.get("permissions"), user.get("jellyfinUsername")))
if user.get("id") != 1:
    sys.exit("the first sign-in did not become user 1, so it is not the administrator the setup needs.")
'

step "read the key the service issued, and finish its setup"
# The key is read once through the session and then travels only as the header the adapter sends
# it in. It is never printed: it is a credential to a container that is gone in a minute, and a
# transcript is the one thing here that outlives it.
SERVICE_KEY=$(service_session GET /settings/main | python3 -c "$FIELD" apiKey)
test -n "$SERVICE_KEY"
echo "the service issued a key of ${#SERVICE_KEY} characters"
service_session POST /settings/initialize | python3 -c '
import json, sys
public = json.load(sys.stdin)
if not public.get("initialized"):
    sys.exit("the service does not report itself initialized after being told to be.")
print("the service reports initialized")
'

step "the key opens the service's own request list, which its status route does not need"
# The status route is public and a green answer from it says nothing about the key, which
# docs/bridge.md records as the bound on the plugin's own reachability answer. So the key is proven
# accepted here on a route that refuses without one, before anything is handed over with it.
service GET '/request?take=20' | python3 -c '
import json, sys
listed = json.load(sys.stdin)
print("the service holds {0} request(s) before anything is handed over".format(len(listed.get("results", []))))
if listed.get("results"):
    sys.exit("a fresh service already holds a request, so nothing below could tell its own from it.")
'

step "point the plugin at the service"
# The whole configuration is read, two fields written, and the whole written back, because that is
# what the dashboard sends and the configuration rules run on the way in: an address with no key
# would be refused here rather than dialled.
BRIDGE_ADDRESS="http://$SERVICE_ALIAS:5055"
configuration=$(api GET "/Plugins/$PLUGIN_ID/Configuration" | BRIDGE_ADDRESS="$BRIDGE_ADDRESS" SERVICE_KEY="$SERVICE_KEY" python3 -c '
import json, os, sys
configuration = json.load(sys.stdin)
configuration["BridgeAddress"] = os.environ["BRIDGE_ADDRESS"]
configuration["BridgeApiKey"] = os.environ["SERVICE_KEY"]
print(json.dumps(configuration))
')
api POST "/Plugins/$PLUGIN_ID/Configuration" "$configuration"
api GET "/Plugins/$PLUGIN_ID/Configuration" | BRIDGE_ADDRESS="$BRIDGE_ADDRESS" python3 -c '
import json, os, sys
configuration = json.load(sys.stdin)
if configuration.get("BridgeAddress") != os.environ["BRIDGE_ADDRESS"]:
    sys.exit("the server holds {0!r} as the bridge address rather than what was written.".format(configuration.get("BridgeAddress")))
if not configuration.get("BridgeApiKey"):
    sys.exit("the server holds no bridge key after one was written.")
print("BridgeAddress={0}  BridgeApiKey=set, {1} characters  BridgeAccounts={2} row(s)".format(
    configuration["BridgeAddress"], len(configuration["BridgeApiKey"]), len(configuration.get("BridgeAccounts") or [])))
'

step "the plugin reports the bridge reachable"
# Reachable is the status route answering, and nothing about the key: docs/bridge.md says so and
# this does not claim more. That the key is accepted was shown two steps up on a route that needs it.
api GET "$prefix/Health" | python3 -c "$PYFIELD"'
import json, sys
health = json.load(sys.stdin)
bridge = field(health, "bridge")
print("Bridge={0}  BridgeLastReachableAt={1}".format(bridge, field(health, "bridgeLastReachableAt")))
if str(bridge).lower() != "reachable":
    sys.exit("the plugin reports the bridge as {0!r} with the service up and configured.".format(bridge))
'
api GET "$prefix/Capabilities" | python3 -c "$PYFIELD"'
import json, sys
capabilities = json.load(sys.stdin)
print("BridgeConfigured={0}".format(field(capabilities, "bridgeConfigured")))
if not field(capabilities, "bridgeConfigured"):
    sys.exit("the capabilities say no bridge is configured after an address was written.")
'

step "a person asks for a film"
asker_password=$(python3 -c 'import secrets; print(secrets.token_urlsafe(24))')
read -r ASKER_ID ASKER_TOKEN <<<"$(account bridge-check-asker "$asker_password")"
test -n "$ASKER_ID"
test "$ASKER_ID" != "$ADMIN_ID"
created=$(as "$ASKER_TOKEN" POST "$prefix/Requests" "$(printf '{
  "kind": "Movie",
  "title": "%s",
  "year": %s,
  "providerIds": { "Tmdb": "%s" }
}' "$TITLE" "$YEAR" "$TMDB_ID")")
printf '%s\n' "$created"
REQUEST_ID=$(printf '%s' "$created" | python3 -c "$FIELD" id)
# The creation answers with the identifier and the outcome and not the row, so the revision an
# operator decides against is read off the queue, which is where an operator reads it too.
REVISION=$(api GET "$prefix/Requests/Queue?take=200" | REQUEST_ID="$REQUEST_ID" python3 -c "$PYFIELD"'
import json, os, sys
rows = [row for row in field(json.load(sys.stdin), "requests") if str(field(row, "id")) == os.environ["REQUEST_ID"]]
if len(rows) != 1:
    sys.exit("the queue holds {0} row(s) for the request just made.".format(len(rows)))
print(field(rows[0], "revision"))
')
echo "request $REQUEST_ID at revision $REVISION"

step "the administrator approves it, which is the handover"
# The answer is the request as the queue holds it after the submission ran: approved, carrying the
# service's own number for it, and no failed-handover mark. A handover the service refused would
# come back approved too, with the mark set and no reference, which is the near-miss this verdict
# is written against.
approved=$(api POST "$prefix/Requests/$REQUEST_ID/Approve" "{\"revision\": $REVISION}")
printf '%s\n' "$approved"
REFERENCE=$(printf '%s' "$approved" | python3 -c "$PYFIELD"'
import json, sys
row = json.load(sys.stdin)
state = field(row, "state")
if str(state).lower() != "approved":
    sys.exit("the request is {0!r} after approval.".format(state))
if field(row, "handoverFailedAt") is not None:
    sys.exit("the handover failed at {0}: the approval stands and the service has nothing.".format(field(row, "handoverFailedAt")))
backend = field(row, "backend")
if not backend:
    sys.exit("the approval carries no reference, so nothing was handed over and nothing failed either, which is the answer a server with no bridge gives.")
if str(field(backend, "service")).lower() != "overseerr":
    sys.exit("the reference names service {0!r}.".format(field(backend, "service")))
reference = field(backend, "id")
if not str(reference).isdigit():
    sys.exit("the reference {0!r} is not the number the form issues.".format(reference))
print(reference)
')
echo "the service called it $REFERENCE"

step "what the service holds under that number"
# The request over there: the same kind, the same TMDB number, and the status the mapping table
# names APPROVED, which is 2 in the form's own enumeration that docs/bridge.md quotes. It arrives
# under the service's administrator, because no mapping row names the person who asked, and the
# form's description says an administrator's request is approved on arrival. Its media status is
# printed and not asserted: what the service does about fetching is downstream of this check.
service GET "/request/$REFERENCE" | TMDB_ID="$TMDB_ID" python3 -c '
import json, os, sys
held = json.load(sys.stdin)
media = held.get("media") or {}
who = held.get("requestedBy") or {}
print("id={0}  type={1}  status={2}  media.tmdbId={3}  media.status={4}  requestedBy.id={5}  createdAt={6}".format(
    held.get("id"), held.get("type"), held.get("status"), media.get("tmdbId"), media.get("status"), who.get("id"), held.get("createdAt")))
if held.get("type") != "movie":
    sys.exit("the service holds a {0!r} and a film was asked for.".format(held.get("type")))
if str(media.get("tmdbId")) != os.environ["TMDB_ID"]:
    sys.exit("the service holds TMDB {0!r} and {1} was asked for.".format(media.get("tmdbId"), os.environ["TMDB_ID"]))
if held.get("status") != 2:
    sys.exit("the service reports status {0!r}, and 2 is what the mapping table holds as APPROVED.".format(held.get("status")))
print("the service holds the film under its own number, approved")
'
service GET '/request?take=20' | REFERENCE="$REFERENCE" python3 -c '
import json, os, sys
listed = json.load(sys.stdin).get("results", [])
if [str(row.get("id")) for row in listed] != [os.environ["REFERENCE"]]:
    sys.exit("the service lists {0} and the one request handed over is {1}.".format([row.get("id") for row in listed], os.environ["REFERENCE"]))
print("the service lists exactly that one request")
'

step "the reconciliation asks the service and reads the answer through the table"
# The scheduled task the plugin registers, started by hand rather than waited an hour for. Its
# answer for this request is the word APPROVED, which the table holds as inert: the service agrees
# with the decision this side already made, and the request stays approved.
TASK_ID=$(api GET /ScheduledTasks | python3 -c '
import json, sys
mine = [task for task in json.load(sys.stdin) if task.get("Key") == "RequestsBridgeReconciliation"]
if len(mine) != 1:
    sys.exit("the server lists {0} reconciliation task(s).".format(len(mine)))
print(mine[0]["Id"])
')
echo "task $TASK_ID"
api POST "/ScheduledTasks/Running/$TASK_ID"
for _ in $(seq 1 60); do
    if api GET "/ScheduledTasks/$TASK_ID" | python3 -c '
import json, sys
task = json.load(sys.stdin)
sys.exit(0 if task.get("State") == "Idle" and task.get("LastExecutionResult") else 1)
' 2>/dev/null; then
        break
    fi
    sleep 1
done
api GET "/ScheduledTasks/$TASK_ID" | python3 -c '
import json, sys
task = json.load(sys.stdin)
result = task.get("LastExecutionResult") or {}
print("State={0}  LastExecutionResult.Status={1}  StartTimeUtc={2}  EndTimeUtc={3}".format(
    task.get("State"), result.get("Status"), result.get("StartTimeUtc"), result.get("EndTimeUtc")))
if task.get("State") != "Idle" or not result:
    sys.exit("the reconciliation did not finish in the time given.")
if result.get("Status") != "Completed":
    sys.exit("the reconciliation ended {0!r}: {1}".format(result.get("Status"), result.get("ErrorMessage")))
'

step "the request is where the table says APPROVED leaves it"
api GET "$prefix/Requests/Queue?take=200" | REQUEST_ID="$REQUEST_ID" REFERENCE="$REFERENCE" python3 -c "$PYFIELD"'
import json, os, sys
rows = [row for row in field(json.load(sys.stdin), "requests") if str(field(row, "id")) == os.environ["REQUEST_ID"]]
if len(rows) != 1:
    sys.exit("the queue holds {0} row(s) for the request.".format(len(rows)))
row = rows[0]
state = field(row, "state")
backend = field(row, "backend") or {}
print("state={0}  backend={1}  handoverFailedAt={2}".format(state, json.dumps(backend), field(row, "handoverFailedAt")))
if str(state).lower() != "approved":
    sys.exit("the request is {0!r} after the reconciliation, and APPROVED moves nothing.".format(state))
if str(field(backend, "id")) != os.environ["REFERENCE"]:
    sys.exit("the reference moved to {0!r}.".format(field(backend, "id")))
'
api GET "$prefix/Health" | python3 -c "$PYFIELD"'
import json, sys
health = json.load(sys.stdin)
print("Bridge={0}  BridgeLastReachableAt={1}".format(field(health, "bridge"), field(health, "bridgeLastReachableAt")))
if field(health, "bridgeLastReachableAt") is None:
    sys.exit("nothing recorded the service as reachable, after the health endpoint and the reconciliation both asked it.")
'

step "done"
echo "one request from a person on $image ($framework) arrived on Jellyseerr $SERVICE_VERSION as request $REFERENCE, approved, and the reconciliation read it back through the table"

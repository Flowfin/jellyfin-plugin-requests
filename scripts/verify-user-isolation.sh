#!/usr/bin/env bash
# Ask a running Jellyfin of one claimed line whether one person's requests reach another person.
#
# The suite already asserts that the endpoint serving a person's own list reads the store for that
# person and nothing wider, and that the queue endpoint carries the elevation policy. Neither of
# those is the question #67 asks. A double has no session, no authorisation pipeline and no cache,
# so what a double cannot say is whether the server hands the same answer to a second caller, or
# whether a policy written on an action is a policy the server enforces.
#
# So this creates two ordinary accounts on a real server, has each of them ask for something, and
# reads back what each of them is given.
#
# THE ORDER OF THE CALLS IS PART OF THE CHECK. Asking once as one person and once as another says
# nothing about a cached answer, because an answer cached against the route rather than against the
# caller only comes back to the wrong person when a second caller arrives after the first. So the
# second person's list is asked for immediately after the first person's, and the first person's is
# asked for again after the server has answered both.
#
# What it does not do is render anything. Nothing here opens a browser, which is the headless rule
# in docs/testing.md, so what is checked is the answer the page draws from and the bytes of the page
# itself, not the markup a browser would make of them.
#
# The server, the install, the wizard and the administrator account are scripts/server-under-test.sh,
# which the plugin-load check and the activity check also source. The two ordinary accounts are this
# script's own. They live for the length of that container and are reachable only from a loopback
# port.
#
# usage: scripts/verify-user-isolation.sh <image> <target-framework> [host-port]
#   scripts/verify-user-isolation.sh jellyfin/jellyfin:10.11.11 net9.0  18100
#   scripts/verify-user-isolation.sh jellyfin/jellyfin:12.0-rc4  net10.0 18101

set -euo pipefail

image=${1:?server image, for example jellyfin/jellyfin:10.11.11}
framework=${2:?target framework, for example net9.0}
port=${3:-18100}

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)

# shellcheck source=scripts/server-under-test.sh
. "$repo_root/scripts/server-under-test.sh"

trap server_stop EXIT

server_start "$image" "$framework" "$port" "jellyfin-isolation-check-$framework"

prefix=/MediaRequests/v1

# The titles are distinctive so that finding one in an answer cannot be some other row that happened
# to be there, and so that a leak can be looked for in the raw bytes rather than only in the fields
# this script knows the names of.
first_title="A film only the first person asked for"
second_title="A film only the second person asked for"
shared_title="A film both of them asked for"
first_note="The first person typed this and nobody else may read it."
second_note="The second person typed this and nobody else may read it."

# One authenticated call as somebody other than the administrator. The token is the first argument
# and everything after it is the call. `--fail-with-body` for the reason the shared `api` uses it: a
# refusal from this plugin names the field that was wrong, and a check that threw the body away
# would print a status code and nothing to act on.
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

# The same call, writing the body to a file and printing the status code, for the two places where a
# refusal is the thing being checked and a non-zero exit would end the run before it is read.
status_as() {
    local token=${1:?token} method=${2:?method} path=${3:?path} out=${4:?body file}

    curl --silent --max-time 30 -X "$method" "$BASE$path" \
        -H "Authorization: $AUTH_HEADER, Token=\"$token\"" \
        --output "$out" --write-out '%{http_code}'
}

# An ordinary account, created by the administrator and authenticated as itself. Prints the
# identifier and the token, separated by a space.
#
# The account is refused if the server made it an administrator, because every refusal below would
# then be a refusal this check did not test. The password is generated rather than written here: it
# exists for the length of one container on a loopback port, and a literal in a script is a literal
# somebody copies into a server that is neither.
ordinary_account() {
    local name=${1:?account name} password created authenticated
    password=$(python3 -c 'import secrets; print(secrets.token_urlsafe(24))')

    created=$(api POST /Users/New "$(printf '{"Name": "%s", "Password": "%s"}' "$name" "$password")")
    printf '%s' "$created" | python3 -c '
import json, sys

user = json.load(sys.stdin)
policy = user.get("Policy") or {}
if policy.get("IsAdministrator"):
    sys.exit("{0} was created as an administrator, which is not the account this check needs.".format(user.get("Name")))
' >&2

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

# Refuses unless an answer holds exactly the titles named for that caller, and unless none of the
# strings that belong to somebody else appears anywhere in it.
#
# THE RAW BYTES ARE READ AND NOT ONLY THE FIELDS. A leak arriving in a field this script does not
# know the name of is the leak worth catching, and a reader walking a list of field names would hand
# it back green.
VERDICT='
import json, sys

label = sys.argv[1]
expected = sorted(line for line in sys.argv[2].splitlines() if line)
forbidden = [line for line in sys.argv[3].splitlines() if line]

raw = sys.stdin.read()
answer = json.loads(raw)

rows = answer["Requests"] if "Requests" in answer else answer["requests"]
titles = sorted(row.get("DisplayTitle", row.get("displayTitle")) for row in rows)

if titles != expected:
    sys.exit("{0} was given {1} and what belongs to that caller is {2}.".format(label, titles, expected))

for secret in forbidden:
    if secret in raw:
        sys.exit("{0} was given {1!r}, which belongs to somebody else.".format(label, secret))

print("{0} was given {1}".format(label, titles))
'

step "make two ordinary accounts"
read -r FIRST_ID FIRST_TOKEN <<<"$(ordinary_account isolation-check-one)"
read -r SECOND_ID SECOND_TOKEN <<<"$(ordinary_account isolation-check-two)"
test -n "$FIRST_ID"
test -n "$SECOND_ID"
test "$FIRST_ID" != "$SECOND_ID"
test "$FIRST_ID" != "$ADMIN_ID"
test "$SECOND_ID" != "$ADMIN_ID"
echo "first $FIRST_ID, second $SECOND_ID, administrator $ADMIN_ID"

step "the first person asks for something"
as "$FIRST_TOKEN" POST "$prefix/Requests" "$(printf '{
  "kind": "Movie",
  "title": "%s",
  "year": 1999,
  "providerIds": { "Tmdb": "isolation-check-first" },
  "note": "%s"
}' "$first_title" "$first_note")"
echo

step "the second person asks for something else"
as "$SECOND_TOKEN" POST "$prefix/Requests" "$(printf '{
  "kind": "Movie",
  "title": "%s",
  "year": 2001,
  "providerIds": { "Tmdb": "isolation-check-second" },
  "note": "%s"
}' "$second_title" "$second_note")"
echo

step "both of them ask for one and the same thing"
# A title somebody has already asked for is joined rather than asked for twice, so this is the one
# row on the server both people are entitled to see. It is the sharpest case here: the row is
# legitimately shared and the note written on it is not.
as "$FIRST_TOKEN" POST "$prefix/Requests" "$(printf '{
  "kind": "Movie",
  "title": "%s",
  "year": 1975,
  "providerIds": { "Tmdb": "isolation-check-shared" },
  "note": "%s"
}' "$shared_title" "$first_note")"
echo
as "$SECOND_TOKEN" POST "$prefix/Requests" "$(printf '{
  "kind": "Movie",
  "title": "%s",
  "year": 1975,
  "providerIds": { "Tmdb": "isolation-check-shared" },
  "note": "%s"
}' "$shared_title" "$second_note")"
echo

step "what the first person is given"
as "$FIRST_TOKEN" GET "$prefix/Requests?take=200" | python3 -c "$VERDICT" \
    "the first person" \
    "$first_title
$shared_title" \
    "$second_title
$second_note
$SECOND_ID"

step "what the second person is given, immediately after"
# Immediately after the call above, because that is when an answer cached against the route rather
# than against the caller comes back to the wrong person.
as "$SECOND_TOKEN" GET "$prefix/Requests?take=200" | python3 -c "$VERDICT" \
    "the second person" \
    "$second_title
$shared_title" \
    "$first_title
$first_note
$FIRST_ID"

step "what the first person is given once the server has answered both"
as "$FIRST_TOKEN" GET "$prefix/Requests?take=200" | python3 -c "$VERDICT" \
    "the first person, asking again" \
    "$first_title
$shared_title" \
    "$second_title
$second_note
$SECOND_ID"

step "the queue is refused to somebody who is not an administrator"
# The elevation on that action is asserted in the suite as an attribute on a method. Whether the
# server enforces it is a property of the server, and nothing on this board has asked one until now.
queue_body=$(mktemp)
queue_status=$(status_as "$FIRST_TOKEN" GET "$prefix/Requests/Queue?take=200" "$queue_body")
echo "the queue answered $queue_status"
cat "$queue_body"
echo
python3 -c '
import sys

status = sys.argv[1]
body = open(sys.argv[2], encoding="utf-8", errors="replace").read()
forbidden = sys.argv[3:]

if status == "200":
    sys.exit("the queue was served to somebody who is not an administrator.")
if status not in ("401", "403"):
    sys.exit("the queue answered {0}, which is neither the refusal this looks for nor a serving.".format(status))
for secret in forbidden:
    if secret in body:
        sys.exit("the refusal carried {0!r}, which belongs to somebody else.".format(secret))
print("the queue refused with {0} and carried nothing that belongs to anybody.".format(status))
' "$queue_status" "$queue_body" "$second_title" "$second_note" "$SECOND_ID"
rm -f "$queue_body"

step "the administrator is not refused the same call"
# Without this, the step above would pass on a server where the queue is broken for everybody, which
# is a different thing from a queue closed to a person who may not read it.
api GET "$prefix/Requests/Queue?take=200" | python3 -c '
import json, sys

wanted = sys.argv[1:]
answer = json.load(sys.stdin)
rows = answer["Requests"] if "Requests" in answer else answer["requests"]
titles = [row.get("DisplayTitle", row.get("displayTitle")) for row in rows]
for title in wanted:
    if title not in titles:
        sys.exit("the queue does not hold {0!r}, so the refusal above says nothing.".format(title))
print("the administrator is served {0} rows and all three titles are among them.".format(len(rows)))
' "$first_title" "$second_title" "$shared_title"

step "the page a person opens carries no row of anybody's"
# The page draws its rows from the call already checked above rather than carrying them, and this is
# where that stops being an assumption about the served bytes. A page that ever did carry them would
# be a second copy of the answer, on a route where nothing above would look.
page_body=$(mktemp)
for who in first second; do
    case $who in
        first) token=$FIRST_TOKEN ;;
        second) token=$SECOND_TOKEN ;;
    esac
    page_status=$(status_as "$token" GET "$prefix/Page" "$page_body")
    echo "the page answered the $who person with $page_status, $(wc -c <"$page_body") bytes"
    python3 -c '
import sys

who, status = sys.argv[1], sys.argv[2]
body = open(sys.argv[3], encoding="utf-8", errors="replace").read()
forbidden = sys.argv[4:]

if status != "200":
    sys.exit("the page answered the {0} person with {1} rather than serving it.".format(who, status))
for secret in forbidden:
    if secret in body:
        sys.exit("the page served to the {0} person carries {1!r}.".format(who, secret))
print("the page served to the {0} person carries no title and no note.".format(who))
' "$who" "$page_status" "$page_body" "$first_title" "$second_title" "$shared_title" "$first_note" "$second_note"
done
rm -f "$page_body"

# The channel is the surface #67 is written about, and it is the one where a leak has a route the
# API does not. The server writes what a channel returns into its own library database, under a
# parent belonging to the channel rather than to the caller, and an item in that database is
# ordinarily visible to whoever can see the folder holding it.
#
# THAT ROUTE WAS MEASURED OPEN AND THE CHANNEL WAS CHANGED BECAUSE OF IT. Read against a channel
# that answered one person's own requests, this walk had the first person browse, then the second,
# then the first again, and on both claimed lines the first person came back holding a title only
# the second had asked for. #67's third condition says what happens then, and what the channel
# answers now is one folder saying where to read your own requests, the same for everybody.
#
# So what is checked below is an absence rather than a filter. A leg asserting that somebody is
# shown their own rows and not another person's is what the suite over the double already held, and
# it was green while the server was handing one person the other's answer.
#
# The channel name and the folder sentence are read out of the catalogue rather than written here. A
# second copy of either would disagree with the plugin the first time a sentence is reworded, and
# this check would then pass by finding no channel and no folder at all.
catalogue="$repo_root/Jellyfin.Plugin.Requests/Localisation/Strings/en.json"

word_for() {
    python3 -c '
import json, sys
print(json.load(open(sys.argv[1], encoding="utf-8"))[sys.argv[2]])
' "$catalogue" "${1:?catalogue key}"
}

# What the channel hands one person, written one answer per line: the root first, then every row of
# the root asked for as a folder.
#
# EVERY ROW OF THE ROOT IS ASKED FOR, rather than the ones the server marked as folders. Reading
# that mark would make the walk depend on a field whose absence leaves the inside of a folder unread
# and the verdict green, and a row that is not a folder is answered with nothing, which costs one
# call.
browse_channel() {
    local token=${1:?token} who=${2:?user id} out=${3:?answer file} root row

    root=$(as "$token" GET "/Channels/$CHANNEL_ID/Items?userId=$who")
    printf '%s\n' "$root" >"$out"

    for row in $(printf '%s' "$root" | python3 -c '
import json, sys
for row in json.load(sys.stdin)["Items"]:
    print(row["Id"])
'); do
        as "$token" GET "/Channels/$CHANNEL_ID/Items?userId=$who&folderId=$row" >>"$out"
        printf '\n' >>"$out"
    done
}

# Refuses unless the whole of what a person was handed is the one folder the catalogue names, with
# nothing inside it, and unless anything belonging to anybody appears anywhere in the bytes.
#
# THE FORBIDDEN SET IS BOTH PEOPLE ON EVERY CALL, not the other person. There is nothing per-user
# for this surface to get right or wrong now, so the first person reading their own title here would
# be the same defect as reading the second person's.
CHANNEL_VERDICT='
import json, sys

label = sys.argv[1]
folder = sys.argv[2]
forbidden = [line for line in sys.argv[3].splitlines() if line]

raw = open(sys.argv[4], encoding="utf-8", errors="replace").read()
answers = [json.loads(line) for line in raw.splitlines() if line.strip()]

if not answers:
    sys.exit("{0} was handed nothing at all by the channel.".format(label))

root = [row.get("Name") for row in answers[0]["Items"]]
if root != [folder]:
    sys.exit("{0} was handed {1} and the whole of this channel is {2}.".format(label, root, [folder]))

inside = [row.get("Name") for answer in answers[1:] for row in answer["Items"]]
if inside:
    sys.exit("{0} found {1} inside it, and nothing is meant to be there.".format(label, inside))

for secret in forbidden:
    if secret in raw:
        sys.exit("{0} was shown {1!r}, which belongs to somebody.".format(label, secret))

print("{0} was handed the one folder and nothing else.".format(label))
'

# Everything a request on this server carries that a person could be identified by or told apart
# from another by. None of it may reach this surface at all any more.
nobodys="$first_title
$second_title
$shared_title
$first_note
$second_note
$FIRST_ID
$SECOND_ID"

step "find the channel this plugin registers"
CHANNEL_NAME=$(word_for mine.title)
CHANNEL_FOLDER=$(word_for mine.channel.whereToLook)
CHANNEL_ID=$(as "$FIRST_TOKEN" GET "/Channels?userId=$FIRST_ID" | python3 -c '
import json, sys
wanted = sys.argv[1]
listed = [row for row in json.load(sys.stdin)["Items"] if row.get("Name") == wanted]
if not listed:
    sys.exit("no channel called {0!r} is listed, so nothing below would read this plugin.".format(wanted))
if len(listed) != 1:
    sys.exit("{0!r} is listed {1} times.".format(wanted, len(listed)))
print(listed[0]["Id"])
' "$CHANNEL_NAME")
echo "the channel is $CHANNEL_NAME, $CHANNEL_ID"

channel_body=$(mktemp)

step "what the first person is shown in the channel"
browse_channel "$FIRST_TOKEN" "$FIRST_ID" "$channel_body"
python3 -c "$CHANNEL_VERDICT" "the first person, browsing" "$CHANNEL_FOLDER" "$nobodys" "$channel_body"

step "what the second person is shown, immediately after"
# Immediately after, for the reason the three list calls above are ordered in the same way. An
# answer the server kept against the channel rather than against the caller only reaches the wrong
# person when a second caller arrives after the first, and this is the order that caught it when
# this surface still had something per person to get wrong.
browse_channel "$SECOND_TOKEN" "$SECOND_ID" "$channel_body"
python3 -c "$CHANNEL_VERDICT" "the second person, browsing" "$CHANNEL_FOLDER" "$nobodys" "$channel_body"

step "what the first person is shown once the server has answered both"
browse_channel "$FIRST_TOKEN" "$FIRST_ID" "$channel_body"
python3 -c "$CHANNEL_VERDICT" "the first person, browsing again" "$CHANNEL_FOLDER" "$nobodys" "$channel_body"

rm -f "$channel_body"

# What the three walks above cannot say. They ask the channel, and the route this issue is written
# about is what the server did with the answers afterwards: the rows are written into the library
# database under the channel as their parent, and asking the library for that parent reaches
# whatever is beneath it without going through this plugin at all.
library_body=$(mktemp)

step "what the library holds under the channel, asked by everybody in turn"
# The first person browsed the channel, so the folder is in the library under it and their answer
# has to carry it. Without that the whole reading passes on a server where this query answers with
# an empty set whatever is asked of it, which is a different thing from a library holding nothing of
# anybody. It is the same near-miss the queue is asked about twice for.
for who in first second administrator; do
    case $who in
        first) token=$FIRST_TOKEN; identifier=$FIRST_ID; expected=$CHANNEL_FOLDER ;;
        second) token=$SECOND_TOKEN; identifier=$SECOND_ID; expected= ;;
        administrator) token=$TOKEN; identifier=$ADMIN_ID; expected= ;;
    esac

    library_status=$(status_as "$token" GET \
        "/Items?userId=$identifier&parentId=$CHANNEL_ID&recursive=true&limit=500" "$library_body")
    echo "the library answered the $who with $library_status, $(wc -c <"$library_body") bytes"
    python3 -c '
import json, sys

who, status, folder, required = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
body = open(sys.argv[5], encoding="utf-8", errors="replace").read()
forbidden = [line for line in sys.argv[6].splitlines() if line]

if status != "200":
    sys.exit("the library answered the {0} with {1} rather than serving it.".format(who, status))

for secret in forbidden:
    if secret in body:
        sys.exit("the library served the {0} {1!r}, which belongs to somebody.".format(who, secret))

names = sorted({row.get("Name") for row in json.loads(body)["Items"]})

if required and names != [required]:
    sys.exit("the library holds {0} under the channel for the {1} and it has to hold {2}.".format(
        names, who, [required]))

if names not in ([], [folder]):
    sys.exit("the library holds {0} under the channel and the only thing there may be is {1}.".format(
        names, [folder]))

print("the library served the {0} {1} and carried nothing of anybody.".format(who, names))
' "$who" "$library_status" "$CHANNEL_FOLDER" "$expected" "$library_body" "$nobodys"
done

rm -f "$library_body"

step "the second person asking for the first person's channel is refused"
# The server's guard rather than this plugin's, and asked rather than assumed. Nothing per person
# reaches this surface any more, so what this holds is the shape of the refusal rather than a leak
# it prevents, and it is the leg that stays true if per-user rows are ever proposed again.
impersonation_body=$(mktemp)
impersonation_status=$(status_as "$SECOND_TOKEN" GET \
    "/Channels/$CHANNEL_ID/Items?userId=$FIRST_ID" "$impersonation_body")
echo "naming somebody else answered $impersonation_status"
python3 -c '
import sys

status = sys.argv[1]
body = open(sys.argv[2], encoding="utf-8", errors="replace").read()
forbidden = [line for line in sys.argv[3].splitlines() if line]

if status == "200":
    sys.exit("the channel was read for the first person by the second person.")
for secret in forbidden:
    if secret in body:
        sys.exit("the refusal carried {0!r}, which belongs to somebody.".format(secret))
print("naming somebody else was refused with {0} and carried nothing of anybody.".format(status))
' "$impersonation_status" "$impersonation_body" "$nobodys"
rm -f "$impersonation_body"

step "done"
echo "two people and three requests, one of them shared, on $image ($framework): neither was given the other's"

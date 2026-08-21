#!/usr/bin/env bash
# A Jellyfin server of one claimed line, with this plugin installed and an account to call it with.
#
# Two checks in this repository need the same server: the one that asks whether the plugin loaded at
# all, and the one that walks a request through its life and reads what the server recorded. Booting
# it is a dozen steps with a startup wizard in the middle, and two copies of those steps drift the
# first time a server line changes one of them. So the steps live here once and the checks source
# this file.
#
# This needs no display, no administrator rights and no trusted certificate. The server runs in a
# container, is reached over plain HTTP on the loopback interface, and is removed by the caller's
# trap when the run ends. The one credential it creates lives for the length of that container.
#
# What a caller gets, after `server_start`:
#
#   $BASE       the address to call, on the loopback interface
#   $TOKEN      an access token for the account the wizard created
#   $ADMIN_ID   that account's user identifier
#   $PLUGIN_ID  the identifier the server gave this plugin
#   api ...     curl with the address, the authorisation header and the failure flags already on it
#   dk ...      docker with the path rewriting Git Bash does turned off
#
# It decides nothing and asserts nothing beyond the server answering and the plugin being active,
# which is the precondition of every check that sources it rather than a check of its own.

# The assembly and the name the server lists it under. Both are read by every caller.
ASSEMBLY=Jellyfin.Plugin.Requests
PLUGIN_NAME=Requests

# Git Bash rewrites an argument that looks like an absolute path, which turns a container path into
# a Windows one. Turning that off for the whole script would break dotnet, which wants the native
# spelling, so it is turned off for the docker calls only.
dk() {
    MSYS_NO_PATHCONV=1 docker "$@"
}

step() { printf '\n== %s\n' "$*"; }

# Removes the container. A caller sets this as its own trap, because a trap set here would be
# replaced by the caller's and the container would outlive the run.
server_stop() {
    dk rm --force "${CONTAINER:-}" >/dev/null 2>&1 || true
}

# Starts a server, installs the plugin, completes the wizard and authenticates.
#
#   $1  the server image, for example jellyfin/jellyfin:10.11.11
#   $2  the target framework to publish for, for example net9.0
#   $3  the port on the loopback interface
#   $4  a name for the container, so two lines can run beside each other
server_start() {
    local image=${1:?server image} framework=${2:?target framework} port=${3:?port} name=${4:?container name}

    CONTAINER="$name"
    BASE="http://127.0.0.1:$port"

    local repo_root project publish_dir host_dll plugin_dir
    repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
    project="$repo_root/$ASSEMBLY/$ASSEMBLY.csproj"
    plugin_dir="/config/plugins/$ASSEMBLY"

    # The wizard needs an account before anything can be asked of the server. It exists for the
    # length of one container and is reachable only from this loopback port.
    local admin_user=verify admin_password
    admin_password=$(python3 -c 'import secrets; print(secrets.token_urlsafe(24))')

    AUTH_HEADER='MediaBrowser Client="plugin-load-check", Device="load-check", DeviceId="plugin-load-check", Version="1.0.0.0"'

    step "publish $ASSEMBLY for $framework"
    # Under the repository rather than in the temporary directory, because this path is handed to
    # both dotnet and docker and those two disagree about what an absolute path looks like on this
    # platform.
    publish_dir="$repo_root/artifacts/load-check/$framework"
    rm -rf "$publish_dir"
    dotnet publish "$project" -c Release -f "$framework" -o "$publish_dir" --nologo -v quiet
    ls -1 "$publish_dir"
    host_dll=$(cygpath --windows "$publish_dir/$ASSEMBLY.dll" 2>/dev/null || printf '%s' "$publish_dir/$ASSEMBLY.dll")

    step "start a server from $image"
    dk rm --force "$CONTAINER" >/dev/null 2>&1 || true
    dk run --detach --name "$CONTAINER" --publish "127.0.0.1:$port:8096" "$image" >/dev/null

    step "install the plugin"
    # The server is started first because /config is a volume: it is populated when the container
    # runs, and a copy made before that lands under a directory the running server never reads.
    local _
    for _ in $(seq 1 60); do
        if dk exec "$CONTAINER" test -d /config/plugins 2>/dev/null; then
            break
        fi
        sleep 1
    done
    dk exec "$CONTAINER" mkdir -p "$plugin_dir"
    # Only the assembly. The rest of the publish output is a symbol file and a documentation file,
    # and a package shipping those would be shipping what a server has no use for.
    dk cp "$host_dll" "$CONTAINER:$plugin_dir/$ASSEMBLY.dll"
    # Plugins are read at start, so the server has to come up again with the plugin already in place.
    dk restart "$CONTAINER" >/dev/null
    dk exec "$CONTAINER" ls -1 "$plugin_dir"

    step "wait for the server to answer"
    # THREE ANSWERS IN A ROW RATHER THAN ONE. The first run of this on a hosted runner ended here
    # with curl 56, a connection reset: the port accepts while the server is still coming up, so one
    # answer is not the server being ready and the next call is refused. What gets printed is the
    # body of the last answer, so nothing calls the endpoint again afterwards to have something to
    # show and races the same way.
    local info="" settled=0
    for _ in $(seq 1 120); do
        if info=$(curl --silent --fail --max-time 5 "$BASE/System/Info/Public" 2>/dev/null); then
            settled=$((settled + 1))
            if [ "$settled" -ge 3 ]; then
                break
            fi
            sleep 1
            continue
        fi
        settled=0
        info=""
        sleep 2
    done
    test "$settled" -ge 3
    printf '%s\n' "$info"

    step "complete the startup wizard"
    # These are open on a server whose wizard has not been completed. Completing it is what makes an
    # authenticated call possible, and everything below is an authenticated call.
    curl --silent --fail --max-time 30 -X POST "$BASE/Startup/Configuration" \
        -H 'Content-Type: application/json' \
        -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
    curl --silent --fail --max-time 30 "$BASE/Startup/User" >/dev/null
    curl --silent --fail --max-time 30 -X POST "$BASE/Startup/User" \
        -H 'Content-Type: application/json' \
        -d "{\"Name\":\"$admin_user\",\"Password\":\"$admin_password\"}"
    curl --silent --fail --max-time 30 -X POST "$BASE/Startup/RemoteAccess" \
        -H 'Content-Type: application/json' \
        -d '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}'
    curl --silent --fail --max-time 30 -X POST "$BASE/Startup/Complete" -H 'Content-Length: 0'
    echo "wizard completed"

    step "authenticate"
    local authenticated
    authenticated=$(curl --silent --fail --max-time 30 -X POST "$BASE/Users/AuthenticateByName" \
        -H 'Content-Type: application/json' \
        -H "Authorization: $AUTH_HEADER" \
        -d "{\"Username\":\"$admin_user\",\"Pw\":\"$admin_password\"}")
    TOKEN=$(printf '%s' "$authenticated" | python3 -c 'import json,sys; print(json.load(sys.stdin)["AccessToken"])')
    ADMIN_ID=$(printf '%s' "$authenticated" | python3 -c 'import json,sys; print(json.load(sys.stdin)["User"]["Id"])')
    test -n "$TOKEN"
    test -n "$ADMIN_ID"
    echo "authenticated as $ADMIN_ID"

    step "what the server says about its plugins"
    local plugins
    plugins=$(api GET /Plugins)
    printf '%s' "$plugins" | python3 -c '
import json, sys
for plugin in json.load(sys.stdin):
    print("Name={0}  Version={1}  Status={2}  Id={3}".format(
        plugin.get("Name"), plugin.get("Version"), plugin.get("Status"), plugin.get("Id")))
'

    step "verdict"
    PLUGIN_ID=$(printf '%s' "$plugins" | python3 -c '
import json, sys
name = sys.argv[1]
mine = [p for p in json.load(sys.stdin) if p.get("Name") == name]
if not mine:
    sys.exit("{0} is not in the plugin list: the server did not load it.".format(name))
if len(mine) != 1:
    sys.exit("{0} appears {1} times in the plugin list.".format(name, len(mine)))
status = mine[0].get("Status")
if status != "Active":
    sys.exit("{0} is {1} rather than Active.".format(name, status))
print(mine[0]["Id"])
' "$PLUGIN_NAME")
    echo "$PLUGIN_NAME is Active, id $PLUGIN_ID"
}

# One authenticated call. The body is read from the third argument where there is one.
#
#   api GET  /Plugins
#   api POST /MediaRequests/v1/Requests '{"kind":"Movie"}'
#
# `--fail-with-body` rather than `--fail`, because a refusal from this plugin carries the field that
# was wrong and a check that threw that away would print a status code and nothing to act on.
api() {
    local method=${1:?method} path=${2:?path} body=${3:-}

    if [ -n "$body" ]; then
        curl --silent --fail-with-body --max-time 30 -X "$method" "$BASE$path" \
            -H 'Content-Type: application/json' \
            -H "Authorization: $AUTH_HEADER, Token=\"$TOKEN\"" \
            -d "$body"
    else
        curl --silent --fail-with-body --max-time 30 -X "$method" "$BASE$path" \
            -H "Authorization: $AUTH_HEADER, Token=\"$TOKEN\""
    fi
}

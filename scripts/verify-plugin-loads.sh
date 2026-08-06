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

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
project="$repo_root/Jellyfin.Plugin.Requests/Jellyfin.Plugin.Requests.csproj"
assembly=Jellyfin.Plugin.Requests
plugin_name=Requests
container="jellyfin-plugin-load-check-$framework"
base="http://127.0.0.1:$port"
plugin_dir="/config/plugins/$assembly"

# The wizard needs an account before anything can be asked of the server. It exists for the length
# of one container and is reachable only from this loopback port.
admin_user=verify
admin_password=$(python3 -c 'import secrets; print(secrets.token_urlsafe(24))')

auth='MediaBrowser Client="plugin-load-check", Device="load-check", DeviceId="plugin-load-check", Version="1.0.0.0"'

# Git Bash rewrites an argument that looks like an absolute path, which turns a container path into
# a Windows one. Turning that off for the whole script would break dotnet, which wants the native
# spelling, so it is turned off for the docker calls only.
dk() {
    MSYS_NO_PATHCONV=1 docker "$@"
}

cleanup() {
    dk rm --force "$container" >/dev/null 2>&1 || true
}
trap cleanup EXIT

step() { printf '\n== %s\n' "$*"; }

step "publish $assembly for $framework"
# Under the repository rather than in the temporary directory, because this path is handed to both
# dotnet and docker and those two disagree about what an absolute path looks like on this platform.
publish_dir="$repo_root/artifacts/load-check/$framework"
rm -rf "$publish_dir"
dotnet publish "$project" -c Release -f "$framework" -o "$publish_dir" --nologo -v quiet
ls -1 "$publish_dir"
host_dll=$(cygpath --windows "$publish_dir/$assembly.dll" 2>/dev/null || printf '%s' "$publish_dir/$assembly.dll")

step "start a server from $image"
dk rm --force "$container" >/dev/null 2>&1 || true
dk run --detach --name "$container" --publish "127.0.0.1:$port:8096" "$image" >/dev/null

step "install the plugin"
# The server is started first because /config is a volume: it is populated when the container runs,
# and a copy made before that lands under a directory the running server never reads.
for _ in $(seq 1 60); do
    if dk exec "$container" test -d /config/plugins 2>/dev/null; then
        break
    fi
    sleep 1
done
dk exec "$container" mkdir -p "$plugin_dir"
# Only the assembly. The rest of the publish output is a symbol file and a documentation file, and
# a package shipping those would be shipping what a server has no use for.
dk cp "$host_dll" "$container:$plugin_dir/$assembly.dll"
# Plugins are read at start, so the server has to come up again with the plugin already in place.
dk restart "$container" >/dev/null
dk exec "$container" ls -1 "$plugin_dir"

step "wait for the server to answer"
for _ in $(seq 1 90); do
    if curl --silent --fail --max-time 5 "$base/System/Info/Public" >/dev/null 2>&1; then
        break
    fi
    sleep 2
done
curl --silent --fail --max-time 10 "$base/System/Info/Public"
printf '\n'

step "complete the startup wizard"
# These are open on a server whose wizard has not been completed. Completing it is what makes an
# authenticated call possible, and the plugin list is an authenticated call.
curl --silent --fail --max-time 30 -X POST "$base/Startup/Configuration" \
    -H 'Content-Type: application/json' \
    -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
curl --silent --fail --max-time 30 "$base/Startup/User" >/dev/null
curl --silent --fail --max-time 30 -X POST "$base/Startup/User" \
    -H 'Content-Type: application/json' \
    -d "{\"Name\":\"$admin_user\",\"Password\":\"$admin_password\"}"
curl --silent --fail --max-time 30 -X POST "$base/Startup/RemoteAccess" \
    -H 'Content-Type: application/json' \
    -d '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}'
curl --silent --fail --max-time 30 -X POST "$base/Startup/Complete" -H 'Content-Length: 0'
echo "wizard completed"

step "authenticate"
token=$(curl --silent --fail --max-time 30 -X POST "$base/Users/AuthenticateByName" \
    -H 'Content-Type: application/json' \
    -H "Authorization: $auth" \
    -d "{\"Username\":\"$admin_user\",\"Pw\":\"$admin_password\"}" |
    python3 -c 'import json,sys; print(json.load(sys.stdin)["AccessToken"])')
test -n "$token"
echo "authenticated"

step "what the server says about its plugins"
plugins=$(curl --silent --fail --max-time 30 "$base/Plugins" -H "Authorization: $auth, Token=\"$token\"")
printf '%s' "$plugins" | python3 -c '
import json, sys
for plugin in json.load(sys.stdin):
    print("Name={0}  Version={1}  Status={2}  Id={3}".format(
        plugin.get("Name"), plugin.get("Version"), plugin.get("Status"), plugin.get("Id")))
'

step "verdict"
plugin_id=$(printf '%s' "$plugins" | python3 -c '
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
' "$plugin_name")
echo "$plugin_name is Active, id $plugin_id"

step "the configuration page the dashboard would fetch"
# The embedded resource path is built at run time from the plugin's namespace. A half rename leaves
# a build that is green and a page that is empty, and this is where that shows.
page_status=$(curl --silent --output /dev/null --write-out '%{http_code}' --max-time 30 \
    "$base/web/ConfigurationPage?name=$plugin_name")
echo "GET /web/ConfigurationPage?name=$plugin_name -> $page_status"
test "$page_status" = "200"
curl --silent --fail --max-time 30 "$base/web/ConfigurationPage?name=$plugin_name" | head -5

step "the configuration the page reads and writes"
curl --silent --fail --max-time 30 "$base/Plugins/$plugin_id/Configuration" \
    -H "Authorization: $auth, Token=\"$token\""
printf '\n'
write_status=$(curl --silent --output /dev/null --write-out '%{http_code}' --max-time 30 \
    -X POST "$base/Plugins/$plugin_id/Configuration" \
    -H 'Content-Type: application/json' \
    -H "Authorization: $auth, Token=\"$token\"" \
    --data '{}')
echo "POST /Plugins/<id>/Configuration -> $write_status"
test "$write_status" = "204"

step "done"
echo "$plugin_name loaded and answered on $image ($framework)"

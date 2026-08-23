#!/usr/bin/env bash
# Ask the shipped store what a caller sees when the volume it writes to runs out of room.
#
# This is the third condition of #46. The other two are met by tests in the ordinary suite, which
# truncate the persisted bytes at every offset and stop a write in the middle. Neither of those
# needs anything the machine does not already have. Filling a disk does, and the headless rule in
# docs/testing.md refuses a suite that needs a container engine, so the measurement is made here
# rather than under `dotnet test`.
#
# WHAT IS FILLED IS A MOUNT WITH A HARD SIZE AND NOTHING ELSE. `--tmpfs /data:size=...` gives the
# container a filesystem of exactly that size, in memory, discarded when the container exits.
# Nothing outside it is written to, so the objection to filling a disk on somebody's machine does
# not apply to filling this one.
#
# The probe is tools/full-disk-probe. It drives the store this repository ships, not a copy of it,
# and it refuses to report a measurement it did not make: if every addition succeeds it exits 2
# saying so, which is what a mount that was never size limited produces. A missing or mistyped size
# is the mistake that would otherwise leave a green check that measured nothing.
#
# The runtime image is derived from the target framework rather than read out of
# scripts/server-lines.tsv. That file pairs a framework with the Jellyfin server image that runs
# the plugin, and no server is started here: what this needs is the .NET runtime the line
# provides, which is a different fact about the same framework.
#
# usage: scripts/verify-full-disk.sh <target-framework> [size]
#   scripts/verify-full-disk.sh net9.0
#   scripts/verify-full-disk.sh net10.0 256k

set -euo pipefail

framework=${1:?target framework, for example net9.0}
size=${2:-256k}

# The ASP.NET Core image rather than the bare runtime one. The plugin compiles against
# Jellyfin.Controller, which carries a framework reference to Microsoft.AspNetCore.App, and that
# reference is recorded in the probe's runtime configuration whether or not the store touches a
# type from it. The bare runtime image refuses to start such an application, which is how this was
# found: `No frameworks were found.` on both lines.
case "$framework" in
    net9.0) runtime=mcr.microsoft.com/dotnet/aspnet:9.0 ;;
    net10.0) runtime=mcr.microsoft.com/dotnet/aspnet:10.0 ;;
    *)
        echo "no .NET runtime image for $framework. The claimed lines are:" >&2
        grep -v '^#' "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/server-lines.tsv" >&2
        exit 1
        ;;
esac

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
out=$(mktemp -d)
trap 'rm -rf "$out"' EXIT

echo "== publishing the probe for $framework"

# Framework-dependent on purpose. The runtime under the measurement is then the one the image
# carries and named in this file, rather than one bundled at publish time, so a run on the wrong
# image fails to start instead of quietly measuring the other line.
dotnet publish "$root/tools/full-disk-probe/FullDiskProbe.csproj" \
    --configuration Release \
    --framework "$framework" \
    --output "$out"

# `mktemp -d` makes a directory only its owner may enter, and the runtime image runs as a user
# that is not that owner, so without this the container cannot see the published probe at all.
# It failed that way the first time this ran, with `realpath(/probe/...) failed: Permission
# denied`, which reads as a missing file rather than as a permission.
chmod -R a+rX "$out"

echo "== running the probe on a $size filesystem under $runtime"

# --network none because nothing here talks to anything, --cap-drop ALL and no-new-privileges
# because a probe needs none of it, and the published output goes in read-only: the only thing this
# container may write to is the mount it is here to fill.
# mode=1777 on that mount for the same reason as the chmod above: the container is not root, and
# the mount is the one place it has to be able to write.
docker run --rm \
    --network none \
    --cap-drop ALL \
    --security-opt no-new-privileges \
    --volume "$out:/probe:ro" \
    --tmpfs "/data:rw,size=$size,mode=1777" \
    "$runtime" \
    dotnet /probe/Jellyfin.Plugin.Requests.FullDiskProbe.dll /data

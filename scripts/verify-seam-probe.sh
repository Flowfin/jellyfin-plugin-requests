#!/usr/bin/env bash
# Ask a running Jellyfin of one claimed line what a SECOND plugin in the same process can see of
# this one.
#
# The failure #117 exists to prevent is invisible at build time and looks exactly like the sibling
# not being installed: if the two plugins do not share a load context, the type this plugin declares
# and the type the sibling names are not the same type, the container returns nothing, and nothing
# anywhere says why. No reading of this tree answers that. Only a server does, and the two claimed
# lines are different major versions of the host, so an answer from one is a claim about the other.
#
# WHAT THE PROBE IS AND WHY IT REFERENCES NOTHING. `tools/seam-probe` is a plugin of its own that
# names this plugin's assembly, its seam type, its member and its want by string and compiles
# against the host alone. That was a convenience while the shape was open and it is the shape itself
# since 2026-08-28, so what the probe does IS what a sibling does. An
# assembly reference would fail to resolve before anything could be reported on exactly the servers
# where the answer is interesting, and a probe that cannot run is a probe that says nothing.
#
# THIS REFUSED ONLY SILENCE UNTIL 2026-08-26 AND NOW REFUSES THE ANSWER TOO. While #117 listed three
# options for where the shared type comes from, a shared load context and a separate one were both
# results, and each decided which of the three was available. #117 took the third option on
# 2026-08-28 - no shared type at all, the handover taken by name through reflection - after runs of
# this script measured the other two unavailable on both claimed lines, and `docs/seam.md` carries
# the choice with both measurements. That choice rests on a plugin being able to name a type declared
# in another plugin's assembly AND to call the member it finds there, which is what the probe now
# asks. Once a decision rests on an answer, the opposite answer is a defect and not a result.
# `scripts/read-seam-probe-answer.sh` is what refuses it and carries the reasons one at a time;
# `scripts/prove-seam-probe-refusals.sh` is where each of them is watched biting.
#
# usage: scripts/verify-seam-probe.sh <image> <target-framework> [host-port]
#   scripts/verify-seam-probe.sh jellyfin/jellyfin:10.11.11 net9.0  18098
#   scripts/verify-seam-probe.sh jellyfin/jellyfin:12.0-rc4  net10.0 18099

set -euo pipefail

image=${1:?server image, for example jellyfin/jellyfin:10.11.11}
framework=${2:?target framework, for example net9.0}
port=${3:-18098}

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=scripts/server-under-test.sh
. "$here/server-under-test.sh"

repo_root=$(cd "$here/.." && pwd)
probe_out="$repo_root/artifacts/seam-probe/$framework"

# The server's log, kept in a file rather than in a pipe for the reason the load check gives about
# its configuration page: a pipe closed early turns a readable answer into an exit status.
probe_log=$(mktemp)
trap 'server_stop; rm -f "$probe_log"' EXIT

step "publish the probe for $framework"
# Under the repository rather than in a temporary directory, because this path is handed to both
# dotnet and docker and those two disagree about what an absolute path looks like on this platform.
rm -rf "$probe_out"
dotnet publish "$repo_root/tools/seam-probe/SeamProbe.csproj" -c Release -f "$framework" -o "$probe_out" --nologo -v quiet
# Only the assembly, for the same reason the plugin install takes only one file: a symbol file and a
# documentation file are not what a server has any use for.
find "$probe_out" -type f ! -name 'Jellyfin.Plugin.SeamProbe.dll' -delete
ls -1 "$probe_out"

EXTRA_PLUGIN_DIRECTORY="$probe_out" EXTRA_PLUGIN_NAME="SeamProbe" \
    server_start "$image" "$framework" "$port" "jellyfin-seam-probe-$framework"

step "what the second plugin could see"
dk logs "$CONTAINER" >"$probe_log" 2>&1 || true
grep -a "SEAM-PROBE" "$probe_log" || true

# The prose above is for a person. The verdict is one line and is read by a script of its own, so the
# same reading can be handed every answer it refuses without a server being started for each.
if ! "$here/read-seam-probe-answer.sh" "$probe_log"; then
    echo "What the server said about loading plugins:" >&2
    grep -aiE "seamprobe|plugin" "$probe_log" | tail -40 >&2
    exit 1
fi

#!/usr/bin/env bash
# Read the answer a seam probe run produced, and refuse the answers the tree cannot be built on.
#
# WHY THIS REFUSES NOW AND DID NOT BEFORE. When the probe was written, both answers were results:
# #117 listed three options for where the shared contract type comes from, and a shared load context
# and a separate one each decided which of the three was available. So the run refused silence and
# nothing else. That is no longer the position. #117 took the third option on 2026-08-28, after the
# other two were measured unavailable on both claimed lines - no shared type at all, the handover
# taken by name through reflection - and `docs/seam.md` carries the choice with both measurements.
# Once a decision rests on an answer, the opposite answer is a defect rather than a result, and a run
# that prints it and passes tells nobody.
#
# WHAT IT IS BUILT AGAINST HAS CHANGED WITH THE CHOICE, and the direction is worth stating because
# the old reason is still readable above it. It used to be that the contract type would move OUT of
# `Jellyfin.Plugin.Requests` into a package on the day that package landed, leaving the probe naming
# an assembly that no longer held it. There is no package now and the type is not going anywhere, so
# what this guards is the opposite: the type, the member and the want are named by string on both
# sides of the seam, so a rename here is what a sibling meets at runtime and nowhere else. This
# reader is the second half of that; `SeamSurfaceTests` in the suite is the first, and it reds
# without a server.
#
# THE CALL IS PART OF THE ANSWER SINCE THE THIRD OPTION WAS TAKEN. Being handed an implementation
# says the lookup works. It says nothing about whether the call can be made, and under reflection the
# call is where the remaining risk sits: the member is found by name, the want is built out of the
# other plugin's own type, and every one of those steps can fail at runtime with nothing failing at
# build time. So the probe makes the call a sibling makes, and a run whose lookup succeeded and whose
# call did not is refused rather than read as a working seam.
#
# It reads a file and nothing else: no container, no server, no network. That is what lets
# `scripts/prove-seam-probe-refusals.sh` watch every refusal here bite, one log at a time.
#
# usage: scripts/read-seam-probe-answer.sh <server-log>

set -euo pipefail

log=${1:?path to a file holding the server log written during a probe run}

if [ ! -f "$log" ]; then
    echo "read-seam-probe-answer: ${log} does not exist. The answer is read out of a run's log, never out of what a run was expected to say." >&2
    exit 1
fi

# The last one, because a server that restarted its hosted services writes the line again and the
# newest answer is the one about the process that ended up running.
answer=$(grep -a "SEAM-PROBE result " "$log" | tail -1 || true)

if [ -z "$answer" ]; then
    echo "read-seam-probe-answer: the probe wrote no result line, so this run measured nothing. A probe that could not run is the one outcome nobody can read, and it is refused rather than reported." >&2
    exit 1
fi

# Matched rather than word-split, so a line carrying the fields in some other order, or carrying a
# field this reader does not know, is refused as unreadable instead of being read as a passing one.
pattern='SEAM-PROBE result assemblies=([0-9]+) contract=(reachable|missing) implementations=([0-9]+) call=([a-z]+)$'
if [[ ! $answer =~ $pattern ]]; then
    echo "read-seam-probe-answer: the result line does not have the shape this reader knows, so nothing about the run can be read from it: ${answer}" >&2
    exit 1
fi

assemblies=${BASH_REMATCH[1]}
contract=${BASH_REMATCH[2]}
implementations=${BASH_REMATCH[3]}
call=${BASH_REMATCH[4]}

echo "the probe answered: assemblies=${assemblies} contract=${contract} implementations=${implementations} call=${call}"

if [ "$assemblies" -eq 0 ]; then
    echo "read-seam-probe-answer: no assembly named Jellyfin.Plugin.Requests was loaded, so this is an answer about a server that does not have this plugin in it rather than about what a second plugin can see of it." >&2
    exit 1
fi

if [ "$assemblies" -gt 1 ]; then
    echo "read-seam-probe-answer: ${assemblies} assemblies named Jellyfin.Plugin.Requests were loaded. Two copies of one assembly in one process is the failure this seam exists to avoid: each plugin resolves its own, the type this plugin registers and the type a sibling reaches are then different types with the same name, and the container returns nothing to the sibling. That was measured on both claimed lines on 2026-08-27 and it is why no shared type ships." >&2
    exit 1
fi

if [ "$contract" = "missing" ]; then
    echo "read-seam-probe-answer: the seam type is not reachable from a second plugin. Under the shape #117 took there is no package and no second copy, so the type is declared by this plugin and named by string from the other side; a type a second plugin cannot reach means that name no longer resolves, and the constants in tools/seam-probe/ContainerReport.cs are what name it." >&2
    exit 1
fi

if [ "$implementations" -eq 0 ]; then
    echo "read-seam-probe-answer: the container returned no implementation of the seam to the second plugin. That is the silence #117's fourth condition is about - an operator cannot tell it from a sibling that was never installed - so it is refused here rather than printed." >&2
    exit 1
fi

if [ "$call" != "answered" ]; then
    echo "read-seam-probe-answer: the lookup worked and the call did not (call=${call}). A seam nobody can call is not a seam, and under the shape #117 took the member and the want are found by name at runtime, so this is exactly where a rename lands: nomember is the member gone, nowant is the want type or one of its properties gone, failed is a call that did not come back with the answer the contract carries, and notattempted means nothing got far enough to try." >&2
    exit 1
fi

echo "One assembly, the seam reachable from a plugin that compiles against nothing of this one, ${implementations} implementation(s) handed back by the container, and the call made and answered. That is the shape docs/seam.md says the choice rests on."

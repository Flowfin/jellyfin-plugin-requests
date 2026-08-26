#!/usr/bin/env bash
# Read the answer a seam probe run produced, and refuse the answers the tree cannot be built on.
#
# WHY THIS REFUSES NOW AND DID NOT BEFORE. When the probe was written, both answers were results:
# #117 listed three options for where the shared contract type comes from, and a shared load context
# and a separate one each decided which of the three was available. So the run refused silence and
# nothing else. That is no longer the position. #117 chose one of the three on 2026-08-21 - a
# contract-only package both sides compile against, with exactly one copy shipped - and `docs/seam.md`
# carries the choice and says it rests on a plugin being able to name a type whose assembly ships in
# another plugin's directory. Once a decision rests on an answer, the opposite answer is a defect
# rather than a result, and a run that prints it and passes tells nobody.
#
# WHAT IT IS BUILT AGAINST is written on #117 on 2026-08-25: the contract type moves out of
# `Jellyfin.Plugin.Requests` the day the package lands, the probe's two constants stop naming
# anything that declares it, and a run that only refuses silence goes green on a measurement about an
# assembly that no longer holds the type. Whoever moves the type meets a red job that names the
# constants instead of discovering months later that the measurement had quietly stopped being about
# anything.
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
pattern='SEAM-PROBE result assemblies=([0-9]+) contract=(reachable|missing) implementations=([0-9]+)$'
if [[ ! $answer =~ $pattern ]]; then
    echo "read-seam-probe-answer: the result line does not have the shape this reader knows, so nothing about the run can be read from it: ${answer}" >&2
    exit 1
fi

assemblies=${BASH_REMATCH[1]}
contract=${BASH_REMATCH[2]}
implementations=${BASH_REMATCH[3]}

echo "the probe answered: assemblies=${assemblies} contract=${contract} implementations=${implementations}"

if [ "$assemblies" -eq 0 ]; then
    echo "read-seam-probe-answer: no assembly named Jellyfin.Plugin.Requests was loaded, so this is an answer about a server that does not have this plugin in it rather than about what a second plugin can see of it." >&2
    exit 1
fi

if [ "$assemblies" -gt 1 ]; then
    echo "read-seam-probe-answer: ${assemblies} assemblies named Jellyfin.Plugin.Requests were loaded. Two copies of one contract assembly in one process is the failure this seam exists to avoid: the type this plugin registers and the type a sibling names are then different types with the same name, the container returns nothing, and it looks exactly like the sibling not being installed." >&2
    exit 1
fi

if [ "$contract" = "missing" ]; then
    echo "read-seam-probe-answer: the contract type is not reachable from a second plugin. The shape chosen on #117 is one shipped copy of the contract that both sides name, so a type a second plugin cannot reach is that shape gone; if the type moved on purpose, the constants in tools/seam-probe/ContainerReport.cs are what name it and they move with it." >&2
    exit 1
fi

if [ "$implementations" -eq 0 ]; then
    echo "read-seam-probe-answer: the container returned no implementation of the contract to the second plugin. That is the silence #117's fourth condition is about - an operator cannot tell it from a sibling that was never installed - so it is refused here rather than printed." >&2
    exit 1
fi

echo "One assembly, the contract reachable from a plugin that ships no copy of it, and ${implementations} implementation(s) handed back by the container. That is the shape docs/seam.md says the choice rests on."

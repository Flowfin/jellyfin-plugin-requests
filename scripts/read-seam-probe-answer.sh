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
# WHAT IT WAS BUILT AGAINST HAS HAPPENED. #117 said on 2026-08-25 that the contract type would move
# out of `Jellyfin.Plugin.Requests` the day the package landed, that the probe's two constants would
# stop naming anything that declares it, and that a run only refusing silence would go green on a
# measurement about an assembly that no longer held the type. The type moved, the constants moved
# with it, and the assembly this reader names is now `Jellyfin.Plugin.Requests.Contract`.
#
# THREE FIELDS ARRIVED WITH THAT MOVE AND THEY ARE A DIFFERENT QUESTION FROM THE FIRST THREE. The
# first three are the reflection lookup, which asks whether a second plugin can find the type by
# name. The last three are the compile-time reference, which is the shape a sibling actually ships
# in: it resolves the contract as a package, names the type in its own source, and is bound by the
# runtime or is not. That is the third condition of #117, and a run that measured only the first
# three would answer a question no sibling asks.
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
pattern='SEAM-PROBE result assemblies=([0-9]+) contract=(reachable|missing) implementations=([0-9]+) binding=(bound|unbound) bound-implementations=([0-9]+) same-type=(yes|no)$'
if [[ ! $answer =~ $pattern ]]; then
    echo "read-seam-probe-answer: the result line does not have the shape this reader knows, so nothing about the run can be read from it: ${answer}" >&2
    exit 1
fi

assemblies=${BASH_REMATCH[1]}
contract=${BASH_REMATCH[2]}
implementations=${BASH_REMATCH[3]}
binding=${BASH_REMATCH[4]}
bound_implementations=${BASH_REMATCH[5]}
same_type=${BASH_REMATCH[6]}

echo "the probe answered: assemblies=${assemblies} contract=${contract} implementations=${implementations} binding=${binding} bound-implementations=${bound_implementations} same-type=${same_type}"

if [ "$assemblies" -eq 0 ]; then
    echo "read-seam-probe-answer: no assembly named Jellyfin.Plugin.Requests.Contract was loaded, so this is an answer about a server that does not have this plugin in it rather than about what a second plugin can see of it. The contract ships inside the plugin package and is named in the artifact list of build.yaml; a server that has the plugin assembly without it is an install that left a named artifact behind." >&2
    exit 1
fi

if [ "$assemblies" -gt 1 ]; then
    echo "read-seam-probe-answer: ${assemblies} assemblies named Jellyfin.Plugin.Requests.Contract were loaded. Two copies of one contract assembly in one process is the failure this seam exists to avoid: the type this plugin registers and the type a sibling names are then different types with the same name, the container returns nothing, and it looks exactly like the sibling not being installed. Exactly one copy ships, out of the plugin package; a second one arrives when somebody drops the ExcludeAssets or the Private=false from a consumer of the contract project." >&2
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

if [ "$binding" = "unbound" ]; then
    echo "read-seam-probe-answer: the COMPILE-TIME reference a second plugin holds to the contract did not bind. Finding the type by name and being bound to it by the runtime are two different things, and the second is the one a sibling depends on: a sibling names the type in its own source, resolves the contract as a package, and never reflects. A reference the runtime will not bind is a sibling that does not run, so this is refused rather than reported, and what the runtime raised is on the probe line above this verdict." >&2
    exit 1
fi

if [ "$same_type" = "no" ]; then
    echo "read-seam-probe-answer: the type the compile-time reference bound to is not the type found by name in the loaded contract assembly. That is two types with one full name in one process, which is the failure #117 exists against, arriving through the binding rather than through a second copy on disk. Nothing about it is visible at build time on either side." >&2
    exit 1
fi

if [ "$bound_implementations" -eq 0 ]; then
    echo "read-seam-probe-answer: the container returned no implementation for the type the second plugin named at compile time, although the same lookup by name was answered. A sibling asking the way a sibling actually asks would get nothing back, which is the silence the fourth condition of #117 is about." >&2
    exit 1
fi

echo "One assembly, the contract reachable from a plugin that ships no copy of it, ${implementations} implementation(s) handed back for the type found by name, and the compile-time reference bound to that same type with ${bound_implementations} implementation(s) behind it. That is the shape docs/seam.md says the choice rests on, measured the way a sibling asks."

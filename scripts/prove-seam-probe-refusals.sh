#!/usr/bin/env bash
# Every refusal the seam-probe reader claims, watched refusing once, over a log written to carry the
# answer it names.
#
# `scripts/read-seam-probe-answer.sh` is what decides whether a probe run passes. It runs on a server
# in a container, on two claimed lines, and neither of those can be produced on every machine that
# has to trust it; a reader nobody has watched saying no is a reader that might say yes to
# everything. It reads a file, so every one of its answers can be handed to it directly.
#
# THE ONE THIS EXISTS FOR IS THE LOG WITH NO RESULT LINE IN IT. Until this reader landed, a run
# passed if the probe wrote any line at all, so a probe reporting that the contract type was NOT
# reachable was a green job. That is the shape #117 names on 2026-08-25 as the failure to build
# against: the day the contract type moves into the package it stops being declared by the assembly
# the probe names, and a run that only refuses silence goes on passing about nothing.
#
# The near-miss beside it is `head` where the reader has `tail`. A server that restarts its hosted
# services writes the result line twice, and a reader taking the first one reports the answer of a
# process that is no longer running.
#
# The fixtures are written here rather than committed. What is being proved is the reading of one
# line, so a captured server log would add several hundred lines that mean nothing to it.
#
# It needs no container, no server and no network, so it runs in seconds.
#
# usage: scripts/prove-seam-probe-refusals.sh

set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
reader="$here/read-seam-probe-answer.sh"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# A log of the shape the server writes: the probe's prose lines, and the verdict last.
# usage: log <file> <result-line-or-empty>
log() {
    local file=$1 result=$2
    {
        printf '[2026-08-26 21:00:00.000 +00:00] [INF] [1] Main: Jellyfin version: 10.11.11\n'
        printf '[2026-08-26 21:00:01.000 +00:00] [WRN] [9] Jellyfin.Plugin.SeamProbe.ContainerReport: SEAM-PROBE assemblies loaded under the name Jellyfin.Plugin.Requests: 1\n'
        printf '[2026-08-26 21:00:01.000 +00:00] [WRN] [9] Jellyfin.Plugin.SeamProbe.ContainerReport: SEAM-PROBE one of them is at /config/plugins/Jellyfin.Plugin.Requests/Jellyfin.Plugin.Requests.dll\n'
        if [ -n "$result" ]; then
            printf '[2026-08-26 21:00:01.000 +00:00] [WRN] [9] Jellyfin.Plugin.SeamProbe.ContainerReport: %s\n' "$result"
        fi
        printf '[2026-08-26 21:00:02.000 +00:00] [INF] [1] Main: Startup complete\n'
    } > "$work/$file"
}

failures=0

# usage: refuses <heading> <sentence the refusal must carry> -- <command...>
refuses() {
    local heading=$1 sentence=$2
    shift 3
    printf '\n== %s\n' "$heading"
    local out status
    set +e
    out=$("$@" 2>&1)
    status=$?
    set -e
    printf '%s\n' "$out" | sed 's/^/  /'
    if [ "$status" -eq 0 ]; then
        echo "  ACCEPTED it, so the rule does not bite."
        failures=$((failures + 1))
        return
    fi
    case "$out" in
        *"$sentence"*) ;;
        *)
            echo "  refused, but for something other than: $sentence"
            failures=$((failures + 1))
            ;;
    esac
}

printf '\n== the answer both claimed lines gave on 2026-08-22 passes\n'
log clean.log 'SEAM-PROBE result assemblies=1 contract=reachable implementations=1'
if out=$("$reader" "$work/clean.log" 2>&1); then
    printf '%s\n' "$out" | sed 's/^/  /'
else
    printf '%s\n' "$out" | sed 's/^/  /'
    echo "  REFUSED the answer the tree was built on, which is a reader that refuses everything."
    failures=$((failures + 1))
fi

# The trap this reader exists for. The probe ran, said the type was not reachable, and every line it
# wrote carries the marker the old run grepped for, so this log used to be a green job.
log missing.log 'SEAM-PROBE result assemblies=1 contract=missing implementations=0'
refuses "the contract type is not reachable, which used to pass" \
    "the contract type is not reachable from a second plugin" \
    -- "$reader" "$work/missing.log"

log two.log 'SEAM-PROBE result assemblies=2 contract=reachable implementations=1'
refuses "two assemblies of one name in one process" \
    "Two copies of one contract assembly in one process is the failure this seam exists to avoid" \
    -- "$reader" "$work/two.log"

log none.log 'SEAM-PROBE result assemblies=0 contract=missing implementations=0'
refuses "the plugin under test was not loaded at all" \
    "no assembly named Jellyfin.Plugin.Requests was loaded" \
    -- "$reader" "$work/none.log"

log unanswered.log 'SEAM-PROBE result assemblies=1 contract=reachable implementations=0'
refuses "the container handed back nothing, which is the silence an operator meets" \
    "the container returned no implementation of the contract" \
    -- "$reader" "$work/unanswered.log"

# The probe threw before it reached an answer. It writes a line, and that line carries the marker, so
# only a reader looking for the verdict rather than for the marker refuses this.
log threw.log ''
printf '[2026-08-26 21:00:01.000 +00:00] [WRN] [9] Jellyfin.Plugin.SeamProbe.ContainerReport: SEAM-PROBE the probe itself failed: System.TypeLoadException: no\n' >> "$work/threw.log"
refuses "the probe failed before it had an answer" \
    "the probe wrote no result line, so this run measured nothing" \
    -- "$reader" "$work/threw.log"

log shape.log 'SEAM-PROBE result contract=reachable assemblies=1 implementations=1'
refuses "a result line whose fields this reader does not know" \
    "does not have the shape this reader knows" \
    -- "$reader" "$work/shape.log"

# The near-miss: `head` where the reader has `tail`. The server restarted its hosted services, the
# first answer was the good one and the process that is actually running gave the second.
log restarted.log 'SEAM-PROBE result assemblies=1 contract=reachable implementations=1'
printf '[2026-08-26 21:00:30.000 +00:00] [WRN] [9] Jellyfin.Plugin.SeamProbe.ContainerReport: SEAM-PROBE result assemblies=2 contract=reachable implementations=1\n' >> "$work/restarted.log"
refuses "a restart whose second answer is the bad one" \
    "Two copies of one contract assembly in one process is the failure this seam exists to avoid" \
    -- "$reader" "$work/restarted.log"

refuses "a log that was never written" \
    "does not exist" \
    -- "$reader" "$work/no-such.log"

printf '\n'
if [ "$failures" -ne 0 ]; then
    echo "$failures of the rules above did not bite." >&2
    exit 1
fi
echo "Every refusal the reader claims was watched refusing, and the answer both lines gave passes."

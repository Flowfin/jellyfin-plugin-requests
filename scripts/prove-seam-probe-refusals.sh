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
# passed if the probe wrote any line at all, so a probe reporting that the seam type was NOT
# reachable was a green job.
#
# THE ONES ADDED WITH THE THIRD OPTION ARE THE CALL. #117 took the handover by name through
# reflection on 2026-08-28, so the member and the want are resolved at runtime by string and a rename
# fails nothing at build time. The probe makes the call a sibling makes and the reader refuses a run
# whose lookup worked and whose call did not - and there are four ways for that to read, so each of
# them is handed over below rather than one of them standing for the set.
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
        printf '[2026-08-29 21:00:00.000 +00:00] [INF] [1] Main: Jellyfin version: 10.11.11\n'
        printf '[2026-08-29 21:00:01.000 +00:00] [WRN] [9] Jellyfin.Plugin.SeamProbe.ContainerReport: SEAM-PROBE assemblies loaded under the name Jellyfin.Plugin.Requests: 1\n'
        printf '[2026-08-29 21:00:01.000 +00:00] [WRN] [9] Jellyfin.Plugin.SeamProbe.ContainerReport: SEAM-PROBE one of them is at /config/plugins/Jellyfin.Plugin.Requests/Jellyfin.Plugin.Requests.dll\n'
        if [ -n "$result" ]; then
            printf '[2026-08-29 21:00:01.000 +00:00] [WRN] [9] Jellyfin.Plugin.SeamProbe.ContainerReport: %s\n' "$result"
        fi
        printf '[2026-08-29 21:00:02.000 +00:00] [INF] [1] Main: Startup complete\n'
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

printf '\n== the answer the shape #117 took has to produce passes\n'
log clean.log 'SEAM-PROBE result assemblies=1 contract=reachable implementations=1 call=answered'
if out=$("$reader" "$work/clean.log" 2>&1); then
    printf '%s\n' "$out" | sed 's/^/  /'
else
    printf '%s\n' "$out" | sed 's/^/  /'
    echo "  REFUSED the answer the tree is built on, which is a reader that refuses everything."
    failures=$((failures + 1))
fi

# The trap this reader exists for. The probe ran, said the type was not reachable, and every line it
# wrote carries the marker the old run grepped for, so this log used to be a green job.
log missing.log 'SEAM-PROBE result assemblies=1 contract=missing implementations=0 call=notattempted'
refuses "the seam type is not reachable, which used to pass" \
    "the seam type is not reachable from a second plugin" \
    -- "$reader" "$work/missing.log"

log two.log 'SEAM-PROBE result assemblies=2 contract=reachable implementations=1 call=answered'
refuses "two assemblies of one name in one process" \
    "Two copies of one assembly in one process is the failure this seam exists to avoid" \
    -- "$reader" "$work/two.log"

log none.log 'SEAM-PROBE result assemblies=0 contract=missing implementations=0 call=notattempted'
refuses "the plugin under test was not loaded at all" \
    "no assembly named Jellyfin.Plugin.Requests was loaded" \
    -- "$reader" "$work/none.log"

log unanswered.log 'SEAM-PROBE result assemblies=1 contract=reachable implementations=0 call=notattempted'
refuses "the container handed back nothing, which is the silence an operator meets" \
    "the container returned no implementation of the seam" \
    -- "$reader" "$work/unanswered.log"

# The four ways the call can fail. They are one refusal in the reader and four different repairs, so
# each of them is handed over rather than one standing in for the set: a run that reads any of these
# as a working seam is a run that measured the lookup and called it the handover.
log nomember.log 'SEAM-PROBE result assemblies=1 contract=reachable implementations=1 call=nomember'
refuses "the member was renamed, which nothing catches at build time" \
    "the lookup worked and the call did not (call=nomember)" \
    -- "$reader" "$work/nomember.log"

log nowant.log 'SEAM-PROBE result assemblies=1 contract=reachable implementations=1 call=nowant'
refuses "the want type or one of its properties was renamed" \
    "the lookup worked and the call did not (call=nowant)" \
    -- "$reader" "$work/nowant.log"

log failed.log 'SEAM-PROBE result assemblies=1 contract=reachable implementations=1 call=failed'
refuses "the call was made and did not come back with an answer" \
    "the lookup worked and the call did not (call=failed)" \
    -- "$reader" "$work/failed.log"

log notattempted.log 'SEAM-PROBE result assemblies=1 contract=reachable implementations=1 call=notattempted'
refuses "the lookup answered and nothing tried to call" \
    "the lookup worked and the call did not (call=notattempted)" \
    -- "$reader" "$work/notattempted.log"

# The probe threw before it reached an answer. It writes a line, and that line carries the marker, so
# only a reader looking for the verdict rather than for the marker refuses this.
log threw.log ''
printf '[2026-08-29 21:00:01.000 +00:00] [WRN] [9] Jellyfin.Plugin.SeamProbe.ContainerReport: SEAM-PROBE the probe itself failed: System.TypeLoadException: no\n' >> "$work/threw.log"
refuses "the probe failed before it had an answer" \
    "the probe wrote no result line, so this run measured nothing" \
    -- "$reader" "$work/threw.log"

log shape.log 'SEAM-PROBE result contract=reachable assemblies=1 implementations=1 call=answered'
refuses "a result line whose fields this reader does not know" \
    "does not have the shape this reader knows" \
    -- "$reader" "$work/shape.log"

# The line the previous reader passed, word for word. It carries no call field at all, so a reader
# that matched the first three fields and stopped would read a run that never called anything as a
# working seam. This is the one-character mistake somebody makes while widening the pattern.
log ofthethreefields.log 'SEAM-PROBE result assemblies=1 contract=reachable implementations=1'
refuses "the result line the reader before this one passed" \
    "does not have the shape this reader knows" \
    -- "$reader" "$work/ofthethreefields.log"

# The near-miss: `head` where the reader has `tail`. The server restarted its hosted services, the
# first answer was the good one and the process that is actually running gave the second.
log restarted.log 'SEAM-PROBE result assemblies=1 contract=reachable implementations=1 call=answered'
printf '[2026-08-29 21:00:30.000 +00:00] [WRN] [9] Jellyfin.Plugin.SeamProbe.ContainerReport: SEAM-PROBE result assemblies=1 contract=reachable implementations=1 call=failed\n' >> "$work/restarted.log"
refuses "a restart whose second answer is the bad one" \
    "the lookup worked and the call did not (call=failed)" \
    -- "$reader" "$work/restarted.log"

refuses "a log that was never written" \
    "does not exist" \
    -- "$reader" "$work/no-such.log"

printf '\n'
if [ "$failures" -ne 0 ]; then
    echo "$failures of the rules above did not bite." >&2
    exit 1
fi
echo "Every refusal the reader claims was watched refusing, and the answer the chosen shape produces passes."

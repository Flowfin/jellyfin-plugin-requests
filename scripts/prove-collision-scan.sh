#!/usr/bin/env bash
# Every collision the scan claims to refuse, refused once, over a fixture written to carry it.
#
# `scripts/sibling-collision-scan.py` is what the interoperability matrix runs, and until this
# existed nothing had ever seen it say no. A scan that has never refused anything is a claim: it
# passes on a clean set, which is also what a scan that does nothing does.
#
# So there is one fixture per collision kind and one clean set beside them. Each fixture is asserted
# to be refused for its own reason, named in its own sentence, and the clean set is asserted to pass.
# A rule that stops matching turns that fixture green and reds this instead.
#
# It needs no container, no server and no network, so it runs in seconds beside the matrix rather
# than inside it.
#
# usage: scripts/prove-collision-scan.sh

set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
scan="$here/sibling-collision-scan.py"
fixtures="$here/fixtures/sibling-collision"

# Each line is a fixture and the sentence its refusal has to carry. The sentence is a fragment
# rather than the whole line, so a wording change that keeps the meaning does not red this, and a
# rule that stops firing does.
cases=$(cat <<'CASES'
route-taken|is served alone and not beside the set
task-key|two scheduled tasks under the key
task-name|two scheduled tasks named
plugin-name|which is one configuration file between them
plugin-id|two plugins under the identifier
not-active|rather than Active with the set installed
configuration-lost|is there alone and gone beside the set
CASES
)

failures=0

run() { # $1 fixture name; prints the scan output, returns its status
    python3 "$scan" \
        "$fixtures/$1/alone" \
        "$fixtures/$1/together" \
        "$fixtures/$1/installed.txt" 2>&1
}

printf '\n== the clean set passes\n'
if out=$(run clean); then
    printf '%s\n' "$out" | sed 's/^/  /'
else
    printf '%s\n' "$out" | sed 's/^/  /'
    echo "REFUSED the clean set, which is a scan that refuses everything."
    failures=$((failures + 1))
fi

while IFS='|' read -r name sentence; do
    [ -n "$name" ] || continue

    printf '\n== %s\n' "$name"

    if out=$(run "$name"); then
        printf '%s\n' "$out" | sed 's/^/  /'
        echo "PASSED, and this fixture exists to be refused."
        failures=$((failures + 1))
        continue
    fi

    printf '%s\n' "$out" | sed 's/^/  /'

    if ! printf '%s' "$out" | grep -qF "$sentence"; then
        echo "REFUSED, but for something other than: $sentence"
        failures=$((failures + 1))
        continue
    fi

    # One reason and not several. A fixture that trips two rules proves neither, because either one
    # of them could stop working and this would stay green.
    reasons=$(printf '%s' "$out" | grep -c '^COLLISION: ' || true)
    if [ "$reasons" != "1" ]; then
        echo "REFUSED for $reasons reasons, and a fixture has to carry exactly one."
        failures=$((failures + 1))
    fi
done <<< "$cases"

printf '\n== verdict\n'
if [ "$failures" != "0" ]; then
    echo "$failures fixture(s) did not do what they exist to do."
    exit 1
fi
echo "the clean set passes and every collision kind was refused for its own reason"

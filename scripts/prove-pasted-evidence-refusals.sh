#!/usr/bin/env bash
# Every refusal the pasted-evidence check claims, watched refusing once, over documents written to
# carry the answer each one names - and the near misses it must not refuse, watched passing.
#
# `scripts/check-pasted-evidence.sh` reads the tree and passes on a tree nobody has broken yet. A
# reader in that position is indistinguishable from one that says yes to everything, for as long as
# nothing goes wrong, and the thing it guards against - a line number drifting under a document -
# happens quietly and by accident rather than on demand. So each answer is handed to it here.
#
# THE NEAR MISSES ARE HALF OF THIS FILE AND THEY ARE THE EXPENSIVE HALF. A check over prose that
# refuses too much is worse than none: it makes ordinary writing red, somebody turns it off, and the
# defect it was built for comes back with the argument that the check cried wolf. The three below are
# the shapes this repository's documents already contain - a timestamp in a run listing, another
# repository's path behind a ref, a sentence in prose that carries a colon and a number.
#
# The fixtures name files this repository really has, because the check resolves what it reads
# against the tree and a fixture pointing at nothing would prove only that nothing resolves.
#
# usage: scripts/prove-pasted-evidence-refusals.sh

set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/.." && pwd)
checker="$here/check-pasted-evidence.sh"

cd "$root"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# A real line of a real tracked file, taken from the tree rather than typed, so this file does not
# become the next thing that goes stale.
subject="scripts/check-pasted-evidence.sh"
subject_line=1
subject_text=$(sed -n "${subject_line}p" "$subject" | sed 's/\r$//')

# usage: doc <name> <line...>
doc() {
    local name=$1
    shift
    printf '%s\n' "# A fixture" '' 'Evidence:' '' "$@" '' 'End.' > "$work/$name"
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

# usage: accepts <heading> <sentence the pass must carry> -- <command...>
accepts() {
    local heading=$1 sentence=$2
    shift 3
    printf '\n== %s\n' "$heading"
    local out status
    set +e
    out=$("$@" 2>&1)
    status=$?
    set -e
    printf '%s\n' "$out" | sed 's/^/  /'
    if [ "$status" -ne 0 ]; then
        echo "  REFUSED it, which is a reader that refuses everything."
        failures=$((failures + 1))
        return
    fi
    case "$out" in
        *"$sentence"*) ;;
        *)
            echo "  accepted, but said nothing about: $sentence"
            failures=$((failures + 1))
            ;;
    esac
}

# The state a document is in when nobody has broken it.
doc good.md "    ${subject}:${subject_line}:${subject_text}"
accepts "a paste the file still produces" \
    "every pasted match line reproduces" \
    -- "$checker" "$work/good.md"

# THE ONE IT EXISTS FOR. The assertion is still true - that file does carry that line - and the
# coordinate beside it has moved, which is what happens every time anything above it is edited.
doc drifted.md "    ${subject}:$((subject_line + 3)):${subject_text}"
refuses "the number moved under the text, which is the whole defect" \
    "and that line reads" \
    -- "$checker" "$work/drifted.md"

doc rewritten.md "    ${subject}:${subject_line}:#!/usr/bin/env fish"
refuses "the text at that number is not the text pasted" \
    "and that line reads" \
    -- "$checker" "$work/rewritten.md"

doc beyond.md "    ${subject}:999999:${subject_text}"
refuses "the file has no such line at all" \
    "has no line 999999" \
    -- "$checker" "$work/beyond.md"

doc moved.md "    scripts/check-pasted-evidence-that-went-away.sh:1:#!/usr/bin/env bash"
refuses "the file the paste names has moved or gone" \
    "this repository has no" \
    -- "$checker" "$work/moved.md"

# The near misses. Each is a shape a document here already carries, and refusing any of them would
# make ordinary writing red.

doc timestamp.md "    33246667212	push	success	2026-08-29T09:57:04Z"
accepts "a run listing carrying a timestamp is not a paste" \
    "read 0 pasted match lines" \
    -- "$checker" "$work/timestamp.md"

doc foreign.md "    origin/release-10.11.z:MediaBrowser/Session/ISessionManager.cs:1:namespace Whatever;"
accepts "another repository's path behind a ref names a tree this check does not have" \
    "read 0 pasted match lines" \
    -- "$checker" "$work/foreign.md"

doc prose.md "The file ${subject}:${subject_line}:${subject_text} is discussed here in prose."
accepts "a sentence in prose is not an evidence block, whatever colons it carries" \
    "read 0 pasted match lines" \
    -- "$checker" "$work/prose.md"

doc json.md '    {"Id":"00000000-0000-0000-0000-000000000001","RequestedAt":"2026-03-01T12:00:00Z"}'
accepts "a JSON document pasted as an example is not a paste of this tree" \
    "read 0 pasted match lines" \
    -- "$checker" "$work/json.md"

refuses "a document handed to it that does not exist" \
    "does not exist" \
    -- "$checker" "$work/no-such-document.md"

printf '\n'
if [ "$failures" -ne 0 ]; then
    echo "$failures of the rules above did not bite." >&2
    exit 1
fi
echo "Every refusal the pasted-evidence check claims was watched refusing, and every near miss it must not refuse was watched passing."

#!/usr/bin/env bash
# Every refusal the negative-disclosure reader claims, watched refusing once, and the two properties
# that make running text out of the tree defensible, watched holding.
#
# `scripts/check-negative-disclosures.sh` re-runs the `git grep ... ; echo "exit=$?"` blocks the
# documents use to assert that something is NOT here. It passes on a tree nobody has broken, which is
# the output a reader that says yes to everything also produces, and the defect it exists for - a
# negative that quietly stopped being true - is one nobody can produce on demand. So every answer is
# handed to it here.
#
# TWO OF THE CASES BELOW ARE ABOUT WHAT IT REFUSES TO RUN, NOT ABOUT WHAT IT DECIDES. A check that
# executes text out of the tree earns that by being narrow, and narrow is only worth the word if
# somebody watched it refuse the wide thing. Both write a sentinel file if a shell ever sees them, and
# both are asserted to have written nothing.
#
# usage: scripts/prove-negative-disclosure-refusals.sh

set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/.." && pwd)
checker="$here/check-negative-disclosures.sh"

cd "$root"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

sentinel="$work/a-shell-ran-this"

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

# A string this repository holds and one it does not, both asserted here rather than believed, so a
# fixture that has quietly stopped being a fixture fails as itself instead of as the rule it feeds.
#
# `git grep` searches what is tracked, so a string that is only in the working tree is absent to it.
# That is a real distinction and it cost a fixture once: this file named its own checker, which was
# not staged yet, and the broken-negative case passed because the checker was correct about a file
# git could not see.
present='usr/bin/env bash'

# The absent one is built from two halves so that naming it here does not put it in the tree. Written
# whole it would be tracked the moment this file is, which is how it broke: it passed on the machine
# it was written on, where this file was not staged yet, and failed on the runner, where it was.
absent_head='NoSuchSymbolIsIn'
absent_tail='ThisTreeAnywhereAtAll'
absent="${absent_head}${absent_tail}"

if ! git grep -q -- "$present" -- scripts/; then
    echo "the fixture is wrong: scripts/ no longer holds ${present}, so nothing here proves a present string is found." >&2
    exit 2
fi
if git grep -q -- "$absent" -- scripts/; then
    echo "the fixture is wrong: scripts/ now holds ${absent}, so nothing here proves an absent string is missed." >&2
    exit 2
fi

# The state a document is in when nobody has broken it: a negative that is still true.
doc true-negative.md "    git grep -n '${absent}' -- scripts/ ; echo \"exit=\$?\"" '    exit=1'
accepts "a negative the tree still bears out" \
    "every negative disclosure exits as the document says it does" \
    -- "$checker" "$work/true-negative.md"

# THE ONE IT EXISTS FOR, and it is #350 in miniature: the page says the thing is not here, the thing
# is here, and nothing about the page looks wrong.
doc broken-negative.md "    git grep -n '${present}' -- scripts/ ; echo \"exit=\$?\"" '    exit=1'
refuses "the thing the page says is absent is present" \
    "and it exits 0" \
    -- "$checker" "$work/broken-negative.md"

doc broken-positive.md "    git grep -n '${absent}' -- scripts/ ; echo \"exit=\$?\"" '    exit=0'
refuses "the page claims a match and there is none" \
    "and it exits 1" \
    -- "$checker" "$work/broken-positive.md"

doc no-code.md "    git grep -n '${absent}' -- scripts/ ; echo \"exit=\$?\""
refuses "the block asks for an exit code and pastes none" \
    "pastes none" \
    -- "$checker" "$work/no-code.md"

# What it will not run, part one: a redirection. If a shell ever sees this line it creates the
# sentinel, and the assertion after these two cases is that it never did.
doc redirection.md "    git grep -n foo -- scripts/ > ${sentinel} ; echo \"exit=\$?\"" '    exit=1'
refuses "a redirection makes the command unreadable rather than skippable" \
    "means something to a shell outside a quoted run" \
    -- "$checker" "$work/redirection.md"

# What it will not run, part two: the one flag of this verb that executes a command of the caller's
# choosing. It survives the character scan, because every character in it is ordinary, and the option
# set is what stops it.
doc pager.md "    git grep -O'touch ${sentinel}' -n foo -- scripts/ ; echo \"exit=\$?\"" '    exit=1'
refuses "git grep -O runs a pager of the caller's choosing and is not in the option set" \
    "is not among the options it will pass to git grep" \
    -- "$checker" "$work/pager.md"

printf '\n== neither of those two reached a shell\n'
if [ -e "$sentinel" ]; then
    echo "  THE SENTINEL EXISTS. Something ran a command this check says it refuses to run."
    failures=$((failures + 1))
else
    echo "  ${sentinel} does not exist, so neither line was executed."
fi

doc not-git.md "    ls -l /tmp ; echo \"exit=\$?\"" '    exit=0'
refuses "a command that is not git grep" \
    "runs git grep and nothing else" \
    -- "$checker" "$work/not-git.md"

# The near miss. A document with no such block at all passes and says how many it ran, so a run that
# read nothing cannot be read as one that read everything and found nothing.
doc none.md "    Jellyfin.Plugin.Requests/Model/RequestState.cs:22:    Open = 0,"
accepts "a document carrying no negative disclosure says it ran none" \
    "ran 0 negative disclosure(s)" \
    -- "$checker" "$work/none.md"

refuses "a document handed to it that does not exist" \
    "does not exist" \
    -- "$checker" "$work/no-such-document.md"

printf '\n'
if [ "$failures" -ne 0 ]; then
    echo "$failures of the rules above did not bite." >&2
    exit 1
fi
echo "Every refusal the negative-disclosure check claims was watched refusing, and nothing it declines to run reached a shell."

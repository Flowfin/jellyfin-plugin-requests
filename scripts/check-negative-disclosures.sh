#!/usr/bin/env bash
# Re-run the `git grep ... ; echo "exit=$?"` blocks this repository's documents use to assert that
# something is NOT in the tree, and refuse one whose pasted exit code the command does not produce.
#
# WHY THIS EXISTS RATHER THAN BEING COVERED BY THE READER BESIDE IT.
# `scripts/check-pasted-evidence.sh` resolves a pasted `path:line:text` line against the file it
# names. A negative disclosure has no such line - that is the whole point of it - so it pastes an exit
# code and nothing else, and there is nothing in it for that reader to resolve. So the claims this
# board leans on hardest were the ones nothing read: no socket, no permission call, no reach from the
# sweep into the activity log.
#
# WHAT IT MEASURED WHEN IT LANDED. `SECURITY.md` said the plugin makes no outbound call at all and
# pasted a command exiting 1. The command exited 0 and had done since 2026-08-16, through two edits of
# that page. That is #350, and it is why a negative disclosure is not something to take on trust for
# fifteen days at a time.
#
# IT RUNS WHAT THE DOCUMENT SAYS, WHICH IS THE PART TO READ BEFORE TRUSTING IT.
# Executing text out of the tree is a thing to do narrowly or not at all, so:
#
#   - The command is never handed to a shell as a string. It is scanned character by character, and a
#     character that would mean anything to a shell outside a quoted run - a pipe, a redirection, a
#     substitution, a backslash, a brace, a glob - makes the block UNREADABLE, which is a refusal
#     rather than a skip. Inside double quotes the three expansion characters are refused too.
#   - Only then is it split into words, and the first two must be `git grep`.
#   - Every option is checked against a fixed set. `git grep -O` runs a pager of the caller's
#     choosing, which is the one flag of this verb that executes anything, and it is not in the set.
#   - Nothing is written. `git grep` reads.
#
# A block this will not run is reported and fails the run. A check that quietly passed over the
# commands it could not read would be at its most silent exactly where a document had been written to
# evade it.
#
# WHAT IT DOES NOT DO. It compares the exit code and never the match lines, because the lines are the
# other reader's subject and two readers refusing one thing is two places to fix it. It reads only the
# `; echo "exit=$?"` form; a negative stated in prose is a judgement about meaning that no reading of
# this tree makes.
#
# usage: scripts/check-negative-disclosures.sh [markdown-file...]
#   with no argument it reads every tracked markdown file.

set -uo pipefail

cd "$(git rev-parse --show-toplevel)"

if [ "$#" -gt 0 ]; then
    docs=("$@")
else
    mapfile -t docs < <(git ls-files -- '*.md')
fi

if [ "${#docs[@]}" -eq 0 ]; then
    echo "check-negative-disclosures: no markdown file to read. A run with nothing to check is not a run that found nothing." >&2
    exit 2
fi

tail_marker='; echo "exit=$?"'

# The letters a bundled short option may carry. Every one of them only selects what is reported.
short_flags='nclLiwEFvhH'

# The long options this will run, named one by one rather than by a pattern, because a pattern admits
# whatever git grows next and the point of the set is that it does not.
long_flags=' --line-number --count --files-with-matches --name-only --files-without-match --ignore-case --word-regexp --extended-regexp --fixed-strings --invert-match --no-color --text --untracked --cached --no-index --full-name '

# safe_to_split <command>
# Refuses any character that would mean something to a shell outside a quoted run, and the three
# expansion characters inside double quotes. Returns 0 when the string can be split by word rules
# alone, with no expansion of any kind left in it.
safe_to_split() {
    local s=$1 i c state=plain
    for ((i = 0; i < ${#s}; i++)); do
        c=${s:i:1}
        case "$state" in
            plain)
                case "$c" in
                    "'") state=single ;;
                    '"') state=double ;;
                    [A-Za-z0-9_./=:@%,+-] | ' ') ;;
                    *) return 1 ;;
                esac
                ;;
            single)
                [ "$c" = "'" ] && state=plain
                ;;
            double)
                case "$c" in
                    '"') state=plain ;;
                    '$' | '`' | '\') return 1 ;;
                esac
                ;;
        esac
    done
    [ "$state" = plain ]
}

findings=0
blocks=0

for doc in "${docs[@]}"; do
    if [ ! -f "$doc" ]; then
        echo "check-negative-disclosures: ${doc} does not exist." >&2
        exit 2
    fi

    mapfile -t lines < "$doc"
    n=${#lines[@]}

    for ((k = 0; k < n; k++)); do
        raw=${lines[k]%$'\r'}
        case "$raw" in
            "    "*) body=${raw#    } ;;
            *) continue ;;
        esac
        body=${body#'$ '}
        case "$body" in
            *"$tail_marker") ;;
            *) continue ;;
        esac

        blocks=$((blocks + 1))
        docline=$((k + 1))
        cmd=${body%"$tail_marker"}
        cmd=${cmd%"${cmd##*[![:space:]]}"}

        # The pasted code, taken from the rest of this block rather than from the next line alone: a
        # command that matched pastes its matches first and the code last.
        pasted=
        for ((j = k + 1; j < n; j++)); do
            nxt=${lines[j]%$'\r'}
            case "$nxt" in
                "    "*) ;;
                *) break ;;
            esac
            [ -z "${nxt//[[:space:]]/}" ] && break
            case "${nxt#    }" in
                exit=[0-9]*) pasted=${nxt#    exit=} ;;
            esac
        done

        if [ -z "$pasted" ]; then
            findings=$((findings + 1))
            echo "${doc}:${docline}: asks for an exit code and pastes none."
            continue
        fi

        if ! safe_to_split "$cmd"; then
            findings=$((findings + 1))
            echo "${doc}:${docline}: this check will not run it, so it cannot say the claim holds:"
            echo "    ${cmd}"
            echo "  It carries a character that means something to a shell outside a quoted run."
            continue
        fi

        eval "argv=($cmd)"
        if [ "${argv[0]-}" != "git" ] || [ "${argv[1]-}" != "grep" ]; then
            findings=$((findings + 1))
            echo "${doc}:${docline}: this check runs git grep and nothing else, and this is not one:"
            echo "    ${cmd}"
            continue
        fi

        refused_flag=
        for ((a = 2; a < ${#argv[@]}; a++)); do
            tok=${argv[a]}
            [ "$tok" = "--" ] && break
            case "$tok" in
                --*)
                    case "$long_flags" in
                        *" $tok "*) ;;
                        *) refused_flag=$tok ;;
                    esac
                    ;;
                -?*)
                    rest=${tok#-}
                    [ -n "${rest//[$short_flags]/}" ] && refused_flag=$tok
                    ;;
                *) ;;
            esac
            [ -n "$refused_flag" ] && break
        done

        if [ -n "$refused_flag" ]; then
            findings=$((findings + 1))
            echo "${doc}:${docline}: this check will not run it, so it cannot say the claim holds:"
            echo "    ${cmd}"
            echo "  ${refused_flag} is not among the options it will pass to git grep."
            continue
        fi

        "${argv[@]}" > /dev/null 2>&1
        actual=$?

        if [ "$actual" != "$pasted" ]; then
            findings=$((findings + 1))
            echo "${doc}:${docline}: pastes exit=${pasted} under"
            echo "    ${cmd}"
            echo "  and it exits ${actual}."
        fi
    done
done

echo "ran ${blocks} negative disclosure(s) in ${#docs[@]} document(s)"

if [ "$findings" -gt 0 ]; then
    echo "check-negative-disclosures: ${findings} disclosure(s) the tree does not bear out. A claim that something is not here is worth exactly what re-running it says." >&2
    exit 1
fi

echo "check-negative-disclosures: every negative disclosure exits as the document says it does."

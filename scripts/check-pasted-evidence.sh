#!/usr/bin/env bash
# Read every `path:line:text` line this repository's documents paste as evidence, and refuse one that
# the file it names no longer produces.
#
# WHY THIS EXISTS. The documents here argue from commands, and a `git grep -n` paste is the commonest
# form that argument takes. The text in such a paste is the assertion - this file carries this field,
# this call, this rule - and the line number beside it is a coordinate that moves whenever anything
# above it is edited. So the assertion stays true while the evidence for it stops reproducing, and a
# reader who runs the command gets output that does not match the page. Nothing about that failure is
# visible: the build is green, the suite is green, and the document reads exactly as it did.
#
# WHAT IT MEASURED WHEN IT LANDED. Thirty of seventy-nine pasted match lines across five documents,
# which is #348. #340 was the same defect one register over - a release position stated in prose in
# five files that had stopped reproducing - and it was repaired by hand because nothing here reads a
# document against the tree. This is what reads it.
#
# WHAT IT READS. A line that begins a four-space indented block line, splits as `path:number:text`,
# and whose path is a file this repository tracks. That is the shape `git grep -n` prints and the
# shape every evidence block in these documents uses.
#
# WHAT IT CANNOT READ, said plainly rather than left to be discovered:
#
#   - A paste qualified by a ref, `<ref>:<path>:<line>:<text>`. Three documents here paste from
#     Jellyfin's own repository at a ref, which names a tree this check does not have and must not
#     guess at, so no ref-qualified paste is read at all - including one naming this tree.
#   - A paste inside a fenced block. No tracked document uses one for evidence today; every evidence
#     block here is indented.
#   - The context lines `git grep -A` and `-B` print, which carry a dash where a match carries a
#     colon.
#   - Whether the command written above a block is the command that produced it. The paste is checked
#     against the file, never against the command, so a block whose command was edited afterwards
#     passes here and is the reviewer's to catch.
#
# usage: scripts/check-pasted-evidence.sh [markdown-file...]
#   with no argument it reads every tracked markdown file.

set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

if [ "$#" -gt 0 ]; then
    docs=("$@")
else
    mapfile -t docs < <(git ls-files -- '*.md')
fi

if [ "${#docs[@]}" -eq 0 ]; then
    echo "check-pasted-evidence: no markdown file to read. A run with nothing to check is not a run that found nothing." >&2
    exit 2
fi

# What decides whether a `path:line:text` line is evidence about this tree, or an ordinary colon in
# somebody's pasted output, is the first field naming a file that is here. The test is the file
# system rather than the index, so a document and the file it cites can arrive in one change without
# the check refusing the document until somebody stages the file.
#
# The first segment is read out of the index, because that is what separates a path this tree could
# have from a timestamp or a JSON key. A tracked top level with nothing behind it is a paste naming a
# file that has moved or gone, which is a finding rather than something to pass over.
declare -A toplevel=()
while IFS= read -r f; do
    toplevel["${f%%/*}"]=1
done < <(git ls-files)

# Each named file is read once and held by line, rather than a process spawned per pasted line. A
# document citing one file forty times is the normal case here, and the slow shape is one somebody
# would rather not run.
declare -A line=()
declare -A length=()
declare -A loaded=()
hold() {
    local p=$1 n=0 l
    [ -n "${loaded[$p]+x}" ] && return
    while IFS= read -r l || [ -n "$l" ]; do
        n=$((n + 1))
        line["$p:$n"]=${l%$'\r'}
    done < "$p"
    length["$p"]=$n
    loaded["$p"]=1
}

findings=0
read_lines=0

for doc in "${docs[@]}"; do
    if [ ! -f "$doc" ]; then
        echo "check-pasted-evidence: ${doc} does not exist." >&2
        exit 2
    fi

    docline=0
    while IFS= read -r raw || [ -n "$raw" ]; do
        docline=$((docline + 1))
        raw=${raw%$'\r'}

        # Evidence is indented. A prose sentence carrying a colon and a number is not a paste, and
        # reading one as a paste would make this check refuse ordinary writing.
        case "$raw" in
            "    "*) body=${raw#    } ;;
            *) continue ;;
        esac

        path=${body%%:*}
        [ "$path" = "$body" ] && continue
        rest=${body#*:}
        num=${rest%%:*}
        [ "$num" = "$rest" ] && continue
        case "$num" in
            '' | *[!0-9]*) continue ;;
        esac
        text=${rest#*:}

        if [ ! -f "$path" ]; then
            # Anything under no tracked top level - a timestamp, a JSON document, another
            # repository's path behind a ref - is not about this tree and is not this check's.
            if [ -n "${toplevel[${path%%/*}]+x}" ]; then
                findings=$((findings + 1))
                echo "${doc}:${docline}: pastes ${path}:${num}, and this repository has no ${path}."
            fi
            continue
        fi

        read_lines=$((read_lines + 1))

        hold "$path"
        actual=${line["$path:$num"]-}

        if [ "$actual" != "$text" ]; then
            findings=$((findings + 1))
            echo "${doc}:${docline}: pastes ${path}:${num} as"
            echo "    ${text}"
            if [ "$num" -gt "${length[$path]}" ]; then
                echo "  and that file has no line ${num}."
            else
                echo "  and that line reads"
                echo "    ${actual}"
            fi
        fi
    done < "$doc"
done

echo "read ${read_lines} pasted match lines in ${#docs[@]} document(s)"

if [ "$findings" -gt 0 ]; then
    echo "check-pasted-evidence: ${findings} pasted line(s) the tree does not produce. Re-run the command above each block and paste what it prints; do not edit the number to fit." >&2
    exit 1
fi

echo "check-pasted-evidence: every pasted match line reproduces against the file it names."

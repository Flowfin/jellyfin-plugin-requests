#!/usr/bin/env bash
# Does this head change nothing but markdown?
#
# WHY A READER EXISTS RATHER THAN A `paths-ignore`. A workflow that declines a head produces no
# check run for it, and a required context that never reports leaves a pull request pending rather
# than failing it - no red check to point at, and nothing on the page that looks wrong to the person
# waiting. `.github/workflows/build.yaml` carries the same trap at its own `pull_request` trigger and
# names it there. The ABI floor build and the code scan were the two workflows on this board still
# declining a markdown-only head, which is why six of the fifteen contexts `docs/quality-parity.md`
# declares could not be required. They report on every head now and ask this reader whether there is
# anything to do, so the context costs seconds where nothing changed and full price where it did.
#
# ONE HOME FOR THE RULE. Two workflows ask the question and neither carries a copy of it. A second
# copy is a second rule the moment either one is edited, and the answer decides whether an ABI floor
# build and a full code scan happen at all.
#
# IT FAILS TOWARD DOING THE WORK. Every case it cannot resolve - a missing reference, an argument it
# was not given, a comparison that returns nothing - answers `false`, which spends runtime. The
# opposite default answers `true` on a head nobody compared, and a scan skipped on a head that
# changed code is a scan nobody notices is missing.
#
# WHAT `.md` MEANS HERE IS THE SUFFIX AND NOTHING ELSE, matched case-sensitively, which is what the
# `**/*.md` filter it replaces matched. `README.MD` and `notes.md.cs` are both code as far as this
# reader is concerned, and both answers are the safe direction rather than an accident.
#
# THE COMPARISON IS THREE-DOT, so it is the merge base of the two references against the head. On a
# pull request that is the change the head proposes rather than everything the base branch has done
# since. On a push whose `before` is an ancestor of its `after` the merge base is `before`, so the
# two readings agree; where a rewrite makes it something older the comparison widens, which is the
# direction that spends runtime rather than the one that skips work.
#
# usage: scripts/head-changes-only-documentation.sh <base-ref> <head-ref>
#   prints `true` or `false` on standard output and exits 0. The reason for a `false` goes to
#   standard error, so a run log says which case it met.

set -euo pipefail

base=${1-}
head=${2-}

answer() {
    printf '%s\n' "$1"
    exit 0
}

decline() {
    printf 'head-changes-only-documentation: %s Building.\n' "$1" >&2
    answer false
}

zero='0000000000000000000000000000000000000000'

if [ -z "$base" ] || [ -z "$head" ]; then
    decline "no pair of references to compare was given, so nothing says what this head changed."
fi

if [ "$base" = "$zero" ] || [ "$head" = "$zero" ]; then
    decline "one side of the comparison is the empty reference, which is a branch with no before."
fi

for ref in "$base" "$head"; do
    if ! git rev-parse --verify --quiet "${ref}^{commit}" >/dev/null; then
        decline "${ref} is not a commit this clone holds, so no comparison can be made against it."
    fi
done

if ! git merge-base "$base" "$head" >/dev/null 2>&1; then
    decline "${base} and ${head} share no history, so there is no change between them to read."
fi

# -z rather than the default listing: a path with a space, a quote or a byte outside ASCII is quoted
# and escaped by the default output, and a reader that unquoted it would be a second parser nobody
# asked for. It goes to a file rather than into a variable, because a command substitution cannot
# hold the separator this asked for.
changed=$(mktemp)
trap 'rm -f "$changed"' EXIT
git diff --name-only -z "${base}...${head}" > "$changed"

if [ ! -s "$changed" ]; then
    decline "${base}...${head} changes no file at all, which is not a head anybody wrote documentation on."
fi

while IFS= read -r -d '' path; do
    case "$path" in
        *.md) ;;
        *) decline "${path} is not markdown." ;;
    esac
done < "$changed"

answer true

#!/usr/bin/env bash
# Every answer the documentation-only reader gives, watched being given once, over a repository
# written to carry the case it names.
#
# `scripts/head-changes-only-documentation.sh` decides whether an ABI floor build and a full code
# scan happen at all. On an ordinary head it answers `false` and everything runs, which is what the
# two workflows did before it existed - so a reader that had never answered `true` would look exactly
# like one that works, for as long as nobody pushed documentation. And a reader that answered `true`
# too readily skips a scan on a head that changed code, which is the failure nobody sees at all.
#
# It takes two references and reads them with git, so every case below is a repository built here
# rather than a head anybody pushed. No network, no runner and no pull request.
#
# THE ONE IT EXISTS FOR IS THE MERGE BASE, and it is the last case below. A documentation branch cut
# from a master that has since taken a code change is the ordinary shape of a slow-moving branch. A
# two-dot comparison of that pair reports the code the branch does not have as a change the branch
# makes, so the reader answers `false` and the head pays for a build it did not ask for; a three-dot
# comparison reports what the branch actually changed. Both readings are green on a branch nobody
# waited on, which is why this case is written down rather than trusted.
#
# usage: scripts/prove-documentation-only-decisions.sh

set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
reader="$here/head-changes-only-documentation.sh"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

repo="$work/repo"
mkdir -p "$repo"
cd "$repo"
git init --quiet .
# The identity and the signing setting are this throwaway repository's own. Nothing here is pushed
# anywhere and no commit below leaves this directory; what is being built is an input to a reader,
# in the same sense as the JSON documents the manifest proofs write.
git config user.email "proof@example.invalid"
git config user.name "Documentation only proof"
git config commit.gpgsign false
# The line endings this fixture writes are its own too. A clone configured to translate them would
# otherwise decide what these commits hold, and what is being proved is a reader of paths.
git config core.autocrlf false

# usage: commit_with <message> -- then the files already written
commit_with() {
    git add -A .
    git commit --quiet --allow-empty -m "$1"
    git rev-parse HEAD
}

write() {
    mkdir -p "$(dirname "$1")"
    printf '%s\n' "$2" > "$1"
}

failures=0

# usage: answers <expected> <heading> <base> <head>
answers() {
    local expected=$1 heading=$2 base=${3-} head=${4-}
    printf '\n== %s\n' "$heading"
    local out err status
    err="$work/stderr"
    set +e
    out=$("$reader" "$base" "$head" 2>"$err")
    status=$?
    set -e
    [ -s "$err" ] && sed 's/^/  /' "$err"
    printf '  %s -> %s\n' "$expected" "$out"
    if [ "$status" -ne 0 ]; then
        echo "  EXITED $status, and this reader answers on standard output rather than through a status."
        failures=$((failures + 1))
        return
    fi
    if [ "$out" != "$expected" ]; then
        echo "  ANSWERED $out where $expected is the whole point of this case."
        failures=$((failures + 1))
    fi
}

write README.md "the first line"
write Jellyfin.Plugin.Requests/Plugin.cs "namespace X;"
base=$(commit_with "a tree with a document and a source file")

write README.md "a second line"
write docs/operating.md "a page"
only_markdown=$(commit_with "two markdown files and nothing else")

git checkout --quiet -b mixed "$base"
write docs/operating.md "a page"
write Jellyfin.Plugin.Requests/Plugin.cs "namespace X; // and a change"
mixed=$(commit_with "a markdown file beside a source file")

git checkout --quiet -b code "$base"
write Jellyfin.Plugin.Requests/Plugin.cs "namespace X; // moved"
code=$(commit_with "a source file and no markdown")

git checkout --quiet -b renamed "$base"
git mv README.md README.cs
renamed=$(commit_with "a markdown file that stopped being markdown")

git checkout --quiet -b shouting "$base"
# A path of its own rather than README.md in the other case: on a filesystem that ignores case the
# two are one file, and the case this proves would quietly become the first case again.
write docs/SHOUTING.MD "a document whose suffix is not the one the filter matched"
shouting=$(commit_with "a suffix in the other case")

git checkout --quiet -b lookalike "$base"
write notes.md.cs "not a document"
lookalike=$(commit_with "markdown in the middle of a name rather than at the end")

git checkout --quiet -b spaced "$base"
write "docs/a page with spaces.md" "a page"
spaced=$(commit_with "a path a default listing would quote")

git checkout --quiet -b removed "$base"
git rm --quiet README.md
removed=$(commit_with "a document deleted rather than written")

git checkout --quiet --orphan stranger
git rm --quiet -rf . >/dev/null 2>&1 || true
write OTHER.md "a tree with no shared history"
stranger=$(commit_with "an unrelated history")

# The case this file exists for. Master takes a source change after the documentation branch is cut,
# and the branch is compared against master's tip.
git checkout --quiet -b moved-on "$base"
write Jellyfin.Plugin.Requests/Plugin.cs "namespace X; // master moved on"
moved_on=$(commit_with "a source change the documentation branch does not have")
git checkout --quiet -b slow-document "$base"
write docs/catalogue.md "a page written while master moved on"
slow_document=$(commit_with "documentation cut before that source change")

answers true  "only markdown changed"                                   "$base" "$only_markdown"
answers false "markdown beside a source file"                           "$base" "$mixed"
answers false "a source file and no markdown"                           "$base" "$code"
answers false "a markdown file renamed to a source file"                "$base" "$renamed"
answers false "a suffix in the other case"                              "$base" "$shouting"
answers false "markdown in the middle of a name"                        "$base" "$lookalike"
answers true  "a path a default listing would quote"                    "$base" "$spaced"
answers true  "a document deleted rather than written"                  "$base" "$removed"
answers false "no references at all"
answers false "the empty reference on one side"                         "0000000000000000000000000000000000000000" "$only_markdown"
answers false "a base this clone does not hold"                         "1111111111111111111111111111111111111111" "$only_markdown"
answers false "the same commit on both sides"                           "$base" "$base"
answers false "two histories that share nothing"                        "$stranger" "$only_markdown"
answers true  "a documentation branch whose base moved on underneath"   "$moved_on" "$slow_document"

printf '\n'
if [ "$failures" -ne 0 ]; then
    echo "$failures of the cases above did not get the answer they are written for."
    exit 1
fi
echo "Every case above got the answer it is written for."

#!/usr/bin/env bash
# The server image for one target framework, read out of scripts/server-lines.tsv.
#
# Every check that starts a container needs the pair, and the checks arrive at it from different
# directions: the activity check is handed a line, the package check is handed the framework the
# packaging metadata declares. So the pair lives in one file and this is how it is asked for.
#
# An unknown framework is refused rather than answered with an empty string, because an empty image
# reaches `docker run` as a missing argument and fails several steps later with an error about
# something else.
#
# usage: scripts/server-image-for.sh net9.0
set -euo pipefail

framework=${1:?target framework, for example net9.0}
table="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/server-lines.tsv"

image=$(awk -F '\t' -v want="$framework" '$1 == want { print $2; found = 1 } END { exit !found }' "$table") || {
    echo "no server image for $framework in $table. The claimed lines are:" >&2
    grep -v '^#' "$table" >&2
    exit 1
}

printf '%s\n' "$image"

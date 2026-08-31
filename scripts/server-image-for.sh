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
# WHICH SERVER OF THE LINE IS THE SECOND ARGUMENT. `line`, the default, is a server of that line and
# is what every caller wanted before this argument existed. `floor` is the oldest server the line's
# claimed `targetAbi` names, which is a different question and the one the released-package check
# asks: an install that works only on the newest server of a line says nothing about the oldest one
# the packaging metadata claims.
#
# `floor` IS THE ONE ANSWER THAT MAY LEGALLY BE EMPTY, and only because the table declares it. A dash
# in the third column is a written statement that the line's floor has no published server image, so
# an empty answer here is a fact somebody reviewed rather than a lookup that went wrong, and a caller
# that reads it installs nothing rather than installing on the wrong server.
#
# usage: scripts/server-image-for.sh net9.0 [line|floor]
set -euo pipefail

framework=${1:?target framework, for example net9.0}
which=${2:-line}
table="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/server-lines.tsv"

case "$which" in
    line) column=2 ;;
    floor) column=3 ;;
    *)
        echo "server-image-for: $which is not a server this table holds. It is 'line' or 'floor'." >&2
        exit 1
        ;;
esac

image=$(awk -F '\t' -v want="$framework" -v column="$column" '$1 == want { print $column; found = 1 } END { exit !found }' "$table") || {
    echo "no server image for $framework in $table. The claimed lines are:" >&2
    grep -v '^#' "$table" >&2
    exit 1
}

if [ "$which" = floor ] && [ "$image" = "-" ]; then
    # Declared absent rather than missing. Printing nothing and succeeding is what lets the caller
    # say so on its own output instead of failing for a server nobody has published.
    exit 0
fi

if [ -z "$image" ]; then
    echo "no $which server image for $framework in $table, and no dash declaring there is none. A row that answers neither is a row nobody finished." >&2
    exit 1
fi

printf '%s\n' "$image"

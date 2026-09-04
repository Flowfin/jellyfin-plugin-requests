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
# A SECOND ARGUMENT ASKS FOR ANOTHER SERVER OF THE SAME LINE. The table names one server per line,
# and that one is the newest the line has published. A caller that has to install at the floor a
# packaging file claims needs a different server of the same line, and the only part of the answer
# that changes is the tag. So the registry repository stays where it is declared and the caller
# supplies the version, rather than a second table holding the same registry name a second time and
# disagreeing with this one the first time the line moves registries.
#
# The version is refused rather than pasted into the image unread. A tag carrying a slash or a colon
# reaches `docker run` as an image somebody else publishes, and an empty one reaches it as the
# repository with no tag at all, which is `latest` and is a server of no particular version.
#
# usage: scripts/server-image-for.sh net9.0 [server-version]
#   scripts/server-image-for.sh net9.0           the newest server of that line
#   scripts/server-image-for.sh net9.0 10.11.0   the same line at the version named
set -euo pipefail

framework=${1:?target framework, for example net9.0}
version=${2:-}
table="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/server-lines.tsv"

image=$(awk -F '\t' -v want="$framework" '$1 == want { print $2; found = 1 } END { exit !found }' "$table") || {
    echo "no server image for $framework in $table. The claimed lines are:" >&2
    grep -v '^#' "$table" >&2
    exit 1
}

if [ -n "$version" ]; then
    case "$version" in
        *[!A-Za-z0-9._-]*)
            echo "server-image-for: '$version' is not a tag. A server version names one tag of the image $framework runs on, and anything carrying a slash or a colon names some other image entirely." >&2
            exit 1
            ;;
    esac
    case "$image" in
        *:*) ;;
        *)
            echo "server-image-for: the image for $framework in $table is '$image' and carries no tag, so there is nothing here to replace with $version." >&2
            exit 1
            ;;
    esac
    image="${image%%:*}:${version}"
fi

printf '%s\n' "$image"

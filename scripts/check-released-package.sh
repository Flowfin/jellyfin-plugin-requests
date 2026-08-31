#!/usr/bin/env bash
# Is a published release asset a thing a server of a claimed line could install, and which line is
# it for?
#
# WHY THIS EXISTS. `scripts/check-manifest-is-fresh.sh` reads the document a server fetches against
# the releases that exist, and says of itself that it does not hash an archive and does not install
# anything. `.github/workflows/package.yaml` installs a package on a server, but the package it
# installs is the one the run just built, which is not the one an operator downloads. Between the
# two sits the thing #152's last condition is about: the bytes that are actually published. This is
# the reader for those, and the install itself is the step after it.
#
# IT TAKES ITS INPUTS AS FILES AND REACHES NO NETWORK. The download is the workflow's, so every
# answer this can give is one `scripts/prove-released-install-refusals.sh` hands it directly, with no
# release and no network. A reader nobody has watched saying no is a reader that might say yes to
# everything, and this one is green for as long as nothing goes wrong with a publish - which is the
# situation the incident behind #111 arose in.
#
# WHAT IT DECIDES, IN ORDER:
#
#   * the three files it was handed exist
#   * the archive is an archive a server could unpack
#   * the archive's digest is the digest the release published beside it
#   * the release metadata names a version
#   * the release metadata names a targetAbi some packaging file at the root claims
#
# THE LINE IS DERIVED AND NEVER WRITTEN HERE. The published metadata names a `targetAbi` and no
# framework, so the framework comes from the packaging file at the root that claims that `targetAbi`.
# That is the same derivation `check-manifest-is-fresh.sh` makes for the same reason: adding a line
# is adding a packaging file, and a list in this script would be the copy that disagrees first.
#
# WHAT IT DOES NOT DO. It does not start a server and it does not fetch anything. Whether the plugin
# loads is `scripts/verify-plugin-loads.sh`, which this answers the arguments for.
#
# usage: scripts/check-released-package.sh <archive> <checksum-file> <metadata-file> <answers-file>
#   the checksum file is the `.sha256` sidecar the publish attaches, in `sha256sum` shape
#   the metadata file is the `.zip.meta.json` sidecar the publish attaches
#   the answers file is written, not read: `version=`, `targetAbi=` and `framework=`, one per line

set -euo pipefail

archive=${1:?path to the released archive}
checksum=${2:?path to the .sha256 the release published beside it}
metadata=${3:?path to the .zip.meta.json the release published beside it}
answers=${4:?path to write the answers to}

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/.." && pwd)

for f in "$archive" "$checksum" "$metadata"; do
    if [ ! -f "$f" ]; then
        echo "check-released-package: ${f} does not exist. This reads what a download actually returned, never what it was expected to return." >&2
        exit 1
    fi
done

# AN ASSET THAT IS NOT AN ARCHIVE FAILS SEVERAL STEPS LATER AND ABOUT SOMETHING ELSE. A publish that
# attached an error page, a truncated upload, or a file that was never zipped all reach a server as
# an install that does not happen, and the server's own message for it is not about the release.
if ! unzip -t "$archive" >/dev/null 2>&1; then
    echo "check-released-package: ${archive} is not an archive that unpacks. A server handed this asset cannot install it, whatever the manifest says about it." >&2
    exit 1
fi

# THE DIGEST IS COMPARED RATHER THAN RECOMPUTED INTO THE ANSWER. What the sidecar promises and what
# the archive is are two published facts, and a release where they disagree is one an operator
# checking the download would reject. The sidecar is `sha256sum` output, so the name in it is the
# name the publish gave the file; the archive is compared by its bytes rather than by that name,
# because a download saved under another name is still the same asset.
published_digest=$(awk 'NR == 1 { print $1 }' "$checksum" | tr -d '\r')
if [ -z "$published_digest" ]; then
    echo "check-released-package: ${checksum} carries no digest on its first line, so there is nothing the archive can be compared against." >&2
    exit 1
fi
actual_digest=$(sha256sum "$archive" | awk '{ print $1 }')
if [ "$actual_digest" != "$published_digest" ]; then
    echo "check-released-package: ${archive} hashes to ${actual_digest} and the release publishes ${published_digest} beside it. An operator who checks the download rejects it, and one who does not installs bytes nobody attested." >&2
    exit 1
fi

version=$(jq -r '.version // empty' "$metadata" | tr -d '\r')
if [ -z "$version" ]; then
    echo "check-released-package: ${metadata} names no version. A catalogue entry with no version is one a server cannot order against what it already has." >&2
    exit 1
fi

abi=$(jq -r '.targetAbi // empty' "$metadata" | tr -d '\r')
if [ -z "$abi" ]; then
    echo "check-released-package: ${metadata} names no targetAbi, so the server line this package was built for cannot be told from any other and there is no floor to install it at." >&2
    exit 1
fi

# Every line this repository claims, derived from the packaging files rather than listed here.
framework=""
claimed=""
for packaging in "$root"/build.yaml "$root"/build-*.yaml; do
    [ -f "$packaging" ] || continue
    packaged_abi=$(sed -n 's/^targetAbi: *"\([^"]*\)".*/\1/p' "$packaging" | head -1)
    packaged_framework=$(sed -n 's/^framework: *"\([^"]*\)".*/\1/p' "$packaging" | head -1)
    [ -n "$packaged_abi" ] || continue
    claimed="${claimed}${claimed:+, }${packaged_abi}"
    if [ "$packaged_abi" = "$abi" ]; then
        framework=$packaged_framework
    fi
done

if [ -z "$framework" ]; then
    echo "check-released-package: the release names targetAbi ${abi} and no packaging file at the root claims it. The lines this repository claims today are ${claimed:-none}. Either a release went out for a line that was dropped, or the packaging file was removed while its releases still stand." >&2
    exit 1
fi

{
    printf 'version=%s\n' "$version"
    printf 'targetAbi=%s\n' "$abi"
    printf 'framework=%s\n' "$framework"
} > "$answers"

echo "$(basename "$archive") unpacks, hashes to the digest published beside it, and names version ${version} at targetAbi ${abi}, which is the line ${framework} packages for."

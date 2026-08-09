#!/usr/bin/env bash
# Write a CycloneDX bill of materials for a built plugin archive.
#
# Somebody installing a binary from a catalogue is trusting whoever built it. The provenance
# attestation says which source and which workflow run produced the archive. This says what is
# inside it. They answer different questions and neither one replaces the other.
#
# The document is derived from the archive and from nothing else. Every entry is a file the archive
# carries, named as the archive names it, with the SHA-256 of the bytes that get written on install.
# Nothing is read out of the source tree, which is what makes regenerating this from a downloaded
# archive a check rather than a restatement: the two documents either agree or the download is not
# the package that was built.
#
# What it deliberately does not carry. No assembly version is read out of a DLL, because that needs
# a .NET toolchain and the version a catalogue serves is already in the archive's own meta.json. No
# compile-time dependency graph is included either: packages.lock.json at the source commit is that
# graph, and a package in it appears here only if the build copied it into the archive. A short list
# is therefore a fact about the package rather than a gap in this script.
#
# The archive is read rather than unpacked, and the names in it are never taken apart by the shell.
# An earlier draft extracted it and walked the files line by line, which needed a rule refusing an
# entry name that carries a newline, and that rule could not be shown to bite: the unpacker on one
# of the machines this is run from rewrites such a name before anything here sees it. A guard nobody
# can watch fail is worse than the case it was written for, so the pipeline that needed it is gone.
#
# The output is reproducible. There is no timestamp and no random serial number, so the same archive
# always produces the same bytes and two documents can be compared with `diff`.
#
# usage: scripts/bill-of-materials.sh <archive.zip> <output.json>
#   SOURCE_REPOSITORY=Flowfin/jellyfin-plugin-requests \
#   SOURCE_COMMIT=c44552645f0dba120c49599deedbc0244b59dcec \
#   PACKAGE_VERSION=0.1.0.0 \
#     scripts/bill-of-materials.sh requests_0.1.0.0.zip requests_0.1.0.0.cdx.json

set -euo pipefail

archive=${1:?path to the built plugin archive, for example requests_0.1.0.0.zip}
output=${2:?path to write the bill of materials to}

: "${SOURCE_REPOSITORY:?owner and name of the repository the archive was built from}"
: "${SOURCE_COMMIT:?the commit the archive was built from}"
: "${PACKAGE_VERSION:?the plugin version this archive carries}"

if [ ! -f "$archive" ]; then
    echo "bill-of-materials: $archive does not exist, so there is nothing to describe." >&2
    exit 1
fi

# python3 is already what scripts/verify-plugin-loads.sh reaches for, so this adds no runtime the
# tree did not already carry. It reads the archive, hashes each entry and writes the JSON through a
# real encoder: escaping a name is the one part of this a shell gets wrong quietly, and an entry
# name is the input least under this script's control.
ARCHIVE_NAME="$(basename "$archive")" python3 - "$archive" "$output" <<'PY'
import hashlib
import json
import os
import sys
import zipfile

source, destination = sys.argv[1], sys.argv[2]

try:
    archive = zipfile.ZipFile(source)
except zipfile.BadZipFile as problem:
    sys.exit(
        "bill-of-materials: %s is not a readable archive (%s). The packaging step produced "
        "something this cannot describe." % (source, problem)
    )

components = []
with archive:
    # Sorted by name so the order of the components does not depend on the order the packaging
    # step happened to write them in.
    for entry in sorted(archive.infolist(), key=lambda item: item.filename):
        if entry.is_dir():
            continue
        digest = hashlib.sha256(archive.read(entry)).hexdigest()
        components.append(
            {
                "type": "file",
                "bom-ref": "archive:" + entry.filename,
                "name": entry.filename,
                "hashes": [{"alg": "SHA-256", "content": digest}],
                "properties": [{"name": "size-in-bytes", "value": str(entry.file_size)}],
            }
        )

if not components:
    sys.exit(
        "bill-of-materials: %s carries no files. A package with nothing in it is not a package."
        % source
    )

document = {
    "bomFormat": "CycloneDX",
    "specVersion": "1.6",
    "version": 1,
    "metadata": {
        "component": {
            "type": "application",
            "bom-ref": os.environ["ARCHIVE_NAME"],
            "name": os.environ["ARCHIVE_NAME"],
            "version": os.environ["PACKAGE_VERSION"],
            "externalReferences": [
                {
                    "type": "vcs",
                    "url": "https://github.com/" + os.environ["SOURCE_REPOSITORY"],
                    "comment": "built from commit " + os.environ["SOURCE_COMMIT"],
                }
            ],
        },
        "tools": {
            "components": [
                {"type": "application", "name": "scripts/bill-of-materials.sh"}
            ]
        },
    },
    "components": components,
}

with open(destination, "w", encoding="utf-8", newline="\n") as handle:
    json.dump(document, handle, indent=2, sort_keys=True)
    handle.write("\n")

print(
    "bill-of-materials: wrote %s describing %d file(s) from %s."
    % (destination, len(components), source)
)
PY

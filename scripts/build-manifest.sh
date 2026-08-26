#!/usr/bin/env bash
# Write the plugin repository manifest a Jellyfin server fetches, from the packages themselves.
#
# A server installs a plugin from a manifest, so the manifest is the product as far as an installing
# operator is concerned. What makes it dangerous is that it is the one release artefact nothing else
# checks: an entry whose checksum does not match its archive, or whose targetAbi lets a server take
# a build it cannot load, is a broken install for everybody who added the repository, and the run
# that produced it is green.
#
# Every field here is therefore derived from a built package rather than typed. The archive is
# hashed for the checksum, and everything else comes out of the `<archive>.meta.json` the packaging
# tool writes beside it, which is the metadata of the package that actually shipped rather than a
# file read back out of the tree.
#
# THE ONE RULE THAT IS NOT A SHAPE CHECK IS THE ORDERING, and it is the reason this is a script
# rather than a jq one-liner. A Jellyfin server picks a version in two steps: it keeps every entry
# whose targetAbi is at or below its own version, then takes the highest version number of what is
# left. Both steps are in Emby.Server.Implementations/Updates/InstallationManager.cs:
#
#     .Where(x => string.IsNullOrEmpty(x.TargetAbi) || Version.Parse(x.TargetAbi) <= appVer);
#     foreach (var v in availableVersions.OrderByDescending(x => x.VersionNumber))
#
# So the version number is the only thing that separates two entries a server has already accepted.
# Two entries at one version number are indistinguishable to it, and an entry with a higher version
# and a lower targetAbi is offered to a newer server in preference to the build meant for it. This
# script refuses both, because a manifest carrying either one installs the wrong package on a real
# server and says nothing while it does.
#
# usage: scripts/build-manifest.sh <output.json> <archive> [<archive> ...]
#
#   SOURCE_URL_PREFIX=https://github.com/Flowfin/jellyfin-plugin-requests/releases/download/0.1.0.0-stable/ \
#     scripts/build-manifest.sh manifest.json requests_0.1.0.0.zip
#
#   MANIFEST_BASE names a manifest the new entries are added to, so a release adds its versions to
#   what is already published instead of replacing it. Absent, the output starts empty.
#
# The output is reproducible. The entries are sorted by version, there is no build timestamp and no
# serial number, so the same packages always produce the same bytes and two manifests can be
# compared with `diff`.

set -euo pipefail

output=${1:?path to write the manifest to, for example manifest.json}
shift || true

if [ "$#" -eq 0 ]; then
    echo "build-manifest: no package was named. A manifest is written from the packages it lists, so there is nothing to write." >&2
    exit 1
fi

: "${SOURCE_URL_PREFIX:?the URL each archive is downloaded from, up to and including the last slash}"

case "${SOURCE_URL_PREFIX}" in
    */) ;;
    *)
        echo "build-manifest: SOURCE_URL_PREFIX '${SOURCE_URL_PREFIX}' does not end in a slash. The archive file name is appended to it, and without the slash the download URL names a sibling of the directory rather than a file in it." >&2
        exit 1
        ;;
esac

for archive in "$@"; do
    if [ ! -f "${archive}" ]; then
        echo "build-manifest: ${archive} does not exist. The manifest lists packages that were built, never packages that were meant to be." >&2
        exit 1
    fi
    if [ ! -f "${archive}.meta.json" ]; then
        echo "build-manifest: ${archive}.meta.json does not exist. Every field of an entry except the checksum comes out of the metadata the packaging tool writes beside the archive, so an archive without it cannot be described." >&2
        exit 1
    fi
done

if [ -n "${MANIFEST_BASE:-}" ] && [ ! -f "${MANIFEST_BASE}" ]; then
    echo "build-manifest: MANIFEST_BASE names ${MANIFEST_BASE}, which does not exist. Leave it unset to start a manifest rather than pointing it at a file that is missing: a published manifest read back as absent drops every version already released." >&2
    exit 1
fi

# python3 is what scripts/bill-of-materials.sh and scripts/verify-plugin-loads.sh already reach for,
# so this adds no runtime the release job did not carry. It reads the packages, orders the entries
# and writes the JSON through a real encoder, because the fields being copied are prose an operator
# wrote and escaping them is the part a shell gets wrong quietly.
python3 - "${output}" "$@" <<'PY'
import hashlib
import json
import os
import sys

output, archives = sys.argv[1], sys.argv[2:]

# The fields a catalogue holds once per plugin rather than once per version. A manifest carrying two
# of them is an entry whose text flips depending on which package was published last, so they are
# compared across every package and against whatever was already published.
IDENTITY = ("guid", "name", "description", "overview", "owner", "category", "imageUrl")

# The fields an entry cannot be written without. `changelog` and `timestamp` come from the same
# place and are allowed to be absent, because a server shows them and installs without them.
PER_VERSION = ("version", "targetAbi")


def refuse(message):
    sys.exit("build-manifest: " + message)


def version_key(text):
    """Order a version string the way the server does, which is System.Version."""
    parts = text.split(".")
    if not 2 <= len(parts) <= 4:
        refuse(
            "version '%s' is not two to four numeric parts. A server parses this with "
            "System.Version and drops the whole entry when it cannot." % text
        )
    numbers = []
    for part in parts:
        if not part.isdigit():
            refuse(
                "version '%s' has a part that is not a number. A server parses this with "
                "System.Version and drops the whole entry when it cannot." % text
            )
        numbers.append(int(part))
    # System.Version treats an absent field as zero, so 0.1.0 and 0.1.0.0 are one version written
    # two ways. Comparing the strings would let that pair into one manifest as two entries.
    while len(numbers) < 4:
        numbers.append(0)
    return tuple(numbers)


base = {}
existing = []
base_path = os.environ.get("MANIFEST_BASE") or ""
if base_path:
    with open(base_path, "r", encoding="utf-8") as handle:
        try:
            document = json.load(handle)
        except json.JSONDecodeError as problem:
            refuse(
                "%s is not readable JSON (%s). A manifest that cannot be read is one every "
                "already published version disappears from." % (base_path, problem)
            )
    if not isinstance(document, list) or len(document) != 1:
        refuse(
            "%s is not a list of exactly one plugin, and this repository publishes exactly one. A "
            "manifest of another shape is not one this script may rewrite." % base_path
        )
    base = document[0]
    existing = list(base.get("versions") or [])

entries = []
for archive in archives:
    with open(archive + ".meta.json", "r", encoding="utf-8") as handle:
        try:
            metadata = json.load(handle)
        except json.JSONDecodeError as problem:
            refuse("%s.meta.json is not readable JSON (%s)." % (archive, problem))

    for field in IDENTITY + PER_VERSION:
        if not metadata.get(field):
            refuse(
                "%s.meta.json declares no '%s'. The manifest entry is copied from that file, and a "
                "server reads that field." % (archive, field)
            )

    identity = {field: metadata[field] for field in IDENTITY}
    if not base:
        base = dict(identity)
    for field in IDENTITY:
        if base.get(field) != identity[field]:
            refuse(
                "%s.meta.json declares %s '%s' and the manifest already carries '%s'. A catalogue "
                "holds one entry per plugin, so a divergence here is an entry whose text changes "
                "with whichever package was published last."
                % (archive, field, identity[field], base.get(field))
            )

    with open(archive, "rb") as handle:
        checksum = hashlib.md5(handle.read()).hexdigest()

    entry = {
        "version": metadata["version"],
        "targetAbi": metadata["targetAbi"],
        "sourceUrl": os.environ["SOURCE_URL_PREFIX"] + os.path.basename(archive),
        # The server hashes the download with MD5 and compares it case-insensitively against this,
        # in InstallationManager.InstallPackageInternal. It is the one field here that describes the
        # bytes rather than the package, which is why it is computed and never copied.
        "checksum": checksum,
    }
    for field in ("changelog", "timestamp"):
        if metadata.get(field):
            entry[field] = metadata[field]
    entries.append(entry)

versions = existing + entries
if not versions:
    refuse("no version entry was produced, so the manifest would list a plugin nobody can install.")

# Newest first, which is the order a catalogue is read in and is what makes the output reproducible.
versions.sort(key=lambda item: version_key(item["version"]), reverse=True)

seen = {}
for entry in versions:
    key = version_key(entry["version"])
    if key in seen:
        refuse(
            "version %s is carried by two entries, targetAbi %s and targetAbi %s. A server keeps "
            "every entry whose targetAbi is at or below its own version and then takes the highest "
            "version number of what is left, so two entries at one version are one entry to it and "
            "which package it installs is decided by nothing."
            % (entry["version"], seen[key], entry["targetAbi"])
        )
    seen[key] = entry["targetAbi"]

# Sorted by version descending above, so comparing neighbours is enough: an entry newer than the one
# after it must also claim a server line at least as new.
for newer, older in zip(versions, versions[1:]):
    if version_key(newer["targetAbi"]) < version_key(older["targetAbi"]):
        refuse(
            "version %s claims targetAbi %s and version %s claims targetAbi %s, so the newer "
            "version is the one for the older server line. A server on the newer line accepts both "
            "entries and takes the highest version number, which is the package built for the "
            "server it is not."
            % (newer["version"], newer["targetAbi"], older["version"], older["targetAbi"])
        )

base["versions"] = versions

with open(output, "w", encoding="utf-8", newline="\n") as handle:
    json.dump([base], handle, indent=2, sort_keys=True)
    handle.write("\n")

print(
    "build-manifest: wrote %s listing %d version(s) of %s."
    % (output, len(versions), base["name"])
)
PY

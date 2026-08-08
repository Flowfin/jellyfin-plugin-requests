#!/usr/bin/env bash
# Fixture for release-created-only-by-a-tag-push. Nothing runs this file; it
# exists so the rule can be watched refusing the mistake it names.
#
# The near-miss is the one somebody actually makes: a publish step that has the
# archive in hand and reaches for the shortest way to get it in front of people,
# rather than pushing the tag and letting the release metadata gate run first.
set -euo pipefail

tag="$1"
archive="$2"

# Legal neighbour, left here on purpose: reading is not mutating, and the rule
# has to stay quiet on this line for docs/versioning.md to keep counting
# releases.
gh release list --repo Flowfin/jellyfin-plugin-requests --json tagName --jq 'length'

# The regression. Each of these burns the tag it names.
gh release create "$tag" --title "$tag" --notes "cut by hand"
gh release upload "$tag" "$archive"
gh release edit "$tag" --draft=false
gh release delete-asset "$tag" "$archive"
gh release delete "$tag"

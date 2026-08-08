#!/usr/bin/env bash
# Every rule in tools/opengrep/rules.yaml has to be watched refusing something,
# or the clean tree the gate reports means nothing: a rule that matches no file
# and a rule that matches nothing because the tree is good look identical from
# outside.
#
# This scans the fixture directory, collects the rule ids that actually fired,
# and fails unless every declared rule is among them. A rule whose pattern
# stopped matching, a fixture that was edited until it no longer violates
# anything, and a rule added with no fixture all red the gate here.
#
# The gate's other step scans the repository with the same rule set and the
# fixture directory excluded, and requires no findings at all. The two steps are
# the two halves: this one says each rule bites, that one says nothing in the
# tree is bitten.
set -euo pipefail

opengrep=${OPENGREP:-./opengrep}
rules=tools/opengrep/rules.yaml
fixtures=tools/opengrep/fixtures
findings=fixture-findings.json

# The declared set, read out of the rule file rather than written here, so a
# rule added without a fixture is caught instead of being invisible to its own
# proof.
declared=$(sed -n 's/^  - id: \(.*\)$/\1/p' "$rules" | sort)
if [ -z "$declared" ]; then
    echo "no rule ids found in $rules" >&2
    exit 1
fi

# A reported finding names its rule as the config path joined by dots and then
# the id, `tools.opengrep.<id>`, so the two sides are compared with that prefix
# stripped. Stripping it means taking everything after the last dot, which is
# only the id while no id contains one. Refuse a dotted id rather than let this
# quietly compare the wrong halves.
dotted=$(printf '%s\n' "$declared" | grep '\.' || true)
if [ -n "$dotted" ]; then
    echo "rule ids may not contain a dot, because that is what separates an id from its config path:" >&2
    printf '%s\n' "$dotted" >&2
    exit 1
fi

# Findings are not an error here, they are the point, so the scan runs without
# --error and its exit status is only used to explain a missing report.
set +e
"$opengrep" scan --config "$rules" --json "$fixtures" >"$findings"
status=$?
set -e

if ! jq -e 'has("results")' "$findings" >/dev/null 2>&1; then
    echo "opengrep produced no results report (exit $status)" >&2
    exit 1
fi

scan_errors=$(jq -r '(.errors // []) | length' "$findings")
if [ "$scan_errors" != "0" ]; then
    echo "opengrep reported $scan_errors error(s) while scanning the fixtures:" >&2
    jq -r '(.errors // [])[] | "  \(.type // "error"): \(.message // .long_msg // "")"' "$findings" >&2
    exit 1
fi

fired=$(jq -r '.results[].check_id' "$findings" | sed 's/^.*\.//' | sort -u)

echo "== rules declared in $rules"
echo "$declared"
echo
echo "== rules the fixtures made fire"
echo "${fired:-(none)}"
echo

missing=$(comm -23 <(printf '%s\n' "$declared") <(printf '%s\n' "$fired"))
if [ -n "$missing" ]; then
    echo "these rules fired on no fixture, so nothing proves they still bite:" >&2
    printf '%s\n' "$missing" >&2
    exit 1
fi

echo "every declared rule fired on a fixture"

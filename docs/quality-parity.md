# Quality parity

The gate this repository is measured against is the one on
[jellyfin-plugin-sso](https://github.com/iderex/jellyfin-plugin-sso). Every
workflow there is either adopted here or declined here, and every workflow here
with no counterpart there is placed the same way. A difference nobody wrote a
line for is a defect. A difference with a line is a decision.

This document is the reasoning. It is not the gate. What the gate requires is
printed by the commands below, and where the two disagree the commands are
right.

## What each gate actually requires

The other board:

    $ gh api repos/iderex/jellyfin-plugin-sso/rules/branches/main \
        --jq '.[] | select(.type=="required_status_checks")
              | .parameters.required_status_checks[].context'
    build
    ABI floor build
    Package (JPRM) / Build package
    Package (JPRM) / Generate SBOM
    CodeQL
    Analyze (csharp)
    DCO sign-off
    Deterministic PR-hygiene checks
    Enforce greppable invariants
    Reject Trojan Source Unicode
    Audit workflows (zizmor)
    prettier
    dependency-review

This one:

    $ gh api repos/iderex/jellyfin-plugin-requests/rules/branches/master \
        --jq '.[] | select(.type=="required_status_checks")
              | .parameters.required_status_checks[].context'
    call / build
    call / test
    Reject Trojan Source Unicode

Thirteen contexts against three, and the gap is not the same as the gap in what
runs. Most of the thirteen have a counterpart running here already and are
simply not required, which is a repository setting rather than a file in this
tree. What ran on the last change to land, at `1a18979`:

    $ gh api repos/iderex/jellyfin-plugin-requests/commits/1a18979/check-runs \
        --jq '.check_runs[].name' | sort -u
    Analyze (actions, none)
    Analyze (csharp, manual)
    Analyze (javascript-typescript, none)
    Audit workflows (zizmor)
    call / build
    call / test
    Check formatting
    CodeQL
    DCO sign-off
    dependency-review
    floor 10.11.0.0
    floor 12.0.0.0
    lines
    Reject Trojan Source Unicode
    zizmor

Four of the thirteen have no counterpart running here at all: the two packaging
contexts, the pull request hygiene checks and the greppable invariant lint.
Those are #108, #109, #26 and #28. The rest run and are not required, and the
section below is the set that says which of them should be.

## The set to require

Thirteen contexts, named exactly as the checks name themselves. A required check
is matched literally: the ruleset holds a string, GitHub compares it to the name
a check run reports, and nothing reconciles the two. So renaming a job does not
rename a requirement, it removes one and leaves the ruleset asking for a name
nothing produces, which blocks every pull request until somebody notices. Every
name below was copied out of a run rather than out of a workflow file.

    Analyze (actions, none)
    Analyze (csharp, manual)
    Analyze (javascript-typescript, none)
    Audit workflows (zizmor)
    call / build
    call / test
    Check formatting
    DCO sign-off
    dependency-review
    floor 10.11.0.0
    floor 12.0.0.0
    lines
    Reject Trojan Source Unicode

There are no bypass actors, and none is to be added. A rule with a bypass is a
rule that holds for whoever did not think to ask, which is the opposite of what
this list is for.

Commits are to be signed. The ruleset carries no signature rule today and every
commit on the default branch is already signed, so the rule costs nothing to add
and closes the case where one is not:

    $ gh api 'repos/iderex/jellyfin-plugin-requests/commits?sha=master&per_page=100'         --jq '[.[] | .commit.verification.verified]
              | {total: length, verified: (map(select(.)) | length),
                 unverified: (map(select(. == false)) | length)}'
    {"total":39,"unverified":0,"verified":39}

Three checks that run here are deliberately not in the list.

`Scorecard analysis` runs on a push to the default branch, on a schedule and on
a branch-protection change, and never on a pull request. Requiring it would
require a check that no pull request can produce, which blocks every merge
rather than gating one.

`CodeQL` and `zizmor` are the code-scanning tab's own checks rather than jobs
this tree runs, produced when results are uploaded. `CodeQL` reported `neutral`
on the head of the last pull request to land:

    $ gh api repos/iderex/jellyfin-plugin-requests/commits/6f3ccd2/check-runs         --jq '.check_runs[] | select(.name=="CodeQL") | .conclusion'
    neutral

A required check whose verdict is neither pass nor fail is a requirement nobody
can read. The jobs behind those two, the three `Analyze` legs and
`Audit workflows (zizmor)`, are in the list instead, and they are the ones that
go red for a reason this repository wrote.

What is live is printed rather than described, and where the two disagree the
command is right:

    $ gh api repos/iderex/jellyfin-plugin-requests/rules/branches/master         --jq '.[] | select(.type=="required_status_checks")
              | .parameters.required_status_checks[].context'
    call / build
    call / test
    Reject Trojan Source Unicode

Three of the thirteen. The set above is not applied: a ruleset is a repository
setting rather than a file in this tree, and nothing in this change touches one.
#30 carries the application and the demonstration that a red check refuses a
merge.

### One line per difference from the other board's set

- `build` there is `call / build` and `call / test` here, because the legs live
  in a called workflow and a called job's context carries the calling job's
  name.
- `ABI floor build` there is `lines`, `floor 10.11.0.0` and `floor 12.0.0.0`
  here, because the claimed lines are read out of the packaging files rather
  than listed in a job, so a line is a file and not a name in a ruleset.
- `Package (JPRM) / Build package` has no counterpart running here; #108.
- `Package (JPRM) / Generate SBOM` has no counterpart running here; #109.
- `CodeQL` there is required and is not here, because it is the code-scanning
  tab's check rather than a job, and the three `Analyze` legs are required
  instead.
- `Analyze (csharp)` there is `Analyze (csharp, manual)` here, and two more
  beside it, because the build mode is part of the matrix job's name and this
  repository scans three languages rather than one.
- `Deterministic PR-hygiene checks` has no counterpart running here; #26.
- `Enforce greppable invariants` has no counterpart running here; #28.
- `prettier` there is `Check formatting` here, which is the same workflow under
  a job name of its own.
- `DCO sign-off`, `dependency-review`, `Reject Trojan Source Unicode` and
  `Audit workflows (zizmor)` are the same name on both boards.
- Signed commits are required by neither ruleset today. This set asks for them
  here, which is a difference from there rather than parity with it.

## One row per workflow on the other board

Named by file, because two files can produce one context and one file can
produce several.

| File there                  | Here            | Reasoning                                                                                                                                                                      |
| --------------------------- | --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `build.yml`                 | adopted, #108   | It is the reusable packaging leg, and packaging on a pull request is what catches a broken package before release day instead of on it.                                        |
| `codeql.yml`                | adopted, landed | `scan-codeql.yaml` names this repository's own language set and branch triggers rather than the default that would have covered the default branch and nothing else.           |
| `dco.yml`                   | adopted, landed | It arrived with this tree, it runs on every pull request, and the record that it refuses a commit with no sign-off is below.                                                   |
| `dependency-review.yml`     | adopted, landed | It arrived with this tree and refuses a newly introduced dependency carrying a published advisory, which is the one supply-chain failure a diff can be judged for.             |
| `dotnet.yml`                | adopted, landed | It carries the build, test and floor legs there; here they are `gate.yaml` and `abi-floor.yaml`, because the shared workflows know nothing about this tree's multi-targeting.  |
| `e2e-login.yml`             | declined        | There is no authentication flow here to drive end to end, and the risk it stands for, a packaged plugin that builds but does not load, is covered by the run in `testing.md`.  |
| `fuzz.yml`                  | declined        | The untrusted input here is authenticated JSON from the server's own API rather than an anonymous credential, and round-trip tests over the persisted schema in #47 cover it.  |
| `manifest-freshness.yml`    | adopted, #111   | A publish that reports success and leaves the manifest untouched ships nothing installable, and nothing else would notice.                                                     |
| `nightly-betas.yml`         | declined        | Nothing is shipping yet and a nightly channel before a first release is a channel with nothing in it; nothing covers that risk here because there is no risk to cover yet.     |
| `opengrep.yml`              | adopted, #28    | Several rules on this board are patterns a compiler cannot refuse and a document can only ask for.                                                                             |
| `pr-hygiene.yml`            | adopted, #26    | It reasons about the change rather than about the code, which nothing else here does.                                                                                          |
| `prettier.yml`              | adopted, landed | This plugin ships HTML, CSS and JavaScript inside the assembly and no .NET analyzer reaches any of it, and the markdown is where everything here is argued.                    |
| `publish-beta.yml`          | declined        | It publishes to a beta channel this repository has not decided to have, and nothing covers that risk here because no channel exists to protect; #110 is where that is decided. |
| `publish-failure-alert.yml` | adopted, #111   | A freshness check whose failure sits unread in a run log is not a check.                                                                                                       |
| `publish-jf12-beta.yml`     | declined        | Same beta channel, second line, and the same absence of anything to protect.                                                                                                   |
| `publish-jf12-stable.yml`   | adopted, #110   | The 12.0 line is claimed in `build-jf12.yaml` and a claimed line with no release path is a claim nobody can install.                                                           |
| `publish.yml`               | adopted, #110   | The 10.11 line's release path, and the `publish.yaml` here is inherited and knows nothing about two lines.                                                                     |
| `regenerate-manifest.yml`   | adopted, #110   | The manifest is regenerated by the release path rather than edited by hand, because a hand-edited manifest drifts against what was actually published.                         |
| `scorecard.yml`             | adopted, landed | It arrived with this tree and its push trigger named a branch this repository does not have, so until #25 it did not run on the default branch once.                           |
| `stryker-mutation.yml`      | adopted, #29    | The transition table and the authorisation checks are exactly the small branchy code where line coverage says little and a surviving mutant is a missing negative test.        |
| `unicode-guard.yml`         | adopted, landed | It arrived with this tree, it is the one inherited guard already in the required set, and the record that it refuses a bidirectional control character is below.               |
| `wiki-lint.yml`             | declined        | There is no wiki here and the documentation lives in the tree, where the ordinary gate already reaches it.                                                                     |
| `zizmor.yml`                | adopted, landed | It arrived with this tree, its push trigger named a branch this repository does not have, and it was red on the tree as it stood until the inherited callers were pinned.      |

## One row per workflow here with no counterpart there

Eight files here map onto a file there and are placed by the table above:
`dco.yml`, `dependency-review.yml`, `prettier.yml`, `publish.yaml`,
`scan-codeql.yaml`, `scorecard.yml`, `unicode-guard.yml` and `zizmor.yml`. Three
of the rows below are the rest, and eight plus three is what the directory
holds:

    $ ls .github/workflows/ | wc -l
    11

Those three do have a counterpart there and are listed here anyway, because what
they are is not what it is: `dotnet.yml` is one file there and three here, and
the split is the point rather than an accident.

The four rows under them are files this repository declined and then deleted.
The rows stay because the reasoning is what the table is for, and a file that
vanishes without one is a question somebody asks again. `Disposition` says which
of the two a row is, so no reader has to infer presence from a table that no
longer tracks it.

| File here               | Disposition       | Reasoning                                                                                                                                                                  |
| ----------------------- | ----------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `abi-floor.yaml`        | adopted, landed   | The floor leg, split out because the lines and their floors are read out of the packaging files rather than listed in a job, so adding a line is adding a file.            |
| `build.yaml`            | adopted, landed   | The trigger surface and the two check names the ruleset matches literally; the legs themselves moved into `gate.yaml`.                                                     |
| `gate.yaml`             | adopted, landed   | The build and test legs, in this repository rather than called from another organisation, because a called workflow knows nothing about this tree's lockfiles.             |
| `changelog.yaml`        | declined, deleted | It drafts a release changelog against a version scheme this repository has not fixed, and nothing covers that risk here because there is nothing to release yet; #107.     |
| `command-dispatch.yaml` | declined, deleted | It turns issue comments into workflow runs, which is a surface this repository does not use, and nothing here needs covering because nothing depends on it.                |
| `command-rebase.yaml`   | declined, deleted | Same comment-driven surface, the half that rewrites pull request branches on command, and the same absence.                                                                |
| `sync-labels.yaml`      | declined, deleted | It overwrites the label vocabulary from a file in another organisation, and the vocabulary here is this board's own, so adopting it would delete what it is meant to keep. |

## What the inherited guards have been watched doing

Five guards arrived with this repository. #25 asks for a red run and a green run
for each, caused by the thing that guard exists to refuse. Four have both. The
fifth cannot have a red run at all, for a reason that is about the guard rather
than about the effort, and that is stated rather than filled in.

| Guard                          | Red run                                                                        | Green run                          |
| ------------------------------ | ------------------------------------------------------------------------------ | ---------------------------------- |
| `Audit workflows (zizmor)`     | 31041799794, sixteen findings against the eight inherited callers              | 31095610752, at `1a18979`          |
| `Reject Trojan Source Unicode` | 31047199620, a file carrying U+202E                                            | 31095610752, at `1a18979`          |
| `DCO sign-off`                 | 31047200286, a commit with no `Signed-off-by` trailer                          | 31095610752, at `1a18979`          |
| `dependency-review`            | 31047201526, Newtonsoft.Json 12.0.3, below the fix line of GHSA-5crp-9r3c-p9vr | 31095610752, at `1a18979`          |
| `Scorecard analysis`           | none, and none is possible                                                     | at `f1c8881` on the default branch |

The three red runs in the middle are on one head, `bee97c0`, which carried all
three defects at once and was closed without merging.

The scorecard scores the repository and uploads the result. It declares no
threshold and fails only if the action itself errors, so there is no input that
makes it go red for the reason it names, and a guard that cannot refuse anything
is not a guard. That is a defect in what was asked for rather than one this
document can close, and #25 carries it.

## What this document does not do

Nothing here is enforced. No check reads this file, no check compares it against
the workflow directory, and a workflow added or deleted without a row moves
nothing red. The table drifts against the thing it describes the moment either
board changes, and the commands above are the only part that cannot.

The adopted rows that name an issue are decisions, not deliveries. What is
running is the check list at the top, printed from a run, and an adopted row
with an open issue beside it is a workflow this repository has decided it wants
and does not yet have.

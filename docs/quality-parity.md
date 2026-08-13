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
tree. What ran on `55f1ad2`, the head of the change that added the invariant
lint:

    $ gh api repos/Flowfin/jellyfin-plugin-requests/commits/55f1ad2/check-runs \
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
    Deterministic pull request hygiene checks
    Enforce greppable invariants
    floor 10.11.0.0
    floor 12.0.0.0
    lines
    Reject Trojan Source Unicode
    zizmor

One of the thirteen has no counterpart running here at all, and it is the bill of
materials, #109. It was four until the hygiene job landed under #26, three until
the invariant lint landed under #28, and two until the package build landed under
#108, which is `package-lines` and one `package` job per claimed line rather than
the single context the other board has. The rest run and are not required, and
the section below is the set that says which of them should be.

## The set to require

Fifteen contexts, named exactly as the checks name themselves. A required check
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
    Deterministic pull request hygiene checks
    Enforce greppable invariants
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

Three of the fifteen. The set above is not applied: a ruleset is a repository
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
- `Package (JPRM) / Build package` there is `package-lines`, `package 10.11.0.0`
  and `package 12.0.0.0` here, for the same reason the floor build is three
  contexts: the lines are read out of the packaging files rather than listed in a
  job. They are not in the set above, because the set is what #30 applies and
  this pair arrived after it was written down.
- `Package (JPRM) / Generate SBOM` has no counterpart running here; #109.
- `CodeQL` there is required and is not here, because it is the code-scanning
  tab's check rather than a job, and the three `Analyze` legs are required
  instead.
- `Analyze (csharp)` there is `Analyze (csharp, manual)` here, and two more
  beside it, because the build mode is part of the matrix job's name and this
  repository scans three languages rather than one.
- `Deterministic PR-hygiene checks` there is
  `Deterministic pull request hygiene checks` here, which is the same job under a
  name written out, and its blocking tier reaches a different audience: the
  inside set there is owner and member, and here it also holds collaborator,
  because the value a workflow reads out of the event payload and the value the
  API returns for the same pull request disagree on this board.
- `Enforce greppable invariants` is the same name on both boards, over a
  different rule set. A rule is added the first time an invariant on this board
  is decided, so each set says what its own tree has decided rather than what the
  other one has.
- `prettier` there is `Check formatting` here, which is the same workflow under
  a job name of its own.
- `DCO sign-off`, `dependency-review`, `Reject Trojan Source Unicode` and
  `Audit workflows (zizmor)` are the same name on both boards.
- Signed commits are required by neither ruleset today. This set asks for them
  here, which is a difference from there rather than parity with it.

## One row per workflow on the other board

Named by file, because two files can produce one context and one file can
produce several.

| File there                  | Here            | Reasoning                                                                                                                                                                                           |
| --------------------------- | --------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `build.yml`                 | adopted, #108   | It is the reusable packaging leg, and packaging on a pull request is what catches a broken package before release day instead of on it.                                                             |
| `codeql.yml`                | adopted, landed | `scan-codeql.yaml` names this repository's own language set and branch triggers rather than the default that would have covered the default branch and nothing else.                                |
| `dco.yml`                   | adopted, landed | It arrived with this tree, it runs on every pull request, and the record that it refuses a commit with no sign-off is below.                                                                        |
| `dependency-review.yml`     | adopted, landed | It arrived with this tree and refuses a newly introduced dependency carrying a published advisory, which is the one supply-chain failure a diff can be judged for.                                  |
| `dotnet.yml`                | adopted, landed | It carries the build, test and floor legs there; here they are `gate.yaml` and `abi-floor.yaml`, because the shared workflows know nothing about this tree's multi-targeting.                       |
| `e2e-login.yml`             | declined        | There is no authentication flow here to drive end to end, and the risk it stands for, a packaged plugin that builds but does not load, is covered by the run in `testing.md`.                       |
| `fuzz.yml`                  | declined        | The untrusted input here is authenticated JSON from the server's own API rather than an anonymous credential, and round-trip tests over the persisted schema in #47 cover it.                       |
| `manifest-freshness.yml`    | adopted, #111   | A publish that reports success and leaves the manifest untouched ships nothing installable, and nothing else would notice.                                                                          |
| `nightly-betas.yml`         | declined        | Nothing is shipping yet and a nightly channel before a first release is a channel with nothing in it; nothing covers that risk here because there is no risk to cover yet.                          |
| `opengrep.yml`              | adopted, landed | Some rules here are patterns a compiler cannot refuse and a document can only ask for; `invariant-lint.yaml` refuses them, and fails unless every rule fired on a fixture written to be refused.    |
| `pr-hygiene.yml`            | adopted, landed | It reasons about the change rather than about the code, which nothing else here does; `pr-hygiene.yaml` carries the two blocking checks and the two advisory ones, and not its commit-message pair. |
| `prettier.yml`              | adopted, landed | This plugin ships HTML, CSS and JavaScript inside the assembly and no .NET analyzer reaches any of it, and the markdown is where everything here is argued.                                         |
| `publish-beta.yml`          | declined        | It publishes to a beta channel this repository has not decided to have, and nothing covers that risk here because no channel exists to protect; #110 is where that is decided.                      |
| `publish-failure-alert.yml` | adopted, #111   | A freshness check whose failure sits unread in a run log is not a check.                                                                                                                            |
| `publish-jf12-beta.yml`     | declined        | Same beta channel, second line, and the same absence of anything to protect.                                                                                                                        |
| `publish-jf12-stable.yml`   | adopted, #110   | The 12.0 line is claimed in `build-jf12.yaml` and a claimed line with no release path is a claim nobody can install.                                                                                |
| `publish.yml`               | adopted, #110   | The 10.11 line's release path, and the `publish.yaml` here is inherited and knows nothing about two lines.                                                                                          |
| `regenerate-manifest.yml`   | adopted, #110   | The manifest is regenerated by the release path rather than edited by hand, because a hand-edited manifest drifts against what was actually published.                                              |
| `scorecard.yml`             | adopted, landed | It arrived with this tree and its push trigger named a branch this repository does not have, so until #25 it did not run on the default branch once.                                                |
| `stryker-mutation.yml`      | adopted, landed | The transition table and the authorisation checks are exactly the small branchy code where line coverage says little and a surviving mutant is a missing negative test.                             |
| `unicode-guard.yml`         | adopted, landed | It arrived with this tree, it is the one inherited guard already in the required set, and the record that it refuses a bidirectional control character is below.                                    |
| `wiki-lint.yml`             | declined        | There is no wiki here and the documentation lives in the tree, where the ordinary gate already reaches it.                                                                                          |
| `zizmor.yml`                | adopted, landed | It arrived with this tree, its push trigger named a branch this repository does not have, and it was red on the tree as it stood until the inherited callers were pinned.                           |

## One row per workflow here with no counterpart there

Eleven files here map onto a file there and are placed by the table above:
`dco.yml`, `dependency-review.yml`, `invariant-lint.yaml`, `mutation.yaml`,
`prettier.yml`, `pr-hygiene.yaml`, `publish.yaml`, `scan-codeql.yaml`,
`scorecard.yml`, `unicode-guard.yml` and `zizmor.yml`. Three of the rows below
are the rest, and eleven plus three is what the directory holds:

    $ ls .github/workflows/ | wc -l
    14

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

## Mutation testing is adopted, fuzzing is declined

#29 asked for both answers rather than for one silence, and the two answers go
in different directions for reasons that are not the same reason.

Mutation testing is adopted and is `.github/workflows/mutation.yaml`. It runs
weekly and on demand, and it has no `pull_request` trigger and no `push`
trigger, so the number it produces cannot be a required check and cannot hold a
merge. `--break-at 0` is passed at the command on top of that, so the step reds
only when the run itself breaks and never on a low score. That is the shape #29
asked for.

The case for it here is the transition table in M4 and the authorisation checks
in #51: small branchy code where a line is covered by a test that never asserts
what the branch decided. None of that exists yet, so the run today measures the
plugin class and nothing else, and it is worth saying that the value of this
workflow is almost all ahead of it.

It has been watched running. Run 31202495147, on a throwaway branch carrying a
push trigger this file's workflow does not have, because a `workflow_dispatch`
trigger is not dispatchable until the file is on the default branch and a
workflow nobody has watched run is a workflow nobody knows works:

    $ gh api repos/Flowfin/jellyfin-plugin-requests/actions/runs/31202495147 --jq '"\(.id)  \(.name)  \(.event)  \(.head_sha[0:7])  \(.conclusion)"'
    31202495147  Mutation testing  push  bc9a313  success

What it found, which is the part that says the run is not decorative:

    $ gh run download 31202495147 --repo Flowfin/jellyfin-plugin-requests --name mutation-report --dir mut-report
    $ python -c "import json;d=json.load(open('mut-report/reports/mutation-report.json'));[print(m['status'],m['mutatorName'],m['location']['start']) for f in d['files'].values() for m in f['mutants']]"
    Survived Block removal mutation {'line': 24, 'column': 5}
    Killed String mutation {'line': 29, 'column': 36}
    Killed String mutation {'line': 32, 'column': 43}
    Ignored Block removal mutation {'line': 41, 'column': 5}
    Killed Object initializer mutation {'line': 44, 'column': 13}
    Killed String mutation {'line': 47, 'column': 84}

Four killed, one survived, one ignored, and a score of 80 per cent. The
survivor is the body of the constructor in `Jellyfin.Plugin.Requests/Plugin.cs`,
emptied. Deleting `Instance = this;` changes nothing any test asserts, so the
static instance the rest of the plugin will read is set by a line no test
watches. That is one missing negative test, found by the thing that was adopted
to find them, and it is recorded here rather than fixed here because this change
is the workflow and the decision.

Fuzzing is declined, and the row above says why in one line. The longer version
is that the untrusted input on an authentication plugin is a token from an
anonymous caller, and here it is JSON the server has already authenticated plus
responses from a service an operator configured and can reach. Its replacement
is named and is an open issue on this board rather than an intention: the
round-trip tests over the persisted schema in #47, which cover the same parse
path from the direction that matters here.

What is not claimed. Nothing enforces the weekly run: a schedule on a repository
with no recent activity is paused by GitHub, and nothing here notices. Nothing
compares one week's score against the last, so a score that falls is a number
somebody has to go and read. Both are the cost of keeping this off the merge
path and are not being described as covered.

## What the inherited guards have been watched doing

Five workflows arrived with this repository, and they are not five of the same
thing. Four of them read the tree or the change and go red on what they find.
The fifth reads the repository, scores it, and publishes the score, and no step
in it compares that score against anything. The `Kind` column says which of the
two a row is, because four refusals and one report listed under one heading is a
table that reads as five gates.

#25 asks for a red run and a green run for each, caused by the thing that guard
exists to refuse. The four that refuse have both. The one that reports has no
red run and can have none, which is settled below rather than left blank.

| Guard                          | Kind    | Red run                                                                        | Green run                 |
| ------------------------------ | ------- | ------------------------------------------------------------------------------ | ------------------------- |
| `Audit workflows (zizmor)`     | refuses | 31041799794, sixteen findings against the eight inherited callers              | 31095610611, at `1a18979` |
| `Reject Trojan Source Unicode` | refuses | 31047199620, a file carrying U+202E                                            | 31095610670, at `1a18979` |
| `DCO sign-off`                 | refuses | 31047200286, a commit with no `Signed-off-by` trailer                          | 31095610728, at `1a18979` |
| `dependency-review`            | refuses | 31047201526, Newtonsoft.Json 12.0.3, below the fix line of GHSA-5crp-9r3c-p9vr | 31095610678, at `1a18979` |
| `Scorecard analysis`           | reports | none, and none is possible                                                     | 31110915427, at `0ecd860` |

The three red runs in the middle are on one head, `bee97c0`, which carried all
three defects at once and was closed without merging.

Every identifier in the green column is a run of the guard's own workflow, and
the commit beside it is the commit that run happened on rather than the current
head of the default branch. It stops being the current head at the next merge,
and the commands below are what a reader can re-run when it has.

    $ for r in 31095610611 31095610670 31095610728 31095610678 31110915427; do \
        gh api repos/iderex/jellyfin-plugin-requests/actions/runs/$r \
          --jq '"\(.id)  \(.name)  \(.head_sha[0:7])  \(.conclusion)"'; done
    31095610611  Workflow Security Analysis  1a18979  success
    31095610670  unicode-guard  1a18979  success
    31095610728  DCO  1a18979  success
    31095610678  Dependency review  1a18979  success
    31110915427  Scorecard supply-chain security  0ecd860  success

Two commits rather than one, because two of the four refusing guards start on a
pull request and nothing else, and the scorecard has no pull request trigger at
all, which the grep further down this page prints:

    $ git grep -A1 "^on:" origin/master -- .github/workflows/dco.yml .github/workflows/dependency-review.yml
    origin/master:.github/workflows/dco.yml:on:
    origin/master:.github/workflows/dco.yml-  pull_request:
    --
    origin/master:.github/workflows/dependency-review.yml:on:
    origin/master:.github/workflows/dependency-review.yml-  pull_request:

So `1a18979` is a pull request head where all four refusing guards ran at once,
and those triggers are why one commit carrying a run of all five is not
something this repository can produce.

The four cells above named 31095610752 until this correction, and that run is
none of the four guards:

    $ gh api repos/iderex/jellyfin-plugin-requests/actions/runs/31095610752/jobs --jq '.jobs[].name'
    lines
    floor 12.0.0.0
    floor 10.11.0.0

It is the ABI floor, which ran on the same head within seconds of the four and
carries three jobs where each guard's run carries one. The identifier was read
off the list of runs started by that pull request instead of off each guard's
own check, which is the same shape as reading a working checkout and reporting
it as mainline. The fifth cell carried a commit and no identifier at all. The
claim each cell supported was true both times; what a reader following it landed
in was a different workflow.

### The score is a report, and stays one

#141 asked whether to give the score a floor so that it refuses something, or to
record in writing that it stays a report. It stays a report. The reasoning is
here rather than in the issue alone, because this table is where a reader would
otherwise take the row for a gate.

The score itself, at the head this document was last measured against:

    $ curl -s https://api.securityscorecards.dev/projects/github.com/iderex/jellyfin-plugin-requests \
        -o scorecard.json
    $ python -c "import json;d=json.load(open('scorecard.json'));print(d['repo']['commit'],d['date'],d['score'])"
    0ecd860c9adbb09f074a6f65106fa4219ad9e1c2 2026-08-06T14:27:46Z 6

Three things stand against a floor, and the first is that the condition as
written cannot be built. A floor was to read the aggregate score out of
`results.sarif`. That file does not carry one. From the artifact of the run that
produced the score above:

    $ gh run download 31110915427 --repo iderex/jellyfin-plugin-requests \
        --name 'SARIF file' --dir sarif
    $ grep -ciE '"score"|aggregate|overall' sarif/results.sarif
    0
    $ grep -oiE 'score is [0-9-]+' sarif/results.sarif | sort | uniq -c
          5 score is 0
          1 score is 3
          2 score is 8
          2 score is 9

Ten numbers, one per finding rather than one per check, each inside an English
sentence in a finding's message, and only for the checks that produced a finding
at all. A check scoring 10 emits none, so the aggregate cannot be recovered from
the file either: the weights are not in it and neither are the checks that
passed. The aggregate exists in the published result above, which is a different
source and an external one.

The second is that a floor here could refuse nothing that matters, because this
workflow runs after a merge and never before one:

    $ grep -nE "pull_request|^ +if:|branches:" .github/workflows/scorecard.yml
    13:# Branch-Protection check see the ruleset). No pull_request trigger - that path
    33:    branches: [master]
    56:    if: github.event.repository.default_branch == github.ref_name

No pull request trigger, a push trigger naming the default branch, and a job
guarded to the default branch on top of it. A red run would land on `master`
after the change that caused it was already in. The same two lines are why a
floor could not be proved to bite the way every other guard here was: raising it
on a throwaway branch runs nothing, because no branch but the default one starts
this workflow. Proving it would mean pushing a deliberately failing commit to
`master`.

The third is what the number is made of:

    $ python -c "import json;d=json.load(open('scorecard.json'));[print(c['score'],c['name']) for c in sorted(d['checks'],key=lambda c:c['score'])]"
    -1 Packaging
    -1 Signed-Releases
    0 Code-Review
    0 Maintained
    0 CII-Best-Practices
    0 Contributors
    0 Fuzzing
    0 Security-Policy
    3 Branch-Protection
    8 Pinned-Dependencies
    9 Token-Permissions
    10 Dangerous-Workflow
    10 Dependency-Update-Tool
    10 Binary-Artifacts
    10 License
    10 Vulnerabilities
    10 SAST
    10 CI-Tests

`Fuzzing` is 0 because fuzzing is declined in the table above, by name and with a
replacement. `Contributors` is 0 for a repository with one maintainer and cannot
be anything else. `CII-Best-Practices` is 0 until somebody registers for a badge
elsewhere. `Maintained` is 0 with the reason printed by the run itself, which is
that the repository is younger than ninety days:

    $ grep -oE 'score is 0: project was created within the last [0-9]+ days' \
        sarif/results.sarif
    score is 0: project was created within the last 90 days

That one moves with the calendar rather than with a commit. `Packaging` and
`Signed-Releases` are -1 and enter the mean the day a first release exists,
moving the aggregate without one byte of this tree changing. A floor set just
under 6 today would go red on a morning nobody pushed anything, for a reason
nobody here caused, which is the guard that gets switched off rather than fixed.
That an inactive period lowers `Maintained` again afterwards is a claim about
how the check is scored upstream and is not measured here.

What is left after those three are the checks that are about this tree, and the
ones worth refusing are refused already, on every pull request, by a guard that
has been watched going red. `Audit workflows (zizmor)` runs at
`--min-severity=low`, which covers `unpinned-uses`, `excessive-permissions` and
the dangerous-trigger rules, and run 31041799794 above is that gate refusing
sixteen findings at once. That is the same ground as `Token-Permissions`,
`Dangerous-Workflow` and the actions half of `Pinned-Dependencies`, refused
before a merge instead of scored after one.

The half that is covered by nothing is named rather than left out.
`Pinned-Dependencies` is 8 for two findings that are not about actions, and no
check on this board refuses either of them:

    $ grep -oE 'score is 8: [a-zA-Z]+ not pinned by hash' sarif/results.sarif
    score is 8: downloadThenRun not pinned by hash
    score is 8: nugetCommand not pinned by hash

`Branch-Protection` at 3 is a repository setting rather than a file here, and
what the ruleset should require is #30. `Security-Policy` at 0 is a file this
tree does not have yet, which is #106. Neither is closed by this decision and
neither is a reason for a floor: a floor would report the same absences the
report already reports.

So the workflow keeps its shape, its header says in the file that it refuses
nothing, and #25's second condition is answered for the fifth workflow by there
being nothing to answer: it is not a guard, and the four that are have their red
runs.

## What this document does not do

Nothing here is enforced. No check reads this file, no check compares it against
the workflow directory, and a workflow added or deleted without a row moves
nothing red. The table drifts against the thing it describes the moment either
board changes, and the commands above are the only part that cannot.

The adopted rows that name an issue are decisions, not deliveries. What is
running is the check list at the top, printed from a run, and an adopted row
with an open issue beside it is a workflow this repository has decided it wants
and does not yet have.

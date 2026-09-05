# Contributing

Short on purpose. A contributing document nobody finishes is one nobody follows.

## Sign your work

Every commit carries a `Signed-off-by:` line matching its author:

    git commit -s

That line is an assertion about the change, and what it asserts is the Developer
Certificate of Origin 1.1, in [DCO](DCO) at the root of this repository. Read it
once; it is short and it is the whole of what you are certifying.

The `DCO sign-off` check walks every non-merge commit in a pull request and reds
on any one missing the trailer. A commit that already exists can be fixed
without rewriting what it does:

    git rebase --signoff <base>

## Start with an issue

Every change starts as an issue and lands as a pull request. An issue says what
is wrong, what the evidence is, and what "done" means. If the evidence is a
number, it carries the command that produced it.

A pull request names its issue in the body: `Closes #12`, or `Part of #12` where
it does not finish it. The `Deterministic pull request hygiene checks` job reads
the body for that reference, and since #390 it reads it whoever the author is.

So a first contribution can be refused on a convention nobody could have known,
and what answers that is a hand rather than a skip: say so in the pull request
and the reference is supplied, or the issue filed, by whoever handles the
contribution. The skip it replaces was keyed on `author_association`, a field
that answers differently on the two routes that read it, so which contributions
it reached was not a thing anybody could state. A bot is the only author the
blocking tier still skips, because an automated update has no issue to link.

## What the gate expects

The checks that run, which of them hold a merge, and how the set differs from
the sibling board's, are in [docs/quality-parity.md](docs/quality-parity.md).
That document is the answer; this one does not repeat it, because a list here
would drift against it and a contributor would then have two answers.

What is worth knowing before the first push:

- The build treats warnings as errors, on both target frameworks. A warning on
  either server line fails the build on both.
- The suite runs against both frameworks too. A test that passes on one is half
  a test.
- Formatting of `html`, `css`, `js` and `md` is checked by Prettier. C# is the
  analyzers' job and is not in that set.
- Some rules of this tree are patterns rather than prose, in
  `tools/opengrep/rules.yaml`. Each one carries a fixture it is watched refusing,
  so adding a rule means adding the fixture that proves it bites.
- The documents here argue from commands, and the `path:line:text` lines they
  paste as evidence are read back against the files they name. Edit a file and a
  document quoting a line below the edit goes stale without either of them being
  wrong, so `scripts/check-pasted-evidence.sh` refuses that under the same job.
  Re-run the command above the block and paste what it prints; do not edit the
  number to fit. It says what it cannot read at the top of the script.
- A block ending `; echo "exit=$?"` is a claim that something is not in the tree,
  and `scripts/check-negative-disclosures.sh` re-runs it under the same job. It
  runs `git grep` and nothing else, from a fixed set of options, and a command
  carrying anything a shell would act on is refused rather than run. Write the
  claim in that form and it is checked; write it any other way and it is not.

Run the build and the suite before pushing:

    dotnet build Jellyfin.Plugin.Requests.sln --configuration Release -warnaserror
    dotnet test Jellyfin.Plugin.Requests.sln --configuration Release -warnaserror

Nothing runs those for you before the pull request is open.

## Writing a change

One topic per commit and per pull request. A commit carrying two unrelated
changes has a message describing one of them.

A commit message says what changed and what failure it prevents. Where it is a
correction, it says what was wrong and how that was found.

Tests are not optional for a change to behaviour, and a guard is expected to
have been watched failing: delete it, run the suite, see it red, put it back.
A test that could not have failed proves nothing.

## Reporting a security problem

Not here. [SECURITY.md](SECURITY.md) has the private route, and a security
problem does not go in a public issue.

## How people are expected to behave

[CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

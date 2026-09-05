# Releasing

A release is published by pushing a tag. Nothing is created by hand.

## The tag

The tag has the form `X.Y.Z-stable` or `X.Y.Z.W-stable`, for example `1.4.0-stable`
or `0.1.0.0-stable`. The numeric part is the plugin version that Jellyfin installs,
and it must be exactly the `version` in the packaging file the tag selects, written
the same way, with the same number of parts. The `-stable` suffix lives only in the
tag and in the release name.

### One tag per server line, and the number is the same on both

Decided on #110 on 2026-08-28. The number is the release and the suffix is the line:
`0.3.0.0-stable` and `0.3.0.0-jf12-stable` are the same version of the same plugin
packaged for two lines, and neither of `MAJOR`, `MINOR` or `PATCH` is spent on saying
which line a package is for.

| Tag                     | Packaging file      | What is published        |
| ----------------------- | ------------------- | ------------------------ |
| `X.Y.Z.W-stable`        | `build.yaml`        | the line that file names |
| `X.Y.Z.W-<line>-stable` | `build-<line>.yaml` | that line                |

The marker is not a list in this document or in the workflow. What sits between the
number and `-stable` names a packaging file, the run refuses a tag whose file does not
exist and prints the files that do, and adding a line is adding a packaging file. Both
lines are released by pushing both tags, one at a time.

Each release still carries exactly one archive, which is what a catalogue generator
requires to pair an archive with its checksum without breaking a tie it did not choose,
and it is why two lines are two releases rather than one release with two packages.

## Cutting a release

1. Check that `Works alone, works with the sibling set` is green on the commit you
   are about to release, on both claimed lines. It boots a server of each line with
   this plugin alone and again with the supported sibling set installed, and scans
   for collisions over routes, scheduled task names and plugin configuration. A red
   matrix is not released around: either the collision is fixed, or the
   incompatibility is written into [compatibility.md](compatibility.md) as a known
   limitation with its reason, and then this is green because the set no longer
   claims what it cannot do.

    **Nothing enforces this step.** The workflow reports and holds no merge and no
    tag, so what stands between a collision and a release is somebody reading this
    line. Which contexts hold anything here is a branch ruleset setting rather than
    a file in this tree, and it is #30.

    The run also prints which boards were in the set and which were skipped for
    having published nothing on that line. Read it: green over one board is not
    green over the family.

2. Update `version` in `build.yaml` on the release branch and merge it.
3. Check that the commit you want to release is on that branch.
4. Push the tag for that commit:

    ```
    git tag 1.4.0-stable <commit>
    git push origin 1.4.0-stable
    ```

The `Publish Release` workflow takes it from there.

Push one tag at a time and wait for its run to finish. GitHub keeps at most one
queued run per concurrency group, and although the group here is keyed on the tag,
serialising them by hand is what keeps the release order readable.

## What the run produces

The workflow builds the plugin from the tagged commit, creates the GitHub release
for the tag, and attaches five files:

- the plugin archive
- the packaging metadata written beside it, `<archive>.zip.meta.json`
- the bill of materials, `<archive>.cdx.json`
- one `.md5` file, the checksum of the archive
- one `.sha256` file for the same archive

The `.md5` is the value a Jellyfin catalog serves as the plugin checksum. There is
exactly one per release so that no generator can pair a checksum with the wrong
file. The archive, the metadata and the bill of materials are each checked for
existence by name before the release job runs, so a release with four of the five
files is not a state this route can reach.

The run also signs a build provenance statement for the archive, in a separate job
that downloads the archive and runs no build tooling.

Nothing here writes a plugin catalog. A GitHub release is the whole output. If this
repository previously published through the Jellyfin meta plugins workflow, that path
is gone, and no catalog is fed by this route. The generator that would write one now
exists and this route does not call it; the section below says what it does and what
is still missing between it and an operator adding a repository.

## The manifest

A Jellyfin server does not install from a GitHub release. It fetches a manifest,
finds the newest entry whose `targetAbi` its own version accepts, downloads the
`sourceUrl` and checks the download against the entry's `checksum`.

`scripts/build-manifest.sh` writes that document from built packages. Every field
except the checksum is copied out of the `<archive>.zip.meta.json` the packaging tool
writes beside each archive, so the entry describes the package that shipped rather
than a file read back out of the tree, and the checksum is the MD5 of the archive's
own bytes. `MANIFEST_BASE` names the manifest already published, so a release adds
its versions to it instead of replacing them.

Run over the release that exists today:

```
gh release download 0.1.0.0-stable --repo Flowfin/jellyfin-plugin-requests
SOURCE_URL_PREFIX=https://github.com/Flowfin/jellyfin-plugin-requests/releases/download/0.1.0.0-stable/ \
  scripts/build-manifest.sh manifest.json requests_0.1.0.0.zip
```

The output is reproducible: the entries are sorted by version, and there is no build
timestamp and no serial number in it, so two runs over the same packages produce the
same bytes and can be compared with `diff`.

**What it refuses is the pair of packages a server cannot tell apart.** A server
keeps every entry whose `targetAbi` is at or below its own version and then takes the
highest version number of what is left, so the version number is the only thing
separating two entries it has already accepted. Two entries at one version are one
entry to it, and an entry with a higher version and a lower `targetAbi` is what a
newer server takes in preference to the build meant for it. Both are refused rather
than written, and `scripts/prove-manifest-refusals.sh` drives one manifest per defect
and asserts each is refused for its own reason, with a clean pair beside them that has
to pass.

**AN OPERATOR CAN ADD A REPOSITORY NOW, AND THIS PARAGRAPH SAID TWO THINGS STOOD
BETWEEN THEM AND IT.** What stood here said the release route publishes one package for
the one line `build.yaml` names and does not call the generator, and that both packaging
files declare one version number, which is the pair the generator refuses.

The first half went with #319: the route derives the server line from the tag suffix and
publishes a package per line, and `0.3.0.0-stable` and `0.3.0.0-jf12-stable` are the two
releases that exist. The second half is not a defect of the document an operator adds,
because this board does not write that document. The hub at `flowfin.dev` builds it from
the releases, one entry per server line, and breaks a tie between two entries at one
version by their timestamps, which is a rule the generator here has no way to apply to a
document it writes on its own. The address is in [catalogue.md](catalogue.md).

Which entry a server of each line then takes is watched rather than reasoned about:
`scripts/verify-manifest-install.sh` adds the address to a server of each claimed line and
reads back the entry the server chose, `.github/workflows/manifest-install.yaml` runs it
daily, and the refusals job beside it serves doctored manifests to the same check and
watches it say no. What that leaves untouched is the generator's own rule, which is right
for the document it writes: two entries at one version, in a manifest with nothing to order
them by, are one entry to a server.

## The bill of materials

`scripts/bill-of-materials.sh` writes it, in the build job, before anything is
published. It is a CycloneDX document listing every file the archive carries with the
SHA-256 of the bytes that get written on install, and it is derived from the archive
rather than from the project.

Two things it does not carry, both on purpose. It reads no version out of a DLL,
because that needs a .NET toolchain and the version a catalog serves is already in
the archive's own `meta.json`. It is not the compile-time dependency graph either:
`packages.lock.json` at the source commit is that graph, and a package in it appears
in the bill of materials only if the build copied it into the archive. For
`0.1.0.0` the archive holds two files and nothing third-party ships, so the list is
two entries long. That is a fact about the package rather than a gap in the script.

## Checking a release

Both checks are for somebody who downloaded the archive and wants to know what they
have, and neither needs anything from this repository beyond the script.

Where the archive came from:

```
gh attestation verify <archive>.zip --repo <owner>/<repository>
```

What is inside it. The document is reproducible, so regenerating it from the download
and comparing is the check. There is no timestamp and no serial number in it, and the
components are sorted, so two runs over the same archive produce the same bytes:

```
SOURCE_REPOSITORY=<owner>/<repository> \
SOURCE_COMMIT=<the commit in the shipped document> \
PACKAGE_VERSION=<version> \
  scripts/bill-of-materials.sh <archive>.zip regenerated.cdx.json
diff regenerated.cdx.json <archive>.cdx.json
```

A recorded run of both commands against `0.2.0.0-stable`, which is the first release
the publish route produced a bill of materials for:

```
gh release download 0.2.0.0-stable --repo Flowfin/jellyfin-plugin-requests \
  --pattern 'requests_0.2.0.0.zip' --pattern 'requests_0.2.0.0.cdx.json'
gh attestation verify requests_0.2.0.0.zip --repo Flowfin/jellyfin-plugin-requests
echo "exit=$?"
exit=0
```

```
SOURCE_REPOSITORY=Flowfin/jellyfin-plugin-requests \
SOURCE_COMMIT=60faf415328e88461656d4c245e093e357883983 \
PACKAGE_VERSION=0.2.0.0 \
  scripts/bill-of-materials.sh requests_0.2.0.0.zip regenerated.cdx.json
bill-of-materials: wrote regenerated.cdx.json describing 2 file(s) from requests_0.2.0.0.zip.
diff regenerated.cdx.json requests_0.2.0.0.cdx.json
echo "exit=$?"
exit=0
```

The commit to put in `SOURCE_COMMIT` is the one the shipped document names, which is
read out of it rather than guessed:

```
python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["metadata"]["component"]["externalReferences"][0]["comment"])' \
  requests_0.2.0.0.cdx.json
built from commit 60faf415328e88461656d4c245e093e357883983
```

The first command prints its result only to a terminal, so a run whose output is
captured to a file or a pipe shows the exit status and nothing else. Reading the
statement itself rather than the verdict takes `--format json`. Pointed at a
repository that did not build the archive it exits 1 with an HTTP 404, which is the
failing direction of the same command.

The second check was watched failing in both of the directions it exists for, on the
same archive. A file inside the archive changed by one byte:

```
unzip -q requests_0.2.0.0.zip -d tampered && printf '\n' >> tampered/meta.json
( cd tampered && zip -q -r ../tampered_0.2.0.0.zip . )
SOURCE_REPOSITORY=Flowfin/jellyfin-plugin-requests \
SOURCE_COMMIT=60faf415328e88461656d4c245e093e357883983 \
PACKAGE_VERSION=0.2.0.0 \
  scripts/bill-of-materials.sh tampered_0.2.0.0.zip tampered.cdx.json
diff tampered.cdx.json requests_0.2.0.0.cdx.json
26c26
<           "content": "2e32254bbf780ae1de376734a16370be43b0de0bd2cc11cf9ceda6edad3a94f6"
---
>           "content": "62293889c33fe2ab3551336f50a6b0280f43bb3e97fda0eb4688990b923f21fc"
33c33
<           "value": "1205"
---
>           "value": "1204"
echo "exit=$?"
exit=1
```

and a regeneration claiming a different source commit:

```
SOURCE_COMMIT=bcee7a79feb7f31ae6e1d7441e9e20d4853dacc1 ... \
  scripts/bill-of-materials.sh requests_0.2.0.0.zip wrong-commit.cdx.json
diff wrong-commit.cdx.json requests_0.2.0.0.cdx.json
44c44
<           "comment": "built from commit bcee7a79feb7f31ae6e1d7441e9e20d4853dacc1"
---
>           "comment": "built from commit 60faf415328e88461656d4c245e093e357883983"
echo "exit=$?"
exit=1
```

The tampered run also differs on the archive's own name, because the name is a field
of the document. That is worth knowing before somebody renames a download and reads
the two name lines as a finding: compare a download under the name it was published
under.

`0.1.0.0-stable` was built before the bill of materials existed and carries no
`.cdx.json`, so the second check still has nothing to run against for that release
and never will. A release's assets are not touched again on this route, and a
document written by hand afterwards is the thing the first check exists instead of.
The first check runs against it unchanged.

## What fails the run

- The tag does not end in `-stable`, or the workflow was started from something
  other than a tag.
- The tag names a server line with no packaging file, so there is nothing to publish
  for it.
- The numeric part of the tag differs from `version` in the packaging file it selects.
- That file is missing a required field, or `version`, `targetAbi`, `framework` or
  `guid` has the wrong shape.
- `framework` in that file names a target the plugin project is not built for.
- A packaging manifest that shadows `build.yaml` is present, such as `jprm.yaml` or
  `meta.yaml`.
- That file declares an `image` file that is not in the repository.
- The tagged commit is not contained in a release branch, or the tag was moved after
  the run started.
- There is no `packages.lock.json` next to the plugin project, so the release build
  cannot restore against a reviewed dependency graph. Create one with
  `dotnet restore <project> -p:RestorePackagesWithLockFile=true` and commit it.
- The version stamped into the assembly is not the version in `build.yaml`.
- The build produced no archive, or more than one, or no packaging metadata, or no
  bill of materials.
- The archive cannot be read, or carries no files at all, so nothing can describe
  what ships in it.
- A release already exists for the tag.

All of these fail before anything is published.

## What the run notes without failing

The packaging tool warns when `build.yaml` declares neither `image` nor `imageUrl`.
The plugin then shows without a logo in a catalog. That is a warning on every run
until a logo exists, and it is not a reason to hold a release.

## Re-running

A release that exists is not touched again. The release job asks whether a release
exists for the tag before it writes anything and stops if one does, and the upload
step is configured not to replace an asset of the same name. Replacing the bytes of a
version people have already installed is the failure this prevents, and it is worth
more than the convenience of a re-run.

So: if a release went out with the wrong contents, fix the problem, raise the version
in `build.yaml`, and push a new tag.

If a run failed **before** the release was created, the tag is still clean. Fix the
cause and re-run the workflow from the Actions page, or delete and re-push the tag.

If a run failed **after** the release was created but before every asset was attached,
the release is incomplete and a re-run will refuse it. What is possible then depends
on the repository settings below. Without immutable releases you can delete the
incomplete release, delete the tag, and push it again. With immutable releases you
cannot, and the version has to be raised.

## Repository settings this expects

- Default workflow permissions set to read only.
- A rule that restricts who may push `*-stable` tags.
- The `ABI floor build` check required on the release branches.
- Immutable releases, if the repository wants the guarantee that a published release
  can never be edited or deleted at all. The workflow does not depend on it: the
  refusal to touch an existing release is enforced in the release job. Turning it on
  removes the only recovery path for an incomplete release, so try it on one
  repository and cut a release there before turning it on everywhere.

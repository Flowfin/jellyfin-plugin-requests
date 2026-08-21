# Releasing

A release is published by pushing a tag. Nothing is created by hand.

## The tag

The tag has the form `X.Y.Z-stable` or `X.Y.Z.W-stable`, for example `1.4.0-stable`
or `0.1.0.0-stable`. The numeric part is the plugin version that Jellyfin installs,
and it must be exactly the `version` in `build.yaml`, written the same way, with the
same number of parts. The `-stable` suffix lives only in the tag and in the release
name.

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
    a file in this tree, and it is #107.

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
is gone and no catalog is fed until a manifest generator is added.

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

A recorded run of the first command against `0.1.0.0-stable`, which is the release
that exists at the time of writing:

```
gh release download 0.1.0.0-stable --repo Flowfin/jellyfin-plugin-requests \
  --pattern 'requests_0.1.0.0.zip'
gh attestation verify requests_0.1.0.0.zip --repo Flowfin/jellyfin-plugin-requests
echo "exit=$?"
exit=0
```

The command prints its result only to a terminal, so a run whose output is captured
to a file or a pipe shows the exit status and nothing else. Reading the statement
itself rather than the verdict takes `--format json`. Pointed at a repository that did
not build the archive it exits 1 with an HTTP 404, which is the failing direction of
the same command.

`0.1.0.0-stable` was built before the bill of materials existed and carries no
`.cdx.json`, so the second check has nothing to run against until the next release.

## What fails the run

- The tag does not end in `-stable`, or the workflow was started from something
  other than a tag.
- The numeric part of the tag differs from `version` in `build.yaml`.
- `build.yaml` is missing a required field, or `version`, `targetAbi`, `framework`
  or `guid` has the wrong shape.
- `framework` in `build.yaml` names a target the plugin project is not built for.
- A packaging manifest that shadows `build.yaml` is present, such as `jprm.yaml` or
  `meta.yaml`.
- `build.yaml` declares an `image` file that is not in the repository.
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

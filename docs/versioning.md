# Versioning

Two numbers have to move together and mean the same thing: the version the
assembly reports, which is what a server shows an operator, and the version in
the packaging metadata, which is what an update check compares. The template
this repository came from set the first to `0.0.0.0` and the second to
`1.0.0.0`, so the drift was there on the first day.

## One number, and what refuses a second

`PluginVersion` in `Directory.Build.props` is the number. `Version`,
`AssemblyVersion` and `FileVersion` are set from it, and both packaging files
repeat it, because a JPRM metadata file is read by the release path rather than
compiled and there is nothing in it that can reference an MSBuild property.

A repeat is a thing that can be forgotten, so a test refuses a disagreement.
`PackagingMetadataTests.PluginVersionMatchesThePackagingMetadata` reads the
version out of the assembly under test and out of each packaging file as the
release path reads it, and `BothPackagesClaimTheSameIdentity` refuses the two
packages diverging on `name`, `guid` or `version`.

It bites, and this is what it looked like when it did. The number was raised in
`Directory.Build.props` and in neither packaging file:

    $ dotnet test --nologo
    Failed Jellyfin.Plugin.Requests.Tests.PackagingMetadataTests.PluginVersionMatchesThePackagingMetadata(packagingFile: "build.yaml")
       Assert.Equal() Failure: Values differ
    Expected: 0.1.0.0
    Actual:   1.0.0.0
    Failed Jellyfin.Plugin.Requests.Tests.PackagingMetadataTests.PluginVersionMatchesThePackagingMetadata(packagingFile: "build-jf12.yaml")
       Assert.Equal() Failure: Values differ
    Expected: 0.1.0.0
    Actual:   1.0.0.0
    Failed!  - Failed:     2, Passed:    15, Skipped:     0, Total:    17 (net10.0)
    Failed!  - Failed:     2, Passed:    15, Skipped:     0, Total:    17 (net9.0)

Both packaging files named, one failure each, on both claimed lines. With the
two files carried along, the same command is green:

    $ dotnet test --nologo
    Passed!  - Failed:     0, Passed:    17, Skipped:     0, Total:    17 (net9.0)
    Passed!  - Failed:     0, Passed:    17, Skipped:     0, Total:    17 (net10.0)

What this does not do is make the packaging metadata read the number. It refuses
a disagreement rather than removing the copy, and the copy is still three files.
Removing it would mean generating the packaging files during the release, which
is the release path's shape and belongs with #108 and #110 rather than here.

## The scheme

Four parts, `MAJOR.MINOR.PATCH.0`, because an assembly version is a four-field
`System.Version` and a shorter one prints shorter and stops matching the string
in the packaging file. The fourth field is always `0`. It is not a build counter
and nothing sets it; a build that needs to be distinguished from another build
of the same source is a question for the release path and not for this number.

What each part means for somebody who already has the plugin installed:

`MAJOR` moves when something they rely on is taken away or changes shape.
Concretely, on this plugin: a route or a response field of the request API
removed or given a different meaning, a stored request that this version can
read and the previous one cannot, a claimed server line dropped, or a
configuration key removed or reinterpreted. An operator reading a `MAJOR` bump
is being told to read the changelog entry before updating.

`MINOR` moves when behaviour is added and everything that worked still works. A
new endpoint, a new field in a response, a new setting whose default leaves the
plugin doing exactly what it did.

`PATCH` moves when a defect is fixed and nothing is added. No new endpoint, no
new field, no new setting.

While `MAJOR` is `0` the promise is weaker, and it is worth saying plainly
rather than leaving it to convention: below `1.0.0.0` a `MINOR` bump is allowed
to carry a change that would otherwise be a `MAJOR` one, and the changelog entry
says so. That state ends at the first `1.0.0.0`, and what compatibility is
promised from then on is #105.

## Where the number starts

`0.1.0.0`. The template's `1.0.0.0` claimed a released first version, and
nothing has been released:

    $ gh release list --repo iderex/jellyfin-plugin-requests --json tagName --jq 'length'
    0

A version that says 1.0 to a catalogue, to a dashboard and to an operator, for a
plugin with no request API and no store behind it, is a claim this tree cannot
back.

## What a bump does to an installed server, and what is not measured here

What this repository promises is the paragraph above: the part that moved says
what kind of change it was. What a server does with that is the server's
behaviour, and this document does not measure it. That a Jellyfin server
compares the version in a manifest entry against the installed one and offers
the higher of the two is a claim taken from how the catalogue is used, not from
a run recorded here, and the run that would settle it belongs with #110.

Two of the promises above also need code that does not exist yet. A stored
request that an older version cannot read is refused by the version marker in
the store, which is #47 and is not built, and configuration carried between
plugin versions is #97. Until those land, a `MAJOR` bump is a sentence in a
changelog and nothing refuses a downgrade that eats data.

## The changelog

`CHANGELOG.md` at the root, one section per released version, newest first, and
an `Unreleased` section at the top that collects what has landed since the last
release. A version bump and a changelog entry are one change: raising
`PluginVersion` without writing what moved leaves an operator with a number and
no reason for it.

Nothing refuses that today. No check reads `CHANGELOG.md`, and a commit raising
`PluginVersion` with the file untouched is green everywhere. #26 is the hygiene
check that would refuse it, and #107 stays open on that condition until it
lands.

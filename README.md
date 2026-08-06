# Jellyfin Requests

Media requests for Jellyfin as first-class server objects. A user asks the
server for a film or a series it does not have, an administrator sees the ask in
a queue and answers it, and the request keeps its own state and history on the
server rather than in somebody's chat log.

## This is not finished

There is no release, no package and nothing to install. Most of the plugin does
not exist yet: what the tree holds is the scaffolding a Jellyfin plugin starts
from, with the build retargeted at the two server lines below. Nothing here
should be pointed at a server you care about.

The plan is on the issue tracker, cut into milestones. Each issue says what is
wrong, what the evidence is and what has to be true for it to be closed.

## Server lines

Two server generations are claimed, and each gets its own package because a
plugin compiled for one runtime does not load on the other.

| Line           | Runtime | Packaging metadata | Oldest server claimed |
| -------------- | ------- | ------------------ | --------------------- |
| Jellyfin 10.11 | .NET 9  | `build.yaml`       | 10.11.0.0             |
| Jellyfin 12.0  | .NET 10 | `build-jf12.yaml`  | 12.0.0.0              |

Those numbers are the `targetAbi` and `framework` fields of the two files named
in the table, and they are what the project file multi-targets against.

An assembly built for each line has been installed on a server of that line and
reported `Active` by the server itself. The transcript of that run, the images
it ran against and what it does not cover are in
[docs/testing.md](docs/testing.md). What has not been tried is the packaged
install path an operator would use, which is a later milestone.

## Building

```
dotnet build
```

With no framework argument that builds both targets. The .NET 10 SDK builds
both, and a machine with only the .NET 9 SDK has not been tried.

## License

GPL-3.0. The full text is in [LICENSE](LICENSE), and it is the authority for the
terms, the warranty disclaimer and the limitation of liability.

Jellyfin plugins are linked against GPLv3 server code, so a plugin distributed
to others has to be under the GPLv3 or a permissive license compatible with it.

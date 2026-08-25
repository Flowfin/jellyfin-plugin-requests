# Changelog

One section per released version, newest first, and an `Unreleased` section
holding what has landed since the last one. The scheme the numbers follow, and
what each part of a number means for somebody who has this plugin installed, is
in [docs/versioning.md](docs/versioning.md).

Entries say what changed for somebody using the plugin. A change that alters
nothing an operator or a user can observe, such as a workflow or a test, does
not need an entry; the git history is where that is read.

## Unreleased

Nothing has been released. There are no tags and no packages, so everything in
the tree is unreleased and this section starts here rather than reconstructing
what came before it. What landed before this file existed is in the git history,
where it carries the pull request and the issue it came from, and rewriting it
from memory into entries would be a description nobody measured.

- Whether the title somebody asked for has arrived is now answered for them
  rather than for the server. Their own list asks the library on their behalf, so
  a person restricted by a parental rating or without access to the library a
  file sits in is told the same thing about that title as somebody whose server
  does not have it. Until now the row carried what the server holds, which said
  that a library they cannot open has something in it. The lookup is made only
  for the rows on the page being returned.

- The person who asked is told when their own request is answered. Approving,
  declining or fulfilling a request pushes one message to that person's
  signed-in clients and to nobody else's, carrying the title and, on a decline,
  the reason. It reaches whoever is connected at that moment and nothing
  remembers who was missed, so the answer to rely on is still their own page,
  which shows the state whenever they next look. Nothing leaves the machine, and
  the person it is about can turn it off, which is the entry below.

- A person can turn off the message this plugin pushes them about their own
  request. The switch is a checkbox on their own requests page and it is theirs
  alone: no setting an operator can reach overrides it, and nothing an
  administrator can call changes anybody's but their own. It is on by default, so
  a server that upgrades into this behaves exactly as it did before, and what is
  kept is a small file of the people who said no rather than a row per person. A
  setting that cannot be read leaves the message unsent rather than sending it,
  and says so in the server log.

- `FinishedRequestRetentionDays` is enforced. A scheduled task removes a request
  that has been fulfilled, declined or failed for longer than that number of
  days, counted from the move that finished it, and it runs daily and at
  startup. A request nobody has answered is never removed by age. Until now the
  number was a setting nothing acted on and a server kept every request.

- A person signed in to the server can open a page in a browser and see what
  they asked for and what happened to it, at `MediaRequests/v1/Page`. It shows
  their own requests only, offers no decision, and is refused to a caller with
  no session. What it costs to open one in a browser, which is a credential in
  the address, is in [docs/surface.md](docs/surface.md).

- The version starts at `0.1.0.0`. It was `1.0.0.0`, inherited from the
  template, which claimed a released first version of a plugin that has never
  been released.

# How a user reaches this plugin

## The decision

Three surfaces, and they are not alternatives to each other.

The **API** is the floor. Everything else in this repository is built on it and it reaches anything
somebody scripts. On its own it reaches nobody else, so it is not an answer to this question.

A **channel** is the surface for clients this project cannot change. It puts a browsable folder tree
beside the user's libraries, on every client that renders channels, and it needs no change to any
client and no cooperation from anybody.

A **page served by the plugin** is the surface for browsers. It is one page, it is opened by a user
who is already signed in, and it can show and do things a folder tree cannot.

Writing placeholder items into a real library is rejected outright. See below.

## What was rejected, and why

### Placeholder items in a real library

Rejected. It reaches everything a channel reaches, by writing rows that are not media into somebody's
film library. That is not a cost this plugin pays, it is one the operator pays forever: the rows are
in their library, they appear in their searches and their collections, and removing the plugin does
not obviously remove them. A plugin that leaves debris in the thing it was installed to help is not
worth the reach it buys.

### The API on its own

Rejected as an answer and kept as the floor. A user who has asked for something and wants to know
what happened is not going to write a script. Milestone 8 exists because that user has no way to ask
and no way to look, and an interface only a programmer can use leaves them exactly where they were.

### The channel on its own

Rejected. A channel renders a folder tree, and a folder tree is a poor shape for the two things a
browser can do well: showing a decline reason as a sentence, and cancelling something. The page costs
one page and the channel does not replace it.

### The page on its own

Rejected, and this is the one worth spelling out because it is the cheap answer. It costs one page
and it strands every user who is not at a browser, which on a media server is most of them. This
milestone is called the user surface every client can reach; building the browser surface and calling
the rest future work is how that title stops being true without anybody deciding it should.

## What the channel costs

It is the expensive choice and it is chosen with its costs open.

The plugin takes a place in the server's library database, because that is how the server holds what
a channel returns. That is a second thing an uninstall has to clean up, which is #98.

The rendering is per user, and a request is personal data. A user's requests must not be visible to
another user through anything this plugin puts in that database. That is #67, it is the failure that
matters most on this milestone, and it is not treated here as a detail of the implementation:

**If per-user isolation cannot be shown on a running server of each claimed line, the channel falls
back to a shape that carries no per-user data at all.** That is #67's own third condition and it is
repeated here because this page is where the decision lives. A channel that leaks one user's requests
to another is worse than no channel, and the fallback is the answer rather than a softer test.

What a channel renders is items, and a request is not an item of media. The rows will be titles a
person asked for, with their state, and pressing play on one does nothing. That is a real awkwardness
and it is the price of reaching a client nobody here can change. It is not the same defect as writing
placeholders into a real library, because a channel's folder is the plugin's own and disappears with
it.

## What is not reached

Nothing here is softened, and the parts that are claims rather than measurements say so.

**Nothing here has been measured against a client.** The reach matrix is below and it opens with
the sentence that says so; every cell of it that says a user could do something carries the word
untested. Which client families render a channel, and how, is a claim taken from the plan rather
than a thing anybody here has run. Do not read the section above, or the table below, as a
measurement of reach.

**A client with no browser cannot open the page.** That is what the page is: a document served over
HTTP to a signed-in session. A client that draws its own interface and offers no way to open a URL
gets nothing from #69.

**A client that does not render channels gets nothing from the channel**, and there is no fallback
for it inside this plugin beyond the API.

**On a server with no browsing sibling installed, there is no way to ask for anything from a
television client at all.** This is the sharpest one and it is certain rather than a claim about
clients. This plugin ships no way to find a title the server does not have, decided on #113, because
the sibling discover plugin owns the catalogue and this plugin calls no metadata source, decided in
#92. So on such a server the channel can show a user what they have already asked for and can offer
nothing to ask with. The gesture that creates a request arrives from the sibling, which is #68 and
#89.

## The reach matrix

No cell of the reach matrix in docs/surface.md has been checked against a real client, and neither
the channel nor the page it describes is on the mainline today.

That sentence is the first thing in this section because the table under it looks like a
capability list and is not one. It is what the decision above would reach if it were built, written
down so that a user on a client that renders none of it can find that out here instead of by trying.
The same sentence is in `README.md`, word for word, so the two cannot drift apart quietly. Line
breaks differ because the two files wrap at different widths, so the comparison collapses whitespace
before it looks. Each file becomes one line, so the count is 1 where the sentence is present and 0
where a word of it has moved:

    for f in README.md docs/surface.md; do printf '%s: ' "$f"; tr -s '[:space:]' ' ' < "$f" \
      | grep -c 'No cell of the reach matrix in docs/surface.md has been checked against a real client, and neither the channel nor the page it describes is on the mainline today.'; done
    README.md: 1
    docs/surface.md: 1

The rows are client families grouped by what draws the interface, not by vendor, because the two
things that decide reach here are whether the client renders a channel and whether it can open a
URL. Two clients that share both answers share a row.

| Client family                               | See their own requests     | Ask for something new  | Cancel one they asked for |
| ------------------------------------------- | -------------------------- | ---------------------- | ------------------------- |
| Browser                                     | page and channel, untested | sibling only, untested | page, untested            |
| Desktop client wrapping the web interface   | page and channel, untested | sibling only, untested | page, untested            |
| Android phone and tablet                    | channel, untested          | sibling only, untested | nothing                   |
| Android TV and Fire TV                      | channel, untested          | sibling only, untested | nothing                   |
| iPhone and iPad                             | channel, untested          | sibling only, untested | nothing                   |
| Apple TV                                    | channel, untested          | sibling only, untested | nothing                   |
| Roku                                        | channel, untested          | sibling only, untested | nothing                   |
| LG webOS television                         | channel, untested          | sibling only, untested | nothing                   |
| Samsung Tizen television                    | channel, untested          | sibling only, untested | nothing                   |
| Kodi                                        | channel, untested          | sibling only, untested | nothing                   |
| A script or another program against the API | the API                    | the API                | no route today            |

What the cells mean.

`untested` means nobody in this repository has opened that client and looked. It is not a
prediction that the cell works; it is the admission that it has not been tried, and it stands on
every cell that says a user could do something on a client. When a cell is checked against a real
client, the word is replaced by the client and the version it was checked on, so a checked cell and
an untested one can never be read as the same thing.

The cells that say `nothing` or `no route today` are the other kind and are not marked. They say
what does not exist, which no client can contradict, and writing them the same way as a claim about
a client would hide the difference between something nobody has tried and something nobody has
built.

`page and channel` is the browser rows, which reach both surfaces. `channel` is every client that
renders a channel, and whether a given client does is exactly what has not been tried.

`sibling only` is the sharpest cell and the one that is certain rather than untested. This plugin
ships no way to find a title the server does not have, so there is no gesture here to make. On a
server with the browsing sibling installed the want arrives through the seam; on a server without
it, the answer in that column is nothing, on every row above the last. What is untested there is
whether the sibling draws anything on that client, which is that board's measurement and not this
one's.

`nothing` in the cancel column is the cost of the folder tree. Cancelling is a per-state operation
with a reason a person reads, and a channel renders items, so the gesture has nowhere to live there.
A user on a television can see that they asked for something and cannot take it back from that
client.

The last row is not a client family and is in the table because leaving it out would make the API
look like it is not reachable. It is the floor under every other row, it reaches whoever writes
against it, and it reaches nobody else. Its three cells are read off the routes rather than from
the plan, which is also where its `no route today` comes from: nothing on this surface cancels
anything, and what cancelling will mean per state is #68.

    git grep -oh 'Http\(Get\|Post\)("[^"]*")' -- Jellyfin.Plugin.Requests/Api/RequestsController.cs
    HttpPost("Requests")
    HttpGet("Requests")
    HttpGet("Requests/Queue")
    HttpPost("Requests/{id}/Approve")
    HttpPost("Requests/{id}/Decline")

## What the rest of this milestone follows from

Every issue after #65 in milestone 8 is read against the decision above.

- #66, the user's view of their own requests on a client this project has never touched, is the
  channel rendering.
- #67, proving one user cannot see another's requests through the surface, is the channel's
  per-user rendering, with the fallback stated above.
- #68, what gesture creates a request from this side, is unchanged: the gesture arrives through the
  seam and this plugin draws nothing anybody clicks.
- #69, serving a page for browsers, is the page.
- #70, what a user sees when the answer is no or not yet, is written once and rendered by both.
- #71, never revealing a title a user is not allowed to see, applies to both.
- #72, the reach matrix, is the table above. It is the shape of the measurement this page says it
  does not have, with every cell still saying so.
- #73, the localisation catalogue, covers the strings both surfaces show.

None of them assumes a surface other than these, and none is closed as not wanted.

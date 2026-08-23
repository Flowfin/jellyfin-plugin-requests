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

## The channel, as it is built

`Jellyfin.Plugin.Requests/Surface/RequestsChannel.cs`. The server resolves its channels out of the
container this plugin registers into, so the registration is what puts a folder tree beside a
person's libraries on a client nobody here can change.

The root is one folder per state that person actually has something in, in the order somebody reads
rather than the order the states are stored in: what is waiting, then what was approved, then what
arrived, then what was refused, then what could not be obtained. A state they hold nothing in is not
a folder, because an empty folder in a tree is a thing somebody opens for no reason. Inside a folder
are the titles, newest movement first.

Every word comes out of the same catalogue the page reads, by the same key, so the two surfaces
cannot drift into two answers. A row that is waiting carries the sentence that says nobody has
answered it yet and that asking again does not move it, which is the message this plugin exists to
remove. A row that was refused carries the reason and whatever the operator wrote beside it.

Somebody who has never asked for anything gets one folder carrying the sentence that says so, and
opening it is answered with nothing rather than with an error. An empty tree is indistinguishable
from a plugin that has stopped working, and a folder that raises when it is opened is worse than
either.

**No row names anybody, including the person reading it**, and no row carries a provider
identifier. The first is the same rule the endpoint underneath keeps, one layer further out: these
rows are written into the server's own library database, where this plugin no longer decides who
reads them. The second is what keeps a record that somebody asked for something from being matched
against real media by the server, which is the awkwardness this page accepts rather than the
placeholder rows it rejected outright.

**What the channel implements for the cache and what that does not buy.** It carries
`IHasCacheKey`, and the key is the person plus the moment the store last moved. A channel without
one derives a single cache path for every user on the server, which for a view of one person's
requests is the failure this milestone cares about most. That repairs the cache path and nothing
else: the items a channel returns are written under a parent belonging to the channel rather than to
the caller, and the server removes everything under that parent which the current caller's answer
did not contain. Whether two callers arriving in turn can see each other's rows is a property of a
running server, it is #67, and nothing in this repository answers it.

**The channel is built before this plugin is, and that is measured rather than supposed.** It is
worth writing down because it is a trap for anything else this plugin registers. The host resolves a
channel while it is still starting, from `ApplicationHost.SetStaticProperties`, and at that moment
the plugin instance the store's data directory comes from does not exist. A channel that took the
store therefore took the server down with it, on both claimed lines, at the startup wizard. Read out
of the server's own log on a run made to print it:

    System.InvalidOperationException: The request store was asked for before this plugin was loaded, so there is no data directory to keep requests in.
       at Jellyfin.Plugin.Requests.PluginServiceRegistrator.<>c.<RegisterServices>b__0_0(IServiceProvider provider)
       ...
       at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteRuntimeResolver.VisitIEnumerable(IEnumerableCallSite enumerableCallSite, RuntimeResolverContext context)
       ...
       at Emby.Server.Implementations.ApplicationHost.SetStaticProperties()
       at Emby.Server.Implementations.ApplicationHost.InitializeServices(IConfiguration startupConfig)

The elisions are the container's own frames and are marked. So the channel asks for the store when
somebody browses rather than holding one, and the token the server caches on answers the same as a
store nothing has been written to where there is no store to build yet.

**Nothing here has been browsed from a client and nothing has been run against a server.** What is
held is the answer this plugin hands the server, which is what the suite asserts. That the plugin
still loads at all, with this channel registered, is watched on a real server of each line by the
checks that install it.

## The page, as it is built

Both are on the mainline now, and this section is about the browser one.

It is served by this plugin rather than registered with the dashboard:

    GET MediaRequests/v1/Page

A plugin's registered pages are fetched through the dashboard, the dashboard is the administrator's,
and the queue page this plugin registers there is elevated by construction. So a page for a user
cannot be one of those, and this one is an endpoint under the same versioned prefix as the rest of
the API, behind the same authentication. What it answers is in [api.md](api.md).

It shows the caller's own requests and nothing else: the title as it was asked for, what sort of
thing it is, where it stands, when it was asked for, when it last moved, whether the library holds
it, the note the caller wrote, and the reason and the sentence an operator gave when the answer was
no. It draws one call, `GET MediaRequests/v1/Requests`, whose answer names no person at all, which is
what makes "shows nothing about anybody else" a property of the shape rather than of the page's care.

**A caller with no session is refused rather than handed an empty page.** The endpoint carries the
server's default policy. A shell served to anybody and left to fail on its first call puts this
plugin's existence and shape in front of somebody who has not signed in, and reads to a person as a
broken page rather than as a closed door.

### The credential is in the address, and that costs something

A browser navigating to an address sends no Jellyfin session. A session on this server is a header or
a query value and never a cookie, so the only way a person opens an authenticated page in a tab is
with the credential in the address, which the server reads out of `api_key` on both claimed lines.

What that costs is real and is not softened here. The value lands in the browser's history, in
whatever proxy log sits in front of the server, and in any link the person sends somebody else. This
plugin neither creates such a credential nor extends one, and the page carries what it was opened
with no further than the one call it makes, but neither of those undoes the first sentence. An
operator handing this address to their household should treat it as handing over a session.

### What the page does not do

**It offers no decision and no control at all.** Approving and declining need an administrator, and
cancelling something still open needs a state a request can be withdrawn into, which this plugin does
not have: whether there is a cancelled state is an open decision on #113. So the page is a thing to
read, and a control that could not do anything would be worse than none.

**It has no pager.** It asks for the largest page the endpoint serves and says how many matched
beside how many it drew, so somebody with more requests than that is told so rather than shown a
shortened list that reads as the whole of it.

**It borrows nothing from the dashboard.** The shared stylesheet and script the dashboard pages use
are reachable only under a name registered as a plugin page, and those are served to an
administrator, so a user asking for one would meet a refusal instead of a stylesheet. The page
carries its own.

## Whether one person's requests reach another, asked of a running server

The suite runs the controller over a double. What it holds is that the action serving a person's own
list reads the store for that person and nothing wider, and that the queue action carries the
elevation attribute:

    git grep -n "public async Task NothingButTheCallersOwnRequestsComesBackWhateverIsAskedFor" -- Jellyfin.Plugin.Requests.Tests/Api/ListRequestsTests.cs
    Jellyfin.Plugin.Requests.Tests/Api/ListRequestsTests.cs:61:    public async Task NothingButTheCallersOwnRequestsComesBackWhateverIsAskedFor()

A double has no session, no authorisation pipeline and no cache, so two things it cannot answer are
whether the server enforces an attribute written on an action and whether an answer stays one
person's after the server has handed it out. `scripts/verify-user-isolation.sh` asks a running
server of each claimed line instead, and `.github/workflows/user-isolation.yaml` runs it on every
pull request and nightly.

It creates two ordinary accounts, has each of them ask for a title of its own and both of them ask
for a third, which is joined into a single row rather than asked for twice. That row is the one two
people are both entitled to see and the notes written on it are not, which is why it is there.

**The order of the three list calls is the part that is about caching.** One call as one person and
one as another says nothing: an answer cached against the route rather than against the caller only
comes back to the wrong person when the second caller arrives after the first. So the second
person's list is read immediately after the first person's, and the first person's is read again
once the server has answered both. Every assertion reads the raw bytes as well as the parsed rows,
because a leak arriving in a field the script does not name is the one worth catching.

Taken at `c9dd8d2` on the 10.11 line, job `97001703609`:

    the first person was given ['A film both of them asked for', 'A film only the first person asked for']
    the second person was given ['A film both of them asked for', 'A film only the second person asked for']
    the first person, asking again was given ['A film both of them asked for', 'A film only the first person asked for']
    the queue answered 403
    the queue refused with 403 and carried nothing that belongs to anybody.
    the administrator is served 3 rows and all three titles are among them.
    the page served to the first person carries no title and no note.
    the page served to the second person carries no title and no note.

The 12.0 line is job `97001703695` and returns the same eight lines.

The queue is asked for twice on purpose. A queue that is broken for everybody would satisfy the
refusal on its own, so it is asked again as the administrator and has to answer with all three
titles; the refusal above means the endpoint is closed to that person rather than closed.

### That the check bites

Two branches carry the mistakes it exists to catch. Neither is for merging and neither has a pull
request.

`proof/67-one-persons-list-is-not-everybodys` replaces `FindForUserAsync` with `GetAllAsync` in the
action serving a person's own list, which compiles and passes every suite leg that runs the
controller over a double. Run `32560869939`, both lines red at the first list:

    the first person was given ['A film both of them asked for', 'A film only the first person asked for', 'A film only the second person asked for'] and what belongs to that caller is ['A film both of them asked for', 'A film only the first person asked for'].

`proof/67-the-queue-is-not-closed-to-everybody` takes the elevation off the queue action and leaves
the authorisation attribute, which is one word. Run `32560880189`, both lines red at the queue and
green at everything before it:

    the queue answered 200
    the queue was served to somebody who is not an administrator.

### What this does not reach

It is the API and the page. The channel is not in the tree, and the leak #67 is written about is a
property of that surface specifically: the server materialising what a channel returns into its
library database, where an item is ordinarily visible to whoever can see the folder holding it.
Nothing above says anything about that, and the fallback stated under what the channel costs is
unchanged.

Nothing here opens a browser. What is measured is what the server hands back, including the bytes of
the page itself, and not what a client draws from them.

## What is not reached

Nothing here is softened, and the parts that are claims rather than measurements say so.

**Nothing here has been measured against a client.** The reach matrix is below and it opens with
the sentence that says so; every cell of it that says a user could do something carries the word
untested. Which client families render a channel, and how, is a claim taken from the plan rather
than a thing anybody here has run. Do not read the section above, or the table below, as a
measurement of reach.

**A client with no browser cannot open the page.** That is what the page is: a document served over
HTTP to a signed-in session. A client that draws its own interface and offers no way to open a URL
gets nothing from it.

**A client that does not render channels gets nothing from the channel**, and there is no fallback
for it inside this plugin beyond the API.

**A person is never told a title is here when they would not be allowed to open it.** Both surfaces
draw the same rows from the same endpoint, so what each class of user may learn about a title is one
rule in one place rather than one per surface, and it is written down under "What a person may learn
about a title here" in [`docs/api.md`](api.md). What is worth carrying here is the shape of it: a
title the reader may not see reads exactly like a title the server does not have, so neither surface
can say more than the other, and neither can be widened without widening the endpoint underneath
both.

**On a server with no browsing sibling installed, there is no way to ask for anything from a
television client at all.** This is the sharpest one and it is certain rather than a claim about
clients. This plugin ships no way to find a title the server does not have, decided on #113, because
the sibling discover plugin owns the catalogue and this plugin calls no metadata source, decided in
#92. So on such a server the channel can show a user what they have already asked for and can offer
nothing to ask with. The gesture that creates a request arrives from the sibling, which is #68 and
#89.

## The reach matrix

No cell of the reach matrix in docs/surface.md has been checked against a real client, and the
channel now on the mainline has not been browsed from one.

That sentence is the first thing in this section because the table under it looks like a
capability list and is not one. It is what the decision above would reach if it were built, written
down so that a user on a client that renders none of it can find that out here instead of by trying.
The same sentence is in `README.md`, word for word, so the two cannot drift apart quietly. Line
breaks differ because the two files wrap at different widths, so the comparison collapses whitespace
before it looks. Each file becomes one line, so the count is 1 where the sentence is present and 0
where a word of it has moved:

    for f in README.md docs/surface.md; do printf '%s: ' "$f"; tr -s '[:space:]' ' ' < "$f" \
      | grep -c 'No cell of the reach matrix in docs/surface.md has been checked against a real client, and the channel now on the mainline has not been browsed from one.'; done
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

## The words a person reads

Every word any surface here shows is in `Jellyfin.Plugin.Requests/Localisation/Strings/en.json`, keyed,
and no word is written into a page. English is what ships. Adding a language is adding a file beside
that one and changing nothing else: the project embeds the directory by a wildcard, the loader finds
what the assembly carries by walking its manifest, and nothing in this tree holds a list of the
cultures that exist.

A key a culture has no string for falls back to that culture's language, and then to English. It
never falls back to the key, because showing somebody `queue.column.title` is showing them the inside
of the plugin. A key English has no string for either is a failure raised where it was written,
which is a packaging fault rather than anything a caller did.

The words reach a page over the API, at `GET MediaRequests/v1/Strings`, and `docs/api.md` says why
that is the only shape available: the dashboard serves a plugin's pages out of the assembly itself,
so this plugin never sees the request and cannot substitute anything on the way out.

**What that costs, and it is real.** A catalogue that cannot be fetched leaves a page with no words
at all, including the sentence that would say so. It is not a failure of its own: the catalogue and
the queue are two calls to the same server behind the same session, so a page that cannot reach one
cannot reach the other either.

Four rules in `tools/opengrep/rules.yaml` refuse the ways a word gets written back in, and each is
watched refusing a fixture: a word typed into the markup, a literal assigned to `textContent`, an
accessible name written as a literal, and a sentence handed to `RequestsShell.say` where a key
belongs. Beside them, `PageWordsTests` compares the pages and the catalogue in both directions, so a
key with a letter wrong is a red suite rather than a blank cell.

**Three strings are outside all of this and are named rather than left to be found.** The plugin's
own name and description, and the display name of the page the dashboard lists, are read by the
server once when the plugin is registered rather than per person, so there is no culture to resolve
them against and no catalogue entry could reach them. What a language file changes is everything
inside the pages, not the entry in the dashboard's own menu.

## What the rest of this milestone follows from

Every issue after #65 in milestone 8 is read against the decision above.

- #66, the user's view of their own requests on a client this project has never touched, is the
  channel rendering, and it is built. The section above says what it answers and what it does not
  settle.
- #67, proving one user cannot see another's requests through the surface, is the channel's
  per-user rendering, with the fallback stated above.
- #68, what gesture creates a request from this side, is unchanged: the gesture arrives through the
  seam and this plugin draws nothing anybody clicks.
- #69, serving a page for browsers, is the page.
- #70, what a user sees when the answer is no or not yet, is written once and rendered by both.
- #71, never revealing a title a user is not allowed to see, applies to both.
- #72, the reach matrix, is the table above. It is the shape of the measurement this page says it
  does not have, with every cell still saying so.
- #73, the localisation catalogue, covers the strings both surfaces show, and is the section above.

None of them assumes a surface other than these, and none is closed as not wanted.

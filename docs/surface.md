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

The rendering was per user, and a request is personal data. A user's requests must not be visible to
another user through anything this plugin puts in that database. That was #67, it is the failure
that matters most on this milestone, and this page said in advance what would happen if it could not
be shown:

**If per-user isolation cannot be shown on a running server of each claimed line, the channel falls
back to a shape that carries no per-user data at all.**

**IT COULD NOT BE SHOWN, ON EITHER LINE, AND THE FALLBACK IS WHAT THIS CHANNEL NOW IS.** The reading
is under "Whether one person's requests reach another" below, with the jobs it came from. Two people
browsed in turn and the first browsed again, and the first was handed a title only the second had
asked for, word for word the same on 10.11 and on 12.0. So this is a condition that was met rather
than a plan that failed, and the sentence above is kept in the tense it was written in, because
having decided this before the measurement is the whole value of it.

What it costs is in the matrix at the bottom of this page rather than softened here. A person on a
television client can no longer see what they asked for from that client at all. That is worse than
what stood yesterday for everybody who is not at a browser, and it is better than a surface that
hands one person another person's requests.

## The channel, as it is built

`Jellyfin.Plugin.Requests/Surface/RequestsChannel.cs`. The server resolves its channels out of the
container this plugin registers into, so the registration is what puts a place beside a person's
libraries on a client nobody here can change.

It answers one folder, that folder says where a person reads their own requests, and it is the same
folder for everybody. Opening it is answered with nothing rather than with an error, and so is any
other identifier, including the state folders the shape this replaced handed out, because the server
keeps identifiers it was given earlier and a client that saved one will ask for it again.

**It never asks who is browsing and it never reads the store.** That is the property rather than a
consequence of one. `InternalChannelItemQuery.UserId` is not read anywhere in the type, and the
constructor takes the catalogue and nothing else, so there is nothing in the object an answer could
be made one person's from. `TheChannelIsBuiltFromTheCatalogueAndNothingElse` holds the second half,
which makes putting the store back a change to a test rather than a line inside a method that
nothing reads.

Every word still comes out of the same catalogue the page reads, by the same key, so the two
surfaces cannot drift into two answers.

**What this replaced, and why a filter was not enough.** It answered one folder per state that
person had something in, with the titles inside them, the decline reason where one was given, and a
sentence for somebody who had asked for nothing. That answer was correct, and the suite asserting
that one person's rows never carried another person's was green throughout. What a client is served
is a different thing from what this plugin answers. A channel's rows are written into the server's
own library database under a parent belonging to the channel rather than to the caller, and the
server removes everything under that parent which the current caller's answer did not contain.
`IHasCacheKey` named the person and repaired the cache path; it did not repair the parent. Naming
the person in each folder identifier does not either, because the folders hang under the channel and
a library query for that parent reaches whatever is beneath it without passing through this plugin
at all.

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

**It offers no decision about a request.** Approving and declining need an administrator, and
cancelling something still open needs a state a request can be withdrawn into, which this plugin does
not have: whether there is a cancelled state is an open decision on #113. So the page is a thing to
read, and a control that could not do anything would be worse than none.

**It carries one control, and it is not a decision.** A checkbox that turns off the message this
plugin pushes at the person reading the page when one of their own requests moves. It belongs there
because it is a setting about that person rather than about a request, and because the surface it
would otherwise live on is the dashboard, which is the administrator's - an operator able to silence
what somebody else is told is the shape #9's decision refuses.
[notifications.md](notifications.md) carries the switch and both endpoints.

The checkbox is hidden until the endpoint has answered which way it is set, so a page that could not
read it shows no control rather than an unchecked box that reads as "off". A change the server
refuses puts it back where it was, because showing somebody a setting the server does not hold is how
a person finds out by being told something they thought they had turned off.

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

### The channel, and what the same reading found there

The channel is the surface #67 is written about, and it was the last of the three to be asked. The
walk is the same shape as the three list calls: the channel is found by the name the catalogue
holds, each person reads the root and then every row of the root asked for as a folder, the second
person reads immediately after the first, and the first reads again once the server has answered
both.

**It found the leak on its first run and that is why this channel changed.** Read against the
channel that answered a person's own requests, run `32645853066`, both lines red, the 10.11 job
`97209886193`:

    the first person, browsing was shown ['A film both of them asked for (1975)', 'A film only the first person asked for (1999)']
    the second person, browsing was shown ['A film both of them asked for (1975)', 'A film only the second person asked for (2001)']
    the first person, browsing again was shown ['A film both of them asked for (1975)', 'A film only the second person asked for (2001)'] and what belongs to that caller is ['A film both of them asked for (1975)', 'A film only the first person asked for (1999)'].

The 12.0 job is `97209886061` and the three lines come back word for word. Read the third against
the first. The first person asked for a 1999 film and never for a 2001 one, and on their second
visit the 1999 film is gone and the film only the second person asked for is in its place.

The third call is the whole reason the order is what it is. One person reading and then another
reading says nothing; what shows this is the first person coming back after somebody else has been
served.

**What the channel answers now, and it is the fallback rather than a repair.** Under "What the
channel costs" above. Taken at `4eb91c4` on the 10.11 line, job `97213335974`:

    the first person, browsing was handed the one folder and nothing else.
    the second person, browsing was handed the one folder and nothing else.
    the first person, browsing again was handed the one folder and nothing else.
    the library answered the first with 200, 608 bytes
    the library served the first ['Open the requests page on this server to see what you asked for.'] and carried nothing of anybody.
    the library answered the second with 200, 608 bytes
    the library served the second ['Open the requests page on this server to see what you asked for.'] and carried nothing of anybody.
    the library answered the administrator with 200, 608 bytes
    the library served the administrator ['Open the requests page on this server to see what you asked for.'] and carried nothing of anybody.
    naming somebody else answered 403
    naming somebody else was refused with 403 and carried nothing of anybody.

The 12.0 line is job `97213335855` and returns the same eleven lines.

**The three library calls are the half the channel calls cannot reach.** A channel's answer is
written into the server's own library database under a parent belonging to the channel, and
`GET /Items?parentId=<channel>&recursive=true` reaches whatever is beneath that parent without
passing through this plugin at all. So the same parent is asked for as each person and as the
administrator, and the only thing any of them may be served is the one folder.

The first person's call has to return that folder rather than merely not returning anybody's title.
Without it the whole reading passes on a server where that query answers with an empty set whatever
is asked of it, which is a different thing from a library holding nothing of anybody. It is the
same near-miss the queue is asked about twice for.

The last call asks whether the server refuses one person naming another in `userId`. That is the
server's guard rather than this plugin's, and it is read rather than assumed.

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

`proof/67-the-channel-knows-who-is-asking` makes the one folder's name depend on the caller, which
is the smallest way per-user data comes back to that surface. Run `32647542023`, both lines red at
the first walk:

    the first person, browsing was handed ['Open the requests page on this server to see what you asked for. 1b789934-1111-4859-8973-dc24ade9c6d9'] and the whole of this channel is ['Open the requests page on this server to see what you asked for.'].

That branch reds the suite as well, and the strongest thing said for the channel walk is not it. The
walk refused the mainline, on both lines, for a defect nobody had injected, and the shape of this
surface changed because of it. That is the run quoted two sections above rather than a branch made
to fail.

### What this does not reach

Nothing here opens a browser. What is measured is what the server hands back, including the bytes of
the page itself, and not what a client draws from them.

Nothing here says what a television client draws of the one folder the channel answers, or whether
it draws it at all. That is the matrix below, and every cell of it still says nobody has looked.

The library reading is a reading of one parent. Anything written into that database under a parent
this reading does not name is outside it.

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
for it inside this plugin beyond the API. Since #67 that costs less than it did, because what the
channel carries is one sentence saying where to look rather than anything a person came for.

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
#92. So on such a server there is nothing to ask with from a television client, and since #67 there
is nothing to read there either. The gesture that creates a request arrives from the sibling, which
is #68 and #89.

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

| Client family                               | See their own requests | Ask for something new  | Cancel one they asked for |
| ------------------------------------------- | ---------------------- | ---------------------- | ------------------------- |
| Browser                                     | page, untested         | sibling only, untested | page, untested            |
| Desktop client wrapping the web interface   | page, untested         | sibling only, untested | page, untested            |
| Android phone and tablet                    | nothing                | sibling only, untested | nothing                   |
| Android TV and Fire TV                      | nothing                | sibling only, untested | nothing                   |
| iPhone and iPad                             | nothing                | sibling only, untested | nothing                   |
| Apple TV                                    | nothing                | sibling only, untested | nothing                   |
| Roku                                        | nothing                | sibling only, untested | nothing                   |
| LG webOS television                         | nothing                | sibling only, untested | nothing                   |
| Samsung Tizen television                    | nothing                | sibling only, untested | nothing                   |
| Kodi                                        | nothing                | sibling only, untested | nothing                   |
| A script or another program against the API | the API                | the API                | no route today            |

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

`page` is the browser rows, and it is the only cell in that column that says a person can see their
own requests at all. Every other row says `nothing`, and it says it because of #67 rather than
because nobody has tried: the channel answered a person's own requests until a reading on a running
server of each line handed one person another person's title, and it now answers one folder saying
where to look. The cell above the matrix that used to read `channel` was the reach that bought.

`sibling only` is the sharpest cell and the one that is certain rather than untested. This plugin
ships no way to find a title the server does not have, so there is no gesture here to make. On a
server with the browsing sibling installed the want arrives through the seam; on a server without
it, the answer in that column is nothing, on every row above the last. What is untested there is
whether the sibling draws anything on that client, which is that board's measurement and not this
one's.

`nothing` in the cancel column is the cost of the folder tree and predates #67. Cancelling is a
per-state operation with a reason a person reads, and a channel renders items, so the gesture has
nowhere to live there. Since #67 the row above it says `nothing` too, so a user on a television
neither sees what they asked for nor takes it back from that client.

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

## What #66 asked for, and where each of its three conditions stands

#66 is closed and replaced by #316, which states the same goal against the two surfaces that exist
rather than against the one it was written for. A replacement makes the older issue's conditions
somebody's to answer rather than nobody's, so they are answered here one at a time. Re-planned is
not a disposition; met and deliberately dropped are.

**Grouped by state, on a client this project has never touched. The client half is dropped, and the
state half is met as a column rather than as groups.** The channel is what that condition was
written for, and since #67 it answers one folder to every caller and never reads who is asking:

    grep -n 'answers the same single folder to every caller\|public bool IsEnabledFor' Jellyfin.Plugin.Requests/Surface/RequestsChannel.cs
    37:/// answers the same single folder to every caller, it never asks who is browsing, and it never
    91:    public bool IsEnabledFor(string userId) => true;

So no surface here puts a person's own list in front of an unmodified television client, and the
reach matrix above is where that stands rather than being softened by this section. What is dropped
is the client, and what is left is why: a per-person channel was the thing #67 measured being handed
to the wrong person on a running server of each line.

The state half is met on both surfaces. The endpoint carries where each request stands, and the page
draws it beside the sentence for what happens next:

    grep -n 'public required RequestState State' Jellyfin.Plugin.Requests/Api/MyRequest.cs
    58:    public required RequestState State { get; init; }

    grep -n 'mine.column.state"\|named("mine.state"' Jellyfin.Plugin.Requests/Web/mine.html
    87:                        <th scope="col" data-i18n="mine.column.state"></th>
    297:                        cell(row, named("mine.state", request.State));

A column and not groups, and that is the shape rather than an approximation of one: a person reading
their own handful of asks reads the whole table, and grouping it would repeat the same heading over
one row each.

**A declined request shows its reason where one was given. Met.** The row carries the closed-list
reason, and the sentence an operator wrote beside it where they wrote one:

    grep -n 'public DeclineReason? DeclineReason\|public string? DeclineNote' Jellyfin.Plugin.Requests/Api/MyRequest.cs
    88:    public DeclineReason? DeclineReason { get; init; }
    94:    public string? DeclineNote { get; init; }

**The view is empty and harmless for a user who has never asked for anything. Met.** No matches
draws the sentence saying so, rather than an empty table a person has to interpret:

    grep -n 'MatchCount === 0' -A 3 Jellyfin.Plugin.Requests/Web/mine.html
    310:                    if (answer.MatchCount === 0) {
    311-                        summary.textContent = word("mine.empty");
    312-                        return;
    313-                    }

Harmless is the other half of that word and is the endpoint's rather than the page's: a caller with
no requests reads their own list and nothing wider, because the narrowing is the store lookup rather
than a filter over a wider read, which `WhatAPersonIsToldTests` and `ListRequestsTests` hold.

**None of this was read on a running server or in a browser.** The commands above read this
repository. What a client draws is outside what the suite can see, which is the headless rule in
[testing.md](testing.md), and the reach matrix above still has no cell checked against a real
client.

## What the rest of this milestone follows from

Every issue after #65 in milestone 8 is read against the decision above.

- #66, the user's view of their own requests on a client this project has never touched, was the
  channel rendering. It was built and then taken out by #67, and the issue is closed and replaced
  by #316. Where each of its three conditions stands is the section above rather than a word here,
  because two of them are met and only one is dropped.
- #67, proving one user cannot see another's requests through the surface, was measured on a
  running server of each claimed line and could not be shown. Its third condition is what this
  channel now is.
- #68, what gesture creates a request from this side, is unchanged: the gesture arrives through the
  seam and this plugin draws nothing anybody clicks.
- #69, serving a page for browsers, is the page.
- #70, what a user sees when the answer is no or not yet, is written once and rendered by both.
- #71, never revealing a title a user is not allowed to see, applies to both.
- #72, the reach matrix, is the table above. It is the shape of the measurement this page says it
  does not have, with every cell still saying so.
- #73, the localisation catalogue, covers the strings both surfaces show, and is the section above.

None of them assumes a surface other than these, and none is closed as not wanted.

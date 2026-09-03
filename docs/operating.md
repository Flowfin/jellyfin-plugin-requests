# Operating this plugin

The path from an installed plugin to a queue somebody can work, and the sequence to read when the
queue stops moving. It is written for an operator who has never seen this plugin and does not want to
read the rest of `docs/` first.

Read the "This is not finished" section of [../README.md](../README.md) before any of it. Four releases
exist, two per claimed server line, and the newest of each line has been installed once on a server of
that line and answered; the manifest a server fetches still offers only the 10.11 line's `0.2.0.0`. On
`10.11.0`, the floor that `0.2.0.0` claims, the server refuses to load it. Nothing below changes either
half, and none of it applies on a server where the plugin does not load at all.

## From an installed plugin to a queue

Five steps, in this order, because each one depends on the one above it.

### 1. Check the server loaded it

The dashboard's plugin list is the authority. A plugin that is installed and not `Active` is one the
server refused to load, and every step below reads as broken until that is fixed. The reason is in
the server's own log rather than anywhere in this plugin.

### 2. Take the settings decisions

The settings page is where the dashboard puts a plugin's settings, under this plugin's own name. What
each setting is, what it defaults to and what value it refuses are in
[configuration.md](configuration.md), which is the authority for all of it. What this page adds is
the order to take them in and what each one costs you later.

- **How many open requests one person may hold.** This is the setting an operator reaches for the
  first time somebody files thirty in an evening. It counts what is still waiting for an answer
  rather than everything a person has ever asked for, so a long-standing user does not run out
  permanently. There is no value meaning "no limit", and that is refused on the page and again on
  the server rather than accepted and quietly replaced.
- **Which kinds of thing may be asked for.** Turning one off is how a server that holds only films
  stops collecting series requests it will never answer.
- **How long a finished request is kept.** A daily task removes a request that has been fulfilled,
  declined or failed for longer than this, counted from the move that finished it. A request nobody
  has answered is never removed by age. What is held and for how long is
  [personal-data.md](personal-data.md).
- **The address a notice is posted to.** Leave it empty unless you have somewhere to receive one.
  Empty means nothing is sent, and that is a supported way to run rather than a half-configured one.
- **Which movements go to that address.** Approvals, declines and fulfilments have a switch each and
  all three are on, so an address typed and nothing else decided posts all three. The one worth
  deciding on the first evening rather than on the first noisy one is fulfilments: nobody moves a
  request to fulfilled, the library does, so its volume follows how fast titles are arriving rather
  than how often anybody works the queue. With the address empty all three say nothing, which is why
  they are on rather than off.
- **Whether an administrator is told that a request arrived.** This is off, it is a decision of its
  own rather than a fourth switch on the list above, and it is the one an operator looking for "tell
  me when somebody asks" would otherwise conclude does not exist. It needs no address: it sends
  nothing out of the machine and goes down connections the server already holds to clients already
  signed in. Read what [notifications.md](notifications.md) measures about it before turning it on,
  because no Jellyfin client subscribes to the name it goes out under, so switching it on and
  watching the dashboard shows nothing. What reaches an operator today is the activity entries the
  dashboard already draws, and the queue.

### 3. Know that approval is not a switch

There is no setting that makes requests skip the queue. Every request arrives open and stays open
until a person decides it, and per-person automatic approval is a later decision rather than a
feature turned off.

That matters on the first evening: an operator who expected approval to be optional and finds a queue
filling up has not misconfigured anything, and there is nothing to look for.

### 4. Find the queue

This plugin registers a page of its own in the dashboard's main menu, beside the dashboard's own
entries rather than inside the plugin list. The plugin list holds the settings; the menu entry holds
the queue, because one is opened twice and the other every day.

What the queue shows, and why each column earns its place in a decision, is [queue.md](queue.md).
Approving and declining are done from that page, and a decline carries a reason and, where you write
one, a sentence the person who asked will read.

### 5. Give people a way to ask

This is the step that decides whether the queue ever has anything in it, and it is the one this
plugin does least for.

**This plugin ships no way to find a title the server does not have.** There is no search here and no
gesture on a television client that creates a request. That was decided rather than overlooked, and
the reasoning is in [surface.md](surface.md) and in the README section above.

So a request arrives by one of two routes. Either the browsing sibling plugin is installed and hands
one across, which is the route the design is built around, or something drives the create endpoint
directly, which is what an operator scripting against their own server would do. The endpoints, what
they take and what they answer, are in [api.md](api.md).

A person who has asked for something reads their own requests on a page this plugin serves, at
`MediaRequests/v1/Page`, and what that address costs to hand out is written down in
[surface.md](surface.md). It is worth reading before you send it to a household, because a browser
opening an address sends no session and the credential therefore travels in the address.

**What to tell a person who asks you how to use this** is [user-guide.md](user-guide.md), which
answers it per client family, because the answer differs by what the client in their hand can draw
and there is no single one to give. It is not repeated here: an operator who paraphrases it into
their own house rules ends up with a second copy that goes stale against the reach matrix in
[surface.md](surface.md), and the families that get nothing from this plugin are exactly the ones a
paraphrase gets wrong.

## Wiring an external request service

Most servers run none, and on those this plugin is the whole system: approving is an answer and
nothing is fetched. Where a service speaking the Overseerr form runs beside the server, three settings
on the plugin's page point this plugin at it, and from then on an approval is handed to that service
and the queue is asked about hourly. [bridge.md](bridge.md) is the authority for everything below;
this section says what to do in which order, what each step costs, and what to read when it stops.
The address in step 2 of the setup above is a different thing: that one is where a notice about a
request is posted, and it has nothing to do with a request service.

**1. Read what typing the credential in means, before typing it.** It goes in the plugin's
configuration file under the server's data directory, which means it is in your backups by design,
readable by anything else loaded into the same server process, and readable by an administrator of
the server over the API, because that is how the dashboard renders a plugin's settings page.
Encrypting it at rest with a key on the same disk is refused rather than unimplemented, and the
reason is under "Where a credential would live, and what may be claimed about it" on that page.

**2. Type the address**: the root of the service as a browser opens it, scheme included and with no
path. The plugin puts the form's own path under it. This one is readable back on the page, on
purpose: it names a machine and carries nothing that authenticates, and you have to be able to see
which service your server is pointed at.

**3. Type the key** the service issued for this server, which is the API key on the service's own
settings page. It travels as a header on every call and nowhere else, and the page shows whether one
is set and never the value. An address with no key is refused rather than saved.

**4. Decide whose name a request arrives under.** The identity mapping is a table you keep, one row
per person, and it is empty on a fresh install. That is the shipping answer rather than a state on
the way to one: with no rows, every request arrives at the service under the service's own account,
and it is told that a request was made and not by whom. Attribution is turned on one person at a time
by writing a row, which is the person's identifier on this server, a space, and the number the
service knows them by, and writing that row is what says that person's account over there may leave
this server. Matching people by name is refused rather than built and switched off, because it is
wrong in the direction that credits one person with another's request.

**5. Read the panel.** From the moment the address is saved, the panel at the top of the queue asks
the service on every turn of the page, and the queue gains a column saying whether each approved
request was handed over. The sentence on the panel is the first thing to read when something is
wrong, and the sequence below is what to do about each one.

### When the bridge stops

Read in this order and stop at the first line that matches. Every one of these is a sentence on the
panel or a cell in the queue; none of them needs the log to be found, and where the log holds the
detail the step says so.

**1. The panel says the service is not answering.** That is a fact about the service or the network
between, not about this plugin, and the moment beside it is when this server last saw it answer.
Check that the service is up and that the address in step 2 is the one a browser reaches it at.
There is nothing to reset here: nothing remembers the service as down, the next approval and the
next hourly run ask again on their own, and every call gives up after ten seconds rather than
hanging.

**2. The panel says the service refused this server's key.** The bridge is stopped: nothing is asked
about until the key is corrected, and the server's log says so at error as well. An approval made
meanwhile fails and shows so on its row. Take a key from the service's own settings page and type it
at step 3; the panel asks again on the next turn of the page, so the sentence changes without a
restart, and the hourly run picks up from there.

**3. The panel says the service reports a version this plugin does not know.** The bridge is stopped
the same way. This plugin knows Overseerr's 1.x line and Jellyseerr's 2.x line by major version and
stops on anything else, a development build included, because it has never been read or measured
against that form. Nothing on this server fixes it: either the service or this plugin has to move,
and [bridge.md](bridge.md) says which versions are known today.

**4. The panel says the service answered, and one row says handing it over failed.** That is a fact
about that one request, and nothing else is held up by it. Three things produce it: the service does
not know the title by the TMDB number this side holds, the request carries no TMDB identifier at all,
or the mapped account for the person who asked is not a number. The server's log names which, by
request identifier; the row does not. A request in this state is approved and not handed over, and
nothing hands it over again on its own. To hand it over again, decline it with a reason and approve
it again: the second approval is a fresh handover, and the history keeps both moves.

**5. The panel says the service answered, no row says failed, and an approved request sits still.**
The service holds it and has not finished, and its own queue is where to look. The hourly run moves a
request here to failed only when the service says it declined or gave up; a title the service is
still fetching stays approved here until this server's library sees it arrive, which is step 5 of the
sequence below.

### Jellyseerr 2.7.3 on the 12.0 line signs in with the legacy header

If the service is Jellyseerr `2.7.3` and this server is on the 12.0 line, the service's own first
sign-in against this server fails with a `400` before a password is looked at, and nothing in this
plugin is involved. Jellyseerr `2.7.3` names its client in the `X-Emby-Authorization` header, and a
12.0 server reads that header only while `EnableLegacyAuthorization` is on, which it is not by
default there; with the header unread the sign-in names no client and the server refuses it. The
plugin's own calls to the service carry the key in `X-Api-Key` and are unaffected, and the plugin
never signs in to the server. Neither line's web client carries a label for the switch in its string
catalogue, so expect to set it outside the dashboard: it is `EnableLegacyAuthorization` in the
server's `system.xml` under its configuration directory, or the same field read and written on
`/System/Configuration`, which is how `scripts/verify-bridge-round-trip.sh` turns it on for the
server it starts. A later Jellyseerr may send the `Authorization` header instead, and this paragraph
is then about a version range rather than a switch. Read on 2026-09-03, so that a reader on a later
server re-runs it rather than trusting the sentence:

    gh api 'repos/jellyfin/jellyfin/contents/MediaBrowser.Model/Configuration/ServerConfiguration.cs?ref=v12.0-rc7' --jq .content | base64 -d | grep -n 'EnableLegacyAuthorization'
    290:    public bool EnableLegacyAuthorization { get; set; }

    gh api 'repos/jellyfin/jellyfin/contents/MediaBrowser.Model/Configuration/ServerConfiguration.cs?ref=v10.11.11' --jq .content | base64 -d | grep -n 'EnableLegacyAuthorization'
    290:    public bool EnableLegacyAuthorization { get; set; } = true;

    gh api 'repos/jellyfin/jellyfin/contents/Jellyfin.Server.Implementations/Security/AuthorizationContext.cs?ref=v12.0-rc7' --jq .content | base64 -d | grep -n 'X-Emby-Authorization'
    235:                auth = httpReq.Headers["X-Emby-Authorization"];

    curl -sS https://raw.githubusercontent.com/fallenbagel/jellyseerr/v2.7.3/server/api/jellyfin.ts | grep -n "X-Emby-Authorization"
    147:          'X-Emby-Authorization': authHeaderVal,

    for line in release-10.11.z master; do curl -sS "https://raw.githubusercontent.com/jellyfin/jellyfin-web/$line/src/strings/en-us.json" | grep -c -i 'LegacyAuthorization'; done
    0
    0

## When requests stop moving

Read the panel at the top of the queue page, in the order below. It answers from this server process
rather than from the install, so every moment on it is measured since the server last started, and
the sentences say so. Stop at the first step that is wrong; the ones below it read as broken whenever
a step above them is.

**1. Is the panel answering at all?** A line saying that whether this plugin is working could not be
read, above a queue that did answer, means the plugin is serving pages and the check behind the panel
is not. Everything below this step is unread rather than healthy.

**2. Could the requests be read?** A line saying the requests could not be read is the one failure
that makes every count above it meaningless, and it is the first thing to fix. It names no path, on
purpose. The server's own log names the file and says why, and that is where to go next. Nothing else
on this page will be right until the store opens.

**3. Do the counts show anything at all?** A queue with nothing in any state is almost never a broken
plugin. It is step 5 of the setup above: nobody has a way to ask. Check that the browsing sibling is
installed, or that whatever drives the create endpoint is still running, before looking at anything
here.

**4. When was something last written?** A line saying nothing has been written since this server
started, on a server that has been up for a while and has people using it, points at the same place
as step 3 rather than at the store. A moment here that is recent and a queue that has not moved
points at a person rather than at the plugin.

**5. When was the library last checked?** Approving a request does not fetch anything. What moves a
request to fulfilled is this plugin noticing the title arrive in the server's own library, and that
check runs on the library's own events and on a schedule. A line saying the library has not been
checked since this server started, on a server that has been up for a while, is why approved requests
are sitting still, and it is the most common cause of the complaint that the queue stops after
approval.

**6. What does the bridge say?** On most servers this reads that no external request service is set
up, and that is the ordinary answer rather than a fault: approving is an answer and nothing is
fetched. Where one is configured, the line is one of four sentences and "When the bridge stops" above
is the sequence for them: not answering is a fact about that service with the moment it last answered
beside it, a refused key and an unknown version are the bridge stopped until you act, and a service
that answered while a row says its handover failed is a fact about that one request.

### What the sequence does not reach

**It says nothing about a person's access.** A request that a person cannot see is a question about
who they are and what they may read, and none of the six steps above touches it.

**It is not a log.** Every step that ends at a file or a reason ends at the server's own log, because
this plugin puts no path and no credential on that panel. That is the same rule everywhere else here:
what one person learns about another's request is nothing, and a diagnostics surface is not an
exception to it.

**Nothing here was read on a running server, with one exception.** The panel, its sentences and the
order above are read off the page and the endpoint behind it in this repository, and so is the
sequence for the bridge: none of its four failures has been produced by a running service, which
[bridge.md](bridge.md) says beside the legs that hold them. The exception is the `400` and the switch
in the paragraph on Jellyseerr `2.7.3`, which the round-trip procedure met and measured on a server it
started, in run `33730767209` on this board. A session in front of a real install answering these
questions in this order is a different statement and is not made here.

# Operating this plugin

The path from an installed plugin to a queue somebody can work, and the sequence to read when the
queue stops moving. It is written for an operator who has never seen this plugin and does not want to
read the rest of `docs/` first.

Read the "This is not finished" section of [../README.md](../README.md) before any of it. One release
exists, it carries the package for one of the two claimed server lines, and the packaged install path
has not been tried on a server. Nothing below changes that.

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

**Nothing in this tree talks to one.** The only bridge that ships is the one for a server that has no
service, it makes no call, and the settings carry no field for an address or a credential. There is
nothing to wire today.

That is not the same as nothing being decided, and the parts an operator will have to reason about
are already written down. [bridge.md](bridge.md) is the authority for all three, and this section
says which part of it answers which question.

**The credential.** Where it goes, who on the machine can read it, and what is refused by name are
under "Where a credential would live, and what may be claimed about it". The short version an
operator needs before agreeing to type one in: it goes in the plugin's configuration file under the
server's data directory, which means it is in your backups by design, readable by anything else
loaded into the same server process, and readable by an administrator of the server over the API,
because that is how the dashboard renders a plugin's settings page. Encrypting it at rest with a key
on the same disk is refused rather than unimplemented, and the reason is written there.

**Whose name a request arrives under.** The identity mapping is a table an operator keeps, one row
per person, and it is empty on a fresh install. That is the shipping answer rather than a state on
the way to one: with no rows, every request arrives at the service under the service's own account
and it is told that a request was made and not by whom. Attribution is turned on one person at a time
by writing a row, and writing that row is what says that person's account name may leave this server.
Matching people by name is refused rather than built and switched off, because it is wrong in the
direction that credits one person with another's request.

**What each failure looks like.** This is the part no document can answer yet, and the reason moved
on 2026-08-28. The four failures now have decided behaviours: unreachable is temporary and retried
with a bound, a refused credential stops the bridge and tells the operator rather than being retried
until somebody notices, a service version the adapter does not know is surfaced as an incompatibility
and not retried either, and a title the service has never heard of is a fact about one request and
never stops the rest. Those are the ruling on issue #86 and they are written here as an address
rather than as a sequence, because nothing implements them: the only bridge that ships is still the
one for a server with no service, so none of the four can occur. Issue #315 is the adapter that makes
them real, and this section is written against what it does on the day it lands. A sequence written
before then would be an account of intentions rather than of behaviour, which is the one thing
somebody reading a page like this under pressure cannot afford.

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
fetched. Where one is configured, a line saying it is not answering is a fact about that service and
not about this plugin, and the moment it last answered is beside it.

### What the sequence does not reach

**It says nothing about a person's access.** A request that a person cannot see is a question about
who they are and what they may read, and none of the six steps above touches it.

**It is not a log.** Every step that ends at a file or a reason ends at the server's own log, because
this plugin puts no path and no credential on that panel. That is the same rule everywhere else here:
what one person learns about another's request is nothing, and a diagnostics surface is not an
exception to it.

**Nothing here was read on a running server.** The panel, its sentences and the order above are read
off the page and the endpoint behind it in this repository. A session in front of a real install
answering these six questions in this order is a different statement and is not made here.

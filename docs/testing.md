# Testing

## The headless rule

Every test in this suite runs with no display, with no elevated privileges, and without writing to
any store the machine shares between programs. The suite is `dotnet test` on a checkout and nothing
else: no browser, no server, no container, no socket to anywhere outside the process, no
certificate installed, no service registered, no port below 1024.

The reason is what a rule like this costs when it is absent. A suite that needs one more thing than
the machine has is a suite somebody skips, and a skipped suite says the same thing as a green one.
The second reason is narrower and matters more on a shared machine: a test that raises an elevation
or consent prompt interrupts whoever is sitting at it, and a test that writes a certificate into a
trust store changes what every other program on that machine believes, long after the run ends.

### Deciding whether a proposed test is allowed

A test is refused where running it needs any of the following. The list is the rule; nothing below
it adds a condition.

- A display, or a browser downloaded per machine to stand in for one.
- An elevation or consent prompt, in any form: an installer, a service, a scheduled task, a
  privileged port, a driver, or a write to a machine trust store.
- A real network peer. A socket to another process on the same machine is one; an in-process
  handler reached through a client's handler pipeline is not.
- A running Jellyfin server, a container engine, or any other thing that has to be installed before
  the suite can be run at all.
- Real wall-clock waiting. `Thread.Sleep` and `Task.Delay` in a test are how an ordering problem
  gets hidden rather than tested, and #34 replaces both by injecting the clock.

Everything else is allowed, and a test that needs none of these needs no permission from this
document.

A refused test is not dropped. Each one below names what replaces it, and a proposal that fits a
refusal is answered with that replacement rather than with a no.

Four of the ways this rule gets broken are refused, and the rest of it is read by a person. The
refusals are rules in the invariant lint, and each names the line above it stands for:

    git grep -n "^  - id: no-browser-automation-package\|^  - id: no-certificate-store-access\|^  - id: suite-writes-only-under-the-run-directory\|^  - id: suite-opens-no-socket" tools/opengrep/rules.yaml

`no-browser-automation-package` refuses a reference to Playwright, Selenium, Puppeteer or WebDriver
in the project, props, targets and lock files, which is how a display requirement arrives.
`no-certificate-store-access` refuses `X509Store` in any C# here and `dotnet dev-certs` or
`certutil` in any script or workflow, which is the elevation form that outlives the run.
`suite-writes-only-under-the-run-directory` refuses the seven calls that ask the machine for a
location, anywhere in the test project except the run directory itself and its own tests.
`suite-opens-no-socket` refuses the constructs that build a sending client, accept a connection or
ask a resolver, anywhere in the test project, which is how a real network peer arrives now that the
plugin has an outbound path at all.

What is still read by a person and refused by nothing. A socket opened inside a package the suite
calls passes, and so does an address handed to something that opens one on the suite's behalf,
because the check over the network is source text like the other three. A display requirement
arriving as something other than a package reference passes. A test that reaches a shared location
without naming one of the seven calls passes, for the same reason. A test needing a running server
or a container engine passes. So a test breaking several of the lines above still builds and runs
exactly like one that does not, and the four rules narrow that rather than closing it.

### The refusals, and what replaces each

**A browser-driven test of the administrator page.** Refused: it needs a display, or a headless
browser downloaded onto every machine that runs the suite, which is the second condition above as
well as the first.

What replaces it is two things, because the page is two things. The endpoints the page calls are
tested directly, as ordinary in-process tests against the controller, and they are where the
behaviour lives: #50 lays the controller out, #52, #54 and #56 are the calls the queue makes and
the shapes it gets back, and #61 is acting on one request from the page. The page itself is held by
the formatting gate, `Check formatting`, which reads the embedded HTML, CSS and JavaScript and
refuses a file it would rewrite. That is a formatter and not a validator: it says the page parses
and is written one way, and it says nothing about whether the markup is correct or the page usable.
#64 is where the page is held to the rest of this tree's rules.

**Installing the plugin into a real server, as part of the ordinary suite.** Refused: it needs a
server and a container engine on the machine, which is the fourth condition.

What replaces it is the first-load procedure, `scripts/verify-plugin-loads.sh`. It is #20, it is
closed, and the runs it produced by hand are in the section below together with the mismatch that
was fed to it to show it can fail. It is no longer only a procedure somebody remembers to run:
`Activity entries on a real server` runs it on every pull request and nightly, on both claimed
lines, ahead of the check it exists for. What that leaves uncovered is stated in the section below
rather than here.

**A real HTTPS call to an external request service.** Refused: a real endpoint needs a socket, and
a test endpoint needs its certificate trusted, which means writing to a machine trust store. That
is the third condition and the second one at once.

What replaces it is an in-process HTTP double, so the client under test is exercised through its
own handler pipeline and no socket is opened. It is
`Jellyfin.Plugin.Requests.Tests/Doubles/ASinkEndpoint.cs`, and the outbound call it stands in front
of is the notification sink, which posts a document to the one address an operator typed and reads
a status back. What no double here reaches is a response body, because nothing in this plugin
parses one, and the cases that need a client which does are what #35 is still open for.

This paragraph named an issue rather than a file until #251, and went on saying the double did not
exist for as long as it took somebody to read both.
`TheRefusalListNamesTheDoubleThatReplacesARealOutboundCall` reds if the type is renamed or removed
while this document names it. It reads the name and not the sentence around it, so prose naming the
double and saying the wrong thing about it still passes.

**A message arriving on somebody's client.** Refused: it needs a running Jellyfin holding a session
for that person and a client signed in to it, which is the fourth condition and the first one at
once.

What replaces it is a double over the server's own session manager, in
`Jellyfin.Plugin.Requests.Tests/Doubles/ASessionManagerThatOnlyDelivers.cs`. It has exactly one
method with a body, which is the one call this plugin is allowed to make, and every other way of
pushing something at somebody raises. So what is asserted is that one person was named, which one,
what the message said, and that nothing reaching anybody else was used. What it cannot reach is a
client drawing anything, and `notifications.md` carries the reading of the one client whose source
says what it does with such a message, with the same limit stated there.

**A call refused by the server's authorisation policy.** Refused: the policy is evaluated by the
server, so a test that a signed-in caller who is not an administrator is turned away from the queue
endpoint needs a running Jellyfin holding a session for that person. That is the fourth condition,
and it is the same refusal as installing into a real server above rather than a new kind of one.

What replaces it is two things, on two sides of the endpoint. `EndpointPolicyTests` reads the built
assembly and refuses an endpoint whose policy is not the one written down for it, an endpoint with
no attribute of its own, and an anonymous one; `no-anonymous-endpoint` and
`policy-is-named-by-the-servers-own-constant` refuse the two source shapes that take a policy away,
which are an endpoint reachable with no session and a policy written here as a string rather than
taken from the constant the server registers it under. That is the half about which policy an
endpoint is under. The other half is that the endpoint a caller without elevation can reach has
nothing wider than that caller's own requests to return, which `ListRequestsTests` asks under every
combination of filter, order and page. Neither is the server turning somebody away, and neither
claims to be: what they leave open is that the server evaluates `RequiresElevation` the way its own
endpoints are evaluated under it. That is #51.

Every replacement named above exists, either as something already in the tree or as an issue on
this board. The states move, and a paste taken once reads afterwards as a claim about today, so
this one carries the commit it was read at, `1f5ad56`:

    for n in 20 35 50 51 52 54 56 61 64 115; do gh issue view $n --json number,state,title --jq '"\(.number)  \(.state)  \(.title)"'; done
    20  CLOSED  Prove the built plugin loads on a server of each claimed line
    35  OPEN  Provide an in-process HTTP double for the outbound calls
    50  CLOSED  Lay out the controller, the route prefix and the version rule
    51  OPEN  Decide the authorisation policy for every endpoint
    52  CLOSED  Create a request over the API
    54  CLOSED  Act on a request over the API
    56  CLOSED  Fix the error shape and the status codes
    61  CLOSED  Act on one request from the page
    64  CLOSED  Hold the page to the same rules as the rest of the tree
    115  CLOSED  Make the headless rule refusable rather than written down

    git ls-files scripts/
    scripts/verify-plugin-loads.sh

`Check formatting` is the name the check reports rather than the name of its file, read off a run
rather than off the workflow:

    gh api repos/Flowfin/jellyfin-plugin-requests/commits/dc228a5/check-runs --jq '.check_runs[].name' | sort -u | grep formatting
    Check formatting

### What this list is not

It is not a list of everything that will ever be refused. It holds the refusals the plan makes
visible today, and the conditions above are what decide the next one. A test refused for a
condition not yet met by any proposal gets its own entry here when the proposal arrives, together
with what replaces it.

## Coverage, and the rule it does not replace

Full unit-test coverage is a claim somebody has to be able to check, so the gate collects the number
on every run and refuses a run below a floor.

### What is measured

The `test` job of `gate.yaml` collects coverage in the same run that tests, because a second run
would be a second build and would report a number for code the gate did not test. The collector
ships with `Microsoft.NET.Test.Sdk`, so nothing was added to the dependency graph for it. Both
claimed target frameworks run and the collector writes one merged report, so a line reached on
either server line counts as reached.

The number is covered lines over recorded lines, for the plugin's own package only. The suite's
coverage of itself is not counted: a suite measuring itself says nothing.

The floor is 75%, and it is in `gate.yaml` beside the step that reads it. It is a ratchet rather
than a target: raise it when the number rises, and never lower it to make a run pass. A run below
it fails `call / test`, which is a required check, and the number is written to the run's summary
and printed as an annotation on the pull request either way, so a change that adds untested code is
visible while it is being reviewed rather than afterwards.

A run that produced no report fails as well. A run that collected nothing must not read as a run
that collected everything and found it fine.

To read the same number on a checkout:

    dotnet test Jellyfin.Plugin.Requests.sln --configuration Release --collect "Code Coverage;Format=cobertura" --results-directory coverage

which was 79.50%, 128 of 161 recorded lines, on `b1d401b`.

### The rule the percentage cannot carry

A percentage says lines were executed. It does not say anything was asserted about them, and the
lines that matter here are a small fraction of the total: a transition table has few lines and every
one of them is a decision somebody can get wrong.

So the rule beside the floor is this. **Every state transition, every authorisation refusal and
every error path named in an issue on this board has a test that reaches it, and the test's name
says which one.** The percentage catches drift; this catches the cases that matter.

It is checkable by reading a test name against the issue that named the case, and it is meant to be
checked that way rather than by a tool. An example of the shape, from the store contract in #45:

    git grep -n 'AWriteAgainstAnOvertakenRevisionIsRefused' -- Jellyfin.Plugin.Requests.Tests/
    Jellyfin.Plugin.Requests.Tests/Storage/RequestStoreContract.cs:129:    public async Task AWriteAgainstAnOvertakenRevisionIsRefusedAndSaysWhatTheStoreHolds()

The issue named a refusal, the test names the same refusal, and a reader holding both can see in one
line that the case is covered. A test called `ReplaceTest` would satisfy the percentage and fail
this rule, which is the whole difference between the two.

Nothing refuses a missing case. No check reads an issue, so this rule is read by a person, and a
transition added with no test reaches the mainline as long as the percentage holds. What the floor
does catch is the change large enough to move the number, which is not the same thing and is not
offered as if it were.

## Does the plugin load on a real server

A plugin that builds is not a plugin that loads. An ABI mismatch, an embedded resource path that
stopped resolving after a rename, and a dependency the host does not provide all build clean and
fail at server start or on first use. The suite cannot see any of the three, because it has no
server.

`scripts/verify-plugin-loads.sh` starts a server of one claimed line in a container, installs the
plugin built for that line's runtime, and asks the server itself what it has. It ends non-zero
unless the server reports the plugin as `Active`.

    scripts/verify-plugin-loads.sh jellyfin/jellyfin:10.11.11 net9.0  18096
    scripts/verify-plugin-loads.sh jellyfin/jellyfin:12.0-rc4  net10.0 18097

Run them one after the other rather than together: each publishes into the same tree and each wants
its own port, and the second argument has to be the target framework the image's runtime provides.

What it needs is a container engine, a .NET SDK, `curl` and `python3`. What it does not need is a
display, administrator rights or a trusted certificate. The server is reached over plain HTTP on
the loopback interface, the container is removed when the run ends, and the one account it creates
lives no longer than the container. Nothing it runs can raise a prompt on the machine it runs on.

The plugin is copied into the container after the server has started, because `/config` is a volume
and a copy made before that lands where the running server never looks. The server is then
restarted, because plugins are read at start.

### The recorded run

Run on `e574918775c69640139ee1ecc1f2202efeff27aa`, 2026-08-06, against these images:

    jellyfin/jellyfin@sha256:aefb67e6a7ff1debdd154a78a7bbb780fd0c873d8639210a7f6a2016ad2b35db
    jellyfin/jellyfin@sha256:db1df1d111c27ba1f10bb8fce6630892f66eb66b12c2b24e79011453ac18b3db

The 10.11 line, `scripts/verify-plugin-loads.sh jellyfin/jellyfin:10.11.11 net9.0 18096`:

    == wait for the server to answer
    {"LocalAddress":"http://172.17.0.2:8096","ServerName":"f12222e96abc","Version":"10.11.11","ProductName":"Jellyfin Server","OperatingSystem":"","Id":"5b159e1a1d02497997cf1f933901aafe","StartupWizardCompleted":false}

    == what the server says about its plugins
    Name=AudioDB  Version=10.11.11.0  Status=Active  Id=a629c0dafac54c7e931a7174223f14c8
    Name=MusicBrainz  Version=10.11.11.0  Status=Active  Id=8c95c4d2e50c4fb0a4f36c06ff0f9a1a
    Name=OMDb  Version=10.11.11.0  Status=Active  Id=a628c0dafac54c7e9d1a7134223f14c8
    Name=Requests  Version=1.0.0.0  Status=Active  Id=eb5d78948eef4b36aa6f5d124e828ce1
    Name=Studio Images  Version=10.11.11.0  Status=Active  Id=872a78491171458da6fb3de3d442ad30
    Name=TMDb  Version=10.11.11.0  Status=Active  Id=b8715ed16c4745289ad3f72deb539cd4

    == verdict
    Requests is Active, id eb5d78948eef4b36aa6f5d124e828ce1

    == the configuration page the dashboard would fetch
    GET /web/ConfigurationPage?name=Requests -> 200
    <!doctype html>
    <html lang="en">
        <head>
            <meta charset="utf-8" />
            <title>Requests</title>

    == the configuration the page reads and writes
    {}
    POST /Plugins/<id>/Configuration -> 204

    == done
    Requests loaded and answered on jellyfin/jellyfin:10.11.11 (net9.0)

The 12.0 line, `scripts/verify-plugin-loads.sh jellyfin/jellyfin:12.0-rc4 net10.0 18097`:

    == wait for the server to answer
    {"LocalAddress":"http://172.17.0.2:8096","ServerName":"2461813535d0","Version":"12.0.0","ProductName":"Jellyfin Server","OperatingSystem":"","Id":"2e7c15bb59eb43ad946f7e839158e207","StartupWizardCompleted":false}

    == what the server says about its plugins
    Name=AudioDB  Version=12.0.0.0  Status=Active  Id=a629c0dafac54c7e931a7174223f14c8
    Name=ListenBrainz Similarity Provider  Version=12.0.0.0  Status=Active  Id=a5b2e8c19d4f4a3b8c7e6f1a2b3c4d5e
    Name=MusicBrainz  Version=12.0.0.0  Status=Active  Id=8c95c4d2e50c4fb0a4f36c06ff0f9a1a
    Name=OMDb  Version=12.0.0.0  Status=Active  Id=a628c0dafac54c7e9d1a7134223f14c8
    Name=Requests  Version=1.0.0.0  Status=Active  Id=eb5d78948eef4b36aa6f5d124e828ce1
    Name=Studio Images  Version=12.0.0.0  Status=Active  Id=872a78491171458da6fb3de3d442ad30
    Name=TMDb  Version=12.0.0.0  Status=Active  Id=b8715ed16c4745289ad3f72deb539cd4

    == verdict
    Requests is Active, id eb5d78948eef4b36aa6f5d124e828ce1

    == the configuration page the dashboard would fetch
    GET /web/ConfigurationPage?name=Requests -> 200

    == the configuration the page reads and writes
    {}
    POST /Plugins/<id>/Configuration -> 204

    == done
    Requests loaded and answered on jellyfin/jellyfin:12.0-rc4 (net10.0)

### The recorded run after the check learned to save a setting

The run above predates two things and reads as though neither had happened. It was made before this
plugin minted its own identifier, so the plugin it reports is carrying the template's, and before
the configuration surface had a single setting, so the configuration it reads and writes is `{}`. It
is kept rather than replaced, because it is what was seen on the day it was made.

What the check does now is write two settings whose values are neither field's default and read the
configuration back out of the server, which is the difference between an endpoint that answers a
write and a setting that survives one. This run is from the gate rather than from a machine: the job
that has to know the plugin loaded before it asks anything else runs the same script on every pull
request.

Run on `ca059d2`, 2026-08-21, in run `32516343601`, jobs `96878727285` and `96878727539`.

The 10.11 line, `scripts/verify-plugin-loads.sh jellyfin/jellyfin:10.11.11 net9.0 18098`:

    == what the server says about its plugins
    Name=AudioDB  Version=10.11.11.0  Status=Active  Id=a629c0dafac54c7e931a7174223f14c8
    Name=MusicBrainz  Version=10.11.11.0  Status=Active  Id=8c95c4d2e50c4fb0a4f36c06ff0f9a1a
    Name=OMDb  Version=10.11.11.0  Status=Active  Id=a628c0dafac54c7e9d1a7134223f14c8
    Name=Requests  Version=0.1.0.0  Status=Active  Id=0f9c9107b31b459e81fa6d35dac25e79
    Name=Studio Images  Version=10.11.11.0  Status=Active  Id=872a78491171458da6fb3de3d442ad30
    Name=TMDb  Version=10.11.11.0  Status=Active  Id=b8715ed16c4745289ad3f72deb539cd4

    == verdict
    Requests is Active, id 0f9c9107b31b459e81fa6d35dac25e79

    == the configuration page the dashboard would fetch
    GET /web/ConfigurationPage?name=Requests -> 200
    <!doctype html>
    <html lang="en">
        <head>
            <meta charset="utf-8" />
            <title data-i18n="config.title"></title>

    == the configuration the page reads and writes
    {"OpenRequestsPerUser":10,"AcceptsMovies":true,"AcceptsSeries":true,"FinishedRequestRetentionDays":365,"OutboundNoticeAddress":"","AnnouncesApprovals":true,"AnnouncesDeclines":true,"AnnouncesFulfilments":true}
    POST /Plugins/<id>/Configuration -> 204

    == what the server hands back after the save
    {"OpenRequestsPerUser":7,"AcceptsMovies":true,"AcceptsSeries":true,"FinishedRequestRetentionDays":90,"OutboundNoticeAddress":"","AnnouncesApprovals":true,"AnnouncesDeclines":true,"AnnouncesFulfilments":true}
    OpenRequestsPerUser=7 and FinishedRequestRetentionDays=90 read back after the save

    == done
    Requests loaded and answered on jellyfin/jellyfin:10.11.11 (net9.0)

The 12.0 line, `scripts/verify-plugin-loads.sh jellyfin/jellyfin:12.0-rc4 net10.0 18099`:

    == what the server says about its plugins
    Name=AudioDB  Version=12.0.0.0  Status=Active  Id=a629c0dafac54c7e931a7174223f14c8
    Name=ListenBrainz Similarity Provider  Version=12.0.0.0  Status=Active  Id=a5b2e8c19d4f4a3b8c7e6f1a2b3c4d5e
    Name=MusicBrainz  Version=12.0.0.0  Status=Active  Id=8c95c4d2e50c4fb0a4f36c06ff0f9a1a
    Name=OMDb  Version=12.0.0.0  Status=Active  Id=a628c0dafac54c7e9d1a7134223f14c8
    Name=Requests  Version=0.1.0.0  Status=Active  Id=0f9c9107b31b459e81fa6d35dac25e79
    Name=Studio Images  Version=12.0.0.0  Status=Active  Id=872a78491171458da6fb3de3d442ad30
    Name=TMDb  Version=12.0.0.0  Status=Active  Id=b8715ed16c4745289ad3f72deb539cd4

    == verdict
    Requests is Active, id 0f9c9107b31b459e81fa6d35dac25e79

    == the configuration the page reads and writes
    {"OpenRequestsPerUser":10,"AcceptsMovies":true,"AcceptsSeries":true,"FinishedRequestRetentionDays":365,"OutboundNoticeAddress":"","AnnouncesApprovals":true,"AnnouncesDeclines":true,"AnnouncesFulfilments":true}
    POST /Plugins/<id>/Configuration -> 204

    == what the server hands back after the save
    {"OpenRequestsPerUser":7,"AcceptsMovies":true,"AcceptsSeries":true,"FinishedRequestRetentionDays":90,"OutboundNoticeAddress":"","AnnouncesApprovals":true,"AnnouncesDeclines":true,"AnnouncesFulfilments":true}
    OpenRequestsPerUser=7 and FinishedRequestRetentionDays=90 read back after the save

    == done
    Requests loaded and answered on jellyfin/jellyfin:12.0-rc4 (net10.0)

**The values written back are left on that server and nowhere else.** Each run is a container of its
own, removed when the run ends, so the setting saved here is not a setting anybody's install now
carries.

### That the check bites

A procedure that cannot fail proves nothing, so the mismatch it exists to catch was fed to it. The
12.0 build, which compiles and packages cleanly, installed on a 10.11 server:

    scripts/verify-plugin-loads.sh jellyfin/jellyfin:10.11.11 net10.0 18098

    == what the server says about its plugins
    Name=AudioDB  Version=10.11.11.0  Status=Active  Id=a629c0dafac54c7e931a7174223f14c8
    Name=Jellyfin.Plugin.Requests  Version=10.11.11.0  Status=NotSupported  Id=ff768900c0ec47d72192ea5c133ecc62
    Name=MusicBrainz  Version=10.11.11.0  Status=Active  Id=8c95c4d2e50c4fb0a4f36c06ff0f9a1a
    Name=OMDb  Version=10.11.11.0  Status=Active  Id=a628c0dafac54c7e9d1a7134223f14c8
    Name=Studio Images  Version=10.11.11.0  Status=Active  Id=872a78491171458da6fb3de3d442ad30
    Name=TMDb  Version=10.11.11.0  Status=Active  Id=b8715ed16c4745289ad3f72deb539cd4

    == verdict
    Requests is not in the plugin list: the server did not load it.
    exit=1

The server keeps the plugin in its list under the file name rather than the plugin's own name, with
`Status=NotSupported`, because it never got far enough to ask the plugin what it is called. That is
the shape a reader should expect when this fails.

### What this does not cover

The run above happened on one machine, by hand. This paragraph said nothing on a merge route runs
it, and that ended with #75: `.github/workflows/activity-entries.yaml` runs this script on both
lines on every pull request, ahead of the activity check that needs the same server. The transcript
above is still a hand run at the commit it names and has not been retaken from a job.

What that job is not is a required check. It reports, and a merge is not held on it, so a change
that breaks loading still reaches the mainline; what has changed is that somebody sees it happen on
the pull request rather than finding out later. Which contexts hold a merge here is a branch
ruleset setting and is #107.

The 12.0 line is a release candidate. `12.0-rc4` is what exists today and the tag will move.

The packages are not what a catalogue would serve. This installs the assembly the build produces
into the plugin directory; it does not build the zip, publish a manifest, or exercise the install
path an operator uses. Those are M14.

Nothing here plays media, so the container's transcoding dependencies are never touched, and a
failure that needs a real library to show would not show here.

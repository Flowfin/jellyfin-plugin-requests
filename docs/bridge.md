# The bridge to an external request service

Most servers run nothing of the sort, and on those this plugin is the whole system. Where an
operator does run one, an approved request can be handed to it and the two systems then hold an
opinion each about the same thing. This page is about the one place those opinions have to be
reconciled: what the service's words mean here.

It is not the whole of the bridge. The interface is
[`IRequestBackend`](../Jellyfin.Plugin.Requests/Bridge/IRequestBackend.cs), the implementation every
server without a service runs is
[`NoRequestBackend`](../Jellyfin.Plugin.Requests/Bridge/NoRequestBackend.cs), what an approval hands
over is below, and what happens when the service misbehaves is a separate question this page does not
answer. Where a credential lives is answered at the end of it.

**There is one adapter in this tree, and it speaks the Overseerr form.**
[`OverseerrBackend`](../Jellyfin.Plugin.Requests/Bridge/Overseerr/OverseerrBackend.cs) is the
implementation every server resolves; with no address written it hands every call to
`NoRequestBackend` and adds nothing, so a server without a service is still the server this page
opens with. #82 built the submission path behind the interface, #113 decided the form and that an
adapter is written here as its own module, and #315 asked for it. What the adapter sends, what it
makes of what comes back, and what it refuses before sending anything are in the section on it
below. **One procedure in this tree calls a running service, and nothing in the suite does.**
`scripts/verify-bridge-round-trip.sh` walks one request onto a Jellyseerr started beside a server of
each claimed line, and `.github/workflows/bridge-round-trip.yaml` runs it; the section on the adapter
says what that run measured on 2026-09-03 and what it did not, and the section at the end says what a
reading of the form's description is worth against a reading of an instance.

## The mapping

The table is data, in
[`BackendStates.Table`](../Jellyfin.Plugin.Requests/Bridge/BackendStates.cs), and everything below is
printed from it: `BackendStateMappingTests` compares the rows and the reasons on this page against
that table and fails if either side changes without the other.

The service keeps two lists of words. `RequestStatus` is where the request stands with it, and
`MediaStatus` is what it holds of the media. They are independent, and they share a word, so a report
carries which list it was read from as well as the word itself.

<!-- mapping begins -->

| Vocabulary    | Word      | This side |
| ------------- | --------- | --------- |
| RequestStatus | PENDING   | none      |
| RequestStatus | APPROVED  | none      |
| RequestStatus | DECLINED  | Failed    |
| RequestStatus | FAILED    | Failed    |
| RequestStatus | COMPLETED | none      |
| MediaStatus   | AVAILABLE | none      |

<!-- mapping ends -->

Four of the six move nothing. That is the finding rather than a set of gaps, and it follows from
where the service sits: downstream of a decision an operator already made here, and upstream of a
library check that runs here. The one thing it can say that this side could not otherwise know is
that the fetch will not arrive.

`none` is not "unsupported". It is a word this table holds, and holding it is what keeps it from
falling to the rule below.

## Why each row reads the way it does

<!-- reasons begins -->

- **RequestStatus PENDING**: none. The service is waiting for its own approval step. Nothing is handed to it until an operator here has already approved the request, so this word says where the service stands and nothing about where the request stands.
- **RequestStatus APPROVED**: none. The service agrees with the decision this side already made, and a request cannot be approved twice. The row exists so that agreement is an answer rather than a word nothing recognises.
- **RequestStatus DECLINED**: Failed. The service will not fetch it, so it was sent onward and will not arrive by that route. It is not a decline here: a decline is an operator's answer and carries a reason, and from failed an operator can still decline it or send it onward again.
- **RequestStatus FAILED**: Failed. The thing doing the fetching gave up, which is what this state names and the reason it exists.
- **RequestStatus COMPLETED**: none. The service having finished is not this server holding the media. Fulfilled is the library's word here, observed by the sweep when the person who asked can actually watch it, and taking the service's word for it would fulfil requests on a server whose library never received anything.
- **MediaStatus AVAILABLE**: none. Available there is available on whatever that service can see, which is neither this server's library nor this user's access to it. It has a row of its own because it is the word most likely to be wired straight to fulfilled by somebody in a hurry.

<!-- reasons ends -->

Every row that moves something names a move the transition table allows and admits the plugin as the
caller for. That is checked rather than reviewed, so a row added later cannot ask for a move only an
operator may make. It is why a decline over there is `Failed` here and not `Declined`: declining is a
decision, decisions belong to a person, and the plugin is not one. `docs/lifecycle.md` is where that
split is argued.

## A word this table has not seen

Nothing moves, and the word is reported as unseen rather than turned into the nearest state that
looks about right. `BackendStates.Lookup` answers with no row at all for it, which is a different
answer from a row that moves nothing, and there is no default case anywhere behind it.

The reason to be strict here is that the cost of a guess lands somewhere nobody can see it. A request
put into a state nobody chose reads exactly like a request an operator moved, in the queue and in the
history, and the person it belongs to is told something untrue about their own request.

Case is ignored when a word is looked up, because two adapters written against one service will spell
one word two ways and both mean what the service meant. Nothing else is normalised.

## What an approval hands over

Approving is the moment something else is asked to fetch the title, and
[`BridgeSubmission`](../Jellyfin.Plugin.Requests/Bridge/BridgeSubmission.cs) is the whole of what this
side does about it. It runs after the decision has been written and never instead of it.

**A submission that failed never takes an approval back.** The operator decided, the queue already
holds the decision, and undoing it because a service on another machine was down would be the plugin
overruling a person. What a failure leaves is an approved request carrying no reference, a line in the
server log, and the moment the attempt failed written onto the request, which is a state that can be
handed over again once the service answers.

**That moment is what an operator reads instead of the log.** Two fields answer three states rather
than two: a reference and no failure is a request the service took, no reference and a failure is one
it refused, and neither is a request nothing has been tried on. Without the second field the first
and third read as the same row, and the one that needs somebody is the one that looks ordinary. The
operator's queue draws it as a column of its own, and only on a server where a service is configured:

    git grep -n 'queue.handover' -- Jellyfin.Plugin.Requests/Localisation/Strings/en.json

**A failure that cannot be marked costs nothing but the column.** The mark is a second write, after
the one that holds the decision, and a store that refuses it leaves the approval and the log line
exactly as they were. What is lost then is the row reading as one nothing was tried on, which is a
worse page and not a worse record.

**Submitting the same request twice is refused, and the request itself is what refuses it.** The
reference is written only after a service answered, so a request carrying one has already been handed
over and is not handed over again. Harmless was the other available answer and it is the wrong one: a
second submission of an accepted request is a second copy of the same download on the service's side,
and nothing here could tell the two apart afterwards.

The one case that needs somebody to look is the service accepting a request and the reference then
failing to be written back. The service holds it and this queue does not know so. Nothing retries,
because retrying is the duplicate the rule above exists against, and the log line carries the
identifier the service issued so the two can be reconciled by hand.

On a server with no external service none of this happens and none of it is reported. The shipping
bridge hands back no reference, which is an answer rather than a failure, so an approval there leaves
no log line and no field.

## Whose name a request carries over there

The external service has its own users, and something has to decide whose name a submitted request
arrives under. Three shapes were available and the decision on #113 is the first of them.

| Shape                             | Chosen          | What it costs                                                                                                                              |
| --------------------------------- | --------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| A mapping an operator keeps       | yes             | somebody has to keep it current, and a person who is not in it is not attributed                                                           |
| One shared account for everything | as the fallback | that side cannot tell who asked, so its own queue reads as though the plugin asked for everything                                          |
| Matching by the person's name     | no              | it is wrong the first time two people have similar names, and it is wrong in the direction that attributes one person's request to another |

**Matching by name is not built, rather than built and switched off.** The lookup in
[`BackendAccounts`](../Jellyfin.Plugin.Requests/Bridge/BackendAccounts.cs) takes a Jellyfin user
identifier and there is no other way in, which `NothingIsResolvedFromWhatAPersonIsCalled` refuses a
change to. The failure it stands against is one nobody notices from this side: the queue here stays
correct while somebody over there is credited with a request that is not theirs, and the first sign
of it is that person seeing it.

**The mapping is empty on a fresh install.** That is the shipping answer and not a state on the way
to one. An operator who configures a bridge and writes no rows has every request arrive over there
under the service's own account, so the service is told that a request was made and not by whom.
Attribution is turned on one person at a time, by writing a row, and writing the row is what says
that person's account name may leave.

The table is kept in the plugin's own configuration, as `BridgeAccounts` beside the address it
belongs with, and edited on the settings page in the bridge section, one line per person. It has the
same lifecycle as that address: per server, written by whoever runs it, backed up with the rest of the
configuration, and gone with it; a file of its own would be a second place to keep current and a
second place to lose. It arrived with the adapter that reads it and not before, because a settings
field nothing reads is somewhere to type something nothing uses. **The account in a row is the
service's own numeric identifier for that person**, because the Overseerr form identifies its users by
number, and a row whose account is not a number is refused when the settings are saved rather than
discovered at the first handover. [`configuration.md`](configuration.md) carries the field.

### What leaves the server when a bridge is configured

Nothing leaves any server today, because the only bridge in this tree is the one with nothing behind
it and it makes no call. What the shape above allows to leave, once an adapter exists, is this:

- **The account name the operator wrote**, for a person who has a row. It is that operator's own
  string for that service, passed through as they typed it.
- **Nothing at all about a person who has no row.** No Jellyfin user name, no Jellyfin user
  identifier, and no electronic mail address. The request arrives under the service's own account.

The account record carries the operator's string and nothing else, and
`TheAccountCarriesNothingButWhatTheOperatorTyped` refuses a field added to it later. That matters
because a field holding a display name would start sending it on the next submission and would read
as an improvement in the diff.

What a request itself carries to the service, as opposed to whose name it carries, is #82's, and it
is not decided here.

## Asking the service where things stand

The service is where a handed-over request is actually worked on, so its state moves without this
plugin being told. `ReconciliationTask` asks, hourly and at startup, in the `Requests` category of
the server's own task list; `BridgeReconciliation` is what it runs.

**What it looks at is the rule rather than a check inside it.** Only requests that are still
approved and carry a reference are asked about. That is the one state a handover leaves behind, and
it is what makes the precedence rule a property of the run's shape:

- **A local decline is never reversed**, because a declined request is never asked about, so there
  is no answer that could resurrect it. A decline an operator has to make twice because a remote
  system keeps undoing it is the failure they never forgive.
- **A fulfilled request is never asked about either.** Fulfilled is the library's word here,
  observed when the person who asked can actually watch it, and no service on another machine knows
  better.
- **A request nothing was handed over for is never asked about**, because there is nothing to ask
  with.

**Every change goes through the transition table, with the plugin as the actor.** The entry the
history keeps carries no person, which is true: nobody here decided it. A move the table refuses is
reported in the log naming the request, and the request is left exactly as it is. Today the only
word that moves anything moves an approved request to failed, so a request that has been sent onward
and will not arrive stops looking like an operator forgot about it.

**A word this table has not seen is a logged refusal and nothing else**, which is the section above
applied at the one place that could have guessed.

**A service that did not answer is said rather than swallowed**, once for the run and not once per
request: the fact is about the service. Nothing is walked, every request is left as it is, and the
next run asks again. A service that could not be asked at all is the same answer, because an
unhandled exception in a scheduled task is a task that stops running.

**One request the service cannot answer about does not stop the others.** That failure is a fact
about one reference - it may have been issued by a service an operator has since replaced - and a
run that stopped at the first would let one unknown title hold up every other request on the server.

**A request that moved underneath the run is left as the newer decision left it.** What the service
said is dropped rather than retried, because a retry would put its word over an answer an operator
gave a moment ago.

**On the ordinary install this does nothing and costs one call.** Most servers have no service, the
run ends at the reachability check, and it says nothing above debug.

**What is not claimed.** One run of this task on a server has been watched, against a running
Jellyseerr, in the round trip the adapter's section below records: it asked about one approved
request, was answered `APPROVED`, and left the request as it was. That is the inert row and nothing
else; no run against a service has ever moved a request, because no service has ever answered
`DECLINED` or `FAILED` here, and what the suite measures for those is the reconciliation against a
double, on both claimed target frameworks. Which failures an adapter tells apart, and what each of
them then does, is #86 and is not decided here. And **the person who asked is not told when their request fails this way** - no
sentence is written for that state, so they find out on their own page. That is a gap rather than a
decision, and `RequesterMessage.ForMove` is where it is written down.

## What needs a bridge

Nothing does, and that is a claim the suite refuses to let drift rather than a sentence somebody
wrote once. The register below is every part of this plugin that touches the bridge at all, with
what it does on a server that has none, which is most of them.

`NoBackendCompletenessTests` compares this table against the assembly. Anything that takes
`IRequestBackend` and is not named here reds the suite, so a feature that only works with a bridge
cannot arrive quietly: it either gets a line in this table saying so, or the change does not land.

<!-- needs-a-bridge begins -->

| What                     | Without a bridge                                                                |
| ------------------------ | ------------------------------------------------------------------------------- |
| `BridgeReconciliation`   | Ends at the reachability check, walks no request, and says nothing above debug. |
| `BridgeSubmission`       | Hands nothing over, keeps nothing, and writes no line about it.                 |
| `CapabilitiesController` | Answers, and says that no bridge is configured.                                 |
| `HealthController`       | Answers, and says the bridge is not configured rather than not answering.       |

<!-- needs-a-bridge ends -->

The register is deliberately not a list of features. A feature that needs a bridge is one that takes
the bridge, and taking it is a fact about a type that reflection can read; "which features need a
service" is a judgement nobody can check. So the check is over what touches it, and the column is
where the judgement is written down for a reader.

The other half of the same claim is that exactly two implementations ship, and the suite names both:
`NoRequestBackend`, which is what a server with no service runs, and `OverseerrBackend`, which is what
every server resolves and which hands every call to the first until an address is written. A third
implementation arriving in the plugin assembly unnamed reds the suite for the same reason a feature
taking the bridge unnamed does.

## Where a credential would live, and what may be claimed about it

**There is a credential now, and it is `BridgeApiKey`.** The adapter sends it as the `X-Api-Key`
header on every call to the configured address and nowhere else, and it is empty on every install
where nobody has pasted one. Everything below was written before the adapter existed, as a position
rather than a description written afterwards to fit whatever was built, and the adapter was built to
fit it; where a sentence below has since become enforced rather than claimed, it says so.

**One field for an address does exist and it is not this one.** `OutboundNoticeAddress` is where a
notice about a request is posted, it is empty on every install where nobody has decided otherwise,
and [`notifications.md`](notifications.md) is what it does. It is named here because a sentence
saying the configuration holds no address, in a tree that holds one, is the sentence a reader carries
away.

### The mark such a value would carry is already in force

`SecretAttribute` marks a setting whose value may not appear in anything this plugin writes for
somebody else to read, and the notice address above carries it:

    git grep -nE '^    (\[Secret\]|public string OutboundNoticeAddress)' -- Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:151:    [Secret]
    Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs:152:    public string OutboundNoticeAddress { get; set; } = string.Empty;

**The mark refuses nothing on its own**, and that is the part to read before treating it as
protection. Nothing reads it at run time and no code path behaves differently for a marked property.
What holds the rule is a lint rule pointed at the mark, and a suite leg refusing a mark that no rule
names, so the two cannot drift apart:

    git grep -n 'id: no-marked-setting-in-a-message' -- tools/opengrep/rules.yaml
    tools/opengrep/rules.yaml:789:  - id: no-marked-setting-in-a-message

    git grep -n 'public void EveryMarkedSettingIsNamedByARuleInTheInvariantLint' -- Jellyfin.Plugin.Requests.Tests/Configuration/SecretsStayOutOfTheLogTests.cs
    Jellyfin.Plugin.Requests.Tests/Configuration/SecretsStayOutOfTheLogTests.cs:147:    public void EveryMarkedSettingIsNamedByARuleInTheInvariantLint()

It is not a redacting type, and that is a decision rather than an omission: the host keeps this
configuration by serialising it and the settings page edits it the same way, so a value that hands
out a redaction on those paths is a setting that does not survive a restart. What answers that
instead is a write-only settings page.

None of that decides anything about a bridge credential. It is written here so that the section
below argues against what is in force rather than against an empty tree, and where such a credential
is kept is #85.

### Where it goes

In the settings file, beside every other setting. That is the row already written down in
[`storage.md`](storage.md), `plugin-configurations/Jellyfin.Plugin.Requests.xml` under the server's
own data directory, and it is where it goes because it is where the server puts a plugin's
configuration. The host decides that and this plugin does not choose it:

    ### MediaBrowser.Common.Plugins.BasePlugin`1  [MediaBrowser.Common.dll]
        System.String get_ConfigurationFilePath()
        System.Void SaveConfiguration(TConfigurationType)

    ### MediaBrowser.Common.Configuration.IApplicationPaths  [MediaBrowser.Common.dll]
        System.String get_PluginConfigurationsPath()

Read out of the reference assemblies, `jellyfin.common` at `10.11.11` on `net9.0` and at
`12.0.0-rc4` on `net10.0`, with identical output on both. **The `net9.0` target compiles against
`10.11.0` since #360**, which is the floor `build.yaml` claims, and the three members are in that
package too:

    for v in 10.11.0 10.11.11; do echo "--- jellyfin.common $v ---"; tr -d '\000' < ~/.nuget/packages/jellyfin.common/$v/lib/net9.0/MediaBrowser.Common.dll | grep -oE 'get_ConfigurationFilePath|SaveConfiguration|get_PluginConfigurationsPath' | sort | uniq -c; done
    --- jellyfin.common 10.11.0 ---
          1 get_ConfigurationFilePath
          1 get_PluginConfigurationsPath
          1 SaveConfiguration
    --- jellyfin.common 10.11.11 ---
          1 get_ConfigurationFilePath
          1 get_PluginConfigurationsPath
          1 SaveConfiguration

That is a count of the name in the assembly rather than a signature, which is a weaker reading than
the block above it and is stated as one. A plugin that
wrote its secret somewhere else would be a plugin whose secret is not in the operator's backup and
not in their restore, which is a worse failure than the one it would be avoiding.

### Who on the machine can read it

Everybody who can read that file, and the list is longer than an operator expects.

The account the server runs as, which is what reads it on every start. Anybody with administrative
rights on the machine. Anybody holding a backup of the server's data directory, because the file is
in it by design and the row in `storage.md` says it is required. Any other plugin loaded into the
same server process, which can read the file directly, and that is the same position
[`seam.md`](seam.md) takes about a caller inside the process: anything in there can already read this
plugin's files.

And an administrator of the server over the API, because the dashboard reads a plugin's configuration
back in order to render its page. This plugin's own page does exactly that for the settings that
exist today:

    git grep -n "getPluginConfiguration" -- Jellyfin.Plugin.Requests/Configuration/configPage.html
    Jellyfin.Plugin.Requests/Configuration/configPage.html:325:                                return ApiClient.getPluginConfiguration(RequestsConfig.pluginUniqueId);
    Jellyfin.Plugin.Requests/Configuration/configPage.html:338:                        ApiClient.getPluginConfiguration(RequestsConfig.pluginUniqueId)

So the protection a credential gets here is the protection the server's data directory gets, and this
page claims no more than that.

### What is refused by name

**Encrypting it at rest with a key on the same disk.** The key has to be readable by the same process
that reads the file, so anything that can read one can read the other, and what it buys is the
appearance of protection. That is worse than buying nothing: a page saying the credential is
encrypted changes what an operator does about a stolen backup. It is refused here rather than left as
an idea somebody has later and half builds.

**A second place to put it.** An environment variable or a file of its own would be somewhere the
operator's backup does not reach and somewhere the dashboard cannot show, which trades one honest
exposure for two ways to lose the value.

### What is claimed

Four things, and each is a rule about this plugin's own code rather than about the machine. It is
never written to a log. It is never included in anything a diagnostics route produces. It is never
returned to a page or an endpoint that does not need it. And it is never sent anywhere except the
service the operator configured.

**Two of the four are held by something that runs, and two are held by the shape of the wire.** The
setting carries `[Secret]`, the invariant lint rule `no-marked-setting-in-a-message` names it in both
of its pattern lines, and `EveryMarkedSettingIsNamedByARuleInTheInvariantLint` reds if the two ever
part; that is the first condition of #85. `AFailedHandoverWritesNoPartOfTheKeyToTheLog` drives a
submission into a refused connection through the real submission path, which logs the whole
exception, and asserts the key is in no line at any level; that is #85's second. What holds the other
two is that the key travels only as a header: the platform's exception names the destination and not
the headers, so nothing this plugin hands a logger can carry it, and the address it is sent to is the
one the operator wrote. Neither of those two is a check, and `NoCallCarriesTheKeyAnywhereButTheHeader`
is what stands between them and a change that puts the key in a query string.

## The adapter, and what it sends

[`OverseerrBackend`](../Jellyfin.Plugin.Requests/Bridge/Overseerr/OverseerrBackend.cs) is the one
adapter, it speaks the form the section below quotes, and it is configured by three settings on the
plugin's page: the root of the service, the key the service issued, and the account mapping above.
[`configuration.md`](configuration.md) carries each one with its default and what a wrong value is
refused for.

**With no address written it is `NoRequestBackend`.** Every call reads the settings first, and where
no address is set it hands the call to the bridge with nothing behind it and adds nothing. That is
what every install runs until an operator types an address, and it is why the register above did not
grow: nothing that takes the bridge changed.

**What an approval sends, in the form's own field names.** `mediaType`, which is `movie` or `tv`;
`mediaId`, which is the request's TMDB identifier and nothing else; for a series, `seasons`, which is
the list that was asked for or the form's own word `all` where the whole show was; and `userId`, only
for a person with a row in the mapping, carrying the number the operator wrote for them. Nothing else
this side holds is on the wire: no title, no year, no other provider's identifier, no Jellyfin user
identifier, no name. `AFilmIsPostedInTheFormAndTheNumberTheServiceAnswersWithIsKept`,
`ASeriesCarriesTheSeasonsAskedForAndTheWholeShowWhereNoneWereNamed` and
`AMappedPersonArrivesUnderTheirAccountAndAnUnmappedOneUnderNobodys` read the body back off an
in-process service and are what hold that list. [`personal-data.md`](personal-data.md) counts the same
fields as what leaves.

**A request with no TMDB identifier is not handed over, and the queue says so.** That is the first of
the three answers the section below sets out, taken on #315 on 2026-09-02: the handover is refused
before anything is sent, the approval stands, the moment is written onto the request on the path
`HandoverFailedAt` already carries, and the log line beside it names what is missing. The operator's
queue draws it as approved and not handed over, which is a state a person can read and act on by
adding the identifier. Not the second answer, because requiring a TMDB identifier at approval rewrites
the approval rule and every surface that creates a request for the sake of one backend's key space,
and would refuse requests this side can identify perfectly well. Not the third, because searching a
title is refused by name by the identity rule, and the form's own description shows why: three
searches that take text and no route from one number to another. If a route from an IMDb or TVDB
number to a TMDB one ever exists on that side, it is a lookup and not a search, and it is an issue of
its own then. `ARequestWithNoTmdbIdentifierIsRefusedBeforeAnythingIsSent` holds the refusal to
"before anything is sent".

**The number-to-word step lives in
[`OverseerrWords`](../Jellyfin.Plugin.Requests/Bridge/Overseerr/OverseerrWords.cs) and nowhere else.**
The section below records that where that step lives was a decision this adapter had to take rather
than discover, and the answer is a second table beside the first: five request-status numbers, each
to the word the mapping table holds it under, read by nothing but the adapter. `OverseerrWordsTests`
compares the two tables in both directions, so a word this step produces is always a row the mapping
holds and every request-status row the mapping holds has a number here. A number the step does not
know is reported as its own digits, so the mapping table's rule for an unseen word is what fires, and
the reconciliation's log line then carries a number an operator can look up rather than a word this
side guessed.

**What a report carries is the request's status and not the media's.** A report is one word, the
reconciliation looks it up in both vocabularies, and the request's own status is the fact that says
what happened to the request; the media status beside it in the same answer is read by nothing. So
the `MediaStatus` row of the mapping above is one this adapter never produces, which is stated here
so that nobody reads its presence as a claim about this form.

**What the adapter refuses to carry, and where a failure goes.** The credential is a header on every
call and never part of an address or a body, which `NoCallCarriesTheKeyAnywhereButTheHeader` asserts
over all four calls. A refused call is an exception naming the status the service answered and never
the body it answered with, because the body is whatever the thing at that address chose to send. A
body that is not JSON, and JSON with no identifier or no status in it, are failures and never a
reference or a word; those are the two cases #35 named and could not reach before something here read
a body. A call that runs past ten seconds is given up on as a timeout and not as a cancellation,
because the reconciliation stops its whole run on a cancellation and one slow answer is one request
left as it is. Which of those failures are told apart, and what a bound retry is, is still #86.

**`Reachable` still means the status route answered**, and the section below says what that does not
say: that route takes no credential, so a green answer is a service that is up and nothing about
whether the key is accepted.

**One request has made the round trip against a running service, on both claimed lines.**
`scripts/verify-bridge-round-trip.sh` starts Jellyseerr `2.7.3`, pinned by digest in the script,
beside a server of each line, walks its own first sign-in against an administrator made on that
server, reads the key it issued, points the plugin at it through the server's configuration route,
has a person ask for a film by TMDB number and an operator approve it, and reads the request back out
of the service's own list; then it runs `ReconciliationTask` and reads the request here again. Run
`33731657684` on this board, jobs `100572739356` for 10.11 and `100572739618` for 12.0, is the first
green one, and the two lines that carry the measurement read the same on both:

    id=1  type=movie  status=2  media.tmdbId=550  media.status=3  requestedBy.id=1
    state=Approved  backend={"Service": "overseerr", "Id": "1"}  handoverFailedAt=None

So the submission's field names are the ones a real service takes, the number it answers with is the
one this side keeps, the status it reports is `2`, which `OverseerrWords` turns into `APPROVED` and
the table above holds as inert, and the request stays approved here after the reconciliation. The
media status `3` beside it is `PROCESSING` in the form's own enumeration below and is read by
nothing here, as the paragraph above says.

**What that run does not say.** It is Jellyseerr and not Overseerr proper, because Overseerr's only
described route creating the first user takes a Plex account token, and Jellyseerr's takes a Jellyfin
username and password the job makes and forgets; whether Overseerr proper behaves the same is not
measured. The service has no download client, so its own log says the request is skipped rather than
fetched, and nothing downstream of the service is exercised. No failure path is walked: a refused
key, a service that goes away and a word the table has not seen are the suite's legs and #86's
question. And one server setting is turned on for the service on the 12.0 line: Jellyseerr `2.7.3`
names its client in the `X-Emby-Authorization` header, which a 12.0 server reads only while
`EnableLegacyAuthorization` is on, and it is off there by default. The first run of the procedure met
that as a `400` on the service's sign-in, the procedure now turns the switch on where the server has
it and prints what it found, and an operator running that pair of versions meets the same wall.

## Where the list of words came from

The words above are the ones issue #81 names for the Overseerr form, which is the form the first
adapter is written against, decided on #113. **They were not read off a running service, and nothing
in the suite can read one.** No fixture here was captured from a service and the suite makes no
outbound call. One of the six has since been met on a running Jellyseerr, in the round trip the
adapter's section above records: the service reported `2` and `OverseerrWords` turned it into
`APPROVED`. The other five have not been seen from a service, and the rule below is what stands
between that and a wrong answer.

What has been read is that form's own published description, and the comparison against it is the
section below. That is a weaker reading than a running service and a stronger one than this table had
before it, and it leaves the sentence above exactly as it stands: a document describing a service is
not the service, and a description can be behind the implementation it describes.

So the list is still this board's own statement of the vocabulary rather than a measurement of a
running one, and the rule for an unseen word is what stands between that and a wrong answer. Whoever
writes the adapter against an instance is the first person in a position to compare the two against
the thing itself, and a word that arrives from a real service and is not here is a row this table is
missing rather than a fault in the service.

## The form's own description, read

Fetched on 2026-08-30 from the form's own repository, which is the only description of it anything
here has read:

    curl -sS -o overseerr-api.yml -w "http=%{http_code} bytes=%{size_download}\n" \
      https://raw.githubusercontent.com/sct/overseerr/develop/overseerr-api.yml
    http=200 bytes=177902

Everything below is quoted out of that file rather than summarised, so a reader can disagree with the
source rather than with this page. Nothing in this repository fetches it and no check compares this
page against it, so these quotations go stale in silence, and the command above is what a later reader
re-runs rather than trusting them.

### Where the calls go, and what carries the credential

    servers:
      - url: '{server}/api/v1'

    securitySchemes:
      cookieAuth:
        type: apiKey
        name: connect.sid
        in: cookie
      apiKey:
        type: apiKey
        in: header
        name: X-Api-Key

**The credential travels in a header rather than in a query string**, and that is what makes one of
the four claims above cheap to keep true instead of impossible. A transport failure names its
destination in the exception the platform raises, which is the half of the leak no wording of this
plugin's own log lines can fix; with the key in a header that exception carries the address and not
the credential. The cookie scheme beside it is how to get this wrong, and an adapter that ever
authenticated by a route putting the value in a URL would put the leak back where it started.

### The four operations, against the four this side has

`IRequestBackend` has four and no more, and each one lands on a path item in that document.

| This side             | The form                      | What it answers                                   |
| --------------------- | ----------------------------- | ------------------------------------------------- |
| `CheckReachableAsync` | `GET /status`                 | that the service is up, and nothing about the key |
| `SubmitAsync`         | `POST /request`               | `201` with the request the service created        |
| `ReportAsync`         | `GET /request/{requestId}`    | `200` with that request                           |
| `WithdrawAsync`       | `DELETE /request/{requestId}` | `204` and no body                                 |

**`/status` is answered without a credential**, and for the first row that is a problem rather than a
convenience:

    /status:
      get:
        summary: Get Overseerr status
        security: []

A green answer from it says the service is up and says nothing about whether the key is accepted, so
`Reachable` read off `/status` alone reports a working bridge on an install whose credential is
wrong. Which call answers the second question is #86's and is not decided here.

**A submission has two required fields and every other one is optional:**

    mediaType:
      type: string
      enum: [movie, tv]
      example: movie
    mediaId:
      type: number
      example: 123

    required:
      - mediaType
      - mediaId

This side has a kind, a title and whatever external identifiers the request arrived with. What a
`mediaId` is over there is answered below. Where an adapter gets one is answered for most requests
by a rule this side already enforces, and is a decision nobody has taken for the rest.

### `mediaId` is a TMDB identifier, and the description does not say so

It is given as a number with an example and nothing about whose number it is. What the service does
with the value says which one. Fetched on 2026-08-31, from the same repository as the description
above:

    curl -sS -o MediaRequest.ts -w "http=%{http_code} bytes=%{size_download}\n" \
      https://raw.githubusercontent.com/sct/overseerr/develop/server/entity/MediaRequest.ts
    http=200 bytes=19586

    grep -n 'tmdb.getMovie\|tmdb.getTvShow\|tmdbId: requestBody.mediaId' MediaRequest.ts
    116:        ? await tmdb.getMovie({ movieId: requestBody.mediaId })
    117:        : await tmdb.getTvShow({ tvId: requestBody.mediaId });
    121:        tmdbId: requestBody.mediaId,

The value posted is handed straight to that service's TMDB client as a film or a programme
identifier and is then stored as `tmdbId`. So a submission from here carries the TMDB identifier of
the thing that was asked for, and no other provider's number will do. This is read off the
implementation rather than the description, which is the same weaker-and-stronger reading as the
numbering above and goes stale in the same silence.

**Most requests that reach a submission carry an identifier, and that is a rule here rather than a
hope.** Nothing is handed over until an operator has approved it, and an approval is refused on a
request carrying no external identifier at all:

    git grep -n 'RequestNotIdentifiedException(to)' -- Jellyfin.Plugin.Requests/Model/RequestLifecycle.cs
    Jellyfin.Plugin.Requests/Model/RequestLifecycle.cs:387:            throw new RequestNotIdentifiedException(to);

**What that rule does not promise is the one this call needs.** It asks for an identifier and not for
a TMDB one, so a request identified by an IMDb or a TVDB number alone is approvable here and has no
`mediaId` over there. The described API offers no way to turn one number into the other. Its three
searches take text:

    grep -n '^  /search' overseerr-api.yml
    4115:  /search:
    4163:  /search/keyword:
    4203:  /search/company:

    sed -n '4121,4127p' overseerr-api.yml
          parameters:
            - in: query
              name: query
              required: true
              schema:
                type: string
                example: 'Mulan'

Searching that text and taking a result would be this plugin deciding what a title means, which is
the one thing the identity rule refuses by name:

    git grep -n 'Identity is a provider identifier and a kind' -- Jellyfin.Plugin.Requests/Model/RequestIdentity.cs
    Jellyfin.Plugin.Requests/Model/RequestIdentity.cs:16:/// <b>Identity is a provider identifier and a kind, never a title.</b> Titles collide, get

So what an adapter does with an approved request that carries no TMDB identifier is a decision and
not a lookup. Three answers are available and this page takes none of them: refuse the handover and
leave the request approved with its failure recorded, which is the path `HandoverFailedAt` already
carries; require a TMDB identifier at approval rather than any identifier, which is a change to the
rule above and to every surface that creates a request; or ask the service to search a title, which
the sentence above refuses. It is owed on #315.

**A withdrawal can be refused for a reason that is about the credential rather than about the
request**, which that document says in prose and not in a status code:

    delete:
      summary: Delete request
      description: Removes a request. If the user has the `MANAGE_REQUESTS` permission, any request can be removed. Otherwise, only pending requests can be removed.

Everything this plugin hands over has already been approved here, and a request that side has
approved is not pending, so the ordinary withdrawal is exactly the one a caller without that
permission may not make.

### The two alphabets are numbers, and this table is words

    status:
      type: number
      example: 0
      description: Status of the request. 1 = PENDING APPROVAL, 2 = APPROVED, 3 = DECLINED
      readOnly: true

    status:
      type: number
      example: 0
      description: Availability of the media. 1 = `UNKNOWN`, 2 = `PENDING`, 3 = `PROCESSING`, 4 = `PARTIALLY_AVAILABLE`, 5 = `AVAILABLE`, 6 = `DELETED`

**A number-to-word step exists and lives nowhere.** The table above is data so that two adapters
cannot disagree about what a word means with nothing saying which is right. An adapter that turns `3`
into `DECLINED` on its own moves that argument one layer down, where the table stops being the place
it is settled. Where the step lives is a decision the adapter has to take rather than discover.

**Two of the five request words this table holds are absent from those three, and the same document
shows the service using both.** `FAILED` and `COMPLETED` are not among `1`, `2` and `3`, and they are
in the alphabet that document gives the listing filter:

    - in: query
      name: filter
      schema:
        type: string
        nullable: true
        enum:
          [
            all,
            approved,
            available,
            pending,
            processing,
            unavailable,
            failed,
            deleted,
            completed,
          ]

So both rows are words the service knows, at the only place in that document they appear, and the
numbers they arrive as are written down nowhere in it.

**The numbers are in the implementation instead, and the description above is behind it.** The same
repository declares both alphabets as enumerations, and the request-status one has five members where
the description names three:

    curl -sS -o media.ts -w "http=%{http_code} bytes=%{size_download}\n" \
      https://raw.githubusercontent.com/sct/overseerr/develop/server/constants/media.ts
    http=200 bytes=272

    export enum MediaRequestStatus {
      PENDING = 1,
      APPROVED,
      DECLINED,
      FAILED,
      COMPLETED,
    }

    export enum MediaStatus {
      UNKNOWN = 1,
      PENDING,
      PROCESSING,
      PARTIALLY_AVAILABLE,
      AVAILABLE,
      DELETED,
    }

A member of a TypeScript numeric enumeration that carries no value of its own takes one more than its
predecessor, so the request statuses run `PENDING` 1, `APPROVED` 2, `DECLINED` 3, `FAILED` 4,
`COMPLETED` 5, and the media statuses run `UNKNOWN` 1 through `DELETED` 6.

**That is the number-to-word step's data and it is not this table's.** Every word in the mapping above
is accounted for, `FAILED` being 4 and `COMPLETED` 5 rather than absent, so the row that moves a
request on evidence only the service holds has a number behind it. Where the step that reads it lives
is still the decision named above, and it is still not taken here.

**Five of the six media values have no row**, and the rule for an unseen word is what makes that safe
rather than silently wrong: `UNKNOWN`, `PENDING`, `PROCESSING`, `PARTIALLY_AVAILABLE` and `DELETED`
move nothing and are reported as unseen. `DELETED` is the one to look at before an adapter ships,
because media a service no longer holds is a fact this side may want, and today it is a word nothing
here recognises.

### What was not read

Nothing in this section is off a running instance. Every reading above is of a branch of that
project's own repository, which is what that project intends to ship rather than what any operator
is running: the two disagreeing about the request-status alphabet is the demonstration that a
document and a service are different things, and a numbering read from source is subject to the
same gap. What has been read off an instance is the round trip in the adapter's section above, and
its reach is exactly the calls it makes: one submission, one report answering `2`, and the status
route. No response of a service is captured in this tree; the run's log is where the answers are,
and the section above quotes the two lines that carry the measurement.

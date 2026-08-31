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

**There is no adapter in this tree, and #315 is the issue that asks for one.** #82 closed as
completed on 2026-08-23 having built the submission path behind the interface, and it did not produce
a client that speaks to a service: the only implementation of the interface is still the one for a
server without one. Whether such an adapter is written on this board at all was an open call recorded
on #113, and that call is taken: one is written here, as its own module. So every sentence below that
says something arrives with the adapter is waiting on work somebody can pick up rather than on a
decision nobody has taken.

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

Where the table is kept and how it is edited arrives with the adapter that reads it, which is #315.
Nothing on this side reads the table today, so a settings field for it now would be somewhere to type
something nothing uses.

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

**What is not claimed.** No run of this task on a server has been watched, and no service was
reached: what the suite measures is the reconciliation against a double, on both claimed target
frameworks. Which failures an adapter tells apart, and what each of them then does, is #86 and is
not decided here. And **the person who asked is not told when their request fails this way** - no
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

The other half of the same claim is that only one implementation ships. Every server this plugin runs
on resolves `NoRequestBackend` until an adapter replaces that one registration, and the suite refuses
a second implementation arriving in the plugin assembly unnamed for the same reason.

## Where a credential would live, and what may be claimed about it

**There is no credential today and nothing here holds one.** The only implementation of the bridge in
this tree is the one for a server that has no service, it makes no call, and the configuration
carries no field for an address or for a secret. Everything below is what will be true the day the
adapter adds one, written now so that it is a position rather than a description written afterwards
to fit whatever was built.

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

Read out of the reference assemblies each target framework compiles against, `jellyfin.common` at
`10.11.11` on `net9.0` and at `12.0.0-rc4` on `net10.0`, with identical output on both. A plugin that
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
    Jellyfin.Plugin.Requests/Configuration/configPage.html:220:                                return ApiClient.getPluginConfiguration(RequestsConfig.pluginUniqueId);
    Jellyfin.Plugin.Requests/Configuration/configPage.html:233:                        ApiClient.getPluginConfiguration(RequestsConfig.pluginUniqueId)

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

**None of the four is enforced, because there is nothing yet to enforce them over.** The lint rule
that would refuse a log or a diagnostics call reaching it, and the test that would assert it does not
appear in a log written during a bridge failure, are the other two conditions of #85, and both
quantify over a value that does not exist. They land with the adapter and not before.

## Where the list of words came from

The words above are the ones issue #81 names for the Overseerr form, which is the form the first
adapter is written against, decided on #113. **They were not read off a running service, and nothing
in this tree can read one.** No fixture here was captured from a service and the suite makes no
outbound call.

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

This side has a kind and a title. What a `mediaId` is over there, and where an adapter gets one, is
the question that answer opens, and this page does not close it.

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

Nothing off a running instance, and that is the same disclosure as the one above rather than a softer
version of it. No call has ever been made from this tree to a service of this form and no response of
one has ever been captured here. Both readings above are of a branch of that project's own
repository, which is what that project intends to ship rather than what any operator is running:
the two disagreeing about the request-status alphabet is the demonstration that a document and a
service are different things, and a numbering read from source is subject to the same gap.

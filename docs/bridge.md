# The bridge to an external request service

Most servers run nothing of the sort, and on those this plugin is the whole system. Where an
operator does run one, an approved request can be handed to it and the two systems then hold an
opinion each about the same thing. This page is about the one place those opinions have to be
reconciled: what the service's words mean here.

It is not the whole of the bridge. The interface is
[`IRequestBackend`](../Jellyfin.Plugin.Requests/Bridge/IRequestBackend.cs), the implementation every
server without a service runs is
[`NoRequestBackend`](../Jellyfin.Plugin.Requests/Bridge/NoRequestBackend.cs), and how a submission is
made and what happens when the service misbehaves are separate questions this page does not answer.
Where a credential lives is answered at the end of it.

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

Where the table is kept and how it is edited arrives with the adapter that reads it, in #82. Nothing
on this side reads it today, so a settings field for it now would be somewhere to type something
nothing uses.

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

## What needs a bridge

Nothing does, and that is a claim the suite refuses to let drift rather than a sentence somebody
wrote once. The register below is every part of this plugin that touches the bridge at all, with
what it does on a server that has none, which is most of them.

`NoBackendCompletenessTests` compares this table against the assembly. Anything that takes
`IRequestBackend` and is not named here reds the suite, so a feature that only works with a bridge
cannot arrive quietly: it either gets a line in this table saying so, or the change does not land.

<!-- needs-a-bridge begins -->

| What                     | Without a bridge                                |
| ------------------------ | ----------------------------------------------- |
| `CapabilitiesController` | Answers, and says that no bridge is configured. |

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
adapter in #82 adds one, written now so that it is a position rather than a description written
afterwards to fit whatever was built.

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
    Jellyfin.Plugin.Requests/Configuration/configPage.html:145:                                return ApiClient.getPluginConfiguration(RequestsConfig.pluginUniqueId);
    Jellyfin.Plugin.Requests/Configuration/configPage.html:158:                        ApiClient.getPluginConfiguration(RequestsConfig.pluginUniqueId)

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
quantify over a value that does not exist. They land with the adapter in #82 and not before.

## Where the list of words came from

The words above are the ones issue #81 names for the Overseerr form, which is the form the first
adapter is written against, decided on #113. **They were not read off a running service, and nothing
in this tree can read one.** No fixture here was captured from a service, no schema was fetched, and
the suite makes no outbound call.

So the list is this board's own statement of the vocabulary rather than a measurement of it, and the
rule for an unseen word is what stands between that and a wrong answer. Whoever writes the adapter in
#82 is the first person in a position to compare the two, and a word that arrives from a real service
and is not here is a row this table is missing rather than a fault in the service.

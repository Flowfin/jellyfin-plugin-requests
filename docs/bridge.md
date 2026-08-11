# The bridge to an external request service

Most servers run nothing of the sort, and on those this plugin is the whole system. Where an
operator does run one, an approved request can be handed to it and the two systems then hold an
opinion each about the same thing. This page is about the one place those opinions have to be
reconciled: what the service's words mean here.

It is not the whole of the bridge. The interface is
[`IRequestBackend`](../Jellyfin.Plugin.Requests/Bridge/IRequestBackend.cs), the implementation every
server without a service runs is
[`NoRequestBackend`](../Jellyfin.Plugin.Requests/Bridge/NoRequestBackend.cs), and how a submission is
made, what happens when the service misbehaves and where a credential lives are separate questions
this page does not answer.

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

## Where the list of words came from

The words above are the ones issue #81 names for the Overseerr form, which is the form the first
adapter is written against, decided on #113. **They were not read off a running service, and nothing
in this tree can read one.** No fixture here was captured from a service, no schema was fetched, and
the suite makes no outbound call.

So the list is this board's own statement of the vocabulary rather than a measurement of it, and the
rule for an unseen word is what stands between that and a wrong answer. Whoever writes the adapter in
#82 is the first person in a position to compare the two, and a word that arrives from a real service
and is not here is a row this table is missing rather than a fault in the service.

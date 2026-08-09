# The request lifecycle

A request is in one of five states, and which moves between them are allowed is a table rather than
a chain of conditionals. The table is data, in
[`RequestLifecycle.Table`](../Jellyfin.Plugin.Requests/Model/RequestLifecycle.cs), and everything
below is printed from it: `RequestLifecycleTests` compares the grid and the reasons on this page
against that table and fails if either side changes without the other.

## The five states

`Open` is where a request starts. Nothing has been decided and nobody has necessarily looked at it.

`Approved` means an operator said yes. It does not mean the server has the media, and the gap
between the two is the ordinary case rather than an edge one.

`Declined` means an operator said no. A reason is required, decided on #113.
`RequestLifecycle.Decline` is the only way into this state and it takes one, so a decline with no
reason cannot be made. The short list is `DeclineReason`, and anything it does not cover is `Other`,
which requires the free text beside it, so the escape hatch cannot be used to give no reason at all.
Leaving `Declined` clears the reason, because a reason standing on an approved request is a sentence
that is no longer true.

`Fulfilled` means the thing that was asked for is in the library and the person who asked can watch
it.

`Failed` means it was approved, sent onward, and did not arrive. Without it, a request that the
external service accepted and then dropped sits in `Approved` forever looking like an operator
forgot about it. It was added on the decision recorded on #113, which also refused a `Cancelled`
state: a user withdrawing a request is a second road to "finished" that an operator does nothing
different about.

State says nothing about whether the server holds the media. That is `LibraryAvailability`, it is a
separate field on the record, and several of the refusals below only make sense because it exists.

## The table

Rows are the state being left, columns are the state being entered.

<!-- grid begins -->

| From      | Open    | Approved | Declined | Fulfilled | Failed  |
| --------- | ------- | -------- | -------- | --------- | ------- |
| Open      | refused | allowed  | allowed  | allowed   | refused |
| Approved  | refused | refused  | allowed  | allowed   | allowed |
| Declined  | refused | allowed  | refused  | refused   | refused |
| Fulfilled | refused | refused  | refused  | refused   | refused |
| Failed    | refused | allowed  | allowed  | allowed   | refused |

<!-- grid ends -->

Ten of the twenty-five moves are allowed. Every pair has a cell, including a state paired with
itself, so a sixth state is eleven new cells rather than a silent widening.

## Why each cell reads the way it does

<!-- reasons begins -->

- **Open to Open**: refused. A move to the state it is already in is not a move, and appending a history entry saying nothing happened makes the history harder to read rather than more complete.
- **Open to Approved**: allowed. An operator says yes.
- **Open to Declined**: allowed. An operator says no.
- **Open to Fulfilled**: allowed. The library already holds what was asked for, so there is nothing left for anybody to decide.
- **Open to Failed**: refused. Nothing was ever sent onward, so there is nothing that could have failed.
- **Approved to Open**: refused. Nothing returns to open. A request reading as undecided after somebody decided it hides that decision from the next person to look at the queue.
- **Approved to Approved**: refused. A move to the state it is already in is not a move, and appending a history entry saying nothing happened makes the history harder to read rather than more complete.
- **Approved to Declined**: allowed. An operator takes an approval back, and the reason says why. This is the repair for an approval given by mistake.
- **Approved to Fulfilled**: allowed. It arrived and the person who asked can watch it.
- **Approved to Failed**: allowed. It was sent onward and did not arrive, so it stops looking like an operator forgot about it.
- **Declined to Open**: refused. Nothing returns to open. A request reading as undecided after somebody decided it hides that decision from the next person to look at the queue.
- **Declined to Approved**: allowed. An operator changes their mind. One request carrying both moves beats asking the person who was refused to ask again.
- **Declined to Declined**: refused. A move to the state it is already in is not a move, and appending a history entry saying nothing happened makes the history harder to read rather than more complete.
- **Declined to Fulfilled**: refused. A declined request whose title later appears in the library is an availability observation and not a decision. Approving it first is the move that says a person changed the answer.
- **Declined to Failed**: refused. Nothing was ever sent onward, so there is nothing that could have failed.
- **Fulfilled to Open**: refused. Fulfilled is the end of this request. A file that turns out to be the wrong one is a new request for the right one, and a library that stops holding it is an availability observation rather than a decision being undone.
- **Fulfilled to Approved**: refused. Fulfilled is the end of this request. A file that turns out to be the wrong one is a new request for the right one, and a library that stops holding it is an availability observation rather than a decision being undone.
- **Fulfilled to Declined**: refused. Fulfilled is the end of this request. A file that turns out to be the wrong one is a new request for the right one, and a library that stops holding it is an availability observation rather than a decision being undone.
- **Fulfilled to Fulfilled**: refused. A move to the state it is already in is not a move, and appending a history entry saying nothing happened makes the history harder to read rather than more complete.
- **Fulfilled to Failed**: refused. Fulfilled is the end of this request. A file that turns out to be the wrong one is a new request for the right one, and a library that stops holding it is an availability observation rather than a decision being undone.
- **Failed to Open**: refused. Nothing returns to open. A request reading as undecided after somebody decided it hides that decision from the next person to look at the queue.
- **Failed to Approved**: allowed. An operator sends it onward again.
- **Failed to Declined**: allowed. An operator gives up on it, and the reason says why. Without this a failure has no ending.
- **Failed to Fulfilled**: allowed. It arrived after all, by this route or by somebody putting it in the library by hand.
- **Failed to Failed**: refused. A move to the state it is already in is not a move, and appending a history entry saying nothing happened makes the history harder to read rather than more complete.

<!-- reasons ends -->

## The three cells people disagree about

**Can a declined request be reopened?** It can be approved, and it cannot go back to open. An
operator changing their mind is ordinary, and the alternative is asking the person who was refused
to ask again, which loses the connection between the two attempts and depends on that person still
being around. Going back to `Open` is different: it would erase the fact that somebody decided, and
the queue would show the request as untouched.

**Can an approval be taken back?** Yes, by declining it. That leaves both moves in the history, so
an operator dealing with a complaint can see that the request was approved on Tuesday and declined
on Wednesday, which is what happened.

**Can a fulfilled request return to an earlier state?** No. A file that turns out to be the wrong
one is a request for the right one, and it is a new request because the old one describes what was
asked for and got an answer. A library that no longer holds the media is an observation, which the
`Availability` field on the record already carries with the time it was made.

## What is not enforced

`RequestLifecycle.Move` and `RequestLifecycle.Decline` are the two places in the plugin that change
a state, and both refuse a move this table refuses. Nothing stops a caller writing
`with { State = ... }` on the record instead: the record is immutable, and immutability makes a move
produce a new value rather than making it go through here. There are no callers yet, so the property
holds today by there being nothing to hold it against. The invariant lint in #28 is where a rule
refusing that shape would live, and this paragraph is what it does not yet do.

The history of the moves a request has made is #43, and it is not built here. `StateChangedAt` and
`StateChangedByUserId` on the record are the current move only, and a request that has been approved
and then declined carries no trace of the approval until that issue lands.

# Configuration

Almost every install runs the defaults, so the defaults are the product. This page is the list of
what an operator can change, what happens when they change nothing, and why each default is the
conservative answer rather than the convenient one.

The settings are in
[`PluginConfiguration`](../Jellyfin.Plugin.Requests/Configuration/PluginConfiguration.cs), edited on
the plugin's settings page in the dashboard. The table below is compared against that class by
`PluginConfigurationTests`, which fails if a setting is added, removed or given a different default
without this page moving with it, and again if the page cannot reach one of them.

## The settings

<!-- settings begins -->

| Setting                      | Default |
| ---------------------------- | ------- |
| AcceptsMovies                | true    |
| AcceptsSeries                | true    |
| FinishedRequestRetentionDays | 365     |
| OpenRequestsPerUser          | 10      |

<!-- settings ends -->

**AcceptsMovies** and **AcceptsSeries** are on, and they are the two kinds this version knows how to
recognise in a library. Accepting a kind nothing can match would leave a person waiting on a request
that no fulfilment check can ever answer, so the accepted set and the recognised set are the same
set. Which kinds ship at 1.0 was decided on #113. Turning both off leaves nothing anybody can ask
for, which is a configuration that cannot work rather than a strict one; refusing it is #96.

A kind is a setting of its own rather than an entry in a list. A third kind then has to move this
page, the class and the settings form together, instead of arriving as an appended value that
reaches a fulfilment check with no rule for it.

**FinishedRequestRetentionDays** is 365, and the floor under it is 30. A request record says that a
named person asked for a named title on a date. That is personal data, it is more revealing than
most of what a media server holds, and it accumulates forever unless something removes it. A year is
the span in which somebody asks "did I already ask for this", which is what keeping any history is
for. The number was decided on #113 and is a field rather than a constant so an operator with a
different answer changes it without waiting for a release. The floor is there so retention cannot be
set to nothing: zero would delete the history quietly and leave the queue answering that question
with no.

What removes an expired request is #49, and it is not built yet. **Until it is, nothing enforces this
number**, and a server running this plugin keeps every finished request. That is a gap rather than a
setting an operator can rely on, and it is written here rather than left for somebody to discover
from a store that never shrinks.

**OpenRequestsPerUser** is 10. The quota is the only thing between one person and the whole disk,
and a limit introduced after people have habits is enforced against those habits rather than in
front of them, which is why it ships from the start rather than later. Ten is a number somebody has
to be trying to reach. It counts what is open rather than what was ever asked, so a person whose
requests are answered can keep asking.

Where the quota is enforced is #114, and that is not built yet either. **Nothing refuses an
eleventh open request today.**

## A fresh install

Nothing has to be configured for the plugin to be usable: a person can ask for a film or a series,
an operator sees it in the queue and answers it, and the library is what says a request was
fulfilled.

Nothing is sent anywhere. There is no address to send to, no credential to hold and no switch that
would turn sending on, because none of the paths that would carry it exist yet: the outbound
notification sink is #78 and the bridge adapter is #82. `NoRequestBackend` is what a server without
an external request service runs, and it is what every server runs today.

## What is deliberately not a setting

**Whether a request needs approval.** Approval is required. Automatic approval was decided on #113
as a per-user setting rather than a switch for the whole server, so a boolean here would be the
wrong shape rather than an early version of the right one.

**Notification switches.** Every path they would turn off is unbuilt. Switches for paths that do not
exist would be settings an operator can change with no effect, which is worse than their absence.
They land with the paths, in #79.

**A bridge address and a credential.** The only bridge in this tree is the one for a server that has
none. A field for an address would be somewhere to type something nothing reads. #82 brings the
adapter, and #85 decides where a credential is kept and what may honestly be claimed about it.

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
| OutboundNoticeAddress        |         |

<!-- settings ends -->

**AcceptsMovies** and **AcceptsSeries** are on, and they are the two kinds this version knows how to
recognise in a library. Accepting a kind nothing can match would leave a person waiting on a request
that no fulfilment check can ever answer, so the accepted set and the recognised set are the same
set. Which kinds ship at 1.0 was decided on #113. Turning both off leaves nothing anybody can ask
for, which is a configuration that cannot work rather than a strict one, and it is refused rather
than saved.

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

What removes an expired request is the scheduled task **Remove finished requests that have been kept
long enough**, in the `Requests` category of the server's task list. It runs daily and at startup,
and it removes a request that has been fulfilled, declined or failed for longer than this number
says. The period is measured from the move that finished the request rather than from the day it was
asked for, so a request answered after sitting open for a year is kept for the whole period after
the answer. Startup is one of its triggers because a period that only elapses while the machine is
running is not the period that was set.

Removed rather than anonymised. A row that has lost its requester still says the title was asked for
on this server on that date and answers nothing anybody wanted to ask, so the period ends with the
record gone.

**What this does not reach is a request that is still open or approved**, at any age. Those are the
two states somebody still owes an answer or a delivery on, and a request that disappeared on its
anniversary would be this plugin answering it with nothing.

**OpenRequestsPerUser** is 10. The quota is the only thing between one person and the whole disk,
and a limit introduced after people have habits is enforced against those habits rather than in
front of them, which is why it ships from the start rather than later. Ten is a number somebody has
to be trying to reach. It counts what is open rather than what was ever asked, so a person whose
requests are answered can keep asking.

Where the quota is enforced is #114, and that is not built yet either. **Nothing refuses an
eleventh open request today.**

**OutboundNoticeAddress** is empty, and empty is the whole of how the outbound notification path is
turned off. It is the only setting on this page that causes anything to leave this server, and it is
the only one that holds text rather than a number or a switch.

With an address in it, every movement in the queue is posted there as a small JSON document. What
that document carries, what it deliberately does not, and what an operator is agreeing to by typing
an address are in [notifications.md](notifications.md); the same fields are counted as what leaves
the server in [personal-data.md](personal-data.md). Nothing waits for the post and nothing is
retried, so a service that is down costs the messages sent while it was down and costs a request
nothing.

There is no second setting saying whether to use the address. Two ways to say off is one of them
being wrong the day somebody sets the other, and an operator who wants it off empties the field.

## What is refused

A plugin configuration is an XML file on the server, and the dashboard is not the only way one
arrives: an operator can edit that file, and a restore can put an older one back. So the rules below
are read at both moments a configuration reaches this plugin, from the same list, in
[`ConfigurationRules`](../Jellyfin.Plugin.Requests/Configuration/ConfigurationRules.cs).

| Setting                      | Refused when                               | Because                                                                                         |
| ---------------------------- | ------------------------------------------ | ----------------------------------------------------------------------------------------------- |
| OpenRequestsPerUser          | below 1                                    | nobody may have a request open, so every ask is refused by an install that still offers itself  |
| AcceptsMovies, AcceptsSeries | both off                                   | there is nothing anybody can ask for                                                            |
| FinishedRequestRetentionDays | below 30                                   | the history is removed while people are still asking about it                                   |
| OutboundNoticeAddress        | set to something that is not http or https | a notice cannot be posted there, so the sink would send nothing while the page shows an address |

**Nothing is corrected on the way in.** A quota of zero is not raised to one and a retention of five
days is not raised to thirty. An install running a value it substituted does something other than
what its own settings page shows, and there is nothing an operator can read that tells them which of
the two is true. The refusal keeps the value they typed and says which field it is about.

On a save, the refusal reaches the dashboard and the file on disk is not touched, so a number typed
by mistake does not cost the configuration that was working. On a read, whatever asked what this
install is set to is refused instead of being answered, and the sentence lands in the server's log
naming the field. That is the honest limit of the second half: it is a log line rather than a banner
on a page, and a page that says what is wrong with an install is #63.

## A fresh install

Nothing has to be configured for the plugin to be usable: a person can ask for a film or a series,
an operator sees it in the queue and answers it, and the library is what says a request was
fulfilled.

Nothing leaves the server. The outbound notification sink exists and has nowhere to send to, because
`OutboundNoticeAddress` is empty until an operator types one; there is no credential to hold and no
other path that could carry anything, since the bridge adapter is #82 and is not built. What does
happen on a fresh install is that every transition is written to the server's own activity log, which
stays on the machine and is a record rather than a message. What an entry says is
[`notifications.md`](notifications.md).
`NoRequestBackend` is what a server without an external request service runs, and it is what every
server runs today.

## What is deliberately not a setting

**Whether a request needs approval.** Approval is required. Automatic approval was decided on #113
as a per-user setting rather than a switch for the whole server, so a boolean here would be the
wrong shape rather than an early version of the right one.

**Notification switches.** Two paths exist and neither has a switch. The activity log is always on
and is meant to be: it is a record rather than a message, it stays on the server, and an operator who
could turn it off would be able to lose the answer to what happened to a request. The outbound sink
is turned off by having nowhere to send to, which is `OutboundNoticeAddress` above rather than a
switch beside it. The rest are unbuilt, and switches for paths that do not exist would be settings an
operator can change with no effect, which is worse than their absence. They land with the paths, in
#79.

**A bridge address and a credential.** The only bridge in this tree is the one for a server that has
none. A field for an address would be somewhere to type something nothing reads. #82 brings the
adapter; where a credential is kept when there is one, who on the machine can read it, and what is
refused by name are in [`bridge.md`](bridge.md).

## What an uninstall leaves behind

**This plugin removes nothing when it is uninstalled.** Both files it wrote stay where they are, and
they are the two rows of the table under "What is on the disk, and where" in
[storage.md](storage.md).

The reason is that neither file is the plugin's. The queue is what people asked for and the settings
are what an operator chose, and there is no copy of either. The call the server makes carries
nothing that says which kind of uninstall this is, so a removal meant to be final and one that is a
step in putting the plugin back arrive here identically. Deleting on both is a queue lost to a click
somebody may have made by mistake, and there is no undo.

That is a decision with a cost, and the cost is the rest of this section. What is in those files is
personal: a request says a named person asked for a named title on a date. An operator who is
finished with this plugin should be able to remove that in one step, without knowing anything about
how the plugin works.

### Removing what is left

Both paths are relative to the server's data directory. Where that directory is differs by
installation and by operating system, and this document does not name it, for the reason
[storage.md](storage.md) gives: the server is the authority for its own paths. Run these from
inside it, with the server stopped.

On Linux and macOS:

    rm -rf ./plugins/Jellyfin.Plugin.Requests
    rm -f ./plugin-configurations/Jellyfin.Plugin.Requests.xml

On Windows, in PowerShell:

    Remove-Item -Recurse -Force .\plugins\Jellyfin.Plugin.Requests
    Remove-Item -Force .\plugin-configurations\Jellyfin.Plugin.Requests.xml

The first removes the queue and any write that was in flight; the second removes the settings. After
both, nothing this plugin wrote is on the disk.

`WhatTheDocumentedCommandRemovesIsWhatIsActuallyLeft` reads those two paths off the host rather than
out of this document and requires them to appear here, so a data folder or a configuration file that
moves reds the suite instead of leaving an operator running a command that deletes nothing.

### What is not said here

**Nothing measured what the server itself removes.** The paragraphs above are about what this plugin
does when it is told it is going away, which is what `AnUninstallRemovesNothingThisPluginWrote`
holds. Whether the server deletes the directory it installed the plugin into, or anything beside it,
is the server's own behaviour on a running install, and no run on this board has asked it. The
commands above are written so that they are right either way: a path that is already gone is a
command that removes nothing.

# Reporting a security problem

## The private route

Use GitHub's private vulnerability reporting for this repository: the **Security**
tab, then **Report a vulnerability**. It is enabled here, and it opens a draft
advisory only you and I can read.

The form is here, without navigating:

<https://github.com/Flowfin/jellyfin-plugin-requests/security/advisories/new>

That is the route. Do not open a public issue for a security problem, and do not
put one in a pull request body: both are readable by anybody the moment they are
written, including by whoever would use what you found.

If you cannot reach that form, open a public issue saying only that you have
something to report privately and nothing about what it is, and you will be given
a route.

## What to expect back

You will get a reply, from one person. This repository is maintained by one
person in their own time; there is no rota behind it and no response time is
promised here, because a promise nobody is staffed to keep is worse than an
honest silence about it.

What is promised is the shape of the reply, in this order:

- an acknowledgement that the report was read, and whether it is understood;
- a verdict, with the reasoning: whether it is a problem in this plugin, in
  something it depends on, or not a problem, and where the line was drawn;
- where it is a problem, what the fix is and when it lands, and where it is not,
  why not.

Credit is offered by default and refused on request. A fix names what was fixed
and what it prevented, in the changelog, whether or not the reporter is named.

## What is in scope

This plugin, in this repository: the code, the packaging metadata, the workflows,
and anything in the tree.

What is not, and where it goes instead:

- Jellyfin itself, including its authentication, its API and its dashboard. Those
  go to the Jellyfin project.
- The server this plugin is installed on. How it is exposed, what is in front of
  it and who can reach it are the operator's, and this plugin makes none of those
  choices.
- An external request service this plugin can be pointed at. That is a separate
  product with its own reporting route.

## What this plugin holds

A report is easier to write against a plugin whose data you know. This one keeps
requests: who asked, what they asked for, when, the decisions made on it and by
whom, and the notes people wrote. People are recorded by the server's own user
identifier and never by name.

There is no telemetry. Nothing about use is collected and nothing is sent
anywhere for this project's benefit, without exception and without a later
review of that.

The plugin makes one outbound call and an operator has to turn it on. It is the
notification sink, and the three lines below are one path rather than three: the
client, and the socket pipeline it is handed at registration.

    git grep -nE 'HttpClient|WebRequest|WebSocket|Socket' -- Jellyfin.Plugin.Requests
    Jellyfin.Plugin.Requests/Notify/OutboundSink.cs:61:    private readonly HttpClient _client;
    Jellyfin.Plugin.Requests/Notify/OutboundSink.cs:104:        _client = new HttpClient(handler, disposeHandler: false)
    Jellyfin.Plugin.Requests/PluginServiceRegistrator.cs:151:            new SocketsHttpHandler(),

It posts nothing until an operator sets `OutboundNoticeAddress`, which is empty
on every install where nobody has decided otherwise, and there is no other way to
turn it on. What it posts then is one small document per movement in the queue.
[docs/notifications.md](docs/notifications.md) writes that document out field by
field, and [docs/personal-data.md](docs/personal-data.md) is the account of
everything that leaves the server.

**This section said the plugin makes no outbound call at all, and that stopped
being true on 2026-08-16.** The sentence is corrected here rather than quietly
replaced, because somebody who read it and took it for a property of the plugin
took it from this page. It was never a change in behaviour: the sink landed with
its switch off and has never been on by default. What was wrong is that this page
went on asserting the tree of six days earlier, and the pasted command under it
went on being quoted with an output it no longer produced.

A bridge to an external request service would be a second such path and does not
exist yet. What goes to one is what the operator configured it to receive, and it
is documented where that is built.

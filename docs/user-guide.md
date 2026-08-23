# Asking for something, from the client you actually use

This is the document for somebody who uses this server rather than runs it. It answers one question
per client family: what you can do here from the thing in front of you, and where the answer is that
this client cannot, what can instead.

If you run the server, [operating.md](operating.md) is yours and this one is not. It is the document
to hand to the people on your server, and it does not repeat what that one says.

## Two answers that are the same on every client

Both are decided rather than missing, so no client and no update changes them.

**There is no way to search for a title here.** This plugin holds requests; it does not hold a
catalogue and it asks nothing about films or series anywhere. So there is no box to type a title
into, on any client. A request reaches this server either from a browsing plugin your operator has
installed beside this one, or from something driving the server's own interface, and if neither of
those is set up on your server then there is no way to ask and asking your operator directly is the
answer.

**There is no way to take an ask back.** A request has no withdrawn state, deliberately, so nothing
on any client can cancel one. What to do instead is ask whoever runs the server to decline it, which
is one action on their side and reads correctly in the history afterwards.

## What each family gets

Each section says what is there today, and carries the line saying whether anybody has opened that
client and looked. The wording is the same as the reach matrix in [surface.md](surface.md) uses, and
it means the same thing:

- **not checked** means nobody has opened that client and looked. It is not a prediction that it
  works. It is the admission that it has not been tried.
- A section that has been checked names the client and the version it was checked on, in place of
  those two words, so a checked section and an unchecked one can never be read as the same thing.

Nothing in this document says a client does something on the strength of a plan. Where the thing
itself is not there, the section says that instead, and no observation can change it while it is
absent.

### Browser

Open the address your operator gives you and you see your own requests: what you asked for, what
sort of thing it is, where it stands, when you asked, when it last moved, whether the server has it
now, the note you wrote, and the reason and the sentence your operator gave if the answer was no.

The address carries your credential in it, which is how a browser tab reaches an authenticated page
on this server at all. Treat the link the way you would treat a password: it lands in your browser
history, and anybody you send it to is being sent your session rather than a page.

There is nothing to press on that page. It shows and it does not decide, because approving and
declining are the operator's and cancelling does not exist.

Not checked.

### Desktop client that wraps the web interface

The same page as the browser row, opened the same way, because this family draws the web interface.

Not checked.

### Android phone and tablet

Nothing from this plugin here. The surface meant for a client that draws its own interface is a
folder that appears beside your libraries. That folder exists and it holds one line telling you to
open the page in a browser, because it cannot show your requests: what it showed reached the wrong
person on a real server of each supported line, which is #67, so it was taken out.

So there is nothing on this family to read your requests from and nothing to look for beyond that
one line.

Reading your own requests works from a browser on the same device, with the address above.

### Android TV and Fire TV

Nothing from this plugin here, for the same reason as the row above.

This is the family the absence costs most, because a television is where somebody is least able to
go and open a browser instead. Reading your own requests means finding a browser somewhere else,
which is a real answer and not a good one.

### iPhone and iPad

Nothing from this plugin here, for the same reason.

Reading your own requests works from a browser on the same device.

### Apple TV

Nothing from this plugin here, for the same reason, and the same cost as the other television
families.

### Roku

Nothing from this plugin here, for the same reason, and the same cost as the other television
families.

### LG webOS television

Nothing from this plugin here, for the same reason, and the same cost as the other television
families.

### Samsung Tizen television

Nothing from this plugin here, for the same reason, and the same cost as the other television
families.

### Kodi

Nothing from this plugin here, for the same reason.

### A script, or another program

Everything this plugin does is reachable over the server's own interface, and that is the floor the
rest of this document is built on. What the routes are, what they take and what they answer is in
[api.md](api.md).

Taking an ask back is the one thing that is not there either, for the reason at the top: there is no
state to move a request into.

## What this document does not tell you

**Whether any of the sections above is true of your client.** Every one of them carries its own
answer to that, and today none of them has been checked against a real client of that family. A
section that says nothing is built is certain, because that is a fact about this plugin rather than
about your client. A section that says something works is not, until it names the client and the
version somebody saw it on.

**Anything about running the server.** Settings, the queue, retention and what is stored about a
person are the operator's documents, and they are linked from [operating.md](operating.md).

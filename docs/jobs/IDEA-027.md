<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-027 — A page cannot leave a message for a page it sends a visitor to

| | |
| --- | --- |
| Size | S |
| Area | Sdk, Web, page session |
| Location | `src/uk.osric.asp.Sdk/IPageSession.cs`, `src/uk.osric.asp.Web/Pages/PageSessionScope.cs` |

## Idea

[The page-session plan](../plans/page-session.md) gives a page a per-visitor store in which it can
write, list and delete its own keys and read any other page's by path. That covers "this page saved
your form" and "here is the message again after the redirect". It does not cover "that page saved
your form, and this page is where you have landed": the receiving page can read the sender's key only
if it already knows the sender's path, which is a convention the URL has to carry and the template has
to remember.

The editor is the case that produced the question. A save is posted at `/edit/{path}`, redirects
somewhere else, and the confirmation belongs to the request that lands, not the one that submitted.
Through a per-path rule that is expressible only because the shell chooses the landing path when it
writes; two pages, which cannot choose each other, have no such move.

It is a different problem from [IDEA-017](IDEA-017.md): that one is a page editing another page's
source, and this one is a page telling another page something. Solving either does not solve the
other, but both grow from the same gap — a page is bound to its own path and the site no longer has a
single place to put "something that happened".

## Sketch

1. Decide first whether this needs the host at all. The candidates are a reserved inbox the page
   session grows (`SendAsync(pagePath, key, value)`, which writes outside the sender's path and so
   breaks the ownership rule that currently makes isolation structural), a one-shot area carried by
   the redirect URL (a query parameter or a fragment, which needs no store, no sweep and no cap, and
   which nobody else can read), or nothing at all until a real case appears.
2. If it is the store, the write rule has to grow a second case, and the question is what stops it
   from becoming "any page can write anywhere" with extra steps. A send that lands in the receiver's
   own space and is deletable by it is the useful shape; a send that only the receiver can read and
   only the sender can delete is the careful one.
3. One-shot semantics belong in the same decision: a message that is read but not deleted shows
   twice, and a message deleted without being read is lost. The editor's `MessageKey` consumes on
   read; a send should say which it is.
4. Bound whatever is added: a message is a key in a store whose caps are per visitor, so a send that
   bypasses the sender's path must not become a way around the key count.
5. Tests: a receiver reads what a sender left without knowing the sender's path; a page cannot send
   into a path that does not exist; the message is consumed once; a page that ignores it loses it to
   the sweep rather than to the next visitor.

If IDEA-017 lands first, the editor stops being a shell endpoint and the sending side becomes a page
writing under its own path to a landing page it names, which may answer this without new capability.

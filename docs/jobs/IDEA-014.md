<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-014 — A page cannot keep per-visitor state

| | |
| --- | --- |
| Size | Medium |
| Area | Sdk, Web, storage |
| Location | `src/uk.osric.asp.Sdk/`, `src/uk.osric.asp.Web/Pages/PageFeatures.cs` |

## Idea

`ctx.Features.GetRequired<IPageStorage>()` is one store for the whole site: every visitor shares it,
a write is last-write-wins, and the handle is bound to the page's own key (DEC-125, DEC-126, DEC-127).
That is the right shape for a
guestbook or a counter and the wrong shape for anything the site says to one visitor.

The shell uses `context.Session` for exactly that. Every editor action writes a one-shot message into
it and redirects (`MessageKey` in `EditorEndpoint`), because a `POST` answered with `303` has nowhere
else to put "Saved successfully." A page has no equivalent, so a page that submits a form and
redirects cannot tell the visitor what happened, and the view grows into a shell endpoint to say it.

The goal is a per-visitor store a page can reach: bounded, keyed by a cookie the shell mints,
readable and writable by page code, and cleared when the visitor signs out. It arrives as a
capability (`IPageSession` from `ctx.Features`) rather than as another member on `IContext`, now
that the [page-features plan](../plans/archive/page-features.md) has landed.

## Sketch

1. Decide the identity first, because it decides everything else. A separate page-session cookie
   minted by the shell is what lets a public page have one; keying off the authentication ticket
   makes the state owner-only and free. A page may not choose the key — `Set-Cookie` is refused to
   page code today — so the value is always the shell's and can be unguessable.
2. Bound it from the first commit. The session that exists now holds a handful of flash messages; a
   store an anonymous client can grow is a memory DoS. Key and value caps, a key count per visitor,
   an idle lifetime and a sweep, sized against the same reasoning as `Pages:Storage` and the render
   buffer. Decide the backing store explicitly: the current `AddDistributedMemoryCache` is lost on
   restart and per process, which a flash message does not mind and a half-written form does.
3. Contract shape: an `IPageSession` capability with
   `TryGetValueAsync<T>`/`SetAsync<T>`/`DeleteAsync`, async and tokenless like `IPageStorage`
   (DEC-123), with presence and value travelling together as `PageValue<T>` does. A `HEAD` render
   may read and must not write, as storage already does.
4. Fail closed on the shell's own cookies. A page may not name `.uk.osric.asp.session`,
   `.uk.osric.asp.antiforgery` or the authentication ticket, and signing out has to clear the page
   session as well as delete the ticket row.
5. What it unlocks: the editor's listing, history and confirmation views once IDEA-015 exists, and
   any page that answers a form with a redirect. What it does not: the assistant's conversations,
   which are durable per-owner data rather than a transient message.
6. Tests: one visitor's value is invisible to another; sign-out clears it; `HEAD` cannot write; the
   caps and the sweep; the cookie carries `HttpOnly`, `SameSite=Strict` and the same `SecurePolicy`
   as the others.

Adding a member to `IContext` changes the SDK module version id, so every stored image is recompiled
on its next request (DEC-26). That is the intended cost of a contract change, not a reason to avoid
one — and it is the cost a new capability interface pays too, which is why the page-features plan
exists: the recompiles do not go away, the churn in what a page must implement does.

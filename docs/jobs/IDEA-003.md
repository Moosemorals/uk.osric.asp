<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-003 — A page has nowhere to keep state

| | |
| --- | --- |
| Size | Medium |
| Area | Core, Web, storage |
| Location | `src/uk.osric.asp.Core/IContext.cs`, `src/uk.osric.asp.Web/Storage/SqliteStorage.cs` |

## Idea

Everything a page knows arrives in the request and leaves in the response. A guestbook needs a
table, a counter needs a row, a form that survives a validation failure needs somewhere to put the
draft — and none of that exists. `IStorage` holds pages, users and nothing else, and page code
cannot reach the database anyway.

The smallest useful thing is a key/value store scoped to the page that owns it, reachable from
`IContext` and reachable from nowhere else.

## Sketch

1. Scope is the open question. `PageRequest` has no user identity, so per-user state needs
   authentication inside pages, which does not exist — so the first version is per page, shared by
   every visitor. Decide that deliberately rather than discovering it.
2. Shape: `ctx.Storage.Get("key")`, `Set("key", value)`, `Delete("key")`, `Keys()`. Values are
   strings — anything structured is the page's business to serialise, and JSON is available.
3. Keep `Core` pure: the methods are a contract on `IContext`; the table, the queries and the
   transaction live in `Web` behind `IStorage`.
4. Bound it: a key is a short ordinal string with a path-ish shape, a value has a byte cap (say
   64 KiB), and a page has a total cap. A page that writes in a loop should hit a wall, not fill
   the disk.
5. Deleting a page (ARCH-10) must delete its state; note it there rather than in a second place.
6. Concurrency is last-write-wins per key. That is fine for the intended use and should be said
   out loud so nobody assumes a compare-and-set that is not there.
7. Backing store options: a `PageState` table keyed by (page key, name) is the obvious one and
   reuses the existing SQLite setup. Reusing the page row with a JSON blob is smaller but couples
   state size to page load and loses per-key writes.
8. Tests: round-trip, missing key, cap exceeded, state gone when the page is deleted.

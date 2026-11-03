<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-10 — A page cannot be deleted

| | |
| --- | --- |
| Severity | Medium |
| Area | Storage, Web, product |
| Location | `src/uk.osric.asp.Web/Pages/IPageStore.cs`, `src/uk.osric.asp.Web/Editor/EditorEndpoint.cs` |

## Problem

A page can be created and overwritten but never removed. `IPageStore` offers only `GetPageAsync`,
`SavePageAsync` and `TrySetCompilationAsync`, and the editor maps only the read and save of
`/edit/<path>`, so the sole way to retire a page is a direct write to the database. A page saved by
mistake — or one that should never have been public, since a page is served at its URL the moment it
is saved — stays reachable forever.

Listing, retirement and history are distinct behaviors, but share the retained-revision storage
planned alongside ARCH-06 and the storage portion of ARCH-07.

## Fix outline

The [combined implementation plan](../plans/editor-tools-and-immutable-assets.md) specifies the
retained revisions, tombstone deletion, recreation sharing history, and permanent scrub. It uses
`?cmd=delete` and `?cmd=scrub` rather than path suffixes, so existing page keys stay editable.
The decisions in that plan supersede the alternatives below.

1. Tag the current head as deleted without removing its revision pointer, source or compile/debug
   data. Public reads return 404; the editor listing shows the tombstone separately.
2. Use GET confirmation and `POST /edit/<path>?cmd=delete`, behind Editor authorization and
   antiforgery. Redirect 303 to the listing with deleted or already-missing feedback.
3. Saving at the same canonical path appends to the existing history and clears the tombstone.
   Startup seeding must respect tombstones rather than restoring deliberately deleted assets.
4. Add GET confirmation and `POST /edit/<path>?cmd=scrub` to remove the live head and every revision
   permanently. Require a typed canonical path, authorization and antiforgery. Include SQLite
   secure deletion/WAL cleanup, safe retry on cleanup failure, and warnings about backups, external
   copies and secret rotation; a logical row deletion alone is not secret erasure.
5. Test retained hashes after delete, shared history after recreation, no resurrection from racing
   compilation, complete removal after scrub, startup behavior, and auth/antiforgery rejection.

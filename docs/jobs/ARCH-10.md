<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-10 — A page cannot be deleted

| | |
| --- | --- |
| Severity | Medium |
| Area | Storage, Web, product |
| Location | `src/uk.osric.asp.Web/IStorage.cs`, `src/uk.osric.asp.Web/Endpoints/EditorEndpoint.cs` |

## Problem

A page can be created and overwritten but never removed. `IStorage` offers only `GetPageAsync`,
`SavePageAsync` and `TrySetCompilationAsync`, and the editor maps only the read and save of
`/edit/<path>`, so the sole way to retire a page is a direct write to the database. A page saved by
mistake — or one that should never have been public, since a page is served at its URL the moment it
is saved — stays reachable forever.

This is not part of ARCH-06 or ARCH-07. A listing makes an unwanted key visible but cannot remove it,
and history is about undoing a save rather than retiring a key.

## Fix outline

1. Add `DeletePageAsync(PagePath, CancellationToken)` to `IStorage` and implement it in
   `SqliteStorage` by deleting the row; the source, image and diagnostics go with it. Return whether
   a row was removed so the editor can tell "deleted" from "was not there".
2. Add `POST /edit/<path>/delete` behind the `Editor` policy, with the antiforgery token the other
   editor POSTs already carry, answering `303` to the listing (ARCH-06) or the editor with a flash
   message.
3. Confirm first: offer the action on a `GET` page with a form, not as a link, so a prefetch or a
   crawler cannot delete a page.
4. Decide how deletion interacts with ARCH-07. Once revisions exist, deletion either drops the key
   and its history or keeps the history and hides the current row. Both are defensible, so pick one
   and write it down; "delete means gone" is the safer default.
5. Nothing else needs to change: `PageService.LoadAsync` reads through to storage on every request,
   and neither the service nor the runtime caches by path, so a deleted row is a 404 on the next
   request.
6. Tests: deleting an existing page makes its URL return 404 and removes the row; deleting a missing
   key reports it as missing rather than failing; the endpoint answers 401/403 without the editor
   policy and 400 without the antiforgery token; the listing stops showing the key.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-06 — There is no way to list pages

| | |
| --- | --- |
| Severity | Medium |
| Area | Web, product |
| Location | `src/uk.osric.asp.Web/IStorage.cs`, `src/uk.osric.asp.Web/Endpoints/EditorEndpoint.cs` |

## Problem

There is no index, navigation or search: the only way to reach a page is to know its exact URL, and
the only way to reach the editor is to type `/edit/<path>`. `IStorage` cannot even enumerate what it
holds — the unused `ListFilesAsync` was removed rather than kept as dead code — so a listing needs a
new query before it can have a UI.

This means pages cannot be discovered at all on a fresh deployment, and there is no way to spot
orphaned or unreachable keys (see ARCH-05).

## Fix outline

1. Decide where the listing belongs: an index at `/` when no page matches, and/or `/edit` with a list
   of stored keys and links.
2. Add the query to `IStorage` and implement it over `StoredFile`; present it in `EditorEndpoint` (a
   shell concern — it is UI over a list of keys), not in `Core`.
3. Sort keys ordinals; show a "new page" form that posts to `/edit/<typed path>`.
4. Keep the page itself unauthenticated if it only lists what is already public; if it lists drafts,
   put it behind the SEC-01 auth boundary.
5. Add tests for the listing and for the empty state.

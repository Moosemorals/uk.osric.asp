<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-06 — There is no way to list pages

| | |
| --- | --- |
| Severity | Medium |
| Area | Web, product |
| Location | `src/uk.osric.asp.Web/Pages/IPageStore.cs`, `src/uk.osric.asp.Web/Editor/EditorEndpoint.cs` |

## Problem

There is no index, navigation or search: the only way to reach a page is to know its exact URL, and
the only way to reach the editor is to type `/edit/<path>`. `IPageStore` cannot even enumerate what it
holds — the unused `ListFilesAsync` was removed rather than kept as dead code — so a listing needs a
new query before it can have a UI.

This means pages cannot be discovered at all on a fresh deployment, and there is no way to spot
orphaned or unreachable keys.

## Fix outline

The [combined implementation plan](../plans/editor-tools-and-immutable-assets.md) settles the
listing as authenticated `GET /edit`, preserving `/edit/` for the root-page editor. It also covers
ARCH-10, ARCH-11 and retained revisions for future asset URLs; asset serving and PageLayout are
separate. Commands use `cmd`, and the listing includes tombstones.

1. Query canonical paths and deleted status from heads only, without source or compilation BLOBs.
   Forward through `PageEditor`; render the UI in the Web shell, not Core.
2. Sort ordinally. Show live-page links and a separate deleted section with recreation and scrub
   actions. The public root remains an ordinary page or 404, never a fallback catalogue.
3. Use an antiforgery-protected `POST /edit?cmd=new` to validate and normalize a typed key, then
   redirect to its editor without saving anything. A later save at a tombstoned path shares history.
4. Require the Editor policy and `Cache-Control: no-store`; preserve `/edit/` for root editing.
5. Test empty/live/deleted states, ordinal order, lightweight queries, root-route distinction,
   validation and authorization/antiforgery behavior through the real host where appropriate.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page cannot list the store's folders

| | |
| --- | --- |
| Difficulty | eager-beaver |
| Utility | medium |
| Status | planned |
| Blocked by | — |
| Plan | [page-tree-listing](../plans/page-tree-listing.md) |
| Area | Sdk, Web (pages), docs |
| Location | `src/uk.osric.asp.Core/Pages/`, `src/uk.osric.asp.Sdk/IPageCatalog.cs`, `src/uk.osric.asp.Sdk/PageEntry.cs`, `src/uk.osric.asp.Sdk/PageListing.cs`, `src/uk.osric.asp.Sdk/PageListingRequest.cs`, `src/uk.osric.asp.Web/Pages/IPageReadStore.cs`, `src/uk.osric.asp.Web/Pages/SqlitePageReadStore.cs`, `src/uk.osric.asp.Web/Pages/PageCatalogScope.cs`, `src/uk.osric.asp.Web/Pages/Seeds/pages`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `docs/pages.md`, `docs/storage.md` |
| Spike | — |

## Idea

`IPageCatalog.ListAsync` answers the heads under a prefix: pages, tombstones, seeded assets and the
site's reserved keys. It never answers a folder, because a folder is not a row — a folder exists only
because a key lives below it. The host has that rule once, in `Core.PageTree`, and `PageStoreReader`
uses it to build the tree the shell's `ls`, `find` and `ls -R` walk. A page has neither: it gets a flat
stream of keys and folds them itself, so every page that wants a tree invents the same fold and can
disagree with the next one about what a folder is.

The callers are already here. The shell's `ls` and `find -type d` need the rows; the IDE plan's T8
tree is page work and needs them; a site map, a moved-folder confirmation and a page that shows its
own neighbourhood all want the same answer. The shell is simply the first caller that cannot work
around it without a private seam (**assistant-a-command-uses-page-capabilities**).

## Sketch

1. **One ordered answer.** `ListAsync` returns folder rows among the keys, in key order, so a cursor
   still resumes from the last key and a folder costs no second read. A slice that starts inside a
   folder returns that folder's children, not the folder again.
2. **A row says what it is.** A listing row becomes a small shape with a `Kind` — page, tombstone,
   site route, folder — and the head's facts when it has one. A folder row has no hash, no saved time
   and no state, and saying so in the type is better than a `PageEntry` with empty strings.
3. **The rule is the host's.** "A key lives below it" is `PageTree`'s, and the capability computes it;
   the page never folds keys and never needs `PagePath`.
4. **A site route keeps its marks.** It is listed with `IsReserved` and no source, as it is today; the
   shell turns that into the `000` mode a site route is shown with.
5. **The docs.** `pages-the-store-is-a-tree` is edited in place to say the capability answers the tree,
   `/help` gains the row shape, and the prompt says what a listing returns.

## What the owner answered

Put to them on 2026-11-21, before any plan was written.

1. **A kind on the row, and the kinds combine.** A page that is also a folder is one row with both
   marks, not two rows and not a `PageEntry` whose hash, saved time and state are empty strings.
2. **Folders are always in the answer.** A page that wants heads only filters on the kind it already
   has; a walk keeps one shape.
3. **The depth is a request flag.** `Recursive` keeps today's meaning by default — the whole subtree —
   and offers the prefix's own children when a page asks, so a tree can descend one folder per
   request.
4. **The shell's own routes are rows.** A page's tree matches the shell's `ls` without owning the
   route table, so a route is a row with no source and its folders are derived like any other key's.
5. **This step adds and proves the rows.** The shell keeps folding keys itself until
   [`tool-surface-is-one-shell`](tool-surface-is-one-shell.md) moves it onto the capability.

The plan is [page-tree-listing](../plans/page-tree-listing.md), which states the shape, the fold, the
two properties a cursor depends on and the cost of each depth.

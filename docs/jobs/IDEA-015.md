<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-015 — A page cannot read the page store

> **Unblocked.** The layout these views needed now exists (DEC-149, [site
> navigation](../plans/archive/site-navigation.md)), so a listing or history page can be a fragment the
> default layout wraps. The store read this item asks for is still its own job. The listing is now a
> table with a **State** column and the assistant reads the same word per page (DEC-165), so the
> shape this job would take over is settled rather than in flux.

| | |
| --- | --- |
| Size | Medium |
| Area | Sdk, Web, storage |
| Location | `src/uk.osric.asp.Sdk/IContext.cs`, `src/uk.osric.asp.Web/Pages/SimpleContext.cs` |

## Idea

The `/edit` listing, history and revision views are shell views over `IPageStore`. They are
presentation: a heading, a list of keys, a nav and a form. A page can read its own storage and
nothing else, so those views cannot become pages however much they look like one.

The goal is a read-only view of the store on `IContext`: list the pages, read one page's source, list
its revisions. No images, no symbols, no writes — writing is IDEA-016 and IDEA-017.

## Sketch

1. Read-only members first, and only what a view needs: list heads under a prefix, read a template
   with its saved time and revision hash, list revisions, read one revision's source.
2. The values must be Sdk records, not the shell's. `PageHeadSummary`, `PageRevisionSummary` and
   `PagePath` live in `Web.Pages` and `Core.Pages`, and a page can load neither (DEC-88), so the read
   API needs its own small values — a key string, a saved time, a hash, a deleted flag — and a
   mapping in `SimpleContext`.
3. The owner gate belongs to the shell, not the page. Pages are public by default (DEC-02) and page
   source is the site's content, so the read should be refused for an anonymous visitor whatever the
   page's own code does; otherwise one careless public page is a source dump of every other page.
4. Cap the read the way the body is capped: how many rows one list returns and how much source one
   render may pull in, so a page cannot read the whole store into the buffer it is about to send.
5. The I/O is async, tokenless and cancelled with the render, as `FetchAsync` is: `SimpleContext`
   already holds the deadline from `AttachDeadline`, and the store call must be linked to it rather
   than to the request alone (DEC-123).
6. Decide the competing road. [Import and export](../plans/archive/page-import-export.md) reads and writes
   the same store from the shell behind the `Editor` policy; two routes to one authority is a
   decision, not an accident. Either the shell keeps the authority and pages get only the read a
   view needs, or pages get the read and those views move.
7. What it unlocks: the listing, history and revision views; a "recent changes" page; and a site nav
   that knows about stored pages, which DEC-121 says a nav has to account for.
8. Tests: an anonymous visitor is refused; a listed key is canonical; a deleted page is listed as
   deleted without its source; the row and byte caps hold; a read is cancelled by the render
   deadline.

The residual risk is worth stating plainly: a page that can read other pages can read the editor's
own source, the seeded assets and every private page. Pages already run with the site's identity
(DEC-40), so this is not a new boundary — but it is a new accident waiting to happen, which is why
the gate belongs in `SimpleContext`.

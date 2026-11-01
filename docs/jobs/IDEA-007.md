<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-007 — The site has no favicon

| | |
| --- | --- |
| Size | Small |
| Area | Web, product |
| Location | `src/uk.osric.asp.Web/Framework.cs:11-22` |

## Idea

A browser visiting the site asks for an icon and gets nothing, so every tab shows the blank-page
placeholder. The icon wanted is <https://osric.uk/rocket.svg>, served — like `site.css` (IDEA-006) —
as a page rather than a static file.

## Sketch

1. Add `<link rel="icon" href="/rocket.svg" type="image/svg+xml">` to `Framework.GetHeader`, next to
   the stylesheet link. Encoded the same way the `<title>` is; it is a constant, so nothing needs
   escaping, but keep the shape consistent.
2. Create a page at `/rocket.svg` whose text is the SVG markup, with
   `ctx.SetContentType("image/svg+xml")` and the markup written raw. Literal template text is
   already `WriteRaw`, so no escaping is applied — which is what an SVG needs and also means the
   template must not contain a stray `<%`.
3. Keep the SVG as it is: it is a small hand-written document, not something to minify.
4. Browsers will still ask for `/favicon.ico` if a link is missing or ignored, and some clients ask
   regardless. Decide between a `302` page pointing at `/rocket.svg` and letting it be a `404` —
   the second is cheaper and honest.
5. `Pages:ReservedPaths` must not cover `/rocket.svg`, and the same fresh-deployment question as
   IDEA-006 applies: a site with no database has no icon and no stylesheet.
6. Test the two things a unit test can: the header contains the link, and the page answers
   `image/svg+xml`. Checking that a browser renders it is a browser test, if it is tested at all.

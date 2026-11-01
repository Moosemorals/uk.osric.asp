<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-006 — `site.css` is a static file rather than a page

| | |
| --- | --- |
| Size | Small |
| Area | Web, product |
| Location | `src/uk.osric.asp.Web/wwwroot/site.css`, `src/uk.osric.asp.Web/Framework.cs:20`, `src/uk.osric.asp.Web/Program.cs:99` |

## Idea

The site's own stylesheet is the one place where content still lives in the repository instead of
the database. Making it a page means the site is styled from the editor like everything else, and it
is a cheap proof that the page pipeline can serve something that is not HTML.

## Sketch

1. Create a page at `/site.css` whose whole body is CSS between `<% %>`-free text. Literal template
   text is emitted as `ctx.WriteRaw(...)`, so CSS is not escaped — check this stays true for `>`,
   `&` and `"` before relying on it, and test it.
2. The page sets its content type with `ctx.SetContentType("text/css; charset=utf-8")`;
   `SimpleContext` stores whatever it is given, so this already works. Confirm the browser accepts
   it through `WritePageAsync`, which sets `ContentType` from the page's response.
3. Delete `wwwroot/site.css`. If nothing else is left in `wwwroot`, drop `UseStaticFiles()` and the
   integration test that covers it; if other static assets appear later they can bring it back.
4. Keep `Framework.GetHeader`'s `<link rel="stylesheet" href="/site.css">` exactly as it is — the
   URL does not change, only where the bytes come from.
5. Caching: no page response carries a cache header today, but `Cache-Control` is on the page's
   allow-list, so the CSS page can set its own. Give it something short and correct, or add the
   ETag `ContentFingerprint` is documented as being for "later"; a stylesheet re-fetched on every
   request is a regression against the static file it replaces.
6. Reserved paths: `/site.css` must not be reserved, and the page must survive a restart with an
   empty database — which means the site's stylesheet arriving as a page is also a decision about
   seeding, on a site that currently has no seed step. Say what happens on a fresh deployment.

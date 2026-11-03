<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Non-HTML pages: favicon and stylesheet

Implementation plan for [IDEA-007](../jobs/IDEA-007.md) and
[IDEA-006](../jobs/IDEA-006.md). Together they prove that a stored page can serve useful non-HTML
content, and move the site's own icon and stylesheet into the editor.

## Status

Implemented. The built-in SVG and stylesheet are seeded as editable pages, with host-generated
validators and conditional GET/HEAD handling; static-file serving is removed while `wwwroot` is
empty.

## Goal and constraints

- `/rocket.svg` and `/site.css` are ordinary stored pages served by `PageEndpoint`, not special
  endpoints or static files.
- Their bodies are emitted literally, with no HTML escaping or content transformation.
- Both assets exist in a new database before the host starts accepting requests, but a deploy never
  overwrites an editor's version.
- A browser can revalidate unchanged assets and gets a new representation immediately after an edit.
- No changes to the page SDK or compiler API are needed. `IContext.SetContentType` and the response
  handling in `PageEndpoint` already provide the content-type path.

## Decisions

### 1. SQLite is the source of truth; seed defaults once per missing key

There is no content seed step today. After `Database.MigrateAsync` and before `RunAsync`, create a
scope and check the page store for each built-in asset. Save its default template only when its key
is absent. Store it without a compiled image; the normal page renderer compiles and persists it on
first request.

This is idempotent and preserves edits across restarts. A deployment with an empty database gets
both assets before it listens. If an asset is later deleted, the next startup restores the default;
that follows from the current product having no page-deletion workflow. A failed seed is a startup
failure, not a silently incomplete site.

### 2. Keep asset paths addressable

Remove `/site.css` from `Pages:ReservedPaths` in `appsettings.json` and from
`ReservedPaths.Default`. `/rocket.svg` is not currently reserved and must stay that way. Keep
`/favicon.ico` reserved: browsers or clients that request it without honoring the SVG link get the
existing plain 404, rather than a second icon format or a page the editor can accidentally claim.

### 3. Preserve raw template text

Literal template text is emitted through `ctx.WriteRaw`, which is required for CSS and SVG. Before
moving the assets, pin the behavior with a focused compiler test and a real-host test using literal
content containing `>`, `&`, and `"`; the response must contain the exact text. Keep the SVG source
free of the template opener `<%`, since that sequence begins executable template syntax.

Use the existing page renderer for the assets. In particular, do not add MIME sniffing, an HTML
wrapper, or a second rendering path.

### 4. Use explicit content types and revalidation

The CSS page sets `text/css; charset=utf-8`; the SVG page sets `image/svg+xml`. Both set
`Cache-Control: no-cache`, allowing storage while requiring revalidation.

Add a strong ETag from the UTF-8 bytes of the rendered body for successful page responses, and honor
`If-None-Match` on GET and HEAD with a bodyless 304 when it matches. The host, not page source, owns
the generated validator so an edit cannot retain a stale ETag. This is generic response handling in
`PageEndpoint`, but does not cache page execution: the page still runs to produce the representation
whose validator is checked. Test a matching validator and then an edited body with a different
validator. Do not apply this conditional path to POST.

### 5. `/favicon.ico` remains a 404

`PageLayout.Header` adds `<link rel="icon" href="/rocket.svg" type="image/svg+xml">` beside the
stylesheet link. The SVG is a small standalone document, returned raw. No redirect from
`/favicon.ico` is needed: the linked SVG is the supported icon and an unlinked legacy request is an
honest 404.

Confirm the SVG's source and reuse rights before checking it into the seed content. Keep the document
small and hand-written; minification and a browser-rendering test are not part of this change.

### 6. Remove static-file serving only while `wwwroot` is empty

`site.css` is the only current file under `wwwroot`. Once it is represented by the seeded page,
delete that file and remove `UseStaticFiles()` and the integration test that asserts it is served by
static middleware. Replace that test with assertions against the page-backed stylesheet. If another
static asset is added later, its introduction should explicitly restore/configure static-file
serving instead of leaving two sources for the same path.

## Implementation sequence

### Phase 0 — prove the non-HTML path

1. Add a focused test that literal template text containing `>`, `&`, and `"` is unchanged.
2. Add an integration case that serves a stored page with an explicit non-HTML content type and
   verifies its exact body and response `Content-Type`.

### Phase 1 — add idempotent built-in asset seeding

1. Add the default `/rocket.svg` and `/site.css` templates in a small page-asset seeder.
2. Run it after migrations and before the host starts accepting requests.
3. Test an empty database receives both keys, an existing edited body is not overwritten, and a
   second seed pass changes nothing.

### Phase 2 — serve the favicon page (IDEA-007)

1. Add the icon link to `PageLayout.Header`.
2. Seed `/rocket.svg` with the reviewed SVG source and set `image/svg+xml`.
3. Assert the header link, response content type, raw SVG body, and `/favicon.ico` 404 through the
   real host.

### Phase 3 — serve the stylesheet page (IDEA-006)

1. Remove `/site.css` from both reserved-path lists.
2. Seed `/site.css` with the existing stylesheet text and set `text/css; charset=utf-8`.
3. Verify CSS metacharacters survive unchanged and saving an edit through the editor changes the
   served representation.

### Phase 4 — caching and static-file cleanup

1. Add generated ETags and conditional GET/HEAD handling to `PageEndpoint`; test 200, 304, HEAD, and
   changed-content behavior.
2. Set `Cache-Control: no-cache` in both asset templates.
3. Delete `wwwroot/site.css`, remove `UseStaticFiles()` if `wwwroot` is empty, and replace the old
   static-file integration test.
4. Update architecture/testing documentation if the HTTP validator behavior becomes part of the
   general page response contract.

## Acceptance criteria

- A fresh database serves both `/site.css` and `/rocket.svg` without a manual editor save.
- Deploying over an existing database preserves edited asset source.
- The CSS and SVG responses have the expected media types and exact raw bodies.
- The shared HTML header links to both assets.
- An unchanged GET can return 304 after revalidation; changing an asset changes its ETag and body.
- `/favicon.ico` returns 404, and `/site.css` and `/rocket.svg` are not reserved page paths.
- No static-file middleware remains while `wwwroot` has no files.
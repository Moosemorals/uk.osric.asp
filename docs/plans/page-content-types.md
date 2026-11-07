<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Content types from page extensions

Status: **proposed, not implemented**. Written 2026-11-07. This is a design and an implementation
plan, not a record of work done. It does not change the page SDK, the template language, the
compiler or the page store schema.

## The short version

A stored page's default response content type is chosen from the last segment of its key. A key
ending in `.js` or `.mjs` is served as `text/javascript; charset=utf-8`, `.css` as
`text/css; charset=utf-8`, `.json` as `application/json; charset=utf-8`, `.txt` as
`text/plain; charset=utf-8`, and everything else keeps the current HTML default. A page can still
call `ctx.SetContentType` to override the default, including back to HTML.

Inferred non-HTML responses also default to `Cache-Control: no-cache` unless the page sets its own
cache header. This preserves the behaviour the seeded scripts and stylesheets have today, and stops
a page that omits the old directive from being cached heuristically.

The extension is also a **source-language signal**: it says the author intends the literal text to
be JavaScript, CSS or JSON. It is not a promise that the text is valid, and it does not validate
anything. [IDEA-021](../jobs/IDEA-021.md) is the separate idea for using the signal in the
assistant's validator.

## Goals and non-goals

Goals:

- A page's default content type follows a small, explicit, auditable mapping from its key.
- `ctx.SetContentType` remains the one explicit override; precedence is unchanged.
- The seeded scripts and stylesheets keep their current content type and cache behaviour.
- A pure JavaScript page needs no template directive at all, so its source is valid JavaScript as
  stored.
- The mapping is available to the validator as a source-language kind, without the validator being
  part of the response path.

Non-goals:

- Binary assets in the page store. Pages are text templates; a `.png` key is still a text page and
  is not part of the initial map.
- MIME sniffing, `Content-Disposition`, content negotiation on `Accept`, or a second static-file
  pipeline.
- Inferring `Content-Security-Policy` or `AllowScripts` from the extension.
- Validating the page source. That is [IDEA-021](../jobs/IDEA-021.md).
- Changing `PageResponse.Default`, the SDK, the compiler, the page store or the editor's save
  contract.

## What already exists

- `SimpleContext` starts with `PageResponse.Default.ContentType`, currently
  `text/html; charset=utf-8`, and `SetContentType` overwrites it. `PageEndpoint` writes the final
  value to the response.
- The seeds in `src/uk.osric.asp.Web/Pages/Seeds` set their own content type and
  `Cache-Control: no-cache` in a template block at the top of the file. Each block is already inside
  a native comment (`//`, `/* */` or `<!-- -->`), so the stored source is valid in its own language.
- `SecurityHeaders` sets `X-Content-Type-Options: nosniff` on every response and owns the CSP
  header. A page can widen scripts only through `ctx.AllowScripts()`.
- `PageEndpoint` computes an ETag from the rendered body and honours `If-None-Match` for GET and
  HEAD, so `no-cache` means revalidate, not necessarily re-render without a validator.
- `PagePath` canonicalises keys to lowercase and rejects `?` and `#`. A key has no separate
  extension property today.

## Decisions

### 1. The extension selects a default; `SetContentType` still wins

The mapping is a default, not a constraint. A `.js` page may still call
`ctx.SetContentType("text/html; charset=utf-8")` if that is what it wants to send, and an HTML page
may call it to serve something else. The extension decides the initial value; the page decides the
final one. This is the same precedence as today, with a different initial value.

The source language and the response type are separate facts. A `.js` page that overrides its
response type is still JavaScript source for validation; a page at `/notes` that sets
`text/javascript` is still HTML source by convention. [IDEA-021](../jobs/IDEA-021.md) uses the key,
not the final header.

### 2. Use an explicit map in `Core`, not the framework's file-extension provider

Add a pure `PageContentTypes` helper beside `PagePath` in `uk.osric.asp.Core.Pages`. It returns a
small value with the media type and a kind:

| Key ends in | Media type | Kind | Default cache |
| --- | --- | --- | --- |
| `.js`, `.mjs` | `text/javascript; charset=utf-8` | JavaScript | `no-cache` |
| `.css` | `text/css; charset=utf-8` | Css | `no-cache` |
| `.json` | `application/json; charset=utf-8` | Json | `no-cache` |
| `.txt` | `text/plain; charset=utf-8` | Text | `no-cache` |
| anything else | `text/html; charset=utf-8` | Html | unchanged |

`ASP.NET`'s `FileExtensionContentTypeProvider` is deliberately not used. It maps binary extensions
that a text page store cannot actually serve, and its list changes with the framework. A small
explicit map is easier to audit and to test, and the site only needs a handful of types.

The extension is the suffix after the last `.` in the final `/` segment, and only when there is at
least one character before the dot. `/foo.js` is JavaScript; `/foo` and `/foo.js/bar` are HTML;
`/.js` has no stem and is HTML. Keys are lowercased by `PagePath`, but the lookup should still be
case-insensitive so a caller holding raw request text gets the same answer. `.html` and `.htm`
need no special row: they fall through to the HTML default.

`.svg` is deliberately not in the initial map. It is active content, and `/rocket.svg` already sets
`image/svg+xml` explicitly. Adding it later is a one-line change and a test, not a rewrite.

### 3. Apply the default in `SimpleContext`, not in `PageEndpoint`

`SimpleContext` is where the page's response is assembled, and it already owns `_contentType`. Set
it from `PageContentTypes.ForPath(request.Path).MediaType` in the constructor, then let
`SetContentType` overwrite it. `PageResponse.Default` stays HTML as the SDK-level default; only
stored pages get the extension-based initial value.

No SDK change is needed: `PageResponse.ContentType` already carries the final value, and
`PageEndpoint` already writes it.

### 4. Inferred non-HTML responses default to `Cache-Control: no-cache`

The seeds currently pair their content type with `no-cache`. If the content-type directive is
removed, a pure script or stylesheet page would otherwise be heuristically cached by a browser,
which is the opposite of what the script editor needs. Keep the guarantee in the host: when the
inferred kind is not HTML, add `Cache-Control: no-cache` unless the page already set a
`Cache-Control` header.

`no-cache` does not prevent storage; it requires revalidation, and the existing ETag/304 path makes
that cheap. A page can still override it with `ctx.SetHeader("Cache-Control", "max-age=…")` because
`Cache-Control` is on the page header allow-list. Authenticated responses keep the final word:
`PageEndpoint` applies `no-store` after the page's headers, so a signed-in render is never a shared
cache's answer.

JSON and plain text get the same default. They are cheap to revalidate and may be data a script
loads; a page that wants different caching can say so.

### 5. Do not infer CSP or `AllowScripts`

The content type describes the resource; `AllowScripts` describes the document that loads it. A
`.js` response must not widen its own `Content-Security-Policy`, and an HTML page that includes a
`<script type="module">` must keep calling `ctx.AllowScripts()` explicitly. `SecurityHeaders` and
`IContext.AllowScripts` are unchanged.

### 6. No content negotiation on `Accept`

The key is the contract. A request for `/foo.js` gets JavaScript even if it sends
`Accept: text/html`; a page that wants to vary by `Accept` can inspect the request itself. This
keeps the response cache and the ETag story simple and matches the site's existing "a key names one
page" model.

### 7. Update the seeds and the author prompt together

Once the default is in place, remove the `ctx.SetContentType(...)` calls from the seeded scripts,
stylesheets and (if it is not deferred) the SVG. Keep the cache directive only where the host's
default does not already give the page what it wants.

Update the `## Scripts` section of `PageAuthorPrompt` to say that a page whose key ends in `.js` or
`.mjs` is served as JavaScript, that `SetContentType` is only for an override, and that the source
should be a native ES module. Bump `PageAuthorPrompt.Version` and cover the change in
`PageAuthorPromptTests`.

The seeds' first line is currently doing double duty as a language comment and a host directive.
After this change the script and stylesheet sources are cleaner: `/assistant.js` can begin with its
SPDX comments, and `/site.css` with its own. That is also what makes the source directly parseable
by the validator in the common case.

### 8. Record the decision when it is implemented

This plan proposes the rule; [docs/decisions.md](../decisions.md) should gain the corresponding
entry when the code lands, including the exact map, the cache default, the `.svg` exclusion and the
statement that the extension is a default rather than a constraint.

## Implementation sequence

### Phase 0 — the pure mapping

1. Add `src/uk.osric.asp.Core/Pages/PageContentTypes.cs` with a small result value
   (`MediaType`, `Kind`) and `ForPath(string path)`.
2. Add `test/uk.osric.asp.Core.Tests/Pages/PageContentTypesTests.cs`: every mapped extension,
   unknown extensions, no extension, a dot in a directory name, `/.js`, uppercase input, and
   `/foo.js/bar` (HTML because the last segment has no extension).
3. Pin the exact strings (`text/javascript; charset=utf-8`, not `application/javascript`) in the
   tests.

### Phase 1 — apply the default and the cache default

1. Change `SimpleContext` to initialise `_contentType` from `request.Path`.
2. Add a default `Cache-Control: no-cache` in `ToResponse` for inferred non-HTML kinds when the page
   has not set the header. Keep the existing `_headers` dictionary case-insensitive.
3. Add tests to `SimpleContextTests` for:
   - `/app.js` defaults to JavaScript;
   - `/notes` still defaults to HTML;
   - `SetContentType` overrides the inferred type;
   - inferred assets get `no-cache`, a page-set `Cache-Control` wins, and an HTML page does not get
     the inferred cache header.
4. Add `PageEndpointTests` or integration tests for a stored `/x.js` page with no directive, a
   stored `/x.css` page that overrides to HTML, HEAD, and a matching `If-None-Match` 304.
5. Confirm `SecurityHeaderTests.AnAssetPageForbidsScriptsAndKeepsItsContentType` still passes for
   the seeded stylesheet, and add the same assertion for a seeded script if one is served directly
   in a test.

### Phase 2 — seeds, prompt and docs

1. Remove the content-type part of the first line from the seeds that now get it from the
   extension. Drop the whole line where the host cache default matches the seed's intent; keep a
   cache override only where the seed wants something different.
2. Update `PageAssetSeederTests` and any seed-content assertions.
3. Update `PageAuthorPrompt` and its tests; bump the version.
4. Update `docs/architecture.md`:
   - the paragraph that says each seed is a complete template "including its response content type
     and cache headers";
   - the page-contract paragraph that says content type goes through `SetContentType`, to mention
     the extension default;
   - the ownership table with a row for the pure content-type rule.
5. Add the decision entry to `docs/decisions.md` when the implementation is actually accepted.

### Phase 3 — gate

1. Run `make check` (format, licences, package audit, tests, Release publish).
2. Run `make test-browser` if a seeded script or stylesheet changed; the browser suite proves the
   seeded assets still load and run.
3. Before deploying, list stored keys whose final segment contains a dot but is not in the map, and
   confirm none of them relied on the HTML default. `SetContentType` remains the escape hatch.

## Tests

The mapping is a pure function and needs table-driven tests, not a host test:

- `/app.js`, `/app.mjs` → JavaScript.
- `/app.css` → CSS; `/app.json` → JSON; `/app.txt` → plain text.
- `/app` → HTML; `/app.html` and `/app.htm` → HTML through the default.
- `/v1.2/app` → HTML; `/v1.2/app.js` → JavaScript.
- `/.js` → HTML; `/app.js/child` → HTML.
- `/APP.JS` → JavaScript if the helper is called with raw text; canonical keys are lowercase.

Response tests:

- A page at `/x.js` with no template directive has `Content-Type: text/javascript; charset=utf-8`.
- A page at `/x.css` that calls `SetContentType("text/html; charset=utf-8")` sends HTML.
- A page at `/x` that calls `SetContentType("text/javascript; charset=utf-8")` still sends
  JavaScript; the override is not limited to the extension's kind.
- `Cache-Control: no-cache` is present for inferred non-HTML pages, absent for HTML pages, and a
  page-set `Cache-Control` wins.
- An authenticated response still gets `no-store` and no ETag, even if the inferred type would
  otherwise get `no-cache`.
- `nosniff` and the default CSP are unchanged; an HTML page must still call `AllowScripts` to load
  a script.

Seed tests:

- `/site.css` and `/assistant.js` still send their expected content types and cache policy after
  their first line changes.
- The stored seed source contains no `<%` if the directive is removed, so it is directly parseable
  as CSS or JavaScript.

## Acceptance criteria

- The extension mapping is a pure `Core` function with table tests.
- A stored `/x.js` page with no page code sends `text/javascript; charset=utf-8`; `/x.css`,
  `/x.json`, `/x.txt` send their mapped types; everything else sends HTML.
- `ctx.SetContentType` overrides the default for any page, including back to HTML.
- Inferred non-HTML pages default to `Cache-Control: no-cache`, page overrides win, and
  authenticated responses remain `no-store` with no ETag.
- `AllowScripts` and CSP are not inferred; `nosniff` remains on every response.
- The seeds keep their behaviour and become directly parseable in their own languages where the
  directive is removed.
- No change to the SDK, the compiler, the page store schema or the template language.
- `make check` passes.

## Risks and open questions

- **Core module version.** Adding `PageContentTypes` to `Core` changes `Core`'s MVID, so
  `RuntimeId` changes and every stored image is recompiled on its next request (DEC-26). That is a
  one-time cost, not a behaviour change, but it should be deliberate.
- **Existing keys that look like assets.** A page at `/notes.json` that relied on the HTML default
  will start being served as JSON. The site is owner-authored and the map is explicit, but a
  pre-deploy audit is cheap. `ctx.SetContentType("text/html; charset=utf-8")` is the escape hatch.
- **Cache default.** `no-cache` on every inferred non-HTML type is a deliberate simplification. A
  page that wants a long max-age can override it; [IDEA-010](../jobs/IDEA-010.md) immutable assets
  would do exactly that. Confirm that is acceptable before implementation.
- **`.svg`.** Deferred. If it is added, record the active-content reasoning and test that
  `nosniff` and the CSP still hold; do not infer `AllowScripts` for it.
- **Where the mapping lives.** `Core` keeps it pure and available to both `Web` and a future
  validator; `Web` would also work but would put a protocol rule in the shell. The plan chooses
  `Core`.
- **Explicit `.html`/`.htm` rows.** The default covers them; adding rows changes nothing and may
  make the map look more complete than it is. Leave them out.
- **Name.** `PageContentTypes` is the working name; `PageFileKinds` would fit better if the
  validator later needs more than a media type and a language.

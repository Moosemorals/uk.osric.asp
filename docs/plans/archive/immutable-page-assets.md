<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Immutable page assets

| | |
| --- | --- |
| Status | Implemented 2026-11-13 |
| Size | Large — a Core flag, a storage column, a second response shape on the page endpoint, a script-policy column for extensions, an Sdk seam, three tagging call sites and the tests for each |
| Area | Core, Sdk, Web |
| Backlog | IDEA-010, closed by this plan |
| Decisions | DEC-238–DEC-248, recorded with this plan |

## The short version

A stored page with no C#, which therefore has nothing the shell can wrap around it, is an **asset**:
its response *is* its source, so a URL that names one exact revision can promise the browser never
to revalidate.

- `?v=<hash>` on a page's own URL selects one retained revision. The value is the stored
  64-character lowercase hexadecimal hash itself. Nothing falls back: an unknown, wrong-case,
  malformed or mismatched hash is a 404, and a versioned URL never quietly serves the current page.
- A page with no C# is **never compiled at all**. Generation already tokenizes it, so it costs one
  branch to know there is nothing to compile: the save stores the flag and no image, and every
  request writes the stored source out.
- A page with no C# is a complete representation: nothing wraps it, so a hand-written or imported
  HTML page is an asset. It may load same-origin modules when its extension row says so.
- A no-code revision is served with `Cache-Control: public, max-age=31536000, immutable` and a
  strong `ETag`. Every other response, versioned or not, stays `no-store` as it is today.
- Links are tagged automatically wherever the shell owns the markup: the document head through
  `PageDocument.Head`, the shell's own views through `ShellDocument`, and any URL a page asks for
  with `ctx.AssetUrl`.

The revision storage this rests on already exists (a unique indexed `Hash` and two hash lookups);
this plan adds the flag that says a revision is safe to pin, the response that honours it, and the
links that use it. The `/asset/{hash}` route IDEA-010 sketched is not needed: keeping the asset at
its own path means `ctx.Path`, `ctx.Request.Path`, relative URLs, query, cookies and headers all
mean exactly what they meant before.

## What exists today

- `PageRevisions` keeps every revision with a unique indexed hash; `IPageStore` already looks a
  revision up by hash (`GetRevisionByHashAsync`, `GetRevisionTemplateByHashAsync`) and never
  substitutes the head.
- Every page response is `no-store` with no validator: `SimpleContext.ToResponse` adds it,
  `PageEndpoint.WritePageAsync` writes it again after the page's own headers and removes any `ETag`.
  The page-session plan accepted the loss of validators on `/site.css` and `/rocket.svg` until this
  job.
- `PageDocument.Head` hardcodes `BaseStylesheets = ["/site.css"]` and a `<link rel="icon"
  href="/rocket.svg">`, so the two most-wanted assets are emitted by the Sdk, not by the shell.
- `PageAssetSeeder` saves a seed as a `PageTemplate` with no image, so nothing is generated until the
  first render. A revision nobody has generated has no opinion about its own source.
- `PageEndpoint` already redirects a non-canonical case with the query string carried across, and
  reserved paths never reach the page route.

## Not in this plan: the assistant's view of history

The assistant can write and tombstone pages and its `diff_page` reads two retained revisions, but it
could not list a page's revisions or read one, so a model that overwrote or deleted the wrong page
could not say what was there. That was deliberately not part of this change: two store reads behind
the owner's sign-in share nothing with a public URL, a cache header or the verdict. It is now
delivered by [revision restore](revision-restore.md).

The two must not be implemented through each other, in either direction. A revision the assistant
reads may have code, may be wrapped, and may belong to a tombstoned path, so `read_revision` uses the
hash lookup `diff_page` already uses and never fetches a `?v=` URL, whose job is selecting and
caching a revision for a browser rather than describing one to a tool. In return, the retained-revision read this plan adds in Step 4 carries the
verdict for the endpoint's response and is not shaped around a tool: a tool that wants the history
has to keep working when the verdict is `NULL`, is `false`, or names a revision that is not an asset.

## Decisions

### 1. `v` addresses a retained revision, and never falls back

`/site.css?v=<hash>` serves the revision the hash names. That revision may be historical, and it may
belong to a tombstoned path. An unknown hash, a hash that is not well formed, a repeated `v`, or a
hash whose revision was stored under a different path is a 404 — never the current page,
because a URL that promised one byte sequence and delivered another is worse than a broken link.

The path is still the page: `ctx.Path` and `ctx.Request.Path` are the URL's path, relative URLs
resolve as they always did, and the content type still comes from the key's extension. This is the
whole reason for a query parameter rather than a separate route. IDEA-010's question about path
semantics is answered by not changing the path at all.

The revision is executed exactly as it would be at its own path: same request, same form, same
cookies, same headers, same layout rules (a page with code is wrapped in the *current* chrome; a
page with no code is wrapped in nothing). `?v` stays in `ctx.Request.Query`, because rewriting one part of the
request would be a lie the page could not see through; the pages that could act on it are the ones
that are not cached anyway.

`v` is a read of a fixed representation, so a method other than `GET` or `HEAD` on a versioned URL
is a 405 with `Allow: GET, HEAD`. A revision whose code writes must not be reachable by a cached
URL, and nothing about an immutable asset needs a body.

### 2. An asset is a page with no code

The condition that makes a revision safe to pin is the condition that makes its response its source:
no code, and no chrome the shell could inject. Those are the same question, and the verdict the
generator already reaches answers it. `PageAssetPolicy.IsAsset` is that one answer.

A page with no code is never wrapped: its source is the complete representation, even for an HTML
key. It gets no banner, no skip link, no `/site.css` and no title requirement. If it wants chrome it
adds a code block, becomes a fragment, and is wrapped by the ordinary rule. A `.json` page that
computes its body has the code that rules it out.

This removes the document extension list from the design. Every extension row still names the media
type, so `.html` has an explicit row and does not inherit a kind from `Fallback`; `.htm` and
`.xhtml` are ordinary rows. A page with code may write its own whole document by clearing
`IPageLayout.Layout`: it has code, so it can say so, and it is not an asset.

Two edges the help page has to teach, because both are invisible in the editor:

- A no-code `.html` key gets no navigation, no skip link, no `/site.css` and no title requirement.
  It is a complete document the author wrote rather than a fragment the site completed.
- A no-code page that wants the site chrome must add a code block. There is no hidden layout for a
  static page, which is what keeps the two halves of this decision in step.

The seeded `/rocket.svg` loses the C# block at the top that sets its content type — `.svg` is
already mapped to `image/svg+xml` — so the icon qualifies. Existing sites keep the old seed until
the owner restores it, which is the existing seeding rule and not a reason to break it.

### 3. The verdict comes from generation, and a page with no code is never compiled

`TemplateSourceGenerator.Generate` already tokenizes the template. It gains the answer it is holding
and throwing away: `TemplateGenerationResult.HasCode` is true when any token other than `Text`
appears. That is a *token* check, not a text scan, so a stylesheet containing `<!--` or a script
containing a quoted `"<%"` is judged by the same rules the template language uses.

The verdict decides whether there is anything to compile at all. `PageImageBuilder.Build` stops
after generation when `HasCode` is false: no compile slot is taken, no Roslyn call is made, and the
revision is stored with no image and no diagnostics. A template made only of `Text` tokens cannot
fail to parse either — the tokenizer's one error needs a `<%` — so a no-code page has nothing to
report in the editor, ever.

The verdict travels on `PageImage` to the new nullable `PageRevisions.TemplateHasCode` column, and
is written wherever a revision is generated: an editor save, an import, and a lazy compile. It is
**not** part of the image bytes, because it is a property of the source: a revision whose image is
discarded on a runtime change keeps its verdict, and a revision recompiled under a new runtime
answers the same way it did before.

There is no migration pass and no backfill. An existing revision keeps `NULL` until something
generates it again, and `NULL` means *not an asset*. A revision whose image is still current is
never regenerated, so it keeps `NULL` — including the live `/site.css` — and stays unversioned
until it is next saved. A revision regenerated for any other reason gets its verdict then, which is
free and honest. The owner adopts the feature by restoring the seed or making any edit, and the plan
says so rather than pretending otherwise.

### 4. A versioned asset promises `immutable`, and carries a validator

```
Cache-Control: public, max-age=31536000, immutable
ETag: "<hash>"
```

`public`, because a C#-free page cannot read identity, write a session or vary by visitor; the
response has none of the hazards that made every page `no-store`. A year, because that is what
`immutable` is worth claiming. The `ETag` is the hash itself, strong: if a browser or proxy
revalidates anyway, `If-None-Match` answers 304 with the same cache headers and no body. `HEAD`
answers as it always does, headers and `Content-Length` with no body.

Compression stays Caddy's job; the app writes no `Vary`.

### 5. Unversioned responses stay `no-store`

A request without `v` is served exactly as it is today, for every page including an asset. An
unversioned asset URL is not immutable-safe — the browser would keep an old copy under a URL that
no longer means it — and one rule for every unversioned response is one rule to test. Tagging is
what makes a URL cacheable; a bare URL is a fallback that still works.

This decision changes no bytes on the wire: an unversioned asset is still the stored source written
with `no-store` (decision 7), so it is the same body it is today.

### 6. The verdict decides caching, never routing

A `v` that names a revision which is not an asset — it has code, or the shell wraps it — still
serves that revision, through the normal render path, with `no-store`. The hash's job is selection;
the flag's job is the promise. Keeping them apart means one lookup and one rule, and a historical
revision of `/help` is as addressable as `/site.css` while promising nothing.

### 7. A page with no code is served from source, never executed

For an asset nothing is compiled, nothing is loaded and no render slot is taken: the
stored source is the body, byte for byte, on every request. The `v` parameter selects *which*
revision and whether the answer may be cached; it never selects *how* the bytes are produced. That
is the only way the `immutable` promise is a fact rather than an inference about the compiler, and
it keeps a stylesheet out of the one compile slot and off the render budget.

Because every no-code revision is a complete representation, there is no partial case to explain: no
layer renders around it and no capability is offered to a page that will not run.

The long path must agree with the shortcut, so a test pins the invariant that the handler generated
for a template with no code writes exactly its source. If that ever stops being true the flag is
wrong, and the test says so before a browser caches the difference.

### 8. Tagging happens in the three places that own markup

- **The document head of a stored page.** `PageDocument.Head` gains an optional URL resolver,
  applied to the base sheets, the sheets the view asked for and the icon. It stays a pure string
  builder: the resolver is the caller's, so `Sdk` gains a parameter and no dependency.
- **The shell's own views.** `ShellDocument.BeginAsync` resolves the base sheet, the icon and its
  `stylesheets` argument before writing the document, so the views' sheet lists need no rewrite.
  A view's script tag is part of the `head` markup that view writes, so that view resolves its own
  script path through the same resolver.
- **A page's own markup.** `ctx.AssetUrl(href)` answers with the versioned URL when the target is a
  live, no-code page that nothing wraps, and with the href unchanged otherwise. A page can use it
  for a script, an image or a sheet it writes itself, which is the only way markup inside a body can
  be tagged: rewriting rendered HTML is not on the table.

Resolution is one read of the heads per render, held in memory for the render: a page's sheets are
chosen while it runs, so the shell cannot know the set in advance, and `ctx.AssetUrl` cannot be
asynchronous without making every layout awkward. One small query per render, then a dictionary
lookup, is the same shape as the rest of this codebase. A stale entry inside one render is harmless:
the hash still names a retained revision, so the older URL still serves the older bytes.

`AssetUrl` only touches a bare site-relative path. Anything with a query or fragment, anything
absolute or protocol-relative, and any key that is not a live no-code asset is returned unchanged,
which is what keeps an unversioned fallback link working when an asset is missing, renamed or
broken.

`PageDocument.Head`'s resolver is optional, so a stored layout that does not pass one keeps
today's markup. The seeded `/_layout` passes `ctx.AssetUrl`, and existing sites adopt versioned
links when the owner restores the seed — the same rule as every other seed change.

### 9. The token is the revision hash itself

The hash stays lowercase hexadecimal in storage: the unique index, the archive format and every
existing row already depend on it. The URL carries that same 64-character value, so `v` needs no
escaping in a query or attribute and no conversion on either side. It is also the value the editor's
history and the assistant's revision reads already name, so there is one identity rather than two
spellings of it.

Matching is exact ordinal equality, and `RevisionHash.IsCanonical` accepts only the lowercase
storage spelling: uppercase, truncation, padding and any other variant are a 404. A shorter token
would have bought a few URL bytes at the price of a canonicalisation or ambiguity rule, which is not
worth having here.

### 10. A tombstone keeps its assets; a scrub does not

Deleting a page hides its public URL. Its revisions stay, so its versioned URLs keep answering:
the hash names a row that still exists, and a page that embedded the old asset keeps working.
A scrub removes the rows, so those URLs 404. Neither can recall a copy a browser or a proxy already
holds — the asset may have been a committed secret, and the help page and the operations notes
must say that rotating the secret and cleaning external copies are separate work.

### 11. The script policy belongs to the extension row

`SecurityHeaders` writes `script-src 'none'` for every response unless something widens it, and the
only thing that widens it today is `ctx.AllowScripts()` in page code. A page with no code has no code
to call it, so a static document that wants a module cannot opt in from its source.

Each extension row therefore carries a `ScriptPolicy` of `none` or `self`. The policy follows the
key's extension and is applied before the page runs, so it does not follow a `SetContentType` and it
does not depend on the revision's verdict or on whether the URL is versioned. A page with code may
widen `none` with `AllowScripts`; it cannot narrow `self`. Inline script, `javascript:` URLs and
event-handler attributes stay blocked either way, because the policy never allows `unsafe-inline`.

The shipped `.html` row is `none`, so a static document that needs a module is one configuration
change away, not a hidden code default. A row such as `.htmlx` with `self` is the operator's choice.
The shell's own views are untouched.

## Implementation

Each step is a reviewable unit, not an instruction to commit. Use the existing Makefile targets; add
no parallel scripts. New files need SPDX headers.

### Step 1 — The verdict in Core

- [x] Add `HasCode` to `TemplateGenerationResult`, set by `Generate` from the tokens it already
  reads: `false` only when every token is `Text`.
- [x] Keep the tokenizer internal. `Generate` is the one place that answers, so there is no second
  implementation to drift.
- [x] Update `PublicAPI.Unshipped.txt`.

**Done when:** `make test-core` proves a pure-text template, an empty template and a template with
no block are `HasCode: false`; each of code, declaration, write and raw-write is `true`; and a text
template containing `<!--` or a quoted `"<%"` is still `false`.

### Step 2 — Carry and store the verdict

- [x] `PageImage` carries `HasCode`; `PageImageBuilder.Build` sets it from the generation result
  beside `TemplateHash` and `RuntimeId`, and returns before the compile when the template has no
  code: no compile slot, no Roslyn call, no image, no diagnostics.
- [x] Add nullable `PageRevisions.TemplateHasCode` and its mapping. Write it on save
  (`SqlitePageStore`, `SqlitePageBatchStore`) and on `TrySetImageAsync`, so a stale revision that is
  lazily recompiled records the verdict that compile reached. Never clear it with the image.
- [x] The renderer decides on the verdict before its staleness path. `false` is served from source
  and is never generated, compiled or counted as `never-compiled`; `NULL` is generated once — that
  is the first render of a seed — and the result is stored: no image and the flag for a no-code
  revision, an image and the flag for the rest. After that a page's cost is fixed by what it is.
- [x] `PageTemplate`, `PageImage`, `PageReadHead` and `PageRevision` surfaces carry it as
  `bool?`, so a `NULL` is distinguishable from `false` and means "never judged".
- [x] An import that carries an image for a no-code revision stores the verdict and discards the
  image, rather than keeping bytes nothing will use.
- [x] Leave existing rows alone: no migration pass, no backfill.

**Done when:** the storage tests prove a no-code save takes no compile slot and stores no image, a
save of a page with code stores the verdict and its image, a lazy compile fills a `NULL` verdict,
and no path can write a verdict without having generated the exact source it describes.

### Step 3 — No-code pages are complete

- [x] A page with no code is served from source and never wrapped; the endpoint skips the chain on
  `RenderResult.IsSource`. A page with code may still clear `IPageLayout.Layout` to write its own
  document.
- [x] Each extension row carries its media type and `ScriptPolicy`; the policy follows the key's
  extension and is applied before the page runs.
- [x] Add the explicit `.html` row to the default map with `ScriptPolicy: none`.
- [x] Express "is an asset" once — a page with no code — and use that one answer from the endpoint,
  `ctx.AssetUrl`, the head resolver and `PageEntry.IsAsset`, so the seeded listings label an asset
  by its verdict rather than by whether its key happens to be HTML.

**Done when:** a no-code `.html` page is served raw, with no banner, no `/site.css` and no title
requirement, under the `.html` row's policy; a code page that clears `layout.Layout` is served
alone; `/about` is still wrapped and still `'none'`; a code `.htm` is wrapped; a no-code `.htm` is
raw; and a row configured with `ScriptPolicy: self` gets `'self'` while a `none` row does not.

### Step 4 — Address a revision by hash

- [x] A pure Core validator for the `v` value: `RevisionHash.IsCanonical` accepts exactly 64
  lowercase hexadecimal characters. Pin length, alphabet, casing and the absence of padding in tests.
- [x] Add a retained-revision read that returns the path, source and verdict together, or extend
  `GetRevisionByHashAsync` to carry them.
- [x] In `PageEndpoint`, before the head is read: parse `v` (repeated, empty or malformed is a 404),
  look the revision up, and reject a revision stored under another path with a 404. Reserved and
  unaddressable paths keep their existing refusals.
- [x] Select the *revision* rather than the head for the rest of the request: the renderer needs a
  way to take a given `PageTemplate` — source for an asset, its image or a lazy compile for a
  revision with code — and the chain must keep using the URL's path.

**Done when:** `Web.Tests` proves an old revision of a page can be rendered after a newer save, a
hash naming another path is a 404, a malformed or repeated hash is a 404, a tombstoned path
answers at its versioned URL but 404s at its own, and the canonical-case redirect carries the query.

### Step 5 — The asset response

- [x] Serve an asset from source on **every** request: versioned, it is written with the
  immutable headers, the `ETag`, `Content-Length`, the extension's content type and the shell's
  security headers; unversioned, the same bytes go out with `no-store` and no validator.
- [x] Answer `If-None-Match` on a versioned asset with 304 and the same cache headers. `HEAD` writes
  headers and `Content-Length` with no body.
- [x] Refuse a method other than `GET`/`HEAD` on a versioned URL with a 405 and `Allow`.
- [x] Write the response on its own path, so `WritePageAsync`'s unconditional `no-store` and
  `ETag` removal cannot reach it.
- [x] Count the outcome on the existing meter — served, not-modified, unknown or non-asset — with no
  path label.

**Done when:** `Web.Tests` and `Web.IntegrationTests` prove the header set on an asset, the 304, the
405, no session cookie, no compile slot taken and no image stored, that a `.html` asset arrives as a
complete document with its own `<head>` and no chrome injected, and that an unversioned asset and
every non-asset response are still `no-store` without an `ETag`.

### Step 6 — Tag the links

- [x] `PageDocument.Head` takes an optional `Func<string, string>` and applies it to the base
  sheets, the requested sheets and the icon; the default leaves today's markup untouched.
- [x] `IContext.AssetUrl(href)` resolves against the render's map of live no-code heads, and returns
  the href unchanged in every other case. Document it on `IContext` and in the help page.
- [x] Load the map once per page render, through the existing read budget, and hand it to the chain.
- [x] `ShellDocument.BeginAsync` resolves its base sheet, icon and `stylesheets` through the same
  map before building the document; each shell view resolves its own script path as it writes its
  `head` markup.
- [x] Update the seeded `/_layout` to pass `ctx.AssetUrl`, and `/rocket.svg` to drop its C# block.

**Done when:** a page's document links `/site.css?v=…` and `/rocket.svg?v=…`; the editor, admin,
assistant and status documents link versioned sheets and scripts; a missing, deleted or
never-compiled asset is linked bare; an authored `ctx.AssetUrl("/my.js")` is versioned only when
that page is a no-code asset; and a layout that passes no resolver emits exactly what it does today.

### Step 7 — Prove the invariant and the contract

- [x] A test that the generated handler for a template with no code writes its source byte for byte,
  including line endings — the guard on decision 7, even though the builder short-circuits before
  the compiler.
- [x] A test that a save of a page with no code calls neither the compiler nor the compile queue.
- [x] A test that an edit changes the head's hash and that the previous hash still serves the
  previous bytes.
- [x] A test that the asset is served without compiling, by counting compiler calls, on both the
  versioned and the unversioned route.
- [x] Browser tests are not added: nothing here is a browser behaviour the unit and integration
  tests cannot prove.

### Step 8 — Documentation

- [x] `/help`: the `v` parameter, what an asset is, `ctx.AssetUrl`, why an unversioned link still
  works, that a page with no C# is never compiled (so the editor has no diagnostics to show for
  it), that a no-code page is a complete representation with no chrome and no title requirement,
  how an extension row's `ScriptPolicy` controls modules, how to pin a module by hand, and that an
  immutable copy cannot be recalled after a scrub. A page-author-facing change lands with the
  manual, per the repository rule.
- [x] `docs/pages.md`: the request contract gains `v` and the caching rule; `ctx.AssetUrl` in the
  `IContext` list; a no-code page compiles nothing and is never wrapped; the content-type section
  gains the per-extension `ScriptPolicy` column and the explicit `.html` row.
- [x] `docs/shell.md`: the second response shape, who may promise `immutable`, and the layout rule
  that a no-code page is never wrapped.
- [x] `docs/storage.md`: the column, the no-image revision, the lazy-compile fill and the absence of
  a backfill.
- [x] `docs/metrics.md`: the new asset outcome counter.
- [x] `docs/glossary.md`: *asset*, *versioned URL*, *document*.
- [x] Close IDEA-010 and add its row to the jobs README when the work merges,
  noting that the `/asset/{hash}` route was replaced by a query parameter and that runtime-change
  binary cleanup is still open.

### Step 9 — The gate

- [x] `make build`, `make test`, `make format`, `make license-check`.
- [x] Take the decisions above, give them the next `DEC-` numbers from `docs/decisions.md` on
  trunk, and update every citation in the branch in the merge commit: DEC-238 to DEC-248.

## Considered and rejected

- **`/asset/{hash}` as a route.** It is the sketch in IDEA-010 and it moves the page's path out from
  under its own code: `ctx.Path`, relative URLs and any page comparing itself to `ctx.Request.Path`
  would all need a rule. A query parameter selects the same revision without moving anything.
- **A text scan for `<%`.** The tokenizer already decides what a template is; a second, simpler
  scanner would disagree with it on exactly the awkward inputs.
- **Hardcoding `.html` in the shell.** The switch would be invisible to the owner, would hold only
  because the fallback happens to be HTML today, and could not follow a host that serves HTML under
  another extension.
- **A document extension list.** A second list beside the type map would have to be kept in step
  with it, and the verdict already answers the only question that matters: no code means nothing
  wraps it. The extension row still decides the type and the script policy.
- **Hardcoding `script-src 'self'` for documents.** A static page with no module would get a wider
  policy than it needs, and the operator would have no way to choose. The policy is a configured
  column with `none` as the shipped `.html` value.
- **Letting the cache flag choose the CSP.** The same markup would get a different security policy
  depending on whether a revision happened to contain code, so editing a document to add a title
  would silently remove its scripts.
- **Backfilling the flag in a migration.** It buys a live site immediate versioned links in exchange
  for a migration that has to tokenize every stored revision and a second code path that can drift
  from the compiler. Restoring the seed is one click and the owner is the only reader.
- **Versioning every same-origin link by rewriting the response body.** The body is the page's own
  markup; rewriting it would make the shell a text processor and would break on any page that
  writes a URL in a way the rewriter did not anticipate.
- **Cacheable unversioned assets.** `no-store` everywhere is one rule; `no-cache` plus a validator
  for one class of page is a second caching policy to reason about, for links that the tagging
  makes rare.
- **A shorter token by truncating the hash.** Shorter hrefs, but a prefix collision needs an
  ambiguity rule and a range lookup, and the honest answer to a collision is a 404 for a URL that
  was generated by the site itself.

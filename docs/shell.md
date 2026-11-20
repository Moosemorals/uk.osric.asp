<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The shell

Everything the site serves that is not a stored page: the document shape every view shares, the
layout a page is wrapped in, the error document, and who may reach what.
[Architecture](architecture.md) says where this code lives; [pages](pages.md) says how the page
pipeline works.

## The document shell

Everything the site's own code serves — sign-in, status, the error branches, the recovery
confirmations and the administration views — is one document shape, written by `Html/ShellDocument.cs`. `BeginAsync` sets the
content type, the status and `Cache-Control: no-store` (unless the response already carries a header,
which is how a view that mints an antiforgery token keeps the framework's `no-cache, no-store`),
writes the head (the title, `/site.css`, the view's own sheet where it has one, then the icon) and
opens `<main id="main">`; `EndAsync` closes the landmark and the document. The sheets and the icon
are resolved through `PageAssetMap`, one read per request cached on the request, so a live asset is
linked with its `?v=` hash; a view's own `<script>` path is resolved the same way through
`ShellDocument.AssetUrlAsync`. A view writes its own body
content and nothing else, so the title, the landmark and the sheet order cannot drift apart from one
view to the next.

- The body opens with the skip link, then the banner, then the one `main`. `BeginAsync` requires a
  navigation argument, so no view can be written without a menu: the shell composes its own with
  `SiteNavigation`, and the seeded layout writes the same items for stored pages, filtered from the
  catalog's `ShellLinks()` (`shell-the-menu-is-written-twice`, `pages-the-document-is-the-shells`).
  The header carries where the visitor can go from here; the footer carries About and the privacy
  policy on every page, and the shell writes the same footer from `SiteNavigation.FooterLinks()`.
- A shell document is marked (`shell-the-shell-is-marked`): `body` carries `class="shell"` before any
  class the view asked for, and the banner carries a visible **Site tools** label before the
  navigation landmark. Both are text and structure rather than colour, so a shell page says what it
  is with no sheet and to a screen reader. The stored layout carries neither, which is the point: a
  shell page answers when the page store is broken, so its menu is the site's routes rather than the
  pages the store holds.
- `/site.css` is a seeded page and the only base sheet (`pages-one-base-sheet`). It holds the document's shape —
  the visually hidden utility, the listing and the form grid — and the site's look, with the shape
  rules first. The view's own sheet is linked last, so it can override a structural rule without a
  more specific selector. The owner restores a stale seeded sheet from the page list.
- `/admin/status` writes body content only — its heading, the administration landmark, its window
  nav, host line, banner, jump bar and its groups of figures — and its document is the builder's,
  like every other view's. A chart it draws can also be requested on its own at
  `/admin/status/chart`, which is a drawing rather than a document and so does not go through this
  builder.
- `PageLayout` keeps the fragments a shell view writes: `Gutter` and `Diagnostics` (a `ul` with `role="list"`,
  `shell-the-markup-is-semantic`). Everything the shell's own document needs — the head with its base sheets and icon, the
  banner with the skip link, `Nav`, `OpenMain`/`CloseMain` and the close — is
  `Html/ShellDocument.cs`, and only the shell writes it (`pages-the-document-is-the-shells`). A stored page does
  not call it: the seeded layout writes its own document with the seeded
  [`/_lib/document`](../src/uk.osric.asp.Web/Pages/Seeds/_lib/document) library, so the owner can
  edit and restore what a stored page is served in. `Html/` keeps `DiagnosticLine`,
  `AntiforgeryField`, `SiteNavigation` and `PageUrls` beside it.
- A stored page with code is a fragment, and it declares the pages that wrap it, so it writes a
  fragment rather than a document; the wrapper writes the head, the banner and the landmark with the
  library. See "Layouts".

Every one of these views is rendered once by `ShellViews` and checked against the shared accessibility
rules — see [testing](testing.md#the-five-suites) — with what a string cannot
prove left to a recorded hand pass (`shell-accessibility-is-asserted`).

## Layouts

A page with code is a fragment, and nothing wraps it by default. It declares the pages that wrap it
with `IPageComposition`: `Wrap("/_some_layout")` names one, `Wrap()` names the layout in
`Pages:Layout` (`/_layout` as seeded), and calls stack outward, so `Wrap("/_markdown"); Wrap()` is the
markdown wrapper first and the site layout outside it (`pages-a-page-chooses-its-layout`). A page that
declares none is served exactly as it wrote itself.

The host runs the chain after `HandleAsync` returns, so a guard's early `return;` is still wrapped.
It renders inside out — the page first, then its wrapper, then the wrapper's wrapper — and writes the
outermost body. A redirect, a `HEAD`'s empty body, a non-HTML content type and a no-code page are never
wrapped: they are served exactly as the page asked (`pages-one-request-one-chain`). A page that
flushes its response declares no wrapper, and a `Wrap` after the first flush throws.

The wrapper receives the layer inside it as `IPageLayout.Content`, a `PageCapture` beside the frame of
the outermost layer inside it, and folds what it wants: the host merges nothing behind its back
(`pages-a-page-inside-a-page-is-a-capture`, `pages-the-wrapper-owns-the-title`). The one thing the
host composes is the script permission, unioned so a document that carries a body cannot lose the
permission its script needs. A wrapper does not own the response: status, content type, headers and a
redirect are ignored and logged, except that an embedded page's redirect is refused. `Title` is a
`string?`, so the wrapper decides what a missing title means. A wrapper must have code, because a
no-code page has no way to read `Content`.

The chain shares one render slot, deadline, body allowance, fetch allowance and template allowance,
and one write, so two layers do not get two bites. `Pages:Render:MaxLayoutDepth` bounds the number of
wrappers; a page that reaches itself again with the same request is refused as a loop. The layout
named by `Wrap()` degrades: one that cannot be served is skipped with a warning and
`page.layout.fallback`, and the page is served unwrapped. A page named by `Wrap(key)` is an error when
it cannot be served, because silence would be worse.

## Composition

A page can render another stored page through `IPageComposition` (`pages-an-embed-is-a-render`).
`EmbedAsync(key, request?)` renders the whole page — its body and every wrapper it declares — with
its own key, storage, session and capabilities, the caller's identity, and the request the call gives
it, and writes the outermost body where the call is. It returns a `PageFrame`: the outermost layer's
title, head, sheets and script permission, never the body, which is already written. `CaptureAsync`
is the same render returned as a `PageCapture` with the write left out. An embedded page is not
sandboxed: it may write the store and start a job, and it cannot redirect because it has no response
of its own. A failure throws, and a page may catch it: the embedded output never reached the page's
body, so nothing is half-written (`pages-a-capture-returns-the-body`).

The input is the request, in both directions. A null request forwards the caller's, and one that is
given runs as it stands except that `Path` stays the visitor's page, so `ctx.Path != ctx.Request.Path`
is how a page knows it is inside another (`pages-a-composed-page-gets-a-request`).
`PageRequestExtensions` is the copy-and-change surface.

Recursion is bounded and honest. The same page with the same request anywhere above this one is a
loop and is refused; the same page with a different request is a recursive component, allowed,
counted as `page.composition.recursive` and bounded by `Pages:Render:MaxEmbedDepth`.
`Pages:Render:MaxEmbeds` bounds the fan-out of one request, because depth alone would let a listing
embed a thousand siblings under one slot and one deadline.

## The response shapes

There are three response shapes. The ordinary one is buffered, wrapped where the rules say, and
written by `PageResponseWriter` and `WritePageAsync` with `Cache-Control: no-store` and no
validator. The second exists only for an asset: a revision whose kind is text. It is never rendered,
loaded or compiled;
the stored source is the response, and `PageEndpoint.WriteSourceAsync` writes it on its own path with
the extension's content type, `Content-Length`, the shell's security headers and no session cookie.

An unversioned asset is still `no-store`. A versioned asset — `?v=` naming a retained revision — is
the one response that may promise `Cache-Control: public, max-age=31536000, immutable` and a strong
`ETag`; `If-None-Match` answers `304` with the same cache headers. Only `GET` and `HEAD` are answered
on a versioned URL. The versioned response is separate from `WritePageAsync` so that function's
unconditional `no-store` and `ETag` removal cannot reach it. [Pages](pages.md#assets-and-versioned-urls)
has the selection rules and `docs/metrics.md` the `page.asset.requests` outcome counter.

The third shape is a **streamed** page: a page with code that calls `ctx.Response.FlushAsync()`. The first
flush commits the response through `PageBodySink` and `PageResponseWriter`, and each later flush
writes the buffer through the same sink. `PageEndpoint` skips `PageChain.WrapAsync` and
`WritePageAsync` entirely once `SimpleContext.IsCommitted` is true, so a streamed page is never
wrapped; a `Wrap` call after the flush throws. A committed failure is a truncation: the endpoint logs
and aborts rather than attempting an error page or a `504`, and `DisconnectGuard` treats a client that
left mid-write as a Debug-level end rather than an unhandled exception
([pages](pages.md#streaming)).

The chain renders inside out, content first, because only then does the host know what the content
produced. Each layer is a `PageCapture` — a body beside a `PageFrame` — and the layer outside consumes
it as `IPageLayout.Content`:

```
render /about   → buffer 1   (sets Title, AddHead, AddStylesheet)
render /_layout → buffer 2   (Content = capture of buffer 1)
write buffer 2
```

- **One budget for the chain.** `PageRenderer.OpenSessionAsync` opens before the content layer is
  built, so one render slot and one deadline cover every layer and every embed; each layer renders
  under them with its own key on the metric. The fetch allowance, the body allowance, the write and
  the antiforgery pair are shared the same way, so a layout cannot spend them twice
  (`pages-one-request-one-chain`).
- **The content owns the response.** Status, content type, headers and the redirect come from the
  layer the visitor asked for; a layout or an embedded page that tries to set one is ignored and
  logged, and an embedded page's redirect is refused (`pages-one-render-three-roles`,
  `pages-layouts-are-markup`). `AllowScripts` is the union, because the wrapper's script is part of
  its chrome.
- **The wrapper owns the title.** `Title` is a `string?` and the wrapper decides what a missing one
  means, so the host no longer refuses a page that forgot
  (`pages-the-wrapper-owns-the-title`).
- **A page controls its own sheets.** `AddStylesheet` once per link, from none to many; an href that
  is not site-relative is refused rather than blocked silently by `style-src 'self'`. The wrapper
  links the page's sheets where its document wants them, and the seeded document library keeps the
  base sheet first, then the wrapper's, then the page's, with an exact duplicate linked once
  (`pages-one-base-sheet`).
- **Depth and cycles.** `Pages:Render:MaxLayoutDepth` (four shipped, accepted 1–16) bounds the
  number of wrappers, and the ancestor stack refuses a page that reaches itself again with the same
  request; the same page with a different request is a recursive component, allowed and capped by
  `Pages:Render:MaxEmbedDepth`.
- **Failure is not symmetric.** A *default* wrapper that is missing or does not compile serves the
  page unwrapped, logs a warning and counts `page.layout.fallback`; a wrapper the page *named* that
  cannot be served is an error naming the key.
- **The menu is written twice, on purpose.** `SiteNavigation` composes it from the principal for the
  shell's own views, and the seeded `/_layout` writes it from `ctx.User` for stored pages — the header
  items and the constant footer alike. Both read one list: the shell routes and their menus in
  `Shell/ShellCatalogue.cs`, which `IPageCatalog.ShellLinks()` gives a page as `ShellLink` values.
  `AdminNavigation` filters the same values for the administration views, so a route cannot appear in
  one menu and not the other (`pages-the-catalog-knows-the-shell`). The shell's document must not
  depend on a stored page, because the error, health and sign-in routes have to answer when the page
  pipeline is broken (`shell-the-menu-is-written-twice`). The shell's copy comes from
  `ShellDocument.Nav`, the layout's from the seeded library, and an integration test compares the
  document shape the two write as well as the items in it. The footer is the layout's own chrome and
  is not in the list; the editor is a stored page, so its menu is written by the page rather than by
  `SiteNavigation`.

The layout is not a guard: the content has already run. It is not a data boundary: it reads its own
storage and nothing of the page's.

The author-facing version of this section is the seeded `/help` page, which is itself a page the
owner can edit and restore. It is linked from the editor page; a change to what a page author can
do updates it in the same change.

## The administration index

`/admin` is a route, not a page: it answers anyone, and it draws the administration section of the
same link list the menus use, minus the tools this visitor may not reach. An anonymous visitor sees
the public tools and a line saying the rest needs a sign-in; there is no redirect. The key is
reserved, so the editor refuses to store a page there.

## The copyright page

`/admin/copyright` renders the licence and notice files the image carries — `LICENSE`,
`THIRD-PARTY-NOTICES.md`, the `LICENSES` texts and `REUSE.toml`. They are packed beside the assembly
for the same reason the changelist is: the container has no checkout. The route is public like the
reference, and `/about` links to it rather than carrying a copy that has to be kept in step.

## The shell's own files

`admin.css`, `admin.js`, `status.css`, `reference.css` and `rocket.svg` are in the project's
`wwwroot` and served by the static file handler, which runs before routing so a file answers before
the page catch-all can claim its key. They are not pages: they are not seeded, not listed by the
editor and not versioned by `/?v=`. The five keys are in the shell's route table, so the editor refuses
a page that the file would shadow. An installation that predates the move keeps a seeded row at each
key; the file shadows it, and the **Stored pages at shell paths** section of `/admin/seeds` lists it
for the owner to scrub. `/site.css` and the editor's and assistant's sheets and scripts
are still seeded, because a stored page links them and the owner may want to fix one without a
release.

## The reference

`/admin/reference` reads the XML documentation the build carries. The four product assemblies emit it
(`GenerateDocumentationFile`, `code-analyzers-are-strict`), `dotnet publish` copies each file beside its assembly, and the
container carries them at `/app`; `/admin/reference` is the index and `/admin/reference/{slug}` is one
assembly.

The shell owns it for the same reason it owns the changelist: the XML is a build artifact the image
carries, and a page cannot read a file beside the assembly ([the build's own
facts](architecture.md#where-a-view-lives)). It is public even though the key is under `/admin` — the
URL is a filing cabinet, not an authorisation — and it carries the admin menu because that is where a
reader finds it. `ReferenceLibrary` reads the four files once, on first use, normalises each into one
document (the owner computed from the member id, stable anchors, shortened signatures, `see` links
resolved across the corpus or to Microsoft Learn), and `ReferenceRenderer` transforms each document
once with the embedded `reference.xslt`; the fragment is held for the process. A missing file is an
assembly the index does not list, and a build with none says so rather than failing. An
`<inheritdoc/>` entry is copied down from its base or interface member when that source is one of
ours, and names the source when it is not. The `/reference.css` file in the web root styles the
document and marks a link that leaves the site with an arrow.

## Error responses

No person is handed an empty body, and no program is handed a document it cannot parse. One writer,
`Errors/ErrorResponses.cs`, takes an `ErrorMessage` — a stable `Code`, a `Title`, a plain-language
`Summary` and the `Remedies` that lead somewhere useful — and writes it for the audience the request
named: a browser gets the document shell with a heading, the explanation, a recovery nav, the error
code and the reference id; a request that accepts JSON without HTML gets an RFC 9457 problem document at
`application/problem+json`, with the code as an extension member. `ErrorAudience` makes that choice
from `Accept` alone; a request that names neither is treated as a browser, because a person who gets a
document can still read it.

The recovery nav gains an "Edit this page" link for a signed-in owner whenever the failing request
named a page the site serves — the key is addressable and not reserved by the shell — and never
for a shell route, so a failure on `/metrics` cannot offer to edit it. The link is derived from the
request by the writer rather than passed in by each call site, which is why the layout errors from
`PageChain` and the exception page agree. A remedy that already leads to that editor URL, such as
`NotFound`'s "Create this page", is left as it is.

Three layers keep the promise:

- Call sites that know the problem say it. `NotFound` keeps `"{key} not found."` and adds a
  "Create this page" link for a signed-in owner; the editor page names an unknown action instead of
  stopping at a status.
- `UseStatusCodePages` is the guarantee. An endpoint, the framework, a routing 404/405, a rejected
  antiforgery token or the rate limiter can all answer with a status and nothing else; the fallback
  turns that into `ErrorMessage.ForStatus`, or `ErrorMessage.FormExpired` for a refused POST, and
  writes nothing when a body, content type or length is already present.
- Where the failure is recoverable, the host recovers and says so. A rejected sign-in form is
  re-rendered with a fresh token rather than reported, and a "Save and view" whose save has compile
  errors returns the author to the editor with the diagnostics instead of a page that cannot render.

`ErrorEndpoint` keeps the exception path: a 500 is the same document, with stack detail only for a
signed-in owner, and a problem document when the request wanted JSON.

## Authentication and the editor

There is no editor in the shell. The editor is a seeded page at `Pages:Editor:Key` — `/pages` as
shipped — so it is served by the page pipeline and guards itself with the ordinary `ctx.User` check.
What keeps it trustworthy is not a route but the page's own guard: `IPageWrites` is offered to
every page now, so the editor's key decides where the editor is served and nothing else, and the
authority to write is the signed-in visitor's (`pages-any-page-may-write-the-store`). The shell still
owns the things a page must not: routing, the session, the recovery routes and the typed confirmation
a scrub needs. Pages stay public unless their own code checks `ctx.User` (see
"Page authentication" below).

A start applies the seed a release moved. After the create-seeder and before the host listens, the
pack is planned against the store and every row that says *release newer* — the release moved the
seed, and the page still holds the one it was installed from — is written through the ordinary seed
save, so it compiles first, records the shipped hash as the anchor and keeps the source it replaced in
history. It happens on every start and remembers nothing, which is what makes it a rule rather than a
deploy step: after the write the row is in sync, so the next start has nothing to do. A page the owner
edited is never touched, so editing a page is how a source the release moved on from is kept. Nothing
else is written automatically — a *new* seed is created by the create-seeder and the rest need a tick
— and a command gets the create-seeder alone, because it is an operator reading or extracting the
store rather than a host about to serve.

A seeded page is also installed from its row by hand: the row says *release newer* and the page still
holds the seed it was installed from. The action opens a confirmation that shows the diff against the
seed, and its button saves the bundled source through the ordinary revision path, so a page comes back
without discarding its earlier revisions. A row whose content already is the shipped seed but whose
record says otherwise offers *Record as installed*, a POST that writes the anchor and nothing else. A
page that drifted is shown with the diff and no action: the capability refuses an install on it, so a
hand-made form cannot do what the page will not offer. A seed is *installed* and a saved revision is
*restored*: `?edit=<key>&cmd=install` on the seed,
`?rev=<hash>&cmd=restore` from the page's history, both confirmed the same way and both refusing the
change that would leave the live source as it is. The page's history offers a restore link on every
revision except the current one; the editor says when a key was deleted and that saving brings it
back. A deleted page's history is scrubbed from `/admin/scrub/<key>`, which is the shell's route
because it is irreversible: the owner types the page's key to confirm, and a live page has to be
deleted before it can be scrubbed.

The seed page is `/admin/seeds`: it lists every page the release ships with its verdict and a tick for
each row the owner chooses. The safe rows — *new*, *release newer* and the rows whose content already
matches but have no record of installing it — start ticked; the rows that could replace the owner's
source — *edited*, *conflict*, *deleted* and *unknown* — start unticked. A *release newer* row is
usually written by the next start, so its tick is for a row a start has not reached; a row whose
content already matches is recorded as installed here, because only the owner can say where a page
came from. A `POST` writes exactly the ticked rows, refuses a row that changed after the page loaded,
and never stops at a drift.
`?diff=<key>` shows one row's change in the same bare document. The same page lists every stored path
the shell answers — a seed that became a shell file leaves one — and scrubs the ticked keys after the
owner types `scrub`; it re-checks that each key is still reserved before it removes a head, revision
or state row.

- Accounts live in the `Users` table in the same SQLite database. There is no registration endpoint
  and no account UI: the `users` command in the Web app creates, lists, resets and removes accounts
  through the same `StorageDbContext` and migrations as the site.
- Passwords are PBKDF2-HMAC-SHA256 hashes from `PasswordHasher<LocalUser>`; the plaintext exists only
  at the CLI prompt and in the login request. The policy is a 12-character minimum and a 1024
  maximum, with no composition rules.
- The sign-in is a cookie from `Microsoft.AspNetCore.Authentication.Cookies`, named `a`. It slides
  for `Authentication:TicketLifetimeDays` (30 shipped) of inactivity, and the login form's **Keep me
  signed in** checkbox makes it persistent across browser restarts. It is not the page session,
  which is a different cookie with its own lifetime.
- The cookie carries a random 128-bit key, not the identity: the framework's own data-protected
  ticket lives in the `AuthTickets` table in the same SQLite database, so the request header stays
  small and serialisation stays the framework's. A sign-in always mints a new key; only a sliding
  renewal keeps the one the browser has. Sign-out deletes the row, and an hourly sweep drops tickets
  that expired with no request to remove them.
- Long-lived cookies are revocable: `users passwd` rotates the account's `SecurityStamp`, and
  `OnValidatePrincipal` rejects any cookie whose stamp no longer matches the row. Removing the
  account has the same effect on the next request. Signing in again does **not** rotate the stamp,
  so several browsers or devices can hold a valid session at the same time; only a password change
  or account removal revokes them all.
- Every administration tool requires the `Editor` policy, which is just an authenticated account:
  `/admin/import`, `/admin/export`, `/admin/changes`, `/admin/seeds`, `/admin/scrub`
  and `/admin/status` with its chart. Three routes under the root are public
  because they are the site's own facts rather than its controls: `/admin` itself, because an
  anonymous visitor is entitled to see which tools exist, `/admin/reference`, because it describes
  the build serving the request as `/about` does, and `/admin/copyright`, because the notices the
  image ships are for whoever asks.
  `/login`, `/logout`, `/health` and `/metrics` are the other routes outside the page catch-all, and
  every one of them is a reserved page key. The reserved keys are also what the assistant calls the
  shell files: it lists and reads a record of each one and can never write one, and
  `ShellCatalogue.Reserved` is the list `ReservedPaths.Default` starts from, pinned by a test, so a
  new route adds its record in the same change (`assistant-reads-the-shell-files`).
- Sign-in is throttled by a fixed window of `Authentication:Login:PermitLimit` attempts
  (ten shipped) every `Authentication:Login:WindowSeconds` (sixty shipped), and a request over the
  limit is answered `429` before any password is hashed. The window is per client
  address, taken from the `X-Forwarded-For` Caddy writes, so one address spending its attempts
  cannot deny sign-in to anyone else. The address is folded into one of 1024 buckets, because the
  limiter never releases a partition and the address is otherwise unbounded (`auth-sign-in-is-rate-limited-per-client`).
- Login, logout and every administration POST validate an antiforgery token. A rejected request is
  answered `400` before it reads the form or touches storage. Pages are not endpoints with metadata,
  so `PageEndpoint` checks an authenticated page `POST` itself, before the page runs — which is what
  covers the editor page's save, delete, install and restore.
- The data-protection key ring is persisted on a volume. That is what lets the 30-day cookie and
  in-flight antiforgery tokens survive a redeploy.

### Cookies

Three cookies are the shell's, and each has a one-character name because a browser's cookie pane
stops being self-explanatory:

| Name | What it is |
| --- | --- |
| `a` | The authentication ticket: the key whose row lives in `AuthTickets` |
| `f` | The antiforgery pair's cookie |
| `s` | The per-visitor page session: 128 random bits, one row in `PageSessions` |

A page cannot read or set any of them: `Set-Cookie` is refused by the header allow-list, all three
are `HttpOnly`, and `PageRequestSnapshot` leaves them out of the `Cookies` a page sees. The names
live in one place, `ShellCookies`. `a` is `SameSite=Lax` so a link from elsewhere still arrives
signed in; `f` and `s` are `Strict`. All three follow the same `SecurePolicy`, which is `Always` in
production. The names are short rather than long on purpose — the session cookie's value changed
meaning with the [page session](plans/archive/page-session.md), and this was the cheapest moment
to rename the other two (`shell-the-cookie-names`).

### Page sessions

A page keeps per-visitor state through `ctx.Capabilities.GetRequired<IPageSession>()` (see
[pages](pages.md#page-contract)). The shell owns the identity:

- **Minted lazily.** `PageEndpoint` resolves the visitor's session once per request, before any layer
  runs. A row is created on the page's first write and the cookie is set on the response that wrote
  it, so a page that only reads sets no cookie and costs no row.
- **Bound to the request, not the page.** Every layer of the chain — the page and each layout —
  shares one session id, so two layers cannot see different sessions. A layout and a `HEAD` render
  get a read-only handle.
- **Slid once.** Reading or writing slides the idle timer with one `UPDATE` per request, never one
  per key, and a `HEAD` render does not slide it. An anonymous session lives 30 minutes, a
  signed-in one 8 hours.
- **Rotated on sign-in.** A session row carries the account it belongs to. Signing in deletes the
  row the browser presented and mints one for the account, so a stale cookie cannot inherit what an
  anonymous visitor or a previous account left behind. Signing out deletes the row and clears the
  cookie. Neither writes a flash message: a message written after a sign-out has nowhere to live.
- **Swept on a timer.** `PageSessionCleanupService` removes expired rows every five minutes, and the
  mint path deletes expired rows before it evicts anything live, so a burst of new visitors cannot
  push a live session out because stale ones are holding the room.
- **Capped.** `Pages:Session` bounds the values (see [storage](storage.md#page-sessions)). A live
  session cap evicts the most idle **anonymous** session first and only touches a signed-in one when
  no anonymous session is left, so a flood sheds strangers rather than the person using the site.

The shell keeps no flash message of its own. The editor page's notices are written into the render
that produced them: it answers a save in place, so a message that a redirect would have to carry is
not needed. `/admin/seeds` and `/admin/scrub` answer with their own documents for the same reason.
- Every response carries one set of security headers, written by `SecurityHeaders` when the response
  starts (`shell-security-headers-are-one-set`). The assistant and admin views widen the policy to `script-src 'self'` for their
  own modules, and a stored page asks for the same policy with `ctx.Response.AllowScripts()` — which is how
  the editor page loads `/editor.js` (`shell-security-headers-are-one-set`).
  A stored page's starting policy comes from its extension row's `ScriptPolicy`, before the page
  runs; a page with code may widen `none` with `AllowScripts`, while a no-code page has no code to
  do so and depends on the configured row. The header stays shell-owned, so a page cannot set
  `Content-Security-Policy` with `SetHeader`, and inline scripts stay blocked on every path.
  `/admin.js`, `/editor.js` and `/assistant.js` are seeded pages like `/site.css`, so they stay
  editable; each is a native ES module loaded with `<script type="module">`.

### A run's view

The assistant's `view` tool is the one caller that reads the site over HTTP as somebody. The shell
gives it a second authentication scheme, `View`, and a token that is not a sign-in:

- **Minted per run, in memory.** `ViewTokenStore` holds one 256-bit secret per executing run, at most
  `Assistant:MaxActiveRuns` of them, and nothing else anywhere: no table, no file, no key ring, no
  configuration. `RunProcessor` mints it when the run starts and forgets it in the run's `finally`, so
  a finish, a failure, a cancel and an unexpected exception all revoke it. A restart ends every run
  and therefore every token.
- **Presented as `Authorization: Bearer`.** The default scheme is a policy scheme that forwards a
  request carrying that header to `View` and everything else to the cookie, and the cookie stays the
  challenge scheme, so a browser's sign-in redirect is unchanged. The handler compares the secret in
  fixed time, then validates the account the way the cookie does on every request — it exists, and its
  `SecurityStamp` has not moved (`auth-sign-in-is-long-lived-and-revocable`). The principal is the
  run's owner, with `uk.osric.asp.view` set to the run id.
- **Two refusals, in one middleware.** `ViewGuard` runs after the identity is known and before
  anything is authorised: a bearer header that did not authenticate is a `401` with
  `WWW-Authenticate: Bearer`, never a silent anonymous view; and a view may use `GET` and `HEAD` only,
  refused `405` with `Allow: GET, HEAD` before authorisation and long before the antiforgery check.
  It sits in one place because the page endpoint covers pages only — the editor, the admin tools, the
  assistant and the memory page are all shell routes with their own `POST`s.
- **Only one origin.** `Site:Origin` is where the host reaches the site it serves;
  `ViewCredential` offers the token to that scheme, host and port and to nothing else, hop by hop, so
  a redirect off the origin drops it (`pages-outbound-fetch`). A page cannot name the token: it is not
  in `Pages:Fetch:Credentials` and has no configuration key.
- **The origin has to be the visitor's.** A view of a page that mints an antiforgery token — the
  editor is the one that does — needs an HTTPS request, because the antiforgery cookie is
  `Secure`-always in production. The shipped `Site:Origin` is the container's own loopback, which a
  development host can use; the deployment names the public origin, and a production host left on
  plaintext logs one warning at startup rather than failing a run later.

### Page authentication

The sign-in the editor page uses is available to any page, as ordinary page code rather than a
directive, a metadata column or a route policy. A page is public unless it guards itself:

```html
<% if (ctx.User is not { } user) { ctx.Response.RedirectToLogin(); return; } %>
```

- `ctx.User` is a `PageUser?` carrying only the account name, mapped by `PageEndpoint` from the
  principal `UseAuthentication` has already validated. An authenticated identity without a
  `NameIdentifier` maps to `null`, so a half-built principal fails closed. A page cannot forge the
  identity with a header, a cookie or a form field: it exists only as the shell's value.
- `ctx.Response.RedirectToLogin()` sends the visitor to `/login?returnUrl=` followed by this page's canonical
  key and **raw** query, built by `LoginRedirect` rather than from the collapsed query dictionary, so
  `/search?q=a&q=b` returns with both values. The status is `302` for `GET`/`HEAD` and `303` for
  `POST`, so a submitted form is not replayed. `LocalRedirect` still decides where a `returnUrl` may
  point, so this cannot become an open redirect. A fragment cannot be preserved: the browser never
  sends one.
- The endpoint validates `IsRequestValidAsync` for an authenticated `POST` before the page runs, and
  answers `400` with `no-store` when the pair is missing or stale. This is deliberately not opt-in:
  the sign-in cookie is exactly what turns a cross-site post into an action by the owner. An
  anonymous `POST` is unchanged, because it has no cookie-backed identity to forge.
- `ctx.Response.AntiforgeryField` is the hidden input the page writes with `<%==`, built by the same
  `AntiforgeryField.Hidden` the shell's own forms use. It is generated once per render under the
  body lock, and returns an empty string after the render is abandoned, which fails closed.

A page's guard is not a security boundary against its author: page code still runs in-process with
the site's identity (`pages-there-is-no-sandbox`). Authentication decides who may reach the guard, not what a page may do.

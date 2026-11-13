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
  `SiteNavigation`, and the seeded layout writes the same items for stored pages (DEC-151, DEC-154).
- `/site.css` is a seeded page and the only base sheet (DEC-217). It holds the document's shape —
  the visually hidden utility, the listing and the form grid — and the site's look, with the shape
  rules first. The view's own sheet is linked last, so it can override a structural rule without a
  more specific selector. The owner restores a stale seeded sheet from the page list.
- `/status` writes body content only — its heading, window nav, banner and figures — and its document
  is the builder's, like every other view's. A chart it draws can also be requested on its own at
  `/status/chart`, which is a drawing rather than a document and so does not go through this builder.
- `PageLayout` keeps the fragments a shell view writes: `Gutter` and `Diagnostics` (a `ul` with `role="list"`,
  DEC-108). Everything a document shares — the head with its base sheets and icon, the banner with
  the skip link, `Nav`, `OpenMain`/`CloseMain` and the close — is `Sdk/PageDocument.cs`, so the
  shell, a seeded layout and a page all write one document shape (DEC-156). `Html/` keeps
  `DiagnosticLine`, `AntiforgeryField`, `SiteNavigation` and `PageUrls` beside it.
- A stored page with code is wrapped in a layout by default, so it writes a fragment rather than a
  document; the layout writes the head, the banner and the landmark through the same builder. See
  "Layouts".

Every one of these views is rendered once by `ShellViews` and checked against the shared accessibility
rules — see [testing](testing.md#the-five-suites) — with what a string cannot
prove left to a recorded hand pass (DEC-110).

## Layouts

A page with code is a fragment. The host wraps every `text/html` page with code in the layout named
by `Pages:Layout` (`/_layout` as seeded), unless the page clears `IPageLayout.Layout`. A page with no
code is a complete representation and is never wrapped: its stored source is the response, whether
the key is HTML or not. A redirect, a `HEAD`'s empty body, a non-HTML content type, a no-code page
and the layout itself are never wrapped (DEC-149). A code page that wants its own document clears
`IPageLayout.Layout` itself.

## The asset response

There are two response shapes. The ordinary one is buffered, wrapped where the rules say, and
written by `WritePageAsync` with `Cache-Control: no-store` and no validator. The second exists only
for an asset: a revision with `TemplateHasCode = false`. It is never rendered, loaded or compiled;
the stored source is the response, and `PageEndpoint.WriteSourceAsync` writes it on its own path with
the extension's content type, `Content-Length`, the shell's security headers and no session cookie.

An unversioned asset is still `no-store`. A versioned asset — `?v=` naming a retained revision — is
the one response that may promise `Cache-Control: public, max-age=31536000, immutable` and a strong
`ETag`; `If-None-Match` answers `304` with the same cache headers. Only `GET` and `HEAD` are answered
on a versioned URL. The versioned response is separate from `WritePageAsync` so that function's
unconditional `no-store` and `ETag` removal cannot reach it. [Pages](pages.md#assets-and-versioned-urls)
has the selection rules and `docs/metrics.md` the `page.asset.requests` outcome counter.

The chain renders inside out, content first, because only then does the host know what the content
produced:

```
render /about   → buffer 1   (sets Title, AddHead, AddStylesheet)
render /_layout → buffer 2   (Body = buffer 1)
write buffer 2
```

- **One budget for the chain.** `PageRenderer.OpenSessionAsync` takes one render slot and one
  deadline; each layer renders under them with its own key on the metric. The fetch allowance, the
  body allowance and the antiforgery pair are shared the same way, so a layout cannot spend them
  twice (DEC-150).
- **The content owns the response.** Status, content type, headers and the redirect come from the
  layer the visitor asked for; a layout that tries to set one is ignored and logged. `AllowScripts`
  is the union, because the layout's script is part of its chrome (DEC-158).
- **A wrapped page must set a title.** Reading `Title` before it is set throws, and the host refuses
  the chain before the layout starts, so the error names the page that forgot. A layout may read the
  title so far and compose its own; the outermost layer is the one written (DEC-157).
- **A page controls its own sheets.** `AddStylesheet` once per link, from none to many. The order on
  the wire is the document's base sheets, then each layout's from outermost in, then the page's in
  the order it asked; an exact duplicate is linked once and an href that is not site-relative is
  refused rather than blocked silently by `style-src 'self'` (DEC-155).
- **Depth and cycles.** `Pages:Render:MaxLayoutDepth` (four by default, clamped 1–16) bounds the
  chain and a seen-set stops at the first repeat — which is why the seeded `/_layout`, pre-filled
  with the default like every layer, needs no special case.
- **Failure is not symmetric.** A *default* layout that is missing or does not compile serves the
  page unwrapped, logs a warning and counts `page.layout.fallback`; a layout the page *named* that
  cannot be served is an error naming the key.
- **The menu is written twice, on purpose.** `SiteNavigation` composes it from the principal for the
  shell's own views; the seeded `/_layout` writes it from `ctx.User` for stored pages. The shell's
  document must not depend on a stored page, because the error, health and sign-in routes have to
  answer when the page pipeline is broken (DEC-151). Both go through `PageDocument.Nav`. The
  editor is a stored page, so its menu is written by the page rather than by `SiteNavigation`.

The layout is not a guard: the content has already run. It is not a data boundary: it reads its own
storage and nothing of the page's.

The author-facing version of this section is the seeded `/help` page, which is itself a page the
owner can edit and restore. It is linked from the editor page; a change to what a page author can
do updates it in the same change.

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
named a page the site serves — the key is addressable and not one of `Pages:ReservedPaths` — and never
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
What keeps it trustworthy is not a route but the capability: `IPageEditor` is registered for that
key alone, so a page stored anywhere else cannot read, write, delete or restore another page, and
the shell still owns the things a page must not: routing, the session, the recovery routes and the
typed confirmation a scrub needs. Pages stay public unless their own code checks `ctx.User` (see
"Page authentication" below).

A seeded page is installed from its row in the editor's listing: the action opens a confirmation
that shows the diff against the seed, and its button saves the bundled source through the ordinary
revision path, so a page comes back without discarding its earlier revisions. A seed is *installed* and a saved revision is *restored*:
`?edit=<key>&cmd=install` on the seed, `?rev=<hash>&cmd=restore` from the page's history, both confirmed the same way
and both refusing the change that would leave the live source as it is. The page's history offers a
restore link on every revision except the current one; the editor says when a key was deleted and
that saving brings it back. A deleted page's history is scrubbed from `/admin/scrub/<key>`, which is
the shell's route because it is irreversible: the owner types the page's key to confirm, and a live
page has to be deleted before it can be scrubbed.

- Accounts live in the `Users` table in the same SQLite database. There is no registration endpoint
  and no account UI: the `users` command in the Web app creates, lists, resets and removes accounts
  through the same `StorageDbContext` and migrations as the site.
- Passwords are PBKDF2-HMAC-SHA256 hashes from `PasswordHasher<LocalUser>`; the plaintext exists only
  at the CLI prompt and in the login request. The policy is a 12-character minimum and a 1024
  maximum, with no composition rules.
- The sign-in is a cookie from `Microsoft.AspNetCore.Authentication.Cookies`, named `a`. It slides
  for 30 days of inactivity, and the login form's **Keep me signed in** checkbox makes it persistent
  across browser restarts. It is not the page session, which is a different cookie with its own
  lifetime.
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
- `/admin/**` and `/status` require the `Editor` policy, which is just an authenticated account.
  `/login`, `/logout`, `/heath` and `/metrics` are the other routes outside the page catch-all, and
  every one of them is a reserved page key. The reserved keys are also what the assistant calls the
  shell files: it lists and reads a record of each one and can never write one, and `ShellRoutes`
  is pinned to `ReservedPaths.Default` by a test, so a new route adds its record in the same change
  (DEC-198).
- Sign-in is throttled by a fixed window of ten attempts a minute (`LoginRateLimitOptions`), and a
  request over the limit is answered `429` before any password is hashed. The window is per client
  address, taken from the `X-Forwarded-For` Caddy writes, so one address spending its attempts
  cannot deny sign-in to anyone else. The address is folded into one of 1024 buckets, because the
  limiter never releases a partition and the address is otherwise unbounded (DEC-118).
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
to rename the other two (DEC-237).

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
not needed. `/admin/restore` and `/admin/scrub` answer with their own documents for the same reason.
- Every response carries one set of security headers, written by `SecurityHeaders` when the response
  starts (DEC-93). The assistant and admin views widen the policy to `script-src 'self'` for their
  own modules, and a stored page asks for the same policy with `ctx.AllowScripts()` — which is how
  the editor page loads `/editor.js` (DEC-138).
  A stored page's starting policy comes from its extension row's `ScriptPolicy`, before the page
  runs; a page with code may widen `none` with `AllowScripts`, while a no-code page has no code to
  do so and depends on the configured row. The header stays shell-owned, so a page cannot set
  `Content-Security-Policy` with `SetHeader`, and inline scripts stay blocked on every path.
  `/admin.js`, `/editor.js` and `/assistant.js` are seeded pages like `/site.css`, so they stay
  editable; each is a native ES module loaded with `<script type="module">`.

### Page authentication

The sign-in the editor page uses is available to any page, as ordinary page code rather than a
directive, a metadata column or a route policy. A page is public unless it guards itself:

```html
<% if (ctx.User is not { } user) { ctx.RedirectToLogin(); return; } %>
```

- `ctx.User` is a `PageUser?` carrying only the account name, mapped by `PageEndpoint` from the
  principal `UseAuthentication` has already validated. An authenticated identity without a
  `NameIdentifier` maps to `null`, so a half-built principal fails closed. A page cannot forge the
  identity with a header, a cookie or a form field: it exists only as the shell's value.
- `ctx.RedirectToLogin()` sends the visitor to `/login?returnUrl=` followed by this page's canonical
  key and **raw** query, built by `LoginRedirect` rather than from the collapsed query dictionary, so
  `/search?q=a&q=b` returns with both values. The status is `302` for `GET`/`HEAD` and `303` for
  `POST`, so a submitted form is not replayed. `LocalRedirect` still decides where a `returnUrl` may
  point, so this cannot become an open redirect. A fragment cannot be preserved: the browser never
  sends one.
- The endpoint validates `IsRequestValidAsync` for an authenticated `POST` before the page runs, and
  answers `400` with `no-store` when the pair is missing or stale. This is deliberately not opt-in:
  the sign-in cookie is exactly what turns a cross-site post into an action by the owner. An
  anonymous `POST` is unchanged, because it has no cookie-backed identity to forge.
- `ctx.AntiforgeryField` is the hidden input the page writes with `<%==`, built by the same
  `AntiforgeryField.Hidden` the shell's own forms use. It is generated once per render under the
  body lock, and returns an empty string after the render is abandoned, which fails closed.

A page's guard is not a security boundary against its author: page code still runs in-process with
the site's identity (DEC-40). Authentication decides who may reach the guard, not what a page may do.

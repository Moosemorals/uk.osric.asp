<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Site navigation and a default layout

Status: **implemented**. Written 2026-11-08; revised after review, which chose a
default layout delivered as a capability rather than more `IContext`, a page that controls its own
stylesheets from none to many, a title a wrapped page must set, and a document builder that owns the
document's policy. This plan therefore absorbs IDEA-005 phase 2 instead of following it. It was a
design and an implementation plan; the phases below are what was built.

## The short version

- **IDEA-005 phase 2 lands first, as a feature.** `ctx.Features.GetRequired<IPageLayout>()` wraps a
  page in another page: `Layout` chooses it (the host pre-fills the configured default), `Title` and
  `AddHead` describe the document, `Body` is what the layout wraps. **`IContext` gains nothing.**
- **Every HTML page is wrapped by default.** `Pages:Layout` names the layout, seeded as `/_layout`;
  a page opts out by clearing `Layout`, and a non-HTML response or a redirect is never wrapped.
- **One stored layout holds the chrome**: head, skip link, `<header>` with the site menu, and the
  body. The menu is written once, in a page the owner can edit, and every page gets it without
  asking.
- **A page controls its own sheets — none, one, or many.** `AddStylesheet("/blog.css")` once per
  link, in the order the page adds them, after the site's two and after any layout's, so a page is
  still the last word on how it looks.
- **The items are:** `/` always; `/login` when anonymous; `/assistant` (when enabled), `/edit`,
  `/admin`, `/logout` when signed in. The layout writes that branch from `ctx.User`; the shell
  composes its own from the principal it validated.
- **The shell keeps its own document.** `ShellDocument` writes head, skip link, banner and `main`
  for the views the host serves, through the same `PageDocument` builder — the error, health,
  sign-in and editor routes must not depend on the page pipeline.
- **`/admin` becomes a fragment page** — a heading and three links, one of them `/status`, which
  nothing links to today — and it gets the menu for free.
- **The builder comes first.** IDEA-018's `PageDocument`, `Nav` and `NavLink` are what the layout,
  the shell and the page all write through.

## Goals

- A visitor reaches the site's own tools from every page, without a history stack, and a page gets
  its chrome without writing it.
- One composition of the shell's menu, one stored composition for pages, and one renderer for both.
- The layout is a page: the owner edits the chrome without rebuilding or redeploying.
- A page decides its own look, from no extra sheet to several, without owning the `<head>`.
- Nothing about the page contract grows for chrome, and nothing a view cannot do moves into a view.
- Accessibility is asserted: named landmark, current item as text, skip link first and pointing at
  `#main`.

## Non-goals

- **Wrapping non-HTML responses or redirects.** A page that answers a stylesheet, a script, an image,
  a `302` or a non-HTML content type (DEC-144) is never wrapped.
- **Wrapping the shell's own views in the stored layout.** Recovery, error, health, sign-in and the
  editor must answer when the page pipeline is broken (IDEA-017's bootstrap trap, DEC-131).
- **A content menu.** The menu lists the site's *tools*, not its pages; a "pages by title" menu
  needs the store read of [IDEA-015](../../jobs/README.md#closed).
- **A nav editor.** The items are written where the document is written: the shell's composition for
  shell views, the stored layout for pages.
- **Signing out with a `GET` that changes state.** The menu links to a confirmation view; the action
  stays a `POST` with an antiforgery pair.
- **A per-page layout directory.** One default, a page may name another, and a layout may name a
  parent. That is the whole routing model.

## What exists today

Checked against the code on 2026-11-08.

- **Nothing wraps a page.** `PageEndpoint` builds one `SimpleContext` over a `StringWriter`, calls
  `PageRenderer.RenderAsync` once and writes the buffer. A page owns its whole response
  ([PageEndpoint.cs](../../../src/uk.osric.asp.Web/Pages/PageEndpoint.cs), DEC-31).
- **The contract has no document.** `IContext` carries `Request`, `Path`, `User`, `Aborted`, the
  logging and write members, status/content type/header/`AllowScripts`/`Redirect`/
  `RedirectToLogin`/`AntiforgeryField`, and `Features` — no title, no head, no body, no layout
  ([IContext.cs](../../../src/uk.osric.asp.Sdk/IContext.cs)).
- **Capabilities are a settled seam.** `IPageFeatures` with `TryGet`/`Get`/`GetRequired`; the host
  registers, a capability is present or absent, never null; storage and fetch are the two today
  (DEC-142, DEC-143).
- **The response value** is status, content type, headers, location and `AllowsScripts`
  ([PageResponse.cs](../../../src/uk.osric.asp.Sdk/PageResponse.cs)).
- **A render takes a slot and a deadline per call.** `PageRenderer.RenderAsync` waits on
  `PageBudget.RenderSlots`, creates the request's deadline, renders and releases — so two layers
  would take two slots and two deadlines unless the call is restructured
  ([PageRenderer.cs](../../../src/uk.osric.asp.Web/Pages/PageRenderer.cs)).
- **The body is capped in the writer.** `SimpleContext` refuses a write past
  `RenderOptions.MaxBodyChars` (4 MiB by default), per context.
- **The document shell** writes the head, the sheet order and `<main id="main">`, and takes at most
  **one** sheet of the view's own, so a view that wants two has nowhere to say so; `main` is the
  body's only element, so there is no skip link
  ([ShellDocument.cs](../../../src/uk.osric.asp.Web/Html/ShellDocument.cs), DEC-107, DEC-116, DEC-121).
- **Seven named local navs** exist for the shell's own views; nothing is shared.
- **Two pages nobody can navigate to.** Nothing links to `/` except an error remedy, and nothing
  links to `/status`. The dev database holds one page that writes a document (`/`), one deliberately
  broken page (`/borked`) and five asset pages (`/site.css`, `/editor.css`, `/editor.js`,
  `/status.css`, `/rocket.svg`); the assets are non-HTML and never wrapped, and the one page that
  writes a document is the owner's to migrate.
- **`/admin` has no view.** It is reserved as a prefix; only the import and export routes answer
  beneath it. The only sign-out control is the form at the bottom of the editor.
- **The accessibility suite** renders every shell view once and applies eleven rules (DEC-110).

## What this touches

| Job or decision | What it already says | What this plan does |
| --- | --- | --- |
| [IDEA-005](../../jobs/IDEA-005.md) | Phase 2 is a layout *page* rendered around a page; `SetLayout`, `WriteBody`, title/head slots; depth limit and seen-set; one budget for the chain; a layout is presentation, not a guard | **Implements phase 2**, with the layout's slots on a capability instead of `IContext`, and the default on. Shared *types* stay deferred |
| [IDEA-018](../../jobs/README.md) | The document builder and its fragments should move to `Sdk` | **Implements it** in phase 1, scoped to `PageDocument`, `Nav` and `NavLink` (see "Scope") |
| [IDEA-015](../../jobs/README.md#closed) | The listing, history and revision views need a layout to leave the shell | Unblocks it: those views become fragments the default layout wraps. The store read is still its own job |
| [IDEA-017](../../jobs/IDEA-017.md) | The editor is the recovery path; do not let it depend on page code | Preserved: shell views never use the stored layout, so a broken layout cannot lock the owner out |
| [IDEA-001](../../jobs/IDEA-001.md) | The editor is mostly markup | The editor loses its ad-hoc sign-out form; its page menu stays |
| [IDEA-022](../../jobs/IDEA-022.md) | `SimpleContext` does five jobs | Gains a constructor argument (the layout state), not a contract member |
| [BUG-18](../../jobs/BUG-18.md) | Every failure is a document a person can read | Error documents gain the menu above the recovery nav |
| DEC-121 | No skip link until chrome precedes `main` | Superseded: the chrome arrives |
| DEC-107, DEC-116 | One writer owns the head and the landmark; sheet order is fixed | Extended to the banner and the skip link, for both the shell and the layout, and to layers: a page's sheets are last |
| DEC-174 | Seed restore is a per-page action on `/edit`; `/admin` is not a page surface | `/admin` returns as an *index of tools*, so the objection that retired it (a duplicate page list) does not apply |
| DEC-02, DEC-132, DEC-133, DEC-135, DEC-136 | Pages are public unless they guard themselves; identity is shell-supplied; authenticated POSTs need a field; authenticated responses are `no-store` | Unchanged: the layout is not a guard, and the menu's links are content, not authority |
| DEC-26 | `RuntimeId` includes the Sdk module version id | One deliberate bump from the builder and the capability; every stored image recompiles on its next request |
| DEC-142, DEC-143 | Capabilities are for what a render may or may not offer; always-present things stay on `IContext` | Extended: the layout is a capability because it is a set of *slots a render offers*, which is what the page-features review already expected |
| DEC-144 | A page's content type comes from its key's extension | Becomes the rule that decides whether a page is wrapped |
| DEC-31, DEC-123 | The body is buffered; the render owns the deadline | The chain renders inside the same budget rather than beside it |
| DEC-93, DEC-138 | CSP is shell-owned; a page opts into `script-src 'self'` | Unchanged, and `AllowScripts` is the union across layers |

## Design

### D1 — The builder is the seam (IDEA-018)

`Sdk/PageDocument.cs` returns markup: the head and open body, `Banner`, `OpenMain`/`CloseMain`, the
close, and `Nav`; `Sdk/NavLink.cs` moves from `Web/Html`. `ShellDocument` and `PageLayout.Nav`
become adapters over it, so the shell and the stored layout cannot drift about encoding, the
landmark, the sheet order or the skip link (DEC-107).

**The builder owns the document's policy.** That is the whole point of it being the one writer:

- `Head` writes the language, the charset, the viewport, the title, the base sheets
  (`/layout.css` then `/site.css`, DEC-116), the sheets it is given in order, the icon, then any
  extra head markup. It refuses a blank title, so a document without one cannot be written.
- `Banner(navMarkup)` writes the skip link and the `<header>` around a nav, so "the skip link is the
  first focusable element and points at `#main`" is a property of the builder rather than a habit of
  two callers.
- `OpenMain` writes the one `id="main"` landmark, and `CloseMain`/`End` close what was opened.

**The line is document versus response.** The builder decides what the document contains and in what
order; it never decides what the *response* is. Status, content type, security headers (including
CSP and `AllowScripts`), caching and the body cap stay with `ShellDocument`, `SimpleContext`,
`SecurityHeaders` and `PageEndpoint`. A builder that could weaken a header would undo more than it
fixed (IDEA-018 point 7), and that worry is about the response, not the markup.

**Scope.** In: `PageDocument`, `Nav`, `NavLink`. Out: `Gutter` and `Diagnostics` — they are the
editor's, and their value wraps `PageDiagnostic`, a `Core.Compilation` type the `Sdk` may not
reference (DEC-88); moving them needs an Sdk diagnostics value and severity, a decision of its own.

### D2 — The layout is a capability, not more `IContext`

```csharp
public interface IPageLayout {
    string? Layout { get; set; }                    // key of the page that wraps this one; null serves it alone
    string Title { get; set; }                      // required when wrapped; reading it unset throws
    string Head { get; }                            // every layer's AddHead, outermost first
    IReadOnlyList<string> Stylesheets { get; }      // every layer's AddStylesheet, in cascade order
    void AddHead(string markup);
    void AddStylesheet(string href);                // none, one, or many; called once per link
    string Body { get; }                            // what the layout wraps; empty in the content layer
}
```

- **Reached through `Features`**, so `IContext` keeps exactly what it has and a page that never wants
  a layout never meets one. The parallel is `ctx.Features.GetRequired<IPageStorage>()`.
- **`Layout` is pre-filled by the host** with `Pages:Layout`, so the default needs no page code and
  opting out is one statement. A page may name a different stored page.
- **`Title` is not optional.** It starts unset, reading it unset throws, and the host refuses the
  chain before it reaches the layout, so the error names the page that forgot rather than the layout
  that noticed. A page that clears `Layout` is exempt, because it writes its own `<head>` (D4).
- **`Body` is a value, not a writer.** The page writes raw markup (`<%== layout.Body %>`), which is
  the idiom `ctx.AntiforgeryField` already sets, and it keeps the capability from writing to the
  response behind the context's back. A page that reads `Body` in the content layer gets the empty
  string; the layout is the only layer that has one.
- **`Head` is read and `AddHead` is written**, so the outermost layout writes every layer's head
  once, and a page can add a `<meta>` without owning the `<head>`.
- **`AddStylesheet` is the CSS link, typed.** Calling it zero, one or many times decides how many
  `<link rel="stylesheet">` the document carries; the href is escaped by the builder and the order is
  the order of the calls. It is separate from `AddHead` because three things need to be true that a
  raw string cannot promise: the page's sheets come **after** the site's and any layout's, so a page
  is still the last word (DEC-116); the same href is never linked twice; and a path that is not a
  site-relative URL — the shape `LocalRedirect.IsSafe` already accepts, query and fragment included —
  is refused rather than silently blocked by `style-src 'self'` (DEC-93).
- **`Stylesheets` is what the layout writes**, already merged and ordered by the host, so the layout
  never has to know which layer asked for what.
- **It is not a guard.** The content has already run by the time the layout starts, so a layout
  hides chrome; it cannot prevent a side effect (IDEA-005 point 6, DEC-02).

### D3 — Default, opt-out, and what is never wrapped

The content runs first, so the host knows what it produced when it decides:

1. **Never wrapped:** a redirect; a content type that is not `text/html`; a `HEAD` render still runs
   the chain for its representation and validators but writes nothing; the layout itself, which the
   seen-set reaches at once.
2. **Wrapped by default:** every other page, because `Layout` already names `Pages:Layout`.
3. **Opted out:** the page clears `Layout`. That is the escape hatch for a page that is already a
   whole document, and the one the migration below uses.
4. **A named layout must be a stored page and must not be a reserved key**; anything else is an
   error naming the key rather than a page served as an orphan fragment.

### D4 — The chain: one request, one budget, several renders

Content first, then its layout, then the layout's parent:

```
render /about        → buffer 1  (sets Title, AddHead, AllowScripts)
render /_layout      → buffer 2  (Body = buffer 1)
write buffer 2 with the merged head and title
```

- **One slot, one deadline, one fetch budget, one body budget, one antiforgery token** for the whole
  chain. `PageRenderer` gains a session that takes the slot and the deadline once and renders layers
  under it, so a two-layer page costs one slot rather than two. Each layer still gets its own
  `PageRenderer` pass for compiling, metrics and abandonment, with its own path on the metric.
- **The content owns the response.** Status, content type, location and headers come from the layer
  the visitor asked for; a layout's attempt to set them is ignored and logged, because a layout is
  markup and nothing else. `AllowsScripts` is the union, because the layout may load the sheet's
  script.
- **The title is the outermost layer's, and only the outermost layer's.** Each layer's state is
  seeded with the title so far, so the content sets the subject (`Title = "Assistant"`) and a layout
  that wants the site's name reads that value, composes
  (`Title = $"moosemorals.com — {layout.Title}"`) and renders last, so its value is the one written.
  Additive titles need no contract under this rule — they are one string interpolation in the
  layout — and a contract-level version (a separator setting, or a list of segments) is deferred
  until a layout actually wants something the interpolation cannot express.
- **The document is assembled from the outside in.** The head markup and the stylesheets are
  collected per layer and written outermost first, which is the reverse of the order the layers
  rendered — the content runs first but sits last in the document. Within one layer, calls keep
  their order. So the sheets on the wire are always `/layout.css`, `/site.css` (DEC-116), then any
  sheet a layout added (outermost layout first), then the page's own, in the order the page asked
  for them. A page with none adds none, and the site's two are still there.
- **Each layer has its own `ctx.Path`** — and therefore its own storage handle — but the same
  `ctx.Request`; `ctx.Request.Path` is the page the visitor asked for, which is what the menu marks
  as current.
- **Depth and cycles.** `Pages:Render:MaxLayoutDepth` (four by default, clamped to 1–16) and a
  seen-set; a cycle stops at the first repeat. Seed `/_layout` therefore needs no special case: the
  default points at it and the seen-set stops it.
- **Failure is not symmetric, on purpose.** A *default* layout that is missing or does not compile
  serves the content unwrapped, logs a warning and records a fallback metric — deleting or breaking
  `/_layout` degrades the site to today's behaviour instead of taking it down. A layout the *page*
  named that is missing or does not compile is an error naming the key, because that was a
  deliberate choice and silence would be worse.
- **The body cap counts the chain.** The budget is shared by the layers rather than handed to each,
  so a layout cannot spend another 4 MiB of chrome.

### D5 — The menu lives in the layout, and in the shell through the same builder

`Pages/Seeds/_layout` writes the skip link, the `<header>` with `<nav aria-label="Site">`, opens
`main`, writes `layout.Body`, and closes. The items are ordinary page code:

```html
<% var layout = ctx.Features.GetRequired<IPageLayout>();
   var links = new List<NavLink> { new("/", "Home") };
   if (ctx.User is null) {
       links.Add(new("/login", "Sign in"));
   } else {
       links.Add(new("/assistant", "Assistant"));
       links.Add(new("/edit", "Pages"));
       links.Add(new("/admin", "Admin"));
       links.Add(new("/logout", "Sign out"));
   } %>
<%== PageDocument.Head(layout.Title, layout.Stylesheets, head: layout.Head) %>
<%== PageDocument.Banner(PageDocument.Nav("Site", links)) %>
<%== PageDocument.OpenMain() %>
<%== layout.Body %>
<%== PageDocument.CloseMain() %>
<%== PageDocument.End() %>
```

The layout writes no markup of its own shape: the head, the skip link, the banner, the landmark and
the close all come from the builder, so a seeded layout and a shell view cannot disagree about them.

- **Current is the requested path.** `ctx.Path` inside a layer is the layer's key, so the menu
  compares `ctx.Request.Path` — the same fact the shell's composition uses.
- **The shell composes its own**, in `Web/Html/SiteNavigation.cs`, from the principal
  `UseAuthentication` validated and the routes this installation answers: it omits `/assistant` when
  the assistant is off, and builds `returnUrl` from the canonical path and raw query. The layout can
  know neither fact — and `ctx.Request.Query` is a dictionary, so the raw query it would need is not
  there — so it writes `/assistant` unconditionally and links `/login` plainly. When the assistant
  is off, that link lands on the shell's "The assistant is not available" document, which carries
  the menu; signing in from a page lands on `/edit`, which is `LocalRedirect`'s default. An owner who
  wants the page back adds `returnUrl` from `ctx.Request.Path`.
- **Two copies of the list, one renderer.** The shell's composition and the layout's markup can
  drift; both go through `PageDocument.Nav`, the layout is one file, and the prompt teaches the same
  bytes. That is the price of the contract not growing.

### D6 — `/admin` is a fragment page now

It is a heading and three links (`/admin/import`, `/admin/export`, `/status`), so it is a page, and
with the default layout it writes no head and no chrome at all:

```html
<% if (ctx.User is null) { ctx.RedirectToLogin(); return; } %>
<h1>Administration</h1>
<ul>…</ul>
```

- `ReservedPaths.Default` and `Pages:ReservedPaths` lose the bare `/admin` and gain `/admin/import`
  and `/admin/export`.
- `/status` stays out of the top menu and is offered here: it reports on the installation, while
  every top-level item is somewhere to work.
- `AdminNavigation` gains the index, so the three admin views reach each other.

### D7 — Sign-out is a link to a confirmation view

`/logout` stays `POST`-only with an antiforgery pair. The menu links to a new `GET /logout` that
asks "Sign out?" and renders the form. A `GET` that changes state can be taken by a prefetcher, and
a `POST` form in the menu would put an antiforgery field in every document — including public pages
a shared cache may store. An anonymous `GET /logout` is a `303` to `/`. The editor's inline form
goes.

### D8 — What the layout is not

- **Not a guard.** Authorization stays in the page (DEC-02, DEC-132).
- **Not a data boundary.** A layout reads its own storage and nothing of the page's (DEC-125).
- **Not the recovery path.** If the compiler or the page store is broken, `ShellDocument` still
  answers the error, health, sign-in and editor routes.

## Considered and rejected

**`SetLayout`, `WriteBody`, `Title` and `AddHead` on `IContext`** (IDEA-005's original sketch).
Rejected by the review: four new members for chrome, when the capabilities seam already exists and
`IContext` is the contract every stored page compiles against.

**A rendered menu string (`ctx.SiteNav`).** Rejected: it grows the contract and takes the menu out
of the owner's hands.

**The host rewriting a page's HTML to insert the chrome.** Rejected: it edits bytes a page produced,
breaks full documents and non-HTML pages, and invalidates the `ETag` and body-cap contracts.

**An opt-in layout with no default.** The original shape of this plan. Rejected by the review: the
point of a layout is that a page does not have to ask, and the menu rides on it.

**Wrapping the shell's views in the stored layout.** Rejected: the error page, the editor and
sign-in must answer when the layout does not compile, and a stored page cannot be the recovery path
for stored pages.

**Failing loudly when the *default* layout is missing.** Rejected: one deleted page would take every
page down, and the owner can delete it from the listing. Degrade, log, and measure instead.

**A `POST` form inside the menu for sign-out.** Rejected: an antiforgery field in every document,
and a cached public page must not carry one visitor's token.

## Components

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Sdk/PageDocument.cs` | **new** — head and open body, `Banner`, `OpenMain`/`CloseMain`, the close, `Nav`, and the document's own policy: base sheets, icon, skip link, landmark |
| `src/uk.osric.asp.Sdk/NavLink.cs` | **new** — moved from `Web/Html` unchanged |
| `src/uk.osric.asp.Sdk/IPageLayout.cs` | **new** — `Layout`, `Title`, `Head`, `Stylesheets`, `AddHead`, `AddStylesheet`, `Body`, reached through `Features` |
| `src/uk.osric.asp.Sdk/IContext.cs` | **unchanged** |
| `src/uk.osric.asp.Sdk/PageResponse.cs` | **unchanged** — the content layer's response is the response |
| `src/uk.osric.asp.Web/Html/PageLayout.cs`, `Html/NavLink.cs` | **changed / deleted** — the fragments become adapters over `Sdk` |
| `src/uk.osric.asp.Web/Html/ShellDocument.cs` | **changed** — writes the skip link and banner through the builder; a required navigation argument and a list of the view's sheets |
| `src/uk.osric.asp.Web/Html/SiteNavigation.cs` | **new** — the shell's composition: identity, assistant, path, query in; links out |
| `src/uk.osric.asp.Web/Pages/PageLayoutState.cs` | **new** — the `IPageLayout` implementation: the chosen key, the required title, head, sheets, body, and whether the page changed the default |
| `src/uk.osric.asp.Web/Pages/PageEndpoint.cs` | **changed** — builds the chain, renders layers, merges, writes the outermost body |
| `src/uk.osric.asp.Web/Pages/PageRenderer.cs` | **changed** — a render session that takes one slot and one deadline and renders layers under it |
| `src/uk.osric.asp.Web/Pages/SimpleContext.cs` | **changed** — takes the layout state and a shared body budget; no contract change |
| `src/uk.osric.asp.Web/Pages/RenderOptions.cs` | **changed** — `MaxLayoutDepth` from `Pages:Render:MaxLayoutDepth` (four by default, clamped to 1–16) and the shared body budget |
| `src/uk.osric.asp.Web/Pages/PageLayoutConfiguration.cs` | **new** — `Pages:Layout`, normalised, empty means none |
| `src/uk.osric.asp.Web/Program.cs` | **changed** — register the layout configuration; map `GET /logout` |
| `src/uk.osric.asp.Web/Pages/Seeds/_layout` | **new** — the layout page: head, skip link, banner, menu, body |
| `src/uk.osric.asp.Web/Pages/Seeds/admin` | **new** — the admin index fragment |
| `src/uk.osric.asp.Web/Pages/Seeds/layout.css`, `site.css` | **changed** — the skip link, the banner and the menu |
| `src/uk.osric.asp.Web/Authentication/LoginEndpoint.cs` | **changed** — `LogoutFormAsync` renders the confirmation; `LogoutAsync` unchanged |
| `src/uk.osric.asp.Web/Editor/EditorEndpoint.cs` | **changed** — the sign-out form goes; the nav and the sheet list on every document |
| `src/uk.osric.asp.Web/Admin/*`, `Assistant/AssistantEndpoint.cs`, `Metrics/StatusEndpoint.cs`, `Errors/ErrorResponses.cs` | **changed** — the nav above each view's own local nav, and `stylesheet:` becomes a list at the eleven call sites that pass one |
| `src/uk.osric.asp.Web/Pages/ReservedPaths.cs`, `appsettings.json` | **changed** — `/admin` out, `/admin/import` and `/admin/export` in; `Pages:Layout` set |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | **changed** — `page-author-v5`: a page is a fragment, the layout is the document, the menu is the layout's |
| `docs/architecture.md`, `docs/decisions.md`, `docs/testing.md`, `docs/jobs/` | **changed** — see "Documentation and decisions" |

The seeded sheets and the layout are content: an installation running since before this change
keeps its old `/layout.css`, `/site.css` and no `/_layout` until the seeds run, and it degrades to
today's unwrapped pages until they do.

## Tests

**`Web.Tests` — `PageDocumentTests`** (new; the Sdk has no suite and `Web.Tests` already tests the
fragments)

- `TheHeadMatchesWhatTheShellWrites`
- `TheSheetsAreLinkedLayoutThenSiteThenTheView`
- `TheBannerIsTheSkipLinkThenTheHeaderAroundTheNav`
- `TheOpenBodyAndTheMainLandmarkAreSeparatedSoChromeCanGoBetween`
- `ClosingTheDocumentClosesTheLandmarkItOpened`
- `TheBuilderEncodesTheTitleAndTheAttributes`
- `APageWithNoSheetsGetsTheBaseTwoAndNothingElse`
- `EverySheetThePageAsksForIsLinkedInTheOrderItAsked`
- `OnePageMayLinkSeveralSheets`
- `TheSameSheetIsLinkedOnlyOnce`
- `ASheetThatIsNotSiteRelativeIsRefused`
- `NavRendersLinksAndMarksTheCurrentItemAsText`
- `NavEncodesTheHrefAndTheText`
- `NavNamesTheLandmarkAndRefusesAnEmptyName`
- `NothingInTheBuilderWritesAHeaderOrAStatus`

**`Web.Tests` — `PageLayoutChainTests`** (new, the heart of the plan)

- `AnHtmlPageIsWrappedInTheDefaultLayout`
- `APageThatClearsItsLayoutIsServedAlone`
- `APageMayChooseAnotherLayout`
- `ANonHtmlPageIsNeverWrapped`
- `ARedirectIsNeverWrapped`
- `TheContentRunsBeforeItsLayout`
- `TheLayoutReceivesTheContentAsItsBody`
- `ATitleOrHeadValueSetByTheContentReachesTheLayout`
- `AWrappedPageThatSetsNoTitleIsAnErrorNamingThePage`
- `ReadingATitleThatWasNeverSetThrows`
- `TheOutermostLayoutWritesTheTitle`
- `EachLayersHeadMarkupIsWrittenOnce`
- `ALayersSheetsComeAfterTheSitesAndBeforeThePagesOwn`
- `APageThatAddsNoSheetAddsNone`
- `TwoPagesSheetsKeepTheOrderTheyWereAddedIn`
- `TheSameSheetFromTwoLayersIsLinkedOnce`
- `TheContentOwnsTheStatusTheContentTypeAndTheHeaders`
- `AllowingScriptsInAnyLayerAllowsThemInTheResponse`
- `TheDefaultLayoutFallingOverServesTheContentUnwrapped`
- `AMissingExplicitLayoutIsAnErrorNamingTheKey`
- `ALayoutMayNameAParentAndTheChainNests`
- `ACycleStopsAtTheFirstRepeat`
- `TheChainStopsAtTheDepthLimit`
- `OneRequestTakesOneSlotAndOneDeadline`
- `TheBodyBudgetIsSharedByTheChain`
- `EachLayerReadsItsOwnStorage`
- `TheLayoutSeesTheRequestedPathAndItsOwnKey`

**`Web.Tests` — `SiteNavigationTests`** (new)

- `AnAnonymousVisitorIsOfferedHomeAndSignIn`
- `ASignedInVisitorIsOfferedEveryTool`
- `TheAssistantIsOfferedOnlyWhenItIsEnabled`
- `TheSignInLinkCarriesTheCanonicalPathAndRawQuery`
- `TheHomeItemIsCurrentOnlyAtTheRoot`
- `EveryEditRouteMarksThePageListCurrent`
- `AnAdminRouteMarksAdminCurrent`
- `TheSignOutViewMarksSignOutCurrent`

**`Web.Tests` — `ShellDocumentTests`, `AccessibilityTests`** (changed)

- `TheSkipLinkIsTheFirstThingInTheBodyAndTargetsMain`, `TheBannerPrecedesTheOnlyMainLandmark`, and
  the existing head, sheet, class and cache tests, unchanged in shape.
- `TheSkipLinkIsTheFirstFocusableElementAndTargetsMain` as a shared rule; the existing "one `main`,
  one `h1`", "every nav is named and no two share a name" and heading-order rules apply to the
  wrapped page and to every shell view.
- `ShellViews` gains `logout` and `admin`.

**`Web.Tests` — `ReservedPathsTests`, `PageAssetSeederTests`, `PageLayoutConfigurationTests`**

- `TheAdminIndexIsNotReservedButItsToolsAre`
- `TheAdminSeedIsAReachablePage`
- `TheDefaultLayoutComesFromConfigurationAndNormalises`
- `TheLayoutDepthComesFromConfigurationAndIsClamped`

**`Web.IntegrationTests` — `SiteChromeTests`** (new)

- `EveryPageIsWrappedInTheSeededLayout`
- `AnAnonymousVisitorSeesSignInAndNoTools`
- `ASignedInVisitorSeesTheToolsAndSignOut`
- `SavingTheLayoutChangesEveryPageOnItsNextRequest`
- `TheAdminIndexIsAFragmentTheLayoutWraps`
- `TheAdminIndexRequiresSignIn`
- `TheStatusDashboardIsReachableFromAdmin`
- `SigningOutFromTheMenuEndsTheSession`
- `APageThatClearsItsLayoutIsServedUnwrapped`
- `DeletingTheLayoutLeavesEveryShellViewAnswering`
- `AnAnonymousRenderSetsNoAntiforgeryCookie`

## Phases

Each phase ends with `make test` green and `make format-check` clean.

1. **The builder.** `Sdk/PageDocument`, `Sdk/NavLink`, `PageLayout.Nav` and `ShellDocument` as
   adapters. Nothing renders differently; a stored page can already write a document. This is
   [IDEA-018](../../jobs/README.md).
2. **The layout and the default.** `IPageLayout`, `PageLayoutState`, the chain in `PageEndpoint`,
   the render session, `Pages:Layout` switched on, the shared body budget, the configured depth
   limit, the fallbacks, and the `/_layout` seed with head/`main`/body only. The bundled seeds are
   five asset pages with non-HTML content types, so none of them is wrapped and no seed needs
   migrating. **Pages the owner has written are not migrated by this plan** — one that writes its
   own document must clear `Layout` or be rewritten — and the release note says so. This is
   IDEA-005 phase 2.
3. **The menu, both places.** `/_layout` gains the skip link, the banner and the nav;
   `SiteNavigation` and `ShellDocument` gain theirs, with the nineteen `BeginAsync` call sites; the
   `admin` seed, the reserved-path change, `GET /logout`, the `/status` link, `layout.css` and
   `site.css`. Unit, accessibility and integration tests.
4. **Teach it and record it.** `PageAuthorPrompt` to `page-author-v5`; `docs/architecture.md`
   (a "Layouts" section and the document shell's banner); decisions; `docs/testing.md`; the jobs:
   IDEA-005 phase 2 done, IDEA-018 closed, IDEA-015 and IDEA-017 notes updated.

Phase 2 depends on phase 1; phase 3 depends on phase 2. Phase 2 is the risky one and is worth
landing on its own.

## Documentation and decisions

- **DEC-148**: the layout is a capability, `IPageLayout` — `Layout`, `Title`, `Head`, `Stylesheets`,
  `AddHead`, `AddStylesheet`, `Body` — reached through `IContext.Features`, because it is a set of
  slots a render offers rather than something every page always has. `IContext` and `PageResponse`
  are unchanged; the content owns the response, `AllowsScripts` is the union, head markup and sheets
  accumulate outermost-first, and `Body` is a value the layout writes rather than a method that
  writes for it.
- **DEC-157**: a wrapped page must set a title, and the outermost layer writes it. Reading `Title`
  when it was never set throws, and the host refuses the chain after the content has run and before
  the layout starts, so the error names the page that forgot rather than the layout that noticed. A
  page that clears `Layout` is exempt, because it writes its own head. Additive titles need no
  contract: a layout composes `$"moosemorals.com — {layout.Title}"` and renders last. A
  contract-level version is deferred until a layout needs something interpolation cannot say.
- **DEC-158**: a layout is pure markup. It may not set the status, the content type or a response
  header; an attempt is ignored and logged, and `AllowScripts` is the only response setting it
  shares, as a union with the content's, because its script is part of its chrome.
- **DEC-149**: `Pages:Layout` names the default. Every `text/html` page is wrapped unless it clears
  `Layout`; a redirect or a non-HTML response never is; the layout itself is reached by the
  seen-set. A missing or uncompilable *default* degrades to unwrapped with a warning and a metric; a
  missing *named* layout is an error naming the key.
- **DEC-150**: one request renders the chain under one render slot, one deadline, one fetch budget,
  one body budget and one antiforgery token, with a seen-set and `Pages:Render:MaxLayoutDepth`
  (four by default, clamped to 1–16). Each layer keeps its own `ctx.Path` (and storage) and shares
  `ctx.Request`.
- **DEC-151**: the site menu is written twice on purpose — composed by the host for shell views,
  written in the seeded `/_layout` for pages — and rendered by one `PageDocument.Nav`. The shell's
  document is not the stored layout, because the recovery routes must not depend on page code.
- **DEC-152**: `/admin` is a seeded fragment page linking import, export and `/status`; `/_layout` is
  an ordinary page (editable, restorable, not reserved), and only `/admin/import` and `/admin/export`
  stay reserved beneath `/admin`. This is a partial reversal of DEC-174, which retired a *duplicate
  page list*, not an index of tools.
- **DEC-153**: sign-out is a `POST` from a confirmation view reached by a `GET`.
- **DEC-154**: the skip link and the banner return, superseding DEC-121 and extending DEC-107.
- **DEC-155**: a page controls its own stylesheets — none, one or many — with `AddStylesheet`, and
  the order is `PageDocument.Head`'s: the document's base sheets (`/layout.css`, `/site.css`), then
  each layout's from outermost to innermost, then the page's in the order it asked. An exact
  duplicate href is linked once, and an href that is not site-relative is refused with an exception
  rather than left to be silently blocked by `style-src 'self'` (DEC-93). The builder, not the
  caller, writes the base sheets and the icon, so the shell and the layout cannot disagree about
  them.
- **DEC-156**: the document builder owns the document's policy — the language, charset, viewport,
  title, base sheets and their order, the icon, the skip link, the banner, the `main` landmark and
  the close — because that is what makes "one writer" worth having and what stops a seeded layout
  and a shell view disagreeing. It owns no *response* policy: status, content type, security
  headers (CSP and `AllowScripts` included), caching and the body cap stay with the shell. IDEA-018
  point 7's worry is about the response, and the line is drawn there.
- `docs/architecture.md`: a "Layouts" section (the capability, the chain, the default, the
  fallbacks) and the document shell's banner and skip link.
- `docs/testing.md`: the shell view count and one sentence naming the new suites.
- `docs/jobs/README.md`: IDEA-005's phase 2 marked done with a pointer here; IDEA-018 closed;
  IDEA-015 and IDEA-017 note that the layout they needed now exists.
- `deploy/README.md`: nothing new to deploy; the layout and its sheets are seeded pages, restored
  from `/edit` like any other.

## Risks

- **One layout, every page.** `/_layout` is now the site's single point of visual failure. The
  mitigations are the fallback (a missing or broken layout serves content unwrapped), the shell's
  own document for the recovery routes, and the warning plus metric so a silent fallback is not a
  silent failure. The owner should know that editing the layout changes the whole site on the next
  request — which is the point, and worth saying in the editor.
- **Pages that write their own document.** This plan turns the default on and does not migrate the
  owner's pages: one that writes `<!DOCTYPE html>` is wrapped and nests until the owner clears
  `Layout` or rewrites it as a fragment. That is visible on the first request rather than silent,
  the escape hatch is one statement, and the release note names the rule. A listing flag for a
  template that opens with `<!DOCTYPE` is a cheap follow-up if finding them becomes a chore.
- **Two renders per page.** Latency roughly doubles for the chrome, and a first request compiles two
  pages. The slot is held once, so concurrency is unchanged; the deadline covers both.
- **The body cap is now spent on chrome too.** A page near the limit today may exceed it once the
  layout is added; the budget is shared rather than doubled, and the message should say which layer
  ran out.
- **`ETag` covers the chrome.** Editing the layout invalidates every page's validator. That is
  correct and should be stated, because a full revalidation across the site is expected, not a
  fault.
- **Two chrome writers, one shape.** The builder owns the document's shape, but the shell composes
  its menu items and the layout writes its own, so the *items* can drift. One renderer, one place in
  the prompt, and a test that compares the two lists keep it honest.
- **A broken layout cannot be fixed from the editor?** It can: the editor is a shell view. The trap
  IDEA-017 warns about is only real if the *save* moves into a page, which this plan does not do.

## Settled at review

| Question | Answer |
| --- | --- |
| `Body` as a value or a writer | The value: `<%== layout.Body %>` (DEC-148) |
| Who owns the title | A wrapped page must set one, and the outermost layer writes it; reading it unset throws, and the host refuses the chain naming the page that forgot (DEC-157). Additive titles are an interpolation in the layout; a contract-level version is deferred |
| Is the default on out of the box | Yes, `Pages:Layout = "/_layout"`. The bundled seeds are non-HTML and need no migration; the owner's own pages are the owner's to migrate |
| How deep may a chain go | Four, from `Pages:Render:MaxLayoutDepth`, clamped to 1–16 (DEC-150) |
| May a layout set the response | No — status, content type and headers are the content's, and a layout is pure markup (DEC-158) |
| Should the shell compare its menu with the layout's | Yes, an integration test over the two item lists |
| `media` or `nonce` on a sheet | Not yet; `AddStylesheet(href)` covers what was asked |

**Deferred, so they are not rediscovered.** A contract for additive titles (a separator setting or a
list of segments) if a layout ever needs more than interpolation; a per-page layout directory; a nav
editor; and flagging a `<!DOCTYPE` template in the listing, if finding the owner's un-migrated pages
turns out to be a chore.

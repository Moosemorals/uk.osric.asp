<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The seed pack keeps the pages, and the shell keeps its files

| | |
| --- | --- |
| Status | Merged 2026-11-16. The decisions below are in [the record](../decisions.md); the steps landed in five commits |
| Size | Large as a programme, Medium as each step — three seeds retired, five files moved from the pack to `wwwroot`, one shell route added and one rebuilt from the shell's link list, one route that reads the licence files, and the tests, manual, prompt and deployment notes for each. No migration, no new configuration key and no `Sdk` change |
| Area | Web (html, admin, documentation, pages, seeds, wwwroot), deploy, docs |
| Spike | None. Every count was read off the working tree on 2026-11-16: 26 files in `Pages/Seeds`, the consumers of each seed, the route table, `ReservedPaths`, `ShellRoutes`, `SecurityHeaders`, `ShellDocument` and the tests that name a retired key |
| Decisions | Recorded at the merge: `shell-admin-is-a-shell-route`, `shell-admin-assets-are-static-files` and `shell-the-licences-are-read-not-restated` are new, and the first supersedes the retired admin-seed entry; `pages-scratch-saves-itself` and `shell-the-menu-is-written-twice` are edited in place |
| Backlog | [IDEA-030](../jobs/closed.md) takes the link list this plan builds; [the shell navigation plan](shell-navigation.md) is the wider programme, and its D4 is superseded here |

## The short version

The pack is 26 files, and only ten of them are pages. Three pages are demonstrations or an index the
shell should answer for, and five of the fourteen assets are loaded only by the shell's own
administration views. This plan takes the pack down to its pages:

1. **Three seeds go.** `/scratch` and `/recent` are demonstrations of capabilities `/help`
   documents and the editor demonstrates better; `/admin` becomes a shell route. `/vim-help` stays
   seeded for now (the owner, 2026-11-16).
2. **The shell's own files stop being pages.** `admin.css`, `admin.js`, `status.css`,
   `reference.css` and `rocket.svg` move to `wwwroot` and the static file handler serves them.
   Nothing else moves in this step: `/site.css` is the owner-editable base sheet and the assistant's
   sheet and script stay in the pack for now.
3. **`/admin` is a shell route.** It renders the Administration section of one list of shell links,
   so the index and the menu the administration views carry cannot disagree, and it is public — an
   anonymous visitor sees the public tools and a sign-in line, not a redirect.
4. **The licence texts move off `/about`.** A public `/admin/copyright` renders the `LICENSE`, the
   `LICENSES/` texts, the `REUSE.toml` and the third-party notices the build carries, so `/about`
   stops carrying a second copy of a file it has to keep in step by hand.

Each step is independently shippable, and the order below is the order they are built.

## What the owner decided

| Question | Answer | What it settles |
| --- | --- | --- |
| Which seeds are deleted? | `/scratch`, `/recent`, `/admin` (2026-11-16) | Step 5, and step 3 for `/admin` |
| What happens to `/vim-help`? | Left seeded for now (2026-11-16) | It stays; its two links to `/notes` and `/vim` are unchanged, and the question stays open |
| What is `/admin` now? | A shell route whose contents come from the one shell link list (2026-11-16) | Step 3 and step 4 — the list is built first |
| Who may read `/admin`? | Anyone, with the owner's tools hidden (2026-11-16) | Step 4: the index filters by audience; it is not a redirect |
| Who may read `/admin/copyright`? | Anyone, like `/admin/reference` (2026-11-16) | Step 5: mapped without `EditorPolicy` |
| Which files leave the pack? | `admin.css`, `admin.js`, `status.css`, `reference.css`, `rocket.svg` (2026-11-16) | Step 1. `/site.css` and `assistant.*` are **not** in this step |
| What about `/privacy`'s drift? | Deferred (2026-11-16) | Not touched here; it stays a job |

## What exists today

| Seed | Lines | Read by | Verdict |
| --- | ---: | --- | --- |
| `pages` | 766 | The product | keep |
| `help` | 743 | The manual, linked from the editor and the admin menu | keep |
| `_layout` | 28 | Every C# page | keep |
| `_lib/document`, `_lib/editor` | 255 | The layout and the editor | keep |
| `about`, `privacy` | 197 | The footer of every document | keep |
| `editor-mode` | 71 | Owns the editor's session answer | keep |
| `site.css` | 277 | Every document, and the owner may edit it | keep |
| `editor.css`, `editor.js`, `vim.css`, `vim.js`, `sortable.*`, `assistant.*` | 4,276 | The editor and the assistant | keep |
| `admin` | 9 | The site menu's Admin link | **retire** — four of the eight administration tools, written by hand |
| `recent` | 45 | Nothing; not in a menu | **retire** — a public listing that publishes every key and save time |
| `scratch` | 93 | `/help#writing` and the assistant prompt | **retire** — the editor is the worked example of the same save |
| `vim-help` | 200 | The editor's menu and hint; `/vim.css` | keep, for now — five of its links are to `/notes` and `/vim`, which this pack does not ship |
| `admin.css`, `admin.js` | 143 | The import, export, changes and seed-apply views | **move to `wwwroot`** |
| `status.css` | 92 | `/admin/status` and its chart | **move to `wwwroot`** |
| `reference.css` | 111 | `/admin/reference` | **move to `wwwroot`** |
| `rocket.svg` | 117 | The icon every document links; `make vscode-extension` | **move to `wwwroot`** |

The route table already reserves every administration tool except the index itself
(`ReservedPathsTests.TheAdminIndexIsNotReservedButItsToolsAre` pins that), and `ShellRoutes.All` is
pinned to `ReservedPaths.Default` in both directions, so a new route without a record fails the gate.

## The design

### D1 — The shell's own files are files

> Recorded as `shell-admin-assets-are-static-files`.

`wwwroot/admin.css`, `admin.js`, `status.css`, `reference.css` and `rocket.svg` are the same bytes
under a different home, and `app.UseStaticFiles()` is added to the pipeline. It sits after the
request log scope and `SecurityHeaders`, and **before `UseRouting`**, so a physical file answers
before routing and the page catch-all never sees the key.

- **The URLs do not change.** `/admin.css` is still `/admin.css`; a stored page that linked
  `/admin.css?v=<hash>` still works, because the query is not part of the path the file provider
  matches.
- **The five keys become reserved.** A page at `/admin.css` would otherwise be shadowed silently by
  a file. They join `Pages:ReservedPaths`, gain a `ShellRoutes` record that says they are the shell's
  own files, and the editor refuses to save them, as it already refuses a shell route.
- **Caching is the file handler's.** A static file answers with `Last-Modified` and `ETag` and no
  `Cache-Control`, so a browser revalidates and gets a `304`; the stored assets keep their `no-store`
  and their `?v=` revision links. The security headers apply unchanged, and `style-src 'self'` and
  `img-src 'self'` already allow them.
- **`PageAssetMap` is untouched.** It versions page-store assets; a path it does not know is
  returned as written, so the shell's links to its own files are unversioned and correct.
- **The pack stops shipping them.** On an existing installation the seeded row remains and the file
  shadows it; `seeds list` reports it as a seed the release no longer ships, and the owner deletes
  or scrubs it in the editor. `deploy/operations.md` gets the one-time note, replacing the "restore
  `/status.css` after deploying this page" line.

`site.css`, the editor's six files (`editor.css`, `editor.js`, `vim.css`, `vim.js`, `sortable.css`,
`sortable.js`) and the assistant's two stay in the pack: the first is the owner-editable base sheet
(`pages-one-base-sheet`), and the other eight are loaded by a stored page, so the owner may still
want to fix one without a release.

### D2 — One list of shell links, in `Web`

`Html/ShellLinks.cs` holds the values and the list:

```csharp
internal enum ShellSection { Site, Administration }
internal enum ShellAudience { Everyone, Anonymous, Owner }
internal readonly record struct ShellLink(string Href, string Text, ShellSection Section, ShellAudience Audience);
internal static class ShellLinks {
    public static IReadOnlyList<ShellLink> All(bool authenticated, string path, string query, string editorPath);
}
```

This is [the shell navigation plan](shell-navigation.md)'s D1 without its `Sdk` half: the values stay
internal to `Web` because no page reads them yet. [IDEA-030](../jobs/closed.md) moves them to
`Sdk` and puts `ShellLinks()` on `IPageCatalog` when `_layout` and the editor want the list; that is a
mechanical move, and the seeded layout keeps its hand-written menu until then.

> **Landed 2026-11-18.** The `Sdk` half is in the shell navigation plan: the values are
> `Sdk/ShellLink.cs` and `IPageCatalog.ShellLinks()`, and the seeded layout reads them.

- **`SiteNavigation`** filters the `Site` section and marks the current item, exactly as it does
  today; the sign-in link carries this request's `returnUrl`, so the list takes the path and query.
- **`AdminNavigation`** filters `Administration` and marks the current item. Its label becomes **Site
  administration**, because the list includes the status dashboard and the copyright page.
- **The list is facts.** No `IsCurrent`, no markup: each renderer decides how a link is marked. The
  index page renders the same values as text rather than links.
- **A test asserts the list against the route table**: every administration href is a reserved key
  or the editor's configured key, so a link cannot lead to a page the shell does not answer.

### D3 — `/admin` is a shell route

> Recorded as `shell-admin-is-a-shell-route`; it retires the entry that kept the index a seeded page.

`Admin/AdminIndexEndpoint.cs` answers `GET /admin` without `EditorPolicy`:

```
<h1>Administration</h1>
<p>What this site lets its owner change…</p>
<ul class="admin-pages" role="list">…one <li> per Administration link…</li></ul>
<p>…the manual, and the sign-in line for a visitor with no account…</p>
```

- **The audience is the filter, not the route.** `Owner` links are rendered only for a signed-in
  visitor; `Everyone` links (Reference, Copyright, Help) are always rendered. An anonymous visitor
  gets the public tools and a line saying the rest needs a sign-in; there is no redirect.
- **The index links itself out.** The list carries `/admin` so the menu the administration views
  draw marks it current; the index page renders it as text, which is the same rule the menus follow.
- **The route is reserved.** `/admin` joins `Pages:ReservedPaths`; the prefix rule means every
  `/admin/…` key stays reserved as it is today, and the existing error remedies that offer "edit
  this page" already refuse a reserved key.
- **The seeded page is deleted**, and the decision that kept it is retired (`shell-admin-is-a-shell-route`). An existing installation's
  stored `/admin` row becomes an unreachable owner page; the operations note says to scrub it.

### D4 — `/about` keeps the credits; `/admin/copyright` keeps the licences

> Recorded as `shell-the-licences-are-read-not-restated`.

`Admin/CopyrightEndpoint.cs` answers `GET /admin/copyright` publicly, in the shell document with the
admin menu, and renders what the build carries:

| Source | Rendered as |
| --- | --- |
| `LICENSE` | The site's own licence (MIT), in a `<details>` with the text in `<pre class="source">` |
| `THIRD-PARTY-NOTICES.md` | Rendered Markdown through `MarkdownRenderer.ToHtml`, because it is the notice the image carries for the packages it ships |
| `LICENSES/*.txt` | One `<h3>` per file, sorted, each text in a `<details>` |
| `REUSE.toml` | A sentence on what it is and what `make license-check` proves, and the file itself in a `<details>` |

A `LicenceLibrary` singleton reads the four once, lazily, the way `ReferenceLibrary` does; a missing
file is named on the page and the rest still renders, so a build that did not come through the
Makefile says what it lacks rather than failing.

- **They ship.** `LICENSE`, `LICENSES/*.txt` and `REUSE.toml` join `THIRD-PARTY-NOTICES.md` in the
  publish output (`None` items with `CopyToPublishDirectory`), because the deployed image has no
  checkout and the route cannot read the repository.
- **The notices read the same in both places.** The two relative links in
  `THIRD-PARTY-NOTICES.md` (`LICENSES/…`) become absolute repository URLs, so GitHub and the site
  resolve them and the page needs no rewriting pass.
- **`/about` stops copying.** It keeps the introduction, the build's commit and the credits, and its
  licence section becomes one paragraph linking to `/admin/copyright`. The comment that says the
  page mirrors the notices goes with the text.
- **A test reads the files**: the licence, the notices, every `LICENSES` text and the `REUSE.toml` are
  on the page, a missing file is named, and the route answers anonymously.

### D5 — The two retired demonstrations

- **`/scratch`.** The rule is `pages-scratch-saves-itself`, and it stays: a page may save its own
  source. The entry is edited to stop naming a seed, `/help#writing` keeps its code sample as the
  worked example, and the integration suite that drives a self-save creates its own page instead of
  reading the seed.
- **`/recent`.** The listing it demonstrated is `/pages`, which the owner already has, and a public
  listing of every key and save time is a disclosure the site does not need. `/help#catalog` keeps
  the sample; `PageCatalogTests` creates the page it lists.

The assistant's author prompt names `/recent` and `/scratch` as examples and is corrected in the same
step, or the model keeps teaching a page that is gone.

## What it touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Web/wwwroot/{admin.css, admin.js, status.css, reference.css, rocket.svg}` | **new** — moved verbatim from the pack |
| `src/uk.osric.asp.Web/Pages/Seeds/{scratch, recent, admin, admin.css, admin.js, status.css, reference.css, rocket.svg}` | **deleted** |
| `src/uk.osric.asp.Web/Program.cs` | `app.UseStaticFiles()` before routing; map `/admin` and `/admin/copyright` |
| `src/uk.osric.asp.Web/Html/ShellLinks.cs` | **new** — the values and the one list |
| `src/uk.osric.asp.Web/Html/SiteNavigation.cs` | Filters the `Site` section; the sign-in href carries `returnUrl` |
| `src/uk.osric.asp.Web/Admin/AdminNavigation.cs` | Filters `Administration`; the label becomes **Site administration** |
| `src/uk.osric.asp.Web/Admin/AdminIndexEndpoint.cs` | **new** — the public index, audience-filtered |
| `src/uk.osric.asp.Web/Admin/CopyrightEndpoint.cs`, `LicenceLibrary.cs` | **new** — the licence page and its read |
| `src/uk.osric.asp.Web/Pages/ReservedPaths.cs`, `appsettings.json` | `/admin`, `/admin/copyright` and the five files |
| `src/uk.osric.asp.Web/Assistant/ShellRoutes.cs` | Records for the seven new reserved keys |
| `src/uk.osric.asp.Web/Pages/Seeds/about` | The licence section becomes a link |
| `src/uk.osric.asp.Web/Pages/Seeds/help` | The `/scratch` and `/recent` examples rewritten |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | The examples it teaches |
| `src/uk.osric.asp.Web/uk.osric.asp.Web.csproj` | Pack `LICENSE`, `LICENSES/*.txt` and `REUSE.toml` |
| `THIRD-PARTY-NOTICES.md` | The two relative links become repository URLs |
| `REUSE.toml` | The retired seeds leave the annotation list; `wwwroot` files carry their own tags |
| `Makefile` | `ROCKET_SVG` moves to `wwwroot` |
| `deploy/operations.md` | The `/status.css` restore note and the one-time scrub note |

## Tests

| Suite | What proves it |
| --- | --- |
| `PageAssetSeederTests` | `SeedPaths` is 18, not 26; the five moved files are not seeds; the pack compiles as it does today |
| `PageAssetTests` (integration) | The five static files answer with `ETag` and no `no-store`; the stored assets keep `no-store` and their `?v=` links; `/site.css` and `/assistant.*` are unchanged |
| `ReservedPathsTests` | `/admin`, `/admin/copyright` and the five files are reserved; `SimilarKeysAreNotReserved` moves `/rocket.svg` to the reserved set |
| `ShellRoutesTests` | The record set matches `ReservedPaths.Default` in both directions, with the seven new records |
| `ShellLinksTests` (new) | One href per item; every administration href is reserved or the editor's key; audiences and sections; no duplicate labels inside a landmark |
| `AdminIndexEndpointTests` (new) | Anonymous sees the public tools and no owner tool; the owner sees every tool; the current item is text |
| `CopyrightEndpointTests` (new) | The MIT text, the notices, every licence file and the `REUSE.toml` are on the page; a missing file is named; anonymous is allowed |
| `ShellViews`, `AccessibilityTests` | `admin` and `copyright` join the rendered views; the landmark label is **Site administration** |
| `SiteChromeTests` (integration) | The shell and the layout still offer the same Site items |
| `SiteNavigationTests` | The same items, now filtered from the list |
| `PageSelfSaveTests` (integration) | Creates the page it saves instead of reading `/scratch` |
| `PageCatalogTests` (integration) | Creates the listing page instead of reading `/recent` |
| `RestoreEndpointTests`, `ApplySeedsEndpointTests`, `ExportEndpointTests` | The pack's new size, and a moved file no longer appearing as a seed |
| `HelpReferenceLinksTests` | Every anchor and link `/help` carries still resolves |
| `RocketTests` (browser) | The icon still draws under the site's policy, now from `wwwroot` |

## Documentation

- [the shell](../shell.md): `/admin` is a route, the static files, and the copyright page.
- [pages](../pages.md), [the glossary](../glossary.md): the deleted examples.
- [architecture](../architecture.md): a shell file is served from `wwwroot`; a page is a row.
- [deployment](../../deploy/README.md) and [operations](../../deploy/operations.md): what the image
  now carries, and the one-time scrub on an existing installation.
- The **author manual** (`/help`): the Vim keys section, and the two examples that named a retired
  seed. This is required by the same rule that asks for the tests.

## Risks

| Risk | Mitigation |
| --- | --- |
| A static file shadows a stored page silently | The five keys are reserved, so the editor refuses them and the assistant's shell reports them as shell files |
| A missing `wwwroot` makes `UseStaticFiles` log and serve nothing | The folder is in the project; a test asserts each of the five answers |
| An old link with `?v=<hash>` 404s | The query never reaches the file provider; the path still matches |
| Deleting a seed leaves a live row on the server | The operations note names the keys and the editor action; `seeds list` already reports a seed the release no longer ships |
| The notices render dead relative links | The two links become absolute repository URLs in the same change |
| `/admin` as a public route teaches a visitor nothing useful | It lists the two public tools and says the rest needs a sign-in, which is the same sentence the reference page's menu already implies |

## Open questions

- **`/vim-help`.** The key table is 200 lines of prose that must track `vim.js` by hand, and five of
  its links are to `/notes` and `/vim`, which this pack does not ship. Moving it into `/help#vim` is
  the obvious fix; the owner chose to leave it seeded for now.
- **The assistant's two assets.** `assistant.css` and `assistant.js` are loaded only by shell views
  and are the obvious next two files to move; the owner kept them in the pack for now.
- **`/site.css`.** It is the one sheet a page and the shell both link, and `pages-one-base-sheet`
  makes it owner-editable. Moving it is a different decision.
- **`/admin/status` under `/admin`.** [The shell navigation plan](shell-navigation.md)'s D7 is not part of
  this step, so the status dashboard keeps its route and its `/status.css`.
- **`/privacy`.** Its session, sign-in and logging numbers are literals while `appsettings.json` owns
  them; the owner deferred the fix.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Shell navigation and the shell root

| | |
| --- | --- |
| Status | Built 2026-11-18 on the shell-navigation branch — waits for the owner at the merge |
| Size | Large as a programme, Medium as each step — one `Sdk` value (with three enums) and one method on the existing catalog, the seeded layout and admin page, one route moved, shell chrome, the assistant's own menu, and the manual/prompt/document sweep. No migration, no new configuration key, no build change |
| Area | Sdk, Web (html, admin, assistant, metrics, pages, seeds), docs |
| Spike | None. Every claim below was read off the working tree on 2026-11-16: the two menu lists, the admin index, the shell routes, the reserved paths and the tests that pin them |
| Backlog | [IDEA-030](../jobs/closed.md) — closed by this plan |
| Decisions | Edits `shell-the-menu-is-written-twice` and `shell-admin-is-a-shell-route`; records **pages-the-catalog-knows-the-shell**, **shell-the-shell-is-marked** and **shell-the-tools-live-under-admin**. Existing names it relies on: `pages-capabilities-are-one-seam`, `pages-a-layout-is-a-capability`, `pages-the-document-is-the-shells`, `pages-page-first`, `code-policy-values-are-configuration` |

> **Built 2026-11-18.** The route table and the `/admin` shell route had already landed by the time
> this plan was built, so D4 is the seed-trimming plan's and the remaining five steps are on a
> branch of the same name. The owner answered the questions below: all five steps in one branch,
> `/admin/status` with no redirect, and the visible **Site tools** label. One deviation from D2: the
> public `ShellLink` also carries `ShellMatch`, so a page's own menu can mark the same current item
> the shell does rather than guessing which links are subtrees.

## The short version

[IDEA-030](../jobs/closed.md) is right: the same handful of links is written by hand in a dozen
places, and every route change is a sweep. The shape that fixes it is three changes, and they are
deliberately separate steps:

1. **One list of shell links, and pages can read it.** The existing `IPageCatalog` gains
   `ShellLinks()`: the shell's links as values — an href, a label, the section they belong to and
   who they are for. The seeded `/_layout` and `/admin` page stop owning copies and filter the list
   into their own menus. `SiteNavigation` and `AdminNavigation` keep the shell's own document
   honest by filtering the same values. The capability returns facts; the page draws. This is the
   change IDEA-030 asks for.
2. **The shell is visibly the shell.** A shell document carries `class="shell"` on its body and a
   visible **Site tools** label in its banner; the stored layout does not. We stop treating "a shell
   page cannot show stored pages in its menu" as a gap to hide: it is a property of a document that
   has to answer when the page store is broken, and the marker says so. A page that wants a
   page-store menu writes one beside the shell's.
3. **The shell's tools live under one root.** `/admin` is already the shell's root in all but name:
   the index is a page, and every tool except `/status` is beneath it. The step finishes the move
   by putting the status dashboard at `/admin/status` and its chart at `/admin/status/chart`. The
   assistant stays at `/assistant` because it is a page in waiting ([IDEA-025](../jobs/IDEA-025.md)),
   `/login` and `/logout` stay because they bootstrap identity, and `/health`, `/metrics` and
   `/favicon.ico` stay because they are the wire rather than pages.

The first two steps are independent of the third and can land in either order. The third is mostly a
mechanical sweep because the first makes the menu follow one list, and because the root already
exists.

## Goals

1. One list of shell links, read by the shell's own views and by pages, and no view writing a
   second copy of the links the shell already knows.
2. A page can render the site's own links without the host writing a nav, a footer or a menu string
   for it: it gets values, chooses a section and an audience, orders them, marks the current one,
   and owns the markup.
3. The stored layout stops owning the *contents of the site menu* while remaining the document's
   chrome; its footer stays its own markup, because the footer is page chrome rather than a shell
   capability.
4. Every shell document is recognisably a shell document: one root for its tools, one marker in its
   chrome, and one sentence in the manual that explains why its menu is the site's tools rather than
   its pages.
5. The shell never reads the page store to draw its chrome, so the error, health, sign-in, status and
   recovery routes still answer when the page pipeline is broken.
6. Every change is proved by the tests that already pin the menu, the document shape and the route
   table, plus a small number of new ones for the catalog method and the moved route.

## Non-goals

- **A page-store menu in the shell.** The shell's tools are a fixed list; a page can add a
  page-store menu, the shell cannot.
- **Moving `/assistant`.** It is the last view-shaped thing in the shell and has a job to become a
  page ([IDEA-025](../jobs/IDEA-025.md)). Moving it under the shell root now would be undone.
- **Moving `/login` and `/logout`.** They are identity bootstrap, they are conventional, and a
  public visitor should not be taught the site's internal vocabulary to sign in.
- **Moving `/health`, `/metrics` or `/favicon.ico`.** They are not pages. The probe, the scrape
  target and the icon's reserved key are deployment and wire contracts; a shell root would make
  them worse.
- **A configurable root.** The root is the route's identity and the authorization boundary; a bad
  value could let a page claim a shell route. `Pages:Editor:Key` is configuration because it decides
  where a page lives and nothing about authority. If the root should be a policy value, that is
  [IDEA-031](../jobs/IDEA-031.md)'s question.
- **A capability that draws a nav or a footer.** `IPageCatalog.ShellLinks()` returns facts; the
  page draws. A `Site`, `Footer` or `Administration` list on a new capability would be a menu API
  wearing a capability's name.
- **A nav editor, a page-registered menu, or a rendered menu string on `IContext`.** The page
  renders the values; the host does not write the markup.
- **A footer capability.** About and the privacy policy are the layout's own chrome; the shell's
  own document has its own footer. Neither needs a shell link list.
- **Changing authentication, authorization, the layout capability or the document builders.** The
  shell's document and the stored layout remain separate builders for the reason
  `pages-the-document-is-the-shells` records.

## What exists today

The counts are from the working tree, not from the job's prose.

| Copy | Where | What it is today |
| --- | --- | --- |
| Page catalog | [`Pages/PageCatalogScope.cs`](../../src/uk.osric.asp.Web/Pages/PageCatalogScope.cs), [`Sdk/IPageCatalog.cs`](../../src/uk.osric.asp.Sdk/IPageCatalog.cs) | The page's one read seam: four store reads and no shell links. This is the capability the change extends |
| Site menu, shell | [`Html/SiteNavigation.cs`](../../src/uk.osric.asp.Web/Html/SiteNavigation.cs) | `Links(...)` returns Home, Sign in or the owner tools; `Nav`/`For` render them; `FooterLinks()` is the constant footer |
| Site menu, stored pages | [`Pages/Seeds/_layout`](../../src/uk.osric.asp.Web/Pages/Seeds/_layout) | A hand-written `List<NavLink>` with the same items, a plain `/login` rather than a `returnUrl`, and a hand-written footer |
| Admin menu, shell | [`Admin/AdminNavigation.cs`](../../src/uk.osric.asp.Web/Admin/AdminNavigation.cs) | A list built from a `current` token; nine items, including Page list and Help |
| Admin index, stored page | [`Pages/Seeds/admin`](../../src/uk.osric.asp.Web/Pages/Seeds/admin) | A hand-written `<ul>` of import, export, reference and `/status` |
| Assistant menu | [`Assistant/AssistantEndpoint.cs`](../../src/uk.osric.asp.Web/Assistant/AssistantEndpoint.cs), [`Assistant/AssistantMemoryEndpoint.cs`](../../src/uk.osric.asp.Web/Assistant/AssistantMemoryEndpoint.cs) | Three assemblies of Conversations, Memory and Page list, with the current item chosen at each call |
| Error remedies | [`Errors/ErrorMessage.cs`](../../src/uk.osric.asp.Web/Errors/ErrorMessage.cs) | Home, Sign in or Page list, chosen for the failure; not a navigation landmark |
| Status window | [`Metrics/StatusEndpoint.cs`](../../src/uk.osric.asp.Web/Metrics/StatusEndpoint.cs) | A Time window filter, not a site menu |
| Editor menu | [`Pages/Seeds/pages`](../../src/uk.osric.asp.Web/Pages/Seeds/pages) | The editor's own Page list, Help, history and confirmation menus |

Three tests hold the current shape together: [`SiteNavigationTests`](../../test/uk.osric.asp.Web.Tests/SiteNavigationTests.cs)
asserts the shell's items, [`SiteChromeTests.TheShellAndTheLayoutOfferTheSameItems`](../../test/uk.osric.asp.Web.IntegrationTests/SiteChromeTests.cs)
compares the words in the two menus (not their hrefs), and
[`ShellViews`](../../test/uk.osric.asp.Web.Tests/ShellViews.cs) renders every shell and stored view for
the accessibility rules. The route table is pinned in both directions by
[`ShellRoutesTests`](../../test/uk.osric.asp.Web.Tests/ShellRoutesTests.cs) and
[`ReservedPathsTests`](../../test/uk.osric.asp.Web.Tests/ReservedPathsTests.cs).

### The route audit, and what "one root" means here

[The audit](../architecture.md#where-a-view-lives) already sorts every route into a column. This is
the same list against the owner's three questions:

| Route | Why it is where it is | Under the root? |
| --- | --- | --- |
| `/` and every stored page | The product | No — the page store |
| `/admin` | The shell route that indexes the tools (`shell-admin-is-a-shell-route`) | It is the root's index |
| `/admin/import`, `/admin/export`, `/admin/changes`, `/admin/reference`, `/admin/restore`, `/admin/scrub`, `/admin/seed-apply` | Shell tools; bootstrap or the build's own facts | Yes |
| `/status`, `/status/chart` | Bootstrap: what the owner reads when a page did not load | **Move to `/admin/status` and `/admin/status/chart`** |
| `/assistant`, its events, approvals, options, stop, delete and memory view | A page in waiting; the account-scoped store and the tools' authority are the blockers | No — IDEA-025 moves it to a page key |
| `/login`, `/logout` | Identity bootstrap | No |
| `/favicon.ico`, `/health`, `/metrics` | The runtime and the wire | No |
| `{**path}` | The page catch-all | No — it is the product |

The root is therefore **`/admin`**, and the step is small because the only permanent tool outside it
is the status dashboard. A new `/shell` or `/_shell` root would be a second namespace for the same
tools, would have to leave the page index behind, and would not survive the assistant's move to a
page.

## The design

### D1 — One list of shell links, read by shell views and pages

The list is a `ShellLink` value, not a rendered menu: an href, a default label, the section it
belongs to, and who it is for. One `ShellLinks` builder in `Web` owns the list. `SiteNavigation` and
`AdminNavigation` filter it for the shell's own document; the seeded layout and admin page filter it
for their own menus. No view writes the links a second time.

| Section | Contents |
| --- | --- |
| `Site` | Home; Sign in for an anonymous visitor; Assistant, Pages, Admin and Sign out for the owner |
| `Administration` | Administration, Import, Export, Changelist, Restore, Apply seed updates, Reference, Status, Page list and Help |

Each link also carries an audience: `Everyone`, `Anonymous` or `Owner`. The audience is the fact
that makes filtering honest. The site menu hides the owner tools from an anonymous visitor; the
administration menu shows them on the public reference page because a link that leads to sign-in is
a useful answer there. A page chooses which audiences it renders; the shell's own views choose
today's rules.

The list is the shell's *links*, not every route it answers. `/admin/scrub` is reached from the
editor after a delete, not from a menu; it stays out, as it is today. A page that wants the full
route record has the reference and the assistant's shell-file records; it does not need a menu.

The footer is not in the list. About and Privacy policy are the layout's own chrome, and the layout
writes them as it does today; the shell's own document keeps `FooterLinks`. There is no footer
capability, because there is no shell fact a page needs to draw one.

The assistant's menu is not in the list either: no page asks for it, and the assistant becomes a
page in [IDEA-025](../jobs/IDEA-025.md). It still gets one builder in this plan (D8), so the shell
stops writing it three times.

### D2 — The page catalog knows what the shell can do

`IPageCatalog` gains one synchronous read:

```csharp
public IReadOnlyList<ShellLink> ShellLinks();
```

The interface's summary changes from "four reads and no writes" to four store reads, one list of
shell links and no writes, and its "every member returns a Task" paragraph becomes "the store reads
return a Task; `ShellLinks()` is a value".

The values are in `Sdk`:

```csharp
public enum ShellSection { Site, Administration }

public enum ShellAudience { Everyone, Anonymous, Owner }

public readonly record struct ShellLink(
    string Href,
    string Text,
    ShellSection Section,
    ShellAudience Audience);
```

The rules that keep it a fact rather than a menu API:

- **It is values, not markup.** There is no `IsCurrent` and no `NavLink`: the page decides which
  links to render, in what order, with what labels, and which one is text rather than a link. The
  old rejection of `ctx.SiteNav` stands; this is the same rule with a better home.
- **It is the catalog, not a new capability.** `IPageCatalog` is already the page's read seam: it
  answers what the site stores, and now it answers what the shell can do. One capability, one read
  seam.
- **It returns the whole list with audiences.** A page can show an owner link to an anonymous
  visitor and let the sign-in redirect be the answer, as the reference page does today, or it can
  filter. The host does not decide for the page.
- **The sign-in link carries this request's `returnUrl`.** The chain builds the list once from the
  request path and query and the editor key, and gives the same list to every layer's catalog, so a
  layout and the page it wraps see one answer. The sign-in href is the shell's `/login?returnUrl=…`
  rather than the layout's old plain `/login`.
- **The list is the same for every visitor; the audience is how a page filters it.** A page uses
  its own `ctx.User`, so a half-built principal gets the anonymous answer and a page's menu agrees
  with its own guard (`pages-identity-is-a-plain-value`). The shell's `SiteNavigation` filters the
  same way for its own document. The sign-in link is in the list for every visitor; the audience
  hides it from an owner.
- **It is a value, so the method is synchronous.** The shell links are host facts; `ShellLinks()`
  returns them without a task. It still refuses after the render ends, as the catalog's reads do.
- **The page can slice, reorder, relabel or add.** The list is read-only; the page's menu is its
  own. The shell's list is not a menu the page has to keep.
- **The value is checked where it is built.** `ShellLink` validates its href and label as
  `NavLink` does, because a page may construct one of its own alongside the host's.
- **A job's catalog has no shell links.** A worker has no request to return to and no visitor, so
  its `ShellLinks()` is empty; the links are a render fact.

The author-facing rule is one sentence: *the catalog tells you what the shell can do; you decide
what to render.* The layout already wrote the Site menu, so a page writes a second menu only with
its own label, and it does not need a capability to draw the footer the layout already owns.

### D3 — The seeded layout reads the list and writes its own menu

The seeded `/_layout` keeps its job: it is the document's chrome, the owner can edit it, and the
host still wraps every `text/html` page with code in it. What it stops owning is the contents of the
site menu.

Before:

```html
var links = new List<NavLink> { new("/", "Home", At("/")) };
if (ctx.User is null) { links.Add(new("/login", "Sign in", At("/login"))); }
else { links.Add(new("/assistant", ...)); ... }
Document.Banner(ctx, "Site", links, "site-nav");
```

After:

```html
var catalog = ctx.Capabilities.GetRequired<IPageCatalog>();
var links = new List<NavLink>();
foreach (ShellLink link in catalog.ShellLinks()) {
    if (link.Section != ShellSection.Site) { continue; }
    if (link.Audience == ShellAudience.Anonymous && ctx.User is not null) { continue; }
    if (link.Audience == ShellAudience.Owner && ctx.User is null) { continue; }
    links.Add(new NavLink(link.Href, link.Text, IsCurrent(link)));
}
Document.Banner(ctx, "Site", links, "site-nav");
...
<footer><% Document.Nav(ctx, "Footer", new List<NavLink> {
    new("/about", "About"), new("/privacy", "Privacy policy") }, "site-footer"); %></footer>
```

The footer is unchanged: it is the layout's own markup, not a shell capability. The layout's
`IsCurrent` keeps the `At`/`Under` rules it has today; the sign-in link carries a `returnUrl`, so
its current check compares the path rather than the whole href, as the shell's does. The shell's
`SiteNavigation` applies the same rules to the same values, and the integration test compares the
two. The document library's `Head`, `Banner`, `Nav`, `OpenMain`, `CloseMain` and `End` do not change.

The layout is shorter in the part that mattered — it no longer owns a copy of the links — and longer
in the part that is its own: filtering, current marking and the footer are page code. That is the
trade the owner asked for: pages get the facts and the control.

### D4 — The seeded admin index reads the same list

> **Superseded 2026-11-16** by [the seed-trimming plan](seed-trimming.md): `/admin` is a shell
> route that renders the same list (`shell-admin-is-a-shell-route`). The list, its sections and its
> audiences are unchanged; only the renderer moved from a seed to the shell.

`/admin` was a page, and the step below is what it became. Its hand-written `<ul>` becomes a
filter over the catalog:

```html
<% var catalog = ctx.Capabilities.GetRequired<IPageCatalog>();
   var links = new List<NavLink>();
   foreach (ShellLink link in catalog.ShellLinks()) {
       if (link.Section == ShellSection.Administration) {
           links.Add(new NavLink(link.Href, link.Text, IsCurrent(link)));
       }
   } %>
<h1>Administration</h1>
<% Document.Nav(ctx, "Site administration", links, "editor-menu"); %>
```

The landmark's label changes from **Page administration** to **Site administration** in the same
step. The list now includes the status dashboard, which is not page administration; the new words
describe the root. The shell's admin views and the page use the same label.

The page guard stays first. The page links every tool the shell's own admin views link; Status joins
the list in D7, when its route exists. The page decides its own presentation: `Document.Nav` is one
way, and a loop over the same `IReadOnlyList<ShellLink>` is another if the owner prefers the old
`.pages` list. `site.css` gains the `.editor-menu` shape rule (flex, wrap, gap) the shell's admin
views already rely on, so both readers see the same menu. The page can also show the `Reference` and
`Help` links to an anonymous visitor when its own guard allows it; that is a page choice, not a
capability one.

### D5 — The shell is marked as the shell

A shell document gets two marks, one structural and one visible:

- `ShellDocument.BeginAsync` always writes `class="shell"` on `<body>`, followed by any class the
  view asked for. The assistant's body class becomes `shell assistant-page`.
- `ShellDocument.Banner` writes a `<p class="shell-label">Site tools</p>` before the navigation
  landmark. The label is text, not a colour or a `::before`, so it is readable with no sheet and by
  assistive technology.

`/site.css` gains the shape and look rules: the `.shell-label` is small and muted; `.shell
.site-header` carries a distinct accent. The page's own sheet still comes last, so a shell view can
override one rule without a more specific selector. If `/site.css` is missing or broken, the shell
document still answers and the label still says what it is; only the accent is lost.

The banner a shell view writes becomes:

```html
<a class="skip-link" href="#main">Skip to content</a>
<header class="site-header">
<p class="shell-label">Site tools</p>
<nav class="site-nav" aria-label="Site">…</nav>
</header>
```

The stored layout's banner is the same without the label and without `body.shell`.

The stored layout does **not** get the marker. That is the point: a page document and a shell
document are different documents with different menus, and a reader can see which one they are in.
The integration test that compares the two documents keeps comparing the shared skeleton — one
`main`, one Site nav, one footer — and now asserts the difference: the shell's body has the class and
the label, the layout's does not.

This is the answer to the owner's second question. A shell page cannot show stored pages in its
navigation because its navigation is the site's tools, and it must answer when the page store is
broken. The marker stops that being a hidden inconsistency and makes it a stated property. A page
that wants a page-store menu reads `IPageCatalog.ListAsync`, adds it beside `ShellLinks()`, and
writes both as it likes; the layout already gives it the site's menu for free.

### D6 — Why the shell still has its own document

The shell's document and the stored layout stay separate builders. This is not a compromise the
capability removes: the error, health, sign-in, status and recovery routes have to answer when the
page pipeline is broken, so none of them can be served by a stored page, and the shell's document
cannot call the seeded document library.

What changes is the *list*: both documents read the same `ShellLink` values and render their own
markup. `shell-the-menu-is-written-twice` is edited rather than retired — the menu is still written
twice, but the links are one list and each renderer decides what to do with them. The comparison
test becomes stronger: it compares `(href, text, current)` rather than the words, and it compares a
shell route with a page route rather than two page routes.

### D7 — The shell's tools live under `/admin`

The status dashboard moves:

| Before | After |
| --- | --- |
| `GET /status` | `GET /admin/status` |
| `GET /status/chart?...` | `GET /admin/status/chart?...` |
| `Pages:ReservedPaths` holds `/status` | holds `/admin/status` |
| `ShellRoutes` records `/status` | records `/admin/status` |
| `AdminNavigation` has no Status item | has Status after Reference |
| `StatusEndpoint` has the site nav and its window filter | gains the Site administration landmark, with Status current |
| `Dashboard` builds `/status/chart?...` | builds `/admin/status/chart?...` |

`/admin/status` is mapped with the same `EditorPolicy` as the other administration views;
`/admin/reference` stays the one public exception. The sheet stays at `/status.css`, because every
feature sheet is at the root (`pages-one-base-sheet`); moving a page route does not move its sheet.

No redirect is proposed. A `301` from `/status` would keep the old name alive and would need its own
reserved key, which is the opposite of the step. The dashboard is owner-only, so the cost is one
bookmark; the plan asks the owner whether they want the redirect anyway.

The root is not configuration. It is the route's identity and the boundary the `Editor` policy and
`Pages:ReservedPaths` are written against; a value an operator could move would be a way to let a
page claim a shell route. `Pages:Editor:Key` is configuration because it only says where the editor
*page* is served. The root is the kind of constant `/login`, `/health` and `/admin` already are.

#### If the owner wants a new root instead

A `/_shell` root is the obvious alternative, and it is a different plan. The shape would be:

| Today | Under `/_shell` |
| --- | --- |
| `/admin/import`, `/admin/export`, `/admin/changes`, `/admin/reference`, `/admin/restore`, `/admin/scrub`, `/admin/seed-apply` | `/_shell/admin/import`, … |
| `/status`, `/status/chart` | `/_shell/status`, `/_shell/status/chart` |
| `/login`, `/logout` | Stay, or `/_shell/login` and `/_shell/logout` |
| `/assistant` | Stay until IDEA-025, unless it moves twice |
| `/health`, `/metrics`, `/favicon.ico` | Stay — they are the wire |
| `{**path}` | Stay — it is the page runtime |
| `/admin` page index | Stays a page outside the root, or the root index becomes shell code |

The cost is every route map, every link, every test and every document that names one of those paths,
plus a second reserved prefix and a root that the admin page either sits outside of or duplicates.
The benefit is a URL that says "shell" in every address. The recommendation is `/admin` because it
already says "administration", the page-first audit already treats the index as a page, and the
status dashboard is the only tool that has to move.

### D8 — The assistant's own menu is one builder

No page asks for the assistant's menu, so it is not a capability. It is still three hand-written
lists today. A small `AssistantNavigation` in `Assistant/` builds Conversations, Memory and Page
list with the current item by path; `AssistantEndpoint` and `AssistantMemoryEndpoint` call it. When
[IDEA-025](../jobs/IDEA-025.md) moves the assistant to a page, that page either keeps the builder or
replaces it with its own menu; the shell's copy is then deleted with the endpoint.

The assistant's "Conversation navigation" and "Memory navigation" confirmations keep their own
labels and their Cancel links: they are confirmation menus, not the assistant's site menu, and the
landmark rule is one label per landmark rather than one list per area.

### D9 — Error remedies and the status filter are not site navigation

`ErrorMessage.DefaultRemedies` and `ErrorEndpoint` choose Home, Sign in or Page list for a failure.
That is a recovery list, not a menu: it is already tailored to the failure and it is rendered as the
recovery nav beside the site menu. It stays where it is. The status dashboard's Time window nav is a
filter over one page's data, not an audience list; it stays too. The footer is the third case: the
layout's About and Privacy links are page chrome, and the shell's footer is the shell document's;
neither asks the catalog for a list. All three keep their distinct labels, so the "no two landmarks
share a name" rule continues to hold.

### D10 — The new names, and what the record will say

The merge writes these, or folds them into the entries they qualify if the owner prefers:

**pages-the-catalog-knows-the-shell** — `IPageCatalog` answers what the shell can do as well as what
the store holds: `ShellLinks()` returns the shell's links as `ShellLink` values with a section and an
audience. The host returns facts; the page filters, orders, labels, marks current and draws. There
is no nav or footer capability, no `IsCurrent` in the value, and no new capability for the
question. *Recorded in [pages](../pages.md#page-contract) and this plan.*

**shell-the-shell-is-marked** — a shell document carries `body.shell` and a visible **Site tools**
label, so a reader can see that its menu is the site's tools rather than a page's own. The shell
never reads the page store to draw its chrome; a page that wants a page-store menu writes one beside
the shell's. *Recorded in [the shell](../shell.md#the-document-shell), `/site.css` and this plan.*

**shell-the-tools-live-under-admin** — every shell **tool** the audit keeps lives under `/admin`:
the administration views and the status dashboard. The routes outside it are not tools: `/login` and
`/logout` bootstrap identity, the assistant is a page in waiting, `/health`, `/metrics` and
`/favicon.ico` are the wire, and `{**path}` is the page runtime. `/status` and its chart join the
root as `/admin/status`. *Recorded in [the shell](../shell.md), [architecture](../architecture.md#where-a-view-lives)
and this plan.*

`shell-the-menu-is-written-twice` is edited: the menu is still written in two documents, but both
render one list and the shell's document still does not depend on the page pipeline.
`shell-admin-is-a-shell-route` carries the index, and the tools it indexes now include Status under
the same root.

## What it touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Sdk/IPageCatalog.cs` | One new synchronous read: `ShellLinks()` |
| `src/uk.osric.asp.Sdk/ShellLink.cs`, `ShellSection.cs`, `ShellAudience.cs` | New values: an href, a default label, a section and an audience |
| `src/uk.osric.asp.Sdk/PublicAPI.Unshipped.txt` | The new method and values, with `#nullable enable` entries |
| `src/uk.osric.asp.Web/Html/ShellLinks.cs` | New: the one list of `ShellLink` values, built for the request's path, query and editor key |
| `src/uk.osric.asp.Web/Pages/PageCatalogScope.cs` | Returns the list; its constructor takes the list the chain built, or an empty list for a worker |
| `src/uk.osric.asp.Web/Pages/PageChain.cs` | Builds the list once per request and hands it to every layer's catalog |
| `src/uk.osric.asp.Web/Work/PageJobRunner.cs` | Gives a worker's catalog an empty shell-link list; a job has no request or visitor |
| `src/uk.osric.asp.Web/Html/SiteNavigation.cs` | Filters `ShellLinks` for the shell's Site menu; `FooterLinks` is unchanged |
| `src/uk.osric.asp.Web/Admin/AdminNavigation.cs` | Filters `ShellLinks` for the shell's administration menu; Status item; the label becomes **Site administration**; `Nav(context)` |
| `src/uk.osric.asp.Web/Html/ShellDocument.cs` | `body.shell`; the **Site tools** label. The footer is unchanged |
| `src/uk.osric.asp.Web/Assistant/AssistantNavigation.cs` | New: Conversations, Memory, Page list with path-based current |
| `src/uk.osric.asp.Web/Assistant/AssistantEndpoint.cs`, `AssistantMemoryEndpoint.cs` | Use `AssistantNavigation` |
| `src/uk.osric.asp.Web/Pages/Seeds/_layout` | Read `IPageCatalog.ShellLinks()`, filter the Site section and write its own nav; keep its own footer |
| `src/uk.osric.asp.Web/Pages/Seeds/admin` | Read `IPageCatalog.ShellLinks()`, filter the Administration section and write its own nav; link `/admin/status` |
| `src/uk.osric.asp.Web/Pages/Seeds/site.css` | `.shell-label`, `.shell .site-header`, `.editor-menu` shape rules |
| `src/uk.osric.asp.Web/Pages/Seeds/help` | The catalog's `ShellLinks`, the marked shell page, the moved status path, a layout example that renders the list itself |
| `src/uk.osric.asp.Web/Program.cs` | Map `/admin/status` and `/admin/status/chart` |
| `src/uk.osric.asp.Web/Pages/ReservedPaths.cs`, `appsettings.json` | Replace `/status` with `/admin/status` |
| `src/uk.osric.asp.Web/Metrics/StatusEndpoint.cs`, `ChartEndpoint.cs`, `Dashboard.cs` | New paths; the admin nav on the status page |
| `src/uk.osric.asp.Web/Assistant/ShellRoutes.cs`, `PageAuthorPrompt.cs` | The moved route and the catalog's shell links in the assistant's instructions |
| Tests | `PageCatalogScopeTests`, a new `ShellLinksTests`, `SiteNavigationTests`, `AdminNavigation`/`StatusEndpointTests`, the `ReferenceEndpointTests`/`ExportEndpointTests` label assertions, `ShellDocumentTests`/`ShellDocumentFragmentTests`, `SiteChromeTests`, `ReservedPathsTests`, `ShellRoutesTests`, `PageAssetSeederTests`, `StatusPageTests`, `StatusPageSizeTests`, `SecurityHeaderTests`, `SiteLayoutRecoveryTests`, `DashboardTests`, `ChartEndpointTests`, `PageAuthorPromptTests`, `ShellViews` |
| Docs | [the shell](../shell.md), [pages](../pages.md), [architecture](../architecture.md), [metrics](../metrics.md), [testing](../testing.md), [assistant](../assistant.md), [glossary](../glossary.md), [decisions](../decisions.md), [deployment](../../deploy/README.md), [operations](../../deploy/operations.md), `README.md`, the live jobs and plans that name `/status` or the old landmark label; not `docs/archive/**` or `docs/plans/archive/**` |

## Phases

Each phase is a step through the gate and stops for the owner (`product-steps-wait-for-the-owner`).
The first two are independent; the third is cheaper after the first.

### 1 — One list and the catalog method

- Add `ShellLink`, `ShellSection`, `ShellAudience` and `ShellLinks`; add `ShellLinks()` to
  `IPageCatalog`; build the list once in `PageChain` and pass it to each `PageCatalogScope`.
- Make `SiteNavigation` and `AdminNavigation` filter that list; remove `AdminNavigation`'s
  `current` token.
- Change `_layout` and the seeded `admin` page to read the list and write their own menus; leave
  the layout's footer alone.
- Strengthen the integration comparison to `(href, text, current)` and compare a shell route with a
  page route.
- Add `ShellLinksTests`: the site and administration sections, the audiences, the multi-value query
  in a sign-in `returnUrl`, and a stable order. Extend `PageCatalogScopeTests` for `ShellLinks()` and
  its refusal after the render ends. Update `SiteNavigationTests` for the same site menu.

**Done when** the seeded layout, the shell's Site menu, the seeded admin page and the shell's
administration menu render the same items for the same request; a page that writes its own document
can read `ShellLinks()` and render whatever menu it likes; `make check` is green.

### 2 — The shell marker

- `body.shell`, the **Site tools** label, and the `/site.css` rules.
- Update the document-shape test to keep the shared skeleton and assert the difference.
- Add a `ShellViews` assertion that every shell view has the marker and the stored views do not.

**Done when** every shell document is marked, every stored page is not, the accessibility rules
still pass, and a missing `/site.css` leaves the label and the document working.

### 3 — `/status` joins the root

- Move the two routes, update the admin nav, the status page's own links, the dashboard's chart
  links, `ReservedPaths`, `appsettings.json`, `ShellRoutes`, the seeds, the tests and the deployment
  documentation.
- Sweep `/status` out of live docs and live plans; leave the archives alone.
- Add the status item to the administration list and render the Site administration landmark on the
  status page; add the `/admin/status` case to `ShellLinksTests` and `AdminNavigation`'s tests.

**Done when** `/admin/status` and `/admin/status/chart` answer with the owner's policy, `/status`
answers through the page catch-all (a 404 unless a page exists), the reserved list and the route
records agree, and no live document or seed links `/status`.

### 4 — The assistant's menu

- Add `AssistantNavigation`; replace the three hand-written lists.
- Keep the confirmation menus and their labels.

**Done when** the assistant list, conversation and memory views render the same assistant menu, and
the accessibility sweep still finds one label per landmark.

### 5 — The manual, the prompt and the record

- Update `/help`: `IPageCatalog.ShellLinks()`, the marked shell page, the moved route, and a layout
  example that renders the list itself rather than writing the links.
- Update `PageAuthorPrompt` and the shell-file list.
- Write the decisions, edit the two existing entries, close [IDEA-030](../jobs/closed.md) and
  update its row in [the job list](../jobs/README.md), including the Size it turned out to be.
- Sweep the live docs; leave the archives.

**Done when** `make check` and `make decisions` are green, the manual's example compiles, and the
assistant's prompt names the catalog method and the moved route.

## Verification

- **Unit.** `ShellLinksTests` (the two sections, the three audiences, the sign-in `returnUrl`, a
  stable order), `PageCatalogScopeTests` (`ShellLinks()` returns the list, refuses after the render
  ends, and is empty for a worker), `SiteNavigationTests` (the same anonymous and owner Site menu,
  the footer unchanged), `AdminNavigation` path marking, `ShellDocumentTests`/`ShellDocumentFragmentTests`
  (body class and label; the footer is unchanged), `StatusEndpointTests` (admin nav, new paths).
- **Integration.** `SiteChromeTests` compares a shell route's menu with a stored page's menu on
  `(href, text, current)`; the admin page and a shell admin page render the same administration
  list; a page that clears its layout and writes its own document renders a menu from
  `IPageCatalog.ShellLinks()`; the shell's document is marked and the layout's is not;
  `/admin/status` is owner-only and its chart is at `/admin/status/chart`; `/status` no longer
  answers as a route.
- **Accessibility.** `ShellViews` renders every shell view and the stored `admin`, `help` and editor
  pages. The rules already assert a named landmark and no repeated label; the sweep now covers the
  page-rendered menus.
- **Route tables.** `ReservedPathsTests` and `ShellRoutesTests` stay in both directions;
  `appsettings.json` and `ReservedPaths.Default` stay together.
- **Docs and records.** `make decisions`; `grep` for `/status` in live docs; `make license-check`
  for the new files.
- **A hand pass.** The marker's contrast and the "obviously different" claim are looked at in a
  browser at 200% zoom and with the sheet disabled; the status bookmark is updated; the admin page
  and a shell admin page are compared side by side. The existing shell accessibility hand pass is
  the place this is recorded.

## Risks

- **An owner-edited seed.** `/_layout`, `/admin` and `/help` are pages the owner can edit. The
  release offers the new seed; it does not force it. A layout edited before this change keeps its
  hand-written menu until the owner applies the seed, which is the ordinary seed-provenance flow.
- **A production override of `Pages:ReservedPaths`.** The array is read by index
  (`__0`, `__1`, …), so an override must supply the whole intended list rather than one changed
  entry. The deployment documentation must name `/admin/status`; a host that keeps only `/status`
  lets a page be saved at a key the shell shadows. The release notes and the deploy README say so.
- **A bookmark to `/status`.** No redirect is proposed; the owner may ask for a one-release `301`.
- **A page repeating the Site landmark.** The host cannot see a stored page's markup. The manual and
  the prompt state the rule; the accessibility sweep covers the views the shell renders, not every
  page the owner writes.
- **A page showing an owner link to an anonymous visitor.** The audience is a fact; the page filters
  it. The seeded layout filters; the manual and the prompt show the one-line check. The result of
  forgetting is a sign-in redirect, not a leak.
- **The catalog method's shape is permanent.** `ShellLink` carries a section and an audience because
  the first customers filter on both; a later need is an additive member or a new value, not a menu
  registry. The footer is deliberately not in it, so there is no list a page must render to be
  complete.
- **The assistant moves later.** `AssistantNavigation` is shell code until IDEA-025; moving the
  assistant to a page deletes it with the endpoint, which is cheaper than pretending it is a page
  capability now.
- **The marker's wording.** "Site tools" has to be true on `/login` and `/logout` too. The owner may
  prefer "Shell page" or "Site page"; the plan recommends the first and treats the words as a review
  question rather than a code decision.

## The questions this plan asks the owner

1. **The root.** Finish `/admin` by moving `/status` to `/admin/status` (recommended), or introduce
   a new root such as `/_shell` for the tools? The new root is a larger sweep and still leaves the
   assistant outside; the recommendation is the existing root.
2. **The status redirect.** A bookmarked `/status` becomes a 404 unless a one-release `301` is
   wanted. The recommendation is no redirect.
3. **The marker.** Is **Site tools** the right words, and is a text label in the banner the right
   amount of difference? The alternative is a body class and CSS only.
4. **The footer.** The recommendation is that it stays the layout's own markup, with no capability
   and no current-marking change; the shell's own document keeps its own footer. Confirm that is the
   page chrome you want to hand over.
5. **Owner links for an anonymous page.** Should `ShellLinks()` include the owner links with an
   audience (recommended, so a page can show them and let the sign-in redirect answer, as the
   reference page does), or return only what the current visitor can use?
6. **The assistant's menu.** Centralise it in this programme (recommended, it is three copies) or
   leave it for IDEA-025, which deletes it?
7. **The admin page's presentation.** Render the administration list as the shell does
   (`Document.Nav` with the Site administration label), or keep the old `<ul class="pages">` and loop
   over the same list? Both use one source; only the look differs.

## Considered and rejected

- **A new `/_shell` or `/shell` root.** It reads more clearly, but it is a second namespace for the
  same tools, it leaves the page index at `/admin`, it duplicates the landing page, and the
  assistant still cannot move under it without pre-empting IDEA-025. The saving is a mass rename,
  not a rule.
- **Moving `/login` and `/logout` under the root.** They are identity bootstrap and conventional;
  a public visitor should not meet the shell's vocabulary to sign in. Moving them changes
  `LoginPath`, `RedirectToLogin`, every return URL, the rate limiter's special case and the prompt
  for no user benefit.
- **Moving `/assistant` under the root now.** It is a page in waiting; the move would be undone by
  IDEA-025 and would make the page-first audit's "not yet" column look like a permanent home.
- **A page-store menu in the shell.** The shell must answer when the page store is broken. A
  page-store query in the shell's chrome would make `/admin/status` depend on the store it is
  diagnosing.
- **A rendered string on `IContext` (`ctx.SiteNav`).** It was rejected in the original site
  navigation review and is still wrong: the host writes the page's markup, the owner loses control
  of the menu, and the contract grows for a list. The capability gives values instead.
- **A new `IPageNavigation` capability.** It would be a second read seam for the same page, and its
  natural shape is a `Site`/`Footer`/`Administration` menu — a menu API rather than a fact. The
  catalog is already the page's view of the site; it gains one method instead.
- **A capability that draws the nav or the footer.** The page owns its markup; a rendered string or
  a ready-made `NavLink` list with `IsCurrent` takes that ownership back. `ShellLinks()` returns
  values and the page renders them.
- **A footer list.** The footer is page chrome; the layout writes it and the shell document writes
  its own. A capability for it would be a specific menu API with no generic question behind it.
- **One capability per audience.** `ShellLink` already carries the section and the audience; a
  separate `ISiteNavigation`, `IAdminNavigation` or `IAssistantNavigation` would multiply the seam
  for the same list.
- **A configurable `Shell:Root`.** The root is the route identity and the authorization boundary.
  A value that could move it is a value that could let a page claim a shell route; that is the
  direction configuration must not be able to weaken (`code-policy-values-are-configuration`).
- **A page-defined navigation registry.** Authority over the site's links would leave the shell and
  enter the page store; it also gives the shell a page-store dependency by another route.

## Not in scope

- A sitemap page or a page that lists every stored page automatically; `IPageCatalog` already lets a
  page do that, and the site's pages are the owner's content.
- A new nav editor, a menu file, or a configuration key for the menu's items.
- Any change to authentication, authorization, cookies, the page session, the layout chain, the
  document builders or the page store's writes.
- A new browser test for the marker: a unit test proves the class and the label; a browser test
  would prove a CSS rule, and what a string cannot prove is the hand pass.
- Moving the status sheet (`/status.css`) or any other seeded asset; asset paths stay at the root.

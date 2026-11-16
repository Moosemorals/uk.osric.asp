<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-030 — Every view writes its own navigation

| | |
| --- | --- |
| Size | M |
| Area | Sdk, Web (shell, admin, assistant, metrics, pages), seeds |
| Location | `src/uk.osric.asp.Sdk/IPageCatalog.cs`, `src/uk.osric.asp.Web/Pages/PageCatalogScope.cs`, `src/uk.osric.asp.Web/Html/ShellLinks.cs`, `src/uk.osric.asp.Web/Html/SiteNavigation.cs`, `src/uk.osric.asp.Web/Admin/AdminNavigation.cs`, `src/uk.osric.asp.Web/Admin/AdminIndexEndpoint.cs`, `src/uk.osric.asp.Web/Pages/Seeds/_layout`, `src/uk.osric.asp.Web/Pages/Seeds/help` |

> **Part-built 2026-11-16.** The one list exists in `Web` (`Html/ShellLinks.cs`), and the site menu,
> the administration menu and `/admin` all filter it; what is left is the `Sdk` half — the values and
> `IPageCatalog.ShellLinks()` — so that the seeded layout and the editor can read the list too. See
> [the seed-trimming plan](../plans/seed-trimming.md).
>
> **Planned 2026-11-16.** [The shell navigation and shell root plan](../plans/shell-navigation.md) is
> written and waits for the owner. It takes the idea's one-list shape, extends `IPageCatalog` with
> `ShellLinks()` so a page can render the shell's links itself, marks shell documents as shell, and
> finishes the `/admin` root by moving `/status` to `/admin/status`. The plan sizes the whole as
> Large, in five independently shippable steps.

## Idea

The same three or four links — the editor, the admin index, the manual, the way home — are written
by hand in a dozen places: the shell menu, the admin menu, the seeded layout, the editor page, the
assistant and its memory page, restore, import, export, the scrub confirmation and the error
remedies. Each one is a `NavLink` list assembled at the call site, so every new route or moved key
is a sweep, and every sweep is a chance to miss one.

That is how a retirement goes wrong. Removing `/edit` in phase 4 of [the page-editor
plan](../plans/archive/page-editor.md) touched `SiteNavigation`, `AdminNavigation`, six endpoints, both
`PageUrls` users, four error-message factories and five seeds, and the `Pages:Editor:Key` plumbing
that followed touched them all again to stop them naming `/pages` themselves.

The shape it wants is one list per *audience* — visitor, owner, administrator — built in one place
from the current route and the configured keys, and rendered by a landmark with a label. A view
that needs a subset gets it as a slice, not as a hand-written copy.

## Sketch

- One `Navigation` value per audience: the items, their order, and the rule that marks one current.
  `SiteNavigation` and `AdminNavigation` are almost this already; the seeds are the copies.
- The audience lists are shell knowledge — they name routes and keys the page store cannot — so a
  page reaches them the way it reaches the editor's key: through a capability or a context value,
  not by writing the list again.
- The seeded `_layout` stays the document's chrome (`pages-a-layout-is-a-capability`): what it should stop owning is the
  *contents* of the menu, which is what `SiteNavigation` and the page disagree about today.
- Two rules worth pinning while it moves: a landmark has one label and no view repeats it, and the
  item for the page being shown is text rather than a link, in both audiences.
- The accessibility sweep in `ShellViews` already renders every shell document; a page-rendered
  menu would join it, so "every menu is the same menu" becomes an assertion rather than a review.

Worth doing before the next key or route changes, and worth sizing against IDEA-029's language
server only because both touch the same pages.

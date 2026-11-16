<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# One table of the shell's routes

| | |
| --- | --- |
| Status | Proposed 2026-11-16 — waits for the owner. Nothing below is built |
| Size | M — one catalogue replaces two files, `ReservedPaths` derives its list from it, one configuration section is removed, and the tests, prompt, manual and docs follow. No migration, no new route, no `Sdk` change |
| Area | Web (shell, html, assistant, pages), docs |
| Spike | None. The three lists were read off the working tree on 2026-11-16: `ShellRoutes.All` (21 records), `ShellLinks.All` (17 links) and `ReservedPaths.Default` (21 keys); plus the prompt's own copy of the shell files |
| Decisions | At the merge: **shell-one-table-of-routes** and **shell-reserved-keys-are-an-invariant** are new; `code-policy-values-are-configuration` already carries the exception both rely on, and [IDEA-031](../jobs/IDEA-031.md) gains a row |

## The short version

`ShellRoutes` and `ShellLinks` describe the same routes from two directions — the record the assistant
reads, and the menus the shell draws — and `ReservedPaths` is a third copy of the same key set, held in
configuration and kept in step by a test. They already drifted: the assistant's prompt lists fourteen
shell files and the table has nineteen.

This plan makes one table the authority:

1. **One catalogue.** `Web/Shell/ShellCatalogue.cs` holds one entry per route: its path, methods,
   audience, one-line description and the menus it appears in (none, one, or two — `/admin` is in
   both). `ShellRoutes` and `ShellLinks` are deleted; the menu list is derived, and so is the record
   the assistant reads.
2. **`ReservedPaths` is the route paths.** The five lines of configuration go: the key set is derived
   from the table, so the two cannot disagree and there is nothing for an operator to keep in step.
3. **The prompt reads the table.** The assistant's shell-file paragraph stops naming the keys by hand
   and is built from the catalogue when the prompt is, which is the copy that is stale today.

## Why the lists are one thing

| Fact | `ShellRoutes` today | `ShellLinks` today | Verdict |
| --- | --- | --- | --- |
| The key | `Path` | `Href` | **one table** |
| Who may reach it | `Access`, prose | `Audience`, an enum | prose derives from the enum, plus a note |
| What it is called | — | `Text` | the menu entry |
| Where it appears | — | `Section`, `Match` | the menu entry |
| What it does | `Description` | — | the entry |
| How it answers | `Methods` | — | the entry |

Two things do not merge, and the table says so rather than pretending:

- **A link that is not a route.** Home `/`, the editor at `Pages:Editor:Key` and the stored `/help`
  are pages. They are entries of the same table — so the menu order is still one list — but they
  carry no methods, description or reserved key.
- **A route that is not a link.** `/admin/scrub`, `/favicon.ico`, `/health` and `/metrics` answer but
  are not menu items. They have no menu entry, and that is the whole of the difference.

The footer (`About`, `Privacy policy`), the error remedies and the assistant's own menu stay where
they are: none of them is a shell route, and [the shell navigation plan](shell-navigation.md)'s D9
already argues why each is its own list.

## The design

### D1 — One entry per route

```csharp
internal sealed record ShellMenu(ShellSection Section, string Text, ShellMatch Match);

internal sealed record ShellRoute(
    string Path,
    string Methods,
    ShellAudience Audience,
    string Description,
    params ShellMenu[] Menus) {
    public string Access => Audience switch {
        ShellAudience.Everyone => "anyone",
        ShellAudience.Anonymous => "anyone who has not signed in",
        _ => "the owner",
    };
}
```

`Access` is derived because it is the same sentence with a different subject; the two routes that say
more keep a note (`/login`'s throttle, `/metrics`' internal-network rule), which is the only prose
left. `Menus` is a list because `/admin` is both the Site menu's **Admin** and the root of the
Administration menu's **Administration**, and collapsing those two into one would be the loss the
table exists to prevent.

The entries are written in *reading order*: the Site menu filters its section and the Administration
menu its own, so one table order serves both, with no sort key to keep straight. Everything after the
last menu entry is a route with no menu.

### D2 — The links are derived

```csharp
internal static class ShellCatalogue {
    public static IReadOnlyList<ShellEntry> Entries { get; }   // the table, in reading order
    public static IReadOnlyList<ShellRoute> Routes { get; }    // Entries where the entry is a route
    public static IReadOnlyList<string> Reserved { get; }      // Routes.Select(route => route.Path)
    public static IReadOnlyList<ShellLink> Links(string path, string query, string editorPath);
    public static string? Text(PagePath path);                 // the record the assistant reads
}
```

- **`ShellLink` survives** as the value a menu renders: href, text, section, audience, match. It is
  the output of `Links`, not a second list.
- **`Links` walks the table once** and yields one `ShellLink` per (entry, menu) pair, with the
  editor's path substituted for the editor's token and `/login`'s href carrying the request's
  `returnUrl`. That last one is the only special case and it gets one comment, as it has today.
- **`ShellAudience` decides the filter**, and `IsFor` stays beside the values it tests.
- **`Text` is unchanged on the wire**: five lines, one fact per line, pinned by `ShellRoutesTests`,
  because the assistant's `cat` and `read_page` read it.

### D3 — The assistant's prompt reads the table

`PageAuthorPrompt.Build()` interpolates the key list from `ShellCatalogue.Reserved` instead of naming
fourteen of them. The prompt's version goes to `page-author-v35`, because its text changes.

### D4 — `ReservedPaths` derives its keys

```csharp
public static IReadOnlyList<string> Default { get; } = ShellCatalogue.Reserved;
```

- **`Pages:ReservedPaths` is removed** from `appsettings.json`, from `Program`'s reading and from
  `ShippedConfigurationTests`. `TestOptions.ReservedKeys()` goes with it.
- **The list is an invariant, not a policy value.** `code-policy-values-are-configuration` names the
  exception: a security policy may be configured only in the direction that keeps or tightens the
  shipped guarantee. A shorter reserved list lets a stored page shadow `/login`, so configuration
  could only weaken it, and the route table is the only place that knows the routes exist.
- **A stale override is refused rather than ignored.** If `Pages:ReservedPaths` is still present in
  the configuration — an operator kept it from the release before this one — the host stops and says
  the key is no longer read and the shell's routes are the list. Ignoring it would leave an operator
  believing they had narrowed or widened something.

### D5 — What this is not

- **Not the `Sdk` half of [IDEA-030](../jobs/IDEA-030.md).** A page still cannot read the links; that
  needs `IPageCatalog.ShellLinks()` and the values in `Sdk`, and it is a separate step.
- **Not a menu editor.** The table is code. A page can draw its own menu from what it knows; the
  shell's is the shell's.
- **Not a new route or a moved one.** Every path stays what it is; only the copy of the list goes.

## What it touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Web/Shell/ShellCatalogue.cs` | **new** — the entries, `Routes`, `Reserved`, `Links`, `Text` |
| `src/uk.osric.asp.Web/Shell/ShellMenu.cs` | **new** — `ShellSection`, `ShellAudience`, `ShellMatch`, `ShellMenu` |
| `src/uk.osric.asp.Web/Assistant/ShellRoutes.cs` | **deleted** — its records move to the catalogue |
| `src/uk.osric.asp.Web/Html/ShellLinks.cs` | **deleted** — its list becomes the table's menus |
| `src/uk.osric.asp.Web/Pages/ReservedPaths.cs` | `Default` derives from the catalogue; the doc comment says invariant rather than configuration |
| `src/uk.osric.asp.Web/Program.cs` | No `Pages:ReservedPaths` read; a refusal if the key is still configured |
| `src/uk.osric.asp.Web/appsettings.json` | The section goes |
| `src/uk.osric.asp.Web/Html/SiteNavigation.cs`, `Admin/AdminNavigation.cs`, `Admin/AdminIndexEndpoint.cs` | `ShellCatalogue.Links` |
| `src/uk.osric.asp.Web/Assistant/PageStoreReader.cs` | `ShellCatalogue.Text` |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | The key list is interpolated; version `v35` |
| `src/uk.osric.asp.Web/Assistant/PagePathPolicy.cs` | The doc comment stops naming a configuration key |
| `test/…/ShellRoutesTests.cs`, `ShellLinksTests.cs` | Re-pointed at the catalogue, and joined by the tests below |
| `test/…/ShippedConfigurationTests.cs`, `TestOptions.cs` | The reserved-keys test and helper go |

## Tests

| Test | What it proves |
| --- | --- |
| `TheTableIsOneEntryPerRoute` | Every entry's path is unique; every menu's section and text are unique within it; every route is reserved and every reserved key is a route |
| `EveryMenuLeadsSomewhereTheSiteAnswers` | A menu href is a reserved key or one of the three page tokens — the rule `ShellLinksTests` has today |
| `AMenuHasNoRepeatedHrefOrLabel` | Kept as it is, per section |
| `AnOwnerAndAVisitorGetTheirOwnMenus` | `IsFor`, the two Site menus, and the sign-in href's `returnUrl` |
| `ARecordIsOneFactPerLine` | `Text` keeps its five lines and its field names |
| `TheAccessSentenceFollowsTheAudience` | The four derived sentences, and the two notes |
| `AStaleReservedPathsSectionStopsTheHost` (integration) | The upgrade message names the key and the change |
| `ShellRoutesTests.EveryReservedKeyHasARecord…` | Deleted: it pinned two copies of one list, and there is now one |

## Risks

| Risk | Mitigation |
| --- | --- |
| A menu derived from a table loses an item someone wanted | The table is the same 17 links in the same order; `ShellLinksTests` and the menu assertions keep failing if one goes |
| `Access` prose drifts from what the assistant used to read | The two notes are explicit, and a test asserts the four sentences |
| A deployment with `Pages:ReservedPaths` stops on upgrade | That is the point of the refusal, and `deploy/operations.md` gains one line under the release notes |
| The catalogue becomes the god-object both files became | It holds facts about routes and nothing else: no markup, no `IsCurrent`, no policy. The renders still decide |

## Open questions

- **`ShellEntry` for a page link.** The token form (`Home`, `Editor`, `Help`) keeps the configured
  editor path out of the table, at the cost of one switch in `Links`. The alternative — two tables
  joined by an order key — trades that switch for a number to keep straight on every row. The token
  is the recommendation.
- **Where the table lives.** `Web/Shell/` rather than `Html/` or `Assistant/`, because three
  consumers read it and none of them owns it. The `Sdk` move that IDEA-030 proposes would put the
  values in `Sdk` and keep the table in `Web`.

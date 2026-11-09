<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Reading the page store from a page

Status: **proposed**. Written 2026-11-09. Review chose a capability reached through
`ctx.Features` with nothing added to `IContext`, **no shell gate** — the read is as public as the page
that calls it, and a page that wants privacy guards itself — a listing that returns every key the
store holds with the marks that say what each one is, and two seeded demo pages: a public listing and
a guarded one that shows source. The read is therefore proven by the site as well as by the test
suite.

## The short version

- **IDEA-015 lands as one capability.** `ctx.Features.GetRequired<IPageCatalog>()` reads the page
  store. **`IContext` gains nothing** (DEC-167).
- **Four reads and no writes:** `ListAsync(prefix)`, `ReadAsync(key)`, `RevisionsAsync(key)` and
  `ReadRevisionAsync(key, hash)`. No image, no symbol, no `PagePath`, no write member.
- **Public, like the page that calls it.** The capability is offered to every render and refuses
  nothing for being anonymous: pages are public unless their own code guards them (DEC-02), and this
  read is no exception. A page that wants its listing private puts the guard at the top of its own
  source, exactly as any private page does (DEC-168).
- **Everything, marked.** The listing returns every head the store holds — live, tombstoned, seeded,
  reserved — each carrying `IsDeleted`, `IsReserved` and `IsHtml`, so a view never re-derives the
  shell's own rules (DEC-169). The prefix is a subtree, not a string prefix.
- **Two caps.** `Pages:Read:MaxRowsPerList` (500) is enforced in the statement and refuses one row
  past the cap; `Pages:Read:MaxSourceCharsPerRender` (512 KiB) is shared by every layer of the chain
  and refuses rather than truncates (DEC-170).
- **A new read seam** over `IDbContextFactory<StorageDbContext>`, because `IPageStore` is scoped to a
  `StorageDbContext` and a page's task deliberately outlives the request (DEC-171).
- **The shell keeps its authority.** Saving, deleting, scrubbing, restoring seed and the import and
  export routes stay where they are: a page gets a bounded read and no second writer (DEC-172).
- **Two seeded demo pages.** A public `/recent` lists the store's keys, saved times and marks; a
  guarded `/pages` shows a page's source and history to the owner. The capability is used by the site
  as well as by the tests, and the difference between the two is one `ctx.User` guard.

## Goals

- A page can render the listing, history and revision views that are shell views today — the listing
  and history of [IDEA-017](../jobs/IDEA-017.md)'s cheap half — without meeting an image, a symbol, a
  `PagePath` or a write.
- The read is bounded the way every other page capability is: by the render's deadline, by
  abandonment, and by a cap that refuses rather than half-answers.
- **No gate the page cannot see.** The read is public wherever the page that calls it is, so privacy
  is one visible `ctx.User` check in the page's own source rather than an invisible rule that answers
  an error.
- The values a page binds against are `Sdk` records, and nothing from `Core` or `Web` crosses into
  the contract (DEC-88).
- A save made in another tab is visible to the read on the next render, with no cache of its own.

## Non-goals

- **A write.** `ctx.SaveAsync` is [IDEA-016](../jobs/IDEA-016.md), and writing a named key is
  [IDEA-017](../jobs/IDEA-017.md). Nothing here creates, saves, deletes, scrubs or restores.
- **Moving the editor's views.** The listing, history and revision views stay in `EditorEndpoint`.
  This plan is what makes moving them possible; moving them is IDEA-017's cheap half, and it is a
  separate change with its own markup, accessibility and browser tests.
- **A title, a size or a compile state in the listing.** A title is a compile or a render away, and
  the listing reads rows. [ARCH-14](archive/page-diagnostics-and-visibility.md) is where image state and
  staleness get their own column, in the shell.
- **A content menu.** The menu lists the site's tools; "pages by title" needs a title, which this
  plan deliberately does not read.
- **Import, export and the archive.** Those keep their own routes behind the `Editor` policy.
- **A content-type table for pages.** `IsHtml` reports what `Pages:ContentTypes` already decides
  (DEC-144); a page that needs the media type names the key and lets the shell decide.
- **Streaming a listing.** ARCH-13 stays open; this is a buffered answer like every other.

## What exists today

Checked against the code on 2026-11-09.

- **The contract is `IContext` plus `Features`.** `IContext` carries the request, path, user,
  abandonment, logging, the body, status/content type/header/`AllowScripts`, the two redirects, the
  antiforgery field and `Features`; three capabilities are registered today — `IPageStorage`,
  `IHttpFetch` and `IPageLayout` ([IContext.cs](../../src/uk.osric.asp.Sdk/IContext.cs),
  [architecture.md](../architecture.md#page-contract), DEC-142).
- **A capability is offered or absent, never `null`,** and the host decides; the Sdk cannot reach
  `Core` or `Web` (DEC-88, DEC-142).
- **A capability that does I/O is bound to the render.** `PageStorageScope` is the model: it is bound
  to the page's key, refuses a write on a `HEAD`, throws when the render was abandoned, and passes the
  deadline into the store call ([PageStorageScope.cs](../../src/uk.osric.asp.Web/Pages/PageStorageScope.cs),
  DEC-123, DEC-125).
- **The store is scoped and its rows are shell types.** `IPageStore` reads through a `StorageDbContext`
  that belongs to the request scope, and it answers with `PageHeadSummary`, `PageRevisionSummary` and
  `PageRevisionTemplate`, all of which name `PagePath` and so cannot cross into `Sdk`
  ([IPageStore.cs](../../src/uk.osric.asp.Web/Pages/IPageStore.cs), DEC-88).
- **Every head is listed, and a prefix is not a filter.** `ListPagesAsync` returns every head in
  ordinal order, tombstones included; there is no prefix argument and no row limit.
- **Live source only.** `GetSourceAsync` returns the template a live head points at and `null` for a
  tombstone; history is read through `ListRevisionsAsync` and `GetRevisionTemplateByHashAsync`, which
  are keyed by hash alone.
- **A state store already solves the lifetime problem.** `SqlitePageStateStore` is a singleton over
  `IDbContextFactory<StorageDbContext>`, because "a render that was abandoned can outlive the request
  scope, and a page's task must never touch a disposed one"
  ([SqlitePageStateStore.cs](../../src/uk.osric.asp.Web/Pages/SqlitePageStateStore.cs)).
- **`PageEnvironment` is one record of singletons** — reserved paths, layout, render options, content
  types, storage options, fetch options, the state store, the fetcher, metrics and antiforgery — and
  `PageChain.CreateLayer` builds each layer's `PageFeatures` from it
  ([PageEnvironment.cs](../../src/uk.osric.asp.Web/Pages/PageEnvironment.cs),
  [PageChain.cs](../../src/uk.osric.asp.Web/Pages/PageChain.cs)).
- **The chain already shares a body budget and a fetch budget** across the content and its layouts, so
  a layout cannot double an allowance (DEC-150).
- **The manual is `/help`, a seeded page,** and a change to what a page author can do updates it in
  the same change ([jobs/README.md](../jobs/README.md#help-for-authors); the decision entry's ID
  collides, see "Documentation and decisions").
- **The demo routes are free.** `/recent` and `/pages` are not reserved, are not shell routes, and no
  seed claims them ([ReservedPaths.cs](../../src/uk.osric.asp.Web/Pages/ReservedPaths.cs),
  [appsettings.json](../../src/uk.osric.asp.Web/appsettings.json)).

## What this touches

| Job or decision | What it already says | What this plan does |
| --- | --- | --- |
| [IDEA-015](../jobs/IDEA-015.md) | A page cannot read the page store; list heads, read a template, list revisions, read a revision's source; **the gate belongs to the shell**; caps and the deadline apply; decide the competing road | **Implements it**, as a capability rather than "on `IContext`", with the shell gate removed by review (DEC-168) and the marks added |
| [IDEA-017](../jobs/IDEA-017.md) | The cheap half is the listing, history and revision views; the full editor is later and the shell keeps the save | Unblocks the cheap half; the editor itself is untouched |
| [IDEA-016](../jobs/IDEA-016.md) | A page saves its own source; it needs an Sdk diagnostic value | Nothing here; the value shapes this plan adds are what a save's answer will sit beside |
| [IDEA-014](../jobs/IDEA-014.md) | A page cannot keep per-visitor state | Nothing here; a page that saves from a form and redirects still cannot say why |
| [Import and export](archive/page-import-export.md) (IDEA-009, closed) and [IDEA-020](../jobs/IDEA-020.md) | Import and export read and write the same store from the shell behind `Editor`; a reset-to-archive is not part of that flow | The competing road is settled: the shell keeps the write, the page gets a bounded read (DEC-172) |
| [IDEA-005](../jobs/IDEA-005.md) | Shared *types* between pages are deferred until a concrete helper needs them | Untouched: this adds values a page may name, not a declaration a second page can use |
| DEC-88 | `Sdk` is the only product assembly a page may name | The capability and its three values live in `Sdk`; the mapping from `Core`/`Web` rows happens in `PageCatalogScope`, the capability's implementation |
| DEC-142, DEC-143 | A capability is registered or absent, never `null`; the host decides what a render offers | Extended: this one is always registered, and it never refuses (DEC-168) |
| DEC-123 | I/O on a capability is a `Task`, takes no token, and is linked to the render's deadline | Followed: the scope holds the deadline and passes it to the store per call |
| DEC-125, DEC-126 | `IPageStorage` is bound to the page's key; a `HEAD` reads but never writes | Followed in shape, not in scope: this handle is not bound to a key, and a read is allowed on `HEAD` |
| DEC-02, DEC-133 | Pages are public unless their own code guards them; the shell supplies identity | Unchanged: the read is as public as the page that calls it, and a page's own `ctx.User` check is the only gate (DEC-168) |
| DEC-144, DEC-149 | A key's extension decides the content type; an HTML page is wrapped in the layout | `IsHtml` is the same rule, reported rather than re-derived; both demo pages are fragments and are wrapped |
| DEC-26 | `RuntimeId` includes the Sdk module version id | One deliberate bump: the interface and its values invalidate every stored image, which recompiles on its next request |
| DEC-150 | One render slot, deadline, fetch budget and body budget for a chain | One more shared budget, on the same terms |
| DEC-31 | A page owns its whole response and is buffered | Unchanged; the demo pages write fragments into the layout |
| DEC-110 | Every shell view is asserted for accessibility | The demo pages are stored pages rather than shell views, so their markup is asserted in the integration suite instead |

## Design

### D1 — One capability, four reads, nothing on `IContext`

`Sdk/IPageCatalog.cs`:

```csharp
public interface IPageCatalog {
    Task<IReadOnlyList<PageEntry>> ListAsync(string prefix);
    Task<PageSource?> ReadAsync(string key);
    Task<IReadOnlyList<PageRevisionInfo>> RevisionsAsync(string key);
    Task<PageSource?> ReadRevisionAsync(string key, string hash);
}
```

Three values, because a page may name neither `Core` nor `Web` (DEC-88):

```csharp
public readonly record struct PageEntry(
    string Key, DateTimeOffset SavedAt, string Hash, bool IsDeleted, bool IsReserved, bool IsHtml);

public sealed record PageSource(
    string Key, string Hash, DateTimeOffset SavedAt, string Source);

public readonly record struct PageRevisionInfo(string Hash, DateTimeOffset SavedAt, bool IsCurrent);
```

- `ListAsync` lists heads: the key, the head's revision time and hash, and the three marks of D3.
  The time and hash ride along because the join is one statement and because a "recent changes" view
  would otherwise read every page's history to find out when each one last changed — the one unlock
  [IDEA-015](../jobs/IDEA-015.md) names that a key-only listing cannot serve cheaply.
- `ReadAsync` reads a live page's template, or `null` when no live page has the key. A tombstone has
  no source: it is listed as deleted and read as absent.
- `RevisionsAsync` lists a key's history newest first, including the revisions of a tombstoned page —
  the history survives the delete by design. An unknown key is an empty list, not an error.
- `ReadRevisionAsync` reads one revision's template, or `null` when that key has no revision with that
  hash. Both handles are required, so a hash remembered from one page cannot be spent on another.
- Every member returns a `Task` and takes no `CancellationToken`, as `IPageStorage` and `IHttpFetch`
  do: the render owns `Aborted` and the deadline, and the scope links them per call (DEC-123).
- Nothing is added to `IContext`. A page that never reads the store never meets the capability, which
  is the whole reason `Features` exists.

`DateTimeOffset` is already admitted by the page API policy, and the three values need no policy
change ([PageApiPolicy.cs](../../src/uk.osric.asp.Core/Compilation/PageApiPolicy.cs)).

### D2 — Public by default, like the page that calls it

`PageCatalogScope` is the only implementation. It is constructed in `PageChain.CreateLayer` with the
layer's key, the read store, the options, the chain's read budget, the reserved paths and the content
types, and registered on **every** layer's `PageFeatures`, so a layout may read the store as well as a
page may.

**There is no gate.** The capability answers for whoever the calling page answers for: an anonymous
visitor gets the same answers a signed-in one does, and privacy is the page's own business. That is
DEC-02 applied without an exception — a page is public unless its own code checks `ctx.User` — and the
alternative, a shell rule the page cannot see, is worse in three ways: it hides a second gate behind
the visible one, its failure is a `500` where the page's own guard would be a redirect, and it makes
the same page behave differently for two visitors with no line of its source to explain it.

**IDEA-015 point 3 said the opposite** — "the read should be refused for an anonymous visitor whatever
the page's own code does; otherwise one careless public page is a source dump of every other page".
Review overruled it: the store's content is the site's content, a public page's rendered output is
already public, and the site's model for what is private is the guard the page writes. The cost is
stated plainly in "Risks": any page that reads the store can publish every template, including one
the owner believed private, and a public listing says what exists and when it changed.

So the checks a call makes are only about the call itself:

1. **The arguments.** A prefix that cannot name anything, or a hash that is empty, is an
   `ArgumentException`, because that is a page bug and the author should see it as one.
2. **The render.** Abandoned ⇒ `InvalidOperationException`; the deadline is passed to the store, and
   its cancellation surfaces as the render's timeout, exactly as `PageStorageScope` does it.

A page that wants its listing private writes the ordinary guard — `if (ctx.User is not { } user)
{ ctx.RedirectToLogin(); return; }` — at the top of its own source, and `/help` shows it there. The
guarded demo page `/pages` is the working example (D6).

### D3 — Everything, marked

The listing returns every head: live pages, tombstones, the seeded assets (`/site.css`, `/editor.js`,
`/_layout`), and any reserved key that a direct write or an import left in the store. Filtering is the
page's business, and the marks are what make that possible without re-deriving shell rules:

- `IsDeleted` is the head's own flag: a tombstone is listed, and reads as absent.
- `IsReserved` is `ReservedPaths.IsReserved` — the same prefix rule the shell refuses pages with, so a
  view cannot disagree with routing about what is reserved.
- `IsHtml` is `PageContentTypes.ForPath(key)` starting with `text/html`, which is the rule that decides
  whether a page is wrapped (DEC-144, DEC-149). A view can then offer "these are documents, these are
  assets" without owning the content-type table.

**The prefix is a subtree, not a string prefix.** `ListAsync("/blog")` lists `/blog` and `/blog/…`
and never `/blogroll`: a key matches when it equals the prefix or starts with the prefix plus `/`.
`""` and `"/"` mean the whole store, and any other prefix is normalised with
`PagePath.Normalize`, so `/Blog/` and `/blog` are the same request. A prefix whose normalised key is
not addressable (`/a?b`, `/a#b`) is an `ArgumentException`.

The result is ordered by key. The order is the database's, which for these keys is ordinal text
order; the shape is fixed in the statement rather than sorted afterwards, because the row cap needs
the order to be decided before the limit.

### D4 — Caps: one row limit in SQL, one source budget across the chain

`Pages:Read` in `appsettings.json`, read by a new `PageReadOptions` in the style of
`PageStorageOptions` and `PageFetchOptions`, with every value clamped so a configuration mistake
cannot switch a cap off:

| Setting | Default | What it bounds |
| --- | --- | --- |
| `MaxRowsPerList` | 500 | Rows one `ListAsync` may return. Clamped to 1–10000 |
| `MaxSourceCharsPerRender` | 524288 (512 KiB) | Characters of template one render may pull in, across every layer |

- **Rows.** The store selects at most `MaxRowsPerList + 1` rows. One row past the cap is a refusal
  (`InvalidOperationException`, naming the cap and suggesting a prefix), not a silently truncated
  list: a view that shows 500 of 900 pages and says nothing is worse than one that says why. The
  refusal is the body cap's shape, which is the cap a page author already meets.
- **Source.** A new `ReadBudget`, built once per request beside the body and fetch budgets and shared
  by every layer of the chain — the same reasoning as `RenderFetchBudget`: a layout must not double
  the allowance by being a second layer. `ReadAsync` and `ReadRevisionAsync` charge the template's
  length before returning it; a read that would go over is refused rather than clamped, because half
  a template is a misleading answer. Listing charges nothing: rows are metadata, and the body cap
  bounds what a page can write out of them.
- **Why 512 KiB.** `/help` is about 15 KiB and nothing shipped is larger than about 17 KiB, so the
  default is room for a listing plus several pages and their revisions. An editor form value is capped
  only by the form parser's own limit (4 MiB by default), so a very large stored template can exceed
  the read budget; the message says so, and an operator who wants to show one can raise the setting.

### D5 — The read owns its own context, because a page's task outlives the request

`IPageStore` is scoped to the request's `StorageDbContext`, and `PageEnvironment` is a record of
singletons (`Program.cs`), so a capability cannot reach it. The alternative — resolving a scope per
render — puts a service locator in the render path and still hands the page a context that can be
disposed under it when the request ends.

So the read gets its own seam, exactly as a page's state did:

- `IPageReadStore` is the path-parameterised, read-only seam: `ListAsync(prefix, limit, ct)`,
  `GetSourceAsync(key, ct)`, `ListRevisionsAsync(key, ct)`, `GetRevisionSourceAsync(key, hash, ct)`.
  It answers with small `Web` records — a key string, a saved time, a hash, a deleted flag, a
  template — and no `PagePath`, because normalising a key and deciding the marks is the capability's
  job and a page should never meet a core type.
- `SqlitePageReadStore` is the EF Core implementation, a singleton over
  `IDbContextFactory<StorageDbContext>`: every call creates and disposes its own context, for
  `SqlitePageStateStore`'s reason, quoted in "What exists today".
- Prefix matching is one escaped `LIKE` (`\`, `%` and `_` escaped, `ESCAPE '\'`) plus an equality arm
  for the prefix itself, with the limit in the same statement. A key may legally contain `%` or `_`
  (`/100%` is a page), so the escape is not decorative and has its own test.
- It is read-only by construction: no `SaveChangesAsync` call site, no `Database.BeginTransaction`,
  and nothing that names `PageTemplate`, `PageImage` or `PageImageBuilder`.

`PageEnvironment` gains `ReadStore` and `Read`, both singletons, and the record keeps its "every
member is a singleton" rule.

### D6 — Two demo pages: `/recent` and `/pages`

Both are seeds, both are fragments the default layout wraps, both set their title, and the only
difference between them is one line of source:

- **`/recent` is public.** No guard: it lists the store — the key, when it was last saved, and the
  marks as text ("deleted", "reserved", "asset"), most recently saved first — and each key links to
  `/pages?page=<key>`. It reads keys and heads and never a template, so it publishes what exists and
  when it changed, and not one line of source. It is the worked example of `ListAsync` and the
  smallest honest public changelog.
- **`/pages` guards itself** with the ordinary private-page guard — `if (ctx.User is not { } user)
  { ctx.RedirectToLogin(); return; }` — and then shows a page's source and history: `?page=/blog`
  gives its history (hash, saved time, which one is current) and its current source in a `<pre>`;
  `?rev=<hash>` as well gives that revision's source. Everything is written with `ctx.Write`, because
  page source is exactly what a demo of this capability must escape, and the page says so in a line of
  its own text.
- **The pair is the argument for D2.** Same capability, same visitor, one guard between a public list
  and a private source view: that is the site's whole privacy model, and it is visible in two seeded
  files rather than in a rule the shell applies where nobody can read it.
- **Discovery.** `/help` names both, in the same change, as the help-for-authors rule asks; `/recent`
  links to `/pages` and `/pages` back. Neither goes in the site menu: the menu is chrome and these are
  content.
- **They are seeds,** so they can be edited, restored and deleted like any other page, and an existing
  installation gains both on its next start because they are new keys.
- **Both call themselves demonstrations** in their own text and name
  [IDEA-017](../jobs/IDEA-017.md), so nobody mistakes them for the editor.

Why pages rather than only tests: the capability's interesting failures are the ones that only show up
with a real layout around it, a real render deadline and a real store — a listing that reads another
page's history, a source escaping bug, a cap message a person sees. The two pages make those ordinary,
and the unguarded one makes the public case one request away.

### D7 — What this is not, and the competing road

[IDEA-015](../jobs/IDEA-015.md) point 6 asks for a decision: the import and export routes read and
write the same store from the shell behind the `Editor` policy, and a page read is a second route to
the same authority.

**Settled:** the shell keeps every write and every bulk route. Import, export, save, delete, scrub,
restore and the assistant's bounded tools keep their own seams (`IPageStore`, `PageEditor`,
`PageToolExecutor`); the page read is read-only, bounded, cannot reach an archive, and is as public as
the page that calls it. Two readers are a smaller risk than two writers, and the read is the one a
view needs. A page that wants to write is IDEA-016, which reuses `PageEditor.SaveAsync` rather than
opening a second writer.

## Considered and rejected

**`ReadAsync` and friends on `IContext`.** IDEA-015's original sketch. Rejected by review: `IContext`
is the contract every stored page compiles against, and a page that never reads the store should never
meet it (DEC-142, DEC-167).

**A shell gate on the read** ([IDEA-015](../jobs/IDEA-015.md) point 3, "refused for an anonymous
visitor whatever the page's own code does"). Rejected by review, in both of its shapes: an absent
capability for an anonymous render, and a registered one that refuses. Each puts the privacy rule in
the shell, where a page's source cannot show it and a page author cannot change it; each makes the
same page behave differently for two visitors for a reason nothing in the page says; and the refusing
shape answers `500` where the page's own guard would answer a redirect. The read is as public as the
page that calls it (D2), and the two demo pages are the same capability with and without a guard.

**Anonymous reads that return nothing.** Rejected: an empty list looks like an empty store, so nobody
would ever tell a page that works from one that hides everything.

**Truncating a list or clamping a template.** Rejected: a view that shows some of the store and says
nothing is worse than a refusal, and half a template compiles into nothing an author can use.

**Returning the store's own rows.** `PageHeadSummary` and `PageRevisionTemplate` name `PagePath`,
which lives in `Core`, and `PageRevisionSummary` carries the image status the editor shows and a view
has no use for; a page can name none of them (DEC-88).

**Reusing `IPageStore` through `PageEnvironment`.** Rejected: it is scoped, and resolving a scope per
render is a service locator that can still hand a page a disposed context (D5).

**Filtering reserved and non-HTML keys in the shell.** Rejected by review: the listing marks them
instead, which keeps one rule and lets a view decide (D3).

**Reading a page's title into the listing.** Rejected: a title is a render or a compile away, and a
listing that compiles every page is the store reading its own images. A cheap title would be a shared
declaration ([IDEA-005](../jobs/IDEA-005.md)) or a new value, and neither belongs in a read.

**One demo page rather than two.** Rejected by review: a listing and a source view want different
audiences, and shipping them as a public page and a guarded one answers the gate question in two files
instead of a paragraph.

## Components

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Sdk/IPageCatalog.cs` | **new** — the four reads, reached through `Features` |
| `src/uk.osric.asp.Sdk/PageEntry.cs`, `PageSource.cs`, `PageRevisionInfo.cs` | **new** — the three values a page may name |
| `src/uk.osric.asp.Sdk/IContext.cs` | **unchanged** |
| `src/uk.osric.asp.Core/Compilation/PageApiPolicy.cs` | **unchanged** — `System.DateTimeOffset` is already admitted |
| `src/uk.osric.asp.Web/Pages/PageCatalogScope.cs` | **new** — the implementation: arguments, marks, deadline, abandonment, budget |
| `src/uk.osric.asp.Web/Pages/ReadBudget.cs` | **new** — the chain's shared source allowance |
| `src/uk.osric.asp.Web/Pages/PageReadOptions.cs` | **new** — `Pages:Read`, clamped like its neighbours |
| `src/uk.osric.asp.Web/Pages/IPageReadStore.cs`, `SqlitePageReadStore.cs` | **new** — the read-only seam and its EF Core implementation |
| `src/uk.osric.asp.Web/Pages/PageEnvironment.cs` | **changed** — `ReadStore` and `Read` |
| `src/uk.osric.asp.Web/Pages/PageChain.cs` | **changed** — builds the layer's `PageCatalogScope` and registers it on every layer; takes the request's `ReadBudget` |
| `src/uk.osric.asp.Web/Pages/PageEndpoint.cs` | **changed** — one `ReadBudget` per request, beside the body and fetch budgets |
| `src/uk.osric.asp.Web/Program.cs` | **changed** — register `PageReadOptions`, `IPageReadStore` and the two `PageEnvironment` members |
| `src/uk.osric.asp.Web/appsettings.json` | **changed** — a `Pages:Read` section |
| `src/uk.osric.asp.Web/Pages/Seeds/recent` | **new** — the public listing |
| `src/uk.osric.asp.Web/Pages/Seeds/pages` | **new** — the guarded source and history view |
| `src/uk.osric.asp.Web/Pages/Seeds/help` | **changed** — the capability, the guard a private page uses, and the two demo pages |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | **changed** — `page-author-v10`: the capability exists, it reads, and it is as public as the page that calls it |
| `test/uk.osric.asp.Web.Tests/PageEndpointTests.cs`, `ShellViews.cs`, `PageHarness.cs` | **changed** — the new `PageEnvironment` members, with a fake read store |
| `test/uk.osric.asp.Web.Tests/FakePageReadStore.cs` | **new** — an in-memory seam for the capability tests |
| `docs/architecture.md`, `docs/decisions.md`, `docs/jobs/`, `docs/testing.md` | **changed** — see "Documentation and decisions" |

## Tests

**`Web.Tests` — `PageCatalogScopeTests`** (new; the heart of the plan)

- `AnAnonymousVisitorCanReadTheStore`, `ASignedInVisitorReadsTheSameThing`
- `ALivePageIsReadWithItsHashAndItsSavedTime`, `AMissingPageReadsAsNothing`
- `ATombstonedPageIsListedAndReadsAsNothing`
- `AReservedKeyIsMarkedReserved`, `AnAssetIsMarkedAsNotHtml`, `ADocumentIsMarkedAsHtml`
- `TheListingIsOrderedByKey`
- `APrefixListsItsSubtreeAndNotItsNeighbours` — `/blog` lists `/blog` and `/blog/x`, never `/blogroll`
- `TheRootPrefixListsEverything`, `APrefixIsNormalised`
- `APrefixThatIsNotAddressableIsRefused`
- `AnUnknownKeyHasNoRevisions`, `AHistoryListsNewestFirstAndMarksTheCurrentRevision`
- `ATombstonedPagesHistoryIsStillReadable`
- `ARevisionIsReadByItsKeyAndHash`, `AHashFromOnePageIsNotReadUnderAnother`
- `OneRowPastTheCapIsRefused`, `TheReadBudgetIsSharedByTheChain`, `ASourcePastTheBudgetIsRefused`
- `AnAbandonedRenderCannotRead`, `TheDeadlineCancelsARead`

**`Web.Tests` — `SqlitePageReadStoreTests`** (new, over `DatabaseTemplate`)

- `ALivePageIsReadAndATombstoneIsNot`
- `TheListingIncludesTombstonesAndSeeds`
- `TheLimitIsInTheStatement` — a store with more rows than the limit returns the limit plus one
- `APrefixContainingAWildcardIsNotAWildcard` — a page at `/100%` and one at `/100x` are told apart
- `TheHeadRevisionTimeAndHashAreTheCurrentOnes`
- `ARevisionsSourceIsReadByKeyAndHash`
- `TheStoreNeverWrites` — the head and revision rows are unchanged after a read

**`Web.Tests` — `PageReadOptionsTests`** (new)

- `TheDefaultsAreTheDocumentedOnes`
- `AMissingSectionUsesTheDefaults`
- `ARowLimitBelowOneIsClamped`, `ASourceBudgetBelowTheFloorIsClamped`

**`Web.Tests` — `PageLayoutChainTests`, `PageFeaturesTests`** (changed)

- `EveryLayerIsOfferedTheCatalog`, `ALayoutMayReadTheStore`, `TheChainSharesOneReadBudget`

**`Web.IntegrationTests` — `PageCatalogTests`** (new)

- `ThePublicListingNamesEveryPageAndWhenItChanged`
- `ThePublicListingIsServedToAnAnonymousVisitor`
- `ThePublicListingNeverShowsSource`
- `TheGuardedPageSendsAnAnonymousVisitorToSignIn`
- `TheGuardedPageListsTheStoreForItsOwner`
- `TheGuardedPageShowsAPagesHistoryAndSource`
- `TheGuardedPageShowsOneRevisionByHash`
- `APageSourceIsEscapedInTheGuardedPage` — a page whose source contains `<script>` is shown as text
- `BothDemoPagesAreWrappedInTheStoredLayout`
- `SavingAPageChangesWhatTheDemoPagesShow`

**`Web.Tests` — `PageAssetSeederTests`, `PageAssetTests`** (changed)

- The seed list gains `/recent` and `/pages`; the parametrised seed tests pick them up
- `ThePublicListingIsAReachableFragment`, `TheGuardedPageGuardsItself` — the guard is asserted against
  the shipped template, so a later edit that drops it fails a test rather than a review

**Accessibility.** `AccessibilityTests` renders shell views, so it does not reach `/recent` or
`/pages`. Both pages' markup is asserted through `MarkupReader` in `PageCatalogTests` — one `h1`, one
table with scoped headers and no cell without a heading, and the source view's named `<pre>` — which
is the same rule set applied to the two views that are content.

**Deliberately not tested.** Browser behaviour: neither page has script, so `Web.BrowserTests` gains
nothing, and [IDEA-001](../jobs/IDEA-001.md) covers the editor's own scripts.

## Phases

Each phase ends with `make test` green and `make format-check` clean.

1. **The seam.** `IPageReadStore`, `SqlitePageReadStore`, `PageReadOptions`, `Pages:Read`, the
   `PageEnvironment` members and their registration. No page can see it yet; the store tests are the
   whole of it.
2. **The capability.** The three `Sdk` values, `PageCatalogScope`, `ReadBudget`, the `PageChain` and
   `PageEndpoint` wiring, the marks, the caps, the deadline and abandonment. **This is the risky
   phase** and it is worth landing on its own, with the unit tests and nothing else changed.
3. **The demo pages.** `Seeds/recent`, `Seeds/pages`, `/help`, the integration tests, the seed-list
   tests.
4. **Teach and record.** `PageAuthorPrompt` to `page-author-v10`; `architecture.md`; decisions; the
   jobs (IDEA-015 closed, IDEA-017's note updated); `testing.md`.

Phase 2 depends on phase 1, phase 3 on phase 2. Phases 1 and 2 are one sitting; phase 3 is another.

## Documentation and decisions

- **DEC-167**: the page store read is a capability, `IPageCatalog` — `ListAsync`, `ReadAsync`,
  `RevisionsAsync`, `ReadRevisionAsync` — reached through `IContext.Features`, with `PageEntry`,
  `PageSource` and `PageRevisionInfo` as the values a page may name. `IContext` and `PageResponse` are
  unchanged, and the capability reads and never writes.
- **DEC-168**: the read is public by default. There is no shell gate: the capability answers an
  anonymous render exactly as it answers a signed-in one, and a page that wants its listing or its
  source private puts the ordinary `ctx.User` guard at the top of its own source (DEC-02). This
  overrules [IDEA-015](../jobs/IDEA-015.md) point 3, whose objection — a careless public page becomes a
  source dump of every other page — is answered by saying so in `/help`, in the plan's risks, and by
  the guarded demo page. A refusal inside a capability is rejected: it is an invisible second gate
  whose failure is a `500` rather than the redirect the page's own guard gives.
- **DEC-169**: the listing returns every head the store holds, each marked `IsDeleted` (the head's
  flag), `IsReserved` (`ReservedPaths`) and `IsHtml` (`Pages:ContentTypes`), and a prefix selects a
  subtree — the key itself and keys beneath it — never a string prefix. Ordering is by key.
- **DEC-170**: `Pages:Read:MaxRowsPerList` (500, clamped 1–10000) is selected for as a limit of one
  more than the cap and refused when the extra row arrives; `Pages:Read:MaxSourceCharsPerRender`
  (512 KiB) is one `ReadBudget` per request, shared by every layer of the chain, charged by
  `ReadAsync` and `ReadRevisionAsync` and refused rather than clamped.
- **DEC-171**: pages read the store through `IPageReadStore`, a read-only singleton over
  `IDbContextFactory<StorageDbContext>`, because `IPageStore` is scoped and a page's task outlives the
  request; it answers with `Web` records and never with a `PagePath`, and the mapping to `Sdk` values
  happens in the capability.
- **DEC-172**: the shell keeps every write of the page store — save, delete, scrub, restore, import
  and export, and the assistant's bounded tools — and a page gets a bounded read; there is no second
  writer, and IDEA-016 will reuse `PageEditor.SaveAsync` rather than open one.
- `docs/architecture.md`: "Page contract" lists four capabilities, and a short paragraph under "Source
  storage" says what a page may read, what bounds it, and that it is as public as the page that calls
  it.
- `docs/testing.md`: one sentence naming `PageCatalogScopeTests`, `SqlitePageReadStoreTests` and the
  integration suite.
- `docs/jobs/IDEA-015.md`: deleted; its row leaves the table and it joins "Closed" with a pointer
  here, and the Closed entry says point 3 (a shell gate) was overruled in favour of DEC-168, so the
  record does not read as though it was quietly dropped. `docs/jobs/README.md`: IDEA-017's note says
  the read exists and its cheap half is unblocked.
- **A collision to fix while in the file.** `docs/decisions.md` uses **DEC-160 twice** — the budget
  entry of 2026-11-08 (which `plans/archive/assistant-live-budgets.md` cites) and the author-manual
  entry added the same day in `eca173d`. The author-manual row is the later one and is cited nowhere
  by ID — `jobs/README.md` states the "Help for authors" rule without one — so it should be
  renumbered to the next free ID after the plans that have already claimed numbers — **DEC-173**,
  since `plans/archive/page-diagnostics-and-visibility.md` claims up to DEC-166 and this plan takes
  DEC-167–DEC-172. **DEC-132 is also used twice**, and is left alone here: it is older and cited by
  two plans, so it is a decision of its own rather than a tidy-up.
- `docs/glossary.md`: no new term. The capability reads terms the glossary already defines — key,
  head, revision, tombstone, seed — and "catalog" would be a type name rather than vocabulary.
- `deploy/README.md`: nothing to deploy; the demo pages are seeds and their settings have defaults.

## Risks

- **Every stored template is public through any page that reads it.** There is no gate, so a page
  that calls `ReadAsync` or `ReadRevisionAsync` publishes the source it reads to whoever can reach
  that page — including a template whose own `ctx.User` guard hides its output, and including the
  history of a deleted page until it is scrubbed. Pages already run with the site's identity
  (DEC-40), so this is a new way to publish something by accident rather than a new boundary. The
  mitigations are deliberate and documentary: `/help` says it in the capability's paragraph, the
  guarded `/pages` is the worked example, and the guard is one line in the page's own source. There
  is no "this template is secret" setting, and the plan does not invent one.
- **A public listing says what exists and when it changed.** `/recent` publishes every key, its saved
  time and whether it is deleted, reserved or an asset, to anonymous visitors. For a shop-window site
  that is the point, but it is a default the seed ships with, so the owner should know: deleting or
  editing `/recent` is one action on the page list, and the capability is unaffected.
- **An anonymous visitor can make the site work.** With no gate, anyone who reaches a page that reads
  the store spends the store's queries and the render's source budget. That is why the caps exist: one
  statement per list, a limit inside it, a source budget the whole chain shares, one render slot and
  one deadline. Nothing here is unbounded, but a public reader is the reason to keep the defaults
  rather than raise them.
- **Two readers of one store.** The shell reads through `IPageStore`/`PageEditor` and pages read
  through `IPageReadStore`. The read seam is read-only, its query rules live in one statement, and a
  future write must go through `PageEditor` (DEC-172); the risk is drift in what a listing shows
  versus what the editor lists, which the integration tests compare through the same saves.
- **A big store meets a small cap.** 500 rows is a wall in a site with more pages than that, and the
  refusal is the first thing the owner sees after a bulk import. The message says to use a prefix, and
  the setting is there to raise.
- **A large template cannot be read.** 512 KiB is generous for this site and a hard stop for a
  pathological one; the message names the setting.
- **The demo pages become content.** Both are seeds, so an installation keeps them and the owner may
  edit or delete them; if `/recent` goes, `/help`'s link points at a missing page, and if `/pages`'s
  guard is edited out, the store's source becomes public. A seeder test pins the guard in the shipped
  template — the seed, not the row, because the owner is allowed to change the row and that is exactly
  what the guard is for.
- **One more contract bump.** Adding an interface and three values changes the Sdk module version id,
  so every stored image recompiles on its next request (DEC-26). Expected, and the same cost the
  layout and the page features paid.
- **A layout that reads the store.** Offered on purpose, bounded by the same chain budget, and worth
  watching: a layout that lists the store makes every page pay for it.

## Settled at review

| Question | Answer |
| --- | --- |
| A capability or more `IContext` | A capability, `IPageCatalog`, through `ctx.Features`; `IContext` unchanged (DEC-167) |
| Is the read gated | No. It answers an anonymous render exactly as a signed-in one, and a page that wants privacy guards itself (DEC-168); IDEA-015 point 3 is overruled |
| What a listing returns | Every head, marked deleted, reserved and HTML (DEC-169) |
| Row and source caps | `Pages:Read`, 500 rows and 512 KiB per render, both refusing (DEC-170) |
| Where the read runs | A read-only singleton over the context factory (DEC-171) |
| The competing road | The shell keeps every write; pages get a bounded read (DEC-172) |
| How far this job goes | The capability and two seeded demo pages; moving the editor's views is IDEA-017's cheap half |
| Demo pages or tests only | Both, and two: a public `/recent` listing and a guarded `/pages` that shows source, so the capability is proven through the real host and the gate question is answered in two files |

**Deferred, so they are not rediscovered.** A title (or an image state) in the listing, which needs a
compile or [ARCH-14](archive/page-diagnostics-and-visibility.md)'s column; shared types between pages
([IDEA-005](../jobs/IDEA-005.md)); and moving the editor's listing, history and revisions into pages,
which is [IDEA-017](../jobs/IDEA-017.md)'s cheap half and the next job this one unblocks.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# One seed page, and the rows a shell path keeps

| | |
| --- | --- |
| Status | Merged 2026-11-19 — built on *seed-page* and merged into trunk with `make check` green. |
| Size | Large — one shell endpoint replacing two, two storage reads and one storage write, one startup scan, one route and menu row, the author manual, the assistant prompt, the docs and the tests. No `Core`, `Runtime`, `Sdk`, configuration or migration change. |
| Area | `Web` shell and page storage, `/help`, the assistant prompt, docs and tests |
| Spike | None. Every claim about what exists was read off `trunk` on 2026-11-19: `ApplySeedsEndpoint`, `RestoreEndpoint`, `PageSeedInstaller`, `SeedComparison`, `SeedVerdictWords`, `PageSeeds`, `PageEditor`/`PageSaver`, `PageWritesScope`, `ScrubEndpoint`, `IPageStore`/`SqlitePageStore`, `PageContentHashRepair`, `AdminPage`, `ShellCatalogue`, `Program.cs`, `DiffRenderer`, the tests, `/help`, the docs and `deploy/operations.md`. |
| Backlog | None for this plan. The owner's follow-up idea, [`seed-auto-apply-at-startup`](../../backlog/closed.md), was recorded separately on 2026-11-19 and landed as [seed auto apply](seed-auto-apply-at-startup.md). |
| Decisions | Taken at the merge: `shell-seeds-are-chosen-row-by-row` and `shell-reserved-rows-are-scrubbed-not-served`; `shell-the-seed-apply-is-a-route`, `shell-the-admin-tools`, `shell-reserved-keys-are-an-invariant` and `pages-scrubbing-is-not-editing` were edited in place; the `/admin/restore` mentions in `pages-any-page-may-write-the-store` and `pages-delete-install-and-restore` became `/admin/seeds`. No name was retired. |

The two names this plan proposed are in [the decisions record](../../decisions.md):
`shell-seeds-are-chosen-row-by-row` and `shell-reserved-rows-are-scrubbed-not-served`.

## The short version

**The seed page.** `/admin/restore` and `/admin/seed-apply` become one shell page at `/admin/seeds`.
It lists every seed this release ships with its verdict, and a tickbox for each row that can be
written. The safe rows are ticked; the rows that would replace the owner's source are not. A `POST`
writes exactly the ticked rows — it does not stop at a drifted row, because an unticked row is a
choice, not a failure. Every write is an ordinary seed save: the bundled source is compiled and
stored as a new revision, so the source it replaces stays in the page's history. A row that changed
after the page loaded is refused, and the rest of the run continues. Each row that would change links
to `?diff=<key>`, a shell-rendered diff that works when `/pages` itself does not. The two old routes
are removed and answer `404`.

**The dead rows.** The same page carries a second section: every key the shell answers that still has
rows in the page store. A stored page at a shell path is unreachable — the shell answers the key, not
the page — and it is what is left when a seed becomes a shell file, as `admin.css`, `admin.js`,
`status.css`, `reference.css` and `rocket.svg` did. The section lists each key with its head and
revision count, offers a tick per row, and scrubs the ticked keys after the owner types `scrub`. The
POST proves each key is still reserved before it deletes anything, so a crafted form cannot remove a
normal page. The scrub removes the head, every revision and the page's state in one transaction, with
the same secure delete and WAL checkpoint the editor's scrub uses. A startup scan logs a warning when
the condition exists, and a test fails the build if the seed pack ever ships a reserved key.

The owner's seed scenario is the design's first test: `/pages` is broken, and `/about` and `/help` are
edits to keep. The owner opens `/admin/seeds`, ticks `/pages` and whichever of its assets are broken,
posts, and `/pages` serves again. Nothing else is written. The old `/admin/restore` would have
overwritten every edit; the old `/admin/seed-apply` would have stopped at the first edited row and
written nothing after it.

The shell-path scenario is the second: an installation carries a seeded row at `/admin.css` from
before the shell took the key. The static file answers the URL, `seeds list` calls the row a retired
seed, and the editor cannot delete it because `PageWritesScope.Resolve` refuses a reserved key and
`ScrubEndpoint` requires a tombstone. `/admin/seeds` lists it under "Stored pages at shell paths",
and a tick and the typed word remove it.

## Goals

1. One page lists every seed and every action, so the owner can see the whole pack and choose.
2. A drifted page is unticked by default rather than a stop; the rows after it still run.
3. A write that would replace the owner's source is a deliberate tick, labelled as what it does.
4. A row that changed between the preview and the `POST` is refused, and the other ticked rows still
   run.
5. The page answers when `/pages`, `/editor.css`, `/editor.js` or `/_layout` is broken: it is the
   shell's own route and loads no seeded stylesheet or script.
6. The owner can see what a write would change without opening the possibly broken editor.
7. One route, one menu entry, one record; the old routes and every live citation go in the same
   change.
8. Every stored row at a key the shell answers is found and shown, whether it is live, a tombstone,
   a revision with no head, or page state with nothing else.
9. The repair is explicit and limited to reserved keys: a tick selects the rows, a typed word
   confirms the batch, and the `POST` re-checks the reserved list before every delete.
10. The condition is visible when it recurs: a startup line names the keys, and a build test stops a
    seed pack from creating one.

## Non-goals

- **A page editor.** No source editing, revision restore, delete or scrub of a normal page on this
  page; those stay in the editor and `/admin/scrub`.
- **An import, export or reset.** It writes only the seeds the release ships, and only the rows the
  owner ticks. A page the owner made at a normal key is never touched.
- **A new seed comparison.** `SeedComparison` and the seven verdicts are unchanged; the page reports
  the host's existing verdicts.
- **An automatic apply at startup or on a deploy.** The page is the owner's action, as both routes
  are today.
- **An automatic scrub.** A reserved-key row is deleted only by the owner's tick and typed word; the
  startup scan reports and never writes. A key can stop being reserved in a later release, and the
  site does not get to decide that the history it kept is worthless.
- **Retired seed rows as a separate feature.** A row at a shell path is shown because it is a stored
  page at a reserved key, not because `seeds list` calls it retired.
- **A general orphan cleanup.** The scan covers keys the shell answers. A revision or state row at a
  normal key with no head is a different corruption and is not removed here.
- **A bulk "restore everything" button.** The safe default is the bulk action; the destructive rows
  are ticks the owner makes one at a time.
- **JavaScript or a select-all control.** The forms are server-rendered; the defaults are attributes,
  not script.
- **An inline diff on the main page.** The seed diff is a separate `?diff=<key>` view on the same
  route. The repair preview shows the key, head and revision count; an owner who wants the bytes
  before a scrub exports the archive first.
- **A page API change.** No `IContext` member, capability, layout slot or response rule changes; a
  page cannot call this view, and `/help` changes for the route and the words only.
- **A command-line repair.** The page is a shell route, so it answers when the page pipeline is what
  failed. A database that will not open is the `users`/`seeds` command's problem, not this
  condition's.

## What exists today

| Piece | Where | What it does now |
| --- | --- | --- |
| The routine apply | `ApplySeedsEndpoint` at `/admin/seed-apply` | Walks the pack in ordinal key order, writes every *release newer* row, creates every *new* one, adopts every *store newer* one, skips *in sync*/*deleted*/*retired*, and stops at the first *edited*, *conflict* or non-matching *unknown* row. There is no selection: every safe row is written by one button. |
| The emergency restore | `RestoreEndpoint` at `/admin/restore` | Writes every shipped seed whose content differs from the bundled source, including *edited*, *conflict*, *unknown* and *deleted*, and reports every outcome past a failure. There is no selection: one button overwrites every edit. |
| The shared plan | `PageSeedInstaller.PlanAsync` | Reads each shipped seed's head and builds `SeedPlan(Path, Source, SeedHash, Summary, HoldsSeed)`. `HoldsSeed` is *content equals the seed*; the raw head is not kept. |
| The rule | `SeedComparison.Of` in `Core` | R (the head's anchor), S (the shipped seed) and H (the head's content) produce the verdict and the two permissions: `CanApply` (H = R and S ≠ R) and `CanAdopt` (content = S and no anchor to S). |
| The words | `SeedVerdictWords` | The host's label and sentence for the seven comparisons, plus `New()` and `Deleted()`. |
| The writes | `PageEditor.SaveSeedAsync` and `AdoptAsync` | A seed save compiles before it stores and records the seed hash as the anchor; an adopt writes the anchor only, no revision. A save with errors is stored and reported. |
| The document | `AdminPage` | The bare document the two seed pages are written in: no stylesheet, no script, a skip link and `main#main`. Every refusal on the two pages uses `ErrorResponses`, which draws the shell document and therefore links `/site.css` — the one seeded asset the recovery page exists to survive. |
| The routes | `Program.cs`, `ShellCatalogue`, `ReservedPaths` | Two GET/POST pairs under the editor policy, with antiforgery on the POSTs. The two routes appear in the administration menu and in `ShellCatalogue.Reserved`; `/admin` is itself a reserved prefix, so every key beneath it is reserved too. |
| The citation surface | `/help`, `PageAuthorPrompt`, `docs/` | Both routes are named in the manual, the assistant's prompt and the shell architecture; live plans and backlog items cite them too. |
| The historical move | `deploy/operations.md`, `docs/shell.md` | Five files — `admin.css`, `admin.js`, `status.css`, `reference.css` and `rocket.svg` — moved from the seed pack to `wwwroot`. An installation that predates the move keeps a seeded row at each key; the static file shadows it, `seeds list` calls it a retired seed, and the operations note says the owner "deletes or scrubs it in the editor". |
| The dead end | `PageWritesScope.Resolve`, `ScrubEndpoint` | The editor's write path throws `"{key} is a shell file; no page is stored there."` for a reserved key, and `/admin/scrub` refuses a head that is not a tombstone. A live reserved row therefore cannot be deleted or scrubbed through any page surface; the documented remedy cannot be carried out. |
| The store | `IPageStore` / `SqlitePageStore` | `ListPagesAsync` lists heads, so a revision or state row with no head is invisible. `ScrubPageAsync` removes a path's head, state and revisions only when its head is already a tombstone; it is the editor's scrub. |
| The startup precedent | `PageContentHashRepair` | A one-transaction, idempotent repair that runs after migration and before the seeder, with a log line when it changes something. It is the shape a startup scan can borrow, without the write. |
| The tests | `ApplySeedsEndpointTests`, `RestoreEndpointTests` (unit and integration), `ShellViews`, `AdminIndexEndpointTests`, `ShellMenusTests`, `PageAssetSeederTests`, `SqlitePageStoreTests`, `AccessibilityTests` | Pin the stop rule, the one-button restore, the two menu entries, the two routes and the store's scrub precondition. |

## What the owner decided

| Question | Answer | What it settles |
| --- | --- | --- |
| The seed route | A new `/admin/seeds`; `/admin/restore` and `/admin/seed-apply` answer `404`, as `/edit` did (2026-11-19) | One route, one menu entry and one record. The old keys stay unreachable because `/admin` remains a reserved prefix; no page can claim them. |
| The default ticks | Only the safe rows: *new*, *release newer* and every adoptable row (2026-11-19) | One press still does what the routine apply did, without the stop. The destructive rows are always a deliberate tick. |
| Which seed rows may be overwritten | Every row that differs, with destructive rows unticked by default (2026-11-19) | *edited*, *conflict*, *deleted* and *unknown* are offered and labelled; the tick is the owner's authority, and the old source stays in history. |
| A seed that changes between preview and POST | Refuse that row; write nothing for it and report it (2026-11-19) | The form carries the state it was drawn from; the other ticked rows still run. |
| The diff | A `?diff=<key>` mode on the shell page (2026-11-19) | The link works when the editor is broken and keeps one route; the diff is shell markup with no seeded stylesheet or script. |
| The shell-path rows | Scan the database for keys the shell answers that still have rows, and scrub them (2026-11-19) | The page lists every such key, the owner ticks the rows, and a typed `scrub` confirms the batch. The scan runs at startup as a warning and never writes. The POST proves each key is reserved before it deletes. |

The repair is the owner's addition of 2026-11-19. The explicit, typed, reserved-only shape is the one
the site's existing scrub rule requires (`pages-scrubbing-is-not-editing`); it is not a second kind of
authority, only the shell's maintenance route for rows no page surface can reach.

## The design

### 1. One route, one page

`GET /admin/seeds` and `POST /admin/seeds` are one shell route, behind `EditorPolicy`, with
`RequireAntiforgeryTokenAttribute(true)` on the POST. `GET /admin/seeds?diff=<key>` is the same route
in its read-only diff mode. The page draws `AdminPage`'s bare document, not `ShellDocument` and not a
stored layout, because it must answer when the page pipeline is what failed.

The page has two sections, each with its own form: **Seeds** (the apply table) and **Stored pages at
shell paths** (the repair table). Both forms post to `/admin/seeds` with a hidden `action` field,
`apply` or `scrub`; a POST with neither is a `400` that says to reload. The page has one route and one
menu entry, not two tools.

`/admin/restore` and `/admin/seed-apply` lose their mappings and their `ShellCatalogue` rows. They
leave the reserved list as named keys but remain unreachable: `ReservedPaths` matches by prefix, and
`/admin` is itself a reserved route, so the page catch-all answers `404` for both. That is the
owner's "like `/edit`" choice, with the stronger guarantee that a page cannot later be stored there
and impersonate the recovery route.

One menu entry, "Seeds", at order 50 in the Administration section (the two entries at 50 and 60 go).
`AdminIndexEndpoint` names it in the owner's paragraph. The menu, the index and the assistant's
record all come from `ShellCatalogue`, so the table is the only place the route is described.

### 2. The seed rows, and what a tick means

`PageSeedInstaller.PlanAsync` is unchanged in what it compares and gains the head it already read:

```csharp
internal sealed record SeedPlan(
    PagePath Path,
    string Source,
    string SeedHash,
    PageSeedSummary Summary,
    bool HoldsSeed,
    PageHeadSummary? Head);
```

The preview uses the head's four facts as the row's state: whether it is a tombstone, its anchor R,
its content hash H, and the shipped seed hash S. The raw head is the smallest addition that lets the
POST prove the row has not moved, and it adds no read.

| Verdict | What the store holds | Tick label | Default | What the tick does |
| --- | --- | --- | --- | --- |
| *new* | no page at a key this release ships | Create the page | ticked | saves the bundled source as a new revision |
| *release newer* | the seed it was installed from; the release moved it | Write the newer seed | ticked | saves the bundled source |
| *store newer* | the shipped bytes, but no anchor to them | Record it as installed | ticked | writes the anchor only; no revision |
| *unknown*, content matches | no anchor, but the content is the shipped seed | Record it as installed | ticked | writes the anchor only; no revision |
| *deleted* | a tombstone at a key this release still ships | Bring the page back | unticked | saves the bundled source; the retained history stays |
| *edited* | the owner's change; the release did not move the seed | Replace it with the bundled seed | unticked | saves the bundled source over it |
| *conflict* | both the page and the seed moved | Replace it with the bundled seed | unticked | saves the bundled source over it |
| *unknown*, content differs | no anchor, different content | Replace it with the bundled seed | unticked | saves the bundled source over it |
| *in sync* | exactly the shipped seed | no tickbox | — | nothing |
| *retired* | not shipped by this release | not listed in the seed table | — | — |

The default is the existing automatic gate: `Summary.CanApply || Summary.CanAdopt`. That is what
makes the safe rows the same set the routine apply wrote, and it is what makes the destructive rows
off until the owner asks. The tick is the authority for the rest: the shell endpoint is owner-only,
and an explicit tick says "write this shipped source over that row". The automatic gate stays where it
is for the editor's per-row install and the assistant; the page does not weaken it.

The seed table lists only the pack's files. A retired seed at a normal key is an ordinary page and is
not touched. A row at a *reserved* key is not in the seed table either — it is in the shell-path
section, because the shell, not a page, answers its key.

### 3. The seed POST: choose, re-plan, refuse a moved row

The seed form carries, for every row with an action:

- `action=apply`;
- `seed` = the canonical key, one per ticked row, following the import page's selection pattern;
- `was.deleted.{key}`, `was.anchor.{key}`, `was.content.{key}` and `was.seed.{key}` — the head's four
  facts and the shipped seed hash the preview was drawn from. The empty string is the null head or
  null anchor; the fields are always present, so a crafted form cannot omit one.

The POST:

1. A bad antiforgery token is a `400` in the bare document, with a link back to `/admin/seeds` and no
   writes.
2. No `seed` value is not an error: the result says "Nothing was ticked, so nothing was written."
3. Every selected key must be one the current pack ships, and its four `was.*` fields must be
   present. A key the current pack no longer ships, or a missing field, is a stale form as a whole:
   `400`, no writes, reload and look again. The page cannot describe a row it no longer has.
4. The endpoint re-plans every seed and, for each ticked row, compares the submitted four facts with
   the fresh row's. A difference — content changed, the page was deleted or restored, an anchor was
   adopted, or a deploy moved the shipped seed — is `Stale` for that row: nothing is written for it,
   and the other ticked rows still run.
5. A row whose fresh action is `Adopt` is `AdoptAsync`; a row whose fresh action is `Write` is
   `InstallAsync` through `SaveSeedAsync`. A fresh `in sync` row is `Unchanged`. A write that throws
   is `Failed` and does not stop the rest. A seed that compiles with errors is `WrittenWithErrors`,
   because the source is stored and reported exactly as a save reports it.
6. A refused adopt (the page moved between the re-plan and the write) is `Stale`, not `Failed`.
7. Every write, adopt, refusal and failure is logged with a structured template naming the key.

`SeedOutcome` becomes `Unchanged`, `Written`, `WrittenWithErrors`, `Adopted`, `NotTicked`, `Stale` and
`Failed`; `Stopped` goes. The result page counts written, recorded as installed, left unticked,
changed since the page loaded, and failed, then lists every row with its key, the verdict now and the
outcome. It ends with links back to `/admin/seeds`, `/admin` and `/admin/status`.

The stop rule is deliberately reversed. It existed so a half-applied pack the operator believed was
applied could not happen. On this page the operator sees every row and ticks what to write; an
unticked drifted row is a deliberate omission, not a half-applied pack. A run that stopped after the
first edit was the defect the owner reported, and the regression test places an unticked *edited* row
before a ticked *release newer* row to prove it is gone.

The small race between the re-plan and the write is not closed: a page changed in those milliseconds
can still be overwritten by the ordinary seed save. That is the window the existing restore and apply
already have. The owner's chosen rule is about a row that changed after the page loaded, and the four
facts close that window. Closing the inner one as well would need a conditional create the store does
not expose, and is not worth a store change for this page.

### 4. The rows at shell paths

A **shell path** is a key `ReservedPaths.IsReserved` answers `true` for: every key `ShellCatalogue`
names, and every key beneath one, because the match is a prefix. A stored page at a shell path is
unreachable by construction — `PageEndpoint` writes `404` for a reserved key before it reads the
store — so its head, revisions and state are dead weight that no page surface can show or remove.

The scan is one store read:

```csharp
// IPageStore
Task<IReadOnlyList<PagePath>> ListStoredPathsAsync(CancellationToken cancellationToken = default);
```

It returns every distinct path in `PageHeads`, `PageRevisions` and `PageState`, in ordinal order, so
a revision or a state row left without a head is found as well as a page. The store does not know
what the shell reserves; the caller filters with `ReservedPaths`.

`ReservedPageScan.FindAsync(IPageStore pages, ReservedPaths reserved, CancellationToken)` returns the
reserved subset. The preview then reads each one's head and revisions and renders a row:

| Column | What it says |
| --- | --- |
| Key | The canonical key, and a link to `?diff=<key>` only when a seed still ships it (the diff is the seed table's view; a shell-path row has no seed to compare with). |
| Head | *live*, *tombstone* or *no head* — `GetHeadAsync` is `null` for a path that exists only as a revision or state. |
| Revisions | The count from `ListRevisionsAsync`. |
| Scrub | A tickbox, unticked, labelled "Remove this key and its history". |

The scan is shown even when it is empty, with the sentence "No stored page is at a key the shell
answers." It is a maintenance report, not a seed row list.

The repair form carries `action=scrub`, one `stray` value per ticked key, and a `confirm` text field
that must equal `scrub` (ordinal). The POST:

1. A bad antiforgery token is a `400` with no writes.
2. The confirmation is checked before anything else: anything but `scrub` is a `400` that says the
   batch was not confirmed and names the keys, with a link back. No writes.
3. No `stray` value is not an error: "Nothing was ticked, so nothing was removed."
4. The endpoint re-scans and re-checks `ReservedPaths` for every selected key. A key that is not
   reserved is a `400` and **nothing in the batch is removed** — the same all-or-nothing rule the
   import uses for an offered set, because a form naming a normal page is crafted or stale and the
   repair must never be the way a normal page is scrubbed. A key that is no longer present is
   `AlreadyGone`, not a failure; the rest continue.
5. Each selected key is scrubbed by one store call:

```csharp
// IPageStore
Task<PageScrubResult> ScrubStoredPathAsync(PagePath path, CancellationToken cancellationToken = default);

internal sealed record PageScrubResult(bool Head, int Revisions, int StateKeys);
```

`ScrubStoredPathAsync` deletes the head (live or tombstone), the page state and every revision for
the path, in one transaction, with `PRAGMA secure_delete=ON` and a WAL checkpoint, and reports what it
removed. It has no tombstone precondition: a live reserved row is exactly the case the editor's
`ScrubPageAsync` cannot handle. `ScrubPageAsync` keeps its precondition for the editor and shares the
one private path-wide delete, so there are not two SQL scrub implementations.

The repair result page counts removed keys, already-gone keys and failures, and lists each key with
what was removed. A failure does not stop the rest, and each failure is logged with the key. The
result links back to `/admin/seeds`, where the remaining strays are the new state.

**Why the typed word and not each key.** The editor's scrub asks the owner to type the page's key
because the owner chose one page. Here the condition is a machine-found set; the owner cannot be
expected to know the keys, and the page shows them with their head and revision counts. The tick
selects, the word confirms the irreversible batch, and the POST proves every key is reserved. The
owner who wants the bytes first can export the archive from `/admin/export`; the repair page says so.

### 5. Detection and prevention

**At startup.** After migrations and after `PageAssetSeeder.SeedAsync`, the host runs
`ReservedPageScan.FindAsync` once. If it finds anything, it logs one warning naming the count and the
keys and pointing at `/admin/seeds`. It does not write. The scan is one read, and it gives the owner a
journal line when the condition recurs. It runs after seeding so a seed pack that ships a reserved
key is reported too, not hidden by the seeder's "already exists" check.

**At build time.** A test, `NoShippedSeedIsAShellPath`, asserts that no key in `TestOptions.Seeds` is
reserved. A seed pack that adds `admin.css` or a future shell key fails the gate rather than shipping.
The import already refuses a reserved key (`ImportEndpoint.ApproveAsync`), and the assistant's path
policy refuses one (`PagePathPolicy`); the remaining source is a direct database write or a build
older than the reservation, which is what the repair exists for.

### 6. The diff mode

`GET /admin/seeds?diff=<key>` renders one seed row's change and nothing else. The key is normalised
and must be a seed this release ships; anything else is a bare `404` with a link back. The page reads
the live source with the existing `PageEditor.GetAsync` and renders
`DiffRenderer.ToHtml(live?.Template ?? "", seed.Source)` — before is what the store holds, after is
the bundled seed. A missing or tombstoned page diffs against the empty document and says that
writing would bring it back. `LineDiff`'s existing bounds apply.

The diff is read-only: no form, no tick, no write. The only links are back to `/admin/seeds` and the
row's editor when the owner wants the full view. It is drawn in `AdminPage`'s bare document, so it
works while `/pages` is broken; the diff's classes are unstyled there, but the table is semantic and
readable. A seed row links to this view whenever a tick would write something — *new*, *deleted*,
*release newer*, *edited*, *conflict* and differing *unknown* — and not when the action is an adopt,
because nothing changes. The shell-path table does not use this mode: there is no seed on the other
side of the comparison.

### 7. The document

The preview, the diff, both result pages and every refusal are `AdminPage` documents: no `<link>`, no
`<script>`, a skip link and `main#main`. The forms mint their antiforgery token before the first byte,
as both current pages do, because setting the cookie is a header write. Each table has a caption,
`th scope="col"` headings and a row header per key; every checkbox has a `<label for>`; the seed
button is "Apply the ticked seeds" and the repair button is "Scrub the ticked rows". Neither is a
live count, because there is no script to keep a count true. The seed defaults are `checked`
attributes written server-side; the repair defaults are unchecked.

Every refusal is written in the same bare document, not through `ErrorResponses`: that helper draws
the shell document and links `/site.css`, a seeded page this page exists to recover. The status codes
stay the same — `400` for a bad token, an unconfirmed repair, or a form the page did not offer,
`404` for a diff key that is not a shipped seed.

The seed section's words are the rule in the owner's language:

> Every page this release ships, and what the store holds for it. Tick a row to write it; a row you
> leave unticked is left exactly as it is. Writing a seed compiles it first and saves it as a new
> revision, so the source it replaces stays in the page's history. The rows that could replace your
> own source — edited, conflict, deleted and unknown — start unticked. If a page changes after this
> page loads, its tick is refused and nothing is written for that row; the others still run.

The repair section's words are:

> A key the shell answers serves the shell's own file, not a stored page. A row here is unreachable:
> it is what is left when a seed became a shell file, or when an import or an older build wrote a key
> the shell had taken. Ticking a row and typing scrub removes its head, every revision and its page
> state for good. The page proves every key is still a shell path before it removes anything; a
> normal page can never be scrubbed from here. Export the archive first if you want the bytes.

### 8. The old routes and the record

The two routes, their menu entries and their endpoint classes go. `/help`, `PageAuthorPrompt`,
`docs/shell.md`, `docs/architecture.md`, `docs/glossary.md`, `docs/pages.md`, `docs/storage.md`,
`deploy/operations.md` and `docs/decisions.md` are updated in the same change, because each names the
old routes as the way back or the old remedy as the editor. The live plans and backlog items that
assert today's routes are updated too; archived plans and closed backlog rows are frozen history and
are not edited.

At the merge:

- **shell-seeds-are-chosen-row-by-row** is new: the seed page writes the rows the owner ticks and
  nothing else; the safe rows are ticked, the destructive rows are offered unticked; a ticked row
  that moved is refused and the rest still run; the write is an ordinary seed save, so the old source
  stays in history.
- **shell-reserved-rows-are-scrubbed-not-served** is new: a stored row at a key the shell answers is
  unreachable, and the maintenance page scrubs it after a tick and the typed word; the endpoint
  proves the key is reserved before every delete; the startup scan reports and never writes.
- `shell-reserved-keys-are-an-invariant` is edited in place: the reserved list still stops a new
  write, and the repair is the way a row that predates it is removed.
- `pages-scrubbing-is-not-editing` is edited in place: the editor's scrub keeps its tombstone
  precondition; the reserved-row repair is the same shell-owned, explicit, irreversible act for a
  row no page surface can reach.
- `shell-the-seed-apply-is-a-route` is edited in place: the route is `/admin/seeds`, and the stop
  rule gives way to the new name. The heading stays, because a name is the id and is not renamed.
- `shell-the-admin-tools` is edited in place: "changes, seeds and scrub", the bare document, and the
  shell-path section.
- The `/admin/restore` mentions in `pages-any-page-may-write-the-store` and
  `pages-delete-install-and-restore` become `/admin/seeds`.
- No name is retired, so the archived citations of `shell-the-seed-apply-is-a-route` remain valid.

## What changes

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Web/Admin/SeedsEndpoint.cs` | New: the seed preview, the seed POST, the `?diff` view, the shell-path preview and scrub, the bare document and refusals, structured logging. |
| `src/uk.osric.asp.Web/Admin/ApplySeedsEndpoint.cs` | Deleted. |
| `src/uk.osric.asp.Web/Admin/RestoreEndpoint.cs` | Deleted. |
| `src/uk.osric.asp.Web/Pages/PageSeedInstaller.cs` | `SeedPlan` carries the `Head`; `SeedOutcome` becomes `Unchanged`, `Written`, `WrittenWithErrors`, `Adopted`, `NotTicked`, `Stale`, `Failed`; remarks name the one page. |
| `src/uk.osric.asp.Web/Pages/IPageStore.cs` | `ListStoredPathsAsync` and `ScrubStoredPathAsync`; the result record. |
| `src/uk.osric.asp.Web/Pages/SqlitePageStore.cs` | The union of heads, revisions and state for the scan; one path-wide scrub shared by the repair and the editor's `ScrubPageAsync`. |
| `src/uk.osric.asp.Web/Pages/ReservedPageScan.cs` | New: the reserved subset of `ListStoredPathsAsync`, used by the page and the startup warning. |
| `src/uk.osric.asp.Web/Program.cs` | One GET and one POST mapping for `/admin/seeds`, owner policy and antiforgery; the four old mappings go; the startup warning after seeding. |
| `src/uk.osric.asp.Web/Shell/ShellCatalogue.cs` | One row, `/admin/seeds`, "GET, POST", owner, "Seeds" at order 50, with the `?diff` mode and the shell-path section in the description. |
| `src/uk.osric.asp.Web/Admin/AdminIndexEndpoint.cs` | The owner's paragraph names `/admin/seeds`, its tick and its shell-path section. |
| `src/uk.osric.asp.Web/Admin/AdminPage.cs` | Remark singular: the bare document the seed page is written in. |
| `src/uk.osric.asp.Web/Pages/Seeds/help` | The manual: one bullet for the seed page, the tick table in words, the diff link, the shell-path section and its typed word, and every recovery mention. |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | `/admin/restore` becomes `/admin/seeds`, and the prompt says the page writes the ticked row and scrubs a stored shell-path row. |
| `test/uk.osric.asp.Web.Tests/SeedsEndpointTests.cs` | New unit suite, replacing the two endpoint suites. |
| `test/uk.osric.asp.Web.Tests/ApplySeedsEndpointTests.cs`, `RestoreEndpointTests.cs` | Deleted, their cases folded into the new suite. |
| `test/uk.osric.asp.Web.Tests/FakePageStore.cs` | The two new store members, with revision-only paths visible to the scan. |
| `test/uk.osric.asp.Web.Tests/SqlitePageStoreTests.cs` | The scan's union, the path-wide scrub, and the editor's tombstone precondition. |
| `test/uk.osric.asp.Web.Tests/PageSeedsTests.cs` | `NoShippedSeedIsAShellPath`. |
| `test/uk.osric.asp.Web.Tests/AdminIndexEndpointTests.cs`, `ShellMenusTests.cs`, `ShellViews.cs`, `PageAssetSeederTests.cs` | "Seeds" in the index and the menu; the new view names; `/admin/seeds` in the no-seed case. |
| `test/uk.osric.asp.Web.IntegrationTests/SeedsEndpointTests.cs` | New, from `RestoreEndpointTests.cs`: the owner's broken-`/pages` scenario, the selection, the old-route 404s, the diff while the editor is broken, a stored shell-path row inserted directly and scrubbed, antiforgery and the policy. |
| `docs/shell.md`, `docs/architecture.md`, `docs/glossary.md`, `docs/pages.md`, `docs/storage.md`, `deploy/operations.md`, `docs/decisions.md` | The route, the page, the repair, the recovery role and the new rules. |
| `docs/plans/ide-host.md`, `docs/plans/page-move.md`, `docs/plans/account-store.md`, `docs/backlog/page-cannot-move.md`, `docs/backlog/conversation-is-not-a-page.md` | The route facts their prose asserts today. Archived plans and closed backlog rows are not edited. |
| `docs/plans/seed-page.md` | This plan; moved to `docs/plans/archive/` and marked merged when the work lands. |

No configuration, migration, `Core`, `Runtime` or `Sdk` change. `DiffRenderer` is already registered as
`DiffRenderer`; the endpoint takes it as a parameter.

## Tests

The unit suite is the whole behaviour of the page, driven through `SeedsEndpoint` with `PageHarness`,
`FakePageStore`, `TestOptions.Seeds`, `ReservedPaths` and `TestAntiforgery`:

1. The preview ticks *new*, *release newer* and every adoptable row, and leaves *edited*, *conflict*,
   *deleted* and differing *unknown* unticked.
2. The preview offers no seed form when every seed is *in sync*.
3. **The reported seed defect**: with an unticked *edited* row before a ticked *release newer* row,
   the later row is written and the edited row is untouched.
4. A ticked *edited* row is overwritten with the bundled source, and the source it replaced remains
   in the page's history.
5. A ticked *store newer* or matching *unknown* row is adopted and writes no revision.
6. A ticked *deleted* row comes back and clears the tombstone; a ticked *new* row is created.
7. A row whose four facts changed between the preview and the POST is `Stale`, nothing is written for
   it, and the other ticked rows still run.
8. Nothing ticked writes nothing and is not an error; a key the current pack does not ship, or a
   missing `was.*` field, is a `400` with no writes.
9. A failed write does not stop the others; a seed that does not compile is `WrittenWithErrors`.
10. A bad antiforgery token is a `400` in the bare document and writes nothing.
11. The preview, both results, the diff and every refusal contain no `<link>` or `<script>`, and keep
    the skip link and `main#main`.
12. The diff view shows the live source against the bundled seed, and an empty before for a missing
    or deleted page.

The repair's unit cases, in the same suite:

13. The preview lists a live row, a tombstone and a revision-only key at reserved paths, with their
    head state and revision counts; it does not list a normal page.
14. The preview has no repair form when there are no strays, and says so.
15. A wrong or missing `confirm` is a `400` and removes nothing.
16. No tick removes nothing and is not an error.
17. **The safety case**: a form naming a normal page's key is a `400` and the batch removes nothing,
    even when a valid reserved key is ticked beside it.
18. A ticked reserved key removes its head, every revision and its state, and the result names what
    was removed; a key that vanished since the preview is `AlreadyGone`; a failure on one key does
    not stop the rest.

The storage suite, `SqlitePageStoreTests`, on a real SQLite file:

19. `ListStoredPathsAsync` returns a path that exists only as a revision and one that exists only as
    page state, as well as a headed page, once each and in ordinal order.
20. `ScrubStoredPathAsync` removes a live head, its revisions and its state, returns the counts, and
    a second call reports nothing; the editor's `ScrubPageAsync` still refuses a live head and still
    scrubs a tombstone.

`PageSeedsTests` gains:

21. `NoShippedSeedIsAShellPath`: every seed the test pack ships is not reserved.

The integration suite drives the real pipeline:

22. An anonymous `GET /admin/seeds` redirects to `/login?returnUrl=…`.
23. A `POST` without antiforgery writes nothing.
24. **The owner's seed scenario**: a signed-in owner breaks `/pages`, ticks `/pages` and posts,
    `/pages` serves the bundled editor again, and no other page is written.
25. The page and the diff work while `/pages`, `/editor.css` and `/_layout` are broken.
26. `/admin/restore` and `/admin/seed-apply` answer `404`, signed in and anonymous.
27. A visitor with no antiforgery cookie is still offered the form and the document is complete.
28. **The shell-path scenario**: a row is written at `/admin.css` directly through `IPageStore` (as
    an older build did), `/admin/seeds` lists it, the scrub removes it, and `/admin.css` still
    serves the shell's own file.

`ShellViews` gains `admin-seeds`, `admin-seeds-result`, `admin-seeds-diff` and
`admin-seeds-scrub-result`; `AccessibilityTests` runs its rules over every view in `Names`, so the
tables' captions and scopes, the labelled tickboxes and the bare document are checked there. No
browser test is added: the page is forms and tables, and the unit and integration suites cover its
contract.

## Documentation

`/help` is the author manual and is a seeded page; it is updated in the same change, as the plan
requires. It gains one bullet in place of the two routes: the page lists every shipped seed, the safe
rows are ticked, the destructive rows are offered unticked, a tick is the authority to replace, the
old source stays in history, a moved row is refused, and `?diff=<key>` shows the change even when the
editor is broken. It gains a paragraph for the shell-path section: a stored page at a key the shell
answers can never be served, it is left by an old build or an import, the page lists it, and a tick
plus the typed word removes its head, revisions and state for good. Its other recovery mentions
(`/admin/restore` in the library, layout and self-save sections) become `/admin/seeds`.

`docs/shell.md` is authoritative for the route, the two sections and the bare document, and its "The
shell's own files" paragraph now says the leftover rows are scrubbed from `/admin/seeds`, not the
editor. `docs/storage.md` records the one path-wide scrub and the editor's tombstone precondition.
`docs/architecture.md` moves the bootstrap row to `/admin/seeds`; `docs/glossary.md` names the seed
page in the bootstrap list; `docs/pages.md` names `/admin/seeds` as the way back from a deleted
editor; `deploy/operations.md` replaces the "delete or scrub it in the editor" remedy with the new
section. `docs/decisions.md` takes the two new entries and the four edits above. Live plans and
backlog items are updated where they assert today's routes; archived plans, `docs/archive/decisions.md`
and `docs/backlog/closed.md` are frozen and are not touched.

## Risks and what they cost

| Risk | What holds it |
| --- | --- |
| The owner overwrites an edit by mistake | The destructive seed rows are unticked and labelled "Replace it with the bundled seed"; the old source is a retained revision; the page says so. The default is the same safe set the routine apply wrote. |
| A seed row changes while the form is open | The four submitted facts are compared with a fresh plan; the moved row is `Stale` and the rest still run. The owner reloads and looks again. |
| A seed change after the re-plan and before the write | Accepted and stated: the window is the one the existing restore and apply have. Closing it needs a conditional create the store does not expose. |
| The repair removes history the owner might want | The page shows the exact keys, whether the head is live or a tombstone, and the revision count; the rows are unticked; the owner types `scrub`; the page says to export the archive first. The rows are unreachable by design, so nothing the owner can visit is lost. |
| A key stops being reserved later | The repair only proves the key is reserved now. A future release that removes a route can make the key a legal page again; the owner chose to scrub the history while it was dead. The startup warning gives them the chance to review before they do. |
| A crafted repair form scrubs a normal page | The endpoint re-scans and refuses the whole batch on any key that is not reserved, so the repair can never be a second way to delete a normal page. The all-or-nothing refusal is tested. |
| A seed pack ships a shell path again | `NoShippedSeedIsAShellPath` fails the gate; the import and the assistant already refuse reserved keys; the startup warning names any row that still arrives by another route. |
| The route move breaks a bookmark | The owner chose the 404. `/help`, the prompt, the menu and the docs name `/admin/seeds` in the same change; `/admin` keeps the old keys reserved, so a page cannot impersonate them. |
| The page is a shell view and cannot be edited | That is why it works when the editor is broken; the plan and `shell-the-admin-tools` record it. |
| The diff is unstyled on the bare page | It is semantic markup from `DiffRenderer`; the classes are there for a sheet, and the table is readable without one. Loading a sheet would reintroduce the dependency the page avoids. |
| The new names are cited before the merge | The plan names them in bold, not backticks, and the merge writes the entries. `make decisions` stays green on the branch. |

## Order of work

0. The owner approves this plan. Nothing is built before that.
1. The storage seam first, test-first: `ListStoredPathsAsync`, `ScrubStoredPathAsync` and the shared
   path-wide scrub in `SqlitePageStore`, with the SQLite tests and the `FakePageStore` members.
2. The seed page: the failing unit and integration tests, `SeedPlan.Head`, `SeedOutcome`, then
   `SeedsEndpoint`'s seed preview, POST and diff; delete the two old endpoints; wire `/admin/seeds`;
   update the catalogue, menu and index.
3. The repair section: the failing tests, `ReservedPageScan`, the shell-path table, the tick and the
   typed confirmation, the scrub POST and its result.
4. Detection and prevention: the startup warning after seeding, and `NoShippedSeedIsAShellPath`.
5. Update `/help`, the assistant prompt and the docs; update the live plans and backlog citations.
6. Run the Web unit and integration suites, then `make check` (which includes `make check-pages`,
   `make decisions`, `make backlog`, `make license-check` and the format check), and `make format`.
7. The merge, only after the owner's approval: write the two decision entries, edit the four
   entries, mark this plan merged, move it to `docs/plans/archive/`, and retarget its links in the
   same change.

## The merge

The plan's Status becomes `Merged` and it moves to `docs/plans/archive/` in the step that records the
decisions, as `taking-a-name` requires. The two new entries are written in `docs/decisions.md`; the
edited entries point at them where they used to state the stop, the old route and the editor-only
scrub; every live link to this plan is retargeted. The owner's approval is the last gate, as it is
for every step (`product-steps-wait-for-the-owner`).

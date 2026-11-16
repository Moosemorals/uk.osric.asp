<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Seed provenance

| | |
| --- | --- |
| Status | Merged 2026-11-15, and this plan is frozen with the work: the migration, the `seeds` command, the verdict, the apply route, the harvested pack and `make check` are on `trunk`. A statement here that the code has since overtaken is expected rather than a defect. The plan was approved on `trunk` and revised the same day after the owner answered the seven open questions |
| Size | Large — a migration with two columns and a one-off repair, one write-path fact, one pure comparison and its words, one field on the page contract and one member removed, one shell route, an archive format version, a `seeds` command with four make targets, two seeded pages, and the prose and tests for each. Slices 0 and 1 are small and independent; 2–4 are the bulk |
| Spike | None. Every number and claim below was measured on `trunk` at `f369bcc`; the first version of this plan was measured at `1badddd`, and the pack has grown by 7,972 bytes since. The two places the design could fail are the revision hash (settled: it is an identity, D1) and the archive reader (settled by the format's own version column, D9) |
| Decisions | `pages-a-seed-write-records-its-hash`, `pages-a-seed-verdict-is-the-hosts`, `shell-the-pack-comes-home`, `shell-the-seed-apply-is-a-route`, `pages-an-archive-carries-provenance` |
| Backlog | [IDEA-036](../../jobs/README.md#closed) — closed by this plan. Relates to [IDEA-024](../../jobs/IDEA-024.md) (an archive is not a form), [IDEA-030](../../jobs/IDEA-030.md) (every view writes its own navigation), and [IDEA-043](../../jobs/README.md#closed) (the widened write authority; the batch is split out of it, unnumbered for now — see *Risks*) |

## The short version

A seed is a snapshot of the current truth per page, identified by nothing. Nothing in the store records
that a page came from a seed, so "the release moved this page" and "the author edited this page" are
the same observation: the bytes differ. One recorded fact fixes it, and there is exactly one place it
can go — the page's row, written in the same transaction as the revision.

**R is the content hash of the seed that a page's stored content is anchored to.** It is written only
by a seed write, and left alone by every other write. With **S** (the hash this release ships) and
**H** (the live content's hash) it gives:

> **An automatic write requires H = R and S ≠ R.**

Three things follow, in this order of value:

1. **A deploy can say what it would change.** `release newer` applies unattended; `edited`, `conflict`,
   `unknown` and `retired` do not. Today every one of them is "the bytes differ".
2. **The page list stops doing arithmetic it cannot win.** The verdict is the host's answer, computed
   from one row, with no reads: today the list reads two sources for every seeded row to produce one
   bit, and charges a third of the render's source allowance for it (D7).
3. **The pack can come home.** The owner edits `/pages` in production — that is what the site is for —
   and nothing brings the change back to `Pages/Seeds`. A `seeds` command (slice 1) does, and *adopt*
   (D6) is what reconciles the store once the harvested pack ships.

The page's own note on the site (`/seed-plan`, a stored page that is not in this repository) proposed
this job. This plan keeps its invariant, its six verdicts and most of its words, and changes three
things the code does not support: the hash it compares, where the verdict is computed, and the fact
that a harvested pack is the *second* automatic case rather than an optional repair.

## The owner's answers

The first version of this plan left seven questions open. They were answered on 2026-11-15, and the
sections below are written to them.

| Question | Answer |
| --- | --- |
| How the pack comes home | `seeds pull` emits a tar on stdout and `make` extracts it. No page serves a pack: an archive is not a form ([IDEA-024](../../jobs/IDEA-024.md)) |
| Does the bulk apply adopt? | Yes. `H = S` is a fact the store verifies, so `store newer` is recorded as installed in the same run |
| A manual install on an `edited` or `conflict` row | No. The row offers the diff, not a button. `/admin/restore` stays the deliberate overwrite-everything path |
| Headings in `/pages` | Live and seeded are one list, as the production page already has them; a `retired` page is an ordinary live page. Two tables: *Pages* and *Deleted pages* |
| The routine apply route | A new `/admin/seed-apply`, beside `/admin/restore`; two routes say "routine" and "emergency" in the URL |
| Ordering against IDEA-043 | Plan and build against today's `trunk`; if it lands first, this branch rebases (see *Risks*) |
| Backfilling the existing rows | Content only. H is repaired from the head revision; R is not invented, so nothing legacy becomes automatic |

## What the store can say today

| Path | What it knows | Where |
| --- | --- | --- |
| The install at start | A seed's page when the key has no head row, so a local edit survives a restart. Reported nowhere. It also *replaces* a stored seed whose source names `PageDocument` — a silent overwrite of the author's page, and junk (D10) | [`PageAssetSeeder.SeedAsync`](../../../../src/uk.osric.asp.Web/Pages/PageAssetSeeder.cs:16), [`Program.cs`](../../../../src/uk.osric.asp.Web/Program.cs:338) |
| `SeedPaths()`, `SeedSourceAsync`, `InstallSeedAsync` | The pack's keys, one seed's source, and a conditional install against the head hash. Offered to the editor page only | [`IPageEditor`](../../../../src/uk.osric.asp.Sdk/IPageEditor.cs:38), [`PageEditorScope`](../../../../src/uk.osric.asp.Web/Pages/PageEditorScope.cs:123) |
| `/admin/restore` | Every shipped key with `current`, `changed`, `missing` or `deleted`, and one POST that writes each of them and reports every outcome (`shell-the-admin-tools`) | [`RestoreEndpoint`](../../../../src/uk.osric.asp.Web/Admin/RestoreEndpoint.cs:40) |
| The page list's byte compare | Live source against seed source as strings: differ, or do not. Two reads per live seeded row | [`pages`](../../../../src/uk.osric.asp.Web/Pages/Seeds/pages:460) |

The facts that shape the design, each checked in the code:

| Fact | Consequence |
| --- | --- |
| The revision hash is an **identity**, not content: `RevisionHash.Of` frames the source with `creationRuntimeId`, `savedAt` and `revisionId` under a `page-revision-v1` prefix | H can never be the head's `Hash`. A content hash has to be recorded separately (D1) |
| A plain content hash already exists: `TemplateFingerprint.Of`, the lowercase SHA-256 of the template's UTF-8, chosen for exactly this kind of comparison | R, S and H are all that function. There is no new algorithm and no wire format (D2) |
| `PageRevisions.TemplateHash` holds it, but only from the image build — a second statement after the revision exists, absent for a page the start seeder wrote (no image at all) and for a compiler fault | `TemplateHash` cannot be the gate's H (D1) |
| `PageRevisions.Template` holds the exact source of every head, so H is computable for every existing row | A content-only backfill is possible, and the owner has asked for it (D1) |
| Nothing normalises the source: no line-ending or BOM rewriting in the save path, and `PageSourceChange.IsNoOp` is an ordinal compare | The note's "constraint that can kill it" is already satisfied; the acceptance test asserts it anyway (D2) |
| The seeds are `Content` files copied beside the assembly and read with `File.ReadAllTextAsync`, not embedded resources | Hashing the pack at start reads the files the seeder already reads. There is no artefact to drift (D2) |
| `SeedSourceAsync` resolves a key by enumerating the whole pack (`FindSeedFile` → `SeedFiles()` → `Directory.GetFiles(...AllDirectories)`) | The list does 1 + one directory enumeration per seeded row per render. One in-memory pack map removes the scans as well as the reads (D7) |
| The pack is 19 files, 171,841 bytes; the listing charges each live seeded row's source to `Pages:Read:MaxSourceCharsPerRender` (524,288) | The bit costs ~33% of the render's allowance (D7) |
| A head row's content is written by two statements: `WriteHeadAsync` (the single save and the batch) and `TryStoreRevisionAsync` (the conditional save); the delete and scrub paths only move `IsDeleted` or remove the row | Both write statements must carry R and H, or a path will disagree with the others (D1) |
| A tombstone is a `PageHeads` row with `IsDeleted = 1`; `ScrubPageAsync` deletes the head and its revisions | R on the head row survives a delete and dies with a scrub (D5) |
| `/admin/restore` writes a tombstoned seed by decision (`shell-the-admin-tools`); the start install leaves it alone | The two paths disagree on purpose, and both behaviours stay: the verdict says which is which (D5) |
| `Editor:Key` is `/pages`, so the page list holds `IPageEditor` and can write any key | "A page cannot write a key it does not own" is false here. The real limit is one action per render (D8) |
| The archive is a versioned SQLite database whose column list is validated exactly, and it carries `Path` and `Source` only | "R belongs in the export" is a format change, not an additive one (D9) |
| `users` is a user command that runs before the host, and `make` has `remote-user-*` targets over `ssh` + `podman exec` | The shape for a `seeds` command and its two remote targets already exists (D11) |
| `/pages` in production already merges live and seeded rows into one list | The repository's seed catches up with the page being edited, rather than inventing a third grouping (D7) |

## The design

### D1 — Two hashes on the head row, written with the revision

`PageHeads` gains two nullable columns:

| Column | Holds |
| --- | --- |
| `SeedHash` | **R** — the content hash of the seed this page's content is anchored to, or null when unknown |
| `ContentHash` | **H** — the content hash of the head's stored source |

Both are written in the statement that already advances the head, and R is written only when the write
is a seed write:

```sql
INSERT INTO "PageHeads" ("Path", "RevisionId", "IsDeleted", "SeedHash", "ContentHash")
VALUES ({key}, {revisionId}, 0, {seedHash}, {contentHash})
ON CONFLICT ("Path") DO UPDATE SET
    "RevisionId"  = excluded."RevisionId",
    "IsDeleted"   = 0,
    "SeedHash"    = COALESCE(excluded."SeedHash", "PageHeads"."SeedHash"),
    "ContentHash" = excluded."ContentHash"
```

`COALESCE` is the whole "left alone" rule in one clause: a caller that passes `null` for the seed hash
leaves whatever is recorded, and a seed write passes the hash it is installing. The conditional
statement that advances the head (`TryStoreRevisionAsync`) carries the same `SeedHash = COALESCE(...)`
clause. It is the same transaction as the revision insert, so a page and its provenance cannot
disagree.

The seed hash travels on `PageTemplate`, beside `Template` and `Image`, so no write path can reach the
store without deciding. A seed write is the only caller that fills it in: `PageAssetSeeder` on a
create, the editor's install, the bulk apply's create, and an import carrying an archive's R. Every
other write — an edit, a restore, the assistant, a batch from a version 1 archive — passes null.

H does not live on the revision even though `PageRevisions.TemplateHash` already holds exactly this
value, because that column is written by a second statement from the image build and is absent for the
two rows that matter most — a seed the start seeder created (which has no image at all) and any save
that hit a compiler fault. The gate must not depend on whether a page has been compiled.

**The existing rows are repaired, once.** R and H are null on every row written before this change. R
cannot be recovered honestly, but H can: `PageRevisions.Template` holds the exact source of every head.
A one-off repair runs after the migration and before the start seeder — in the same startup block, so
the `users` and `seeds` commands see a repaired store too:

```text
select every head whose ContentHash is null, joined to its revision's template
for each: write ContentHash = TemplateFingerprint.Of(template), leaving SeedHash null
```

It is one transaction, idempotent, and names the count it repaired in one log line. It deliberately
does not write inside the EF migration: SQLite has no SHA-256, and a migration that froze a copy of the
algorithm would be a second implementation the day `TemplateFingerprint` changes. The consequence is the
honest split the owner asked for: a legacy page whose source matches the shipped seed reads `store
newer` and can be adopted in one press; a legacy page that differs reads `unknown`; a legacy page that
is not a seed carries no summary at all. Nothing legacy becomes automatic, because R is still absent.

**Rejected: compare against `PageEntry.Hash`.** It is an identity and changes on every save, so every
seeded page would read `edited` forever — the failure mode that hides, which the note warns about.

### D2 — One function, applied to the exact bytes

`TemplateFingerprint.Of` produces R, S and H. S is computed once per process while the pack is read,
into one map in a new `PageSeeds` singleton:

```csharp
// PageSeeds: the pack, read once per process.
IReadOnlyDictionary<string, SeedFile> Files; // canonical key → file, source, hash
```

The pack is read at start anyway (`SeedAsync` already reads every file). Building the map there pays
for itself immediately: it removes a directory walk per `SeedSourceAsync` call and the file read the
listing does today (D7). Slice 1 introduces the map with keys and sources; the hashes are added in
slice 2, when something asks for them.

There is nothing to normalise. The store writes back exactly the string it was given — no line-ending
conversion, no BOM strip, no trailing-newline tidy — and `PageSourceChange.IsNoOp` already relies on
that being true. The acceptance test that asserts R = S = H immediately after a first install is the
guard; it is the first test written, because it is the one that can pass review and fail in production.

### D3 — The verdict is the host's answer, not the page's arithmetic

The note proposed `Seeds()` returning `(Key, Hash)` and `SeedHash` on `PageEntry` and `PageSource`,
which leaves the page to compare three strings and decide. That is the byte compare again, one level
up: two implementations that can disagree, and a page contract that grows a rule.

So the store's rule is computed once, in `Core`, and reported on the listing:

```csharp
// Core/Pages/SeedComparison.cs — pure, no I/O.
public static SeedComparison Of(string? recorded, string? shipped, string? content);

// Sdk/PageEntry.cs — the host's answer, in the shape PageEntry.State already has.
public PageSeedSummary? Seed { get; init; }
```

`SeedComparison` is a record of the verdict and the two permissions it implies. `PageSeedSummary` is a
value in `Sdk` with no behaviour — the verdict's label, its sentence, and whether the row may be
applied or adopted. It carries strings rather than the `Core` enum because `Sdk` references nothing:
the exact split `PageEntry.State` already uses ("the marks are the host's own rules reported rather
than derived").

The page renders the sentence and shows a button per flag; it never compares hashes, and it cannot
disagree with the shell about what may be written. `SeedPaths()` goes (D8) and `PageSource` is
untouched — the diff view needs the seed *source*, which `SeedSourceAsync` already returns.

The words live in one `Web` class beside `PageImageStatus` (`SeedVerdictWords`), because the sentence is
the host's, not the page's. The read seam carries R and H on `PageReadHead`, so `PageCatalogScope`
builds the summary from one row and the pack map; `PageHeadSummary` carries the same two values for the
shell's own writers.

### D4 — Seven verdicts, and the two that must not borrow words

| Verdict | R vs S | H | Automatic | Words |
| --- | --- | --- | --- | --- |
| `in sync` | equal | H = R | — | Holds the seed this release ships. |
| `release newer` | differ | H = R | **apply** | Release 9f3a… updated this page, and you have not changed it since it was installed. Applying saves the new seed; the diff shows what changes. |
| `edited` | equal | H ≠ R | no | Changed since the seed last set it; this release did not move the seed. The diff shows both. |
| `conflict` | differ | H ≠ R | no | Changed since the seed last set it, and this release changed the seed too. Compare, then choose. |
| `store newer` | differ | H = S | **adopt** | Holds the bytes this release ships, but is not recorded as installed from it. Recording it as installed makes it a seeded page and keeps this content. |
| `unknown` | — (R absent) | any | no | **No record of this page coming from a seed.** It is not updated automatically. |
| `retired` | — (S absent) | any | no | This release no longer ships this page. It is left alone, and it is an ordinary live page. |

Two states sit outside the three-value function because they are about the row, not the hashes:

| State | When | Automatic | Words |
| --- | --- | --- | --- |
| `new` | no head row, S present | **create** | This release ships a page here that the store does not have. Installing creates it. |
| `deleted` | head is a tombstone, S present | no — an explicit restore | Deleted here, and still shipped by this release. Restoring writes the bundled source as a new revision; it is not offered as an install. |

And one non-state: R and S both absent is a page the owner made, and carries no seed summary at all.

`store newer` is the row the note's table cannot reach, and it is not exotic — it is what every page
the owner has edited reads the moment their harvested change ships (D11). It is why *adopt* is part
of the first slice that writes anything, not an optional repair for old stores.

**Adopt is a separate predicate, not a verdict.** It is offered whenever `S` is present and `H = S`
and `R ≠ S`, so it appears under both `unknown` (no record) and `store newer` (a record of something
else). Under `unknown` the sentence gains: *Its content already matches what this release ships, so
applying would change nothing; recording it as installed makes it a seeded page.* That is the honest
form of the note's "unknown says unknown" rule, kept while still telling the operator what the safe
action is. It is also what makes the content-only backfill useful: a legacy seeded page reads `store
newer` rather than `unknown`, and one adopt makes it a seeded page again.

`new` is not a row in the page list — there is no head row to list. It belongs to `seeds list` and to
`/admin/seed-apply`'s preview, which both walk the pack rather than the store.

### D5 — The gate, and the three rows that are not hashes

`H = R and S ≠ R` is the whole automatic rule, and it applies to content writes only. Three rows are
decided before it:

- **A create is automatic when there is no head row.** The invariant would otherwise forbid the very
  first install: there is no H to compare. A create writes the seed and records R = S, and it is safe
  because there is nothing to overwrite.
- **A tombstone is never automatic.** The start install leaves it alone (there is a head row), and
  `/admin/restore` writes it by decision (`shell-the-admin-tools`). Both stay; the verdict is `deleted`, and the only
  action is the restore the owner already has.
- **A retired key is never written.** A key that leaves the pack is left alone, and it is not offered
  an install.

**R dies with the page (owner's answer).** `ScrubPageAsync` deletes a tombstoned head and its
revisions, so R goes with them. That is the right answer and one consequence is worth recording: after
a scrub the key has no head, so the *next start* re-creates the seed and records a fresh R. A scrub is
therefore "forget this page's history", not "stop shipping this page" — the second is the `retired`
state, which only a release can create.

The backfill changes what a legacy row *reads*, not what may be written: R stays absent, so no repaired
row can be applied or adopted without a fresh write.

### D6 — Apply and adopt stop where the gate stops

Two writers, two behaviours, on purpose:

- **The upgrade apply** (`/admin/seed-apply`, new) walks the pack in ordinal key order. It writes every
  `release newer` row and every `new` row, adopts every row whose `H = S` and `R ≠ S` (that is a fact
  the store can verify, so the provenance write is not a guess), and skips the rows that are not its
  business: `in sync`, `deleted` and `retired`. It **stops at the first row that has drifted** —
  `edited`, `conflict`, or `unknown` without adopt — and names it. Rows that sort after it are not
  written. An unattended run should produce one thing for the operator to read, not a half-applied site
  with a page of outcomes.
- **The emergency restore** keeps reporting every outcome and continues past a failure
  (`shell-the-admin-tools`). It is the way back when the editor or its layout will not load; a run that stopped at the
  first broken seed would be useless exactly when it is needed. It also stays the deliberate
  overwrite-everything path: it writes the bundled source of every seed whose content differs, the
  `edited` and `conflict` rows included, because that is what the owner asked it for when the site is
  broken. Every write it makes records R = S.

**No routine install button on a row the gate refuses** (owner's answer). The page list offers the
verdict's action — apply for `release newer`, adopt for `store newer` (and for `unknown` when the
content already matches), and create for a `new` key on the apply page, which is the only place a key
with no row appears — and for `edited`, `conflict` and the rest it offers the diff and nothing else.
The deliberate overrides stay: edit the page by hand, restore a revision, or run `/admin/restore` when
the site is broken. That removes the blanket *Install from seed* link the list and the editor menu
carry today, and it is the reason the gate is the whole rule rather than a suggestion.

**The capability refuses too.** The button is not the boundary: the interface's own contract says a
second action, a reserved key or an ended render is a page bug and throws, and after IDEA-043 any page
may hold the write capability. So `InstallSeedAsync` computes the verdict before it writes and throws
`InvalidOperationException` — naming the verdict — unless the row is `new` or `release newer`; a row
already `in sync` stays the idempotent `Unchanged`, so a double-submitted form is not an error. The
confirmation page and its POST are the same call, so the markup and the authority cannot disagree, and
a hand-made form is refused like any other page bug. Adoption gets its own member,
`AdoptSeedAsync(key)`, which writes R only while the head's content hash still equals the shipped hash
and returns the outcome — `Unchanged` on a second press; the adopt button and the bulk apply both call
it.

Adopt is also a button on the row, because the page list is where the owner is looking when they see
`unknown` next to a page they wrote by hand.

### D7 — The listing gets cheaper and tells the truth

Today, for every live seeded row: one directory walk and one file read for `SeedSourceAsync`, one
`catalog.ReadAsync` charged against the render's allowance, and an ordinal compare of two strings that
produces one bit. Against the shipped pack that is 19 walks, 19 file reads, 19 charged reads and ~33% of
`Pages:Read:MaxSourceCharsPerRender` to render one column.

After: R and H come from the head row the listing already joins, S comes from the in-memory pack map,
and the verdict is a function call. `SeedSourceAsync` is called only for the rows whose diff the author
opens. Nothing else about the render changes.

The page has two tables, not three: **Pages** (live and seeded together, as the production page already
has them) and **Deleted pages**. The seed summary is a column on a live row, so a `retired` page is an
ordinary live page with its sentence, and a page the owner made has an empty cell. The editor menu
offers the verdict's action, not a blanket install.

### D8 — `SeedPaths()` goes, and the page stops asking

`IPageEditor.SeedPaths()` is deleted (owner's answer). Its callers are a test and the page list's
grouping; the test goes with it and the grouping becomes the verdict column. The editor menu asks
`SeedSourceAsync`, not the list, and export uses the seeder's static list directly.
`PageSeedInstaller.SeedPaths()` goes too. The pack's key list moves to
`PageSeeds.Paths`, which `/admin/export` keeps using: export keeps its two groups (*Live pages* and
*Seeded pages*) because a selection form has no use for seven verdicts, and its source of truth stops
being the seeder's static list. Adding `Seeds()` to the contract instead would have left two members
answering overlapping questions from the same in-memory list, and the note had to keep both only
because the page was still doing the comparison.

The page needs three things, and it has all three: the verdict per row (D3), the seed source for a
diff, and the verdict's action. **Bulk apply cannot be a page**, and this time for the right reason: not
key ownership — `/pages` *is* the editor and writes any key — but one action per render, so a
changeset of nineteen rows is nineteen submissions. The shell walks the pack; the page applies one row
per POST and `303`s back. (The batch split out of IDEA-043 will remove that limit; the route stays anyway, because an
apply runs when no browser is signed in.)

### D9 — The archive carries R, and the format says so

`PageArchiveFormat.Version` goes to 2, `Pages` gains `SeedHash`, and the reader accepts 1 and 2: a
version 1 archive has no provenance, so every seeded key in it imports with R absent. H is computed by
the write, so a version 1 page whose source matches the shipped seed reads `unknown` with adopt
offered, and one that differs reads `unknown`. That is safe and noisy, which is the right failure. An
import carries R through `PageArchiveEntry` into `PageTemplate`; the export writer always writes 2.

This is a decision the note asked for in a sentence and cannot have as a sentence: the column list is
validated exactly, `docs/storage.md` states the archive carries "source and state and nothing else",
and an import that dropped R would silently make every verdict `unknown` on the restored site.

### D10 — The junk goes first

`PageAssetSeeder.SeedAsync`'s second branch — replace a stored seed whose source names `PageDocument`
— is dead weight: nothing in the wild is written against a type that left the SDK before the first
deployment (owner's answer). It goes, with the `replaced` list it returns, the logging loop in
`Program.cs` and its four tests (one with two cases). *An edited seed that does not name it is left
alone* keeps its assertion folded into the write-count test that already covers it.

Removing it first is not tidying for its own sake: it is an automatic write that overwrites an author's
page, and under the gate it would be the one path that must be exempted. Deleting it means the gate
has no exemptions.

### D11 — The pack comes back from the store

The owner's question, and the part no hash answers: `/pages` and `/help` are edited in production, so
the repository's pack is behind the store and nothing brings it home except copying source out of the
editor. Provenance cannot help here — R says the *store* is ahead of the pack, and there is no record
at all for a key the store never installed.

**Git is the merge tool for the pack; the site is not.** So the command's job is only to get the bytes
out and into the working tree, and the review is `git diff`:

```
make seeds-list                    # every seeded key, its verdict and its state
make remote-seeds-list  HOST=...   # the same, against the running deployment
make seeds-pull                    # the stored source of every changed key, into Pages/Seeds
make remote-seeds-pull  HOST=...   # the same, from the running deployment
```

`seeds list` prints the verdict table from the terminal — the same `Core` function the page uses, so
the two cannot drift, and the plan is testable without a browser. It walks the union of the pack's keys
and the store's anchored keys, so it shows a `new` seed the store lacks and a `retired` page the pack
no longer ships; owner pages with no R and no S are omitted.

`seeds pull` emits a tar of only the keys whose stored source differs from the shipped file (stdout is
the tar; everything human goes to stderr), which `make` pipes through `tar -x -C $(SEED_DIRECTORY)`.
`--all` writes every key the pack ships and the store has live, changed or not. `--missing` names the
keys the pack ships and the store does not have live — no head, or a tombstone — which a pull cannot
fix and an install or restore must. Nothing is deleted: deletion is git's job.

The shape is the one `users` already has: a command word that runs before the host, with the store and
the connection string the deployment uses, plus `ssh` + `podman exec` make targets (`remote-user-list`
is the precedent). Four details make it work:

- **Every target depends on `build`**, as `user-list` does, so the pack the command compares is the one
  in the working tree after a pull.
- **The `seeds` command sends every log line to stderr.** The console sink writes to stdout by default
  and one warning in the middle of a tar is a corrupt pack; for a seeds command the console logger's
  `LogToStandardErrorThreshold` is set to `Trace`, so stdout carries the result and nothing else.
- **`make` refuses a dirty seed directory** unless `FORCE=1`, because the extraction would overwrite an
  uncommitted edit. The command cannot see git, and the remote form has no working tree at all, so the
  check belongs where the extraction happens.
- **A remote pull extracts on the local side**, which is the point: the ssh stdout is the tar.

It needs no schema change, no page capability and nothing from this plan's hashes, which is why it is
slice 1 and can land before the rest. It should land *first*, because the production `/pages` and
`/help` are already ahead of the pack and slices 2–3 edit both files.

### D12 — What it buys, and the honest limit

1. **An unattended upgrade becomes safe.** Today "apply the release to every page" is a coin flip on
   every page anyone edited; with the gate it writes only `release newer` rows and stops at the first
   that is not. That is the difference between a deploy the operator skims and one they can run.
2. **The rule in `/help` becomes enforceable.** "A change that alters what an author can do updates
   this page in the same change" works for seeds because seeds are the one thing the build can see; it
   is unenforceable for a site that already exists, because the operator cannot see which stored pages
   the release moved. The verdict is that sentence, made visible.
3. **The pack has a way home.** D11 is the first path from store to repository, and `store newer` plus
   adopt is what closes the loop once the harvested file ships.

**The limit** stands as the note put it: if deploys are always attended, the pack churns rarely, and one
owner reads the page list anyway, this is not worth two columns, a contract change, a migration and an
archive version. The cheaper thing is to keep the byte compare, rewrite the prompt so it stops
pretending to know why the bytes differ, and ship D11 alone — which is worth doing whatever happens to
the rest.

## What it touches

| Area | Change |
| --- | --- |
| `src/uk.osric.asp.Core/Pages/SeedComparison.cs` | New: the pure comparison over R, S and H, and the two permissions it implies |
| [`uk.osric.asp.Sdk/PageEntry.cs`](../../../../src/uk.osric.asp.Sdk/PageEntry.cs) | One new property, `Seed`, of a new `PageSeedSummary` value (strings and flags; `Sdk` references nothing) |
| [`uk.osric.asp.Sdk/IPageEditor.cs`](../../../../src/uk.osric.asp.Sdk/IPageEditor.cs) | `SeedPaths()` removed; `InstallSeedAsync` refuses a row the gate does not allow; `AdoptSeedAsync` added; `SeedSourceAsync` unchanged |
| `src/uk.osric.asp.Sdk/PublicAPI.Shipped.txt`, `PublicAPI.Unshipped.txt` | The new type and members declared; the removed one moved to `*REMOVED*` |
| [`Pages/PageHead.cs`](../../../../src/uk.osric.asp.Web/Pages/PageHead.cs) | `SeedHash`, `ContentHash` on the entity, with the model snapshot and a migration |
| [`Pages/SqlitePageStore.cs`](../../../../src/uk.osric.asp.Web/Pages/SqlitePageStore.cs) | `WriteHeadAsync` and `TryStoreRevisionAsync` write both, `COALESCE`d; an adopt that writes R only under `H = S`; `GetHeadAsync` reports both |
| [`Pages/PageHeadSummary.cs`](../../../../src/uk.osric.asp.Web/Pages/PageHeadSummary.cs) | Carries R and H to the shell's seed plan |
| [`Pages/PageAssetSeeder.cs`](../../../../src/uk.osric.asp.Web/Pages/PageAssetSeeder.cs) | Reads the pack through `PageSeeds`, records R = S on a create, and loses the `PageDocument` branch |
| [`Pages/PageTemplate.cs`](../../../../src/uk.osric.asp.Web/Pages/PageTemplate.cs) | The seed hash the write anchors to, so no write path can reach the store without deciding |
| [`Editor/PageSaver.cs`](../../../../src/uk.osric.asp.Web/Editor/PageSaver.cs), [`Editor/PageEditor.cs`](../../../../src/uk.osric.asp.Web/Editor/PageEditor.cs) | The saver carries the seed hash onto the revision it builds, including the compiler-fault revision; the editor exposes the adopt the shell calls |
| `src/uk.osric.asp.Web/Pages/PageContentHashRepair.cs` | New: the one-off content-only backfill, run after the migration and before the seeder |
| `src/uk.osric.asp.Web/Storage/Migrations/` | One migration: two nullable columns and a snapshot update, no backfill |
| `src/uk.osric.asp.Web/Pages/PageSeeds.cs` | New: the pack read once per process — keys, sources and hashes |
| [`Pages/PageSeedInstaller.cs`](../../../../src/uk.osric.asp.Web/Pages/PageSeedInstaller.cs) | The plan carries the verdict and the two permissions; `SeedState` becomes the Core comparison, and `SeedOutcome` stays what the write did |
| [`Pages/PageCatalogScope.cs`](../../../../src/uk.osric.asp.Web/Pages/PageCatalogScope.cs) | Builds `PageEntry.Seed` from the head row and the pack map |
| [`Pages/IPageReadStore.cs`](../../../../src/uk.osric.asp.Web/Pages/IPageReadStore.cs), [`SqlitePageReadStore.cs`](../../../../src/uk.osric.asp.Web/Pages/SqlitePageReadStore.cs) | `PageReadHead` carries R and H, from all three listing queries |
| `src/uk.osric.asp.Web/Pages/SeedVerdictWords.cs` | New: the labels and sentences, beside `PageImageStatus` |
| [`Pages/Seeds/pages`](../../../../src/uk.osric.asp.Web/Pages/Seeds/pages) | The listing renders the verdict and the verdict's action; the blanket *Install from seed* link goes; two tables |
| [`Pages/Seeds/help`](../../../../src/uk.osric.asp.Web/Pages/Seeds/help) | *Assets and recovery* and the editor's action list: what a verdict is, the two "we do not know" sentences, which rows offer an action, and the routine apply route beside the emergency restore |
| `src/uk.osric.asp.Web/Admin/ApplySeedsEndpoint.cs` | New: the routine apply, its preview and its POST |
| [`Admin/RestoreEndpoint.cs`](../../../../src/uk.osric.asp.Web/Admin/RestoreEndpoint.cs) | Reports verdicts instead of `current`/`changed`/`missing`/`deleted`; keeps continuing past a failure and writing every differing seed |
| [`Admin/PageArchiveFormat.cs`](../../../../src/uk.osric.asp.Web/Admin/PageArchiveFormat.cs), `PageArchiveReader/Writer`, [`PageArchiveEntry.cs`](../../../../src/uk.osric.asp.Web/Admin/PageArchiveEntry.cs), [`ImportProcessor.cs`](../../../../src/uk.osric.asp.Web/Admin/ImportProcessor.cs) | Version 2, one column, reader accepts 1 and 2; the import carries R into the revision it builds |
| `src/uk.osric.asp.Web/Pages/SeedCommands.cs`, [`Program.cs`](../../../../src/uk.osric.asp.Web/Program.cs) | The `seeds` command and its dispatch; the startup block runs the repair before the dispatch; the `PageDocument` logging loop goes |
| [`ReservedPaths.cs`](../../../../src/uk.osric.asp.Web/Pages/ReservedPaths.cs), `appsettings.json` | `/admin/seed-apply` joins both lists (a test holds them together) |
| [`Admin/AdminNavigation.cs`](../../../../src/uk.osric.asp.Web/Admin/AdminNavigation.cs), [`Assistant/ShellRoutes.cs`](../../../../src/uk.osric.asp.Web/Assistant/ShellRoutes.cs), [`Assistant/PageAuthorPrompt.cs`](../../../../src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs) | The new route described where the other admin routes are, in both directions the tests pin |
| [`Makefile`](../../../../Makefile) | `seeds-list`, `seeds-pull`, `remote-seeds-list`, `remote-seeds-pull`, in the shapes `user-list` and `remote-user-list` use, plus the dirty-directory guard |
| [`docs/storage.md`](../../storage.md) | Two columns and what writes them; the repair; the archive's column list and its version |
| [`docs/pages.md`](../../pages.md), [`docs/shell.md`](../../shell.md) | The listing's seed summary; `AdoptSeedAsync`; the gate's refusal; the apply route; the two tables |
| [`docs/glossary.md`](../../glossary.md) | *Seed*, *seed hash* and *verdict* — the anchor, and what an install records that a save does not |
| [`docs/jobs/README.md`](../../jobs/README.md), [`docs/decisions.md`](../../decisions.md) | The job moves to Closed with its summary and a programme row; five decision entries, named at the merge |
| `/seed-plan` (the store page) | Superseded: update it to point at this plan, or retire it — it is not in the repository, so it is also the first thing a harvest would find |

## Slices

Five, in this order. Slice 0 and slice 1 are independent of the rest and of each other; 2, 3 and 4
are sequential.

### 0 — The junk goes

Delete the `PageDocument` replacement: the branch and `replaced` list in
[`PageAssetSeeder.SeedAsync`](../../../../src/uk.osric.asp.Web/Pages/PageAssetSeeder.cs:16), the logging loop
in [`Program.cs`](../../../../src/uk.osric.asp.Web/Program.cs:340), and the four tests in
[`PageAssetSeederTests`](../../../../test/uk.osric.asp.Web.Tests/PageAssetSeederTests.cs:243), keeping
*an edited seed that does not name it is left alone* as an assertion on the existing write-count test.
`SeedAsync` returns `Task`. GREEN at the end.

### 1 — The pack comes home

`PageSeeds` (keys and sources; hashes in slice 2), `seeds list` and `seeds pull`, the four make targets,
the log-to-stderr rule and their tests. No schema change, no page capability. This is the slice that
fixes the problem the owner has today, and it can ship on its own.

**Run it once before slice 2 starts.** The production `/pages` and `/help` are ahead of the pack; the
harvest brings them into the working tree so slice 2 and slice 3 edit the pages the owner actually has,
rather than reintroducing the old ones.

### 2 — The fact, with nothing visible

The migration, the content-only repair, R and H on every write, the seeder recording R = S on a create,
and the archive's version 2. No verdict yet, no automatic write: the only observable change is that the
columns are filled and the archive carries R. The three tests that prove the write path — bytes agree,
the install runs once, a legacy row is repaired — are written first.

### 3 — The verdict

`SeedComparison` in `Core` and its words in `Web`, `PageEntry.Seed`, `PageHeadSummary` and
`PageReadHead` carrying R and H, `SeedPaths()` removed, the listing rendering verdicts and two tables,
`/help`, `/admin/export` reading `PageSeeds.Paths`. No automatic write yet: the gate applies to nothing
until slice 4, and the only behaviour change is the sentences and the action that appears on a row.

### 4 — Apply and adopt on the gate

`/admin/seed-apply` — the routine apply that writes `release newer`, creates `new`, adopts every
`H = S` row and stops at the first drifted one — plus the list's one-row apply per POST, the adopt
button and the capability's refusal. `/admin/restore` gains the verdicts and keeps its
report-everything, overwrite-everything behaviour. `/help` gains the route and the adopt action in the
same change, as the author-manual rule asks.

## Acceptance tests

1. **Bytes agree.** On an empty store, after the first start, for every seed: the head's `ContentHash`
   = the pack's hash = the recorded `SeedHash`, and the verdict is `in sync`. This is the test for D2,
   and it is written first.
2. **The install runs once.** Restart: no new revisions, every verdict still `in sync`.
3. **An edit is not an update.** Edit a seeded page, restart: the edit survives, R is unchanged, H
   moves, the verdict is `edited`, and nothing auto-applies.
4. **The repair touches H and only H.** A store written before the change, with a seeded page matching
   its seed, a seeded page edited, and a page the owner made: after startup, H is set on all three, R
   stays null on all three, and the verdicts are `store newer` (adopt offered), `unknown`, and no
   summary. Run the startup again: no second write.
5. **A release update applies once.** With a fixture pack that moves one seed: a restart changes
   nothing on its own; the verdict is `release newer`; apply saves one revision, R becomes S, the
   verdict becomes `in sync`; a second apply writes nothing.
6. **Both moved blocks.** Edit the page and move the seed: the verdict is `conflict`; the bulk apply
   writes the safe rows that sort before it, names it as the row it stopped at, and writes nothing
   after it.
7. **Unknown says unknown.** A page at a seeded key with no R: the verdict is `unknown`, never
   automatic, and the rendered sentence is asserted as a string so nobody reintroduces "you edited it".
8. **A tombstone is `deleted`, and both paths keep their decision.** Tombstone a seeded page and
   restart: untouched, verdict `deleted`. Then `/admin/restore`: restored, R = S.
9. **Retired is left alone and reads as a live page.** Remove a seed from the pack: the verdict is
   `retired`, the page is untouched, it is offered no install, and it is listed under *Pages* with the
   other live rows.
10. **A scrub forgets, and the next start re-creates.** Scrub a seeded page, then restart: the seed is
    created afresh with a new R, and the verdict is `in sync`.
11. **Import round-trips provenance.** Export and import: verdicts are preserved. Import a version 1
    archive: every seeded key has no R, so one whose source matches the shipped seed reads `unknown`
    with adopt offered, and nothing auto-applies.
12. **Harvest ends in `store newer` and adopt closes it.** A pack that ships the store's current
    content: the verdict is `store newer`; adopt writes R = S and stores no revision; the verdict
    becomes `in sync`. A legacy page with R absent in the same state is `unknown` with adopt offered.
13. **One rule, three readers.** `seeds list`, the page list and the bulk apply agree about every row
    of a fixture pack — the test that keeps the verdict out of two implementations.
14. **The action follows the gate, and so does the capability.** The rendered listing offers apply on a
    `release newer` row and adopt on a `store newer` row, and offers neither on an `edited` or
    `conflict` row — only the diff. A direct `InstallSeedAsync` call on an `edited` row throws, so a
    hand-made form cannot do what the page will not offer.
15. **The command's two directions.** `seeds list` reports a `new` key the store lacks, a `retired`
    key the pack no longer ships, and omits a page the owner made. `seeds pull` emits exactly the
    changed keys as a tar, `--all` emits the lot, `--missing` names what a pull cannot fix, and a pull
    with nothing to send writes a valid empty tar, so `make`'s extraction succeeds.
16. **The command has two streams.** `seeds pull` writes the tar to its output and every human line to
    its error stream; the console sink's threshold to standard error is checked in verification 3,
    where the tar is read back.
17. **Reserved keys are never seeds**, and a seed install cannot reach one (today's test, kept);
    `/admin/seed-apply` is reserved in both the configuration and the default list.

## Verification

1. `make check` — format, licences, audit, the seeded-page check, every suite and the Release publish.
   The migration, the two seeded pages and the reserved-path test are the parts most likely to need a
   fix.
2. `make seeds-list` against a store written before the change: H is repaired, R is absent, the
   verdicts split as test 4 says. Then a second run: nothing repaired.
3. `make seeds-pull` after an edit in the store: only that key arrives, `git diff` shows it, and a
   dirty seed directory is refused without `FORCE=1`.
4. `make run`, then: `/pages` on a fresh store (all `in sync`), edit one seeded page (`edited`), and
   `make seeds-list` (the same verdicts).
5. `/admin/restore` and `/admin/seed-apply` on a fixture pack that moves one seed: apply writes one
   revision; edit plus move gives `conflict` and stops.
6. Export, import into an empty store, and a read of the verdicts; then a version 1 archive, whose
   seeded keys must read `unknown`, with adopt offered where the source matches the shipped seed.
7. `make image` and the harness against the container, because the pack is read from beside the
   assembly and the command reads the deployment's storage. Then `make remote-seeds-list` and
   `make remote-seeds-pull` against the deployment.

## Risks

- **The repair writes every head once.** 20-ish rows on the owner's site; a one-off read of the head
  revision per page. It does not invent R, so nothing becomes automatic, but it does make a legacy
  seeded page `store newer` rather than `unknown`, which is the intent.
- **The verdict could still drift.** Three readers, one `Core` function, and test 13 — but the words
  live in `Web` and could diverge from the enum. The sentences are asserted by string in tests 7 and 12, which
  pins the ones that matter.
- **Two columns on every write.** A save now writes two 64-character strings on the head. At this size
  it is noise; the storage sampler measures `PageRevisions` and `PageState` and does not need changing.
- **The archive's version 2 is a one-way door for old builds.** A version 2 archive cannot be imported
  by a build that knows only version 1, which is correct and worth saying in `docs/storage.md`.
- **`seeds pull` writes into the working tree.** It is a tar extraction, so `--force`-free and
  reviewable by `git diff`, but it would overwrite an uncommitted edit: `make` refuses a dirty seed
  directory unless `FORCE=1`.
- **A harvested pack can ship a broken page.** The pack is compiled by `make check-pages` in the same
  gate as everything else, so a page harvested from a broken store fails the build — which is the
  second reason the harvest belongs in the repository and not in a template.
- **IDEA-043 is in flight.** It renames `IPageEditor` to `IPageWrites`; if it lands first, the
  `SeedPaths()` removal and the `PageEditorScope` edits rebase onto the new name and this plan's
  `IPageEditor` references become `IPageWrites`. The batch split out of it adds the batch and removes
  the one-action-per-render rule D8 leans on; the apply route stays regardless, because an apply is run
  when no browser is signed in, but the page's one-row POST may become a batch caller later. Neither
  changes the row, the verdict or the gate.
- **The harvest races slices 2–3.** Production `/pages` and `/help` are already ahead of the pack. If
  the harvest is not run first, slice 3's page edits reintroduce the old pages, or leave the store
  `store newer` against a pack that no longer matches the site. Slice 1 exists to make that a
  deliberate step, not a surprise.

## Not in scope

- **A page that serves the pack.** The owner asked for the command and make, not a download page; an
  archive is not a form ([IDEA-024](../../jobs/IDEA-024.md)), and the owner is at a terminal when they
  mean to commit.
- **A pipeline that emits an importable database when seeds change.** Wrong shape: the archive is
  all-or-nothing over the store, so importing one clobbers local edits, discards revisions and bypasses
  the conditional save. It also buys nothing for a fresh site. `/admin/export` and `/admin/import` stay
  an operator's backup and drill.
- **A build-generated manifest of the pack.** Unnecessary: the pack is read at start anyway, and hashing
  the same strings `SeedSourceAsync` hands to the save path leaves no artefact to drift and no pipeline
  step to forget.
- **A commit per page, or one "synced at" commit for the store.** The commits add almost nothing to
  the comparison; a store-wide commit goes stale the moment part of a changeset is applied. Per-key
  hashes, no commits.
- **A marker comment in the page source.** It pollutes the source and breaks the byte compare it would
  be carrying. Provenance is data about the row, not text in the page.
- **Fingerprinting, or a check-in/check-out lock.** Same job, more state, and a lock is wrong for a
  one-owner site.
- **A fourth comparison for the harvest.** The temptation is a "pack newer than store" hash direction.
  Git already answers it, with history, merge and review; a second answer would disagree with it.
- **Filtering the listing to only the rows a release moved.** It is a query parameter over the same
  verdict whenever the pack is big enough to need it, and it is not that big.

**Origin:** the owner's note at `/seed-plan`, a stored page that is not part of this repository,
reviewed against `trunk` on 2026-11-15 and recorded as [IDEA-036](../../jobs/README.md#closed). The review
changed three things: the revision hash is an identity, so the content hash has to be a second column
(D1); the verdict is the host's answer rather than three hashes handed to the page (D3); and a
harvested pack makes `store newer` an ordinary row rather than an optional repair (D4, D11). The owner
also decided, on the first branch, that R dies with a scrub (D5), that the `PageDocument` replacement is
deleted (D10), and that `SeedPaths()` goes (D8); and on the revision, the seven answers at the top of
this document.

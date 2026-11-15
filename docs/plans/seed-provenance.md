<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Seed provenance

| | |
| --- | --- |
| Status | Approved 2026-11-15 — the owner accepted the plan and asked for it on `trunk`. Nothing below is built; the four questions at the end are still open |
| Size | Large — a migration with two columns, one save-path fact, one pure verdict function and its words, one field on the page contract and one member removed, one shell action, an archive format version, a `seeds` command with two make targets, and the prose and tests for each. Slices 0 and 1 are small and independent; slice 2 is the bulk |
| Spike | None. Every number and every claim below was measured on `trunk` at `1badddd`; the two places the design could fail are the revision hash (settled: it is an identity, see D1) and the archive reader (settled by the format's own version column) |
| Decisions | None recorded yet. They are numbered from `trunk` at the merge (DEC-09); this branch refers to them by name |
| Backlog | [IDEA-036](../jobs/IDEA-036.md). Relates to [IDEA-024](../jobs/IDEA-024.md) (an archive is not a form) and [IDEA-030](../jobs/IDEA-030.md) (every view writes its own navigation) |

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

## What the store can say today

| Path | What it knows | Where |
| --- | --- | --- |
| The install at start | A seed's page when the key has no head row, so a local edit survives a restart. Reported nowhere. It also *replaces* a stored seed whose source names `PageDocument` — a silent overwrite of the author's page, and junk (D10) | [`PageAssetSeeder.SeedAsync`](../../src/uk.osric.asp.Web/Pages/PageAssetSeeder.cs:16), [`Program.cs`](../../src/uk.osric.asp.Web/Program.cs:312) |
| `SeedPaths()`, `SeedSourceAsync`, `InstallSeedAsync` | The pack's keys, one seed's source, and a conditional install against the head hash. Offered to the editor page only | [`IPageEditor`](../../src/uk.osric.asp.Sdk/IPageEditor.cs:38), [`PageEditorScope`](../../src/uk.osric.asp.Web/Pages/PageEditorScope.cs:123) |
| `/admin/restore` | Every shipped key with `current`, `changed`, `missing` or `deleted`, and one POST that writes each of them and reports every outcome (DEC-256) | [`RestoreEndpoint`](../../src/uk.osric.asp.Web/Admin/RestoreEndpoint.cs:54) |
| The page list's byte compare | Live source against seed source as strings: differ, or do not. Two reads per live seeded row | [`pages`](../../src/uk.osric.asp.Web/Pages/Seeds/pages:460) |

The facts that shape the design, each checked in the code:

| Fact | Consequence |
| --- | --- |
| The revision hash is an **identity**, not content: `RevisionHash.Of` frames the source with `creationRuntimeId`, `savedAt` and `revisionId` under a `page-revision-v1` prefix | H can never be the head's `Hash`. A content hash has to be recorded separately (D1) |
| A plain content hash already exists: `TemplateFingerprint.Of`, the lowercase SHA-256 of the template's UTF-8, chosen for exactly this kind of comparison | R, S and H are all that function. There is no new algorithm and no wire format (D2) |
| `PageRevisions.TemplateHash` holds it, but only from the image build — a second statement after the revision exists, absent for a page the start seeder wrote (no image at all) and for a compiler fault | `TemplateHash` cannot be the gate's H (D1) |
| Nothing normalises the source: no line-ending or BOM rewriting in the save path, and `PageSourceChange.IsNoOp` is an ordinal compare | The note's "constraint that can kill it" is already satisfied; the acceptance test asserts it anyway (D2) |
| The seeds are `Content` files copied beside the assembly and read with `File.ReadAllTextAsync`, not embedded resources | Hashing the pack at start reads the files the seeder already reads. There is no artefact to drift (D2) |
| `SeedSourceAsync` resolves a key by enumerating the whole pack (`FindSeedFile` → `SeedFiles()` → `Directory.GetFiles(...AllDirectories)`) | The list does 1 + one directory enumeration per seeded row per render. One in-memory pack map removes the scans as well as the reads (D7) |
| The pack is 19 files, 163,869 bytes; the listing charges each live seeded row's source to `Pages:Read:MaxSourceCharsPerRender` (524,288) | The bit costs ~31% of the render's allowance (D7) |
| A tombstone is a `PageHeads` row with `IsDeleted = 1`; `ScrubPageAsync` deletes the head and its revisions | R on the head row survives a delete and dies with a scrub (D5) |
| `/admin/restore` writes a tombstoned seed by decision (DEC-256); the start install leaves it alone | The two paths disagree on purpose, and both behaviours stay: the verdict says which is which (D5) |
| `Editor:Key` is `/pages`, so the page list holds `IPageEditor` and can write any key | "A page cannot write a key it does not own" is false here. The real limit is one action per render (D8) |
| The archive is a versioned SQLite database whose column list is validated exactly, and it carries `Path` and `Source` only | "R belongs in the export" is a format change, not an additive one (D9) |
| `users` is a user command that runs before the host, and `make` has `remote-user-*` targets over `ssh` + `podman exec` | The shape for a `seeds` command and its two remote targets already exists (D11) |

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
leaves whatever is recorded, and a seed write passes the hash it is installing. It is the same
transaction as the revision insert, so a page and its provenance cannot disagree.

H does not live on the revision even though `PageRevisions.TemplateHash` already holds exactly this
value, because that column is written by a second statement from the image build and is absent for
the two rows that matter most — a seed the start seeder created (which has no image at all) and any
save that hit a compiler fault. The gate must not depend on whether a page has been compiled.

**Rejected: compare against `PageEntry.Hash`.** It is an identity and changes on every save, so every
seeded page would read `edited` forever — the failure mode that hides, which the note warns about.

### D2 — One function, applied to the exact bytes

`TemplateFingerprint.Of` produces R, S and H. S is computed once per process while the pack is read,
into one map:

```csharp
// PageSeeds: the pack, read once per process.
IReadOnlyDictionary<string, string> Hashes;   // canonical key → TemplateFingerprint.Of(seed text)
```

The pack is read at start anyway (`SeedAsync` already reads every file). Building the map there pays
for itself immediately: it removes a directory walk per `SeedSourceAsync` call (D7).

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
// Core/Pages/SeedVerdict.cs — pure, no I/O.
public static SeedVerdict Of(string? recorded, string? shipped, string? content);

// Sdk/PageEntry.cs — the host's answer, in the shape PageEntry.State already has.
public PageSeedSummary Seed { get; init; }
```

`PageSeedSummary` is a value with no behaviour: the verdict, the sentence, and whether the row may be
installed or adopted. The page renders the sentence and shows a button per flag; it never compares
hashes, and it cannot disagree with the shell about what may be written. `SeedPaths()` goes (D8) and
`PageSource` is untouched — the diff view needs the seed *source*, which `SeedSourceAsync` already
returns.

The split of responsibilities is the one the architecture already asks for: the comparison is a pure
function over three values in `Core`; the words are one `Web` class beside `PageImageStatus`; the
page contract carries the answer (`PageEntry.State` is the precedent — "the marks are the host's own
rules reported rather than derived").

### D4 — Seven verdicts, and the two that must not borrow words

| Verdict | R vs S | H | Automatic | Words |
| --- | --- | --- | --- | --- |
| `in sync` | equal | H = R | — | Holds the seed this release ships. |
| `release newer` | differ | H = R | **apply** | Release 9f3a… updated this page, and you have not changed it since it was installed. Applying saves the new seed; the diff shows what changes. |
| `edited` | equal | H ≠ R | no | Changed since the seed last set it. Applying would replace that change; the diff shows both. |
| `conflict` | differ | H ≠ R | no | Changed since the seed last set it, and this release changed the seed too. Compare, then choose. |
| `store newer` | differ | H = S | **adopt** | Holds the bytes this release ships, but is not recorded as installed from it. Recording it as installed makes it a seeded page and keeps this content. |
| `unknown` | — (R absent) | any | no | **No record of this page coming from a seed.** Applying would replace whatever is there. |
| `retired` | — (S absent) | any | no | This release no longer ships this page. It is left alone. |

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
action is.

### D5 — The gate, and the three rows that are not hashes

`H = R and S ≠ R` is the whole automatic rule, and it applies to content writes only. Three rows are
decided before it:

- **A create is automatic when there is no head row.** The invariant would otherwise forbid the very
  first install: there is no H to compare. A create writes the seed and records R = S, and it is safe
  because there is nothing to overwrite.
- **A tombstone is never automatic.** The start install leaves it alone (there is a head row), and
  `/admin/restore` writes it by decision (DEC-256). Both stay; the verdict is `deleted`, and the only
  action is the restore the owner already has.
- **A retired key is never written.** A key that leaves the pack is left alone, and it is not offered
  an install.

**R dies with the page (owner's answer, this branch).** `ScrubPageAsync` deletes a tombstoned head and
its revisions, so R goes with them. That is the right answer and one consequence is worth recording:
after a scrub the key has no head, so the *next start* re-creates the seed and records a fresh R. A
scrub is therefore "forget this page's history", not "stop shipping this page" — the second is the
`retired` state, which only a release can create.

### D6 — Apply and adopt stop where the gate stops

Two writers, two behaviours, on purpose:

- **The upgrade apply** (new, in the shell beside `/admin/restore`) writes every `release newer` row,
  adopts every `store newer` row — `H = S` is a fact the store can verify, so the provenance write is
  not a guess — and **stops at the first row it may not write**, naming it. An unattended run should
  produce one thing for the operator to read, not a half-applied site with a page of outcomes.
- **The emergency restore** keeps reporting every outcome and continues past a failure (DEC-256). It is
  the way back when the editor or its layout will not load; a run that stopped at the first broken seed
  would be useless exactly when it is needed.

Adopt is also a button on the row, because the page list is where the owner is looking when they see
`unknown` next to a page they wrote by hand.

### D7 — The listing gets cheaper and tells the truth

Today, for every live seeded row: one directory walk and one file read for `SeedSourceAsync`, one
`catalog.ReadAsync` charged against the render's allowance, and an ordinal compare of two strings that
produces one bit. Against the shipped pack that is 19 walks, 19 file reads, 19 charged reads and ~31% of
`Pages:Read:MaxSourceCharsPerRender` to render one column.

After: R and H come from the head row the listing already joins, S comes from the in-memory pack map,
and the verdict is a function call. `SeedSourceAsync` is called only for the rows whose diff the author
opens. Nothing else about the render changes.

### D8 — `SeedPaths()` goes, and the page stops asking

`IPageEditor.SeedPaths()` is deleted (owner's answer, this branch). Its callers are the page list's
grouping, `/admin/export`'s grouping, `PageSeedInstaller` and `PageEditorScope`; the first two group by
the verdict now, and the last two use the shell's own pack map. Adding `Seeds()` instead would have
left two members answering overlapping questions from the same in-memory list, and the note had to
keep both only because the page was still doing the comparison.

The page needs three things, and it has all three: the verdict per row (D3), the seed source for a
diff, and the install action. **Bulk apply cannot be a page**, and this time for the right reason: not
key ownership — `/pages` *is* the editor and writes any key — but one action per render, so a
changeset of nineteen rows is nineteen submissions. The shell walks the pack; the page applies one row
per POST and `303`s back.

### D9 — The archive carries R, and the format says so

`PageArchiveFormat.Version` goes to 2, `Pages` gains `SeedHash`, and the reader accepts 1 and 2: a
version 1 archive has no provenance, so every seeded key in it imports as `unknown` and nothing
auto-applies. That is safe and noisy, which is the right failure. The export writer always writes 2.

This is a decision the note asked for in a sentence and cannot have as a sentence: the column list is
validated exactly, `docs/storage.md` states the archive carries "source and state and nothing else",
and an import that dropped R would silently make every verdict `unknown` on the restored site.

### D10 — The junk goes first

`PageAssetSeeder.SeedAsync`'s second branch — replace a stored seed whose source names `PageDocument`
— is dead weight: nothing in the wild is written against a type that left the SDK before the first
deployment (owner's answer, this branch). It goes, with the `replaced` list it returns, the three
lines in `Program.cs` that log it, and its three tests. One of the three, *an edited seed that does not
name it is left alone*, keeps its assertion folded into the write-count test that already covers it.

Removing it first is not tidying for its own sake: it is an automatic write that overwrites an author's
page, and under the gate it would be the one path that must be exempted. Deleting it means the gate has
no exemptions.

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
make remote-seeds-pull  HOST=...   # the pack's stored source for every changed key, into Pages/Seeds
```

`seeds list` prints the verdict table from the terminal — the same `Core` function the page uses, so
the two cannot drift, and the plan is testable without a browser. `seeds pull` emits a tar of only the
keys whose stored source differs from the shipped file (stdout is the tar, everything human goes to
stderr), which `make` pipes through `tar -x -C $(SEED_DIRECTORY)`; `--all` writes the whole pack and
`--prune` names the keys the pack ships and the store no longer has, without deleting anything —
deletion is git's job.

The shape is the one `users` already has: a command word that runs before the host, with the store and
the connection string the deployment uses, plus `ssh` + `podman exec` make targets
(`remote-user-list` is the precedent). It needs no schema change, no page capability and nothing from
this plan's hashes, which is why it is slice 1 and can land before the rest.

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
| `src/uk.osric.asp.Core/Pages/SeedVerdict.cs` | New: the pure verdict function over R, S and H |
| [`uk.osric.asp.Sdk/PageEntry.cs`](../../src/uk.osric.asp.Sdk/PageEntry.cs) | One new property, `Seed`, of a new `PageSeedSummary` value |
| [`uk.osric.asp.Sdk/IPageEditor.cs`](../../src/uk.osric.asp.Sdk/IPageEditor.cs) | `SeedPaths()` removed; `SeedSourceAsync` and `InstallSeedAsync` unchanged |
| `src/uk.osric.asp.Sdk/PublicAPI.Shipped.txt`, `PublicAPI.Unshipped.txt` | The new type and members declared; the removed one moved to `*REMOVED*` |
| [`Pages/PageHead.cs`](../../src/uk.osric.asp.Web/Pages/PageHead.cs) | `SeedHash`, `ContentHash` |
| [`Pages/SqlitePageStore.cs`](../../src/uk.osric.asp.Web/Pages/SqlitePageStore.cs) | `WriteHeadAsync` writes both, `COALESCE`d; the seed hash threaded from `SavePageAsync`, `TryStoreRevisionAsync` and the batch save |
| [`Pages/PageTemplate.cs`](../../src/uk.osric.asp.Web/Pages/PageTemplate.cs) | The seed hash the write anchors to, so no write path can reach the store without deciding |
| `src/uk.osric.asp.Web/Storage/Migrations/` | One migration: two nullable columns, no backfill |
| `src/uk.osric.asp.Web/Pages/PageSeeds.cs` | New: the pack read once per process — keys, hashes, sources |
| [`Pages/PageSeedInstaller.cs`](../../src/uk.osric.asp.Web/Pages/PageSeedInstaller.cs) | The plan gains R, S and H per key and reports the verdict |
| [`Pages/PageCatalogScope.cs`](../../src/uk.osric.asp.Web/Pages/PageCatalogScope.cs) | Builds `PageEntry.Seed` from the head row and the pack map |
| `src/uk.osric.asp.Web/Pages/SeedVerdictWords.cs` | New: the sentences, beside `PageImageStatus` |
| [`Pages/Seeds/pages`](../../src/uk.osric.asp.Web/Pages/Seeds/pages) | The listing renders the verdict and offers install/replace/adopt per flag; the editor menu's seed action follows the verdict |
| [`Pages/Seeds/help`](../../src/uk.osric.asp.Web/Pages/Seeds/help) | *Assets and recovery*: what a verdict is, and the two "we do not know" sentences |
| `src/uk.osric.asp.Web/Admin/ApplySeedsEndpoint.cs` | New: the upgrade apply, beside `/admin/restore` |
| [`Admin/RestoreEndpoint.cs`](../../src/uk.osric.asp.Web/Admin/RestoreEndpoint.cs) | Reports verdicts instead of `current`/`changed`/`missing`/`deleted`; keeps continuing past a failure |
| [`Admin/PageArchiveFormat.cs`](../../src/uk.osric.asp.Web/Admin/PageArchiveFormat.cs), `PageArchiveReader/Writer` | Version 2, one column, reader accepts 1 and 2 |
| `src/uk.osric.asp.Web/Pages/SeedCommands.cs`, [`Program.cs`](../../src/uk.osric.asp.Web/Program.cs) | The `seeds` command and its dispatch; the `PageDocument` logging loop goes |
| [`Makefile`](../../Makefile) | `seeds-list`, `seeds-pull`, `remote-seeds-list`, `remote-seeds-pull`, in the shapes `user-list` and `remote-user-list` use |
| [`docs/storage.md`](../storage.md) | Two columns and what writes them; the archive's column list and its version |
| [`docs/pages.md`](../pages.md), [`docs/shell.md`](../shell.md) | The listing's seed summary; the apply route |
| [`docs/jobs/README.md`](../jobs/README.md), [`docs/decisions.md`](../decisions.md) | The job moves to Closed with its summary; one decision entry, numbered at the merge |
| `/seed-plan` (the store page) | Superseded: update it to point at this plan, or retire it — it is not in the repository, so it is also the first thing a harvest would find |

## Slices

Four, in this order. 0 and 1 are independent of the rest and of each other.

### 0 — The junk goes

Delete the `PageDocument` replacement: the branch and `replaced` list in
[`PageAssetSeeder.SeedAsync`](../../src/uk.osric.asp.Web/Pages/PageAssetSeeder.cs:16), the logging loop
in [`Program.cs`](../../src/uk.osric.asp.Web/Program.cs:312), and the three tests in
[`PageAssetSeederTests`](../../test/uk.osric.asp.Web.Tests/PageAssetSeederTests.cs:242), keeping
*an edited seed that does not name it is left alone* as an assertion on the existing write-count test.
`SeedAsync` returns `Task`. GREEN at the end.

### 1 — The pack comes home

`seeds list` and `seeds pull`, the `PageSeeds` pack map they share with everything else, the four make
targets, and their tests. No schema change, no page capability. This is the slice that fixes the
problem the owner has today, and it can ship on its own.

### 2 — The fact and the verdict

The migration, R and H written by every save and by both seed writes, `SeedVerdict` in `Core` and its
words in `Web`, `PageEntry.Seed`, `SeedPaths()` removed, the page list rendering verdicts, `/help`, and
the archive's version 2. No automatic write yet: the gate applies to nothing until slice 3, and the
only behaviour change is the sentences.

### 3 — Apply and adopt on the gate

`/admin/seed-apply` — the bulk apply that writes `release newer`, adopts `store newer` and stops at the
first row it may not write — plus the list's one-row install per POST and the adopt button. `/admin/restore`
gains the verdicts and keeps its report-everything behaviour.

## Acceptance tests

1. **Bytes agree.** On an empty store, after the first start, for every seed: the head's `ContentHash`
   = the pack's hash = the recorded `SeedHash`, and the verdict is `in sync`. This is the test for D2,
   and it is written first.
2. **The install runs once.** Restart: no new revisions, every verdict still `in sync`.
3. **An edit is not an update.** Edit a seeded page, restart: the edit survives, R is unchanged, H
   moves, the verdict is `edited`, and nothing auto-applies.
4. **A release update applies once.** With a fixture pack that moves one seed: a restart changes nothing
   on its own; the verdict is `release newer`; apply saves one revision, R becomes S, the verdict becomes
   `in sync`; a second apply writes nothing.
5. **Both moved blocks.** Edit the page and move the seed: the verdict is `conflict`; the bulk apply
   writes the rows around it and names it as the row it stopped at.
6. **Unknown says unknown.** A page at a seeded key with no R: the verdict is `unknown`, never
   automatic, and the rendered sentence is asserted as a string so nobody reintroduces "you edited it".
7. **Retired is left alone.** Remove a seed from the pack: the verdict is `retired`, the page is
   untouched, and it is offered no install.
8. **A tombstone is `deleted`, and both paths keep their decision.** Tombstone a seeded page and
   restart: untouched, verdict `deleted`. Then `/admin/restore`: restored, R = S.
9. **A scrub forgets, and the next start re-creates.** Scrub a seeded page, then restart: the seed is
   created afresh with a new R, and the verdict is `in sync`.
10. **Import round-trips provenance.** Export and import: verdicts are preserved. Import a version 1
    archive: every seeded key is `unknown` and nothing auto-applies.
11. **Harvest ends in `store newer` and adopt closes it.** A pack that ships the store's current
    content: the verdict is `store newer`; adopt writes R = S and stores no revision; the verdict
    becomes `in sync`. A page whose R is absent in the same state is `unknown` with adopt offered.
12. **One rule, three readers.** `seeds list`, the page list and the bulk apply agree about every row
    of a fixture pack — the test that keeps the verdict out of two implementations.
13. **Reserved keys are never seeds**, and a seed install cannot reach one (today's test, kept).

## Verification

1. `make check` — format, licences, audit, the seeded-page check, every suite and the Release publish.
   The migration and the two seeded pages are the parts most likely to need a fix.
2. `make run`, then: `/pages` on a fresh store (all `in sync`), edit one seeded page (→ `edited`), and
   `make seeds-list` (the same verdicts).
3. `/admin/restore` and `/admin/seed-apply` on a fixture pack that moves one seed: apply writes one
   revision; edit plus move gives `conflict` and stops.
4. Export, import into an empty store, and a read of the verdicts; then a version 1 archive, which
   must read `unknown` everywhere.
5. `make image` and the harness against the container, because the pack is read from beside the
   assembly and the command reads the deployment's storage.

## Risks

- **A legacy row reads `unknown` until its next save.** R and H are null on every existing row and
  there is no backfill (the DEC-240 precedent). Safe, and noisy for exactly one release; the owner's
  site has ~20 such rows. A backfill is possible (H is computable from stored source) and is
  deliberately not in this plan: it would be the one place the two hashes are computed apart.
- **The verdict could still drift.** Three readers, one `Core` function, and test 12 — but the words
  live in `Web` and could diverge from the enum. The sentences are asserted by string in test 6, which
  pins the two that matter.
- **Two columns on every write.** A save now writes two 64-character strings on the head. At this size
  it is noise; the storage sampler measures `PageRevisions` and `PageState` and does not need changing.
- **The archive's version 2 is a one-way door for old builds.** A version 2 archive cannot be imported
  by a build that knows only version 1, which is correct and worth saying in `docs/storage.md`.
- **`seeds pull` writes into the working tree.** It is a tar extraction, so `--force`-free and
  reviewable by `git diff`, but the tar would overwrite an uncommitted edit. The command refuses when
  `Pages/Seeds` is dirty unless `--force`; `make` is not the place for that check.
- **A harvested pack can ship a broken page.** The pack is compiled by `make check-pages` in the same
  gate as everything else, so a page harvested from a broken store fails the build — which is the
  second reason the harvest belongs in the repository and not in a template.

## Questions for the owner

1. **The harvest shape.** `seeds pull` emitting a tar on stdout that `make` extracts into
   `Pages/Seeds`, with `git diff` as the review — accept, or would you rather have a page-driven
   download (`/admin/seed-pack`) that works with no shell? A page can serve the same tar, and the
   argument against it is that an archive is not a form (IDEA-024); the argument for it is that the
   owner is already in a browser when they finish editing `/pages`.
2. **Does the bulk apply adopt, or only apply?** This plan has it adopt `store newer` rows, because
   `H = S` is a fact the store verifies and it is what clears a harvest in one run. The alternative is
   that adopt is always a button, and a harvested pack stays `store newer` until someone presses it.
3. **Three headings or four?** `/pages` has *Live pages*, *Seeded pages* and *Deleted pages*. A retired
   row would sit under *Seeded pages* with the word `retired`. A fourth heading is honest and a fourth
   table for a case that should be rare.
4. **`/admin/seed-apply` as a new route**, beside `/admin/restore`, rather than widening restore with a
   mode. Two routes say "the emergency one" and "the routine one" in the URL; one route with a mode
   would need the operator to know which mode they are in while looking at a button.

## Not in scope

- **A pipeline that emits an importable database when seeds change.** Wrong shape: the archive is
  all-or-nothing over the store, so importing one clobbers local edits, discards revisions and bypasses
  the conditional save. It also buys nothing for a fresh site. `/admin/export` and `/admin/import` stay
  an operator's backup and drill.
- **A build-generated manifest of the pack.** Unnecessary: the pack is read at start anyway, and hashing
  the same strings `SeedSourceAsync` hands to the save path leaves no artefact to drift and no pipeline
  step to forget.
- **A commit per page, or one "synced at" commit for the store.** The commits add almost nothing to the
  comparison; a store-wide commit goes stale the moment part of a changeset is applied. Per-key hashes,
  no commits.
- **A marker comment in the page source.** It pollutes the source and breaks the byte compare it would
  be carrying. Provenance is data about the row, not text in the page.
- **Fingerprinting, or a check-in/check-out lock.** Same job, more state, and a lock is wrong for a
  one-owner site.
- **A fourth comparison for the harvest.** The temptation is a "pack newer than store" hash direction.
  Git already answers it, with history, merge and review; a second answer would disagree with it.
- **Filtering the listing to only the rows a release moved.** T2 in the note had it; it is a query
  parameter over the same verdict whenever the pack is big enough to need it, and it is not that big.

**Origin:** the owner's note at `/seed-plan`, a stored page that is not part of this repository,
reviewed against `trunk` on 2026-11-15 and recorded as [IDEA-036](../jobs/IDEA-036.md). The review
changed three things: the revision hash is an identity, so the content hash has to be a second column
(D1); the verdict is the host's answer rather than three hashes handed to the page (D3); and a
harvested pack makes `store newer` an ordinary row rather than an optional repair (D4, D11). The owner
also decided, on this branch, that R dies with a scrub (D5), that the `PageDocument` replacement is
deleted (D10), and that `SeedPaths()` goes (D8).

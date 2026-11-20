<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A release applies its seed before the host listens

| | |
| --- | --- |
| Status | Approved 2026-11-20 — the owner said to build it. Being built on `idea/seed-auto-apply-at-startup`; nothing below is merged |
| Size | Small: one startup step over the pack the seeder already reads, one type, one log line per row written, a paragraph in two docs, a bullet in the manual and one decision |
| Area | Web (startup, pages), docs, tests |
| Spike | None. Every claim below was read off `trunk` on 2026-11-20 |
| Backlog | [`seed-auto-apply-at-startup`](../backlog/seed-auto-apply-at-startup.md) is this step |
| Decisions | New name proposed: **pages-startup-applies-the-release-seed**. Relies on `pages-a-seed-write-records-its-hash`, `pages-a-seed-verdict-is-the-hosts`, `shell-seeds-are-chosen-row-by-row`, `shell-the-seed-apply-is-a-route`, `product-manual-is-a-page` and `product-steps-wait-for-the-owner` |

## The short version

Startup already creates a seed the store is missing. It now also writes a seed whose row says the
release moved it — `SeedComparison.ReleaseNewer`, `CanApply`, **H = R and S ≠ R** — through the same
save the seed page's tick uses.

- **One row kind.** Only `CanApply`. A *new* row is the seeder's. An adopt — *store newer*, or an
  *unknown* page whose content already matches — writes no source and stays the page's, because
  recording where a page came from is a claim the startup step cannot check and the owner can.
  *Deleted*, *edited*, *conflict* and differing *unknown* each replace or resurrect something the
  owner chose, and every one stays a tick.
- **Every start, and nothing stored.** The check is a standing invariant, not a deploy step: it holds
  whenever the process starts, with no marker, no build identity and no remembered runtime.
- **The ordinary write.** Each selected row goes through `PageSeedInstaller.InstallAsync` and
  `PageEditor.SaveSeedAsync`, so it compiles before it stores, records the shipped hash as the
  anchor and joins the same revision history a manual install writes. The host gets no second writer,
  and a moved library invalidates its dependents exactly as it does from the page.
- **Report, never stop.** One line per written row; a row that fails is logged and the rest still
  run; a seed that does not compile is stored and reported the way a manual install reports it.
- **Not for a command.** A command is an operator reading or extracting the store and its stdout is
  its result, so the step runs only when the host is about to serve.
- **The page keeps everything else.** The other six verdicts, the strays and the scrub stay on
  `/admin/seeds`, and the manual says what now happens without a tick.

## The owner's answer

The item's one open question — every start, or only the first start after a deploy — was put to the
owner on 2026-11-20 and answered **every start, with nothing stored**. The reasons are the decision's,
so they belong in the entry:

1. **The rule never mentions a build.** `H = R and S ≠ R` is a property of a row. A one-shot check
   makes it true at one moment and then stops looking, and a store can leave it again with nobody
   watching — a revision restored, an archive imported, a page re-created while the host was down.
2. **A marker is a cache of "we already did it", and that is how a step gets skipped.** It needs a
   store, a build identity (`BuildInfo.Commit` is nullable), an ordering rule and a rule for a crash
   halfway through; each is a new way not to run, bought for work that is already a no-op.
3. **The no-op is real.** After a write `H = S` and `R = S`, so the row is *in sync* and the second
   start writes nothing. Every start and the first start of a deploy are identical in every case but
   one.
4. **That one case is not a hold.** A page restored to an older seed revision has `H = R` again, so
   the record calls it *release newer* and pre-ticks it; writing it again is the record doing what it
   already says is safe. An owner who wants to keep an old source edits the page, and *edited* is
   never automatic.
5. **Rollback needs no case of its own.** An older image over a newer store reads *store newer* and
   writes nothing. A build-keyed marker would have to decide what a rollback means.

The cost is a handful of head reads per start and one compile per row a release actually moved,
before the port opens — the same compile a manual install does, and it fails the deploy loudly rather
than the first visitor quietly.

## What exists today

- **The startup block** ([`Program.Main`](../../src/uk.osric.asp.Web/Program.cs)) opens one
  `AsyncServiceScope`, migrates, closes interrupted assistant runs, repairs content hashes, calls
  `PageAssetSeeder.SeedAsync`, and — only when the process is not a command — scans for stored pages
  at reserved keys. The command dispatch follows the scope.
- **The seeder creates and never updates.** `PageAssetSeeder.SeedAsync` writes only where
  `GetHeadAsync` answers null, and a create records `seed.Hash` as the anchor. A head row of any kind,
  a tombstone included, is left alone.
- **The installer already has both halves.** `PageSeedInstaller.PlanAsync` reads every shipped seed's
  head into a `SeedPlan` with its verdict; `InstallAsync` calls `PageEditor.SaveSeedAsync`, which
  compiles through `PageSaver` and stores the revision with the shipped hash as the anchor.
- **The loop to copy is the seed page's.** `SeedsEndpoint`'s `POST` runs `SeedAction.Write` rows
  through `InstallAsync` in a `try`/`catch (Exception) when (exception is not
  OperationCanceledException)`, logging one line per row, and never stops at the first failure.
- **`CanApply` is one verdict.** [`SeedComparison.Of`](../../src/uk.osric.asp.Core/Pages/SeedComparison.cs)
  sets it only for `ReleaseNewer`; `CanAdopt` is a separate flag that covers `StoreNewer` and any
  *unknown* page whose content already equals the shipped seed.
- **The editor is a scoped service.** `PageEditor` is registered scoped
  ([`Program.cs`](../../src/uk.osric.asp.Web/Program.cs)), so the startup scope can resolve it.
  Resolving it resolves `IPageCompiler`, whose reference set the host already resolves before it
  listens, with a comment saying a runtime that cannot supply one must fail the deploy.
- **A command's stdout is its result.** The reserved-key scan is skipped for a command for exactly
  that reason, and `SeedCommands` routes everything it says to standard error.
- **No schema is involved.** An install writes a revision, a head row and its seed anchor — the
  columns `pages-a-seed-write-records-its-hash` added — and nothing new is needed.

## Design

### 1. Where the step goes

Inside the startup scope, after `PageAssetSeeder.SeedAsync` and before the reserved-key scan, gated
the way the scan is:

```csharp
if (!isCommand) {
    await PageSeedApplier.ApplyAsync(
        scope.ServiceProvider.GetRequiredService<PageEditor>(),
        scope.ServiceProvider.GetRequiredService<PageSeeds>(),
        app.Logger,
        app.Lifetime.ApplicationStopping);
}
```

A command keeps the seeder because a create only adds a key the command would otherwise not find.
This step replaces a page that exists, and an operator with a command is not the host serving:
`seeds pull` exists to take the store's bytes into the working tree, and writing the release's seed
over a *release newer* row first would empty the one row it was most likely asked to pull — and a log
line through the console provider could land inside the tar.

### 2. The type

`PageSeedApplier`, new in `src/uk.osric.asp.Web/Pages/PageSeedApplier.cs`:

```csharp
internal static class PageSeedApplier {
    public static Task<IReadOnlyList<PageSeedInstaller.SeedResult>> ApplyAsync(
        PageEditor pages,
        PageSeeds seeds,
        ILogger logger,
        CancellationToken cancellationToken);
}
```

`PlanAsync`, then `Where(seed => seed.Summary.CanApply)`, then the endpoint's `try`/`catch` around
each `InstallAsync`. It returns what it did, so a test asserts outcomes without capturing logs. It is
a type of its own rather than a second method on `PageAssetSeeder`: that class is the create rule and
this is the update rule; they share a source and nothing else, and one doc comment each says which.
The seeder takes `IPageStore` because a create writes no compile; the applier takes `PageEditor`
because its write is a save.
The item's Location names `PageSeedInstaller.cs`, and this is its second caller, exactly as
`SeedsEndpoint` is its first.

### 3. The report

One line per written row, and nothing at all when nothing was written:

- `Wrote the release's seed of {Path} as revision {Hash}` — information.
- `Wrote the release's seed of {Path} as revision {Hash}, which has compiler errors` — warning, and
  the revision is stored: a broken seed is a deploy defect, and the next start sees the row *in sync*
  and does not rewrite it, exactly as a manual install behaves.
- `Writing the release's seed of {Path} failed` — error with the exception; the loop continues.

No summary line and no new instrument. At most a handful of rows per release, and a counter that is
zero except after a deploy tells a dashboard nothing; if one is wanted later it is
`page.seed.written`, beside `page.asset.requests`.

### 4. What it does not do

- **Adopt.** `CanAdopt` rows are left exactly as they are: the stored content already matches, so a
  write changes nothing and the only act is recording an anchor that claims the page came from this
  seed. A page restored from an archive or written by hand may match by coincidence, and once adopted
  the next release move would overwrite it automatically.
- **Create, resurrect or repair.** A missing key stays the seeder's, a tombstone stays a tombstone,
  and an *edited* or *conflict* page keeps the owner's source.
- **Touch a reserved key.** No seed ships at one, and a stored page at a shell path is the stray scan's
  and `/admin/seeds`' business, not a seed's.
- **Grow the shell.** No endpoint, capability, setting, table or column is added.

## Tests

Written first and failing, one behaviour each, in `test/uk.osric.asp.Web.Tests` beside the installer's.

| Test | Proves |
| --- | --- |
| `AReleaseNewerRowIsWrittenAtStartupAsync` | The page holds the shipped source, its anchor is the shipped hash, and one revision was added |
| `EveryOtherVerdictIsLeftAloneAsync` | A `TestCaseSource` over *in sync*, *edited*, *conflict*, *store newer*, differing *unknown* and *deleted*: no write, no new revision, the anchor unchanged |
| `AnAdoptableRowIsNotRecordedAsInstalledAsync` | Content already equal to the seed with no anchor stays *unknown*: `CanApply` is the only gate |
| `ASecondStartWritesNothingAsync` | A second run returns no results and adds no revision — the no-op that makes a marker unnecessary |
| `APagePutBackOnItsOldSeedIsWrittenOnTheNextStartAsync` | The owner's answered case: a restored old seed revision is written again, and that is the rule |
| `ARowThatFailsDoesNotStopTheNextAsync` | A store that throws for one key still writes the others, and the failing one is answered `Failed` |
| `ASeedThatDoesNotCompileIsWrittenAndReportedAsync` | A pack whose seed is broken is stored as `WrittenWithErrors`; the row does not stay pending |

The harness already gives the tests a real compile and a `FakePageStore` that keeps anchors.
A *release newer* row is a `PageTemplate` holding different source with `SeedHash` set to that
source's own fingerprint.

One integration fixture, `SeedAutoApplyTests` in `test/uk.osric.asp.Web.IntegrationTests`, proves the
step is in the startup path rather than only in a unit test: boot the host on a fresh store, write
`/about` through `IPageStore` with an older copy anchored to itself, then boot a second
`AspApplication` on the same `ConnectionStrings:Storage` and assert that `/about`'s head now carries
the shipped source and hash and that the log names the row. The second start writes nothing.

## Docs and the manual

- [the shell](../shell.md), in "Authentication and the editor": a paragraph saying a start
  applies every *release newer* row before the host listens, that a page the owner edited is never
  touched, and that the page is still the way to install everything else; and the seed page's
  paragraph reworded so *release newer* is a row the page offers when a start could not get to it,
  not the only way it is written.
- [storage](../storage.md#source-storage): "startup seeding checks whether a head exists, so
  a deleted seeded asset stays deleted" gains the second rule — a head that still holds the seed it
  was installed from is moved to the release's seed.
- The seeded `/help`: the **Install from seed** and **Seeds** bullets say that a release's moved seed
  arrives on its own at the next start when the page was never edited, and that the page remains for
  the rows that need a decision.

## Record changes

- [`seed-auto-apply-at-startup`](../backlog/seed-auto-apply-at-startup.md) takes this plan and becomes
  `planned`; the index in [the backlog](../backlog/README.md) links it. The item's paragraph about a
  build marker records the owner's answer and points here.
- At the merge, **pages-startup-applies-the-release-seed** is written into
  [the record](../decisions.md) with its "Recorded in" line, this plan is archived and the item closes
  into [the closed list](../backlog/closed.md) — nothing of it is left over.
- Nothing else in the record moves: no other item is blocked by this one, and no decision is
  superseded.

## Close-out

The name, and the rule it states:

- **pages-startup-applies-the-release-seed** — a host start writes every seed whose row says the
  release moved it and whose page still holds the seed it was installed from, through the ordinary
  seed save, every start, with nothing remembered. The other verdicts, and a page the owner changed,
  are the seed page's alone.

Draft entry:

> A seed update that needs no decision is not left waiting for the owner to open `/admin/seeds`. The
> host plans the pack on every start, after the create-seeder and before it listens, and writes the
> rows whose verdict is *release newer* — **H = R and S ≠ R** — through `PageEditor.SaveSeedAsync`, so
> the write compiles first, records the shipped hash as the anchor and appends a revision like any
> other install. Nothing is remembered between starts: after a write the row is *in sync* and the next
> start does nothing, so a marker would only add a way for the step to be skipped. A page the owner
> edited, a tombstone, a conflicting page, a page whose content already matches but whose record does
> not say so, and a differing *unknown* page all keep their tick on the page, because each replaces
> or resurrects something the owner chose or records a provenance the host cannot check. A command is
> not a start that serves, so a command gets the create-seeder and none of this.
>
> *Recorded in the archived plan, the shell, `PageSeedApplier` and `Program`* — the merge writes the
> links from `docs/decisions.md`'s own base: `plans/archive/seed-auto-apply-at-startup.md`,
> `shell.md`, `../src/uk.osric.asp.Web/Pages/PageSeedApplier.cs` and
> `../src/uk.osric.asp.Web/Program.cs`.

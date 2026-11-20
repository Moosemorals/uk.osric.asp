<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A release applies the seeds it moved at startup

| | |
| --- | --- |
| Difficulty | eager-beaver |
| Utility | high |
| Status | planned |
| Blocked by | — |
| Plan | [seed-auto-apply-at-startup](../plans/seed-auto-apply-at-startup.md) |
| Area | Web (startup, pages), docs, tests |
| Location | `src/uk.osric.asp.Web/Program.cs`, `src/uk.osric.asp.Web/Pages/PageSeedInstaller.cs`, `src/uk.osric.asp.Web/Pages/PageSeedApplier.cs` (new), `src/uk.osric.asp.Web/Pages/Seeds/help`, `docs/shell.md`, `docs/storage.md` |
| Spike | — |

## Idea

Startup seeding creates a page when no head exists and never updates one. A release that moves a seed
therefore leaves the site serving the old source until the owner opens `/admin/seeds` and ticks the
row. The safe update already has a name and a rule: a *release newer* row is the one the automatic gate
permits — `SeedComparison.ReleaseNewer`, `CanApply`, **H = R and S ≠ R** — so the host can finish that
step itself, at startup, with no owner choice.

It must be exactly and only that row. A *new* seed is created by the startup seeder already; a *store
newer* row is an adopt, a different write; *deleted*, *edited*, *conflict* and *unknown* are the seed
page's explicit ticks, because each can replace or resurrect something the owner chose. The automatic
write is the same one the seed page pre-ticks, and it is the only one the store already treats as safe
(`pages-a-seed-write-records-its-hash`, `pages-a-seed-verdict-is-the-hosts`).

The "only the first start after a deploy" part is settled: the check runs on **every start**, with no
marker, and the plan carries the owner's reasons. After a write the row is *in sync*, so an ordinary
restart does nothing even without one. The case that needed thought is a page deliberately restored to
the old seed's source: its content equals its anchor again, the release still ships a different seed,
and the next start writes it a second time — which is the record doing what it already says, because
that row is *release newer* and the seed page pre-ticks it. An owner who wants to keep an old source
edits the page, and *edited* is never automatic.

## Sketch

1. **One startup step.** After `PageAssetSeeder.SeedAsync` and before the host listens, plan the pack
   with `PageSeedInstaller.PlanAsync` and select only rows whose `Summary.CanApply` is true. Nothing
   else is a candidate.

2. **The ordinary write.** Each selected row goes through `PageSeedInstaller.InstallAsync` /
   `PageSaver.SaveSeedAsync`, so it compiles before it stores, records the seed hash as the anchor and
   joins the same revision history a manual apply writes. The host does not get a second writer.

3. **Report, do not stop.** One line per written row names the key and revision; a row that fails is
   logged and the rest still run. A compile error is stored and reported the way a manual install
   reports it.

4. **The first-start question, answered.** No marker and no remembered runtime: the step runs every
   start, because the write is already a no-op once it has landed and a marker is only a new way for
   the step not to run. A page reverted to its old seed is written again at the next start, and that
   is the rule rather than an exception to it.

5. **The page remains for everything else.** The destructive rows, the deleted rows, the adoption and
   the shell-path repair stay on `/admin/seeds`. `/help` and [the shell](../shell.md) say what happens
   automatically so an owner is not surprised by a revision they did not tick.

6. **Tests.** A release-newer row is written at startup and records its anchor; every other verdict is
   left alone; a failed write does not stop the next; a second start is a no-op; a deliberately
   reverted old source is written again, because *release newer* is what the record calls it.

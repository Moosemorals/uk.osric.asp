<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-028 — A broken editor needs one button that restores every seeded page

| | |
| --- | --- |
| Size | M |
| Area | Web, admin, product, operations |
| Location | `src/uk.osric.asp.Web/Pages/PageAssetSeeder.cs`, `src/uk.osric.asp.Web/Admin/`, `src/uk.osric.asp.Web/Program.cs`, `src/uk.osric.asp.Web/Pages/ReservedPaths.cs` |

## Idea

The editor can restore one seed at a time, but only from a page list that the editor itself has to
serve. If the editor page, its stylesheet, its module or `/_layout` is broken — or if a save stored a
template that no longer compiles — the owner can lose the one view that could repair the site. The
command line and the assistant are ways back, but they are not the obvious one.

The emergency restore is a deliberately small shell page: a `GET` that lists the pages the site
ships with and what restoring them would change, then one `POST` button that installs the bundled
source of every seed. It is not a reset, not an archive import and not a backup:

- every key under `Pages/Seeds` is restored, including the stylesheets, scripts and `/_layout`;
- a seed that is missing or tombstoned comes back, and its history is retained;
- a seed that already matches is not written, so a run does not fill the history with no-ops;
- a page the owner created is never touched, and nothing is deleted or scrubbed;
- the operation is independent of the page pipeline, because it is shell code that reads files and
  calls the ordinary saver.

This is the recovery path the [page-editor plan](../plans/page-editor.md) assumes, which is why that
plan can remove the editor from the shell without keeping a permanent fallback. It does not answer
"put the whole site back to an archive" — that is [IDEA-020](IDEA-020.md), and it remains a separate,
larger decision.

## Sketch

1. **The route is shell and reserved.** `GET` and `POST /admin/restore`, behind the `Editor` policy,
   with antiforgery on the `POST`; add it to `ReservedPaths.Default` and `ShellRoutes.All` in the
   same change, because the assistant's shell-file table is pinned to the reserved list. Use
   `ShellDocument` and `AdminNavigation`, so the page still renders when stored pages do not.
2. **The `GET` is the preview.** List every seed with its key and one of four states: current,
   changed, missing or deleted. One button posts. No JavaScript, no filters, no archive upload.
3. **The `POST` walks the seed list.** Read each bundled source with
   `PageAssetSeeder.ReadSeedTemplateAsync`, compare it with the live head, and call the ordinary
   `PageEditor.SaveAsync` only when the source differs or the key is missing/tombstoned. Each save
   is a new revision through `PageSaver`, so the restore has the same compile-before-store rule as
   every other save.
4. **Report rather than abort.** A seed that fails to compile is still stored and reported as
   "saved with errors"; a failure on one seed does not stop the rest. The response is the summary
   table, with links to the editor for the rows that need attention. The `POST` renders its result
   rather than redirecting, because there is nowhere better to send the owner and the response is
   the answer.
5. **Make it findable when the editor is not.** Link it from `/status` as well as `/admin`, because
   `/status` is the bootstrap route the owner can still reach when `/pages` will not run.
6. **Do not grow a batch write.** This is a recovery button, not an import: sequential saves through
   the one saver are enough at the number of seeds the site ships, and a partial run is easier to
   explain than a transaction that rolls the whole recovery back. If the seed count ever makes the
   run slow, the batch store is the place to look, not a second insert path.
7. **Tests.** An anonymous request is refused; a `POST` without antiforgery writes nothing; a
   changed seed, a missing seed and a tombstoned seed are restored; a matching seed and a non-seed
   page are untouched; one failing seed does not prevent the others; the summary counts what was
   written; `/admin/restore` is reserved and has its shell-route record. The integration suite puts
   `/pages` deliberately into a compile error and proves one `POST` brings it back.

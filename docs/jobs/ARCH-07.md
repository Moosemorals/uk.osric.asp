<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-07 — Page content has no history, so an edit cannot be undone

| | |
| --- | --- |
| Severity | Medium |
| Area | Storage, product |
| Location | `src/uk.osric.asp.Web/Storage/StoredFile.cs`, `src/uk.osric.asp.Web/Storage/SqlitePageStore.cs` |

## Problem

`Files` holds one row per path and `SavePageAsync` overwrites `Content` in place. A mistaken
save destroys the previous page with no undo. The site's content is
code, which is exactly the content most likely to need a diff or rollback.

The deployment notes already warn that the database is the only copy of the content and that backups
are manual, so there is no safety net inside the app either.

## Fix outline

The [editor tools and retained revisions plan](../plans/editor-tools-and-immutable-assets.md)
delivers the storage foundation alongside ARCH-06, ARCH-10 and ARCH-11:

1. Every save appends a row with source, optional compilation/debug data and a unique revision hash
   derived from source, creation runtime id, timestamp and a unique save identity. A transactional
   head pointer selects the current revision; compilation metadata is not the revision identity.
2. Keep all revisions indefinitely. Measure table/index size and database-volume capacity; review
   policy only when page storage exceeds half the disk, not through automatic pruning now.
3. Delete tags the head as a tombstone. Recreating the canonical path shares its old history.
   Only explicit scrub wipes that path and every retained revision, with the secret-cleanup
   limitations and SQLite cleanup requirements documented in the combined plan.
4. Migrate existing source, images, debug symbols and diagnostics without loss.

This job stays open for the user-facing history workflow, to plan after the storage exists:

1. Add authenticated history browsing with `cmd=history`, including deleted paths. List revision
   hashes, save times and compilation status without loading all images or exposing source publicly.
2. Add a confirmed, antiforgery-protected `cmd=restore` POST selecting a revision belonging to that
   path. Restore saves its source as a new revision under the current runtime, preserving history;
   it does not rewind or mutate an old row. Restoring a deleted path clears its tombstone.
3. Test that browsing does not execute code, a revision from another path cannot be restored, and
   restore appends a distinct revision even when the restored source is identical.

Historical asset execution and PageLayout links are [IDEA-010](IDEA-010.md), not part of this
history UI. A revision address identifies saved code, not necessarily deterministic output.

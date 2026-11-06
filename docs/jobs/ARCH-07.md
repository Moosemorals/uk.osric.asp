<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-07 — Retained page history has no restore UI

| | |
| --- | --- |
| Severity | Medium |
| Area | Storage, product |
| Location | `src/uk.osric.asp.Web/Storage/StoredRevision.cs`, `src/uk.osric.asp.Web/Editor/EditorEndpoint.cs` |

## Problem

Every save is retained as a separate revision. The editor can browse a page's revision hashes,
timestamps and compilation state, and inspect any revision's saved source. It cannot yet restore a
revision, so recovering from a mistaken save still requires copying its source into a new edit.
The site's content is code, which is exactly the content most likely to need a reviewable rollback.

## Fix outline

The [editor tools and retained revisions plan](../plans/archive/editor-tools-and-immutable-assets.md)
delivered the storage foundation together with ARCH-06, ARCH-10 and ARCH-11:

1. Every save appends a row with source, optional compilation/debug data and a unique revision hash
   derived from source, creation runtime id, timestamp and a unique save identity. A transactional
   head pointer selects the current revision; compilation metadata is not the revision identity.
2. Keep source and revision identity indefinitely unless explicitly scrubbed. The supported metrics
   report logical revision payload, whole-database allocated pages and containing-volume capacity;
   they do not claim exact per-table allocation. Review policy only when payload exceeds half the
   volume, with no automatic pruning.
3. Delete tags the head as a tombstone. Recreating the canonical path shares its old history.
   Only explicit scrub wipes that path and every retained revision, with the secret-cleanup
   limitations and SQLite cleanup requirements documented in the combined plan.
4. Migrate existing source, images, debug symbols and diagnostics without loss.

The first history-browsing slice is implemented:

1. Authenticated `GET /edit/<path>?cmd=history` lists retained hashes, save times, compilation
   status and creation runtime, including for tombstoned paths. The listing query does not load
   source or binary BLOBs.
2. Selecting a hash opens `cmd=revision` and displays only that revision's HTML-encoded saved
   source and metadata. It neither compiles nor executes the page; hashes from other paths are
   rejected. Both views send `Cache-Control: no-store`.

The remaining work is restore:

1. Add a confirmed, antiforgery-protected `cmd=restore` POST selecting a revision belonging to that
   path. Restore saves its source as a new revision under the current runtime, preserving history;
   it does not rewind or mutate an old row. Restoring a deleted path clears its tombstone.
2. Test that a revision from another path cannot be restored and restore appends a distinct
   revision even when the restored source is identical.

Historical asset execution and PageLayout links are [IDEA-010](IDEA-010.md), not part of this
history UI. A revision address identifies saved code, not necessarily deterministic output.

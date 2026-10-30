<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-07 — Page content has no history, so an edit cannot be undone

| | |
| --- | --- |
| Severity | Medium |
| Area | Storage, product |
| Location | `src/uk.osric.asp.Web/Storage/StoredFile.cs`, `src/uk.osric.asp.Web/Storage/SqliteStorage.cs:23-32` |

## Problem

`Files` holds one row per path and `SetFileContentsAsync` overwrites `Content` in place. A mistaken
save — or a forged one (SEC-02) — destroys the previous page with no undo. The site's content is
code, which is exactly the content most likely to need a diff or rollback.

The deployment notes already warn that the database is the only copy of the content and that backups
are manual, so there is no safety net inside the app either.

## Fix outline

1. Add a `Revisions` table (`Path`, `RevisionId`, `Content`, `SavedAt`) and append on every save;
   keep `Files` as the current view, or drop it and select the latest revision.
2. Write the revision and the current row in one transaction.
3. Add `GET /edit/<path>/history` and `POST /edit/<path>/revert/<revision>` to the editor (behind the
   SEC-01 boundary).
4. Bound retention: keep the last *n* revisions per path, or add a purge job; do not grow without
   limit.
5. Migrate the existing `Files` rows into revision 1.
6. Test: save twice, history has two revisions, revert restores the first.

If revisions are out of scope, at minimum copy the previous content to a `.bak` row on overwrite so
recovery is possible without a database backup.

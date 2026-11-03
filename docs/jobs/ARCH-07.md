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

1. Add a `Revisions` table (`Path`, `RevisionId`, `Content`, `ContentHash`, `SavedAt`) and append
   on every save; compute `ContentHash` from the source at save time, independently of compilation.
   Keep `Files` as the current view, or drop it and select the latest revision.
2. Write the revision and the current row in one transaction.
3. Add `GET /edit/<path>/history` and `POST /edit/<path>/revert/<revision>` to the editor (behind the
   authentication boundary).
4. Use the saved source fingerprint as the version of mutable built-in assets when generating their
   links in shared page headers. A versioned asset request must resolve to that exact revision (or
   fail); it must never serve newer content under an old URL before sending `immutable` cache
   headers. Keep the revision identifier distinct from the current compile-cache metadata: that
   `ContentHash` is nullable until compilation and only describes the current `Files` row.
5. Bound retention: keep the last *n* revisions per path, or add a purge job; do not grow without
   limit. Retention for asset revisions must also cover the lifetime promised by immutable caching,
   or the cache lifetime must be no longer than the retained revision lifetime.
6. Migrate the existing `Files` rows into revision 1 and compute their source fingerprints.
7. Test: save twice, history has two revisions, revert restores the first; changing an asset changes
   its versioned URL, while requesting the old URL still returns the old bytes.

If revisions are out of scope, at minimum copy the previous content to a `.bak` row on overwrite so
recovery is possible without a database backup.

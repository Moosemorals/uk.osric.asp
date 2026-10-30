<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-07 — Concurrent saves race and can fail with a primary-key violation

| | |
| --- | --- |
| Severity | Low |
| Area | Storage |
| Location | `src/uk.osric.asp.Web/Storage/SqliteStorage.cs:23-32` |

## Problem

`SetFileContentsAsync` reads the row, then inserts or updates it:

```csharp
StoredFile? file = await database.Set<StoredFile>().SingleOrDefaultAsync(...);
if (file is null) {
    database.Add(new StoredFile { Path = path, Content = content });
} else {
    file.Content = content;
}
await database.SaveChangesAsync(cancellationToken);
```

Two requests for a new path can both see `null` and both insert; the second `SaveChangesAsync` fails
with a unique-constraint error surfaced as a 500. The site is single-user today so the window is
small, but a double-submitted form is enough, and the editor's redirect makes retries likely.

## Fix outline

1. Use a single upsert. EF Core 10 supports `ExecuteUpdateAsync` for existing rows; for the insert
   case, catch `DbUpdateException` with `SqliteErrorCode.Constraint_PrimaryKey` and retry as an
   update.
2. Or use `INSERT ... ON CONFLICT(Path) DO UPDATE` via `database.Database.ExecuteSqlAsync`.
3. Wrap the operation in a transaction so the read and write see a consistent snapshot.
4. Test: run two saves for the same new path concurrently and assert one row with the last value.

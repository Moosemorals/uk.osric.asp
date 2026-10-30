<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# STYLE-02 — `IStorage` exposes `IEnumerable<string>` instead of `IReadOnlyList<string>`

| | |
| --- | --- |
| Severity | Low |
| Area | Web, API shape |
| Location | `src/uk.osric.asp.Web/IStorage.cs:7`, `src/uk.osric.asp.Web/Storage/SqliteStorage.cs:9-12` |

## Problem

The project conventions say public members expose `IEnumerable<T>`, `IReadOnlyList<T>` or
`IReadOnlyDictionary<TKey, TValue>`. `ListFilesAsync` returns `IEnumerable<string>` while the
implementation materialises a `List<string>` anyway. A lazy `IEnumerable` from a method that has
finished its database work is misleading: the caller cannot know whether enumerating it is cheap,
and a future implementation could return a deferred query tied to a disposed `DbContext`.

## Fix outline

1. Change the signature to `Task<IReadOnlyList<string>> ListFilesAsync(...)`.
2. Return the `List<string>` directly from `SqliteStorage` (it already calls `ToListAsync`).
3. Update the two fake implementations in `MainEndpointTests`/`EditorEndpointTests` and the storage
   tests.
4. While touching it, decide whether the method is still needed at all (ARCH-06); if not, delete it
   instead.

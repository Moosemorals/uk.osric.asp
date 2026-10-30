<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-05 — Storage keys are raw route values with no canonical path rule

| | |
| --- | --- |
| Severity | Medium |
| Area | Web, storage, routing |
| Location | `src/uk.osric.asp.Web/MainEndpoint.cs:13`, `src/uk.osric.asp.Web/EditorEndpoint.cs:13`, `src/uk.osric.asp.Web/Storage/SqliteStorage.cs` |

## Problem

A page is stored under exactly `RouteValues["path"]`. Nothing defines or tests what a canonical key
is:

- The app writes `hello` for `/edit/hello`, while `SqliteStorageTests` uses `/example.cs` — two
  conventions, neither enforced.
- `/dir/../example.cs`, `/example.cs` and `//example.cs` are different keys (the test
  `PathsAreStoredExactlyAndCaseSensitivelyAsync` even asserts dot segments are literal).
- Case is significant, so `/Hello` and `/hello` are different pages that look identical in a browser.
- Trailing slashes, percent-encoded segments and URL casing all create aliases.
- The editor can create keys that no route can reach, or that shadow `/error` and `/edit`.

Storage identity and routing identity are conflated, so "which page is this?" has no single answer.

## Fix outline

1. Write down the rule: for example, "keys are lowercase, start with `/`, contain no `.` or `..`
   segments, no trailing slash, and are compared ordinal-case-insensitively".
2. Implement it as a pure function in `Core`, e.g. `PagePath.TryNormalize(string raw, out string key)`,
   with unit tests for every case listed above.
3. Normalise once, in the shell, before calling `IStorage`; store only canonical keys. Reject (400)
   rather than silently accepting non-canonical input.
4. Migrate existing rows if the rule changes representation; an EF migration can rewrite `Path`.
5. Reserve `/edit`, `/error`, `/site.css` and similar in the rule and return 404 if a stored page
   would shadow them.
6. Update `SqliteStorageTests` to use canonical keys, and add an integration test proving
   `/a/../b`, `/b` and `/B` resolve to the same page.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# STYLE-04 — Several tests use `void` methods around `Assert.That(async ...)`

| | |
| --- | --- |
| Severity | Low |
| Area | Tests |
| Location | `test/uk.osric.asp.Web.Tests/MainEndpointTests.cs:54-70`, `test/uk.osric.asp.Web.Tests/SqliteStorageTests.cs:113-121` |

## Problem

Two conventions are mixed in the web tests:

- `StorageFailurePropagatesAsync` and `RequestCancellationIsForwardedAsync` are declared `void` and
  use `Assert.That(async () => await ..., Throws...)`; other tests are `async Task` with
  `Assert.ThrowsAsync`.
- `CancelledOperationsThrowAsync` is named `...Async` but is synchronous and uses three
  `Assert.That(async ...)` blocks.

The `void` + `Assert.That(async ...)` form relies on NUnit's delegate handling and is easy to read
wrongly; `async Task` + `Assert.ThrowsAsync` is the clearer, conventional shape and matches the
project's "`Async` suffix on asynchronous methods".

## Fix outline

1. Convert the `void` tests to `public async Task ...Async()` and use
   `Assert.ThrowsAsync<T>(async () => await ...)`.
2. Rename or convert `CancelledOperationsThrowAsync` so the name and signature agree.
3. Apply the same shape to any new tests, including the integration suite in
   `test/uk.osric.asp.Web.IntegrationTests`.
4. Add an NUnit analyzer rule or a review checklist item if the pattern recurs.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-08 — The shell seam (routing, middleware, error handling) has no integration tests

| | |
| --- | --- |
| Severity | High |
| Area | Testing, shell |
| Location | `test/uk.osric.asp.Web.Tests/*.cs`, `src/uk.osric.asp.Web/Program.cs` |

## Problem

Every web test constructs a `DefaultHttpContext` and calls an endpoint method directly. Nothing
exercises the parts that only exist when the host runs:

- route matching and ordering (`/edit/{**path}`, `MapGet("/error")`, the catch-all);
- exception-handler re-execution (which is how BUG-01 was found);
- status codes and content types set by middleware rather than handlers;
- static file handling and the `{**path}` catch-all falling through;
- forwarded headers (OPS-01);
- database migration on startup and DI wiring.

The three bugs found by running the app by hand (BUG-01, BUG-02, OPS-01) all live in this gap. The
unit-level endpoint tests are good and should stay; they are not a substitute for a test of the
composed pipeline.

## Fix outline

1. Add a minimal integration test project, or extend `uk.osric.asp.Web.Tests`, using
   `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`). `Program` is `static` with a
   `Main`, so expose `public partial class Program` or refactor to a `CreateApp(string[] args)` factory.
2. Point the app at a temporary SQLite file (or `:memory:`) per fixture, so migrations run against a
   throwaway database.
3. Cover at least:
   - `GET /` and a stored page → 200 with `text/html`;
   - unknown path → 404;
   - throwing `GET` → 500 with the error page;
   - throwing `POST` → 500 with the error page (currently 404 — BUG-01);
   - `GET /error` → 500 (currently 200 — BUG-02);
   - editor round-trip when authorised, 401/403 when not (SEC-01);
   - a page with compile errors → 500 and diagnostics.
4. Run them in `make test` as a fourth target (`test-integration`) so `make test` still means
   "everything".
5. Keep them out of the default fast loop if startup cost grows; `make test-integration` can be the
   explicit entry point and CI can run both.

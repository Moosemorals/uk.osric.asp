<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-01 — An exception during a POST returns 404 "File not found"

| | |
| --- | --- |
| Severity | High |
| Area | Web, error handling, routing |
| Location | `src/uk.osric.asp.Web/Program.cs:44` (`MapGet("/error", ...)`) |

## Problem

The production exception handler is registered with `UseExceptionHandler("/error")`, which re-executes
the pipeline with the *original method*. `/error` is mapped with `MapGet`, so a `POST` that throws
does not reach `ErrorEndpoint`; it falls through to the `Map("{**path}", MainEndpoint.Handle)`
catch-all, which looks for a page named `error`, finds none, and returns 404.

Verified:

```
GET  /boom -> 500 (error page)
POST /boom -> 404 "File not found."
```

So a failing POST (an editor save with a storage failure, a page that throws on POST) reports the
wrong status, shows the wrong page, and hides the failure from the user. The exception is still
logged, but the response lies.

## Fix outline

1. Map the error endpoint for every method: `app.Map("/error", ErrorEndpoint.Handle);`.
2. Make `ErrorEndpoint` explicit about status (set 500 if the response has not started) and keep it
   out of the catch-all's reach.
3. Alternatively, use `UseExceptionHandler` with an inline handler instead of a re-executed path, so
   routing cannot intercept the error response at all.
4. Add an integration test for a throwing `POST` (ARCH-08) — this is the test that would have caught
   it.

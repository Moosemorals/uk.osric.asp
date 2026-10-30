<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-02 — `GET /error` returns HTTP 200

| | |
| --- | --- |
| Severity | Medium |
| Area | Web, error handling |
| Location | `src/uk.osric.asp.Web/ErrorEndpoint.cs:29`, `src/uk.osric.asp.Web/Program.cs:44` |

## Problem

`ErrorEndpoint.Handle` returns `Results.Content(page, "text/html")`, which sets status 200. When the
exception handler re-executes `/error`, middleware has already set 500 and the result keeps it, so
the error page looks correct. But a direct `GET /error` returns the "Something went wrong" page with
**200 OK**, which is wrong for health checks, proxies, crawlers and monitoring, and it makes `/error`
a public page that claims success.

Verified: `curl -o /dev/null -w '%{http_code}' /error` → `200`.

## Fix outline

1. Set the status explicitly in the error endpoint (e.g. `Results.Content(page, "text/html",
   statusCode: 500)`), or set `context.Response.StatusCode = 500` before writing.
2. Prefer an inline `UseExceptionHandler` branch or `IExceptionHandler` implementation so the route
   exists only as an error target; a direct request then returns 404.
3. Add an assertion to the integration suite (ARCH-08).

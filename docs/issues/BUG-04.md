<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-04 — Compilation diagnostics are written raw and unencoded

| | |
| --- | --- |
| Severity | Low |
| Area | Web |
| Location | `src/uk.osric.asp.Web/MainEndpoint.cs:42-46` |

## Problem

When compilation fails, the endpoint writes each diagnostic with `diagnostic.ToString() + "\n"`
straight into the HTML document. Problems:

- The diagnostics are not HTML-encoded; a diagnostic that echoes source text containing `<` or `>`
  produces broken markup (and, if it ever includes attacker-controlled text, a script-injection
  vector).
- `\n` is whitespace in HTML, so multiple diagnostics run together on one line.
- The output is not wrapped in `<pre>`, so indentation is lost.
- Diagnostics are shown in production to any visitor, not just to the editor.

## Fix outline

1. Build the list first, `WebUtility.HtmlEncode` each entry, and join with `<br>` (or use `<pre>` and
   HTML-encode the whole block).
2. Show the error page in the framework layout, with a link to `/edit/<path>` (after SEC-01, that
   link is only useful to the owner).
3. Reuse the same rendering from the editor so a failed save can show diagnostics too (PERF-01).
4. Add a test using a template whose diagnostic includes `<` (e.g. an unterminated string) and assert
   the output is encoded.

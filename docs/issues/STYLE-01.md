<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# STYLE-01 — `EditorEndpoint` uses `goto` and repeats validation

| | |
| --- | --- |
| Severity | Low |
| Area | Web, code style |
| Location | `src/uk.osric.asp.Web/EditorEndpoint.cs:14-37` |

## Problem

The POST branch uses `goto done` three times to jump to the shared redirect:

```csharp
if (string.IsNullOrWhiteSpace(path)) {
    context.Response.Cookies.Append("message", "Path is required.");
    goto done;
}
```

`goto` is not used anywhere else in the repository and the project conventions prefer guard clauses
and early returns. The three branches also repeat the same cookie-append shape, and the whole method
mixes request parsing, validation, persistence and rendering in one `if`/`else` block with `else`
after a `return`.

## Fix outline

1. Extract validation into a small pure helper, e.g.
   `EditorRequest.TryParse(path, form, out string content, out string? message)`, returning a record
   — no `goto`, no repeated cookie code.
2. Use early returns: if the request is a POST, validate, save, set the redirect, and `return`; the
   GET path follows at method level.
3. Extract `Redirect(context, message)` and `RenderEditor(context, path, content, message)` private
   static methods so each does one thing.
4. Move the rendering into a small view helper (or a Razor-free template string) rather than inline
   interpolated HTML.
5. Add tests for each validation failure (most already exist) — the refactor should not change
   behaviour.

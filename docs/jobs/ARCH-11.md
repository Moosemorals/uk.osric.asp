<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-11 — The editor cannot show the generated C# for a page

| | |
| --- | --- |
| Severity | Low |
| Area | Web, developer experience |
| Location | `src/uk.osric.asp.Web/Editor/EditorEndpoint.cs`, `src/uk.osric.asp.Core/Compilation/TemplateSourceGenerator.cs` |

## Problem

The editor shows the template and, after a failed compile, the diagnostics. It does not show the
generated C# that Roslyn actually compiled: the three-line prelude, the `ctx.WriteRaw(...)` calls
for text, the `#line` directives, the page's code blocks and the closing braces.

That view is useful when a diagnostic is surprising, when a `#line` mapping looks wrong, or when a
compiler fault has to be reported. Today the source is only in the `Debug` log (see the
[compiler-diagnostics plan](../plans/compiler-diagnostics.md)) or one
`TemplateSourceGenerator.Generate` call away in a debugger. Showing it also makes the
template language legible: it answers "what does my text become?" without requiring the author to
read the generator.

## Fix outline

The [combined implementation plan](../plans/editor-tools-and-immutable-assets.md) specifies a
read-only `?cmd=generated` view, avoiding collisions with page keys ending in `/generated`, and
uses the current `TemplateSourceGenerator.Generate` API.

1. Link to `GET /edit/<path>?cmd=generated` from the saved page's editor. Require the Editor policy
   and `Cache-Control: no-store`; missing or tombstoned pages return 404.
2. Read current saved source and call `TemplateSourceGenerator.Generate`. On a parse failure show
   `TEMPLATE001`, never partial source. Unsaved textarea changes are not included.
3. Render HTML-encoded source in a read-only `<pre>` with horizontal scrolling. Preserve `#line`
   directives. Do not execute, compile, save or refresh the optional compiled image.
4. Label this as generation by the current runtime, not the exact historical source compiled by
   an older runtime. Highlighting template ranges is a separate optional enhancement.
5. Test generated class, raw writes, line directives, encoded markup, parse failures, missing/deleted
   pages and authentication; prove inspection calls neither the compiler nor renderer.

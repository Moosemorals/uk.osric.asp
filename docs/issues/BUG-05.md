<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-05 — Compile errors point at generated source line numbers, not the template

| | |
| --- | --- |
| Severity | Medium |
| Area | Runtime, developer experience |
| Location | `src/uk.osric.asp.Core/TemplateSourceGenerator.cs:13-37`, `src/uk.osric.asp.Web/MainEndpoint.cs:45` |

## Problem

`TemplateSourceGenerator` concatenates a fixed prelude, the generated `ctx.WriteRaw(...)` calls and
the user's code blocks into one string. Roslyn reports positions in *that* string. For a template

```
line one
<% int value = ; %>
```

the page shows `(3,48): error CS1525`, which corresponds to neither the template's line 2 nor
column 17. The editor also has no way to underline the mistake. With the prelude and generated calls
shifting offsets, the numbers get worse the larger the template is.

## Fix outline

1. Emit `#line` directives around each code block (and reset with `#line default` after it), mapping
   back to the template's line and column. `#line <n> "<path>"` makes Roslyn report the template
   position directly.
2. Better: build the syntax tree with `CSharpSyntaxTree.ParseText(SourceText.From(...))` and use a
   `SourceText` + `#line` span mapping, or `SyntaxFactory.ParseStatement` with explicit trivia, so
   text literals keep their original spans.
3. Surface mapped diagnostics in the editor as a list of `(line, column, message)` values — a pure
   transformation in `Core`, tested with unit tests.
4. Keep the unmapped generated source available behind a debug flag for diagnosing generator bugs.
5. Test: a syntax error on template line *n* reports line *n*, including when it follows several
   text blocks.

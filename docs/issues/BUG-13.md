<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-13 — A warning-only page is reported as "Saved with errors."

| | |
| --- | --- |
| Severity | Low |
| Area | Runtime, Web, diagnostics |
| Location | `src/uk.osric.asp.Runtime/CompileResult.cs:13`, `src/uk.osric.asp.Web/Pages/PageService.cs:48`, `src/uk.osric.asp.Web/Endpoints/EditorEndpoint.cs:98` |

## Problem

`CompileResult` keeps every diagnostic Roslyn produced, warnings included, and carries Roslyn's own
`Success` flag rather than deriving it. That is correct — `EmitResult.Success` is false only for
errors — but the two facts are never separated downstream, so a page that compiles with a warning is
treated as a broken page by everything that looks at the diagnostic list:

```
<% int unused = 1; %>
```

compiles to `Success=true` with an image and one diagnostic,
`warning CS0219: The variable 'unused' is assigned but its value is never used`. `PageService.SaveAsync`
returns that list, and `EditorEndpoint` says **"Saved with errors."** whenever it is non-empty, so the
editor contradicts itself: the page saved, compiled, stored and will be served, yet the message says it
failed.

Two smaller consequences of the same conflation:

- `CompileResult.Diagnostics` is documented as "Empty when `Success` is `true`", which is false for any
  page that produces a warning.
- `EditorEndpoint` shows the diagnostic block for `Diagnostics.Count > 0`, so warnings look identical
  to errors in the editor, and `PageCompilation` cannot tell them apart once stored.

Rendering is unaffected: `MainEndpoint` and `PageService` key off `Image is null`, so a warning-only
page is served normally — only the message is wrong.

Raised while fixing [BUG-06](README.md): the new `TokenizeResult`/`TemplateParseResult` records in
`Core` derive `Success => Errors.Count == 0` because their list holds only fatal problems. `CompileResult`
cannot do the same while warnings share one list, which is why the two families disagree.

## Fix outline

1. Separate severity at the compiler seam: either
   `CompileResult(bool Success, byte[]? Image, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)`
   or a `CompileDiagnostic(Severity, Message)` record. `TemplateCompiler.Compile` filters on
   `diagnostic.Severity == DiagnosticSeverity.Error`.
2. With errors alone in the list, `Success` can be derived (`Success => Errors.Count == 0`) and the
   three result types then agree. Keep `Image` as the thing the shell checks; do not make the shell
   parse message strings.
3. Carry warnings through `PageCompilation` so the editor can render them as warnings, and let
   `SaveAsync` distinguish clean / warnings / errors, so "Saved with warnings." is possible.
4. Fix the `Diagnostics` doc comment even if nothing else changes: the list holds warnings too.
5. Tests: a warning-only page compiles, stores an image, renders, and the editor says "Saved with
   warnings." rather than "Saved with errors."; an error page still says "Saved with errors." and
   still answers "Handler failed to compile.".

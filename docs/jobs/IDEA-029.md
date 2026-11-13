<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-029 — VS Code cannot check a page as it is typed

| | |
| --- | --- |
| Size | L |
| Area | Developer experience, Core, Runtime, tools |
| Location | `tools/vscode-asp`, a new server project under `tools/`, `src/uk.osric.asp.Core/Compilation/GeneratedSource.cs` |
| Spike | [Teaching VS Code the template language](../spikes/vscode-template-language.md) |

## Idea

Reviewing a seeded page in VS Code has highlighting and clickable diagnostics, and neither is live.
[`tools/vscode-asp`](../../tools/vscode-asp) colours the HTML and the C#; `make check-pages` compiles
the seeds and prints the compiler's diagnostics, which the `check pages` task turns into Problems
entries. A save-and-run is still the loop, and the diagnostics arrive after the file is written
rather than while it is being typed.

The compiler is fast enough to close that loop. Generation is hundredths of a millisecond and a warm
compile is 30–70 ms, so a debounced `didChange` can answer as the author pauses. What is missing is
the process that speaks Language Server Protocol, and the client that starts it.

**This is not [WHIM-01](WHIMS.md).** That whim is an LSP *inside the site's own editor* — the
textarea IDEA-001 is replacing — publishing the C# compile diagnostics and the script checker's
JavaScript diagnostics to a browser page. This idea is an editor for the repository: VS Code's side
of the same compiler, reusing the template-space `PageDiagnostic` values that already exist. The two
share the compiler and nothing else.

## Sketch

- **Server: a .NET console app**, no npm. JSON-RPC over stdio with `Content-Length` framing,
  `initialize`/`initialized`/`shutdown`, `textDocument/didOpen|didChange|didClose`, and
  `textDocument/publishDiagnostics`. Around 250–350 lines plus tests, because the framing and the
  document versions are the whole protocol surface a diagnostics-only server needs.
- **Diagnostics are already the right shape.** `TemplateSourceGenerator.Generate` then
  `PageCompiler.Compile` answer with `PageDiagnostic` values positioned in template space, including
  the tokenizer's `TEMPLATE001`/`TEMPLATE002` and the policy's `PAGE002`. The server maps
  `TemplatePosition` to an LSP range and publishes. One `PageCompiler` per process: the 1.5 s cold
  start is the reference set, and it is paid once.
- **A range needs an extent.** `PageDiagnostic` carries a start position only, so a squiggle would be
  one character. `MapDiagnostics` already has `FileLinePositionSpan.EndLinePosition` in hand and
  throws it away; keeping it is a small `Core` change and makes the underline match the token.
- **Completion is the expensive half.** `#line` maps generated source *to* template, and a request
  from the editor needs the other direction. Two options: give `GeneratedSource` a segment map
  (generated offsets beside template positions, ~40 lines and tests) or insert a sentinel at the
  caret, regenerate and find it (~30 lines, and a hack). Roslyn's completion service then needs
  `Microsoft.CodeAnalysis.CSharp.Workspaces`, which [`Directory.Packages.props`](../../Directory.Packages.props)
  already pins for EF Design but no product assembly references.
- **Semantic tokens are optional.** If the grammar ever falls behind C#, classified spans map back
  with `GetMappedLineSpan` and need no reverse lookup, so a server could colour as well as diagnose.
  The fragment grammar is cheaper and already works; this is a fallback, not a first step.
- **The client is the one piece of JavaScript**, and it can be plain: `child_process` spawn, the same
  `Content-Length` framing, `vscode.languages.createDiagnosticCollection`. 150–250 lines, no
  bundler and no npm; `vscode-languageclient` would save the last hundred and add npm, which loses.
  Adding a `main` to `tools/vscode-asp/package.json` turns the declarative extension into an
  ordinary one; the grammars and the language configuration do not change.
- **Tests.** The diagnostic mapping and the document/version bookkeeping are unit-testable if the
  transport is a thin layer; an end-to-end test can drive the server over pipes rather than through
  VS Code. `make check-pages` already covers the compiler path, so the server suite is about the
  protocol and the positions, not about Roslyn.

Two things to decide before it starts: whether completion — and so the Workspaces reference and the
reverse map — is in scope, and whether the server is a `tools/` project or something the Web app can
host. The cheap half (highlighting and `make check-pages`) has already landed, so nothing here is
blocking the next review of a seed.

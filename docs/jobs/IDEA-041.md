<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-041 — A diagnostic is a point in an unnamed source

| | |
| --- | --- |
| Size | S — one value widened in `Core` and again in `Sdk`, one mapping completed in `Runtime`, one mapping in `Web`, and the tests and manual entry for each |
| Area | Core, Runtime, Sdk, Web, docs |
| Location | `src/uk.osric.asp.Core/Compilation/PageDiagnostic.cs`, `src/uk.osric.asp.Core/Compilation/TemplatePosition.cs`, `src/uk.osric.asp.Runtime/PageCompiler.cs`, `src/uk.osric.asp.Sdk/PageDiagnostic.cs`, `src/uk.osric.asp.Web/Pages/PageDiagnostics.cs`, `src/uk.osric.asp.Web/Pages/Seeds/pages`, `tools/vscode-asp`, `tools/check-pages` |

## Idea

A compile diagnostic says where it is, and the where is only half true. Two facts the compiler has are
thrown away before any consumer sees them:

- **No extent.** `MapDiagnostics` reads `span.StartLinePosition` and ignores `span.EndLinePosition`, so
  a diagnostic is one character wide. An editor can put a mark in the gutter and print the sentence; it
  cannot underline the token, and a squiggle in VS Code is a dot.
- **No source.** A page that names a library compiles its diagnostics in the library's template space,
  and `Core`'s diagnostic carries `Source` — the library's key — for exactly that reason. The `Sdk`
  value has no `Source` field, so `PageDiagnostics.ToSdk` drops it. An author is told "line 12, column 3"
  and not told which of five files line 12 is in.

Both are one sentence of the same complaint: *point at the thing, in the file it is in*. Every consumer
wants it and each works around it differently — the editor anchors a mark to a line, VS Code publishes a
one-character range, `make check-pages` prints a position with no span, and the assistant reads a failed
save as prose.

## Sketch

1. **The extent.** `Core.Compilation.PageDiagnostic` gains an end position beside its start, or its
   `TemplatePosition` becomes a `TemplateSpan` with one. The convention stays the one the type already
   keeps: an unknown position is `null` rather than `0`, and an unknown end is the start, so a consumer
   that only wants a line never meets a second kind of unknown.

2. **The source.** `Sdk.PageDiagnostic` gains `Source` — the key of the page or library the position
   refers to, or `null` for the page being saved. `PageDiagnostics.ToSdk` copies it, and the value's
   documentation says what `null` means, because the difference between "this page" and "the library it
   names" is the whole point.

3. **The mapping.** `MapDiagnostics` finishes the span it already has in hand. Roslyn's unmapped
   diagnostics are still thrown away as they are today; the change is one line and one test.

4. **Every consumer follows.** The editor's diagnostic list gains a group per source and an underline
   per span; the `/vim-help`-style gutter mark stays. The VS Code task and the language server
   ([IDEA-029](IDEA-029.md)) take their range from the value rather than recomputing one. The assistant's
   prompt describes the fields it will see.

5. **It is a breaking change to a public value.** `PageDiagnostic` is a positional record in `Sdk` and in
   `Core`, so every construction site moves; `PublicAPI.Unshipped.txt` records the shape while the change
   is on a branch and `PublicAPI.Shipped.txt` records it at the merge. A defaulted `init` property for
   `Source` keeps existing construction sites compiling, and the span is the breaking half.

6. **The documentation** is a line in `/help` where diagnostics are described, a `PageAuthorPrompt`
   sentence with its `Version` bumped, and the diagnostics section of [pages](../pages.md).

## Open questions

1. **Span, or end position.** A `TemplateSpan` is the honest type and a rename of the member every
   caller uses; two more nullable members on the existing record are additive and ugly. The end position
   is the same three numbers either way.
2. **Whether the source is a key or a kind.** A key is what a consumer needs to group and link; a
   boolean "this is a library" would be cheaper and useless for the case that matters, which is naming
   *which* library.
3. **Whether the map is line-based or character-based.** Roslyn's mapped span is per generated line, so
   an end column is exact within one line and approximate across a line break. Saying "the span is
   within one template line" is a limit worth writing down rather than discovering.
4. **Whether the same value should carry the severity's reach.** The editor wants to know whether the
   page will still serve (`pages-a-warning-never-stops-a-page`); that is a property of the set, not of
   one diagnostic, and belongs to the result that carries them ([IDEA-037](IDEA-037.md)).

## Not in scope

- **Fixing a diagnostic.** A span is where a problem is, not what to do about it.
- **The template-shape warnings** — `%><%` with nothing between, an empty block, a `%>` inside a string.
  They are [WHIM-02](WHIMS.md), and they land in the same list once someone writes them.
- **Teaching the compiler's C# diagnostics to be pretty.** Roslyn's text is quoted, not rewritten.

**Origin:** the owner's comparison of the site's IDE plan (`/ide-plan`, a stored page that is not in this
repository) with the job list of 2026-11-15. The IDE plan asks for "the span of a diagnostic"; the
observation that the span is already in hand, and that the source key is already in `Core`'s value and
dropped by the `Sdk` one, is from reading `PageCompiler.MapDiagnostics` and `PageDiagnostics.ToSdk` on
`trunk`.

The plan that places it is [host support for the IDE](../plans/ide-host.md); this is slice 0 there, first because
it is smallest and because a public value should stop growing consumers after it changes.

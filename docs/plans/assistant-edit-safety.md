<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Assistant edit safety

| | |
| --- | --- |
| Status | Implemented in this branch; the decisions are numbered when it lands |
| Size | Medium — four assistant tools, one Core range editor, one template check, the array argument schema, and the tests and documents for each |
| Area | Core (Assistant, Compilation), Web (Assistant tools, page compilation), docs |
| Backlog | None. The page editor landed first (DEC-259–DEC-268); this is the assistant's half of the same work |
| Decisions | D1–D5 below, referred to in prose until the merge takes `DEC-` numbers |

## The short version

The live page editor exposed a class of mistake the assistant tools could not
prevent: an edit is written against line numbers counted in an escaped JSON
blob, the tool applies exactly what it was asked, and a clean compile is no
evidence that the range was the one intended. Two production edits showed the
shape of the problem — a duplicated `if` and a redirect body replaced by a
bare `return;` — and both compiled.

This change makes the arithmetic unnecessary or makes it fail loudly:

1. `edit_page` returns the unified diff and the added and removed counts.
2. `edit_page` accepts exact text assertions for the first and last selected
   line, so an off-by-one is a refusal.
3. `read_page` and `read_revision` put each line's number on the line.
4. `edit_page` accepts several hunks in one atomic save against one hash.
5. A template-shape check runs beside the compiler for stray closers and
   unbalanced braces.

## Goals

1. An edit that compiles but mangles a block is visible in the same turn.
2. An off-by-one that would replace a neighbouring line is refused before
   anything is stored.
3. A model never has to count lines in JSON to name a line.
4. Several hunks leave one revision, not one per hunk.
5. A template-shape mistake is reported even when the C# compiler is silent.

## Non-goals

- **A full `replaceBetween(anchorBefore, anchorAfter, text)` form.** It needs
  the adjacent-anchor case to mean an insertion, which is a second range
  primitive. `expectFirst`/`expectLast` already turn the reported off-by-one
  into a refusal; the anchor-only form can follow if it is still wanted.
- **A warning for every `%><%` or an empty C# block.** WHIM-02 records that
  idea; it would warn on the shipped pages, which use `%><%` deliberately.
- **Making shape warnings errors.** A warning never stops a page compiling,
  and a page that is served with a warning is still inspectable.
- **Changing `write_page`.** It is a deliberate whole-page replacement, and
  the diff is the edit tool's answer.

## What exists today

| Piece | Before | Used here for |
| --- | --- | --- |
| `EditPageTool` | One range, revision-hash check, diagnostics and hash in the answer | The diff, the line assertions, both edit forms, the no-op answer |
| `PageRangeEditor` | One inclusive range, a size limit | A second overload with expectations, and `ReplaceAll` for hunks |
| `ReadPageTool` / `ReadRevisionTool` | A joined source string | The shared projection, now numbered |
| `TemplateSourceGenerator` / `PageCompiler` | Tokenizer and Roslyn diagnostics | The shape check runs beside them and its warnings are stored with theirs |
| `ToolArguments` | Strings, integers and booleans | An array of objects, validated recursively |
| `ToolRegistry` | A flat property schema | The nested array schema the model is offered |

## Decisions

### D1 — `edit_page` answers with the diff, and a no-op is not stored

The result carries `addedLines`, `removedLines`, `coarse`, `truncated` and the
unified diff, bounded by the assistant's result budget. An edit that leaves
the source byte-identical is not written: it answers `changed: false`, the
current hash and a warning, because a duplicate revision is noise in a
history whose job is to record changes. It is also not listed in the
transcript's changed-pages card, which follows what `PageWrites` says was
saved.

### D2 — A line assertion is a refusal, not a correction

`expectFirst` and `expectLast` are the exact text of the first and last
selected line. A mismatch refuses the edit and says which line failed. This is
the minimum the report asked for; an anchor-only address is a later step.

### D3 — Read lines carry their number

`read_page` and `read_revision` prefix each line with `N| `, where `N` is the
1-based physical line the editor counts. The prefix is not source, and the
tool descriptions and prompt say so. A truncated read keeps complete lines at
both ends, so the numbers remain on the lines that survive.

### D4 — Several hunks are one edit against the original lines

`edit_page` takes either one range or an `edits` array; `endLine` defaults to
`startLine`, so a one-line edit names only the line it changes. Every entry's
line numbers refer to the source that was read, not to the result of the entry
before it; ranges must not overlap. The hunks are applied from the bottom up
and saved once, conditionally on the same hash, so the history gains one
revision rather than one per hunk.

### D5 — Template shape is checked beside the compiler

`TemplateShapeCheck` reports a `%>` in literal text (`TEMPLATE003`), a `{`
that is never closed across the template's code blocks (`TEMPLATE004`) and a
`}` with no opener (`TEMPLATE005`). It ignores strings, characters, comments
and raw strings. The warnings are merged with the compiler's diagnostics in
`PageImageBuilder`, so a clean compile no longer hides them; they are
warnings, so the page is still served.

## Tests

- `PageRangeEditorTests`: expectations, multi-hunk ordering, overlap, range
  and size refusals.
- `ToolArgumentsTests`: arrays of objects, nested required and unknown
  properties, nested types and repetitions.
- `TemplateShapeCheckTests`: balanced and unbalanced braces, stray closers,
  strings, comments, raw strings, and an unterminated block left to the
  tokenizer.
- `PageImageBuilderTests`: a shape warning is stored beside a successful
  compile, and a no-code template can still carry one.
- `PageToolExecutorTests`: diff and counts, a refused expectation, a
  multi-hunk save, an overlap, a no-op, both forms at once, an incomplete
  range, an empty array and the nested schema.
- `PageAuthorPromptTests`: the prompt is `page-author-v26` and teaches the
  numbered lines, the assertions, the array and `changed: false`.

## Documents

- [assistant](../assistant.md): the edit answer, the two forms, assertions and
  numbered reads.
- [architecture](../architecture.md): `TEMPLATE003`–`TEMPLATE005` and why the
  check sits beside the compiler.
- [pages](../pages.md): a no-code page can now carry a shape warning.
- The author manual `/help` is unchanged: none of this is a page-authoring
  capability.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The formatter cannot align a run of declarations, and deletes one aligned by hand

| | |
| --- | --- |
| Difficulty | caffeinated-squirrel |
| Utility | low |
| Status | parked |
| Blocked by | — |
| Plan | — |
| Area | Code (formatting and the gate), tools, docs |
| Location | `tools/align/`, `Makefile`, `CONTRIBUTING.md`, `.vscode/tasks.json`, `docs/testing.md` |
| Spike | — |

## Idea

A run of declarations that share an `=` reads better aligned than ragged, and nothing in the
toolchain will write it — worse, the toolchain deletes it. Roslyn decides the spacing of `=` in a
declarator itself: exactly one space either side, and everything else is removed. It is not a rule
an option controls. On a probe file, `csharp_space_around_binary_operators = ignore` left the
padding around `+` alone and still deleted every space of `using Alias        = …` and
`const int One   = 1;`, and the default reports the same lines as `Fix whitespace formatting.
Delete N characters.`

So an aligned declaration is already a `make format-check` failure, and the same formatter runs in
the editor: VS Code's Format Document is what `make format` runs, which is why the question "will my
alignment survive" has no yes in it. Anything that aligns has to run *after* Roslyn and has to own
the check, because the padding it writes is what the whitespace verifier calls a fault.

A different formatter is not the way out. clang-format does carry `AlignConsecutiveAssignments` and
does align a declaration run, but it has no C# string literal handling: on a copy of this tree it
rewrote 394 of 518 files, and 11 of the 14 projects stopped compiling — a `$"""` becomes `$"" "`, and
the seeded page markup inside it is tokenised as code and rewritten silently
(`<a class="skip-link"` becomes `< a class = "skip-link"`). That is a corruption a build does not
always catch, and it is the reason a swap is not the answer today.

**Parked on 2026-11-20 by the owner**, on one of two upstream moves: Roslyn growing a spacing rule
that allows a pad around `=` — or at least one that leaves an existing pad alone — or clang-format
learning to lex a C# raw string literal. Either one makes this a configuration change rather than
a tool, and writing the tool while a supported route may arrive is work that would be thrown away.
The sketch below is what the item does if neither arrives.

## Sketch

1. **A Roslyn parser, and trivia only.** `tools/align/` in the shape `check-pages` and
   `check-backlog` already take; `Microsoft.CodeAnalysis.CSharp` is pinned in
   `Directory.Packages.props`, so the tool parses a file, reads where each `=` is, and rewrites
   whitespace *between* tokens. It never re-tokenises text, so a raw string, a verbatim string and
   a comment are the same length before and after — the one thing clang-format got wrong.
2. **A run is consecutive, one line, one shape.** Two or more declaration statements (locals,
   fields, `const`s) at one brace depth with no blank line or comment between them; the padding is
   the distance to the widest left-hand side plus one space. A run of one is left alone, so the
   transform is idempotent by construction: the second pass finds the same runs and the same widths.
3. **Alignment goes last, and only last.** `make format` becomes whitespace, then `IMPORTS`, then the
   aligner. Roslyn deletes padding, so the reverse order silently does nothing.
4. **The gate normalises and compares.** `--verify-no-changes` cannot be asked of a deliberately
   padded file, so `format-check` cannot stay one call. Either the aligner strips a scratch copy and
   `dotnet format whitespace --verify-no-changes` judges that, then the real tree is compared with
   align-after-format of itself; or the tool calls the Roslyn formatter itself — Workspaces is pinned
   too — and the two steps become one binary and one pass. The open questions choose between them.
5. **The editor is a separate problem.** Format Document will de-align and cannot be taught
   otherwise, and VS Code has no workspace keybindings file: the override is a line in the user's
   `keybindings.json`, documented rather than shipped. A per-file mode (`tools/align --file`) keeps
   that binding fast enough to keep the muscle memory, which is the open question below.
6. **The documentation** is the `format` and `format-check` lines in `make help`, the gate's
   paragraph in [testing](../testing.md), and a note in [CONTRIBUTING](../../CONTRIBUTING.md) that
   Format Document collapses the padding and `make format` puts it back.

## Open questions

Wait for the plan — the owner's answers choose the shape, not the intent. All five can wait, and none
has to be answered to know the item is parked: the upstream change that unparks it picks the shape
before any of them matters.

1. **Which runs align.** Declarations only, or also alias `using`s at namespace scope, `init`
   properties and enum members; and whether a trailing `//` comment joins the run.
2. **A ceiling.** Whether a run whose longest name is far longer than the shortest still aligns, and
   where the cut is if not — a pad of twenty spaces reads as a mistake rather than as order.
3. **The editor's part.** A per-file mode and a documented keybinding override, or Format Document
   stays free to de-align and `make format` is the repair.
4. **Which step owns the check.** Strip-then-verify on a scratch copy, keeping `dotnet format` as the
   authority on everything else; or the tool calling the Roslyn formatter, one binary and one pass,
   and `make format-check` no longer mentioning `dotnet format` for whitespace at all.
5. **Whether the gate runs it at all.** An idea with `low` utility may be a `make format` step the
   owner runs and the gate does not check, in which case the padding is a preference rather than a
   rule and this item is smaller than it reads.

## Not in scope

- **A different formatter, today.** clang-format's alignment is real and the rest of it is unusable
  on raw strings; the evidence is in the idea above and belongs to this item, not to a plan. If
  upstream fixes the strings, the swap becomes the first thing this item weighs rather than
  something it rules out.
- **Spacing anywhere else.** Roslyn owns it, and the aligner only pads a `=` inside a run and strips
  what it would pad.
- **A reformat commit.** `make format` after the tool lands aligns whatever runs the tree already
  has; a separate commit that only adds padding would be the same diff with an extra step.

**Origin:** the owner's question of 2026-11-20, asking whether `.editorconfig` could make
`dotnet format` align a list of declarations on the equals sign, and — after the probe showed it
cannot — their instruction to record it as an idea rather than a plan: "idea, not plan, but yeah",
and "will the changes that your tool makes survive a vscode document reformat".

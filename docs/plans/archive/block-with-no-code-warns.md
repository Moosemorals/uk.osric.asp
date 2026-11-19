<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A block with no code says so, and a block cut short is diagnosed as cut short

| | |
| --- | --- |
| Status | Merged 2026-11-19 — the owner answered the four questions and said build; the record is `pages-a-block-with-no-code-warns` |
| Size | Small — two warnings in a `Core` checker, one consumer in `tools`, three documents and their tests; no host change, no migration |
| Area | Core (Compilation), tools, docs |
| Spike | None. Every claim below was read off `trunk` on 2026-11-19: `TemplateShapeCheck`, `TemplateTokenizer`, `TokenType`, `TemplateSourceGenerator.IsLibrary`, `PageImageBuilder.Build`, `tools/check-pages` and the shape-check tests |
| Backlog | [`block-that-is-not-code-compiles`](../backlog/block-that-is-not-code-compiles.md) is this step, and it closes at the merge |
| Decisions | `pages-a-block-with-no-code-warns` is new. It cites `pages-the-shape-check`, `pages-a-comment-is-not-code` and `pages-a-warning-never-stops-a-page` and edits none |

## The short version

`TemplateShapeCheck` catches the shape problems that compile cleanly — a stray `%>`, an unclosed `{`,
a stray `}` (`pages-the-shape-check`). Two shapes still get through, and one of them is answered with
the wrong cause.

- **A block that holds no code is decisive, silently.** Kind is "is there a token that is not text or
  comment", so `<% %>` makes a text page a **page** — compiled, wrapped, given a banner and a title —
  and `<%! /* note */ %>` satisfies `IsLibrary`'s *declares* flag while declaring nothing, so a text
  page carrying one is stored as a **library** and its key answers `404`.
- **A block cut short inside a literal is blamed on the text after it.** `<% string s = "%>"; %>` ends
  at the `%>` inside the string; the rest is text, Roslyn says `CS1010` against the quote, and the
  checker adds `TEMPLATE003` about text that is a symptom rather than the cause.

Both are warnings, in template space, merged where `TEMPLATE003`–`005` already are, and subject to
`pages-a-warning-never-stops-a-page`. Neither needs Roslyn: the tokenizer's tokens carry the first,
and the scanner's own literal tracking already knows the second.

## What exists today

| | |
| --- | --- |
| The checker | one pass over `tokenized.Tokens`: `AddStrayClosers` on every `Text` token, and `AddUnbalancedBraces` over the rest, skipping comments; a template the tokenizer refused keeps only its `TEMPLATE001` |
| The scanner | `Scan` walks a token's value, skipping `//`, `/* */`, strings (plain, verbatim, interpolated, raw) and characters; `SkipString` and friends answer `value.Length` whether a literal closed at its last character or never closed |
| Kind | `TemplateSourceGenerator.Generate` branches on `IsLibrary`, whose `declares` flag is "a `Declaration` token exists" — not "it declares something" |
| The host's merge | `PageImageBuilder.Build` calls `TemplateShapeCheck.Check` for every template and stores the result as the diagnostics of a no-code page and beside the compiler's for every other kind. **Nothing in `Web` changes** |
| `check-pages` | compiles every file but `Text`, never calls the shape check, and prints the compiler's diagnostics in the problem matcher's shape |
| The seeds | the only corpus that would say whether a new rule is quiet enough to keep, and nothing asks them today |

## The two rules

1. **`TEMPLATE006` — the block holds no code.** A `Code` or `Declaration` token whose value is
   whitespace and comments only, judged lexically by the scanner that already skips both. A lone `;`
   stays code, because only Roslyn can call a statement empty. Two messages from the one code, because
   the consequence differs: *"this block holds no code"* when the template has code elsewhere, and
   *"…and it is what makes this template a page"* when this block is the only one. An empty `<%= %>`
   is already `TEMPLATE002`, an error, and an empty `<%# %>` is a legitimate note: neither is touched.

2. **`TEMPLATE007` — the block holds only a comment.** Its own code, because the fix is different: the
   note the author meant is `<%# … %>`, which leaves the template as it was. The same consequence
   clause, in the template's own words — a lone code block makes it a *page*, a lone declaration makes
   it a *library, which is never served*.

3. **`TEMPLATE008` — the block ends inside a literal or a comment.** The scanner learns to say *a
   literal never closed* rather than only *the scan ended here*: each skip answers whether it found its
   terminator, and an unclosed one warns at the literal's opening quote. The message carries the way
   out, because the author has nowhere else to look: *"the block ends inside a literal; a block ends at
   the first `%>`, so write the character as `\u003e`, or join `"%"` and `">"`"* — and, inside a
   comment, *"close it with `*/`"*. The `\u003e` is spelled so the C# source writes the character
   sequence rather than the character.

4. **The position is the opener.** A `Token` now carries where its opener starts for every block kind,
   not only writes, so a dead block whose opener stands on another line from its content still points
   at the line the author will look at.

5. **One mistake, one warning.** The text run after a cut block continues the same string, so its
   first `%>` is the closer the author meant. `AddStrayClosers` skips that one occurrence; a later `%>`
   in the same run is still `TEMPLATE003`. A template the tokenizer refused keeps today's behaviour —
   the error at the opener, and none of the new guesses beside it.

## The change

1. **`TemplateShapeCheck`** gains the three rules. The scanner's skips change from `int` to a value
   that says whether the literal closed, and `Token` carries its opener for every block kind; `Check`
   keeps its signature and its ordering, so `PageImageBuilder` needs no change.
2. **`tools/check-pages`** stops skipping `Text` and asks the shape check for every file: shape
   warnings and the compile's diagnostics go out through the one `file:line:column` line it already
   emits, deduplicated as it already does. It **fails on a warning as well as an error**, because a
   warning is an error everywhere else in this repository and this tool should not be the exception.
3. **`/help`** gains the paragraph beside the `<%#` entry: a block ends at the first `%>`, so a string
   holding one ends the block early and the rest of the line becomes text; write the character as
   `"%\u003e"`, or join `"%"` and `">"`, which also works in a verbatim or raw string; in literal text
   write `%&gt;`.
4. **`architecture.md`** gains `TEMPLATE006` and `TEMPLATE007` in the diagnostics table and a sentence
   under it for the two shapes; **`pages.md`**'s shape-warning sentence names them.
5. **The assistant's prompt** gets one clause after "It ends at the first `%>`" — a `%>` in a string
   ends the block early, the two spellings of the character, and `TEMPLATE007` — with `Version`
   bumped to `page-author-v42`.
6. **The record**: the item closes, and **pages-a-block-with-no-code-warns** is written at the merge,
   stating the rule rather than restating `pages-the-shape-check`, which it cites. `TEMPLATE007` is
   that entry's second face: "a block ends at the first `%>`" is unchanged and only its diagnosis moves.

## Tests

`TemplateShapeCheckTests`, one behaviour each:

- `<% %>`, a whitespace-only block and `<% /* note */ %>` warn with the "only block" message on a
  template that holds nothing else, and with the plain message when a second block holds code;
- `<%! /* note */ %>` warns, and a text page carrying one is no longer generated as a library — the
  two facts asserted through `TemplateShapeCheck` and `TemplateSourceGenerator` respectively, which
  is where the item's "text page that gains the declaration" lands;
- `<% s = 1; %>`, `<%# note %>` and `<% ; %>` do not warn;
- `%>` inside an ordinary, verbatim, interpolated and raw string warns once, at the quote, and the
  text after it draws no `TEMPLATE003` for the closer that ended the block;
- a block cut short inside a `/* */` warns, and a later `%>` in the text still draws `TEMPLATE003`;
- an unterminated block still reports only `TEMPLATE001`.

The seeds' silence is the gate's job: `make check-pages` now asks the shape check for every seed file,
so a rule that is louder than the corpus shows up as a new line in the gate rather than as a test that
hard-codes today's answer. `check-pages` has no test project, and this change does not add one.

## What the owner decided

| Question | Answer | What it settled |
| --- | --- | --- |
| The comment-only declaration | **A separate warning** | `TEMPLATE007` rather than a second wording of `TEMPLATE006`, because the fix is a different construct; the literal case moved to `TEMPLATE008` |
| A block whose opener is on another line | **Update `Core`** | `Token.OpenerPosition` is set for every block kind, and both dead-block warnings report the opener |
| Whether `check-pages` fails on a warning | **Yes — warnings are errors everywhere else** | the tool fails on any diagnostic, so the seeds' silence is now a gate rather than an observation |
| The assistant's prompt | **Yes, update the assistant** | the clause and `page-author-v42` ship in this step |

## Not in scope

- **Fixing the template.** An autofix that deletes a dead block or escapes a closer is a different
  change with different risk.
- **Making either an error.** `pages-a-warning-never-stops-a-page`.
- **`%><%` between blocks.** Three of the seeds' four uses are the only spelling for a declaration
  followed by code; [`declaration-does-not-name-its-close`](../backlog/declaration-does-not-name-its-close.md)
  marks that junction in the grammar rather than scolding it.
- **An escaped closer.** `%%>` stays unspelled; `declaration-does-not-name-its-close`'s record holds
  the argument.
- **Publishing to VS Code.** `check-pages` already puts its warnings in the Problems list.
- **Roslyn.** None of this needs the compiler, which is the point of the shape check.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A declaration block does not name its close

| | |
| --- | --- |
| Difficulty | protective-dragon |
| Utility | medium |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Core (Compilation), tools (VS Code grammar), Web (seeds), docs |
| Location | `src/uk.osric.asp.Core/Compilation/TemplateTokenizer.cs`, `Token.cs`, `TemplateScriptSkeleton.cs`, `TemplateShapeCheck.cs`, `tools/vscode-asp/syntaxes/asp.tmLanguage.json`, `src/uk.osric.asp.Web/Pages/Seeds/`, `docs/architecture.md`, `src/uk.osric.asp.Web/Pages/Seeds/help` |

## Idea

A block's opener says what it is — `<%`, `<%=`, `<%==`, `<%!`, `<%#` — and its closer says nothing:
`%>` closes all five. A declaration preamble running to a hundred lines therefore ends in the same two
characters that open the code beneath it, and `%><%` is a junction a reader has to resolve by counting
brackets. The shape check can only complain about it, which is why a warning for it was refused: the
seeds use it because the language leaves them no alternative.

Let the closer name its opener. `!%>` closes a declaration, `=%>` an escaped write, `==%>` a raw write,
`#%>` a comment, and `%>` still closes everything — the closer is the opener mirrored, so the glyph
stays adjacent to the body on both sides. Then `!%><%` says what `%><%` cannot.

**The glyph may not be optional.** `<%!` ↔ `!%>` is fine while the echo is required; while both forms
are legal, `name!%>` has two readings — the author's null-forgiving operator plus a plain closer, or
their echo — and the tokenizer has to pick one. It picks the echo, the operator vanishes, and the
resulting `var x = name` still compiles. `!` is a postfix operator, so spacing is no help (`=> x!` is
the idiomatic shape in a declaration), and declaration blocks are **concatenated**, so a lost glyph
can even join a neighbour's. The fix is that the closer is one fixed token per kind: a `!` is body
unless it is the closer's own, and an author who wants the operator writes it:

```
<%! var x = name %>       →  not a close in release 2
<%! var x = name !%>      →  body `var x = name`, closer `!%>`
<%! var x = name !!%>     →  body `var x = name !`, closer `!%>`
```

That is a counting error at worst, never a reinterpretation, which is the difference between a spelling
rule and a footgun.

## Sketch

### Release 1 — legal, preferred, warned about

1. **`!%>` (and `=%>`, `==%>`, `#%>`) close their own block.** `%>` closes every block as it does today,
   so nothing stored breaks. `IndexOf("%>")` still finds every closer, and because the kind is now
   fixed the lookback is deterministic rather than a guess.

2. **A declaration closed with `%>` warns.** `TEMPLATE009`, a warning: "a declaration block closes
   with `!%>`". The seeds migrate in the same change, `make check-pages` starts reporting it, and the
   page list says *Compiled with warnings* on anything the owner has not yet touched.

3. **A `!` that may be an operator warns.** Old code written `<%! string N => x!%>` closes with the
   echo and would otherwise lose the operator in silence. `TEMPLATE010` fires when a declaration
   closes `!%>` and its body ends with something that can end an expression: "if that `!` is the
   null-forgiving operator, write `!!%>`". It stays useful after release 2, it is quiet in practice
   because a correct declaration ends in `;` or `}`, and a trailing preprocessor line
   (`<%! #nullable enable !%>`) is excluded by a check that the last line starts with `#`.

### Release 2 — the echo is the close

4. **`%>` stops ending a declaration.** The scan looks for `!%>`; a `%>` in the body is content. The
   message names the closer to write, at the opener that needs it.

5. **The dividend.** Because `%>` no longer closes a declaration, a declaration may hold one:

   ```csharp
   <%! static readonly Regex Percent = new(@"%\d+%>");  !%>
   ```

   That is the capability an escaped closer was going to buy, arriving free, where it is actually
   wanted: declarations are where regexes, format strings and documentation live. Code blocks still
   end at the first `%>`, so `TEMPLATE008` and the `\u003e` note from [`block-that-is-not-code-compiles`](closed.md) keep
   their place.

6. **Writes and comments keep the echo optional.** An expression cannot end in `=` or `==`, and a
   comment losing a trailing `#` costs nothing, so there is no ambiguity to remove — and requiring
   `=%>` would rewrite every `<%=` on the site for consistency alone. `/help` states the rule as it
   is: a declaration closes with `!%>`, every other block closes with `%>` and may also close with its
   own glyph.

### The plumbing both releases need

7. **The token records the closer it saw**, defaulted to `%>` so existing expectations in
   `TemplateTokenizerTests` keep their meaning. `TemplateScriptSkeleton` stops computing
   `OpenerLength + Value.Length + 2` — the one place that desynchronises silently when a closer is a
   different width — and every position after the closer is computed from the index, so correctness
   follows from the recorded width.

8. **The grammars.** `asp.tmLanguage.json`'s five `end` patterns become the closer for their own block
   (release 1: `(!?%>|%>)` and its siblings), and the `(?=%>)` lookaheads inside the C# rules follow.
   `language-configuration.json` keeps `" %>"` until release 2, then inserts `" !%>"` for a
   declaration.

9. **The gate.** `make check-pages` compiles the seeds with the product's compiler, so it carries
   `TEMPLATE009`/`TEMPLATE010` to every seed without new code once [`block-that-is-not-code-compiles`](closed.md)'s
   consumer lands.

10. **The record.** A plan proposes **pages-declarations-close-with-a-bang**, citing
    `pages-the-shape-check` and `pages-a-comment-is-not-code`, whose "it ends at the first `%>` like
    every other block" sentence has to narrow to the blocks it still describes. `TEMPLATE009` keeps
    its meaning across both releases and **escalates from warning to error** at release 2 — the same
    author-facing fact told louder, which is the shape of a deprecation; if that is too loose for a
    permanent code, the alternative is a new error code at release 2 and `TEMPLATE009` retired with
    the spelling it named.

11. **The documents.** `architecture`'s "a block ends at the first `%>` whatever it holds" becomes two
    sentences, one per release-2 world; `/help`'s syntax list gains the closer beside each opener and
    loses the sentence that says a declaration ends at `%>`; the extension's README says its grammar
    now ends each block on its own close.

## What was considered and is not taken

- **The `%`-before family** (`%!>`, `%=>`, `%==>`, `%#>`). It has no C# collision at all — `%` demands
  a right operand, so none of those sequences can appear in valid code — and it was the reason to
  prefer it while the echo was *optional*. With the echo required the first collision is gone anyway,
  and this family costs `IndexOf("%>")`, the mirror symmetry and the junction's readability
  (`%!><%` names the block it leaves; it reads less like a bracket pair).
- **`%%>` as an escaped closer.** It is already a spelling: `<%%>` is an empty code block, pinned by
  `TemplateTokenizerTests`. Text does not need it (`%&gt;` renders `%>` and never reaches the
  tokenizer), and in code it would delete a character, breaking the column alignment `#line` and the
  generator's padding promise. The character is written `\u003e`.
- **A `%`-count or a second opener, to quote a `%>` inside a block.** The rule is sound and unbounded
  is cheaper than capped — but `<%%>` collides, the opener space multiplies by five kinds, and every
  added level makes `!%><%` harder to read. The spelling to take if demand arrives is to count the
  **angle** bracket instead: `<<% … %>>`, `<<<% … %>>>`. It collides with nothing (`<<%` contains
  `<%`, `%>>` contains `%>`, so both scans stay anchored), composes with the required close as
  `!%>>`, and reads as the bracket pair this idea is about. It is not taken now because `\u003e`
  covers code and writes, the required close frees declarations, and nothing in the repository needs
  it — the demand becomes real when a page renders template source (a preview, a snippet in an
  editor, a playground), and TextMate can only colour one extra level without a parser.

## Open questions

1. **Whether writes and comments should be required too.** Uniformity is one rule for the reader;
   it is also a rewrite of every `<%=` on the site for no ambiguity removed. Recommended: optional.
2. **Whether `TEMPLATE010` survives release 2.** It is unambiguous by then, but the author who meant
   `name!` still gets `name` in silence. Recommended: keep it.
3. **What `%>` does inside a `#%>`-closed comment.** Nothing changes — comments still end at the first
   `%>` — so "a comment cannot mention one" survives, and the record should say so rather than let it
   be discovered.
4. **An echo of the wrong block** (`#%>` inside a declaration). It is body, then a C# error. A specific
   diagnostic is nicer and is not worth a code until someone writes it twice.

## Not in scope

- **A formatter.** Two spellings per write and comment drift without one; the suggestion, the gate and
  the seeds keep them together until a formatter exists.
- **Requiring the echo for every kind.** The ambiguity is declaration-only.
- **The assistant's output.** Whether the prompt writes `!%>` for declarations is a prompt version of
  its own, and it follows the plan rather than this idea.
- **Colouring the closer per block.** VS Code gets it from the grammar above; the site's editor is
  [`editor-is-a-textarea`](editor-is-a-textarea.md), and a language server is the VS Code slice of `check-without-storing`.

**Origin:** the owner's proposal on 2026-11-18 against [WHIM-02](WHIMS.md)'s `%><%` clause, then
sharpened twice: the mirror spelling (`!%>`, not `%!>`) because `IndexOf("%>")` keeps working and the
closer is the opener reversed; and the echo made **required** because optional leaves `name!%>`
undecidable, which the owner found in `<%! var x = name  !%><%!;!%>`.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A block that is not code still compiles

| | |
| --- | --- |
| Difficulty | eager-beaver |
| Utility | medium |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Core (Compilation), tools, docs |
| Location | `src/uk.osric.asp.Core/Compilation/TemplateShapeCheck.cs`, `test/uk.osric.asp.Core.Tests/TemplateShapeCheckTests.cs`, `tools/check-pages/Program.cs`, `docs/architecture.md`, `docs/pages.md`, `src/uk.osric.asp.Web/Pages/Seeds/help` |

## Idea

`TemplateShapeCheck` reports the shape problems that compile cleanly — a `%>` in literal text, a `{`
never closed, a `}` with no opener (`pages-the-shape-check`). Two shapes still get through, and one of
them is answered today with the wrong cause.

**A block that holds no code.** `<% %>`, a block of whitespace, and `<% /* note */ %>` are dead
lexically and decisive semantically, because kind is "is there a token that is not text or comment".
`<% %>` turns a text page into a **page**: compiled, wrapped, given a banner, a skip link and a title
requirement. `<%! /* note */ %>` satisfies `IsLibrary`'s `declares` flag while declaring nothing, so a
text page carrying one is stored as a **library**, and its key answers `404`. The manual already says
`<% /* … */ %>` "makes the template code, the two are not interchangeable" — this is what that costs.

**A block that ends inside a literal.** `<% string s = "%>"; %>` ends at the `%>` inside the string,
the rest of the line becomes text, Roslyn says `CS1010` against the quote — and the check adds
`TEMPLATE003: '%>' has no matching '<%'` about the leftover text, which names the symptom as the
cause. The scan already knows: `SkipString` and its siblings return `value.Length` for a literal that
never closes.

Both are warnings in the existing value, positioned in template space, merged where `TEMPLATE003`–`005`
already are, and subject to `pages-a-warning-never-stops-a-page`. Nothing here needs Roslyn: the
tokenizer's tokens and the scanner's own literal tracking carry every fact.

## Sketch

1. **`TEMPLATE006` — the block holds no code.** A `Code` or `Declaration` token whose value is
   whitespace and comments only, judged lexically: a lone `;` is a statement only Roslyn can call
   empty, so it stays code. Positioned at the character after the opener, which is always on the
   opener's line. Two messages from the one code, because the consequence differs: "this block holds
   no code" when the template has other code, and "…and it is what makes this template a page" when
   this block is the only one. An empty write is already `TEMPLATE002`, an error, and an empty
   `<%# %>` is a legitimate note: neither is touched.

2. **`TEMPLATE007` — the block ends inside a literal.** The scan already walks strings, characters and
   block comments; it gains a report when the token's end is reached still inside one, positioned at
   the literal's opening quote. The message carries the way out, because the author has nowhere else
   to look for it: *"the block ends inside a string literal; a block ends at the first `%>`, so write
   the character as `\u003e`, or join `"%"` and `">"`."* Watch the source: in a C# string `\u003e`
   is unescaped by the compiler, so the message is built with `\\u003e` or a raw literal.

3. **One mistake, one warning.** The text run that directly follows such a block starts inside the
   string, so its first `%>` is that block's own closer. `AddStrayClosers` skips that run; later `%>`
   in it are still `TEMPLATE003`. A template the tokenizer refused keeps today's behaviour: the error
   at the opener, and none of the new guesses beside it.

4. **Nothing to wire in the host.** `PageImageBuilder.Build` already calls the check on every save and
   every lazy compile and merges the result, for pages and no-code pages alike. The editor, the
   history, the page list and the assistant's save answers show the new warnings without a line of
   `Web` code.

5. **`make check-pages` becomes a consumer, and the seeds become the noise test.** The tool skips
   no-code templates and never asks for the shape; it starts asking for every file, prints the
   warnings in the same `file:line:column: severity message` shape its problem matcher already reads,
   and fails only on errors. The shipped seeds then say, mechanically, whether a rule is quiet enough
   to keep.

6. **The tests** go in `TemplateShapeCheckTests`: empty, whitespace-only and comment-only blocks in
   both kinds; a text page that gains one and a text page that gains the declaration; a `%>` inside an
   ordinary, verbatim and raw string; a block cut short inside a comment; the suppressed closer; and
   the seeds' own `%><%` spellings asserted clean.

7. **The record.** `TEMPLATE006` and `TEMPLATE007` are allocated here and never reused. The plan
   proposes **pages-a-block-with-no-code-warns**, stating the rule rather than restating
   `pages-the-shape-check`, which it cites; `TEMPLATE007` is that same entry's second face, since "a
   block ends at the first `%>`" is unchanged and only its diagnosis moves.

8. **The documents.** The diagnostics table and the paragraph beneath it in
   [architecture](../architecture.md); the shape-warnings sentence in [pages](../pages.md); and in
   `/help`'s syntax section, beside the `<%#` entry:

   > A block ends at the first `%&gt;` whatever it holds, so a string that contains one ends the
   > block early and the rest of the line becomes text. An ordinary string writes the character
   > instead of the pair: `"%\u003e"`, or `"%" + "&gt;"`, which also works in a verbatim or raw
   > string, where `\u` escapes are not processed. In literal text write `%&gt;`; it reaches the
   > browser as `%&gt;`.

   The manual is where an author meets `TEMPLATE007`, so it is written in the change rather than
   after it. The assistant's prompt (one clause after "It ends at the first `%>`", and a version
   bump) is worth the same edit and is a separate yes.

## Open questions

1. **Whose block is a comment-only declaration.** `TEMPLATE006` says "no code" for a `<%! … %>` that
   declares nothing; whether the fix it suggests is "delete the block" or "write the declaration" is
   the message's wording, not the rule.
2. **A block that is only whitespace across lines.** The position is the character after the opener,
   which is right for the author and wrong for a block whose opener is on a different line from its
   content. Reporting at the opener would need `Token` to carry an opener position for every kind,
   not only writes; that is a `Core` change to weigh against the rarity.
3. **Whether `make check-pages` should fail on a warning.** It does not fail on one today and warning
   severity does not stop a page; the seeds staying quiet is a habit the gate can only encourage.
4. **What the assistant should be told.** A save's answer already carries the warnings; whether the
   prompt should name the empty block as a thing not to write is [`cannot-check-a-page`](cannot-check-a-page.md)'s subject
   with its own prompt version.

## Not in scope

- **Fixing the template.** A warning says what shape a block has; an autofix that deletes a dead block
  or escapes a `%>` is a different change with a different risk.
- **Making any of them an error.** `pages-a-warning-never-stops-a-page`.
- **`%><%` between two blocks.** The seeds use it four times and three are the only spelling for a
  declaration followed by code. [`declaration-does-not-name-its-close`](declaration-does-not-name-its-close.md) makes the declaration close name itself, so
  the junction is marked by the grammar rather than scolded by a warning; nothing here needs to say
  anything about adjacency.
- **An escaped closer.** `%%>` is already a spelling (`<%%>` is an empty code block), and after
  `declaration-does-not-name-its-close` a declaration does not need one; the character is written `\u003e`. `declaration-does-not-name-its-close`'s record holds
  the full argument and the spelling to take if demand ever arrives.
- **Publishing to VS Code.** The language server is [`vscode-cannot-check-a-page`](vscode-cannot-check-a-page.md); `check-pages`
  already puts the warnings in its Problems list for free.
- **A check without saving.** [`check-without-storing`](check-without-storing.md) answers what a save answers, so it
  inherits these; the assistant's `check` inherits them through that.
- **Roslyn.** None of this needs the compiler, which is the whole point of the shape check.

**Origin:** [WHIM-02](WHIMS.md), promoted by the owner on 2026-11-18. The evidence that the `%><%`
rule cannot be broad is in the seeds; the evidence that the string case is a mis-diagnosis is
`TEMPLATE003` firing on the text after a truncated block.

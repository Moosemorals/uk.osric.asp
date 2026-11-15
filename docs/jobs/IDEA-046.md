<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-046 — The template has no comment, so `<%# %>` is a compile error

| | |
| --- | --- |
| Size | S — a token type, one opener in the tokenizer, five consumers that each have to say what they do with it, and the manual, prompt and grammar entry that teach it |
| Area | Core, Web (pages, assistant, seeds), tools, docs |
| Location | `src/uk.osric.asp.Core/Compilation/TokenType.cs`, `src/uk.osric.asp.Core/Compilation/Token.cs`, `src/uk.osric.asp.Core/Compilation/TemplateTokenizer.cs`, `src/uk.osric.asp.Core/Compilation/TemplateSourceGenerator.cs`, `src/uk.osric.asp.Core/Compilation/TemplateShapeCheck.cs`, `src/uk.osric.asp.Core/Compilation/TemplateScriptSkeleton.cs`, `src/uk.osric.asp.Core/Compilation/TemplateComments.cs` (new), `src/uk.osric.asp.Core/Pages/PageKind.cs`, `src/uk.osric.asp.Web/Pages/PageEndpoint.cs`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `tools/vscode-asp/syntaxes/asp.tmLanguage.json`, `tools/vscode-asp/syntaxes/asp-injection.tmLanguage.json`, `tools/vscode-asp/README.md` |

## Idea

The template has four openers — `<% %>`, `<%! %>`, `<%= %>` and `<%== %>` — and no way to write a
note that reaches nobody. `<%#` is not an opener, so `<%# a note %>` is a code block whose content
begins with `#`, and C# reads that line as a preprocessor directive:

```
template:  <h1>Hello</h1>\n<%# a note %>\n<p>Hi</p>
kind:      Page
generated: #line 2 "page"\n  # a note \n#line default
Roslyn:    Error line 2, column 5: CS1024: Preprocessor directive expected
```

The page is saved with that diagnostic and answers "Handler failed to compile." until it is edited.
The author who wrote a note has broken a working page, and the error names a preprocessor directive
they never wrote.

**The workaround is worse than the error, because it compiles.** `<% /* note */ %>` and
`<% // note %>` are code blocks, and a code block is what makes a template a `PageKind.Page`:

- a no-code page that gains a note stops being an asset — it is compiled, wrapped in the layout and
  loses the `?v=` immutability and `ctx.AssetUrl` that an asset has (`pages-an-asset-is-a-page-with-no-code`);
- a library that gains a note stops being a library, so no page can name it and its key starts
  answering as a page (`pages-a-library-is-named-by-a-using`);
- and the note is emitted into the handler inside a `#line` region, so it buys a compile.

So a note in the source currently costs a page its kind, and the kind is what the site's caching,
wrapping and library rules are built on. A comment should be able to sit in a template without
changing what the template is.

The assistant is the author this bites most, and it has no way to learn: `PageAuthorPrompt` names the
four forms and no comment, both its write tools compile because they store, and nothing in the
template language resembles the `<%# %>` its habits come from. It writes the note, the save fails,
and it rewrites.

## Sketch

1. **An opener, a token, and nothing else.** `<%#` joins the opener switch beside `<%!`, `<%=` and
   `<%==`; it is three characters, like `<%!` and `<%=`, and the `#` cannot be confused with the `=`
   or `!` that follow the others. The content runs to the first `%>` exactly as every other block
   does — no nesting, no `--` terminator, no way to write `%>` inside a comment — so the tokenizer
   gains a branch, not a scanner. An unterminated one is `TEMPLATE001` naming the kind and the opener:
   "unterminated comment block; the '<%#' has no matching '%>'."

2. **It stays in the token stream rather than being dropped.** Three reasons, all of them existing
   code. `TemplateScriptSkeleton` walks the token list and advances its cursor by each token's length,
   so the tokens must still cover every byte of the template or the skeleton's positions drift.
   `TemplateShapeCheck` and the generator read the stream and decide per token type. And "every byte
   is in exactly one token" is the property that lets each consumer state its own choice in one line,
   which is exactly the rule below.

3. **A comment is neither code nor text.** That is the whole semantic rule, and it is three
   conditions:
   - `hasCode` — today `token.Type != TokenType.Text` — excludes `Comment`, so text plus comments is
     still `PageKind.Text`;
   - `IsLibrary` ignores `Comment`, so declarations plus comments is still `PageKind.Library`, and the
     compiler still refines a worker out of it (nothing about a comment reaches a declaration, so the
     `IWorker` entry-point check is untouched);
   - the body switch in `PageSource` gets a `Comment` arm that writes nothing.
   `LibrarySource` needs no change: it emits declarations and nothing else.

4. **The shape check stops counting a comment's braces.** `AddUnbalancedBraces` scans every token that
   is not `Text`; a comment becomes a second exclusion. `<%# { %>` and a note that mentions `}`
   otherwise raise `TEMPLATE004`/`TEMPLATE005` against a page whose code is balanced. Nothing else in
   the check changes: a `%>` in literal text is still `TEMPLATE003`, and a `%>` inside a comment is
   the comment's own terminator rather than a stray closer.

5. **The script skeleton blanks it.** `OpenerLength` gains `Comment => 3` and the construct is blanked
   like a code block, keeping every line break, so a JavaScript page's parse positions are unchanged
   by a note. Miss this arm and the skeleton returns `<%#` and `%>` to Acornima, which finds a syntax
   error in a page that is fine.

6. **A no-code page serves its text without its comments.** This is the part that is easy to miss. A
   `PageKind.Text` revision is never generated at render time: its stored source *is* the response
   (`pages-an-asset-is-a-page-with-no-code`), and the endpoint has two fast paths that hand a head or
   a revision's `Template` straight to `WriteSourceAsync` without asking `PageRenderer` at all.
   Keeping that promise with comments allowed would ship `<%# a note %>` to the browser, so the body
   becomes "the source with each comment construct removed" — `TemplateComments.Remove(template)`, a
   pure `Core` function that joins the `Text` tokens, applied once in `WriteSourceAsync` because that
   is the one place a stored source becomes a response. A comment removes itself entirely, as a code
   block that writes nothing does; it does not leave whitespace in its place. Blanking, which is what
   the skeleton does, is deliberately not reused: the skeleton exists to keep *positions*, the body
   exists to be the bytes a browser gets. The source the author and the tool read stays whole —
   stripping at save time would lose the note and make the revision disagree with the editor — so the
   removal belongs to the response, and `PageKind.Text`'s doc comment ("less its comments") says so.

7. **The fingerprint and the revision are unchanged.** A comment is part of the source, so editing
   one changes the fingerprint and recompiles the page — normalising the text before hashing would
   make a stored image answer for a source it does not name. `RuntimeId` already carries `Core`'s
   module version, so every stored image recompiles once when this lands, and a page that failed with
   `CS1024` becomes a page that compiles.

8. **What an author reads.** A `<dt><code>&lt;%# %&gt;</code></dt>` entry in the manual's
   [template syntax](../../src/uk.osric.asp.Web/Pages/Seeds/help) list (`product-manual-is-a-page`),
   one sentence in `PageAuthorPrompt`'s "Page model" with its `Version` bumped, and an `asp-comment`
   rule in both VS Code grammars, included before the others and scoped `comment.block.asp`, with the
   extension's README paragraph about `<% // note %>` extended to say which of the two is which. The
   HTML comment stays the form that reaches the visitor and stays what the editor's comment toggle
   writes.

9. **The documentation** is the template-language paragraph and the `TEMPLATE001` row in
   [architecture](../architecture.md), the no-code and library paragraphs in
   [pages](../pages.md#assets-and-versioned-urls), and one entry on merge — the name the plan will
   carry is **pages-a-comment-is-not-code**, in bold rather than in backticks because `make decisions`
   fails on a cited name the record does not have yet. The rule it records is that a comment changes
   nothing about what the template is.

## The one thing that changes

`<%#if DEBUG %>` is a code block today, and it compiles: the tokenizer makes it a `Code` token,
`#if`/`#endif` are legal inside the generated method, and the guard works. Making `<%#` a comment
silently drops that guard — both directives become comments and the markup between them is always
written. `<% #if DEBUG %>`, with a space, stays a code block and compiles exactly as it does now, so
there is a way to write it and it is one character away.

Nothing in this repository uses `<%#`: `make check-pages` compiles the seeds and no seed contains it.
The gate is the store, and it is one query — `search_pages` or a `grep` tool call for `<%#` over the
live sources, which also says whether any hit is a directive rather than a comment guess. If one is,
the page is edited to `<% #if` in the same change; if the owner would rather not spend the query, the
cost of being wrong is a page that loses a guard without a diagnostic, which is the one failure here
that is silent.

## Tests

Test-first, and every one of these fails before the tokenizer knows the opener:

- **Tokenizer** — `<%# note %>` is one `Comment` token whose value excludes the delimiters and whose
  position is the content's start; a comment between text, code and a write keeps template order and
  every later position; `%>` ends a comment as it ends every other block, so the rest is text;
  `<%#` does not shadow `<%==`, `<%=` or `<%!`; unterminated is `TEMPLATE001` naming the comment and
  the opener.
- **Generator** — a comment writes no output: the generated source for a template with a note equals
  the generated source for the same template without it; a comment-only template is `PageKind.Text`;
  text plus a comment is `PageKind.Text`; declarations plus a comment is `PageKind.Library` and
  `GenerateLibrary` accepts it; a comment inside a conditional writes nothing.
- **Shape check** — a `{` and a `}` inside a comment raise no warning, and the same braces in code
  still do.
- **Script skeleton** — a comment is blanked, its line breaks survive, and the position of the token
  after it is unchanged.
- **Host** — a no-code page with a comment is stored without compiling, is still an asset, and is
  *served without the comment*, beside `ANoCodePageIsServedRawWhateverTheLayoutPolicyIsAsync` in
  `PageEndpointTests`; and a page with a note compiles clean through `PageImageBuilder`.

## Not in scope

- **`<%-- --%>`.** The ASP.NET server-side comment is the same token type and one more opener in the
  same switch, whenever a second spelling is wanted. One spelling now: `<%#` is the one the assistant
  already reaches for, and two spellings of nothing is a dialect.
- **Stripping comments before fingerprinting.** It would make a comment-only edit free and make the
  fingerprint disagree with the source it names; the recompile is one compile.
- **A comment in the response.** HTML comments already exist, and they are content, not a note.
- **A second comment form on the editor's toggle.** `<!--`/`-->` remain what the toggle writes; a
  template comment is the author's decision about a line, not a block of markup.
- **Teaching the shape check to skip a comment's `%>`.** It already cannot see one: `%>` ends the
  comment, which is the same rule every block follows.

**Origin:** the owner's note of 2026-11-15 — the assistant keeps writing `<%# %>` as a comment, and
the page then fails to compile. The form is not the site's, but nothing in the template language says
so, and the nearest thing that does compile turns the page into something else.

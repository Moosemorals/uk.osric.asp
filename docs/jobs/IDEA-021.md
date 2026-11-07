<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-021 — The assistant cannot validate the JavaScript it writes

| | |
| --- | --- |
| Size | Medium |
| Area | Web, Runtime, Core, assistant |
| Location | `src/uk.osric.asp.Web/Assistant/PageToolExecutor.cs`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs`, `src/uk.osric.asp.Core/Compilation/`, `src/uk.osric.asp.Runtime/` |

## Idea

The assistant can write and edit stored pages. A page that serves JavaScript is a template whose
literal text is JavaScript, but the compiler never parses that JavaScript: `write_page` and
`edit_page` return C# template diagnostics only. A botched edit that leaves half a function in the
middle of a thousand-line script is stored and reported as a successful save. The assistant has no
way to find out until a browser runs the page.

Add a read-only tool that parses the script and returns syntax diagnostics with a position and a
stable code. It is a helper for the assistant, not a replacement for its judgement, and not a gate
on saving.

The [content-type plan](../plans/page-content-types.md) supplies the useful signal: a key ending in
`.js` or `.mjs` is JavaScript source. The validator is useful without that plan — it can validate
supplied content directly — but the plan makes the common path exact and lets the prompt name the
convention.

## Sketch

The shape is a read-only assistant tool over a pure-managed parser, with a position-preserving
template-to-JavaScript skeleton as the fallback when a page still contains template blocks.

### What the parser can and cannot do

The proposed engine is [Acornima](https://github.com/adams85/acornima) 1.9.1, a pure-managed .NET
port of Acorn. It is a real ECMAScript parser, not a lexer:

- It passes the complete Test262 suite for ECMAScript 2026.
- It uses V8's error messages and reports a stable error code, a one-based line, a zero-based
  column and a character index.
- It tracks variable scopes, so duplicate lexical declarations are errors.
- It has a stack guard against the non-catchable `StackOverflowException`, using the same approach
  as Roslyn.
- The package is about 800 KB compressed; the assembly is about 375 KB. There is no native asset
  and no execution.

A throwaway probe against a 986-line / 13.5 KB module produced these results:

| Change | Result |
| --- | --- |
| Valid module | `OK`, ~40 ms cold, <10 ms warm |
| Half a function inserted, leaving two unclosed `{` | `UnexpectedToken: Unexpected token 'export'` at line 986 |
| Balanced function interior inserted, containing `return` | `IllegalReturn` at line 353, exactly the fragment |
| Complete top-level function duplicated | `VarRedeclaration` at line 351, exactly the duplicate |
| Extra `}` | `UnexpectedToken '}'` at line 351 |
| Missing comma in an object literal | `Unexpected identifier 'b'` at line 351 |
| Unterminated template literal | `Unexpected end of input` at line 984 |
| Duplicated valid statements | **Accepted** — syntactically valid |
| Duplicate `var` | **Accepted** — `var` redeclaration is legal |

Two consequences matter.

**Parse modules, not scripts.** Pages are ES modules. `ParseModule` makes `import`/`export` legal
and makes a duplicate top-level `function`, `const` or `class` a `VarRedeclaration`. The same
duplicate in script mode was silently accepted in the probe. The tool must not fall back to
`ParseScript`.

**The first error is not always where the edit is.** The half-function probe reported an `export`
almost six hundred lines later, because that is where the grammar finally became impossible. The
parser is right; the message is just not local. The token stream fixes that: `ParserOptions.OnToken`
gives the punctuator tokens seen before the failure, so a stack of `{`, `(` and `[` can report the
unclosed openers. For the same probe it said:

```
Unclosed '{' opened at line 351, column 20
Unclosed '{' opened at line 353, column 10
```

Those are the duplicated `function` and `if` blocks. Templates and regex literals arrive as their
own token kinds, so the stack is not confused by braces inside them. This hint is worth more than
the parser's line number for exactly the botched-edit case.

A parser cannot catch a spare copy that is still syntactically valid: duplicated statements, a
second `var`, duplicate object properties, or a complete function duplicated inside a new function.
That is a linter or a repeated-block heuristic, not a syntax check, and it is out of scope here.
The tool should say it validates syntax, not intent.

Grammar errors throw at the first failure. `Tolerant = true` and `ParseErrorCollector` only collect
recoverable tokenizer errors (`RaiseRecoverable`), not parser errors. One error per call is fine:
the assistant fixes it, calls the tool again, and the existing step and tool-call limits still
bound the loop.

### Handling the page template

A page is a C# template, not a raw `.js` file. Three approaches were considered.

1. **Render the page and validate the output.** Rejected. Rendering executes page code — storage
   writes, fetches, per-user branches — just to check syntax, and the output cannot be mapped back
   through arbitrary C# output.
2. **Wrap `<%` and `%>` in JavaScript comments.** Rejected. Columns move, `*/` inside a code block
   ends the comment early, a block already inside a comment nests illegally, and a block inside a
   string or regex changes meaning. It can also hide the following code behind a comment.
3. **Build a position-preserving JavaScript skeleton.** Chosen. Use the existing
   `TemplateTokenizer` to walk the template:
   - text tokens are copied verbatim;
   - code and declaration tokens are replaced with spaces, preserving every newline;
   - write and raw-write tokens are replaced with `x` on the first line and spaces thereafter, so
     an expression position stays valid.

   Newlines and columns are preserved, so Acornima's line and column are already page line and
   column. This is the fallback for a `.js` page that still uses `<%= … %>`; a page with no
   template tokens is parsed raw and needs no skeleton at all. A write token spliced into a token
   rather than an expression position (for example inside a numeric literal) is best-effort; the
   common script page has no template tokens at all.

The skeleton helper belongs in `Core` beside the tokenizer, because `Core` already owns the
template language and the tokenizer is internal there. It should return a string plus any
tokenization diagnostics, not a parser type. Acornima stays in `Runtime`; `Core` has no package
references and must not grow one.

### Tool surface

#### Not attached to save (decided)

Validation is an explicit tool call, not a hidden part of `write_page`, `edit_page` or
`PageEditor.SaveAsync`. Save is the compiler's path: it compiles the C# template and returns
Roslyn diagnostics. Adding a JavaScript parser to every save would:

- add latency and a second failure mode to a path that currently has one compiler;
- couple the page editor to a parser it may not need;
- tempt a save to be refused on JavaScript syntax, when the current contract deliberately stores a
  page with its diagnostics so the editor can show them.

The assistant can validate candidate `content` before writing it, or a stored page by `path`, and
decide what to do. If automatic validation is ever wanted later, it is an editor/tooling feature
with its own decision, not a side effect of save.

#### Name and scope

Most existing tools are `verb_noun`: `list_pages`, `read_page`, `edit_page`, `write_page`,
`delete_page`, `search_pages` (and `shell`, which is the exception). The recommendation is
**`validate_script`**:

- it names the unit it validates, rather than pretending to validate a whole page;
- it does not overpromise CSS, JSON or HTML support;
- it fits the existing naming pattern.

`check_script` is the main alternative. `validate_javascript` is too long and repeats the obvious
inside the tool description. `validate_page` is misleading while JavaScript is the only language,
and `validate_file` suggests the page store has file types beyond the extension signal.

That leads to the second question: is this a JavaScript validator or a file validator with
JavaScript as one of its validators?

- **JavaScript validator now (recommended).** A `JavaScriptValidator` in `Runtime`, a
  `validate_script` tool in `Web`, and no registry. This follows the repo's simplicity rule: there
  is one language, one parser and one consumer. The result type can be the existing
  `PageDiagnostic`, which is already language-neutral enough for a later CSS or JSON validator.
- **File validator now.** A `PageValidator`/`IPageValidator` registry keyed by the content-type
  plan's kind, a `validate_page` tool, and JavaScript as one implementation. It is the right shape
  when the second language is real, but it adds an abstraction with one implementer and makes the
  tool name promise more than it delivers. The content-type plan's `Kind` makes the later change
  mechanical: the tool can keep its implementation and add a dispatcher when a second validator
  exists.

The tool's description can still say "validate the JavaScript a page serves" so the model does not
assume it checks CSS or HTML.

#### Arguments and result

Suggested shape:

- `path` and `content`, both optional, at least one required.
  - `path` reads the stored page, validates its script portion, and is refused for protected paths,
    exactly as `read_page` is.
  - `content` validates a candidate before `write_page`, so the assistant can fix a bad script
    without storing it first.
  - If both are present, `content` wins and `path` is used for the label and the protected-path
    check. The handler returns a clear failure when neither is present; the JSON schema cannot
    express "exactly one of".
- `NeedsApproval = false`: the tool reads the store or a string, never writes and never executes
  the script.
- A syntactically invalid script is a **successful tool call** with `valid: false` and a diagnostic
  list. `IsSuccess = false` is reserved for bad arguments, a missing page, a protected path and an
  internal parser failure. The assistant needs the diagnostics, not a tool error it may treat as a
  transport problem.
- Return, per diagnostic: the stable code, severity, one-based line, one-based column and message.
  Reuse the existing `DiagnosticData` shape in `PageToolExecutor` and `PageDiagnostic` in `Core` so
  the JavaScript diagnostics look like compile diagnostics. The Acornima code
  (`UnexpectedToken`, `VarRedeclaration`, ...) should be in the message or a separate field; the
  human-readable description is V8's and is not a stable API.
- Add unclosed-opener hints as warning diagnostics (for example `SCRIPT002: unclosed '{' opened at
  line 351, column 20`) or a separate `hints` array. Either way they are derived from the token
  stream, not the parser's error message. Parser errors can use `SCRIPT001`; the exact prefix is a
  detail for implementation.
- Consider two or three lines of source context around each error. `read_page` can fetch them, but
  the tool already has the source, and saving a round trip matters when the assistant is iterating.

#### Engine and dependency

Acornima 1.9.1, pinned in `Directory.Packages.props`, referenced by `uk.osric.asp.Runtime`. It is
BSD-3-Clause, so its notice must be carried in the repository's REUSE/licence files; `make
license-check` will catch an omission. `Core` stays package-free; `Web` calls the validator through
`Runtime`. Adding a package to `Runtime` changes the line in
[docs/architecture.md](../architecture.md) that says `Runtime` references "Core, Sdk and Roslyn, and
nothing else"; update that sentence when this lands.

Acornima itself does not change `RuntimeId`: it is host-side tooling, not part of the page contract
or the generated image. The skeleton helper is a `Core` change, though, and `RuntimeId` includes
`Core`'s module version id, so expect every stored image to recompile on its next request once
(DEC-26). That is the normal, one-time cost of a `Core` change and should be part of the change's
reasoning, not a surprise.

Alternatives considered:

- **ClearScript/V8.** Exact engine parity and the option to execute later, but a prebuilt native V8
  library (about 58 MB on disk), no musl build (the Linux arm64 asset is glibc-only), a larger
  dependency chain and a much larger attack surface. A pure parser is enough for syntax and does
  not need a process boundary. If the site ever executes model-written JavaScript, that is a
  different job with a sandbox.
- **Jint.** An interpreter built on Acornima; more weight and an execution surface the validator
  does not need.
- **A hand-written lexer or checker.** No dependency, but it is not a grammar. It can find
  unterminated literals and unbalanced delimiters, and will produce false positives or miss
  ordinary syntax errors. Not worth shipping as "validation".
- **Render then validate, comment wrapping, or validation on save.** Rejected above.

## Prompt and assistant loop

Add to the `## Scripts` section of `PageAuthorPrompt`:

> After writing or editing a page that serves JavaScript, call `validate_script` with the page path
> or with the complete content before saving. Treat `valid: false` as unfinished work: fix the
> diagnostics and validate again before reporting done.

Bump `PageAuthorPrompt.Version` to `page-author-v6` and cover the new sentence with a
`PageAuthorPromptTests` assertion. The extension plan's prompt change and this one should land
together if both are accepted, or the version bumps twice if they do not.

## Tests

`Core` skeleton tests:

- Text-only source is unchanged and its positions are unchanged.
- Code and declaration blocks are blanked without changing line count or later columns.
- A write token inside a JavaScript string is replaced with a valid expression and does not create
  a false error.
- A malformed template returns the tokenizer's error rather than a parser crash.
- The skeleton's line and column for a token after several template blocks match the page.

`Runtime` validator tests, table-driven:

- valid 986-line module;
- half a function inserted, with the unclosed-opener hints pointing at the insertion;
- balanced interior containing `return` → `IllegalReturn`;
- duplicate complete top-level function → `VarRedeclaration`;
- the same duplicate parsed as a script is accepted, pinning module mode;
- extra `}`;
- missing comma in an object literal;
- unterminated template literal;
- duplicate `var` and duplicated valid statements accepted, pinning the documented limitation;
- a protected path and a missing page are refused by the tool layer, not the parser.

`Web` tool tests:

- the tool appears in `PageToolExecutor.Definitions` with the expected schema and no approval;
- `path` reads a stored page; `content` validates without writing; both together use `content`;
- invalid JavaScript returns `IsSuccess = true`, `valid: false` and positioned diagnostics;
- a protected path is refused before reading storage;
- no approval record is created;
- the prompt names the tool and the version is bumped.

No browser test is needed: this is a unit-level parser and the existing page-script browser test
already proves that a stored script runs when the host serves it. Performance is measured, not
asserted with a timing test; a large-page case should complete without a timeout under the existing
tool timeout.

## Acceptance criteria

- `validate_script` on a valid `.js` page returns `valid: true`.
- A half-function edit returns `valid: false` with a positioned error and an unclosed-opener hint
  that points at the duplicated block, not only at the later token where parsing failed.
- A duplicated complete top-level function returns a `VarRedeclaration` diagnostic.
- A syntactically valid duplicate block returns `valid: true`; the tool description says it checks
  syntax, not intent.
- Protected paths are refused; nothing is written; no approval is requested.
- `write_page` and `PageEditor.SaveAsync` are unchanged; validation is never implicit.
- `PageAuthorPrompt` tells the assistant to validate, and `make check` passes.

## Open questions

- The name: `validate_script` versus `check_script`; and whether the user would rather have
  `validate_page` with a language field from the start.
- The scope: JavaScript only now, or a validator registry today. The recommendation is JavaScript
  only, with `PageDiagnostic` as the shared result so a later CSS/JSON validator does not need a new
  tool shape.
- Whether the result includes source context and how many lines; whether unclosed-opener hints are
  warnings in `diagnostics` or a separate `hints` array.
- Whether `content` alone is enough (no path) and whether a path whose extension is not `.js`/`.mjs`
  should be refused or still validated as JavaScript.
- Whether import specifiers should be resolved against stored pages. Syntax validation does not
  need it; a missing import is a runtime failure, not a parse failure, and checking it would add a
  second meaning to "valid".
- Whether the skeleton helper should be a public `Core` API or a public tokenizer. The plan
  recommends the former: expose the result, not the tokenizer.
- Whether the editor should ever show these diagnostics. If it does, it reuses the validator; it
  still does not run it on save.

## Relationship to other work

- [The content-type plan](../plans/page-content-types.md) turns the extension into a source-language
  signal and makes the common script page pure JavaScript.
- [IDEA-001](IDEA-001.md), a proper editor, could use the same validator for a "check" action, but
  not as part of save.
- [SEC-03](SEC-03.md) is not a prerequisite: Acornima parses and does not execute, so there is no
  new process boundary to argue about. The process-boundary question returns only if the site ever
  executes the script.
- DEC-15 requires the dependency justification and pin; this idea is the justification.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Checking the JavaScript the assistant writes

Status: **implemented 2026-11-08**. Written 2026-11-08 as the implementation plan for
[IDEA-021](../../jobs/IDEA-021.md), with that job's open questions answered. The decisions are DEC-145
and DEC-146, and the behaviour is recorded in [architecture](../../architecture.md). The backlog
follow-up it promised — Language Server Protocol support for the human editor — is recorded as
[WHIM-01](../../jobs/WHIMS.md), not built here.

Two details differ from the plan as written and are noted here rather than left to be discovered:
the unclosed-opener stack also tracks a template literal's `${`, so a substitution's `}` cannot
close a real brace outside the literal and hide the hint; and page code cannot reach Acornima because
the API policy is default-deny, rather than because the assembly is absent from `PageReferenceSet`.

## The short version

- Two read-only assistant tools share one validator:
  - **`check_script`** takes a stored page's **`path`** and parses the page's literal text as an
    ECMAScript module.
  - **`check_script_text`** takes a **complete page template** as **`content`**, parses it the same
    way and writes nothing.
- Both return `valid` plus a flat list of **`{ line, column, message }`** diagnostics. There is no
  source excerpt, no severity field and no separate hint array.
- Invalid JavaScript is a **successful tool call** with `valid: false`. Only bad arguments, a missing
  page, a protected path or an unexpected parser fault make the call fail.
- JavaScript only. There is no validator registry, no CSS or JSON support, and no content-type
  lookup: the page's key is not consulted, so checking a non-JavaScript page generally produces
  syntax errors. That is the agreed behaviour, not an accident.
- Imports are parsed but never resolved. A missing import target is a runtime problem, not a syntax
  problem.
- Syntax only. A duplicated complete function, duplicated statements or a second `var` can be
  syntactically valid and are reported as valid.
- Not attached to `write_page`, `edit_page`, `PageEditor.SaveAsync` or the editor. The assistant calls
  them explicitly; the human editor's integration is a whim.
- The parser is [Acornima](https://github.com/adams85/acornima) 1.9.1 in `Runtime`; the
  position-preserving template skeleton is a public helper in `Core`.

## Goals

- Give the assistant a way to find a botched JavaScript edit before the owner's browser finds it.
- Let the assistant check a complete page **before** `write_page`, so a bad script is not stored and
  a `RequireApproval` write is not spent on it.
- Let the assistant check a stored page **after** `edit_page`, where a replacement fragment alone
  would be misleading.
- Report positions in the page the author typed, not in generated C# or rendered output.
- Keep both tools read-only: they read one page or one supplied string and write nothing.
- Keep the page-saving contract unchanged: save still compiles the C# template and stores its
  diagnostics; JavaScript validation is never a save gate.
- Keep each result small enough to fit an assistant turn: one parser error per call, a bounded number
  of unclosed-opener hints, and no source context.

## Non-goals

- Automatic validation on save, or a refusal to store a page with invalid JavaScript.
- Validating CSS, JSON, HTML or any language other than JavaScript.
- Resolving `import` specifiers against stored pages, or checking that an imported page exists.
- Type checking, linting, repeated-block detection, duplicate-property detection or any other
  semantic analysis beyond the grammar.
- Reporting C# template or Roslyn diagnostics. `read_page` already returns the stored compile
  diagnostics; both tools only add the tokenizer's `TEMPLATE001` when they cannot build a skeleton.
- Executing the script, or introducing a process boundary. Acornima parses; it does not run code.
- Showing these diagnostics in the human editor. That is [WHIM-01](../../jobs/WHIMS.md), and if it is
  promoted it reuses this validator rather than adding a second one.
- One tool with both `path` and `content`. Two tools keep each schema honest, and the names say which
  source is being checked.
- Checking an `edit_page` replacement fragment on its own. A fragment can be valid only inside its
  surrounding function; `check_script_text` takes a complete page template, and `check_script` checks
  the stored page after the edit.
- `check_file`. The store holds pages, not files, and the check is JavaScript-only.

## What exists today

- [PageToolExecutor](../../../src/uk.osric.asp.Web/Assistant/PageToolExecutor.cs)
  owns one `ToolSpec` table. The model's schema and the execution-time validation are derived from
  the same table, so each tool is one entry and one `switch` arm.
- `read_page` already shows the shape a read-only tool uses: `PagePathPolicy.Validate(path,
  write: false)`, then `PageEditor.GetAsync`, then a bounded `Success`/`Failure` envelope.
- [PageDiagnostic](../../../src/uk.osric.asp.Core/Compilation/PageDiagnostic.cs)
  is the existing positioned, severity-tagged diagnostic value. `PageDiagnostic.SortedForDisplay`
  orders by line and column.
- [TemplateTokenizer](../../../src/uk.osric.asp.Core/Compilation/TemplateTokenizer.cs)
  is internal to `Core`, splits a template into text, code, declaration and write tokens, and reports
  an unterminated block as `TEMPLATE001`. `Token` carries the content's start position but not
  absolute offsets.
- [PageContentTypes](../../../src/uk.osric.asp.Core/Pages/PageContentTypes.cs)
  now maps a key's extension to a media type only. The unused source-language kind was dropped, so
  there is no `Kind` signal to consult even if a tool wanted one.
- `Runtime` exposes `PageCompiler` publicly because `Web` calls it; its remaining helpers are
  internal with `InternalsVisibleTo` for the test assemblies. A public validator is the same pattern.
- `RunProcessor.ExecuteToolAsync` already turns any exception from a tool into a bounded
  `IsSuccess = false` result and logs it. The validator does not need a catch-all of its own.

## Decisions

### D1 — The tools are `check_script` and `check_script_text`

`check_script` keeps the name for the stored-page tool: it is short, names the unit it checks, fits
the existing `verb_noun` tool names and does not promise CSS or HTML. `check_script_text` names the
source it checks: a complete page template supplied in the call.

The pair says which source is being checked. `check_file` is rejected because the store holds pages,
not files, and the check is JavaScript-only; it would also invite the model to pass a filesystem path
that no tool can read. The idea's other alternative, `validate_script`, is not used.

### D2 — Each tool takes exactly one source

- **`check_script`** requires `path`, reads the stored page and parses its literal text.
- **`check_script_text`** requires `content`, parses a complete page template and never reads or
  writes the page store.

They are two tools rather than one tool with both optional arguments because the current
`ToolArguments` schema cannot express "exactly one of" and because the workflows differ: check the
complete content before `write_page`, check the stored page after `edit_page`. Two honest schemas are
simpler than a handler that has to repair what the schema could not say.

`check_script_text` must receive a **complete page template**, not an `edit_page` replacement
fragment. A fragment can be invalid in isolation — a top-level `return`, half a statement — even when
the edit is correct. Both descriptions and the prompt say this. The text tool does not need the
page path policy because it never touches a path; it is still bounded by `MaxToolArgumentChars` and
the run's tool-call budget.

The cost is accepted: an `edit_page` is stored before `check_script` sees it. That is already true of
every JavaScript save today, and the assistant can fix it with `edit_page` using the revision hash it
received. The text tool gives the assistant the option to avoid the bad write in the first place.
Validation remains an explicit call, never a side effect of save.

### D3 — JavaScript only, and the key's extension is not consulted

The validator takes a string and parses it as JavaScript. It does not ask `PageContentTypes` what the
page serves. A `.js` page that sets an HTML content type is still checked as JavaScript, and markup
in a page whose key is not JavaScript is reported as syntax errors. Both tool descriptions say so,
and a test pins it.

This is the simplest useful contract, and the content-type map is a default rather than a
constraint. It also avoids adding a second meaning to "valid" before a second language exists.

### D4 — Parse modules, never scripts

Every assistant-written script page is an ES module by convention: the prompt already requires
`import`/`export` and says to end a side-effect-only script with `export {};`. `ParseModule` makes
`import`/`export` legal, makes top-level `await` legal, and makes a duplicate top-level `function`,
`const` or `class` a `VarRedeclaration`. Parsing as a script would silently accept that duplicate.

The limitation is deliberate: a classic script with a top-level `return` or `with` is reported as
invalid, because it is not a module. Both tool descriptions say "ES module".

### D5 — The result is `valid` plus `{ line, column, message }`

Both tools return the same diagnostic shape. Per diagnostic:

```json
{ "line": 351, "column": 20, "message": "SCRIPT002: unclosed '{'." }
```

There is no `source`, no context lines, no `severity` and no separate `code` field. The stable code is
the message prefix, the same convention `TEMPLATE001` already uses. The assistant can call
`read_page` with a line range when it wants to see the code.

`valid` is true when no diagnostic has `PageDiagnosticSeverity.Error`. A page that parses returns
`valid: true` and an empty list. A warning-only result is possible in principle; the current hints
always accompany an error.

`check_script` includes the path it read. `check_script_text` returns the same `valid` and
`diagnostics` fields without a path, because it has no path. The envelope is the existing one:

```json
{
  "ok": true,
  "data": {
    "path": "/app.js",
    "valid": false,
    "diagnostics": [
      { "line": 351, "column": 20, "message": "SCRIPT002: unclosed '{'." },
      { "line": 986, "column": 1, "message": "SCRIPT001: UnexpectedToken: Unexpected token 'export'." }
    ]
  },
  "error": null
}
```

### D6 — Unclosed openers are diagnostics in the same list

The parser's first error is not always near the edit. A half-inserted function can leave the grammar
valid for hundreds of lines and fail at the next `export`. The token stream fixes the common case:
`ParserOptions.OnToken` sees the punctuator tokens before the failure, so a stack of `{`, `(`, `[`
can report every opener that never closed.

The hint is a `PageDiagnostic` of severity `Warning`, positioned at the opener:

```text
SCRIPT002: unclosed '{'.
```

It is sorted with the parser error by line and column, so the opener — the useful line — usually
comes first. Hints are capped at 20, earliest first, so a pathological page cannot turn the result
into a failure of the envelope's size limit. The parser error is always included.

Templates and regular-expression literals arrive as their own token kinds, so braces inside them do
not enter the stack. An unmatched closer is ignored; the parser already reports it.

### D7 — Imports are not resolved

`import "./missing.js";` parses. Whether the target exists is a runtime question and checking it
would make `valid` mean two things. The validator does not read the page store beyond the page it was
given, so it cannot be used as an accidental existence check.

### D8 — One parser error per call

Grammar errors throw at the first failure. Acornima's `Tolerant` mode and `ParseErrorCollector` only
collect recoverable tokenizer errors, not parser errors. One error per call is enough: the assistant
fixes it, calls again, and the existing run and tool-call limits bound the loop.

### D9 — The skeleton is a public `Core` helper

A script page is a template, not a raw file. The validator must parse the literal text with template
constructs replaced, without rendering the page and without executing any page code.

`src/uk.osric.asp.Core/Compilation/TemplateScriptSkeleton.cs` returns a `ScriptSkeletonResult` of
the transformed source and any tokenizer diagnostics. `Core` owns
the template language, so it owns this transformation; the caller gets a string and diagnostics, not
a tokenizer or a parser type. `Core` stays package-free.

The transformation mutates a `char[]` copy of the template in place, preserving its length and every
newline:

- a text token is left alone;
- a code or declaration token has its whole construct — opener, content and `%>` — replaced with
  spaces, preserving `\r` and `\n`;
- a write or raw-write token has its construct replaced with `0` followed by spaces, preserving
  newlines, so an expression position stays valid.

Positions are therefore page positions: the skeleton has the same line breaks and the same character
count before every later token as the template. A page with no template construct is returned
unchanged. An unterminated block returns `TEMPLATE001` and no usable source; the caller does not
parse it.

Replacing a construct with spaces is deliberate: it keeps token separation (`foo<% x %>bar` becomes
`foo      bar`, not `foobar`). A write token spliced into a position where no JavaScript expression
is legal — inside a numeric literal, after a `.` — is best-effort, and the common script page has no
template tokens at all.

### D10 — Acornima in `Runtime`

Acornima 1.9.1 is a pure-managed ECMAScript parser, not an interpreter. It passes Test262, reports
V8's messages with a stable code, tracks lexical scopes and guards against stack overflow. It has no
native asset and does not execute anything, so SEC-03's process-boundary question does not return.

It is pinned in `Directory.Packages.props` and referenced only by `Runtime`. `Web` calls the public
validator, and generated page code cannot reach Acornima because `PageReferenceSet` builds the page's
references from the SDK and the platform assemblies, neither of which contains it.

Alternatives were considered in the job and remain rejected: ClearScript/V8 brings a native library,
no musl build and an execution surface; Jint is an interpreter built on Acornima; a hand-written
checker is not a grammar.

The probe against a 986-line / 13.5 KB module was about 40 ms cold and under 10 ms warm. A parser per
call is enough; there is no cache. Each tool runs inside the existing 30-second tool timeout, and the
result is bounded by `MaxToolResultChars` with hints capped at 20. Performance is measured, not
asserted by a timing test.

### D11 — Validation is not a save step, and the editor is not in this change

`write_page`, `edit_page` and `PageEditor.SaveAsync` are unchanged. Save compiles the C# template and
returns Roslyn diagnostics; adding a JavaScript parser there would add latency, a second failure mode
and a temptation to refuse a save the current contract deliberately stores.

The human editor does not show these diagnostics. If it ever does, it reuses `JavaScriptValidator`
for an explicit check and still does not run it on save. Language Server Protocol support for the
editor is [WHIM-01](../../jobs/WHIMS.md).

### D12 — Invalid script is a successful call; failures are failures

`IsSuccess = true` whenever the page was read or the content supplied, and the parser ran, even when
`valid` is false. The assistant needs the diagnostics, not a transport-looking error.

`IsSuccess = false` is reserved for:

- an unknown tool or an argument that does not match the schema;
- an unsupported, reserved or protected path;
- a missing page;
- an unexpected exception from the parser or the store. `RunProcessor` already converts that to a
  bounded tool failure and logs it.

### Decisions to record

When implemented, record two decisions:

- **DEC-145 — the tool contract.** `check_script` and `check_script_text` are read-only assistant
  tools sharing one ES-module validator. `check_script` takes a stored page's path; `check_script_text`
  takes a complete page template. Both return `valid` plus `{ line, column, message }`. They are
  JavaScript only, do not consult the content-type map, do not resolve imports and are not a save
  gate.
- **DEC-146 — the mechanism.** Acornima 1.9.1 is referenced by `Runtime`; `Core` exposes a
  position-preserving `TemplateScriptSkeleton`; parser errors are `SCRIPT001` and unclosed-opener
  hints are `SCRIPT002`. The `Core` change recompiles every stored image once (DEC-26).

## The change

### Pipeline

`PageToolExecutor` adds two thin handlers over one private check:

```text
CheckScriptAsync(path):
  validate path with write: false
  read PageTemplate with PageEditor.GetAsync
  return CheckTemplate(page.Template, path)

CheckScriptTextAsync(content):
  return CheckTemplate(content, path: null)

CheckTemplate(template, path):
  build the skeleton with TemplateScriptSkeleton.Create
  parse the skeleton with JavaScriptValidator.Validate
  map the diagnostics to { line, column, message }
  return valid and the projection
```

Each handler builds its own envelope, so `path` appears only for `check_script`. A skeleton error
short-circuits before the parser. It is reported in the same list, so a page with an unterminated `<%`
block produces `TEMPLATE001` rather than a parser crash.

### `Core`: `TemplateScriptSkeleton`

```csharp
namespace uk.osric.asp.Core.Compilation;

/// <summary>A page's literal text, with template constructs replaced so a JavaScript parser can
/// read it without changing any position.</summary>
public sealed record ScriptSkeletonResult(
    string Source,
    IReadOnlyList<PageDiagnostic> Diagnostics) {

    public bool IsSuccess => Diagnostics.Count == 0;
}

public static class TemplateScriptSkeleton {
    public static ScriptSkeletonResult Create(string template);
}
```

`Create` throws `ArgumentNullException` for null, as every public entry point does. It returns the
tokenizer's `TEMPLATE001` diagnostics and an empty `Source` when the template cannot be tokenized.

The implementation walks the token list with a cursor. It derives each construct's full extent from
the cursor, the opener length for its `TokenType`, the token's content length and the two-character
closer. That keeps the tokenizer's content lossless and avoids adding offsets to the internal
`Token` type or changing its equality tests.

### `Runtime`: `JavaScriptValidator`

```csharp
namespace uk.osric.asp.Runtime;

public static class JavaScriptValidator {
    /// <summary>Parses a script as an ECMAScript module and returns positioned syntax diagnostics.</summary>
    public static IReadOnlyList<PageDiagnostic> Validate(string script);
}
```

- `ArgumentNullException.ThrowIfNull(script)`.
- A new `Parser` per call; a parser is cheap and is not shared across calls.
- `ParserOptions { EcmaVersion = EcmaVersion.Latest, Tolerant = false, OnToken = … }`.
- `Parser.ParseModule(script)`; the returned module is discarded.
- Catch `ParseErrorException`, not `Exception`. `Error.Code` and `Error.Description` become
  `SCRIPT001: {Code}: {Description}`; `LineNumber` and `Column + 1` become the position when the
  error carries a defined position. If it does not, the position is `null`.
- Collect punctuator tokens in `OnToken`. `{`, `(` and `[` push their `Location.Start`; a matching
  closer pops only when it is the top of the stack. If the parse failed, emit at most 20
  `SCRIPT002` warnings, earliest first.
- Return `PageDiagnostic.SortedForDisplay` of the hints and the parser error.

Acornima's regex validation keeps its default mode and five-second timeout. A pathological regular
expression is bounded well inside the 30-second tool timeout; anything unexpected is left to
`RunProcessor`'s tool failure path rather than hidden by a catch-all.

### `Web`: the tools

Two `ToolSpec` entries, both with `NeedsApproval = false`:

```csharp
new("check_script",
    "Check the JavaScript a stored page serves. Reads the page by path and parses its literal text "
    + "as an ES module. The key's extension is not consulted: a page that is not JavaScript will "
    + "generally produce syntax errors. Checks syntax only; imports are not resolved and nothing "
    + "is executed.",
    false,
    [new("path", ToolArgumentType.String, true)]),
new("check_script_text",
    "Check the JavaScript in a complete page template supplied as content, without storing it. "
    + "Parses the literal text as an ES module. Pass the whole page, not an edit_page replacement "
    + "fragment: a fragment can be invalid alone even when the edit is correct. Checks syntax only; "
    + "imports are not resolved and nothing is executed.",
    false,
    [new("content", ToolArgumentType.String, true)]),
```

Two `switch` arms calling `CheckScriptAsync` and `CheckScriptTextAsync`. They share one `CheckTemplate`
helper and one diagnostic projection, rather than the richer `DiagnosticData` used by `read_page`,
because the agreed result shape has only three fields per diagnostic:

```csharp
private static object[] ScriptDiagnostics(IReadOnlyList<PageDiagnostic> diagnostics) =>
    [.. diagnostics.Select(diagnostic => new {
        line = diagnostic.Position?.Line,
        column = diagnostic.Position?.Column,
        message = diagnostic.Message,
    })];
```

`valid` is computed before projection, so it cannot disagree with the list. `check_script` returns
`{ path, valid, diagnostics }`; `check_script_text` returns `{ valid, diagnostics }`. The summaries are
`Checked /app.js: valid` and `Checked supplied text: 3 problems`.

### Prompt

Replace the `## Scripts` guidance in
[PageAuthorPrompt](../../../src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs) with:

> Before `write_page`, call `check_script_text` with the complete content. After `edit_page`, call
> `check_script` with the page's path. Never check a replacement fragment on its own: a fragment can
> be invalid alone even when the edit is correct. Both parse the page's literal text as an ES module
> and return `valid` with a list of `line`, `column` and `message`. Treat `valid: false` as unfinished
> work: use the positions, especially any `SCRIPT002` unclosed-opener hint, to find and fix the edit,
> then check again before reporting done. They check syntax, not intent: a duplicated block that still
> parses is not reported.

Bump `PageAuthorPrompt.Version` from `page-author-v7` to `page-author-v8`. Cover the new guidance in
`PageAuthorPromptTests`.

## Files

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Core/Compilation/ScriptSkeletonResult.cs` | New result record |
| `src/uk.osric.asp.Core/Compilation/TemplateScriptSkeleton.cs` | New public skeleton helper |
| `test/uk.osric.asp.Core.Tests/TemplateScriptSkeletonTests.cs` | New Core tests |
| `src/uk.osric.asp.Runtime/JavaScriptValidator.cs` | New public validator |
| `test/uk.osric.asp.Runtime.Tests/JavaScriptValidatorTests.cs` | New Runtime tests |
| `src/uk.osric.asp.Runtime/uk.osric.asp.Runtime.csproj` | Add the Acornima package reference |
| `Directory.Packages.props` | Pin Acornima 1.9.1 |
| `src/uk.osric.asp.Web/Assistant/PageToolExecutor.cs` | Add the two tool specs, switch arms, handlers and the class summary's verb list |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | Add the sentence and bump the version |
| `src/uk.osric.asp.Web/uk.osric.asp.Web.csproj` | Copy `THIRD-PARTY-NOTICES.md` into the publish output |
| `test/uk.osric.asp.Web.Tests/PageToolExecutorTests.cs` | Tool behaviour tests |
| `test/uk.osric.asp.Web.Tests/PageAuthorPromptTests.cs` | Prompt assertion |
| `docs/architecture.md` | `Runtime`'s dependency sentence and the assistant tool list |
| `docs/decisions.md` | Record DEC-145 and DEC-146 when implemented |
| `LICENSES/BSD-3-Clause.txt` | New licence text for the pinned package |
| `THIRD-PARTY-NOTICES.md` | New Acornima copyright and licence notice |
| `docs/jobs/WHIMS.md`, `docs/jobs/README.md`, `docs/jobs/IDEA-021.md` | Backlog follow-up, done alongside this plan |

No `REUSE.toml` entry is needed if `THIRD-PARTY-NOTICES.md` carries its own SPDX header. The licence
file itself is a licence text and does not take a header.

## Phases

Each phase leaves the build green and is testable on its own.

1. **Core skeleton.** Write the failing `TemplateScriptSkeletonTests`, then the helper. No package
   yet, so this phase is a plain `Core` change.
2. **Runtime validator.** Pin Acornima, reference it from `Runtime`, add the licence text and notice,
   copy the notice into the publish output, write the table-driven `JavaScriptValidatorTests`, then
   the validator.
3. **Web tools and prompt.** Add both tool entries, handlers and tests; add the prompt guidance and
   version bump.
4. **Docs and gate.** Update `architecture.md`, record DEC-145 and DEC-146 in `decisions.md`, run
   `make format`, `make license-check`, the three focused suites and `make check`.

## Tests

### Core skeleton

- Text-only source is returned unchanged, character for character.
- Code and declaration blocks are blanked without changing the line count or the columns after them.
- A write token inside a JavaScript string becomes a valid expression and does not create a false
  error.
- A write token spanning lines preserves the newlines.
- `\r\n`, `\n` and a lone `\r` are all preserved.
- A malformed template returns `TEMPLATE001` and no usable source rather than throwing.
- A token after several template blocks keeps its page line and column.

### Runtime validator

Table-driven with `TestCaseSource`, because the cases differ only in source and expected result:

- a valid 986-line module;
- a half-function insertion, with the `SCRIPT002` hints pointing at the inserted openers;
- a balanced function interior containing `return`, reported as `IllegalReturn`;
- a duplicated complete top-level function, reported as `VarRedeclaration`; script mode would
  accept it, so this also pins module mode;
- an extra `}`;
- a missing comma in an object literal;
- an unterminated template literal;
- duplicate `var` accepted;
- duplicated valid statements accepted;
- `import "./missing.js";` accepted, pinning that imports are not resolved;
- an empty script accepted;
- a template literal or regular expression containing braces producing no hint on a valid parse;
- the line and column are one-based and match the source;
- the message begins `SCRIPT001: ` and carries Acornima's code and description.

### Web tools

- `check_script` is in `PageToolExecutor.Definitions`, has a required `path`, no `content` and
  `NeedsApproval == false`.
- `check_script_text` is in `PageToolExecutor.Definitions`, has a required `content`, no `path` and
  `NeedsApproval == false`.
- A valid `.js` page returns `IsSuccess = true`, `valid: true` and an empty diagnostics list.
- An invalid `.js` page returns `IsSuccess = true`, `valid: false` and a positioned diagnostic.
- A non-JavaScript page returns `IsSuccess = true`, `valid: false` and syntax errors, pinning the
  agreed behaviour.
- A page with an HTML key whose content is valid JavaScript returns `valid: true`, pinning that the
  key's extension is not consulted.
- A page with an unterminated template block returns `valid: false` with `TEMPLATE001`.
- A protected path is refused before `FakePageStore.ReadCount` moves.
- A missing page returns `IsSuccess = false`.
- `check_script_text` on valid complete content returns `IsSuccess = true`, `valid: true`, and leaves
  `FakePageStore.ReadCount`, `WriteCount` and `ImageWriteCount` at zero.
- `check_script_text` on invalid content returns `IsSuccess = true`, `valid: false` and a positioned
  diagnostic.
- `check_script_text` honours template blocks: a complete template with `<%= … %>` and `<% … %>`
  checks the literal JavaScript around them.
- A lone `return 1;` passed as text is reported as `IllegalReturn`, pinning the description's warning
  that the argument must be a complete page, not a fragment.
- Both tools write nothing: `WriteCount` and `ImageWriteCount` stay zero.
- `PageAuthorPrompt` contains `check_script`, `check_script_text` and `valid: false`, and its version
  is `page-author-v8`.

### Existing suites

`write_page`, `edit_page` and `PageEditor.SaveAsync` tests are unchanged. No browser test is needed:
the parser is a unit-level value and the existing page-script browser test already proves a stored
script runs when the host serves it.

## Acceptance criteria

- `check_script` on a valid `.js` page returns `valid: true`.
- `check_script_text` on valid complete content returns `valid: true` without reading or writing the
  store.
- A half-function edit returns `valid: false` with a positioned parser error and at least one
  `SCRIPT002` hint at the duplicated opener, not only at the later token where parsing failed.
- A duplicated complete top-level function returns a `VarRedeclaration` diagnostic.
- A syntactically valid duplicate block returns `valid: true`; the tool descriptions say they check
  syntax, not intent.
- A non-JavaScript page returns `valid: false` with syntax errors and is not a tool failure.
- Imports are parsed but missing import targets are not reported.
- Protected paths are refused; a missing page fails; nothing is written; no approval is requested.
- `write_page` and `PageEditor.SaveAsync` are unchanged; validation is never implicit.
- `PageAuthorPrompt` names `check_script` and `check_script_text`, and the version is bumped.
- `make check` passes.

## Risks and limitations

- **Post-edit validation.** `check_script_text` prevents a bad whole-page write, but an `edit_page`
  is still stored before `check_script` sees it. The page contract already permits that; the prompt
  makes the stored check the next step, and `edit_page` fixes it.
- **Text duplication.** `check_script_text` sends the whole page again in the tool call, which costs
  tokens. `check_script` reads the stored page instead; the prompt uses the text tool for whole-page
  writes and the path tool after an edit.
- **Argument size.** `check_script_text` is capped by `MaxToolArgumentChars`; a page larger than that
  can only be checked by path. Pages written by the assistant are within the same cap.
- **Fragment trap.** `check_script_text` requires a complete page; a fragment can be invalid alone.
  The description and prompt say so, but the model can still misuse it. The result is a false
  positive, not a stored change.
- **One error per call.** The assistant may need several rounds for several independent errors. The
  run's step and tool-call limits already bound this.
- **The parser's first error is not local.** The unclosed-opener hints address the common botched
  edit; they cannot address every case.
- **Classic scripts.** Module mode rejects top-level `return` and `with`. The site's convention is ES
  modules, and the prompt says so.
- **Syntactically valid duplicates pass.** A parser cannot find a spare copy that still parses. This
  is a documented limitation, not a bug.
- **Best-effort template tokens.** A write token in a position where no JavaScript expression can go
  may produce a false error. A page that uses `<% if %>` to emit parts of a script is validated as
  the union of its literal text, so a branch that writes half a statement can be reported as an
  error even when a render would be valid. The common script page has no template tokens, and the
  skeleton is the chosen compromise over rendering.
- **Exotic line separators.** Positions use the template's `\r`, `\n` and `\r\n` rules. Acornima also
  treats `U+2028` and `U+2029` as line terminators, so a page containing either may shift later
  positions. This is not worth special handling now.
- **Dependency.** Acornima adds about 375 KB to the image and a BSD-3-Clause notice. It is pure
  managed, has no native asset and does not execute code. The notice is copied into the publish
  output, so the container carries it as well as the repository. `make audit` and
  `make license-check` are part of the gate.

## Relationship to other work

- [The content-type plan](page-content-types.md) has landed: a key's extension names
  the response media type, but there is no source-language kind. That is why these tools do not
  consult the map and why checking a non-JavaScript page is expected to produce errors.
- [IDEA-001](../../jobs/IDEA-001.md) owns the editor. Its LSP follow-up is
  [WHIM-01](../../jobs/WHIMS.md); this plan deliberately does not change the editor.
- [SEC-03](../../jobs/SEC-03.md) is not a prerequisite. Acornima parses and does not execute, so there
  is no new process boundary to argue about.
- DEC-15 requires the dependency justification and pin; this plan and DEC-145/DEC-146 supply them.
- DEC-26 means the `Core` change recompiles every stored image on its next request once. That is the
  normal cost of changing `Core`, not a surprise.

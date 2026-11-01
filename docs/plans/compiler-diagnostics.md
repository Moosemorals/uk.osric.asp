<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Template diagnostics: mapped positions and severities

Implementation plan for BUG-05, BUG-13 and STYLE-03, all closed by this work. BUG-05 and BUG-13 are
the same seam seen from two sides: a compile message is a value that carries both *where* it happened
and *how bad* it is. STYLE-03 is folded in because BUG-05 changes `Token` anyway: once it grows a
position, splitting it into its own file and making it a value type is cheap.

## Status

Implemented on 2026-10-31. Test counts at the time: 99 core, 39 runtime, 233 web, 42 integration.

## Why these belong together

- BUG-05 needs Roslyn's generated offsets translated back to the template. Roslyn already does that
  if the generated source carries `#line` directives — the runtime just has to stop inventing its
  own line arithmetic.
- BUG-13 needs the compiler's messages separated by severity. The same value that holds the mapped
  template position can hold the severity, so there is one `PageDiagnostic` rather than a string
  list plus a parallel warnings list.
- With severity on the diagnostic, `CompileResult.Success` can be derived from errors instead of
  copying Roslyn's flag, which is what makes a warning-only page succeed.
- `Token` gains a `TemplatePosition` for the `#line` directives, so STYLE-03's split and
  `readonly record struct` change land in the same edit.
- Once position is part of the diagnostic, "this error is in our generated code" becomes a testable
  fact. Such errors are compiler faults and must answer `500`, not masquerade as the author's
  mistake (Decision 6).

## Coordinate systems

There are two, and only one of them may leave the compiler:

| Name | Meaning | Where it lives |
| --- | --- | --- |
| **Template space** | 1-based line and column in the page source the author typed | `TemplatePosition`; `Token.Position`, `PageDiagnostic.Position`, `ApiReference.Position` |
| **Generated space** | 1-based line and column in the string handed to Roslyn, including the prelude, `ctx.WriteRaw(...)` calls and `#line` directives | Roslyn's `Location.GetLineSpan()` and an unmapped `GetMappedLineSpan()`; never stored or displayed |

Rules:

- Any line/column pair that crosses a boundary is a `TemplatePosition?`. `null` means "not known";
  there is no `(0, 0)` sentinel that could be mistaken for a real position.
- `Location.GetLineSpan()` is generated space unconditionally. Nothing uses it after this work.
- `Location.GetMappedLineSpan()` returns template space **only** when `HasMappedPath` is true, which
  means the node is inside a `#line ... "page"` region. The two mapping helpers check that flag;
  everywhere else treats an unmapped span as "no position".
- `#line` takes a template line, so `Token.Position.Line` goes in unchanged; the column padding uses
  `Token.Position.Column` unchanged.
- The two closing braces in the postlude are deliberately mapped to
  `#line {templateLineCount + 1} "page"`. An unclosed `{` or `(` is an author error, but Roslyn
  reports it at the end of the generated file; without this directive it would look like a compiler
  fault. The mapped position is "one line past the end of the template", not a claim that the author
  wrote line `N + 1`.
- Do not add a `Line`/`Column` pair to a new type without naming it `TemplatePosition` (or
  documenting it as generated space and keeping it inside the compiler). The type is the comment.

## Decisions

### 1. One `PageDiagnostic` value in `Core`, with severity and position

Take BUG-13's second option: a `PageDiagnostic` record with a severity, not two parallel
`Errors`/`Warnings` lists. One ordered list keeps Roslyn's order for display and one JSON payload in
storage; `Success` is the only consumer that needs to ask about severity.

```csharp
// src/uk.osric.asp.Core/TemplatePosition.cs
/// <summary>A 1-based line and column in the page template, as the author typed it.</summary>
/// <param name="Line">The 1-based line in the template.</param>
/// <param name="Column">The 1-based column in the template.</param>
/// <remarks>
/// The only coordinate system that is stored or displayed. Generated-source coordinates stay
/// inside Roslyn; an unknown position is null, never (0, 0), so it cannot be mistaken for a real
/// one.
/// </remarks>
public readonly record struct TemplatePosition(int Line, int Column) {

    public override string ToString() {
        return $"line {Line}, column {Column}";
    }
}

// src/uk.osric.asp.Core/PageDiagnosticSeverity.cs
/// <summary>How serious a <see cref="PageDiagnostic"/> is.</summary>
public enum PageDiagnosticSeverity {
    Warning,
    Error,
}

// src/uk.osric.asp.Core/PageDiagnostic.cs
/// <summary>One problem found in a page, ready to display.</summary>
/// <param name="Severity">Whether the page can still run.</param>
/// <param name="Position">Where in the template, or null when the position is not known.</param>
/// <param name="Message">The diagnostic code and text, without severity or position.</param>
public sealed record PageDiagnostic(
    PageDiagnosticSeverity Severity,
    TemplatePosition? Position,
    string Message) {

    public override string ToString() {
        string position = Position is { } value ? $"{value}: " : string.Empty;
        string severity = Severity == PageDiagnosticSeverity.Error ? "error" : "warning";
        return $"{position}{severity} {Message}";
    }
}
```

- `Position` is template space. It is `null` when the position is unknown; there is no line-only
  state because a mapped Roslyn span always has both parts, and the policy's shape error has
  neither.
- `Message` carries the diagnostic code (`CS1525: ...`, `PAGE001: ...`, `TEMPLATE001: ...`) but no
  severity or position. `ToString` adds those, so the editor and the error page share one rendering.
- Three files: one top-level type per file, which is the same rule STYLE-03 applies to the tokenizer
  types.

### 2. `#line` directives around every code block, with column padding

`#line` can set a line and a file, but not a column. The generator therefore emits the code block on
the line *after* the directive and pads it to the column the code starts at in the template:

```csharp
result.Append($"#line {token.Position.Line} \"page\"");
result.Append(Environment.NewLine);
result.Append(' ', token.Position.Column - 1);
result.Append(token.Value);
result.Append(Environment.NewLine);
result.Append("#line default");
result.Append(Environment.NewLine);
```

For a template

```text
line one
<% int value = ; %>
```

the generated body contains

```csharp
#line 2 "page"
   int value = ;
#line default
```

The three spaces are the two columns of `<%` plus the space the token itself starts with, so `int`
lands on template column 4 and the `;` on column 16.

- The file name is the fixed string `"page"`. It exists only to make `HasMappedPath` true; no page
  key is needed because the editor edits one page, and a fixed name avoids escaping arbitrary keys
  inside a directive.
- `#line default` after every block is what stops a later text block's newlines from shifting the
  mapping. It also puts the generated `ctx.WriteRaw(...);` statements that carry text outside every
  `#line` region. If Roslyn reports a diagnostic on one of those calls — because the generated call
  itself is wrong, not because the page is — `GetMappedLineSpan()` falls back to generated space
  (`HasMappedPath == false`), so `PageDiagnostic.Position` is `null`.
- That `null` is deliberate. A template line and column would be a fiction: nobody typed that call,
  and the nearest page text is not where the fault is. The generated source logged at `Debug` is
  where to look instead. If malformed page code causes a *secondary* diagnostic to land on a
  generated line, the primary syntax error is still mapped, so the author is not left without a
  position.
- The postlude is the one generated region that gets a template position: before the closing `}}`
  the generator emits `#line {templateLineCount + 1} "page"`. An unclosed block or bracket is the
  author's error, but Roslyn reports it at the end of the file; mapping the postlude makes it an
  author diagnostic at end-of-template instead of an internal fault. (A genuinely broken postlude
  would therefore look like an end-of-template page error; the fixed string is covered by the
  empty-page compile test, so that is a test failure, not a production surprise.)
- Code blocks that begin with a newline are handled naturally: the padding is followed by the page's
  own newline, so the next line starts at column 1 exactly as it does in the template.
- `#line hidden` is deliberately not used. The debugger is not the subject here, and hiding the
  generated calls would complicate the API walker's mapped spans for no gain.

### 3. `GetMappedLineSpan` is the only mapping rule

`PageApiChecker.PageLine` currently subtracts `TemplateSourceGenerator.PageCodeFirstLine`. That
constant only worked because text literals preserved their newlines and nothing else added a line.
`#line` directives add lines on purpose, so the arithmetic must go:

```csharp
private static TemplatePosition? TemplatePositionOf(SyntaxNode node) {
    FileLinePositionSpan span = node.GetLocation().GetMappedLineSpan();
    // GetMappedLineSpan() falls back to generated space for a node outside a #line region.
    // HasMappedPath is what makes the pair template space; without it there is no position.
    return span.HasMappedPath
        ? new TemplatePosition(span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1)
        : null;
}
```

The same helper shape, with the same comment, is used in `TemplateCompiler` for Roslyn's own
diagnostics. Delete `PageCodeFirstLine` and its remark; one mapping rule, one place, one coordinate
system on the way out.

### 4. `Token` becomes a `readonly record struct` with a position — STYLE-03

Move `Token` and `TokenType` out of `TemplateTokenizer.cs`:

```csharp
// src/uk.osric.asp.Core/Token.cs
public readonly record struct Token(TokenType Type, string Value, TemplatePosition Position);

// src/uk.osric.asp.Core/TokenType.cs
public enum TokenType {
    Text,
    Code,
}
```

- `Position` is template space and required, not optional: the only construction site is the
  tokenizer, and a default position would hide a missing one.
- Record-struct equality keeps the existing `Is.EqualTo(new Token[] { ... })` assertions working;
  the tests just have to spell the positions out.
- The tokenizer computes all positions from one array of line starts, so tokenizing stays linear.
  `\r\n`, `\n` and a lone `\r` are line breaks; other Unicode separators are not, matching what the
  existing `TEMPLATE001` line counter already did. (Roslyn is more generous, but a textarea does not
  produce those characters, and pretending to support them would add a rule nobody tests.)

### 5. `CompileResult` derives success from error diagnostics

```csharp
public sealed record CompileResult(byte[]? Image, IReadOnlyList<PageDiagnostic> Diagnostics) {

    /// <summary>Whether the page can run; warnings do not stop it.</summary>
    public bool Success =>
        Diagnostics.All(diagnostic => diagnostic.Severity != PageDiagnosticSeverity.Error);
}
```

- A warning-only page therefore has `Success == true` and an image.
- `CompileResult` only ever contains author-facing diagnostics. If the compiler cannot produce an
  image and there is no mapped author error to explain why, that is a compiler fault and
  `TemplateCompiler` throws instead of returning an image-less success (Decision 6).
- `TemplateParseResult` and `TokenizeResult` keep `Success => Errors.Count == 0`; their lists only
  ever hold errors. All three result types now agree about what success means.

### 6. Compiler faults are 500s, not page diagnostics

A diagnostic is user-facing only if the author can act on it. Everything else is a fault in the
generated source or the compiler configuration, and belongs in the server's error path.

Rules for Roslyn diagnostics:

- `Position != null` (mapped) → author-facing; include it in `CompileResult.Diagnostics`.
- `Position == null` **and** at least one mapped error exists → it is a cascade of the author's
  error; drop it. The mapped error is the one to fix, and the generated source is logged at `Debug`.
- `Position == null` and there is no mapped error → an internal fault. `TemplateCompiler.Compile`
  throws `InvalidOperationException` naming the diagnostic IDs and messages. It does not return a
  `CompileResult`, and nothing is stored as a page diagnostic.
- An unmapped warning is a warning in *our* generated code; drop it. It is not the author's
  problem, and a warning never fails a request.

Rules for the page API checker:

- `PAGE002` (the page code escaped the method) is author-facing even though it has no position: the
  author wrote the brace that closed the method.
- A banned construct or policy violation on a node with `Position == null` is generated code using
  an unapproved API: that is our bug, so throw rather than show `PAGE001` to the author.

Why not simply "any unmapped error is a fault": an unclosed `{` or `(` is a normal author typo, and
Roslyn reports it at the end of the generated file. The postlude's
`#line {templateLineCount + 1} "page"` (Decision 2) makes that error mapped, so the fault rule
applies only to genuinely generated code. A page that leaves a string open produces a mapped
`CS1010` first; any generated-line error that follows is a cascade and is dropped.

The shell does not convert these exceptions into a page response. It may only preserve the source
before rethrowing:

- `MainEndpoint` lets `PageService.RenderAsync` propagate; the ASP.NET exception handler answers
  `500`. `RenderAsync` records the outcome as `error` (not `compile-error`), so a fault is visible
  in the metrics as a server error.
- `PageService.SaveAsync` first stores the source with no compilation, then rethrows: our bug must
  not discard the author's edit. The page has no image and will retry the compile on its next
  request.
- `PageService.Compile` logs the exception at `Error` and the generated source at `Debug`, and
  records a failed compile, before rethrowing. No `PageDiagnostic` is invented for the fault. The
  metric says "failed compile"; the `Error` log is what distinguishes a fault from an author error
  (adding a separate metric tag is deliberately left out of this change).

### 7. Storage keeps the same column, with a tolerant reader for the old payload

`Files.Diagnostics` is already `TEXT`, so no schema change and no EF migration. The JSON changes
from a string array to an array of objects:

```json
[{
  "Severity": "Error",
  "Position": { "Line": 2, "Column": 16 },
  "Message": "CS1525: Invalid expression term ';'"
}]
```

- `Position` is template space by definition; the nested `Line`/`Column` are the same
  `TemplatePosition` pair the editor printed, not generated coordinates.
- Serialize with `JsonStringEnumConverter` so the severity is legible in the database.
- Deserialize `PageDiagnostic[]`. If that throws `JsonException`, fall back to the old `string[]`
  and give each message the severity implied by the row: an image means it was a warning, no image
  means it was an error. Legacy messages have no position (`Position = null`).
- The fallback is deliberately temporary; delete it once no row predates this change. It is the only
  reason `SqliteStorage` needs to know about a legacy shape at all.
- Do **not** bump `TemplateCompiler.FormatVersion`. The emitted assembly is unchanged, and
  `RuntimeId` already includes `Core`'s module version id, which changes with this work, so stored
  images go stale and are recompiled on their next request anyway. The editor can still read an old
  payload before that first request, which is why the fallback exists.

### 8. The shell shows severity, and the editor stops lying

- `EditorEndpoint` answers:
  - `"Saved successfully."` when the list is empty;
  - `"Saved with errors."` when it contains an error;
  - `"Saved with warnings."` when it contains only warnings.
- `Framework.GetDiagnostics` takes `IReadOnlyList<PageDiagnostic>`, orders it with
  `PageDiagnostic.SortedForDisplay` (by line, then column, with unpositioned diagnostics last and a
  stable sort at equal positions), and wraps each line in a `diagnostic-error` /
  `diagnostic-warning` span. `site.css` gains the two colours. Ordering is a display rule, so the
  stored list keeps the compiler's order. The severity word
  from `PageDiagnostic.ToString` is already in the text, so the page is readable with CSS off.
- `RenderResult`, `MainEndpoint` and `PageCompilation` switch from `IReadOnlyList<string>` to
  `IReadOnlyList<PageDiagnostic>`. `PageService.SaveAsync` returns the same list.
- `PageService`'s log line distinguishes three outcomes: `compiled`, `compiled with warnings`,
  `with errors`. `PageMetrics` is unchanged: warnings still count as a successful compile.

### 9. The generated source is available through the existing debug switch

`PageService.Compile` already has the generated source in scope. Log it at `Debug` when compilation
fails:

```csharp
logger.LogDebug("Generated source for {Path}:{NewLine}{Source}",
    path.Path, Environment.NewLine, source);
```

Setting `Logging:LogLevel:uk.osric.asp.Web.Pages.PageService=Debug` is the debug flag; no new
configuration key, no file dumps, and nothing page-authored is shown to a visitor. The source is what
went to Roslyn, directives included, so a generator bug can be reproduced exactly.

Showing that source to the author in the editor is a separate feature, recorded as
[ARCH-11](../jobs/ARCH-11.md).

## Phases

Test first in each phase: add the failing test, make it pass, then tidy.

### Phase 1 — the diagnostic value

Files:

- add `src/uk.osric.asp.Core/TemplatePosition.cs`
- add `src/uk.osric.asp.Core/PageDiagnosticSeverity.cs`
- add `src/uk.osric.asp.Core/PageDiagnostic.cs`
- change `src/uk.osric.asp.Core/TokenizeResult.cs` and `TemplateParseResult.cs` to
  `IReadOnlyList<PageDiagnostic>`
- add `test/uk.osric.asp.Core.Tests/PageDiagnosticTests.cs`

Tests:

- line and column, severity word, code and message render in one line;
- a `null` position omits line and column; a position renders both;
- `Warning` renders `warning`, `Error` renders `error`.

This phase compiles only after the tokenizer produces the new type, so it is the smallest slice that
can be made green: update the tokenizer's `TEMPLATE001` construction at the same time (Phase 2's
position work can follow).

### Phase 2 — tokenizer positions and STYLE-03

Files:

- add `src/uk.osric.asp.Core/Token.cs`, `TokenType.cs` and an internal `TemplateLines.cs`
- change `src/uk.osric.asp.Core/TemplateTokenizer.cs`
- update `test/uk.osric.asp.Core.Tests/TemplateTokenizerTests.cs`

Work:

- put the line-break rule and position calculation in `TemplateLines` (line-start array plus a
  binary-search `TemplatePositionOf(offset)`), so the tokenizer and the generator share one rule;
- give every token its start `TemplatePosition`: text at `index`, code at `codeStart + 2`;
- make `TEMPLATE001` a `PageDiagnostic(Error, TemplatePosition, ...)`; the message loses its
  embedded "at line x, column y" because the position is now a field;
- `Token` is a `readonly record struct`, so equality is by value.

Tests:

- all existing token equality assertions include `Position` (a `TemplatePosition`);
- `TEMPLATE001` carries the position of the `<%`, not the end of the input;
- positions are right after a `\r\n` and after a lone `\r`.

### Phase 3 — `#line` directives in the generator

Files:

- change `src/uk.osric.asp.Core/TemplateSourceGenerator.cs` (it uses the `TemplateLines` helper
  added in Phase 2);
- update `test/uk.osric.asp.Core.Tests/TemplateSourceGeneratorTests.cs`.

Work:

- emit the directive, padded code, newline and `#line default` for every `TokenType.Code`;
- append a newline after text statements so every directive starts a line;
- comment the emission: `Token.Position` is template space, which is exactly what `#line` expects,
  and the padding comes from the same pair;
- emit `#line {templateLineCount + 1} "page"` before the closing `}}`, so an unclosed page block
  is an author diagnostic at end-of-template rather than an unmapped error;
- delete `PageCodeFirstLine`.

Tests:

- a code block on template line 2 is preceded by `#line 2 "page"` and followed by `#line default`;
- the generated line has the padding that puts the code's first character at its template column;
- two code blocks after different numbers of text blocks each keep their own line;
- the postlude is preceded by `#line {lineCount + 1} "page"`;
- no directive is emitted for text-only templates apart from the postlude mapping.

### Phase 4 — the runtime maps and separates

Files:

- change `src/uk.osric.asp.Runtime/PageApiChecker.cs`
- change `src/uk.osric.asp.Core/PageApiPolicy.cs` (`ApiReference` gains a `TemplatePosition?` and
  `Check` returns `PageDiagnostic`)
- change `src/uk.osric.asp.Runtime/TemplateCompiler.cs`
- change `src/uk.osric.asp.Runtime/CompileResult.cs`
- update `test/uk.osric.asp.Core.Tests/PageApiPolicyTests.cs`
- update `test/uk.osric.asp.Runtime.Tests/PageApiPolicyTests.cs` and `TemplateCompilerTests.cs`

Work:

- replace `PageLine` with the mapped `TemplatePositionOf` helper in `PageApiChecker`;
- return `PageDiagnostic` values from the shape check, banned-node check and policy check;
- `ApiReference.Position` is template space; `PageApiPolicy.Check` copies it into the diagnostic
  unchanged;
- `PAGE001` dedups by API name as before; its message no longer embeds `(line n)`;
- `PAGE002` has no position and reports `Position = null`; it is author-facing because it comes from
  the shape check, not from a Roslyn node;
- a banned construct or policy violation whose node has `Position == null` is generated code and
  throws an internal fault; it is never shown as `PAGE001`;
- `TemplateCompiler` parses into a syntax tree and maps `result.Diagnostics` using the rules in
  Decision 6: keep mapped errors and warnings, drop unmapped cascades when a mapped error exists,
  drop unmapped warnings, and throw `InvalidOperationException` when there is an unmapped error and
  no mapped error;
- both mapping helpers (`PageApiChecker.TemplatePositionOf` and `TemplateCompiler`'s equivalent)
  carry the comment that `GetMappedLineSpan()` without `HasMappedPath` is generated space and must
  not escape.

Tests:

- a syntax error on template line 3 reports line 3 and the template column of the offending token,
  including when several text blocks precede it;
- a warning-only page (`<% int unused = 1; %>`) succeeds, emits an image, and reports one
  `Warning` `CS0219`;
- a page with both a warning and an error fails and still carries the warning;
- an unclosed `{` is reported as a mapped `CS1513` at end-of-template, not thrown as a fault;
- a banned API reports `Error`, the API name and the mapped page line/column;
- shell types still produce `CS02xx` and no `PAGE001`;
- feeding `Compile` a shape-valid but malformed generated source (no `#line` region) throws
  `InvalidOperationException` instead of returning a diagnostic;
- a shape-valid generated source that uses a banned API outside a `#line` region throws, because
  that is generated code calling something the policy forbids;
- a generated source with one mapped error and one unmapped error returns the mapped error and does
  not throw.

### Phase 5 — the shell

Files:

- change `src/uk.osric.asp.Web/PageCompilation.cs`, `Pages/PageService.cs`,
  `Pages/RenderResult.cs`, `Endpoints/MainEndpoint.cs`, `Endpoints/EditorEndpoint.cs`,
  `Framework.cs`, `Storage/SqliteStorage.cs`, `Storage/StoredFile.cs`, `wwwroot/site.css`
- update `test/uk.osric.asp.Web.Tests/PageServiceTests.cs`, `EditorEndpointTests.cs`,
  `MainEndpointTests.cs`, `SqliteStorageTests.cs`, `PageMetricsTests.cs`, `PageScopeTests.cs`
- update `test/uk.osric.asp.Web.IntegrationTests/EditorEndpointTests.cs`, `PageCacheTests.cs`,
  `ShellTests.cs`

Work:

- carry the diagnostic list through `PageCompilation` and `RenderResult`;
- classify the editor flash message by severity;
- render diagnostics with severity classes;
- serialize/deserialize the new payload and read the old one;
- log the three compile outcomes and the generated source on failure;
- let a compiler fault propagate: `RenderAsync` leaves the outcome as `error`, and `SaveAsync`
  stores the source with no compilation before rethrowing, so the edit survives our bug;
- add a throwing `ITemplateCompiler` test double to the web tests.

Tests:

- saving a warning-only page says `"Saved with warnings."`, stores an image, renders, and says
  `"Saved successfully."` on the next save of clean source;
- saving an error page still says `"Saved with errors."` and the page still answers
  `"Handler failed to compile."`;
- the editor shows `warning CS0219` as a warning and `error CS1525` as an error;
- a diagnostic payload round-trips through SQLite with severity and position intact;
- an old `string[]` payload is read as warnings when the row has an image and errors when it does
  not;
- the page HTML still encodes diagnostic text;
- a compiler fault thrown by `ITemplateCompiler.Compile` propagates out of `RenderAsync` (the
  existing error-handling tests prove the host turns that into `500`);
- a compiler fault during save stores the source with `Compilation == null` and rethrows, so the
  next editor GET still shows the edit;
- no `PageCompilation` is stored with an internal-fault diagnostic.

### Phase 6 — docs and issue closure

- `docs/architecture.md`: replace "`CompileResult` of booleans and strings" with the
  `PageDiagnostic` contract, record the `#line` mapping rule, and say that the diagnostics column is
  a JSON array of positioned, severity-tagged values.
- `docs/jobs/README.md`: remove BUG-05, BUG-13 and STYLE-03 from the open table and add them to
  Closed with the summary from this work.
- Delete `docs/jobs/BUG-05.md`, `BUG-13.md` and `STYLE-03.md`, following the existing convention
  that closed job files disappear and their history lives in git.

## Test matrix

| Behaviour | Suite | Test |
| --- | --- | --- |
| Diagnostic rendering | Core | `PageDiagnosticTests` |
| Token template positions, `TEMPLATE001` position, STYLE-03 move | Core | `TemplateTokenizerTests` |
| `#line` directives and padding | Core | `TemplateSourceGeneratorTests` |
| Policy diagnostics carry severity and position | Core | `PageApiPolicyTests` |
| Syntax error on template line *n* reports line *n* | Runtime | `TemplateCompilerTests` |
| Warning-only page succeeds and emits | Runtime | `TemplateCompilerTests` |
| Unclosed block maps to end-of-template, not a fault | Runtime | `TemplateCompilerTests` |
| Generated-source error with no mapped error throws | Runtime | `TemplateCompilerTests` |
| Banned API reports mapped line/column | Runtime | `PageApiPolicyTests` |
| Compiler fault propagates and is not stored as a diagnostic | Web | `PageServiceTests` |
| Save returns warnings without failing | Web | `PageServiceTests` |
| Flash message distinguishes warnings and errors | Web | `EditorEndpointTests` |
| Diagnostics styled by severity | Web | `EditorEndpointTests` |
| JSON round-trip and legacy payload | Web | `SqliteStorageTests` |
| Warning-only page is served and reported through the host | Integration | `EditorEndpointTests` |
| Error page still answers 500 with mapped text | Integration | `PageCacheTests`, `ShellTests` |

## Edge cases and risks

- **Unclosed blocks and brackets are author errors.** The postlude's `#line {lineCount + 1}` makes
  `}`/`)` expected diagnostics land at end-of-template. Without it, the most common template typo
  would be classified as a compiler fault.
- **Unmapped errors are faults, not diagnostics.** The generated `ctx.WriteRaw(...)` calls and the
  prelude have no `#line` region. An error there with no mapped author error throws, the shell
  answers `500`, and the generated source is in the `Debug` log. `(3,48)` can never come back as a
  page diagnostic.
- **Cascades are dropped.** If the author already has a mapped error, unmapped errors from the same
  broken parse are not faults; they disappear, leaving the mapped error to fix. The debug log still
  has the full generated source.
- **Unmapped warnings are ours.** A warning on generated code is dropped rather than shown to the
  author; compile warnings they can act on are the ones mapped to their own code.
- **Page code can emit its own `#line`.** That can spoof a position, or make page code look
  generated and produce a `500`. Page code is the owner's code, so this is not a security boundary;
  a future policy rule could reject `#line` in page code if it becomes a nuisance.
- **A code block that ends mid-expression.** The `#line default` line is outside the expression, so
  it still parses as a directive; Roslyn reports the primary syntax error at the mapped position.
  If a secondary error lands on generated code, the mapped error suppresses it (above).
- **Legacy diagnostics.** A mixed warning+error payload from before this change has no per-message
  severity; it is read with the row's image as the only clue. That row recompiles on its next
  request and is rewritten in the new format.
- **Line endings.** `\r\n` and lone `\r` are counted as one break; the padding uses character
  columns, which is what Roslyn reports.
- **Warnings on a page with a policy violation.** The policy stops before emit, so warnings are not
  collected on a page that also violates the policy. Errors dominate; no behaviour is lost.
- **A broken postlude would look like a page error.** It is mapped to end-of-template by design.
  The postlude is the fixed string `}}` and is compiled by every empty-page test, so a real fault
  there is a test failure rather than a production surprise.

## Deliberately not doing

- No rich editor with squiggles: the editor is a `<textarea>`, and the fix is an accurate
  `(line, column, message)` list plus the severity word. Underlining can come with an editor
  component if one is ever built.
- No `Error`/`Warning` columns in storage: the JSON payload is a display-only blob, and a schema
  migration for it would be cost without a query that needs it.
- No compiler-fault page or flash message: the existing unexpected-exception handler is the `500`
  response, and the fault's details go to the server log rather than to the visitor.
- No generated-source viewer in the editor: the source is logged at `Debug` for now; the viewer is
  recorded as [ARCH-11](../jobs/ARCH-11.md).
- No new package. `#line`, `GetMappedLineSpan`, `System.Text.Json` and ASP.NET Core logging are all
  already available.

## Validation

```bash
make test-core
make test-runtime
make test-web
make test-integration
make format-check
make license-check
```

Done when all four suites pass, the two new warnings/error tests are green through the real host,
and a stored broken page shows a template line and column rather than a generated one.

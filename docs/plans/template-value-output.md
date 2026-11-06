<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Writing a value from a template

Implementation plan for ARCH-12: add an escaped expression block,
`<%= %>`, and a raw expression block, `<%== %>`, alongside statement blocks and declaration blocks.

## Status

Complete. `make check` passed: Core 209, Runtime 68, Web 387 and Web Integration 76 tests.

## Goal and constraints

- `<%= expression %>` writes the expression through `IContext.Write`, which HTML-encodes it.
- `<%== expression %>` writes through `IContext.WriteRaw`, which preserves it.
- The expression is a C# string expression. `IContext` is unchanged; a non-string value needs an
  explicit conversion such as `.ToString()`, and otherwise produces Roslyn's normal compile error.
- Expressions retain their template line and column, including on later lines of a multi-line
  expression. Generated wrapper code must not turn a page error into an unmapped compiler fault.
- Existing `<% %>` and `<%! %>` delimiters and their behavior remain unchanged.
- `Core` stays reference-free and Roslyn-free. No storage, schema, runtime identity or page API policy
  change is needed.

## A page

```text
<h1><%= ctx.Path %></h1>
<%== "<hr>" %>
```

The heading's path is escaped; the horizontal rule is raw. A page that mostly writes values can use
the template language for markup and reserve `<% %>` for control flow and statements.

## Decisions

### D1 — `<%==` is recognized before `<%=`

After finding `<%`, the tokenizer checks for `==` first, then `=`, then the ordinary code opener.
The marker is adjacent: `<% = value %>` remains an ordinary code token. Longest-first matching
prevents the first `=` of `<%==` from being consumed as an escaped-write opener.

`%>` still ends a block without considering C# string or comment state. Thus a `%>` inside a quoted
string ends an expression block exactly as it ends a code block today.

### D2 — Preserve the raw expression in a distinct token

`TokenType` gains `Write` and `WriteRaw`; these are distinct from `Code`, `Declaration` and `Text`.
The token's value is the exact text between the opener and `%>`, and its `Position` is the first
content character. It also carries the opener position, because an empty expression is diagnosed at
the delimiter rather than at content that does not exist.

The generator trims whitespace around the expression and removes at most one trailing semicolon.
Internal whitespace and line breaks stay intact. This permits `<%= ctx.Path; %>` while keeping the
tokenizer's content lossless. An expression that becomes empty after that normalization — including
whitespace or just `;` — fails generation with:

```text
TEMPLATE002: write expression is empty.
```

The diagnostic points to the opener for both escaped and raw writes. `TEMPLATE002` is available;
`TEMPLATE001` keeps its existing meaning and code.

### D3 — Generate one call, with separate wrapper and expression mappings

`Write` generates `ctx.Write(expression);`; `WriteRaw` generates `ctx.WriteRaw(expression);`.
Each uses the corresponding existing `IContext` method. No new overload or capability is added.

The generated call is split across lines so the wrapper and expression can have separate `#line`
regions:

```csharp
#line 4 "page"
ctx.Write(
#line 4 "page"
   ctx.Path
#line default
);
```

The wrapper maps to the template opener's line, and its first line is padded to the opener's
template column. A call-level diagnostic such as an argument-count error therefore maps to that
line, but its column identifies the generated method name (`Write`/`WriteRaw`), not the opener:
Roslyn `#line` directives map lines, not columns. The expression region maps to the first
non-whitespace expression character, padded to its template column; Roslyn then maps every retained
line of a multi-line expression to its corresponding template line. `);` is generated code and is
outside the mapping. `#line default` is restored before the next generated or page token.

When trimming leading whitespace moves the expression start, calculate its position using the same
CRLF, LF and CR rules as `TemplateLines`; do not guess from the first line alone. A trailing
semicolon is removed only after trimming, and only one is removed.

### D4 — No API-policy or SDK change

Both `IContext.Write(string)` and `IContext.WriteRaw(string)` are already part of the SDK and allowed
by the page API policy. The generated calls use those methods; no allow-list entry or reference
changes. Non-string expressions are not implicitly converted: the resulting `CS1503` points to the
expression and tells the author to convert it explicitly.

### D5 — Keep existing block diagnostics stable

An unterminated expression block reuses `TEMPLATE001`, with a message naming the exact opener:

```text
TEMPLATE001: unterminated write block; the '<%=' has no matching '%>'.
TEMPLATE001: unterminated raw-write block; the '<%==' has no matching '%>'.
```

No existing diagnostic code is reused or assigned a new meaning. `TEMPLATE002` is reserved for an
empty expression, not a general C# syntax error.

## Implementation sequence

Test-first throughout.

### Step 1 — tokenizer

Add `TokenType.Write` and `TokenType.WriteRaw`, preserve each expression verbatim, retain both its
content position and opener position, and match the longest opener first. Add cases to
`TemplateTokenizerTests`:

- `<%= ctx.Path %>` is a `Write` token and `<%== ctx.Path %>` is a `WriteRaw` token;
- the expression content and starting position exclude the opener and preserve whitespace/newlines;
- `<% = value %>` remains a `Code` token;
- escaped and raw forms work adjacent to text and to one another without empty text tokens;
- unterminated escaped and raw blocks report `TEMPLATE001` at their opener, naming the matching
  delimiter;
- `%>` inside a quoted expression closes the block, preserving the existing delimiter rule.

### Step 2 — generator

Add expression normalization and generation, including empty-expression rejection and source mapping.
Add cases to `TemplateSourceGeneratorTests`:

- escaped and raw tokens generate the exact corresponding `ctx.Write(...)` or `ctx.WriteRaw(...)`
  call;
- expression text is emitted verbatim apart from edge trimming and removal of one trailing `;`;
- a multi-line expression stays multi-line and is mapped to its template line and column;
- wrapper-level diagnostics map to the opener's line (the compiler column remains on the generated
  method name), expression diagnostics map to the expression, and generated closing punctuation is
  outside the mapped region;
- empty and semicolon-only expressions return no source and one positioned `TEMPLATE002`;
- unterminated expression blocks return no source and the appropriate `TEMPLATE001`.

### Step 3 — runtime behavior and diagnostics

Use the existing host rendering path, not a fake context whose `Write` implementation does not HTML
encode. Add cases to `PageEndpointTests` proving that a string containing `<`, `>` and `&` is encoded
through `<%= %>` and preserved through `<%== %>`. Add a compiler case proving that an expression
error, including one on a later line, reports the template line and column; a non-string expression
reports its mapped `CS1503`.

### Step 4 — documentation

- `README.md`, “A page”: teach both expression forms beside `<% %>` and `<%! %>`, with a short
  escaped/raw example and the string-expression rule.
- `docs/architecture.md`: describe both generated calls, the escaping distinction, the delimiter and
  line-mapping rules, and add `TEMPLATE002` to the diagnostic table.
- `docs/jobs/ARCH-12.md`: link this plan and record the decisions are settled.
- `docs/jobs/README.md`: link the plan from ARCH-12's place in the rough order.

### Step 5 — verify

Run the focused Core, Runtime and Web test suites while iterating, then `make check`. Record the
final suite counts and completion status in this plan and close ARCH-12 only after the gate passes.

## Edge cases

| Template | Result |
| --- | --- |
| `<%= ctx.Path %>` | escaped `ctx.Write(ctx.Path);` |
| `<%== "<hr>" %>` | raw `ctx.WriteRaw("<hr>");` |
| `<%= ctx.Path; %>` | escaped write; one terminal semicolon is removed |
| `<%== ctx.Path; ; %>` | only the last semicolon is removed; remaining invalid C# is reported in template space |
| `<%=   %>` | `TEMPLATE002` at `<%=` |
| `<%== ; %>` | `TEMPLATE002` at `<%==` |
| `<% = ctx.Path %>` | ordinary code token, not a write token |
| `<%= ctx.Path` | `TEMPLATE001` at `<%=`, with that opener in the message |
| `<%== ctx.Path` | `TEMPLATE001` at `<%==`, with that opener in the message |
| `<%= "a %> b" %>` | `%>` ends the block even inside the string, as it does for `<% %>` |
| `<%= 42 %>` | `CS1503` at `42`; no implicit conversion or `IContext` overload |
| `<%=\n    ctx.Missing\n%>` | the compiler error maps to the expression's template line and column |

## Files touched

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Core/Compilation/TokenType.cs` | `Write`, `WriteRaw` |
| `src/uk.osric.asp.Core/Compilation/Token.cs` | optional opener position for expression tokens; update documentation |
| `src/uk.osric.asp.Core/Compilation/TemplateTokenizer.cs` | longest-first expression openers and `TEMPLATE001` wording |
| `src/uk.osric.asp.Core/Compilation/TemplateSourceGenerator.cs` | normalize and emit mapped write calls; report `TEMPLATE002` |
| `src/uk.osric.asp.Core/Compilation/TemplateLines.cs` | position advancement for leading whitespace when needed |
| `test/uk.osric.asp.Core.Tests/TemplateTokenizerTests.cs` | expression token cases |
| `test/uk.osric.asp.Core.Tests/TemplateSourceGeneratorTests.cs` | generated call, normalization and mapping cases |
| `test/uk.osric.asp.Runtime.Tests/PageCompilerTests.cs` | mapped expression compile errors |
| `test/uk.osric.asp.Web.Tests/PageEndpointTests.cs` | escaped and raw output behavior |
| `README.md`, `docs/architecture.md` | syntax, behavior and diagnostics |
| `docs/jobs/ARCH-12.md`, `docs/jobs/README.md` | plan status and link |

No changes to `IContext`, `PageApiPolicy`, `PageCompiler`, storage, migrations, `RuntimeId` or the
editor endpoint.

## Acceptance criteria

- `<%= %>` escapes and `<%== %>` preserves output through the real host path.
- The tokenizer chooses the longest adjacent opener; `<% =` remains ordinary code.
- Expression text is preserved except for the specified trim and single-semicolon normalization.
- Empty normalized expressions produce one `TEMPLATE002` at the opener and no generated source.
- Unterminated expression openers produce `TEMPLATE001` at the opener and name it.
- Compile errors in single- and multi-line expressions carry the author's line and column; errors
  caused by the generated call wrapper map to the opener's line rather than becoming compiler
  faults.
- `IContext` remains string-only, and non-string values require an explicit conversion.
- `make check` passes and this plan records its test counts.

## Not in this phase

- Implicit `ToString()` conversion or an `object` overload on `IContext`.
- Alternate syntax, such as a filter pipeline or an unescaped form other than `<%== %>`.
- Parsing C# strings/comments to make `%>` conditional; all block types keep the existing delimiter
  rule.

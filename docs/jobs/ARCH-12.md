<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-12 — A template has no shorthand for writing a value

| | |
| --- | --- |
| Severity | Medium |
| Area | Core, template language |
| Location | `src/uk.osric.asp.Core/Compilation/TemplateTokenizer.cs`, `src/uk.osric.asp.Core/Compilation/TemplateSourceGenerator.cs`, `README.md` |

## Problem

The only thing a template can say about a value is the full call:

```
<p>You asked for <% ctx.Write(ctx.Path); %>.</p>
```

That is the form the README teaches, and it is the only form the tokenizer knows. A page that is
mostly markup with a handful of values therefore reads as C# with HTML pushed to the edges: printing
one expression costs a keyword pair, a semicolon and a `ctx.` prefix every time.

Every previous generation of template engine offered a short echo — `<%= %>` in ERB, EJS and classic
ASP — so it is the spelling an author will try first, and today it silently compiles as the C#
statement `= ctx.Path;` and fails with a syntax error on the generated line. There is no shorthand
for unescaped output either; the author has to drop back to `<% ctx.WriteRaw(...); %>`.

## Fix outline

The syntax, normalization and source-mapping decisions are recorded in the
[implementation plan](../plans/template-value-output.md).

1. Add two openers to `TemplateTokenizer`: `<%=` for an escaped write and `<%==` for a raw one. Match
   the longest first — `<%==` before `<%=` before `<%` — or `<%== value %>` is read as `<%=` followed
   by the expression `= value`. The `=` must follow `<%` immediately, so `<% = value %>` stays ordinary
   code and the existing delimiter rules are unchanged.
2. Carry the distinction in the token rather than a flag on `Code`: give `TokenType` a `Write` and a
   `WriteRaw` member (alongside STYLE-03's move of `Token`/`TokenType` into their own file), so the
   generator's `switch` stays exhaustive and a new form cannot fall through as text.
3. Generate `ctx.Write(<expression>);` and `ctx.WriteRaw(<expression>);`, preserving the expression
   internally after the edge-trim and optional terminal-semicolon normalization.
4. Trim the expression and drop a single trailing `;`, so `<%= ctx.Path; %>` does not become
   `ctx.Write(ctx.Path;);`. Reject an empty expression with a `TEMPLATE002` diagnostic naming the line
   and column of the opener, rather than emitting `ctx.Write();` and letting Roslyn complain about a
   generated line the author cannot see.
5. Keep `TEMPLATE001` honest: an unterminated `<%=` names `'<%='` as the opener that needs its `%>`.
   The message already derives its text from the opener, so this is a parameter, not a new message.
6. Do not widen `IContext` for now. `Write` and `WriteRaw` take a `string`, so a non-string expression
   needs `.ToString()`; the compile error says exactly that, and the page API policy needs no change
   because both calls are already allowed. An `object` overload is a separate decision about escaping
   raw values and is not part of this issue.
7. Check the line mapping still holds. An expression may span lines, and the generated wrapper must
   preserve the expression's template line and column mapping; text tokens already embed the
   template's newlines in a verbatim literal, so this is a constraint on the new statements rather
   than a new problem for BUG-05 to absorb.
8. Update the docs: teach `<%= %>` and `<%== %>` in the README's **A page** section and document the
   generated calls, escaping and source mapping in `docs/architecture.md`.
9. Tests: tokenizer — `<%=`, `<%==`, `<% =` staying code, the two forms adjacent to text and to each
   other, unterminated, and `%>` inside a quoted string ending the block as it already does; generator
   — the exact statements, verbatim expression text, a trailing semicolon, and the `TEMPLATE002`
   diagnostic; runtime or integration — a page renders escaped text escaped and `<%== %>` raw.

## Status

Complete. The expressions generate escaped and raw output, with author-facing compile diagnostics.
`make check` passed: Core 209, Runtime 68, Web 387 and Web Integration 76 tests. See the
[implementation plan](../plans/template-value-output.md) for the syntax, normalization and mapping
decisions, including the wrapper-diagnostic column offset.

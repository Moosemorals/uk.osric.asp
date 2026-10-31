<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-12 — A template has no shorthand for writing a value

| | |
| --- | --- |
| Severity | Medium |
| Area | Core, template language |
| Location | `src/uk.osric.asp.Core/TemplateTokenizer.cs`, `src/uk.osric.asp.Core/TemplateSourceGenerator.cs`, `README.md` |

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

1. Add two openers to `TemplateTokenizer`: `<%=` for an escaped write and `<%==` for a raw one. Match
   the longest first — `<%==` before `<%=` before `<%` — or `<%== value %>` is read as `<%=` followed
   by the expression `= value`. The `=` must follow `<%` immediately, so `<% = value %>` stays ordinary
   code and the existing delimiter rules are unchanged.
2. Carry the distinction in the token rather than a flag on `Code`: give `TokenType` a `Write` and a
   `WriteRaw` member (alongside STYLE-03's move of `Token`/`TokenType` into their own file), so the
   generator's `switch` stays exhaustive and a new form cannot fall through as text.
3. Generate `ctx.Write(<expression>);` and `ctx.WriteRaw(<expression>);`, with the expression copied
   verbatim between the parentheses so Roslyn reports its own errors against the author's code.
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
7. Check the line mapping still holds. An expression may span lines, and the generated statement must
   not add or remove lines the page did not write; text tokens already embed the template's newlines
   in a verbatim literal, so this is a constraint on the new statements rather than a new problem for
   [BUG-05](BUG-05.md) to absorb.
8. Update the docs: the README's **A page** section gains `<%= %>` and `<%== %>` beside `<% %>`, and
   `docs/architecture.md` still says text becomes a `WriteRaw` call — name the two new statements
   there as well.
9. Tests: tokenizer — `<%=`, `<%==`, `<% =` staying code, the two forms adjacent to text and to each
   other, unterminated, and `%>` inside a quoted string ending the block as it already does; generator
   — the exact statements, verbatim expression text, a trailing semicolon, and the `TEMPLATE002`
   diagnostic; runtime or integration — a page renders escaped text escaped and `<%== %>` raw.

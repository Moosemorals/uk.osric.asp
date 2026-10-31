<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-11 — The editor cannot show the generated C# for a page

| | |
| --- | --- |
| Severity | Low |
| Area | Web, developer experience |
| Location | `src/uk.osric.asp.Web/Endpoints/EditorEndpoint.cs`, `src/uk.osric.asp.Core/TemplateSourceGenerator.cs` |

## Problem

The editor shows the template and, after a failed compile, the diagnostics. It does not show the
generated C# that Roslyn actually compiled: the three-line prelude, the `ctx.WriteRaw(...)` calls
for text, the `#line` directives, the page's code blocks and the closing braces.

That view is useful when a diagnostic is surprising, when a `#line` mapping looks wrong, or when a
compiler fault has to be reported. Today the source is only in the `Debug` log (see the
[compiler-diagnostics plan](../plans/compiler-diagnostics.md)) or one
`TemplateSourceGenerator.GenerateTemplateSource` call away in a debugger. Showing it also makes the
template language legible: it answers "what does my text become?" without requiring the author to
read the generator.

## Fix outline

1. Add a read-only generated-source view for a stored page, reachable from the editor (a link next
   to the diagnostics, or `/edit/{**path}/generated`). It must use the same Editor authentication
   policy as `/edit/**` and answer `Cache-Control: no-store`.
2. Generate on demand from the stored content with
   `TemplateSourceGenerator.GenerateTemplateSource`; when the template does not parse, show the
   `TEMPLATE001` diagnostic instead of a partial source.
3. Render it as HTML-encoded `<pre>` (or `text/plain`). Do not execute or compile anything for this
   view.
4. Once BUG-05 lands, the `#line` directives make the view self-explanatory; highlighting the
   ranges that map to the template is an optional follow-up, but plain source is enough to start.
5. Tests: an authenticated editor sees a source containing `class GeneratedPage`,
   `ctx.WriteRaw(` and `#line`; an anonymous request is rejected by the editor policy; a template
   that does not parse shows the parse error rather than generated source.
6. Deliberately out of scope for BUG-05 and BUG-13; those only log the source at `Debug`.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-018 — The document shell is not reachable from page code

| | |
| --- | --- |
| Size | Medium |
| Area | Sdk, Web, Html |
| Location | `src/uk.osric.asp.Web/Html/ShellDocument.cs`, `src/uk.osric.asp.Sdk/IContext.cs` |

## Idea

DEC-107 exists so a view cannot forget the head, the landmark or the sheet order, and DEC-116 fixes
that order. Both are C# in `Web/Html`: `ShellDocument` takes an `HttpContext` and writes to
`HttpResponse`, and `PageLayout` sits beside it with `Nav`, `Gutter` and `Diagnostics` over `NavLink`
and `DiagnosticLine`.

A page owns its whole response and gets none of this. So the second view moved into a page copies the
head, the landmark and the sheet order by hand, and the third one diverges — which makes "move a shell
view into a page" expensive for exactly the views that are worth moving.

The goal is the document shell and its fragments in `Sdk`, over `IContext` and plain values, so a
page and a shell view write the same document. The layout page from IDEA-005 phase 2 is the other
caller: it must write the same head and landmark as the shell without copying them.

## Sketch

1. Move the builders, not the writer. `PageLayout.Nav` is already a string from a label, links and a
   class; `Gutter` is a string from a count; `Diagnostics` is a list of lines and a severity. Those
   move to `Sdk` with `NavLink` and an Sdk diagnostics type — the same value IDEA-016 needs, settled
   once rather than twice.
2. `ShellDocument` has the only ASP.NET dependency, and only to write. Give `Sdk` a `PageDocument`
   that returns the markup, so a page writes it through `IContext.WriteRaw` and the shell's version
   writes the same string to an `HttpResponse`; one implementation, two callers. Do not fork it:
   DEC-107 was about there being one writer. The builder is not one `Begin`/`End` pair: it has the
   head and open `body`, the open and close of `main`, and the close of the document, so a layout can
   put a header before `main` and a footer after it without hand-rolling the head.
3. Encoding stays inside the builder. `WebUtility` is a base-class-library type, so `Sdk` can keep
   escaping in one place rather than leaving it to each page's `WriteRaw`.
4. Keep the risky parts unmissable. The title, the `main` landmark and the sheet order are the rules
   the decision exists to keep; the builder must make the correct document the easy one and a
   hand-rolled head the hard one.
5. The accessibility suite is the check. `ShellViews` renders every shell view and `AccessibilityTests`
   applies the shared rules (DEC-110); a page-built document should pass the same rules, and the
   builder is where that becomes possible at all.
6. Decide the alternative before building. IDEA-005 phase 2 is now a layout *page* rendered around a
   page at runtime, not a partial compiled into it. The layout page calls this builder, and the shell
   calls it through `ShellDocument`, so the document is written once. A compile-time include would
   make every dependent page's image depend on the layout's source — a layout edit would recompile
   every page — and the layout could not see the content's title before writing the head. The SDK
   type is for the stable document and fragments; the layout page is for the editable chrome.
7. Do not put policy in the builder: no CSP, no security headers, no status-code defaults. Those stay
   the shell's (`SecurityHeaders`, `PageEndpoint`) and a document builder that could weaken them
   would undo more than it fixed.
8. Tests: the builder's head matches what `ShellDocument` writes today; the sheet order is asserted
   once for both callers; a page-built document passes the accessibility rules; nothing is written
   twice when a page sets a header after opening the document.

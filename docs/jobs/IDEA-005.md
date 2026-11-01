<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-005 — A page cannot declare a type, so shared code has to be copied

| | |
| --- | --- |
| Size | Large |
| Area | Core, Runtime, template language |
| Location | `src/uk.osric.asp.Core/TemplateSourceGenerator.cs`, `src/uk.osric.asp.Runtime/PageApiChecker.cs:81` |

## Idea

A page's `<% %>` blocks are pasted into the body of `GeneratedPage.Handle`, and `PAGE002` rejects
anything else: a page may not declare types or members. That is why `Framework` — the header,
footer and diagnostics markup — lives in `src/uk.osric.asp.Web/Framework.cs` as C# in the host and
is unavailable to pages. Writing a page means writing one method, with everything it needs inlined,
and no page can share anything with another.

The goal is to write a version of `Framework` *as pages*: a page that declares a helper type or two
which other pages can use.

## Sketch

Two questions, and they are separable — settle the first before the second.

1. **Types inside one page.** The template language needs a way to say "this block is
   namespace-level declaration, not a statement". Options: a distinct delimiter (`<%! %>` and
   `<%+ %>` are the usual spellings), or "a block that begins with `using`/`class`/`record`
   is hoisted". The delimiter is simpler to tokenise, position and explain; hoisting by inspection
   is guesswork the author cannot see. Whichever is chosen, `TemplateSourceGenerator` emits the
   hoisted text outside `Handle`, and `CheckShape` relaxes from "exactly one class" to "the
   generated class plus the page's own declarations".
2. **Types shared between pages.** This is the hard one. Each page is its own assembly, so a type
   declared in page A cannot be referenced by page B. Three ways:
   - a designated *library* page whose source is compiled into every dependent page — no new
     storage, but every page pays its compile time;
   - a shared assembly built from one or more library pages and loaded into the same
     `PageLoadContext` — the honest version, but it needs a dependency order and an invalidation
     story of its own;
   - the real SDK (IDEA-004), where shared helpers are C# in an assembly pages bind against.
     Best long-term answer for anything stable, and no help at all for "I want to write it in a
     page".
3. Whichever way, the API policy still applies to page-declared types: a `class` in a page must
   not become a way to reach `System.Net`. `IsDeclaredIn` already treats same-tree symbols as page
   code, so this mostly falls out — but the members such a type names still go through
   `PageApiPolicy.Check`, and that needs a test.
4. Invalidation: a library page's change must recompile its dependents, the same way
   `ContentFingerprint` invalidates a page's stored image. Decide before shipping anything shared.
5. Start with (1) alone — it makes a single long page bearable and is testable in `Core` with no
   storage change at all. Treat (2) as its own job once (1) has been written with real pages.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-005 — Pages cannot share code or a layout

| | |
| --- | --- |
| Size | Large |
| Area | Sdk, Core, Runtime, Web |
| Location | `src/uk.osric.asp.Sdk/IContext.cs`, `src/uk.osric.asp.Web/Pages/PageRenderer.cs`, `src/uk.osric.asp.Web/Html/ShellDocument.cs` |

> **The layout half is done.** Phase 2 landed as [site navigation](../plans/site-navigation.md):
> every HTML page is wrapped in the stored `/_layout` by default, the slots are on
> `IPageLayout` reached through `ctx.Features`, the chain renders under one slot and deadline, and
> the shell keeps its own document (DEC-148–DEC-158). What is left of this item is shared **code**:
> a named declaration a second page can compile against.

## Idea

A page's `<% %>` blocks are pasted into `GeneratedPage.HandleAsync`, and `<%! %>` now lets a page
declare its own types (DEC-92). Nothing is shared: each page is its own assembly, so a helper or a
fragment written in one page cannot be used by another.

**Done.** The concrete need is a shared *document* — roughly `_layout.cshtml`. The
listing, history and editor views are the next views worth moving out of the shell, and every one of
them needs the head, the `main` landmark, the sheet order and the site chrome. Copying that into each
page is the mistake DEC-107 fixed in the shell, made once per page instead. And the layout must be a
page: the owner has to be able to edit the nav, the title and the markup without rebuilding and
redeploying the site, and a seeded layout must be restorable like any other page.

Shared types are a separate, smaller question. The stable halves of the framework — the document
builder, its fragments, a diagnostics value — belong in `Sdk` (IDEA-018, IDEA-016), where pages bind
to them at compile time. A library page compiled into every dependent page is only worth its
dependency order and invalidation story if a helper has to be authored in a page rather than in the
SDK. The layout does not need it: it is markup, and its home is a page.

## Sketch

### The layout is a page, rendered around another page

1. A page opts in with `ctx.SetLayout("/_layout")`. The host renders the page first, then renders the
   layout page with the page's already-rendered body available through `ctx.WriteBody()`. The layout
   is an ordinary page: same compiler, same image store, same API policy, same storage binding, same
   render budget. There is no second kind of page and no second assembly.
2. The content runs first, so the layout can read what the page set: `ctx.Title`, `ctx.AddHead(...)`,
   `ctx.AllowScripts()`. That order is what makes a layout's `<title>` and `<head>` possible without
   a setup block or a second pass.
3. The chain is per response, not per page image. Content and layout each keep their own compiled
   image. Editing the layout recompiles the layout and nothing else; the next request for any page
   that uses it gets the new chrome. That is the "without a redeploy" requirement, and it is why the
   layout is rendered around the page rather than compiled into it.
4. A layout may name another layout, so the same mechanism nests. The chain is bounded by a depth
   limit and a seen-set; a cycle, a missing layout or a layout that does not compile names the layout
   in the error rather than serving the content as an orphan fragment.
5. One request keeps one render slot, one deadline, one fetch budget, one body cap and one
   antiforgery token for the whole chain. Each layer gets its own `Path` — and therefore its own
   storage handle — but the same `Request`: `ctx.Path` is the layer's key, and `ctx.Request.Path` is
   the page the visitor asked for.
6. A layout is presentation, not a guard. The content has already run by the time the layout starts,
   so authorization stays in the page (DEC-02); a layout can hide chrome, not prevent a side effect.
   A redirect or a non-HTML content type ends the chain, so a page that answers with CSS or a `302` is
   never wrapped.

`SetLayout` is a response setting, like `SetStatus`, rather than a new template directive. It needs
no tokenizer or generator change, it works when a layout chains, and the page that is actually
rendering chooses it. The cost is that a wrong layout path is a render-time error rather than a
save-time one; the error names the layout, and the editor is still a shell view, so it stays
recoverable. A directive that gives the common case a static, validated default can follow if that
trade stops being worth it.

### What moves to the SDK, and what does not

7. IDEA-018 is the other half. The document builder (`PageDocument`) and the fragments a layout wants
   (`Nav`, `NavLink`) move to `Sdk`, and the shell's `ShellDocument` becomes a thin adapter over the
   same builder. The shell and the seeded layout then write one document, and the head, the landmark,
   the sheet order and the encoding live once. The builder carries no policy: status, headers, CSP and
   caching stay the shell's.
8. The SDK is for the stable framework; the layout page is for the editable chrome. A page-authored
   library is not the answer for either.
9. A seeded `/_layout` page uses the builder. Putting a nav before `main` reopens DEC-121 — the skip
   link comes back with the navigation it would skip — so keep the first seed to the document shell
   and a footer if that decision is not ready.

### Shared types, if they are still needed

10. The original three options remain for code, not markup: a library page compiled into every
    dependent page, a shared assembly in the same `PageLoadContext`, or the SDK. Prefer the SDK for
    anything stable. A library page needs a dependency order and an invalidation story, and it makes
    every dependent page's image depend on the library's source — the same recompile storm the layout
    avoids by being rendered, not compiled in. Do not build it before a page-authored helper actually
    needs it.

The API policy still applies to page-declared types: a `class` in a page must not become a way to
reach `System.Net`. `IsDeclaredIn` treats same-tree symbols as page code, and the members such a type
names still go through `PageApiPolicy.Check`; that needs a test when a library page is built.

## Status

Phase 1 is done, by [page declarations](../plans/archive/page-declarations.md) and DEC-92: a page declares
`using` directives and types in `<%! %>` blocks, uses them from its `<% %>` blocks, and saves and
serves like any other page. No storage, schema or `RuntimeId` decision moved — the generator lives
in `Core`, whose module version id was already in the id — and the API policy applies to a declared
type exactly as it does to a code block, so writing a class is not a way to reach `System.IO`.

Phase 2 is now the shared layout, not shared types, and it is being replanned. The layout is the
first capability to arrive through the page-features change (`IContext` keeps the request, response
and logging core; storage, fetch and the layout's own slots become capabilities a page asks for), so
its plan waits until that has bedded in. It stays best done after IDEA-018, because the layout page
calls the builder that job moves, and it is still the first capability the listing and history views
(IDEA-015, IDEA-017) cannot move without; shared types stay deferred until a concrete helper needs
them.

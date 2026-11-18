<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page composes another page

| | |
| --- | --- |
| Status | Merged 2026-11-16 — built on `idea-049` and merged into trunk with `make check` green. The shell trim D14 proposed was dropped at the merge in favour of trunk's one table of routes |
| Size | Large — one new `Sdk` capability with four calls and two values, `IPageLayout` loses `Layout` and gains a nullable `Title`, one layer renderer with three roles, the layout chain rebuilt on it and entered by the page, ten seeded pages and the editor's starter, the manual, the assistant's prompt and its version, two configuration values, one instrument, and the tests that pin the recursion, the response gates and the chrome |
| Area | Sdk, Web (pages, shell, seeds, assistant, metrics), docs |
| Spike | None. Every count below was taken off `trunk` at `6004526` on 2026-11-16, after `pages-the-response-is-one-object` merged |
| Backlog | [IDEA-049](../jobs/closed.md) is this step, closed by the merge. [IDEA-047](../jobs/IDEA-047.md) is the answer if a real page needs more than one write in one render, which composition makes likelier |
| Decisions | New names proposed: **pages-an-embed-is-a-render**, **pages-a-composed-page-gets-a-request**, **pages-a-capture-returns-the-body**, **pages-a-page-inside-a-page-is-a-capture**, **pages-a-page-chooses-its-layout**, **pages-one-render-three-roles**, **pages-the-wrapper-owns-the-title**. Edits `pages-one-request-one-chain`, `pages-a-layout-is-a-capability`, `pages-layouts-are-markup`, `pages-one-base-sheet` and `pages-capabilities-are-one-seam`. The shell trim D14 proposed is dropped: trunk unified the menus into one route table first, so `shell-the-menu-is-written-twice` stands as trunk edits it. Relies on `pages-the-response-is-one-object`, `pages-the-contract-is-the-seam`, `pages-public-by-default`, `pages-a-warning-never-stops-a-page`, `pages-page-io-is-async`, `pages-any-page-may-write-the-store`, `pages-a-page-may-save-itself`, `metrics-one-panel-per-instrument` and `code-policy-values-are-configuration` |

## The short version

A stored page can read another page's **source** (`IPageCatalog`) but not its **output**. So every
view that wants "this page, here" writes the markup again. This change adds the missing primitive and
uses it to remove the last special case in the page runtime: the layout chain stops being a thing the
host does to every page and becomes a thing a page asks for.

**Four calls, one capability.** `IPageComposition` is offered to every layer a render has:

```csharp
Task<PageFrame> EmbedAsync(string key, PageRequest? request = null); // include it, no body back
Task<PageCapture> CaptureAsync(string key, PageRequest? request = null);    // …or the body too
void Wrap(string key, PageRequest? request = null);        // each call adds a layer; first is innermost
void Wrap();                                               // …the site's layout
```

`EmbedAsync` renders another stored page with the request it is given — the caller's when it is
`null` — **exactly as a visit does, wrappers and all**, writes the outermost body where the call is
and returns the outermost `PageFrame` (title, head, sheets, script permission; never the body).
`CaptureAsync` returns the `PageCapture`, which is the same frame with the body. `Wrap` records a
page that will wrap this one, and the request it runs against; each call adds a layer outside the
last, so the first is the innermost and the last is the outermost layer the page names — a wrapper
may add its own outside it. The host runs the chain after `HandleAsync` returns, exactly where it
runs today, so a wrap declared near the top covers every exit path including a guard's early return. `Wrap` is not `WrapAsync`: it does not await the wrapper,
the host does.

**The input is the request.** `PageRequestExtensions` adds `WithQuery`/`WithAnotherQuery`/
`WithoutQuery` as extension methods on `PageRequest`, so `ctx.Request.WithQuery("id", "7")` is the
copy-and-change path and there is no separate options type. The record keeps its shape; the helpers
are an additive surface.

**The outer page chooses.** Every inner render yields a `PageFrame`; `EmbedAsync` returns the
outermost one, `CaptureAsync` returns it with the body, and `/b` calling `Wrap("/a")` hands the
content's `PageCapture` to `/a` as `IPageLayout.Content`. Neither direction merges anything behind the outer
page's back: the page that owns the document decides what to fold, and that is the whole symmetry
between the two calls.

**The role replaces the flag.** The response rule is currently `isLayout` on `SimpleResponse`, and that
one flag decides two different things: who owns the response and what capabilities the layer gets.
It becomes a role — **content**, **layout**, **embedded**. Content owns the response; layout and
embedded withhold it; embedded keeps the capabilities a content page has, including the write and the
jobs, because the authority is the caller's identity, and the embedded page's own wraps run like any
other page's.

**The shell's documents keep their menu.** D14 was written when the shell's menu and the stored
layout's were two hand-written lists. Trunk has since unified the shell's routes into one table
(`Shell/ShellCatalogue.cs`) that draws the site menu and the administration menu from the same
values, which is a better answer to the same duplication, so this step leaves the shell's documents
alone and `shell-the-menu-is-written-twice` stands as trunk edits it.

**Nothing wraps automatically.** A page that does not call `Wrap` is served exactly as it wrote
itself. That is the point and the migration: the seeds and the editor's starter carry the line,
`/help` says why, and a page that forgets is a bare fragment rather than a diagnosis.

## Goals

1. A page can include another stored page's output, or capture it as a string, with the request that
   is its input, under the same budgets, identity and capabilities.
2. A page chooses the page or pages that wrap it, in order; `IPageLayout.Layout` and the host's
   automatic wrap are gone, and the chain still renders content first and writes the outermost
   layer. A page with no `Wrap` is a fragment; a page with one is a document wherever it is rendered.
3. The three layer roles are one rule, not two booleans: the response gates, the stream refusals, the
   write/job/session offers and the layout rule all read the same value.
4. Recursion is bounded and honest: the same page with the same request in an ancestor is a cycle and
   refused; the same page with a different request is a recursive component, allowed, capped and
   counted; fan-out is capped.
5. A no-code page embeds as the fragment it is, with its comments removed, because the projection
   moves from the shell's serving path into the renderer.
6. Nothing in the shell changes: the shell's documents keep the menu trunk draws from its one route table.
7. `/a EmbedAsync(/b)` and `/b Wrap(/a)` are the same relation from opposite ends: both hand the
   outer page a `PageCapture` and let it choose what to fold.
8. The manual, the prompt, the docs and every seeded page say the new thing in the same change.

## Non-goals

- **A slot API beyond `Body`.** A wrapper can embed its own partials around `Body`; named regions are a
  different contract.
- **A general `Items` bag on `IContext`.** The request is the input in both directions; an untyped
  bag is a second, hidden channel, and structured state already has `IPageStorage` and `IPageSession`.
- **A failure value or an error boundary.** `EmbedAsync` throws and a page may catch it. A result value
  that makes ignoring a failure the default is not this step.
- **Moving `/status` or `/assistant` off the shell.** Both stay shell documents for now; the health
  surface must answer when the page pipeline is broken.
- **Batch writes.** One render still writes once across the whole chain. [IDEA-047](../jobs/IDEA-047.md)
  is where several writes with a named policy land.
- **Caching or memoising an embed.** It is part of the page it appears on, and the page is `no-store`.
- **A per-call embed boundary, a partial view of a page, or an embed flag.** An embed is the page's
  whole render, wrappers and all; a view that should insert a fragment is a page that declares no
  wrapper.

## What exists today

| Surface | Says | Where |
| --- | --- | --- |
| `IPageLayout` | seven members: `Layout`, `Title`, `Head`, `Stylesheets`, `AddHead`, `AddStylesheet`, `Body`; `Layout` is pre-filled with `Pages:Layout` and `Title` throws until set | [IPageLayout.cs](../../src/uk.osric.asp.Sdk/IPageLayout.cs) |
| `PageLayoutState` | implements it; `TitleOrNull` carries the unset state internally | [PageLayoutState.cs](../../src/uk.osric.asp.Web/Pages/PageLayoutState.cs) |
| The chain | `PageChain.WrapAsync` reads `contentLayout.Layout`, refuses a missing title before the first layout runs, bounds the chain with `MaxLayoutDepth` and a seen-set, and falls back to serving unwrapped only for the default | [PageChain.cs](../../src/uk.osric.asp.Web/Pages/PageChain.cs), 36 tests in [PageLayoutChainTests](../../test/uk.osric.asp.Web.Tests/PageLayoutChainTests.cs) |
| The role | `isLayout` on `SimpleResponse` and `SimpleContext` decides the response, the stream and the capabilities | [SimpleResponse.cs](../../src/uk.osric.asp.Web/Pages/SimpleResponse.cs), [SimpleContext.cs](../../src/uk.osric.asp.Web/Pages/SimpleContext.cs) |
| The write | `PageWritesScope` owns its own `_acted`, which is one action **per layer**; today there is one content layer, so it reads as one per render | [PageWritesScope.cs](../../src/uk.osric.asp.Web/Pages/PageWritesScope.cs) |
| No-code bodies | the renderer writes the stored source; `PageEndpoint.WriteSourceAsync` is the only place that removes `<%# … %>` | [PageEndpoint.cs](../../src/uk.osric.asp.Web/Pages/PageEndpoint.cs) |
| Seeded pages | ten pages with code; 18 `IPageLayout` uses in ten files; `layout.Layout` is named only in `/help`'s prose and streaming snippet | `src/uk.osric.asp.Web/Pages/Seeds/` |
| The shell | `SiteNavigation.Links` writes home, sign-in or assistant/pages/admin/sign-out, and a footer; `SiteChromeTests` asserts the shell and the seeded layout offer the same items | [SiteNavigation.cs](../../src/uk.osric.asp.Web/Html/SiteNavigation.cs), [SiteChromeTests.cs](../../test/uk.osric.asp.Web.IntegrationTests/SiteChromeTests.cs) |
| Configuration | `Pages:Layout` (required, blank means none) and `Pages:Render:MaxLayoutDepth` | [PageLayoutOptions.cs](../../src/uk.osric.asp.Web/Pages/PageLayoutOptions.cs), [RenderOptions.cs](../../src/uk.osric.asp.Web/Pages/RenderOptions.cs) |
| The prompt | `page-author-v35`, a reflective capability section and 47 `ctx.` mentions | [PageAuthorPrompt.cs](../../src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs) |
| Metrics | 18 instruments, one panel each; `page.layout.fallback` is the only chain instrument | [PageMetrics.cs](../../src/uk.osric.asp.Web/Pages/PageMetrics.cs), [StatusCatalogue.cs](../../src/uk.osric.asp.Web/Metrics/StatusCatalogue.cs) |
| Docs | `IPageLayout` is named 22 times outside the archive; the capability count is eleven | [pages.md](../pages.md), [shell.md](../shell.md) |

## The design

### D1 — `IPageComposition` is one capability with four calls

```csharp
// Sdk/IPageComposition.cs
public interface IPageComposition {
    Task<PageFrame> EmbedAsync(string key, PageRequest? request = null);
    Task<PageCapture> CaptureAsync(string key, PageRequest? request = null);
    void Wrap(string key, PageRequest? request = null);
    void Wrap();
}
```

It is registered on every layer — content, layout and embedded — because a layout embedding a header
and footer partial is the use that argues for this most, and because a page must be able to call
`Wrap` without knowing it is embedded; an embedded page's own chain runs. It is a capability rather than context members for the reason
`pages-capabilities-are-one-seam` gives: a page that never composes never meets it.

`EmbedAsync` includes another page **now**. It renders the page as a visit does — body, then every
wrapper the page declared, in order — into a bounded capture, writes the **outermost** body through
the calling layer's `IPageResponse` and returns the outermost `PageFrame` — title, head, sheets and
script permission, **never the body**, because the body is already in the caller's document. The
body allowance is charged once and a failure leaves nothing half-written in the caller.
`CaptureAsync` is the same render with the write left out (D8).

`Wrap` is a **declaration**: each call records a layer, in order, and the host runs the chain after
`HandleAsync` returns. The first call is the innermost; each later call wraps the result of the
previous. That is what keeps a guard's early `return` wrapped and lets the page set its
status after it has written the body. A page that wants the literal handover would need `WrapAsync`
and a rule that every exit path reaches it; that trap is rejected in
[Considered and rejected](#considered-and-rejected).

Refusals at the call:

| Call | Refusal |
| --- | --- |
| `Wrap` after the first flush | `InvalidOperationException`: a committed body cannot be handed over |
| `Wrap(key)` with a malformed key | `ArgumentException` |
| `Wrap(key)` naming a reserved key | `InvalidOperationException`, as a reserved key is a page bug |

`Wrap()` uses `Pages:Layout` and the content's request. When the configuration is blank, it records
nothing and that layer is skipped; the rest of the chain still runs.

### D2 — The input is a `PageRequest`, not an options type

```csharp
// Sdk/PageRequestExtensions.cs — the copy-and-change helpers
public static class PageRequestExtensions {
    public static PageRequest WithQuery(this PageRequest request, string name, string value);
    public static PageRequest WithQuery(this PageRequest request, IReadOnlyList<KeyValuePair<string, string>> query);
    public static PageRequest WithAnotherQuery(this PageRequest request, string name, string value);
    public static PageRequest WithoutQuery(this PageRequest request, string name);
    public static PageRequest WithoutQuery(this PageRequest request);
}
```

The helpers are **extensions, not members**: `PageRequest` is a sealed record, and the copy-and-change
surface is additive — the record keeps its shape, and its own surface stays the values it carries.
Each validates `request` and the name or value with the same checks a member would have had, and each
carries XML docs, because `Sdk` emits its documentation.

- `EmbedAsync(key)` runs the caller's request untouched.
- `EmbedAsync(key, request)` runs exactly that request; **`Path` is pinned** to the caller's
  `Request.Path`, because it is the page the visitor asked for, not an option.
- `ctx.Request.WithQuery("id", "7")` replaces every value for a name;
  `WithAnotherQuery` adds one beside the existing values, the repeated name `QueryValues` already
  models; `WithoutQuery(name)` drops a name and `WithoutQuery()` clears the query.
- `WithQuery(list)` replaces the whole query in the order given; `Query` (first per name) and
  `QueryValues` (every value in order) are rebuilt together, so the two views cannot disagree.

There is no separate options type: the page's input is the request, and the same request is the input
to a wrapper and to an embed. The extensions are the only addition to the request's surface: the
request is a value and the helpers are conveniences over it, so the value stays the contract. The caller may shape the method, form, headers and cookies as well as
the query, because they are all the page's real input; only `Path` is the host's. `ctx.Path` is still
the embedded page's key, so `ctx.Path != ctx.Request.Path` is how a layer knows it is inside another,
the same signal a layout already gives. The two manual warnings fall out of that and are in D4.

### D3 — The layer role replaces `isLayout`

```csharp
// Web/Pages/PageLayerRole.cs, internal
internal enum PageLayerRole { Content, Layout, Embedded }
```

| Question | Content | Layout | Embedded |
| --- | --- | --- | --- |
| Owns the response (`SetStatus`, `SetHeader`, `SetContentType`, `Redirect`) | yes | no: ignored and logged | no: ignored and logged, except a redirect, which throws |
| May flush | yes, if no wrapper is declared | no | no |
| Offered `IPageWrites` and `IPageJobs` | yes | no | yes |
| Session may write | yes, unless `HEAD` | no | yes, unless `HEAD` |
| `Wrap` | runs | runs | runs |
| `IPageLayout` | own slots; `Content` null | own slots; `Content` is the content's capture | own slots; `Content` null |
| `AntiforgeryField` | the request's pair | the request's pair | the request's pair |

The redirect exception is deliberate: ignoring it would make the ordinary "if no user, redirect to
login" guard vanish into an empty box. Every other setting is a logged no-op, because a layout or an
embed cannot change the document the visitor asked for.

`SimpleResponse` takes the role where it takes `isLayout` today; `IgnoreAsLayout` becomes
`IgnoreWhenNotOwning`, and `ThrowIfCannotStream` refuses both layout and embedded. `SimpleContext`
takes the role and passes it on. `PageChain.CreateLayer(..., PageLayerRole role)` replaces the boolean,
and the capability registration uses `role != PageLayerRole.Layout` for the write and the jobs and the
session's writability.

### D4 — One layer renderer, and the wrap is the policy over it

`PageChain.CreateLayer` and `RenderSession.RenderAsync` already are the one way a page is run. This
change makes every layer use them. A render is a **tree**: the page's body and the wrappers around it
are one spine, and every layer may embed, which is a child render with a spine of its own. The host
walks it by making the calls; the ancestor stack is the path down it:

- **Content**: the endpoint renders the requested key with the caller's request.
- **Layout**: `PageChain` renders each declared wrapper with the previous layer's `PageCapture` as
  `IPageLayout.Content`.
- **Embedded**: `PageCompositionScope` renders the named key with the derived request and then runs
  the page's declared wraps through the same loop, so the embed's body and frame are the outermost
  layer's. There is no exception for an embedded layer: it renders the page the same way a visit
  does, and a page that should be a fragment declares no wrapper.

`PageChain` grows the layer loop the endpoint's `WrapAsync` call makes today, but starts from the
content's declared wrappers instead of `contentLayout.Layout`:

```
request /about
  → /about renders into buffer A, sets Title/head/sheets, calls Wrap()
  → /_layout renders into buffer B with IPageLayout.Content = the capture of A
  → /_layout may call Wrap("/_outer") → buffer C with Content = the capture of B
  → write the outermost buffer

request /page embedding /about
  → /page renders into A; EmbedAsync("/about") runs the same loop:
      → /about renders into B, calls Wrap("/_md")
      → /_md renders into C with Content = the capture of B
      → the embed writes C into A and returns /_md's frame
  → /page may itself be wrapped; the chain continues from its body
```

`PageChain` owns the ancestor stack, the depth and count counters and the default fallback; each layer
owns its own slots and each render produces a `PageCapture` — body, title, head, sheets and script
permission — that the layer outside it consumes. The one thing the host still composes is the script
permission: a layer's capture carries its own `AllowScripts` or the content's, so a document that
carries a body cannot lose the permission its script needs, and the outermost capture's value is the
response's.

`PageChain.CreateLayer` returns the layer it built — the context, the composition state the page
writes its `Wrap` into, and the slots — so the endpoint can hand the content's declaration to the
chain after the render, and the chain can read each layout's declaration in turn.

The endpoint's order changes by one step: it opens the render session **before** it builds the content
layer, so the composition scope holds the session every embedded render runs under. One slot and one
deadline still cover the whole request.

### D5 — The wrapper folds the content's capture; it does not get the response

The wrapper is a page with code. `IPageLayout.Content` is the content's `PageCapture` — `Body`,
`Title`, `Head`, `Stylesheets` and `AllowsScripts` — and the wrapper decides what to fold into its own
`Title`, `AddHead` and `AddStylesheet`. It may embed partials of its own around `Content.Body`. Its own slots become its frame, and the host
pairs that with its body as the capture for its parent, so the rule is the same one `EmbedAsync`
gives an embedding page: **the page that owns the document chooses**.

The request is how a content page picks a wrapper variant without a second wrapper page —
`Wrap("/_layout", ctx.Request.WithQuery("theme", "dark"))` — and the wrapper reads it as any page
reads its query. Nothing is merged behind its back. The seeded `/_layout` folds
`Content.Title ?? "Site"`, the content's sheets and head beside its own; the `Site.Document` library
gets the merge helper so that stays one call. Its own `SetStatus`, `SetHeader`, `SetContentType` and `Redirect` are ignored and logged,
because the content owns the response. The content's `AllowsScripts` is unioned automatically, because
the wrapper may carry its body, and the wrapper's own `AllowScripts` is unioned outward.

A wrapper must have code: a no-code page has no way to read `Content`, so its stored source would
replace the page instead of wrapping it. `Wrap` naming a library, a worker, a no-code page, a missing
page or a deleted one fails when the chain tries to render it, with a message naming the key.

The default wrapper keeps the fallback: `Wrap()` records the configured default, so a default that
cannot be served is skipped with a warning and `page.layout.fallback`, and the rest of the chain
still runs.
`Wrap(key)` is a named wrapper, so a failure is an error naming the key.

### D6 — `IPageLayout` loses `Layout` and `Body`, gains `Content`

```csharp
public interface IPageLayout {
    PageCapture? Content { get; }        // the layer inside; null on the content and embedded layers
    string? Title { get; set; }          // this layer's own title; null until set
    string Head { get; }                 // this layer's own head markup
    IReadOnlyList<string> Stylesheets { get; }
    void AddHead(string markup);
    void AddStylesheet(string href);
}
```

`Layout` goes: the page declares its wrapper through `IPageComposition.Wrap`. `Body` goes: the wrapper
reads `Content.Body`. The remaining slots are the layer's **own**, and the host no longer composes them
with the inner layer's. `PageLayoutState` loses its `defaultLayout` constructor argument, its `Layout`,
its inner-layer composition and `TitleOrNull`; it is a plain holder of the layer's own slots and the
`Content` the host sets. The setter still refuses blank, so the only way to have no title is to leave
it unset.

**The title check is the wrapper's.** The host's pre-chain refusal and its "a wrapped page must set a
title" error go. The wrapper reads `Content.Title` and decides; the seeded `/_layout` uses
`Content.Title ?? "Site"`; a wrapper that wants to refuse instead can throw, naming `ctx.Request.Path`.

This is a public-API change: `Title` is `string?`, `Body` is gone and `Content` is new. The seeds are
updated; `RuntimeId` moves, so every stored page recompiles and a page that used the old members
reports diagnostics rather than serving stale markup.

### D7 — The embedded layer: request, slots, capabilities, output

`PageCompositionScope.EmbedAsync` does the following, in order:

1. **Resolve** the key with `PagePath.Normalize`; a malformed key is an `ArgumentException`; a reserved
   key is an `InvalidOperationException`; the key must be a page the renderer can serve.
2. **Check recursion** (D9) before anything is rendered.
3. **Take the request**: the one passed to the call, or the caller's when none was; `Path` is pinned
   to the caller's `Request.Path`, so it stays the page the visitor asked for.
4. **Create the layer**: a fresh `PageLayoutState` (no content), a `StringWriter`, a fresh commit state,
   `role: Embedded`, the embedded key as the context's `Path`, the caller's user, login redirect,
   antiforgery factory, asset map and abort token, and a **bounded body capture** (D10). The body sink is
   `null`, so the embedded layer cannot stream. Its own `Wrap` calls are recorded as its chain.
5. **Render** it through the session: `session.RenderAsync(key, context)`, then run the page's declared
   wraps through the chain loop (D4); the result is the outermost capture.
6. **Build the frame**: the outermost layer's `Title`, `Head`, `Stylesheets` and `AllowsScripts`, as a
   `PageFrame`; a capture is that frame paired with the body, `new PageCapture(body, frame)`.
7. **Write and return**: `EmbedAsync` writes the outermost body through the caller's `IPageResponse.WriteRaw` —
   charging the shared body allowance once — unions the captured page's script permission, and returns the
   **frame**, never the body. `CaptureAsync` returns the **capture**, body included, without the
   write or the union (D8). The frame and the body are the outermost layer's.

Neither call touches the caller's `IPageLayout`: a caller that wants the embedded page's title, head
or sheets folds them itself, with `AddHead` and `AddStylesheet` on its own slots. That is the same
choice a wrapper makes with `IPageLayout.Content`, which is the symmetry the two directions share.

The embedded page keeps the capabilities a content page has: `IPageStorage` and `IPageSession` bound to
its own key, `IHttpFetch`, `IPageCatalog`, `IPageLayout`, the two renderers, `IPageJobs`,
`IPageWrites` and `IPageComposition`, so it can call `Wrap` like any page and the chain runs as it
would on a visit. The write shares the **render's one action** (D10), so two
instances of a writer cannot both write in one request.

A no-code page embeds as its stored source less its comments; that projection moves into the renderer
in D12. A library or a worker is not a page: it is refused with a `UserFacingException` naming the key.

### D8 — Capture is the same render, returned

`CaptureAsync` takes the same key and request as `EmbedAsync` and shares its whole codepath: the
resolution, the recursion check, the request, the page's own wrappers, the content capabilities, the
withheld response, the one write and the failure table. The difference is the last step. `EmbedAsync` writes the outermost body,
unions the captured page's script permission, and returns the frame;
`CaptureAsync` returns the capture and touches nothing:

```csharp
// Sdk/PageFrame.cs
public record PageFrame(
    string? Title,
    string Head,
    IReadOnlyList<string> Stylesheets,
    bool AllowsScripts);

// Sdk/PageCapture.cs
public sealed record PageCapture : PageFrame {
    public PageCapture(string body, PageFrame frame)
        : base(frame.Title, frame.Head, frame.Stylesheets, frame.AllowsScripts) => Body = body;

    public string Body { get; }
}
```

`EmbedAsync` is the **frame** plus the write and the script union; the body is the only thing the
capture adds, and no value gives a body to a caller that could not use it. The head
and sheet fold is the caller's choice, made with the value it receives. The two calls have one render
and one set of rules. The captured page gets a **clean response** of its own — a fresh `SimpleResponse`
over the capture — so its `Write` and `WriteRaw` land in the body and its status, content type,
headers and `AllowScripts` never reach the caller except as the frame's `AllowsScripts`. A
redirect still refuses, because a captured page has no response to redirect.

The result is deliberately **a value, not the live `IPageLayout`**. `IPageLayout` is the capability a
layer runs with: its `Content` is the layer *inside* it, and its own `Title`, `Head` and `Stylesheets`
are what this layer contributes outward. A frame or a capture is neither — it is the finished
material of one layer, which is why it can be carried, transformed or discarded. The response itself
stays withheld.

This is the page-level twin of the wrapper's `IPageLayout.Content`: the prod site's `/_md` layout
already receives its content as a capture, and a page that wants the same view without being wrapped —
or wants to transform it first — is what capture is for:

```html
<% var captured = await ctx.Capabilities.GetRequired<IPageComposition>().CaptureAsync("/draft");
   foreach (string sheet in captured.Stylesheets) { layout.AddStylesheet(sheet); }
   if (captured.AllowsScripts) { ctx.Response.AllowScripts(); }
   ctx.Response.WriteRaw(ctx.Capabilities.GetRequired<IMarkdownRenderer>().ToHtml(captured.Body)); %>
```

A capture counts as an embed for the ancestor stack, the depth, the fan-out cap and the recursive
counter. It is bounded by the caller's remaining body allowance, but the returned body is charged to
the shared allowance only when (and if) the caller writes it, so a caller that transforms the body is
charged for what it wrote. A caller that captures and discards it holds one bounded copy per call,
which the fan-out cap and the deadline bound.

### D9 — An ancestor stack keyed by the page and its query

`PageChain` owns a stack of `PageRenderIdentity` values, where the identity is the canonical key and a
fingerprint of the query (the ordered `QueryValues` pairs). The content layer is seeded onto it; every
layout, embed and capture pushes itself for the duration of its render and pops after.

- **Same identity in the stack** → `InvalidOperationException` naming the chain. `/foo` embedding
  `/foo` is an infinite loop; `/foo` wrapping the page it wraps is the same.
- **An embed's whole chain is on the stack.** The embedded page's body and every wrapper it declares
  are layers on the same path, so a page that embeds a page whose chain contains the embedder or an
  ancestor is a cycle, and a page that wraps the layout that embeds it is the same.
- **Same key, different identity** → allowed. `/foo?q=1` embedding `/foo?q=2` is a recursive component
  — a comment tree, a directory — and the caps are its bound. It records `page.composition.recursive` and
  logs the chain at Debug, so the one page doing it is visible without making the ordinary case noisy.
- **Repeated at one level** → allowed, because the stack pops. A listing may embed the same card
  twenty times.
- **Depth** — `Pages:Render:MaxEmbedDepth` (shipped 4, accepted 1–16) bounds nested embeds below the
  layer that is running. It is also the real bound on data-driven recursion, where every level passes
  different requests.
- **Fan-out** — `Pages:Render:MaxEmbeds` (shipped 32, accepted 1–256) bounds the embeds in one request.
  Depth alone lets a listing embed a thousand siblings under one slot and one deadline.

`MaxLayoutDepth` stays as it is for wraps; the two depths are chosen independently because a widget is
not chrome.

The fingerprint is a pure function over the query pairs, so it lives in `Core.Pages` with its own
tests: two orders of names that a browser would send differently must not collide, and a repeated name
keeps its order.

### D10 — Output, budgets and the one write

- **The capture is bounded.** `BodyBudget` gains `Remaining`; an embedded layer renders into a
  `BodyBudget` sized to the caller's remaining allowance, and its wrappers render inside the same
  bounded chain; the final body is what must fit. If the embed overruns, it fails before anything
  reaches the caller. On success the caller's `WriteRaw` charges the shared budget once, which
  is why the embedded text is not counted twice and cannot be smuggled past the cap.
- **A returned capture is charged when it lands.** `CaptureAsync` renders under the same bounded view
  but does not write, so the returned string is charged to the shared allowance only when the caller
  writes it. A caller that transforms the body is charged for the transformed text; one that discards
  it holds one bounded copy per call, which `MaxEmbeds` and the deadline bound.
- **One write per render.** `PageWritesScope` keeps its key binding but no longer owns its `_acted`; a
  `PageWriteAction` created by the chain is shared by the content layer and every embedded layer. A
  listing that embeds a self-saving widget therefore refuses the second instance, exactly as two saves
  in one page do today.
- **Jobs are per call.** `IPageJobs` remains per layer; an embed that starts one starts one per call,
  bounded by the queue as today. The manual says so.
- **The stream budget.** An embed inside a page that has already flushed is written through the
  committed response and falls under `Pages:Stream`, not the body budget; the embedded page itself
  never flushes.

### D11 — Failure is a throw the page may catch

| Case | Refusal |
| --- | --- |
| Malformed key | `ArgumentException` |
| Missing, deleted, library, worker, no-code wrapper | `UserFacingException` naming the key and why (a reserved key is refused at the call, D1) |
| A redirect from an embedded page | `UserFacingException` naming the embed and the target |
| A compile failure | `UserFacingException` naming the key; the diagnostics stay in the editor |
| A cycle, a cap, `Wrap` after a flush | `InvalidOperationException` naming the chain or the setting |
| A timed-out embedded render | the render's cancellation, so the request answers `504` rather than a page fault |

A page may catch any of these: the embedded output never reached the caller's body, so the catch is
clean. What does not roll back is a side effect the embedded page already committed (a storage write, a
session write, a started job) and the body allowance it spent on the way.

### D12 — The no-code projection moves into the renderer

`PageRenderer.RenderLayerAsync`'s text branch writes `TemplateComments.Remove(source)` instead of the
source, and `PageEndpoint.WriteSourceAsync` stops removing them because the body it receives is already
the projection. An embed therefore cannot be a way for a `<%# … %>` to reach a visitor, and there is
still one place where a stored source becomes a body.

### D13 — Configuration and the instrument

- `Pages:Render:MaxEmbedDepth` and `Pages:Render:MaxEmbeds` are policy values: a `RenderOptions`
  member read through `ConfigurationReader`, the shipped value in `appsettings.json`, and a test for
  the shipped value, an override and a refusal.
- `page.composition.recursive` is a counter in `PageMetrics`, one panel in `StatusCatalogue`
  ("Recursive composition", a line chart by `page_path`), one row in the metrics table, and one call
  in `MetricNamesTests`. Both `EmbedAsync` and `CaptureAsync` count. The Pages panel count in `docs/metrics.md` moves from nineteen to twenty.

### D14 — The shell is left alone

This was to trim the shell's documents to one way home. Trunk got there first with a better answer:
`ShellCatalogue` is one table of the site's routes, and the shell's site menu and the administration
menu are both filtered from it, so the duplication D14 was aimed at is gone without taking the menu
away from a visitor who lands on `/status` or an error page. The owner chose the trunk design at the
merge; `SiteNavigation`, `ShellDocument`, their tests and `shell-the-menu-is-written-twice` are
trunk's, and this step adds no shell rule.

`TheShellAndTheLayoutOfferTheSameItems` stays, because the two menus are still expected to agree.

### D15 — The record

> **pages-an-embed-is-a-render** — an embedded page is a render of the whole page: its body, every
> wrapper it declares, its own key, storage, session and capabilities, the caller's identity, one
> budget and one slot, and no response. There is no embed-specific wrap suppression; a page that
> should be a fragment declares no wrapper.
>
> **pages-a-composed-page-gets-a-request** — an embed, a capture and a wrap all run the `PageRequest`
> they are given, with `null` meaning the caller's; `Path` is the visitor's page, and the request's
> query is the page's input. There is no separate options type; the copy-and-change helpers are
> extension methods on the request, so the sealed record keeps its shape.
>
> **pages-a-capture-returns-the-body** — an embed returns a `PageFrame` (title, head, sheets,
> script permission; never the body, which is already written), and a capture returns a `PageCapture`:
> the frame beside the body. The captured page's response stays withheld, so a capture that
> would redirect refuses.
>
> **pages-a-page-inside-a-page-is-a-capture** — a page rendered inside another yields a value — a
> `PageCapture` for a wrapper, a `PageFrame` for an embed — and the outer page folds what it
> wants, whether it asked for the inner with `EmbedAsync` or received it with `Wrap`.
>
> **pages-a-page-chooses-its-layout** — a page declares the page or pages that wrap it, innermost
> first; the host does not wrap by default, and `IPageLayout.Layout` is gone.
>
> **pages-one-render-three-roles** — content, layout and embedded are one role on the response and the
> capability offer, not a boolean and a special case.
>
> **pages-the-wrapper-owns-the-title** — `IPageLayout.Title` is nullable and the wrapper decides what a
> missing title means; the host no longer refuses the chain.

Edits: `pages-one-request-one-chain` (the chain is entered by the page and still one render),
`pages-a-layout-is-a-capability` (the slots remain; `Layout` leaves), `pages-layouts-are-markup` (the
wrapper is the layout role), `pages-one-base-sheet` (an embed's sheets are the caller's to fold, and the document library keeps
the cascade order and the dedupe), and
`pages-capabilities-are-one-seam` (the twelfth capability). `shell-the-menu-is-written-twice` is left
as trunk edits it, because the shell's menu did not change.

## What it touches

`Sdk`: `IPageComposition.cs`, `PageFrame.cs`, `PageCapture.cs` and `PageRequestExtensions.cs` new;
`PageRequest.cs` is unchanged; `IPageLayout.cs` loses `Layout` and `Body`, gains `Content` and its
`Title` becomes nullable; `PublicAPI.Unshipped.txt` gains the extension class and its methods.

`Core`: the query fingerprint for the ancestor stack.

`Web`: `PageCompositionScope.cs`, `PageCompositionState.cs`, `PageLayerRole.cs` and
`PageWriteAction.cs` new; `PageChain.cs` (the loop, the stack, the caps), `PageEndpoint.cs` (the
session order, the wrap), `PageRenderer.cs` (the comment projection), `PageLayoutState.cs`,
`PageLayoutOptions.cs` (documentation only), `SimpleContext.cs`, `SimpleResponse.cs`,
`PageWritesScope.cs`, `BodyBudget.cs`, `RenderOptions.cs`, `PageMetrics.cs`, `StatusCatalogue.cs`,
`appsettings.json`, and the seeds (`_layout`, `_lib/document`, `about`, `editor-mode`, `help`, `pages`,
`privacy`, `vim-help`). The document library gains the overload that folds a `PageCapture` and the
layer's own slots into one head, so the seeded `_layout` stays a short page; the editor's empty page
starts from a one-line fragment that calls `Wrap()`, so a page created in the editor is wrapped
without the author knowing the rule. `/admin`, `/recent` and `/scratch` left the seeds in trunk's own
work before this landed, and the pages this step wraps are the ones still shipped.

`Assistant`: `PageAuthorPrompt.cs` — the capability section, examples, the composition rules, and the
version (`page-author-v35` → `v36`).

Tests: `PageLayoutChainTests` (retitled and extended where the wrap changed), new
`PageCompositionTests`, `PageCaptureTests`, `PageRequestQueryTests` and `PageLayerRoleTests` in
`Web.Tests`; `SimpleResponseTests`, `PageEndpointTests`, `PageWritesScopeTests` and
`PageStreamingTests` gain the role and the one-write sharing; `PageAuthorPromptTests`; `SiteChromeTests`,
`SiteLayoutEditingTests` and the pages the integration suite saves gain the `Wrap()` they now need;
`MetricNamesTests`; `TemplateCommentsTests` and `PageCompilerTests` where the projection moved.

Docs: `pages.md` (an embed renders the page's whole chain, `Wrap` calls stack, and a fragment is a
page with no `Wrap`), `shell.md`, `metrics.md`, `glossary.md`, `testing.md`, `architecture.md` where
it names the capability set, `/help`, `docs/jobs/README.md` (the plan link), and `decisions.md` at
the merge only.

## Phases

One branch, three commits, so each can be read alone — the steps below are the phases the work went
through, and the first two were committed together because the role and the chain do not compile apart:

1. **The contract, the values and the role.** `IPageComposition`, `PageFrame`, `PageCapture`,
   the `PageRequest` extension methods, the `IPageLayout` change (`Layout` and `Body` out, `Content` in, nullable
   `Title`), `PageLayerRole`, `SimpleResponse`/`SimpleContext`, `PageLayoutState`, the `PageWriteAction`,
   and the tests that pin the role matrix, the request extension methods, the two values and the nullable title.
2. **The renderer and the four calls.** The layer loop, the session order, the embed and capture
   paths, the ancestor stack, the bounded capture, the head/sheet fold, the projection move, the two
   configuration values and the instrument. The chain tests move to the new shape here.
3. **The pages we ship.** Ten seeds with `Wrap()` and the nullable title, the document library's merge
   overload and the `/_layout` that uses it, the editor's starter, `/help`, the prompt and its version. `make check-pages` and the prompt test prove it.
4. **The docs.** `pages.md`, `shell.md`, `metrics.md`, `glossary.md`, `testing.md`, the plan link and
   the decision entries at the merge; the shell itself is trunk's.

## Verification

- `make check` is the gate before the merge stop.
- **The contract:** an embedded page sees the request's method, form and headers, its own key as
  `ctx.Path`, the visitor's page as `ctx.Request.Path`, and the request's query; omitting the request
  forwards the caller's, `WithQuery` replaces a name, `WithAnotherQuery` adds one and `WithoutQuery`
  drops one; `Path` cannot be changed.
- **The role:** a layout and an embed cannot set the response, cannot flush, and an embed's redirect is
  a refusal; an embed is offered the write and the jobs and a layout is not; two embedded writers spend
  one action.
- **Recursion:** `/foo` embedding itself is refused; `/foo?q=1` embedding `/foo?q=2` renders, counts
  `page.composition.recursive` and stops at the depth cap; a listing embedding one card ten times renders ten
  times; `MaxEmbeds` refuses the eleventh under its configured value.
- **The body:** an embed over the remaining allowance fails before writing; the caller's body is
  untouched; the successful embed is charged once; a capture is charged only when its string is written.
- **The capture:** `CaptureAsync` returns a `PageCapture` and leaves the caller's body untouched; a
  page with wrappers captures as its outermost body and frame; the captured page's `SetStatus` does
  not leak and its head, sheets and script permission arrive only in the value; `EmbedAsync` returns the `PageFrame` (no body) and writes the body; a redirect
  refuses; a capture of an ancestor with the same request is a cycle; the Markdown use in D8 renders.
- **The symmetry:** `EmbedAsync` returns the frame and the caller folds what it wants into its
  own slots; a wrapper receives the content's `PageCapture` as `IPageLayout.Content` and folds what it
  wants; the host merges neither behind the outer page's back; `/a Embed(/b)` and `/b Wrap(/a)` reach
  the same frame/capture shape.
- **The no-code page:** an embedded text page's `<%# … %>` does not reach the body, and a served one
  still loses it.
- **The wrap:** a page with no `Wrap` is a fragment; `Wrap()` uses the configured key; `Wrap(key)` names
  another; `Wrap(key, request)` runs the wrapper against that request; calls stack in order, so
  `Wrap("/_md"); Wrap()` renders `_md` then the site layout; a default that cannot be served is
  skipped and the rest of the chain runs, a named one fails; a page that set no title arrives at the
  wrapper as `Content.Title == null`; a wrapper may embed a partial around `Content.Body`.
- **The embed runs the chain:** an embed of a page that declares a wrapper returns the outermost
  body and frame; an embed of a page that declares the site layout includes it; `CaptureAsync` of
  the same page returns the outermost body. A fragment is a page that declares no wrapper.
- **The shell:** `TheShellAndTheLayoutOfferTheSameItems` still passes, because neither menu moved.
- **The metrics:** `MetricNamesTests` records the new counter and reads it back from `/metrics`; the
  status page renders its panel.
- **The prompt:** every member of `IPageComposition`, `PageFrame` and `PageCapture`, and every
  `PageRequestExtensions` method, is in the reflective section; an example embeds a page that declares
  a wrap, and the prompt says the chain runs.

## Risks

- **The live store's pages that relied on the default wrap become bare fragments.** A page that never
  touched `IPageLayout.Layout` compiles and serves, but without chrome. The seeds and the editor's
  starter carry `Wrap()`, and `/help` says it; the owner should read the store's pages before the merge.
  A page that used `Layout` or a non-null `Title` fails to compile instead, which is loud.
- **A page with `Wrap()` embeds as a document.** The page renders the same chain wherever it is
  rendered, so embedding a page that declares the site layout inserts the layout inside the caller.
  A view that is used both ways needs a fragment page for embeds; `/help` says so and the owner should
  look at the stored pages other pages embed.
- **A request is broader than a query.** The caller can shape the method, form, headers and cookies an
  embedded page sees, because that is the page's real input and the caller is the author. `Path` is
  pinned; nothing else is. That is more freedom than the options type had, and the manual should say
  it rather than let it be discovered.
- **The one-write rule is only true if the counter is shared.** Today each layer owns one; the change
  must move it to the chain or a listing of writers quietly writes several times.
- **The wrapper now merges explicitly.** A wrapper that ignores `IPageLayout.Content` serves its own
  document only; the library's merge overload and the seeded `_layout` are what keep that from being a
  surprise, and `/help` shows the call.
- **The body budget view is the subtle arithmetic.** Charging the embed's capture and the caller's copy
  both would shrink the allowance; charging neither would let a page exceed it. The test that renders a
  near-limit embed, and the capture that is charged only when written, are the proof.
- **A capture is a copy until it lands.** A page can hold one bounded capture per call and discard it;
  `MaxEmbeds` and the deadline bound the total, and a page that writes the string pays for it then.
- **The role touches the response gates**, which is the part a visitor can observe: the eight gate tests
  must move with the flag, not be rewritten, and the redirect exception for an embed is the one new
  behaviour.
- **The title is a public-API change.** The seeds are updated; `RuntimeId` moves; a stored page that
  hands `layout.Title` to a `string` parameter reports a diagnostic.
- **The default fallback is narrower.** Only `Wrap()` can fall back; a named wrapper that cannot be
  served is an error, which is the rule a named layout already has.
- **The prompt's contract section is generated.** A test that nothing documents is the guard; the new
  types and members must be in it in the same commit as the interface.

## The questions this plan asked the owner

1. **What the embed is offered.** The caller's capabilities, including the write and the jobs: the
   authority is the caller's identity. A layout is refused because it runs once per wrapped page.
2. **What the embedded page sees.** The caller's request — method, form, headers, cookies — with the
   request as its input. The consequences (a POST embedder makes the embed take its POST branch; links
   must use `ctx.Path`) are manual warnings, not host guards.
3. **What a no-code page means.** It embeds as the fragment it is, less its comments; a library or a
   worker is not a page and throws.
4. **The capability and the timing.** `IPageComposition` with `EmbedAsync`, `CaptureAsync` and the two `Wrap` forms;
   `Wrap` is a declaration, not `WrapAsync`, so every exit path is wrapped.
5. **The title.** Checked by the wrapper, not the host; `IPageLayout` keeps `Title`, `Head`,
   `Stylesheets` and `Content`; `Body` and `Layout` are gone.
6. **`Wrap()` with no key.** Yes: the configured default, so a page does not hard-code a key the owner
   can move, and the default fallback has a home.
7. **The shell documents.** `/status` and `/assistant` stay shell documents. The proposed trim to one
   way home was dropped at the merge, because trunk unified the menus into one route table first.
8. **The input.** No request forwards the caller's; a request is used as given with `Path` pinned.
   `WithQuery`, `WithAnotherQuery` and `WithoutQuery` are extension methods on `PageRequest`, so a
   copy-and-change is cheap without adding members to the record. No options type.
9. **Recursion.** Key and query: `/foo` embedding `/foo` is a loop; `/foo?q=1` embedding `/foo?q=2` is
   allowed, capped and counted.
10. **Failure.** `EmbedAsync` throws; catching it is the page's choice; there is no failure value and no
    error boundary.
11. **Capture.** Asked for after the plan was first written: the same codepath as an embed, returned as
    a `PageCapture` — the body beside the title, head, sheets and script permission — with the captured
    page's response kept out of the caller. Not the live `IPageLayout`, because its `Content` is the
    layer inside it and its own slots are what it contributes, not a result. It is how a page does what a
    wrapper does with `Content` without being wrapped.
12. **The symmetry.** `EmbedAsync` returns the frame; the wrapper receives the content's `PageCapture`
    as `IPageLayout.Content`. The host stops merging the inner layer's title, head and sheets, so the
    page that owns the document chooses in both directions.
13. **The embed boundary.** Full: an embed renders the page's entire chain, wrappers included, and
    returns the outermost body and frame. There is no per-call boundary, no partial view and no embed
    flag; a page that should be a fragment declares no wrapper.
14. **The wrap stack.** A page may call `Wrap` more than once; the calls stack outward, innermost
    first, and the ancestor check refuses a key that repeats in the chain.

## Considered and rejected

- **A host-driven wrap (`IPageLayout.Layout` and the default applied to every page).** The default is
  what made a page unwrapped only if it opted out; the owner wants the page to own the decision.
- **`WrapAsync` as a handover at the call.** It reads as the sentence it is, but every exit path must
  reach it, and a guard's early `return` would silently serve a bare fragment. A declaration at the
  top covers every path and lets the page set its status after writing.
- **Two capabilities (`IPageEmbed` + `IPageWrap`).** They share the render, the budgets, the stack and
  the guards; one seam is the repository's rule.
- **One wrapper per layer.** The owner's markdown case needs `Wrap("/_md"); Wrap()`, and a page that
  wants a card inside the site layout needs the same; calls stack, and a repeated key is already a
  cycle the ancestor check refuses.
- **Suppressing a page's wrappers on an embed (the earlier "an embed is a fragment" rule).** It loses
  content wrappers — a `_markdown` page would embed as source — and makes `/a Embed(/b)` and
  `/b Wrap(/a)` different shapes. The page renders its whole chain wherever it is rendered; a
  fragment is a page that declares no wrapper. A per-call boundary would need a marker or a document
  render to discover it, and the page's own declaration is simpler.
- **Returning the captured page's `IPageLayout`.** It is the live capability, it is mutable, and its
  `Content` is the layer inside it, not the layer's own output. A value carries the output and the
  facts a caller can fold without handing back a capability.
- **Keeping the automatic inner merge.** The wrapper would keep composing the content's head, sheets
  and title into its own, and an embedder would keep receiving them folded. It is one less line for a
  wrapper and it makes the two directions different; the owner's rule is that the outer page chooses.
- **Returning the captured response's status and content type.** The body is the artifact; a capture
  has no document to answer with, which is why a redirect refuses.
- **A slot API beyond `Body`.** A wrapper can embed partials around its slot; named regions are a
  different contract.
- **A general `Items` bag on `IContext`.** It would be untyped, hidden and a second input channel
  beside the request; structured state already has `IPageStorage` and `IPageSession`.
- **A failure value or a placeholder for a broken embed.** It hides the author's mistake; `try/catch`
  is the page's decision and the output is clean because the embed has its own buffer.
- **A reserved `/_parts` prefix.** It is a manual convention; a partial being servable on its own is
  how it is previewed.
- **Deleting `IPageLayout` entirely.** A wrapper needs a slot; the header/footer include model cannot
  place the content and would make every page write its own document and its own head.

## Not in scope

- Moving `/status` or `/assistant` off the shell; see the health-surface objection in
  [shell.md](../shell.md).
- Rendering a page in a browser — [IDEA-044](../jobs/IDEA-044.md).
- Batch writes — [IDEA-047](../jobs/IDEA-047.md).
- Caching or memoising an embed; the page is `no-store` and the image cache already covers the compile.
- A second response body or a streamed embed.

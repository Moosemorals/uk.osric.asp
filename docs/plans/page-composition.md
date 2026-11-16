<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page composes another page

| | |
| --- | --- |
| Status | Proposed 2026-11-16 — waits for the owner. Nothing below is built |
| Size | Large — one new `Sdk` capability with three calls and two values, `IPageLayout` loses `Layout` and gains a nullable `Title`, one layer renderer with three roles, the layout chain rebuilt on it and entered by the page, the shell's documents trimmed to one way home, ten seeded pages and the editor's starter, the manual, the assistant's prompt and its version, two configuration values, one instrument, and the tests that pin the recursion, the response gates and the chrome |
| Area | Sdk, Web (pages, shell, seeds, assistant, metrics), docs |
| Spike | None. Every count below was taken off `trunk` at `6004526` on 2026-11-16, after `pages-the-response-is-one-object` merged |
| Backlog | [IDEA-049](../jobs/IDEA-049.md) is this step. [IDEA-047](../jobs/IDEA-047.md) is the answer if a real page needs more than one write in one render, which composition makes likelier |
| Decisions | New names proposed: **pages-an-embed-is-a-content-render**, **pages-an-embeds-options-are-its-query**, **pages-a-capture-is-an-embed-returned**, **pages-a-page-chooses-its-layout**, **pages-one-render-three-roles**, **pages-the-wrapper-owns-the-title**, **shell-documents-are-bootstrap**, **shell-a-shell-document-carries-one-way-home**. Edits `pages-one-request-one-chain`, `pages-a-layout-is-a-capability`, `pages-layouts-are-markup`, `pages-one-base-sheet` and `pages-capabilities-are-one-seam`; retires `shell-the-menu-is-written-twice`. Relies on `pages-the-response-is-one-object`, `pages-the-contract-is-the-seam`, `pages-public-by-default`, `pages-a-warning-never-stops-a-page`, `pages-page-io-is-async`, `pages-any-page-may-write-the-store`, `pages-a-page-may-save-itself`, `metrics-one-panel-per-instrument` and `code-policy-values-are-configuration` |

## The short version

A stored page can read another page's **source** (`IPageCatalog`) but not its **output**. So every
view that wants "this page, here" writes the markup again. This change adds the missing primitive and
uses it to remove the last special case in the page runtime: the layout chain stops being a thing the
host does to every page and becomes a thing a page asks for.

**Three calls, one capability.** `IPageComposition` is offered to every layer a render has:

```csharp
Task EmbedAsync(string key, PageEmbedOptions? options = null);   // include another page here
Task<PageCapture> CaptureAsync(string key, PageEmbedOptions? options = null); // …or return it
void Wrap(string key);                                           // hand this page to another
void Wrap();                                                     // …the site's layout, Pages:Layout
```

`EmbedAsync` renders another stored page with the caller's request — same method, form, headers and
cookies, with the options as its query — and writes its body where the call is. `CaptureAsync` is the
same render returned as a string instead of written, for the page that wants to transform it. `Wrap` records the
page that will wrap this one; the host runs the chain after `HandleAsync` returns, exactly where it
runs today, so a wrap declared near the top covers every exit path including a guard's early return.
`Wrap` is not `WrapAsync`: it does not await the wrapper, the host does.

**The role replaces the flag.** The response rule is currently `isLayout` on `SimpleResponse`, and that
one flag decides two different things: who owns the response and what capabilities the layer gets.
It becomes a role — **content**, **layout**, **embedded**. Content owns the response; layout and
embedded withhold it; embedded keeps the capabilities a content page has, including the write and the
jobs, because the authority is the caller's identity.

**The shell stops pretending to be a view.** Its own documents (error, sign-in, `/status`, `/assistant`
and the admin routes) are bootstrap and recovery surfaces. They carry one way home — `/`, plus
`/admin` at or under that prefix — and the site menu is written only by the stored `/_layout`. That
removes the duplicate menu and the test that compared the two copies without making the error and
health routes depend on a stored page.

**Nothing wraps automatically.** A page that does not call `Wrap` is served exactly as it wrote
itself. That is the point and the migration: the seeds and the editor's starter carry the line,
`/help` says why, and a page that forgets is a bare fragment rather than a diagnosis.

## Goals

1. A page can include another stored page's output, or capture it as a string, with options that are
   its query, under the same request, budgets, identity and capabilities.
2. A page chooses the page that wraps it; `IPageLayout.Layout` and the host's automatic wrap are gone,
   and the chain still renders content first and writes the outermost layer.
3. The three layer roles are one rule, not two booleans: the response gates, the stream refusals, the
   write/job/session offers and the layout rule all read the same value.
4. Recursion is bounded and honest: the same page with the same options in an ancestor is a cycle and
   refused; the same page with different options is a recursive component, allowed, capped and
   counted; fan-out is capped.
5. A no-code page embeds as the fragment it is, with its comments removed, because the projection
   moves from the shell's serving path into the renderer.
6. The shell's own documents carry one way home and no site menu; the stored layout carries the menu.
7. The manual, the prompt, the docs and every seeded page say the new thing in the same change.

## Non-goals

- **A slot API beyond `Body`.** A wrapper can embed its own partials around `Body`; named regions are a
  different contract.
- **A data bag in the options.** Options are the query. A typed payload is a new contract value.
- **A failure value or an error boundary.** `EmbedAsync` throws and a page may catch it. A result value
  that makes ignoring a failure the default is not this step.
- **Moving `/status` or `/assistant` off the shell.** Both stay shell documents for now; the health
  surface must answer when the page pipeline is broken.
- **Batch writes.** One render still writes once across the whole chain. [IDEA-047](../jobs/IDEA-047.md)
  is where several writes with a named policy land.
- **Caching or memoising an embed.** It is part of the page it appears on, and the page is `no-store`.

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
| Configuration | `Pages:Layout` (required, blank means none) and `Pages:Render:MaxLayoutDepth` | [PageLayoutConfiguration.cs](../../src/uk.osric.asp.Web/Pages/PageLayoutConfiguration.cs), [RenderOptions.cs](../../src/uk.osric.asp.Web/Pages/RenderOptions.cs) |
| The prompt | `page-author-v35`, a reflective capability section and 47 `ctx.` mentions | [PageAuthorPrompt.cs](../../src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs) |
| Metrics | 18 instruments, one panel each; `page.layout.fallback` is the only chain instrument | [PageMetrics.cs](../../src/uk.osric.asp.Web/Pages/PageMetrics.cs), [StatusCatalogue.cs](../../src/uk.osric.asp.Web/Metrics/StatusCatalogue.cs) |
| Docs | `IPageLayout` is named 22 times outside the archive; the capability count is eleven | [pages.md](../pages.md), [shell.md](../shell.md) |

## The design

### D1 — `IPageComposition` is one capability with three calls

```csharp
// Sdk/IPageComposition.cs
public interface IPageComposition {
    Task EmbedAsync(string key, PageEmbedOptions? options = null);
    Task<PageCapture> CaptureAsync(string key, PageEmbedOptions? options = null);
    void Wrap(string key);
    void Wrap();
}
```

It is registered on every layer — content, layout and embedded — because a layout embedding a header
and footer partial is the use that argues for this most, and because a page must be able to call
`Wrap` without knowing it is embedded. It is a capability rather than context members for the reason
`pages-capabilities-are-one-seam` gives: a page that never composes never meets it.

`EmbedAsync` includes another page **now**. It renders into a bounded capture and then writes the
result through the calling layer's `IPageResponse`, so the body allowance is charged once and a failure
leaves nothing half-written in the caller. `CaptureAsync` is the same render with the fold and the
write replaced by a `PageCapture` return (D8): the caller gets the body and the document facts and
decides what to do with them.

`Wrap` is a **declaration**: it records the key, and the host runs the chain after `HandleAsync`
returns. That is what keeps a guard's early `return` wrapped and lets the page set its status after it
has written the body. A page that wants the literal handover would need `WrapAsync` and a rule that
every exit path reaches it; that trap is rejected in [Considered and rejected](#considered-and-rejected).

Refusals at the call:

| Call | Refusal |
| --- | --- |
| `Wrap` twice on one layer | `InvalidOperationException`: one wrapper per layer |
| `Wrap` after the first flush | `InvalidOperationException`: a committed body cannot be handed over |
| `Wrap(key)` with a malformed key | `ArgumentException` |
| `Wrap(key)` naming a reserved key | `InvalidOperationException`, as a reserved key is a page bug |
| `Wrap` on an embedded layer | **Ignored**, logged at Debug — the embed is a fragment, so the wrapper does not run |

`Wrap()` uses `Pages:Layout`. When the configuration is blank, it records nothing and the page is
served alone, which is the same answer the blank value gives today.

### D2 — `PageEmbedOptions` is the query, plus the copy-and-change helpers

```csharp
// Sdk/PageEmbedOptions.cs
public sealed record PageEmbedOptions {
    public PageEmbedOptions() { }
    public PageEmbedOptions(PageRequest request);
    public IReadOnlyList<KeyValuePair<string, string>> Query { get; init; } = [];
    public PageEmbedOptions With(string name, string value);
    public PageEmbedOptions WithAnother(string name, string value);
    public PageEmbedOptions Without(string name);
}
```

- `EmbedAsync(key)` forwards the caller's query untouched.
- `EmbedAsync(key, new PageEmbedOptions())` sets an explicitly empty query.
- `new PageEmbedOptions(ctx.Request)` copies the caller's `QueryValues`, repeats and order included,
  so `With`/`WithAnother`/`Without` are edits rather than a rebuild.
- `With` replaces every value for a name; `WithAnother` adds one beside the existing values, the
  repeated name `QueryValues` already models; `Without` drops a name.

The embedded `PageRequest` is the caller's with `Query`/`QueryValues` replaced; everything else —
method, form, headers, cookies, path — is the caller's. `Path` is the visitor's page, so
`ctx.Path != ctx.Request.Path` is how a layer knows it is inside another, the same signal a layout
already gives. The two manual warnings fall out of that and are in D4.

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
| `Wrap` | runs | runs (the parent) | ignored |
| `IPageLayout` | slots, `Body` empty | slots, `Body` is the wrapped output | its own slots; `Body` empty |
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
change makes every layer use them:

- **Content**: the endpoint renders the requested key with the caller's request.
- **Layout**: `PageChain` renders the declared wrapper with the content's body in `IPageLayout.Body`.
- **Embedded**: `PageCompositionScope` renders the named key with the derived request.

`PageChain` grows the layer loop the endpoint's `WrapAsync` call makes today, but starts from the
content's declared wrapper instead of `contentLayout.Layout`:

```
request /about
  → /about renders into buffer A, sets Title/head/sheets, calls Wrap()
  → /_layout renders into buffer B with IPageLayout.Body = A
  → /_layout may call Wrap("/_outer") → buffer C with Body = B
  → write the outermost buffer
```

`PageChain` owns the ancestor stack, the depth and count counters, the default fallback and the
`AllowScripts` union; the layers own their own body and slots.

`PageChain.CreateLayer` returns the layer it built — the context, the composition state the page
writes its `Wrap` into, and the slots — so the endpoint can hand the content's declaration to the
chain after the render, and the chain can read each layout's declaration in turn.

The endpoint's order changes by one step: it opens the render session **before** it builds the content
layer, so the composition scope holds the session every embedded render runs under. One slot and one
deadline still cover the whole request.

### D5 — The wrapper gets the body; it does not get the response

The wrapper is a page with code. It reads `IPageLayout.Body`, `Head`, `Stylesheets` and `Title` as a
layout does today; it may compose the title and may embed partials of its own around `Body`. Its own
`SetStatus`, `SetHeader`, `SetContentType` and `Redirect` are ignored and logged, because the content
owns the response. Its `AllowScripts` is unioned into the content's.

A wrapper must have code: a no-code page has no way to read `Body`, so its stored source would replace
the page instead of wrapping it. `Wrap` naming a library, a worker, a no-code page, a missing page or
a deleted one fails when the chain tries to render it, with a message naming the key.

The default wrapper keeps the fallback: `Wrap()` records the configured default, so a default that
cannot be served serves the page unwrapped with a warning and `page.layout.fallback`, as today.
`Wrap(key)` is a named wrapper, so a failure is an error naming the key.

### D6 — `IPageLayout` loses `Layout`, and `Title` becomes nullable

```csharp
public interface IPageLayout {
    string? Title { get; set; }          // was string, throwing until set
    string Head { get; }
    IReadOnlyList<string> Stylesheets { get; }
    void AddHead(string markup);
    void AddStylesheet(string href);
    string Body { get; }
}
```

`Layout` is gone: the page declares its wrapper through `IPageComposition.Wrap`, and a wrapper declares
its parent the same way. `PageLayoutState` loses its `defaultLayout` constructor argument, its
`Layout` property and its `TitleOrNull` member; it keeps the inner-layer composition and the sheet
deduplication. The setter still refuses blank, so the only way to have no title is to leave it unset.

**The title check is the wrapper's.** The host's pre-chain refusal and its "a wrapped page must set a
title" error go. The seeded `/_layout` resolves `layout.Title ?? "Site"`; a wrapper that wants to refuse
instead can throw, naming `ctx.Request.Path`.

This is a public-API change: `Title` is `string?` and a page that hands it to a `string` parameter no
longer compiles. The seeds are updated; `RuntimeId` moves, so every stored page recompiles and a page
that used the old members reports diagnostics rather than serving stale markup.

### D7 — The embedded layer: request, slots, capabilities, output

`PageCompositionScope.EmbedAsync` does the following, in order:

1. **Resolve** the key with `PagePath.Normalize`; a malformed key is an `ArgumentException`; a reserved
   key is an `InvalidOperationException`; the key must be a page the renderer can serve.
2. **Check recursion** (D9) before anything is rendered.
3. **Derive the request**: the caller's request with `Query`/`QueryValues` replaced by the options.
   The method, form, headers, cookies and `Path` stay the caller's.
4. **Create the layer**: a fresh `PageLayoutState` (no inner), a `StringWriter`, a fresh commit state,
   `role: Embedded`, the embedded key as the context's `Path`, the caller's user, login redirect,
   antiforgery factory, asset map and abort token, and a **bounded body capture** (D10). The body sink is
   `null`, so the embedded layer cannot stream.
5. **Render** it through the session: `session.RenderAsync(key, context)`.
6. **Fold outward**: `AddHead(embedded.Head)` when it is non-empty, each `Stylesheet` through the
   caller's `AddStylesheet`, and `AllowScripts()` when the embedded response allows scripts.
7. **Write** the body through the caller's `IPageResponse.WriteRaw`, which charges the shared body
   allowance once.

`CaptureAsync` does the same through step 5 and then returns a `PageCapture` instead of folding and
writing (D8). `EmbedAsync` is literally that value plus steps 6 and 7, which is why the two cannot
drift.

The embedded page keeps the capabilities a content page has: `IPageStorage` and `IPageSession` bound to
its own key, `IHttpFetch`, `IPageCatalog`, `IPageLayout`, the two renderers, `IPageJobs`,
`IPageWrites` and `IPageComposition`. The write shares the **render's one action** (D10), so two
instances of a writer cannot both write in one request.

A no-code page embeds as its stored source less its comments; that projection moves into the renderer
in D12. A library or a worker is not a page: it is refused with a `UserFacingException` naming the key.

### D8 — Capture is the same render, returned

`CaptureAsync` takes the same key and options as `EmbedAsync` and shares its whole codepath: the
resolution, the recursion check, the derived request, the content capabilities, the withheld
response, the one write and the failure table. The difference is the last step. `EmbedAsync` folds the
captured page's head and sheets into the caller, writes its body and opts into its scripts;
`CaptureAsync` returns a value and touches nothing:

```csharp
// Sdk/PageCapture.cs
public sealed record PageCapture(
    string Body,
    string? Title,
    string Head,
    IReadOnlyList<string> Stylesheets,
    bool AllowsScripts);
```

`EmbedAsync` is then literally `CaptureAsync` plus the fold and the write, so the two calls have one
render and one set of rules. The captured page gets a **clean response** of its own — a fresh
`SimpleResponse` over the capture — so its `Write` and `WriteRaw` land in the body and its status,
content type, headers and `AllowScripts` never reach the caller except as the record's
`AllowsScripts`. A redirect still refuses, because a captured page has no response to redirect.

The result is deliberately a **value, not the live `IPageLayout`**. The interface is the capability a
page runs with: it is mutable, `AddHead` and `AddStylesheet` are how a page contributes rather than
what a caller reads, and — decisively — its `Body` is empty on an embedded layer, because `Body` is the
slot a wrapper receives, not the output a page wrote. The capture's output is the response body, and
`PageCapture` carries it beside the three document facts a caller may want to fold: the title, the
head and sheets, and whether the captured page asked for scripts.

This is the page-level twin of the wrapper's `IPageLayout.Body`: the prod site's `/_md` layout already
receives its content as `Body`, and a page that wants the same view without being wrapped — or wants to
transform it first — is what capture is for:

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
- **Same key, different identity** → allowed. `/foo?q=1` embedding `/foo?q=2` is a recursive component
  — a comment tree, a directory — and the caps are its bound. It records `page.composition.recursive` and
  logs the chain at Debug, so the one page doing it is visible without making the ordinary case noisy.
- **Repeated at one level** → allowed, because the stack pops. A listing may embed the same card
  twenty times.
- **Depth** — `Pages:Render:MaxEmbedDepth` (shipped 4, accepted 1–16) bounds nested embeds below the
  layer that is running. It is also the real bound on data-driven recursion, where every level passes
  different options.
- **Fan-out** — `Pages:Render:MaxEmbeds` (shipped 32, accepted 1–256) bounds the embeds in one request.
  Depth alone lets a listing embed a thousand siblings under one slot and one deadline.

`MaxLayoutDepth` stays as it is for wraps; the two depths are chosen independently because a widget is
not chrome.

The fingerprint is a pure function over the query pairs, so it lives in `Core.Pages` with its own
tests: two orders of names that a browser would send differently must not collide, and a repeated name
keeps its order.

### D10 — Output, budgets and the one write

- **The capture is bounded.** `BodyBudget` gains `Remaining`; an embedded layer renders into a
  `BodyBudget` sized to the caller's remaining allowance. If the embed overruns, it fails before
  anything reaches the caller. On success the caller's `WriteRaw` charges the shared budget once, which
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
| A cycle, a cap, a second `Wrap`, `Wrap` after a flush | `InvalidOperationException` naming the chain or the setting |
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

- `Pages:Render:MaxEmbedDepth` and `Pages:Render:MaxEmbeds` are policy values: an options record
  member read through `ConfigurationReader`, the shipped value in `appsettings.json`, and a test for
  the shipped value, an override and a refusal.
- `page.composition.recursive` is a counter in `PageMetrics`, one panel in `StatusCatalogue`
  ("Recursive composition", a line chart by `page_path`), one row in the metrics table, and one call
  in `MetricNamesTests`. Both `EmbedAsync` and `CaptureAsync` count. The Pages panel count in `docs/metrics.md` moves from nineteen to twenty.

### D14 — The shell's documents are bootstrap surfaces

`SiteNavigation.Links` becomes the one way home: `/` always, and `/admin` when the request path is at
or under `/admin`. The assistant, editor, sign-in and sign-out items leave the **shell's** menu; the
stored `/_layout` keeps them, because it is the menu a visitor sees day to day. The footer's two
static links (`/about`, `/privacy`) stay — they are not the menu and carry no policy — unless the
owner wants them gone.

`SiteNavigation.For(context)` keeps its signature and reads only the path. `ShellDocument`'s required
navigation parameter stays, so a view still cannot be written without one.

The integration test that compares the shell's menu with the stored layout's is replaced by two:
a shell document carries Home, plus Admin exactly when it is under `/admin`; and the stored layout
keeps the full menu. `AnAnonymousVisitorSeesSignInAndNoTools` and
`ASignedInVisitorSeesTheToolsAndSignOut` stay, because they assert the stored layout through a wrapped
page.

### D15 — The record

> **pages-an-embed-is-a-content-render** — an embedded page is a content render: its own key, storage,
> session and capabilities, the caller's identity, one budget and one slot, and no response.
>
> **pages-an-embeds-options-are-its-query** — an embed's options are the embedded request's query;
> omitting them forwards the caller's, an empty `PageEmbedOptions` clears it, and a constructor over
> the caller's request makes a copy-and-change cheap.
>
> **pages-a-capture-is-an-embed-returned** — a capture is the embed render returned as a value: the
> body beside the title, head, sheets and script permission, with the captured page's settings kept
> out of it, and a capture that would redirect refuses.
>
> **pages-a-page-chooses-its-layout** — a page declares the page that wraps it; the host does not wrap
> by default, and `IPageLayout.Layout` is gone.
>
> **pages-one-render-three-roles** — content, layout and embedded are one role on the response and the
> capability offer, not a boolean and a special case.
>
> **pages-the-wrapper-owns-the-title** — `IPageLayout.Title` is nullable and the wrapper decides what a
> missing title means; the host no longer refuses the chain.
>
> **shell-documents-are-bootstrap** — the shell's own documents are emergency and recovery surfaces,
> not the site's views; the stored layout is the menu.
>
> **shell-a-shell-document-carries-one-way-home** — a shell document links `/`, and `/admin` when it is
> at or under that prefix.

Edits: `pages-one-request-one-chain` (the chain is entered by the page and still one render),
`pages-a-layout-is-a-capability` (the slots remain; `Layout` leaves), `pages-layouts-are-markup` (the
wrapper is the layout role), `pages-one-base-sheet` (an embed's sheets fold into the page's), and
`pages-capabilities-are-one-seam` (the twelfth capability). `shell-the-menu-is-written-twice` is
retired: the menu is written once.

## What it touches

`Sdk`: `IPageComposition.cs`, `PageEmbedOptions.cs` and `PageCapture.cs` new; `IPageLayout.cs` loses
`Layout` and its `Title` becomes nullable; `PublicAPI.Unshipped.txt`.

`Core`: the query fingerprint for the ancestor stack.

`Web`: `PageCompositionScope.cs`, `PageCompositionState.cs`, `PageLayerRole.cs` and
`PageWriteAction.cs` new; `PageChain.cs` (the loop, the stack, the caps), `PageEndpoint.cs` (the
session order, the wrap), `PageRenderer.cs` (the comment projection), `PageLayoutState.cs`,
`PageLayoutConfiguration.cs` (documentation only), `SimpleContext.cs`, `SimpleResponse.cs`,
`PageWritesScope.cs`, `BodyBudget.cs`, `RenderOptions.cs`, `PageMetrics.cs`, `StatusCatalogue.cs`,
`appsettings.json`, `SiteNavigation.cs`, `ShellDocument.cs`, and the seeds (`_layout`, `about`,
`admin`, `editor-mode`, `help`, `pages`, `privacy`, `recent`, `scratch`, `vim-help`). The editor's
empty page starts from a one-line fragment that calls `Wrap()`, so a page created in the editor is
wrapped without the author knowing the rule.

`Assistant`: `PageAuthorPrompt.cs` — the capability section, examples, the composition rules, and the
version (`page-author-v35` → `v36`).

Tests: `PageLayoutChainTests` (36, retitled where the wrap changed), new `PageCompositionTests`,
`PageCaptureTests`, `PageEmbedOptionsTests` and `PageLayerRoleTests` in `Web.Tests`;
`SimpleResponseTests`, `PageEndpointTests`, `PageWritesScopeTests` and `PageStreamingTests` gain the
role and the one-write sharing; `PageAuthorPromptTests`; `SiteChromeTests` and `SiteLayoutEditingTests`
in the integration suite; `MetricNamesTests`; `TemplateCommentsTests` and `PageCompilerTests` where the
projection moved.

Docs: `pages.md`, `shell.md`, `metrics.md`, `glossary.md`, `testing.md`, `architecture.md` where it
names the capability set, `/help`, `docs/jobs/README.md` (the plan link), and `decisions.md` at the
merge only.

## Phases

One branch, four commits, so each can be read alone:

1. **The contract, the values and the role.** `IPageComposition`, `PageEmbedOptions`, the
   `IPageLayout` change, `PageLayerRole`, `SimpleResponse`/`SimpleContext`, `PageLayoutState`, the
   `PageWriteAction`, and the tests that pin the role matrix, the options helpers and the nullable
   title.
2. **The renderer and the three calls.** The layer loop, the session order, the embed and capture
   paths, the ancestor stack, the bounded capture, the head/sheet fold, the projection move, the two
   configuration values and the instrument. The chain tests move to the new shape here.
3. **The pages we ship.** Ten seeds with `Wrap()` and the nullable title, the editor's starter,
   `/help`, the prompt and its version. `make check-pages` and the prompt test prove it.
4. **The shell and the docs.** `SiteNavigation`, `ShellDocument`, the chrome tests, `/help`'s shell
   paragraph, the four docs, the plan link; the decision entries at the merge only.

## Verification

- `make check` is the gate before the merge stop.
- **The contract:** an embedded page sees the caller's method, form and headers, its own key as
  `ctx.Path`, the caller's page as `ctx.Request.Path`, and the options as its query; omitting the
  options forwards, an empty `PageEmbedOptions` clears, and `With`/`WithAnother`/`Without` do what
  they say.
- **The role:** a layout and an embed cannot set the response, cannot flush, and an embed's redirect is
  a refusal; an embed is offered the write and the jobs and a layout is not; two embedded writers spend
  one action.
- **Recursion:** `/foo` embedding itself is refused; `/foo?q=1` embedding `/foo?q=2` renders, counts
  `page.composition.recursive` and stops at the depth cap; a listing embedding one card ten times renders ten
  times; `MaxEmbeds` refuses the eleventh under its configured value.
- **The body:** an embed over the remaining allowance fails before writing; the caller's body is
  untouched; the successful embed is charged once; a capture is charged only when its string is written.
- **The capture:** `CaptureAsync` returns a `PageCapture` and leaves the caller's body untouched; the
  captured page's `SetStatus` does not leak and its head, sheets and script permission arrive only in
  the value; `EmbedAsync` is the same value folded and written; a redirect refuses; a capture of an
  ancestor with the same options is a cycle; the Markdown use in D8 renders.
- **The no-code page:** an embedded text page's `<%# … %>` does not reach the body, and a served one
  still loses it.
- **The wrap:** a page with no `Wrap` is a fragment; `Wrap()` uses the configured key; `Wrap(key)` names
  another; a default that cannot be served falls back, a named one fails; a missing title reaches the
  wrapper as `null`; a wrapper may embed a partial around `Body`.
- **The shell:** every shell document carries Home, and Admin exactly under `/admin`; a stored page
  still carries the full menu; the shell and the stored layout no longer have to agree.
- **The metrics:** `MetricNamesTests` records the new counter and reads it back from `/metrics`; the
  status page renders its panel.
- **The prompt:** every member of `IPageComposition` and `PageEmbedOptions` is in the reflective
  section; no example wraps in an embed.

## Risks

- **The live store's pages that relied on the default wrap become bare fragments.** A page that never
  touched `IPageLayout.Layout` compiles and serves, but without chrome. The seeds and the editor's
  starter carry `Wrap()`, and `/help` says it; the owner should read the store's pages before the merge.
  A page that used `Layout` or a non-null `Title` fails to compile instead, which is loud.
- **The one-write rule is only true if the counter is shared.** Today each layer owns one; the change
  must move it to the chain or a listing of writers quietly writes several times.
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
- **The shell menu test is a real loss of coverage.** It is replaced by the shell's own rule and the
  stored layout's own rule; the two documents are no longer supposed to be equal.
- **The default fallback is narrower.** Only `Wrap()` can fall back; a named wrapper that cannot be
  served is an error, which is the rule a named layout already has.
- **The prompt's contract section is generated.** A test that nothing documents is the guard; the new
  types and members must be in it in the same commit as the interface.

## The questions this plan asked the owner

1. **What the embed is offered.** The caller's capabilities, including the write and the jobs: the
   authority is the caller's identity. A layout is refused both because it runs once per wrapped page.
2. **What the embedded page sees.** The caller's request — method, form, headers, cookies — with the
   options as its query. The consequences (a POST embedder makes the embed take its POST branch; links
   must use `ctx.Path`) are manual warnings, not host guards.
3. **What a no-code page means.** It embeds as the fragment it is, less its comments; a library or a
   worker is not a page and throws.
4. **The capability and the timing.** `IPageComposition` with `EmbedAsync`, `CaptureAsync` and `Wrap`;
   `Wrap` is a declaration, not `WrapAsync`, so every exit path is wrapped.
5. **The title.** Checked by the wrapper, not the host; `IPageLayout` keeps `Body`, `Title`, `Head` and
   `Stylesheets`.
6. **`Wrap()` with no key.** Yes: the configured default, so a page does not hard-code a key the owner
   can move, and the default fallback has a home.
7. **The shell documents.** `/status` and `/assistant` stay shell documents; every shell document
   carries `/`, plus `/admin` at or under that prefix.
8. **The options.** No options forwards; empty options clear; a constructor over the caller's request
   makes a copy-and-change cheap, with `With`, `WithAnother` and `Without`.
9. **Recursion.** Key and query: `/foo` embedding `/foo` is a loop; `/foo?q=1` embedding `/foo?q=2` is
   allowed, capped and counted.
10. **Failure.** `EmbedAsync` throws; catching it is the page's choice; there is no failure value and no
    error boundary.
11. **Capture.** Asked for after the plan was first written: the same codepath as an embed, returned as
    a `PageCapture` — the body beside the title, head, sheets and script permission — with the captured
    page's response kept out of the caller. Not the live `IPageLayout`, because its `Body` is the
    wrapper's slot and is empty here. It is how a page does what a wrapper does with `Body` without
    being wrapped.

## Considered and rejected

- **A host-driven wrap (`IPageLayout.Layout` and the default applied to every page).** The default is
  what made a page unwrapped only if it opted out; the owner wants the page to own the decision, and
  the shell policy removes the duplication that made the automatic wrap feel necessary.
- **`WrapAsync` as a handover at the call.** It reads as the sentence it is, but every exit path must
  reach it, and a guard's early `return` would silently serve a bare fragment. A declaration at the
  top covers every path and lets the page set its status after writing.
- **Two capabilities (`IPageEmbed` + `IPageWrap`).** They share the render, the budgets, the stack and
  the guards; one seam is the repository's rule.
- **Returning the captured page's `IPageLayout`.** It is the live capability, it is mutable, and its
  `Body` is the wrapper's slot rather than the output the page wrote — on an embedded layer it is
  empty. A value carries the output and the facts a caller can fold without handing back a capability.
- **Returning the captured response's status and content type.** The body is the artifact; a capture
  has no document to answer with, which is why a redirect refuses.
- **A slot API beyond `Body`.** A wrapper can embed partials around its slot; named regions are a
  different contract.
- **A data bag in the options.** Options are the query, and a typed payload is a new value type on the
  page contract.
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

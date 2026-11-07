<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A layout page rendered around a page

Status: **proposed, not implemented**. Written 2026-11-07. This is a design and an implementation
plan, not a record of work done. It changes the page SDK, the Web render path and the seeded pages;
it does not change the template language, the compiler or the page store schema. It depends on
[IDEA-018](../jobs/IDEA-018.md), which moves the document builder into `Sdk`.

## The short version

1. A page asks for a layout with `ctx.SetLayout("/_layout")`. The content renders first; the layout
   renders second and inserts the content with `ctx.WriteBody()`.
2. The layout is an ordinary stored page: same compiler, same image store, same API policy, same
   storage binding, same editor, same import and export. There is no second kind of page and no
   second assembly.
3. Content and layout keep separate compiled images. Editing the layout recompiles the layout and
   nothing else; the next request for any page that uses it gets the new chrome. That is the
   "maintainable without a redeploy" property.
4. One request keeps one render slot, deadline, fetch budget, body cap and antiforgery token. Each
   layer has its own `ctx.Path` and storage handle; the request is shared.
5. A redirect or a non-HTML content type ends the chain. A layout is presentation, not a guard: the
   content has already run, so authorization stays in the page.
6. The chain nests and is bounded by a depth limit and a seen-set. A missing, broken or cyclic
   layout names the layout and fails the response; it never serves an orphan fragment.
7. `IContext` gains `SetLayout`, `Title`, `AddHead`, `Head` and `WriteBody`. The SDK document
   builder gains the split that lets a layout put a header before `main` and a footer after it.
8. No schema or image change. `SdkMvid` changes, so every stored image is compiled again once.

## Goals and non-goals

Goals:

- A stored page can wrap itself in a stored layout page, and the layout can be edited without a
  redeploy and without recompiling the pages that use it.
- The content's title, extra head markup and script permission are visible to the layout, because
  the content runs first.
- A layout is an ordinary page: the page API policy, the editor, the revision history, the seeded
  restore and import/export all apply unchanged.
- One request, one budget: a chain cannot multiply the render slot, the deadline, the fetch count or
  the body cap.
- The shell and the seeded layout write the same document through the SDK builder, so the head, the
  landmark, the sheet order and the encoding live once.
- Existing pages behave exactly as they do today when no layout is set.

Non-goals:

- Compile-time includes, library pages or shared types between pages. That remains the second half
  of [IDEA-005](../jobs/IDEA-005.md), deferred until a concrete helper needs it.
- A general `ctx.RenderPageAsync` or partial-rendering API. A layout is one body slot; arbitrary
  fragments are a separate capability.
- Layout-level authorization or a guard phase. The content runs first, so a layout cannot guard it;
  the page's own `ctx.User` check is the guard (DEC-02).
- Streaming (ARCH-13), background work or a second response pass.
- A new template directive or tokenizer change. The selector is ordinary page code.
- Moving the listing, history or editor views into pages. That is the follow-on
  ([IDEA-015](../jobs/IDEA-015.md), [IDEA-017](../jobs/IDEA-017.md)); this plan makes it possible.
- Per-visitor state ([IDEA-014](../jobs/IDEA-014.md)) or reading the page store from a page
  ([IDEA-015](../jobs/IDEA-015.md)).

## What already exists

- [ShellDocument.BeginAsync](../../src/uk.osric.asp.Web/Html/ShellDocument.cs:32) writes the head
  and opens `<main id="main">`; `EndAsync` closes them. It sets the content type, status and
  `Cache-Control` as well as writing markup.
- [PageLayout](../../src/uk.osric.asp.Web/Html/PageLayout.cs:10) keeps `Gutter`, `Nav` and
  `Diagnostics`; `NavLink`, `DiagnosticLine` and `AntiforgeryField` sit beside it.
- [PageRenderer.RenderAsync](../../src/uk.osric.asp.Web/Pages/PageRenderer.cs:44) loads or compiles
  one page, then runs it on a worker against one deadline. `LoadAsync` compares
  `PageImage.TemplateHash` and `RuntimeId`; `RunAsync` uses `Task.Run`, `Task.WhenAny` and
  `Abandon`.
- [SimpleContext](../../src/uk.osric.asp.Web/Pages/SimpleContext.cs:24) owns the response fields,
  the body writer, the fetch count, the body count and the antiforgery cache. Its
  `Path` is `_request.Path` today, and its storage handle is passed in already bound to that path.
- [PageEndpoint](../../src/uk.osric.asp.Web/Pages/PageEndpoint.cs) takes the request snapshot and
  the user, validates an authenticated `POST`, creates the storage scope and the context, calls the
  renderer, and writes the response with the ETag and security headers.
- [PageAssetSeeder](../../src/uk.osric.asp.Web/Pages/PageAssetSeeder.cs) seeds the files under
  `Pages/Seeds` at the site root and keeps a deleted seed tombstoned.
- `RuntimeId` is `{FormatVersion}:{PolicyVersion}:{SdkMvid}:{CoreMvid}`, so a change to the SDK
  invalidates every stored image.
- The decisions that constrain the document are DEC-107 (one shell writer), DEC-116 (sheet order),
  DEC-121 (no skip link while `main` is first), DEC-123 (async, tokenless I/O), DEC-125 (per-path
  storage isolation), DEC-127 (per-call storage deadlines) and DEC-129 (fetch failures are values).

## The contract

### Today

A page writes its whole response. It can set a title only by writing the `<title>` itself, and it
cannot ask another page to render around it.

### After this plan

```csharp
public interface IContext {
    /// <summary>Renders the stored page at this key as the layout for this response.</summary>
    public void SetLayout(string path);

    /// <summary>The document title; the layout writes it and the SDK builder encodes it.</summary>
    public string? Title { get; set; }

    /// <summary>Appends raw markup to the layout's head slot.</summary>
    public void AddHead(string rawHtml);

    /// <summary>The raw head markup added so far, in the order it was added.</summary>
    public string Head { get; }

    /// <summary>Writes the already-rendered body of the page this layer wraps.</summary>
    public void WriteBody();
}
```

`SetLayout` normalises the value as a [PagePath](../../src/uk.osric.asp.Core/Pages/PagePath.cs:66)
and refuses a value that is not addressable (a `?` or `#`). It takes a page key, not a URL, and the
last call wins. `Title`, `Head` and the existing `AllowScripts` are shared across the chain;
`WriteBody` is per layer and writes the immediately inner body.

A page:

```html
<% ctx.SetLayout("/_layout");
   ctx.Title = "About";
   ctx.AddHead("<meta name=\"description\" content=\"A small site about small things.\">"); %>
<h1>About</h1>
<p>Everything here is a page.</p>
```

A layout:

```html
<%== PageDocument.Begin(ctx.Title ?? "uk.osric.asp", head: ctx.Head) %>
<header>uk.osric.asp</header>
<%== PageDocument.Main() %>
<% ctx.WriteBody(); %>
<%== PageDocument.EndMain() %>
<footer>Written in C#, served from SQLite.</footer>
<%== PageDocument.End() %>
```

A layout with a `<header>` before `main` reopens DEC-121: the seeded layout adds a skip link when it
starts putting navigation there. The first seed below avoids that decision.

## Decisions

### 1. The layout is a page rendered at runtime, not compiled into the content

The content is rendered into its buffer, then the layout is loaded and rendered into a second
buffer with the first body available. This is the only shape that gives all three required
properties at once:

- The content's title and head contributions exist before the layout writes `<head>`, because the
  content has already run.
- The layout keeps its own image. A layout edit recompiles the layout; the content's
  `TemplateFingerprint` and `PageImage` do not change, so no dependency table or layout hash is
  needed in the content image.
- Each layer can be bound to its own path, so the layout's `ctx.Storage` is the layout's, not the
  content's.

A compile-time include (the "library page" option) was rejected for the same three reasons. It
would put the layout's source inside every dependent page's image, so a layout edit would recompile
every page; it could not read the content's title before the generated head without a hoisted setup
block; and it would collapse both pages onto one `ctx.Path`. A shared assembly has the same
dependency and invalidation costs and gives code, not markup. The SDK is the right home for the
stable builder, not for the editable chrome.

### 2. The selector is `ctx.SetLayout`, not a template directive

`SetLayout` is a response setting like `SetStatus`. It needs no tokenizer, generator or `Core`
change, it chains naturally when a layout names another layout, and a page can choose a layout
conditionally.

The cost is that a wrong path is a render-time error rather than a save-time one. That is acceptable
in this phase: the error names the layout, the page's own source still saves normally, and the editor
is a shell view, so the owner can repair the layout or the page without a redeploy. A directive that
provides a static, save-time-validated default can be added later without changing the runtime
chain; it would sit beside `SetLayout`, not replace it.

The value is a page key, not a URL. `PagePath.Normalize` makes it absolute and lower-case, and
`IsAddressable` rejects a value that contains `?` or `#`. A layout path is never interpreted
relative to the current page.

### 3. The content runs first, so the layout can read `Title` and `Head`

The layout's `<head>` is written before its body slot, but the content has already run by then. That
is what makes a document title and extra head markup possible without a setup block or a second
pass.

- `Title` is a shared plain value. The content sets it; a layout may read, replace or append to it;
  an outer layout sees the inner layout's value. The SDK builder encodes it, so a title from request
  data is safe when the layout uses the builder. A hand-written layout that writes
  `<%== ctx.Title %>` is choosing raw output, exactly as it does today.
- `AddHead` appends raw markup to a shared head slot, and `Head` returns it. The builder writes it
  after the shell sheets and the icon, so a page's own sheet or meta still comes last. The content
  is owner code; raw head markup is no more authority than `ctx.WriteRaw`.
- A page with no layout may still call `AddHead`; nothing writes the slot, so it is a no-op. A page
  that owns its whole response writes its own head as it does today.
- `BodyClass` is deliberately not added. The layout owns the `<body>` attributes; a page that needs
  a distinct class can wrap its body in an element. If a real need appears, it is one more shared
  value beside `Title`, not a new mechanism.

### 4. Each layer has its own `Path` and storage; the request is shared

`SimpleContext.Path` becomes the key of the layer being rendered:

- For the content, it is the requested page, exactly as today.
- For a layout, it is the layout's key.

`ctx.Request.Path` remains the page the visitor asked for. This is the rule IDEA-010 also needs for
an asset URL, and it should be settled once:

> `ctx.Path` names the page whose code is running; `ctx.Request.Path` names the page the visitor
> asked for.

`ctx.Storage` is created per layer and bound to that layer's key, so a layout cannot read or write
the content's state by accident. The layout's own state is shared by every page that uses the
layout, which is useful for chrome configuration and is the same last-write-wins store a page
already has. A layout that needs site-wide data reads it from its own store, not the content's.

Logging, fetch metrics and the body-limit error use the layer's key. A layout's log line therefore
says `Page /_layout`, and the request's access log still says the content's URL.

### 5. Response state is shared; the outermost layer is the response

Move the response fields out of `SimpleContext` into an internal shared state owned by the render
session:

- status, content type, headers and location;
- `AllowsScripts`;
- `Title` and the head slot;
- the fetch count and the body count;
- the cached antiforgery field.

Each `SimpleContext` keeps its own writer, its own `Path`, its own storage, its own abandon flag and
its own next-layout value. `SetStatus`, `SetContentType`, `SetHeader`, `Redirect` and `AllowScripts`
delegate to the shared state. Last writer wins for the scalar values; `AllowScripts` is an OR.

The content normally owns the response semantics. Because a layout runs last and is owner code, the
implementation uses last-writer-wins on the shared state: a layout that sets a status or header
overrides the content, and a layout that does not touch them leaves the content's values in place.
A layout that redirects discards the content body and redirects. The docs and the seeded layout
should say that a layout is presentation, and that a content page that redirects ends the chain
before the layout starts. `ToResponse()` reads the shared state and stays the endpoint's only view
of the response.

### 6. A redirect or a non-HTML response ends the chain

After each layer, the session inspects the shared response. The chain stops when:

- `Location` is set (the content redirected, or an inner layout did);
- the content type is not `text/html` (a CSS, JavaScript, JSON or SVG page does not want a
  document);
- the status is `204` or `304` (there is no document to wrap).

A page that sets a layout and then answers with CSS is not wrapped. A layout that changes the
content type after the content has rendered is owner code doing something deliberate; the response
is whatever it says, and the body is the layout's. The chain does not try to second-guess it.

### 7. The chain is bounded, and a broken layout is a layout error

The session keeps a `HashSet<string>` of normalised layout keys and a `MaxLayoutDepth` constant
(8). A repeated key is a cycle; exceeding the limit is a depth error. Both are page-author errors
and produce a `LayoutFailed` result naming the offending key.

A missing layout, or a layout that does not compile, also produces `LayoutFailed` with the layout's
key and diagnostics. It is **not** a `NotFound` for the requested page: the page exists; its
document is broken. The endpoint answers `500` with the shell's error page, which names the layout
and shows the diagnostics. The error page itself uses `ShellDocument`, so it does not depend on any
layout and the editor remains reachable.

A layout that times out yields `Timeout` with the failed path set to the layout, so the log and the
existing `504` page name the layout rather than pretending the content timed out.

### 8. One slot, deadline, fetch budget and body cap for the whole chain

`PageRenderer.RenderAsync` acquires the render slot once and creates the deadline once; every layer
runs inside that slot and deadline. `LoadAsync` and `RunAsync` are called per layer, so each layer
still gets its own cache lookup, compile, load and invoke metrics. The content and the layout share
the compile budget because the layout compiles inside the render's deadline; a compile that outlives
the deadline is still stored, as today.

The shared fetch count enforces `PageFetchOptions.MaxRequestsPerRender` across the chain. The shared
body count enforces `Pages:Render:MaxBodyChars` across the chain, so two layers cannot each fill a
buffer to the cap. The shared antiforgery cache means a content form and a layout form in the same
request use one token.

`page.render.*` remains one sample per request, on the requested page and covering the chain.
`page.invoke.*`, `page.compile.*` and `page.cache.*` remain per layer and carry the layer's path. A
new `page.layout.depth` histogram records how many layouts a request used, so the dashboard can see
the chain without guessing from the invoke count.

### 9. The SDK contract is five members, not a new page kind

`SetLayout`, `Title`, `AddHead`, `Head` and `WriteBody` are additions to `IContext`; nothing in
`IHandler`, `PageRequest`, `PageResponse`, the compiler or the page store changes.

- `SetLayout` is an action; it records the next layout key for this layer.
- `Title` is a shared value with a setter, because a layout may append to what the content set.
- `AddHead` appends raw markup; `Head` returns the slot. They are separate because the layout needs
  to read the slot to pass it to the builder.
- `WriteBody` is a method, not a property, so the layout cannot accidentally escape the already
  rendered HTML by writing it through `ctx.Write`.
- `WriteBody` on the content layer is a no-op; calling it twice on a layout writes the body twice,
  which is the layout author's choice.
- `AllowScripts` is already shared and now spans the chain.
- `PageResponse` stays the HTTP response record. The layout's title and head are document
  composition, not HTTP metadata, so they live in the internal shared state and the SDK contract,
  not in `PageResponse`.

The members are in the `uk.osric.asp.Sdk` namespace, so the page API policy allows them with no new
entry. `PageDocument` and `Nav` are also in `Sdk`; a page can call them because the SDK namespace is
allow-listed. `System.Net` stays denied; the builder keeps `WebUtility` inside the SDK.

### 10. The SDK document builder is split, and the shell and the seed share it

IDEA-018's `PageDocument` is not one `Begin`/`End` pair. It has three pieces:

1. the doctype, `<html>`, `<head>` and the open `<body>` (the `Begin` piece);
2. the open and close of `<main id="main">` (the landmark piece);
3. the close of `</body>` and `</html>` (the `End` piece).

The split is what lets a layout write a header before `main` and a footer after it without
hand-rolling the head. The shell's `ShellDocument.BeginAsync` composes (1) and (2), and `EndAsync`
composes (2) and (3), so every existing shell view writes exactly the same markup as today. The
seeded layout calls (1), writes its chrome, calls (2), writes the body, calls (2)'s close, writes
its footer and calls (3).

The builder encodes the title and the stylesheet hrefs, and writes `/layout.css`, `/site.css`, the
view's own sheet, the icon and then the page's raw head slot, in that order. It carries no status,
headers, CSP or caching; those stay the shell's (`SecurityHeaders`, `PageEndpoint`). This is the
IDEA-018 rule and it is what keeps a document builder from weakening a response.

### 11. The seed is `/_layout`, protected and restorable

Add `src/uk.osric.asp.Web/Pages/Seeds/_layout` (no extension, so the seed key is `/_layout`) using
the builder. The first seed is deliberately minimal:

- `PageDocument.Begin` with the content's title and head;
- `PageDocument.Main`, `WriteBody`, `EndMain`;
- a footer after `main`;
- `PageDocument.End`.

No header or nav before `main`, so DEC-121 is unchanged in the first cut. A nav comes later with
the skip link it requires; that is a separate product decision, not a hidden consequence of adding
a layout.

The seed is a page, so:

- `PageAssetSeeder` seeds it if it is missing and leaves a tombstoned one alone;
- the `/edit` page's restore action brings it back from the bundled file;
- import and export carry it like any other page;
- `PageAssetSeederTests.SeedPaths`, the seed integration tests and the restore tests gain `/_layout`;
- `AssistantOptions.ProtectedPaths` gains `/_layout`, so the assistant cannot rewrite the site's
  framework by accident. The owner can still edit it in the editor.

A page that uses the layout and finds it deleted gets a `LayoutFailed` error naming `/_layout`, not
a broken half-document. The editor is a shell view and stays available to fix it.

### 12. No schema change; `SdkMvid` invalidates images once

There is no `PageImage` column, no migration and no change to `PageTemplate` or `PageImage`. The
layout is a page; its source and image are stored exactly like the content's.

Adding members to `IContext` and `PageDocument`/`Nav` to `Sdk` changes the SDK module version id, so
`RuntimeId` changes and every stored image is recompiled on its next request. That is the normal
contract for an SDK change and needs no special handling. `Core` does not change, so the generator
and the template language are untouched.

The depth limit is a constant, not configuration: `n` is small and a lower limit is safer than a
setting an operator can raise. The existing `Pages:Render:*` and `Pages:Fetch:*` limits are reused;
no new options section is added.

## Runtime

### `PageEndpoint`

The request snapshot, the user mapping, the antiforgery validation, the form limit and the final
response writing stay in `PageEndpoint`. What changes is that it no longer constructs one
`SimpleContext` and hands it to the renderer. It constructs a `PageRenderSession` (internal to
`Web.Pages`) with the shared services and the request, then calls the renderer with the session and
the requested key.

After the render, the endpoint reads `session.Response` and `session.Body` instead of
`page.ToResponse()` and `page.GetBody()`. The ETag, `Content-Length`, `If-None-Match`, the
authenticated `no-store` rule and `SecurityHeaders` are unchanged; they apply to the final body of
the chain.

The session is the seam that keeps the endpoint from knowing about layers:

```csharp
internal sealed class PageRenderSession {
    public PageRenderState Response { get; }
    public string Body { get; }

    public SimpleContext CreateContext(PagePath path, TextWriter writer, string? innerBody);
    public void AttachDeadline(CancellationToken deadline);
    public void Abandon(SimpleContext context);
}
```

### `PageRenderer`

`RenderAsync` keeps its slot/deadline/metrics shape and gains the chain:

```text
acquire one render slot inside one deadline
for each layer:
    load or compile the layer (LoadAsync)
    run the layer with a context bound to the layer key and the inner body (RunAsync)
    if the layer failed: return the failure, naming the layer
    if the response redirected, is non-HTML or has no body: stop
    if the layer set no next layout: stop
    next = normalised layout key
    if next is in seen: return LayoutFailed(cycle)
    if depth == max: return LayoutFailed(depth)
    seen.add(next)
return the final layer's body and the shared response
```

`LoadAsync` and `RunAsync` stay per layer and keep their existing compile/store/abandon behaviour.
The render slot is released once, after the whole chain. `RecordRender` is called once, for the
requested page; the per-layer `RecordInvoke`/`RecordCompile`/`RecordCacheLookup` calls remain.

### `SimpleContext` and `PageRenderState`

Introduce an internal `PageRenderState` owned by the session:

- `int Status`, `string ContentType`, `Dictionary<string, string> Headers`, `string? Location`;
- `bool AllowsScripts`;
- `string? Title`, `StringBuilder Head`;
- `int Fetches`, `int Written`;
- `string? AntiforgeryField` and the shared lock.

`SimpleContext` takes the state, the layer's `PagePath`, the layer's writer and the inner body. Its
`Path` returns the layer key; its `Request` returns the snapshot. Its response members delegate to
the state. Its `_abandoned` flag, `_writer`, `_boundStorage` and next-layout value stay per instance.
`GetBody()` returns the layer's writer contents. `ToResponse()` reads the state.

The body counter must be shared and locked under the same lock as the writers, or the cap can be
overshot by two layers writing concurrently. In this chain only one layer runs at a time, but the
abandoned-page continuation can still write after the response has gone, so the existing per-context
lock moves into the shared state, or the writer and counter are guarded by a state-level lock.

### `PageStorageScope`

The session creates one `PageStorageScope` per layer, bound to that layer's key and the same
`writable` flag (`!HEAD`). The scope's `AttachDeadline` and `Abandon` are called for the active
layer. A content render that writes its own state and a layout that writes its own state are two
independent stores, which is the point.

### Metrics

- `page.render.duration`, `page.render.wait` and `page.render.abandoned` keep their meaning for the
  request; `page.render.duration` now includes the layout layers.
- `page.invoke.duration`, `page.compile.duration`, `page.image.size` and `page.cache.*` keep their
  per-layer meanings and use the layer's path.
- Add `page.layout.depth` (histogram, no path tag) and `page.layout.render` (counter, tagged with
  the layout path and outcome). The dashboard can show layout depth without changing the existing
  render series.
- A layout that is abandoned is counted in `page.render.abandoned` with the layout's path in the
  log; the request's outcome remains `timeout`.

## Diagnostics and the error page

Add a `LayoutFailed` render status carrying:

- the failed layout key;
- the layout's compile diagnostics, when it did not compile;
- a synthetic `PAGE005` diagnostic for a missing layout, a cycle or a depth overflow.

Suggested messages:

| Failure | Message |
| --- | --- |
| missing | `PAGE005: the layout /_layout does not exist.` |
| cycle | `PAGE005: the layout chain repeats /_layout.` |
| depth | `PAGE005: the layout chain is deeper than 8.` |

A layout compile failure uses the compiler's diagnostics with the layout's `#line` positions, and
the error page names the layout. The page's own compile errors stay `CompileFailed`; the endpoint
can name the requested page on the same error page.

`PageEndpoint` gains a `WriteLayoutErrorAsync` beside `WriteCompileErrorAsync`. It uses
`ShellDocument`, so the error page does not depend on the layout it is reporting. The title is
"Layout failed", the body names the key and shows the diagnostics. The editor, the health endpoint
and the metrics endpoint remain shell views and keep answering.

## SDK and policy

- `IContext` gains the five members; `IHandler`, `PageRequest` and `PageResponse` do not change.
- `Sdk` gains `PageDocument` and `Nav`/`NavLink` from IDEA-018; `PageDocument` is split as described
  in decision 10. `PageLayout` in `Web/Html` keeps only the shell's adapter and whatever fragments
  are not yet in `Sdk`. Because the Web type already uses the name `PageLayout`, the SDK fragments
  need a distinct name (for example `PageFragments`) or the Web type is deleted and its callers
  move to the SDK type.
- `PageApiPolicy` needs no new entries: the SDK namespace is already allowed, and `System.Net`
  remains denied. The builder does the encoding inside the SDK.
- Add a policy test that a page can call `PageDocument.Begin`, `ctx.SetLayout`, `ctx.Title`,
  `ctx.AddHead`, `ctx.Head` and `ctx.WriteBody`, and still cannot reach `System.Net` or
  `System.IO`.
- The SDK module version id changes, so `RuntimeId` invalidates every stored image once. No
  `FormatVersion` or `PolicyVersion` bump is needed: the contract is additive.

## The seeded layout

The seed is the first consumer of IDEA-018 and the first proof that a page can be a document. It
uses only the SDK builder and the new context members, so it is also a worked example for the
assistant and the docs.

```html
<%== PageDocument.Begin(ctx.Title ?? "uk.osric.asp", head: ctx.Head) %>
<%== PageDocument.Main() %>
<% ctx.WriteBody(); %>
<%== PageDocument.EndMain() %>
<footer>uk.osric.asp</footer>
<%== PageDocument.End() %>
```

If the owner later adds a header or nav before `main`, the seed and the docs must add a skip link
and DEC-121 changes. That is a deliberate product change, not something the layout mechanism
decides by itself.

## Documentation and decisions

- `docs/architecture.md`:
  - the page contract lists the new members and the `ctx.Path` / `ctx.Request.Path` rule;
  - "The document shell" explains that the shell and the seeded layout share `PageDocument`, and
    that a stored page can opt into the layout;
  - the execution budget explains that one slot and one deadline cover the chain.
- `docs/decisions.md` gains three proposed entries:
  - **DEC-142** — A page layout is a stored page rendered around a page at runtime. The content
    runs first, each layer keeps its own image, one request keeps one slot and deadline, a redirect
    or non-HTML response ends the chain, and a layout is presentation rather than a guard.
  - **DEC-143** — `IContext` gains `SetLayout`, `Title`, `AddHead`, `Head` and `WriteBody`;
    `ctx.Path` is the layer's key and `ctx.Request.Path` is the requested page.
  - **DEC-144** — The SDK document builder is split into head/body, main and close so the shell and
    a layout page write one document; the builder carries no status, header, CSP or caching policy.
- `docs/testing.md` gains the layout chain matrix and the page-built-document accessibility check.
- `docs/glossary.md` gains "layout page" and "layer".
- `docs/jobs/IDEA-005.md` status moves to "phase 2 implemented" when this ships; `IDEA-018` closes
  when the builder is in `Sdk`.
- `Assistant/PageAuthorPrompt.cs` gains a short layout section and a worked example. The `IContext`
  reflection already lists the new members; `ProtectedPaths` gains `/_layout`.
- `docs/plans/archive/page-declarations.md` is history and is not rewritten.

## Implementation phases

### Phase 1 — The SDK document builder (IDEA-018)

Move the head/sheet/landmark markup and the `Nav`/`NavLink` fragments into `Sdk`. Split
`PageDocument` into the head/body, landmark and close pieces. Make `ShellDocument` a thin adapter
over it. The layout only needs `Nav`/`NavLink`; `Gutter` and `Diagnostics` can wait for the SDK
diagnostic value in IDEA-016 and the editor work in IDEA-017. No page can use a layout yet.

Done when the shell's output is byte-for-byte the same as today, `ShellDocumentTests` and the
accessibility suite pass unchanged, and a page can call `PageDocument` from the page API policy.

### Phase 2 — The shared response state

Introduce `PageRenderState`; move `SimpleContext`'s response fields, counters and antiforgery cache
into it. `SimpleContext` still renders one page, and `Path` still equals `Request.Path`. No SDK
change yet.

Done when every existing `SimpleContext`, `PageRenderer`, storage, fetch and metrics test passes
with only constructor churn.

### Phase 3 — The contract and a single layout

Add the five `IContext` members. Add the layer `PagePath` to `SimpleContext` and the per-layer
storage scope. Implement the chain for one layout: content, then layout, body and title/head
sharing. Wire `PageEndpoint` to the session. Add `LayoutFailed` and the layout error page.

Done when a page can set a layout and the rendered document contains the layout's head and chrome
around the content's body; `ctx.Path`/`Request.Path` and storage isolation are tested; a redirect
and a non-HTML page skip the layout; and a missing or broken layout produces the layout error page.

### Phase 4 — Nested layouts, limits and metrics

Allow a layout to set another layout, add the depth limit and the seen-set, add the layout metrics,
and cover timeout/abandon with a layout layer.

Done when a two-level chain renders correctly, a cycle and a depth overflow fail cleanly, a layout
timeout names the layout, and no chain can exceed the fetch or body caps.

### Phase 5 — The seed, the assistant and the docs

Seed `/_layout`, protect it, restore it, add the assistant prompt section, update the docs and
record the decisions.

Done when a fresh install has a working `/_layout`, a page that sets it renders, editing the
layout changes the page without recompiling the page, deleting the layout produces the layout error,
restoring the seed recovers, and the integration suite covers the path.

### Phase 6 — The first consumer (follow-on)

Move one shell view into a page using the layout, with IDEA-015/017 as the prerequisites. This is
the proof that the framework is usable, but it is not part of this plan's code.

## Test matrix

| Test | What it proves |
| --- | --- |
| content + layout render in that order | the body slot and chrome order |
| content sets `Title`/`AddHead`, layout reads them | content-first ordering |
| layout sets `Path` differently from `Request.Path` | per-layer path contract |
| layout `Storage` cannot read content `Storage` | per-layer storage isolation |
| content redirects | no layout runs |
| content is CSS/JavaScript | no layout runs |
| missing layout | `LayoutFailed` names the key, not 404 |
| layout compile error | layout diagnostics and the layout key |
| layout cycle | `LayoutFailed` names the repeated key |
| depth 9 | `LayoutFailed` names the limit |
| `HEAD` with a layout | both layers render, nothing is written |
| form in content and form in layout | one antiforgery token |
| fetch in content and fetch in layout | the shared per-render limit |
| body over the cap in content and layout | the shared cap |
| layout edit only | the content image is reused; the layout image recompiles |
| layout edit | the ETag changes; `If-None-Match` revalidates |
| layout timeout | `504` names the layout |
| layout render metrics | `page.render.*` stays one sample; `page.layout.depth` records the chain |
| shell output | `PageDocument` matches today's `ShellDocument` |
| seeded layout | passes the accessibility rules; is restorable and protected |
| page API policy | new members and `PageDocument` are allowed; `System.Net` is not |

## Risks and what I would not do

- **A layout can break every page.** That is inherent in sharing one; the editor is a shell view,
  so recovery does not depend on the layout. The seed is restorable and protected from the
  assistant.
- **Response semantics are easy to misread.** Last writer wins, content redirect ends the chain, and
  a layout cannot guard. Document all three in the `IContext` comments and the architecture page.
- **`ctx.Path` changes meaning for layouts.** Test it, and settle the same rule for IDEA-010's
  asset URLs. Do not silently rewrite one field.
- **Two buffers and two handler invocations.** The body cap and the one-slot rule bound it; `n` is
  small. Do not add a layout cache or streaming to optimise it before a measurement.
- **The depth limit is a constant.** A deeper chain is a page-author bug, not a configuration
  choice; do not make it an operator setting.
- **The seed is a page key.** Do not introduce a page kind, a column or a reserved path. The layout
  is an ordinary page that happens to be used as one.
- **Do not add a template directive, a general `RenderPageAsync`, a partial API, layout-level
  authorization or shared types here.** Each is a separate decision with its own dependency and
  invalidation story.

## What this does not settle

- `BodyClass` and other `<body>` attributes. Deferred; a wrapper element covers the immediate need.
- A static layout directive for save-time validation. Deferred until the runtime-error trade is
  measured against real pages.
- Partials and arbitrary fragment rendering. Deferred; one body slot is the whole feature.
- Shared types and library pages. Deferred until a concrete page-authored helper needs them; prefer
  the SDK.
- A nav before `main`. It is a product decision because it brings back the skip link (DEC-121).
- Immutable asset URLs in the layout (IDEA-010). The `ctx.Path` / `ctx.Request.Path` rule here is a
  prerequisite, not the implementation.

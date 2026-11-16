<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-049 — A page cannot render another page

| | |
| --- | --- |
| Size | L — one `Sdk` capability with four calls and two values, one layer renderer with three roles, the layout chain rebuilt on it and triggered by the page, `IPageLayout.Layout` and the automatic wrap deleted, two `Pages:Render` values, the shell-document policy and the trim it allows, every seeded page, the manual, the assistant's prompt, the docs and the tests that pin the recursion rules. It wants a plan |
| Area | Sdk, Web (pages, shell, seeds, assistant), docs |
| Location | `src/uk.osric.asp.Sdk/IPageComposition.cs`, `src/uk.osric.asp.Sdk/PageFrame.cs`, `src/uk.osric.asp.Sdk/PageCapture.cs`, `src/uk.osric.asp.Sdk/PageRequestExtensions.cs`, `src/uk.osric.asp.Sdk/IPageLayout.cs`, `src/uk.osric.asp.Web/Pages/PageCompositionScope.cs`, `src/uk.osric.asp.Web/Pages/PageChain.cs`, `src/uk.osric.asp.Web/Pages/PageEndpoint.cs`, `src/uk.osric.asp.Web/Pages/PageRenderer.cs`, `src/uk.osric.asp.Web/Pages/SimpleContext.cs`, `src/uk.osric.asp.Web/Pages/SimpleResponse.cs`, `src/uk.osric.asp.Web/Pages/PageLayoutState.cs`, `src/uk.osric.asp.Web/Pages/RenderOptions.cs`, `src/uk.osric.asp.Web/Pages/PageMetrics.cs`, `src/uk.osric.asp.Web/appsettings.json`, `src/uk.osric.asp.Web/Html/ShellDocument.cs`, `src/uk.osric.asp.Web/Html/SiteNavigation.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs`, `docs/pages.md`, `docs/shell.md` |

> **Raised 2026-11-16, refined the same day.** A sketch, written to be argued with. Four answers moved
> it: an embed is offered the caller's capabilities including the write and the jobs; capture waits;
> the embedded page sees the caller's request, method and all; and a no-code page embeds as the
> fragment it is. The fifth answer moved the centre of gravity: the layout chain is not a follow-on,
> it is the render the chain should have been built on, and the shell's own documents are allowed to
> become plain.
>
> Re-read after `pages-the-response-is-one-object` landed on trunk. It helps: the embed writes through
> the calling layer's `IPageResponse`, which is exactly the narrow seam that split created, and the
> flag the embed has to generalise is now on `SimpleResponse`, where the response rule already lives.
>
> The owner's `WrapAsync` note moved it again: the capability is `IPageComposition`, with one call that
> **includes** another page, one that **captures** its body as a string, and one that **wraps** this page
> in another. The wrap is a declaration —
> `Wrap`, not `WrapAsync` — because the host performs it after the handler returns and every exit path
> is then wrapped. That is the general form of the layout, and it is what lets `IPageLayout.Layout`
> and the host's automatic wrap go without losing the slot.

## Idea

A stored page can read another page's **source** (`IPageCatalog`) but not its **output**. So every
view that wants "this page, here" writes the markup again: an include, a sidebar, a footer, the same
card drawn from a table row. The layout chain already knows how to run one stored page inside
another; it just only ever does it once, from outside, with a document-shaped contract.

The general primitive is an **embed**: a page asks the host to render another stored page, with the
request the caller supplies, and the embedded page's body is written where the call is. Everything
below the request and the output — its key, its storage, its session, its identity, its capabilities,
the request's deadline and allowances — is the embedded page's own, exactly as it would be if a
visitor had asked for it.

The layout chain is then the same render seen from the other side: the page that asks to be wrapped
hands its finished body to a wrapper, which writes around it, and may itself ask for another; a page
may ask for several, and they stack outward from its body. This job makes that literal. There is **one layer renderer** and **three roles**, and a page says which
call it wants — include another page here, or hand itself to one — so the chain's rules stay, as
policy over a render it no longer owns.

## What the caller learns, and what it does not

- An embed is **not a guard**: the embedded page runs, and its own `ctx.User` guard decides what it
  writes. A caller cannot use an embed to see a page an anonymous visitor could not; and cannot use
  it to hide one either.
- An embed is **not a sandbox**: the embedded page has the capabilities a content page has, spends
  the same request budget, and can do the same side effects. Embedding is composition, not isolation.
- An embed is **not a select**: `IPageCatalog.ReadAsync` answers "what does this page say"; an embed
  answers "what does it produce, for this request". A no-code page's body happens to be its source,
  less its comments, exactly as when it is served.
- **No embed flag is added to the request.** The embedded page sees a request — the one it was given,
  or the caller's — not a marker. A page that must know it is inside another notices the structural
  difference: `ctx.Path` names the embedded page while `ctx.Request.Path` is still the visitor's
  page, which is the same difference a layout layer already shows.
- **The outer page chooses.** Every inner render yields a `PageFrame` — title, head, sheets
  and script permission — and the page that owns the document decides what to fold. `EmbedAsync`
  returns that frame (the body is already written); `CaptureAsync` returns a `PageCapture`,
  the frame plus the body. It is the same rule in both directions: `/a` may `Embed("/b")` and
  fold what it wants, and `/b` may `Wrap("/a")` and `/a` folds what it wants.
- **A page chooses its wrappers; nothing wraps it by default.** `Wrap()` asks for the site's layout,
  `Wrap(key)` for a named one and `Wrap(key, request)` for a named one with its own input. Each call
  adds a layer outside the last, so `Wrap("/_markdown"); Wrap()` is markdown then the site layout. A
  page that asks for none is served exactly as it wrote itself, and the host no longer applies
  `Pages:Layout` on its own.

## Sketch

### The contract

`IPageComposition` is a capability, reached like every other, offered to every layer a render has —
the content, any layout the chain adds, and any page already embedded — because a layout embedding a
header and footer partial is the use that argues for this most:

```csharp
public interface IPageComposition {
    Task<PageFrame> EmbedAsync(string key, PageRequest? request = null);
    Task<PageCapture> CaptureAsync(string key, PageRequest? request = null);
    void Wrap(string key, PageRequest? request = null); // each call adds a layer; first is innermost
    void Wrap();
}

public record PageFrame(
    string? Title,
    string Head,
    IReadOnlyList<string> Stylesheets,
    bool AllowsScripts);

public sealed record PageCapture : PageFrame {
    public PageCapture(string body, PageFrame frame)
        : base(frame.Title, frame.Head, frame.Stylesheets, frame.AllowsScripts) => Body = body;

    public string Body { get; }
}
```

`EmbedAsync` includes another page here and returns its `PageFrame`: the title, head, sheets
and script permission — **never the body**, because the body is already written where the call is.
The page's own wrappers run, so the frame and the body are the outermost layer's. `CaptureAsync`
returns the `PageCapture` when the body is what you want. `Wrap` hands this page's finished output to
another page; each call adds a layer around the last, so the first is the innermost and the last is
the outermost layer the page names — a wrapper may add its own outside it. `Wrap()` uses the site's layout, `Pages:Layout`, so a page does not name a
key the owner can move. A page does:

```html
<% await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/cards/post",
       ctx.Request.WithQuery("id", "7")); %>
```

(`Wrap` is deliberately not `WrapAsync` — see [the timing below](#wrap-is-a-page-call).)

**The input is the request the embedded page runs against.** With no request it gets the caller's;
with one it gets exactly that request, with `Path` pinned to the visitor's page. The copy-and-change
helpers — `WithQuery(name, value)`, `WithAnotherQuery(name, value)` and `WithoutQuery(name)` — are
**extension methods** on `PageRequest` in `Sdk/PageRequestExtensions.cs`, so the sealed record keeps
its shape and the helpers are an additive surface; forwarding most of a query and overriding one name
is a value copy rather than a new type. There is no separate options type: the page's input is the request, and the same
request is the input to a wrapper and to an embed.

### What the embedded page sees

**The request you give it, defaulting to the caller's.** The embedded page runs against the request
passed to `EmbedAsync` — same method, same form, same headers, same cookies, with that request's
query — and `Path` is pinned to the visitor's page. That is what makes an embed a render of a request
rather than a second, invented one: a `POST` that renders a summary gives the summary the `POST` and
the form it submitted.

Two consequences belong in the manual, because both will surprise somebody:

- **An embedded page that branches on `ctx.Request.Method` or reads `ctx.Request.Form` runs that
  branch whenever its caller was posted.** That is the honest reading of "the same request", and it
  is why an embed is composition, not a widget sandbox.
- **`ctx.Path` is the embedded page's key; `ctx.Request.Path` is still the visitor's page.** A link
  the embedded page writes must therefore be site-absolute (`ctx.Path + "?…"`), because the browser
  resolves a relative link against the document that was asked for, not against the embed.

Its `IPageStorage` and `IPageSession` are bound to its own key, so a widget keeps its own state. Its
`ctx.EditorPath` and `ctx.Response.AntiforgeryField` are the caller's, because they are facts about
the site and the request: a form inside an embed carries the same antiforgery pair as the rest of the
document.

Its `IPageLayout` slots are its own; if it declares wrappers, the frame an embed returns is the
outermost layer's — title, head, sheets and script permission — for the caller to fold into its own
slots or ignore; the body comes back only from `CaptureAsync`, as a `PageCapture`. Nothing is merged automatically: the caller
owns the document, so the choice is the caller's.

**A no-code page embeds as the fragment it is.** Its body is its stored source less its comments,
the same projection a served no-code page gets. Today that projection lives in
`PageEndpoint.WriteSourceAsync`, which is the shell's serving path; it moves into the layer render so
the embed cannot be a way for a `<%# … %>` to reach a visitor. A library or a worker is not a page
and cannot be embedded — the same rule that keeps one out of a layout chain.

### What it may not do

An embedded page is a content render with the **response withheld**. It cannot set the status,
content type or a header, it cannot redirect, and it cannot flush: all of those belong to the
document the visitor asked for. Today that rule is spelled `isLayout` on `SimpleResponse`, and that
flag also strips the writes, the jobs and the session writes — so the flag becomes a **layer role**
(content, layout, embedded) on `SimpleResponse` and its construction. An embedded page keeps the
capabilities a content page has; only the response is not its.

That includes the write and the jobs, because the authority is the caller's identity: if the caller
can write, so can the page it embeds, and the one-write-per-render and job rules still bound it. A
layout is refused those because it runs once for every page it wraps; an embed runs because its
caller asked for it, once, in a line of code.

A redirect is the sharp case: silently ignoring it makes the ordinary "if no user, redirect to login"
guard vanish and leave an empty box. An embed that redirects should **refuse** — the caller learns its
widget was a guard — rather than pretend it produced nothing.

`Wrap` is held to the same role rules as the layout it replaces: a page may call it repeatedly, and
each call adds a layer; on an **embedded** layer it runs like any other — the embedded page declares its chain and the
host renders it, so a page does not have to know it is embedded to render the same way. The wrapper
is a layout layer — response withheld, no flush, no writes or jobs — because it is markup around the
page the visitor asked for. A wrapper must have code: a no-code page has no way to read `IPageLayout.Content`, so
its stored source would replace the page instead of wrapping it.

### The chain is built on this render

The layout chain is not a second way to run a page: it is a **policy over the same layer render**, and
the page now says when to enter it. A render is a **tree**: the page's body and the wrappers around it
are one spine, and every layer may embed, which is a child render with a spine of its own. The host
walks it by making the calls — there is no separate traversal — and the ancestor stack is the path
down the tree.

- One layer renderer takes a key, a request, a body slot and a role, and answers the body it wrote.
  The **content** role owns the response; the **layout** role withholds it and is handed the
  content's `PageCapture`; the **embedded** role withholds it and takes no slot of its own.
- A request is then: render the requested key as content; for each wrapper it declared, in order,
  render the key as a layout with the previous layer's `PageCapture` as `IPageLayout.Content`; a
  layout that called `Wrap` continues the chain the same way; write the outermost body. `PageChain`
  is that loop, over the same render an embed uses instead of a duplicate of it, and an embed runs
  the same loop for the page it includes.
- The chain's rules do not disappear. The outermost layer still owns the document and a wrapper that
  cannot be served is an error naming the key. The **title is the wrapper's concern now**:
  `IPageLayout.Title` becomes nullable, the host stops refusing the chain for a missing one, and a
  wrapper decides whether to supply a default, refuse, or compose. What changes is only **who asks**:
  the page, not `Pages:Layout`.
- `IPageLayout.Layout` and `IPageLayout.Body` are deleted. `IPageLayout` gains `Content`, the inner
  layer's `PageCapture`, and keeps `Title`, `Head`, `Stylesheets`, `AddHead` and `AddStylesheet` as the
  layer's **own** slots. `Pages:Layout` stays as the key that `Wrap()` means.
- `Wrap()` keeps the default's fallback: a default layout that cannot be served is skipped with a
  warning, because the page did not name it, and the rest of the chain still runs. `Wrap(key)` is an
  error, as a named layout is today.
- The embed writes through the calling layer's **`IPageResponse`**, not its `IContext`: the response
  split already made the output a thing a helper can take on its own, and this is the first
  capability to use it. The caller folds what it wants from the returned frame into its own
  `IPageLayout`; the host never reaches across the two layers to merge them.
- The mechanics are small: the endpoint opens the render session before it builds the layers, or the
  embed capability takes a late-bound session. Either way one slot and one deadline still covers every
  layer of the request, as today.

That is the part worth doing now rather than later: an embed added beside the chain would be a second
way to render a page inside a page, which is the duplication this idea exists to remove.

The shell policy it proposed was that **the shell's own documents are bootstrap and recovery
surfaces** that carry one way home. That proposal is dropped: trunk unified the shell's routes into one
table (`Shell/ShellCatalogue.cs`), which draws the site menu and the administration menu from the same
values, so the duplication the trim targeted is gone and a visitor on an error page or `/status` keeps
the menu. `shell-the-menu-is-written-twice` stands as trunk edits it, and this job changes nothing in
the shell.

`/status` and `/assistant` are settled as shell documents for now. The objection is worth keeping
alive beside that: `/status` is the page you look at when the site is unwell, so making it a stored
page would put the health surface behind the thing it reports on. Revisit when the health route is
decided, not here.

### Wrap is a page call

An embed is an **include**: the caller runs, and another page's body appears inside it. A wrap is the
other direction: the content finishes, and another page writes around its body. The topology, with
`/about` wrapped by `/_layout`:

```
request /about
  → /about renders into buffer A (sets Title, AddHead, AddStylesheet), calls Wrap()
  → /_layout renders into buffer B with IPageLayout.Content = the capture of A
  → /_layout may call Wrap("/_outer", request) → buffer C with Content = the capture of B
  → write the outermost buffer
```

Every layer is a real render under the request's one slot and deadline, exactly as today; the
difference is that each layer names its parent instead of the host.

**When the handover happens.** It is a **declaration**: each `Wrap(key, request)` records a layer and
the request it runs against, and the host runs the chain after `HandleAsync` returns, where it runs
today. The request is
how a content page picks a wrapper variant without a second wrapper page, such as
`Wrap("/_layout", ctx.Request.WithQuery("theme", "dark"))`. The page writes its calls near the
top, one per layer, and every exit path is wrapped — including a guard that writes a short body and
returns. It is
not `WrapAsync`, because it does not await the wrapper; the waiting is the host's.

The literal alternative was considered and rejected: `WrapAsync(key)` renders the wrapper immediately,
replaces the page's body with the wrapper's, and ends the page, so any later use of `ctx` throws. It
reads as the sentence it is and makes the chain a call stack, but every exit path has to reach the
call — an early `return` that skips it silently serves a bare fragment — and the page must set its
status and headers before it. A silent trap in the common guard shape is too high a price for the
wording.

**What the wrapper sees.** The request passed to `Wrap`, or the content's when none was — same
method, form, headers and cookies — with `ctx.Path` the wrapper's key and `ctx.Request.Path` still
the visitor's page. Its `IPageLayout.Content` is the content's `PageCapture`: `Body`, `Title`, `Head`,
`Stylesheets` and `AllowsScripts`. The wrapper folds what it wants into its own `Title`, `AddHead` and
`AddStylesheet` — a default title from `Content.Title ?? "Site"`, the content's sheets beside its
own, the body where the document wants it — and whatever it contributes becomes its own capture for
its parent. Nothing is merged behind its back. The content's script permission is unioned
automatically, because the wrapper's document may carry its body; the wrapper's own `AllowScripts` is
unioned outward. Its `SetStatus`, `SetHeader`, `SetContentType` and `Redirect` are ignored and
logged.

**What the host no longer does.** No automatic wrap, no `Pages:Layout` applied to a page that did not
ask, no `IPageLayout.Layout`. The config value stays as the key `Wrap()` means. The shell's own
documents simply do not call it.

**A page renders the same chain wherever it is rendered.** A page that calls `Wrap()` is the document
when a visitor asks for it and also when another page embeds it; a page that should be a fragment
declares no wrapper. That is the cost of the page owning its whole render: one page cannot be both a
document and a partial, and a view that needs both is two pages — a fragment and a page that wraps
it — or the caller embeds the fragment. It is also what keeps the rule simple: an embed has no
special case, and `/a Embed(/b)` and `/b Wrap(/a)` reach the same shape. `Wrap` after the first flush
is refused, because a committed body cannot be handed over; the check that already stops a page that
would be wrapped from streaming covers the other order.

One consequence worth naming: a page that forgets its `Wrap()` is served as a bare fragment — valid
HTML the browser renders, without the site's chrome, title or sheets. That is the cost of the page
owning the decision, and the editor's new-page template plus `/help` are where it is made unlikely.

### Recursion and cost

Three bounds, because a cycle is not the only way one page becomes many:

- **An ancestor stack, not a set of everything rendered.** `PageChain` owns the layers currently on
  the stack — the requested page, each layout it wraps, each page embedded and still running, and each
  layout that embedded page wraps — and an embed whose **key and query** are already on it is refused
  with the chain in the message. A page
  may embed the same card twenty times, because each instance finishes and leaves the stack; only an
  ancestor closes a cycle. The pair matters: `/foo` embedding `/foo` is an infinite loop and refused,
  while `/foo?q=1` embedding `/foo?q=2` is a recursive component — a comment tree or a directory —
  and allowed, with the caps below as its bound. It is the machinery the layout chain already has,
  shared instead of duplicated, and a layout that embeds the content it wraps is caught too.
- **The recursive case is counted, so it can be watched.** An embed that repeats an ancestor's key
  with a different query counts `page.composition.recursive` and logs the chain at Debug. It is legal and
  occasionally brilliant, but a page that does it is worth knowing about, and the ordinary case pays
  nothing for the instrument.
- **`Pages:Render:MaxEmbedDepth`** bounds nesting, separate from `MaxLayoutDepth` because a widget
  is not chrome and the two costs are chosen independently. It is also the real bound on a
  data-driven recursion, where every level passes a different request and the cycle check never fires.
- **`Pages:Render:MaxEmbeds`** bounds the fan-out in one request. Depth alone lets a listing embed a
  thousand siblings; this is the cap that stops one request turning into a thousand renders under one
  slot and one deadline.

An embed is a real render: it takes no slot of its own (the request's session already holds one) and
shares the deadline, the fetch allowance, the template allowance and the body allowance. It is also a
lazy compile for a key that has never been rendered, so a listing that embeds thirty keys a visitor
has never seen can compile thirty pages on one request — visible in `page.render.*` under each key,
and the reason `MaxEmbeds` is low.

### Failure

Following the catalog's rule, a refusal is an exception that says which of these it is:

- a malformed key is an `ArgumentException`, because that is a page bug;
- a key that is missing, deleted or reserved, a library or a worker, or an embedded page that
  redirects, is a `UserFacingException` naming the page and what it did, because the author can fix
  it — neither a library nor a worker is a page;
- a cycle, a cap or an ended render is an `InvalidOperationException` naming the chain or the setting;
- one body allowance, so an embedded page that writes too much stops the request, not just itself.

A compile failure is the author's error too, and names the key; the diagnostics stay where they
already are, in the editor and `IPageWrites`' answer.

**A page may catch any of these.** That is ordinary page code — `try { await EmbedAsync(...) }` around
a widget whose absence it can live with — and it is safe for the output because the embedded page
writes into its own buffer, so a failure never leaves half of it in the caller's body. Two things do
not roll back: a side effect the embedded page already committed (a storage write, a session write, a
started job), and the body allowance it spent on the way. Catching is the page's decision; the
capability does not hand out a result value that makes ignoring a failure the default.

### Capture is the same render, returned

`CaptureAsync` shares the embed's whole codepath — resolution, recursion, the derived request, the
capabilities, the withheld response, the one write, the failures, and the page's own wrappers — and
differs in one thing:
`EmbedAsync` writes the body into the caller (and unions the captured page's script permission) and returns
the `PageFrame`; `CaptureAsync` returns the `PageCapture` and touches nothing. **The body is
the only difference**, and the types say so: the frame has no body, the capture has one. The
wrapper is the same shape from the other end: the content's `PageCapture` arrives as
`IPageLayout.Content` and the wrapper folds what it wants.

The result is deliberately **a value, not the live `IPageLayout`**. `IPageLayout` is the capability a
layer runs with: its `Content` is the layer *inside* it, and its own `Title`, `Head` and `Stylesheets`
are what the layer contributes outward. A frame or a capture is neither — it is the finished
material of one layer, which is why it can be carried, transformed or discarded. The response itself
stays withheld, so a redirect still refuses.

The caller owns what happens next. The prod site's `/_md` wrapper proves the shape: a wrapper already
receives its content as `IPageLayout.Content`, and a page that wants the same thing without being wrapped
— or wants to transform it first — is what capture is for:

```html
<% var captured = await ctx.Capabilities.GetRequired<IPageComposition>().CaptureAsync("/draft");
   foreach (string sheet in captured.Stylesheets) { layout.AddStylesheet(sheet); }
   if (captured.AllowsScripts) { ctx.Response.AllowScripts(); }
   ctx.Response.WriteRaw(ctx.Capabilities.GetRequired<IMarkdownRenderer>().ToHtml(captured.Body)); %>
```

The capture is bounded by the caller's remaining body allowance, but the returned body is charged to
the shared allowance only when (and if) the caller writes it, so a page that transforms it is charged
for what it wrote. A capture counts as an embed for the depth, the fan-out cap and the recursive
counter.

## The manual and the docs, in the same change

- `/help` gains the capability beside `IPageLayout`, with snippets for `EmbedAsync`, `CaptureAsync`
  and `Wrap`, the "the request is the input" rule, the bounds, the refusals, and the two warnings: an
  embedded page runs the request it is given, and its `ctx.Path` is its own key so its links must be
  site-absolute.
  It also says plainly that a page that does not call `Wrap` is served as a bare fragment, that an
  embed runs the wrappers the page declares and returns the outermost frame, so a page with `Wrap()`
  embeds as the document it renders, that a form inside an
  embed must post to the embedded page's own key rather than to the document's URL,
  that storage is bound to the key so two instances of one widget share state unless the page keys it
  on its query, that an **embed** returns the outermost frame without the body, that a **capture**
  returns the frame with the body, that the caller decides what to fold, and that **the wrapper owns
  the title check** now — the host no longer refuses a missing one.
- Every seeded page with code gains its `Wrap()` line, and the editor's new-page template carries it,
  because that is where a page that would otherwise forget is most often born. The seeded `/_layout`
  folds its content's capture — a default title from `Content.Title`, the content's sheets and head
  beside its own — which is the wrapper's job now; the `Site.Document` library gets the merge helper
  so the seeded layout stays short.
- The assistant's prompt lists the capability, its four calls, the stacking rule and that an embed
  renders the page's whole chain, so it does not write a guard around every wrap, and the nullable
  title.
- `docs/pages.md` adds the capability to the list, the `PageRequest` extension methods
  (`WithQuery`, `WithAnotherQuery`, `WithoutQuery`), the frame/capture split, and says `IPageLayout.Layout`
  and `Body` are gone, `Content` is the inner capture and `Title` is nullable;
  `docs/shell.md` says the chain is policy over the layer render, entered by the page, and that the
  stored layout is the site's menu. `docs/metrics.md` names `page.composition.recursive`, so an
  operator can see the one page that re-enters itself.
- The glossary gains **embed**, **capture** and **wrap**, distinct from asset, library and layout.
- `PublicAPI.Unshipped.txt` in `Sdk` gains the capability, `PageFrame`, `PageCapture` and
  `PageRequestExtensions`, and records `IPageLayout.Layout` and `Body`'s removal and `Title`'s
  nullable type.

## Decisions a plan would propose

Names only, not yet taken: an embedded page is a render of the whole page, wraps included, whose
response is withheld (pages-an-embed-is-a-render); an inner page runs the request it is given, with
null meaning the caller's, and the request's copy-and-change helpers are extension methods on
`PageRequest` (pages-a-composed-page-gets-a-request); a capture is the body beside the frame, while
an embed returns the frame alone (pages-a-capture-returns-the-body); a page declares the page or
pages that wrap it, innermost first (pages-a-page-chooses-its-layout); the chain and the embed are one
layer render with a role (pages-one-render-three-roles); a page inside a page is a
capture the outer page folds (pages-a-page-inside-a-page-is-a-capture); and the wrapper owns the title
check (pages-the-wrapper-owns-the-title).

## Not in scope

- **A slot API beyond `Body`** (named regions an embedded page fills, the way `Body` is the wrapper's
  slot). The first shape is whole-body composition; a wrapper can embed its own partials around
  `Body`, and a second slot is a different contract that waits for a page that cannot be written
  without it.
- **A general `Items` bag on `IContext`.** The request is the page's input in both directions; an
  untyped bag is a second, hidden channel with string keys and no compiler help, and a page that
  needs structured state has `IPageStorage` and `IPageSession`. If a real page needs it, it is an
  additive idea with its own decision.
- **A per-call embed boundary, a partial variant of a page, or an embed flag.** An embed is the
  page's whole render, wrappers and all; a view that should insert a fragment is a page that declares
  no wrapper. A page that must know it is embedded already has the signal `ctx.Path` gives.
- **A failure value or a built-in fallback.** `EmbedAsync` throws, and a page that wants to compose
  optionally catches it — that is the page's call, and the embedded page's output never reaches the
  caller's body, so the catch is clean. A result value that makes ignoring a failure the default, or
  an error boundary that renders a placeholder for a broken embed, is a different contract and hides
  the author's mistake.
- **A reserved prefix for partials.** `/_parts/…` is a naming convention in the manual, not a host
  rule; a partial is still a servable page, which is what makes it easy to look at on its own.
- **Moving `/status` or `/assistant` off the shell.** They are the named exceptions; see above.
- **Rendering a page in a browser** — that is [IDEA-044](IDEA-044.md), and a different question.
- **Caching or memoising an embed.** An embed is part of the page it appears on, and the page is
  `no-store`; the renderer's image cache already covers the expensive half.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-049 — A page cannot render another page

| | |
| --- | --- |
| Size | L — one `Sdk` capability with two calls and their value, one layer renderer with three roles, the layout chain rebuilt on it and triggered by the page, `IPageLayout.Layout` and the automatic wrap deleted, two `Pages:Render` values, the shell-document policy and the trim it allows, every seeded page, the manual, the assistant's prompt, the docs and the tests that pin the recursion rules. It wants a plan |
| Area | Sdk, Web (pages, shell, seeds, assistant), docs |
| Location | `src/uk.osric.asp.Sdk/IPageComposition.cs`, `src/uk.osric.asp.Sdk/PageEmbedOptions.cs`, `src/uk.osric.asp.Sdk/IPageLayout.cs`, `src/uk.osric.asp.Web/Pages/PageCompositionScope.cs`, `src/uk.osric.asp.Web/Pages/PageChain.cs`, `src/uk.osric.asp.Web/Pages/PageEndpoint.cs`, `src/uk.osric.asp.Web/Pages/PageRenderer.cs`, `src/uk.osric.asp.Web/Pages/SimpleContext.cs`, `src/uk.osric.asp.Web/Pages/SimpleResponse.cs`, `src/uk.osric.asp.Web/Pages/PageLayoutState.cs`, `src/uk.osric.asp.Web/Pages/RenderOptions.cs`, `src/uk.osric.asp.Web/Pages/PageMetrics.cs`, `src/uk.osric.asp.Web/appsettings.json`, `src/uk.osric.asp.Web/Html/ShellDocument.cs`, `src/uk.osric.asp.Web/Html/SiteNavigation.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs`, `docs/pages.md`, `docs/shell.md` |

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
> **includes** another page and one that **wraps** this page in another. The wrap is a declaration —
> `Wrap`, not `WrapAsync` — because the host performs it after the handler returns and every exit path
> is then wrapped. That is the general form of the layout, and it is what lets `IPageLayout.Layout`
> and the host's automatic wrap go without losing the slot.

## Idea

A stored page can read another page's **source** (`IPageCatalog`) but not its **output**. So every
view that wants "this page, here" writes the markup again: an include, a sidebar, a footer, the same
card drawn from a table row. The layout chain already knows how to run one stored page inside
another; it just only ever does it once, from outside, with a document-shaped contract.

The general primitive is an **embed**: a page asks the host to render another stored page, with the
options the caller supplies, and the embedded page's body is written where the call is. Everything
below the request and the output — its key, its storage, its session, its identity, its capabilities,
the request's deadline and allowances — is the embedded page's own, exactly as it would be if a
visitor had asked for it.

The layout chain is then the same render seen from the other side: the page that asks to be wrapped
hands its finished body to a wrapper, which writes around it, and may itself ask for another. This
job makes that literal. There is **one layer renderer** and **three roles**, and a page says which
call it wants — include another page here, or hand itself to one — so the chain's rules stay, as
policy over a render it no longer owns.

## What the caller learns, and what it does not

- An embed is **not a guard**: the embedded page runs, and its own `ctx.User` guard decides what it
  writes. A caller cannot use an embed to see a page an anonymous visitor could not; and cannot use
  it to hide one either.
- An embed is **not a sandbox**: the embedded page has the capabilities a content page has, spends
  the same request budget, and can do the same side effects. Embedding is composition, not isolation.
- An embed is **not a select**: `IPageCatalog.ReadAsync` answers "what does this page say"; an embed
  answers "what does it produce, for these options". A no-code page's body happens to be its source,
  less its comments, exactly as when it is served.
- **No embed flag is added to the request.** The request is the caller's, unchanged in shape. A page
  that must know it is inside another is told by the options — or notices the one difference that is
  structural: `ctx.Path` names the embedded page while `ctx.Request.Path` still names the page the
  visitor asked for, which is the same difference a layout layer already shows.
- **A page chooses its wrapper; nothing wraps it by default.** `Wrap()` asks for the site's layout,
  `Wrap(key)` for a named one, and a page that asks for neither is served exactly as it wrote itself.
  The host no longer applies `Pages:Layout` on its own.

## Sketch

### The contract

`IPageComposition` is a capability, reached like every other, offered to every layer a render has —
the content, any layout the chain adds, and any page already embedded — because a layout embedding a
header and footer partial is the use that argues for this most:

```csharp
public interface IPageComposition {
    Task EmbedAsync(string key, PageEmbedOptions? options = null);
    void Wrap(string key);
    void Wrap();
}

public sealed record PageEmbedOptions {
    public PageEmbedOptions() { }

    /// <summary>Starts from the caller's own query, so a copy can be changed.</summary>
    public PageEmbedOptions(PageRequest request);

    public IReadOnlyList<KeyValuePair<string, string>> Query { get; init; } = [];

    public PageEmbedOptions With(string name, string value);
    public PageEmbedOptions WithAnother(string name, string value);
    public PageEmbedOptions Without(string name);
}
```

`EmbedAsync` includes another page here. `Wrap` hands this page's finished output to another page and
names it as the document; `Wrap()` uses the site's layout, `Pages:Layout`, so a page does not name a
key the owner can move. A page does:

```html
<% await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/cards/post",
       new PageEmbedOptions(ctx.Request).With("id", "7")); %>
```

(`Wrap` is deliberately not `WrapAsync` — see [the timing below](#wrap-is-a-page-call).)

**Options are the embedded request's query, and nothing else.** Not a query *string* — the values a
page would see if the request had carried them, so nothing is encoded or decoded on the way and
`ctx.Request.Query` and `ctx.Request.QueryValues` behave exactly as they always do. The two call
shapes are deliberate: **no options** forwards the caller's query unchanged, and
`new PageEmbedOptions()` sets an **explicitly empty** one. The constructor over `ctx.Request` is the
copy-and-change path, so forwarding most of a query and overriding one name does not mean rebuilding
the list by hand: `With(name, value)` replaces every value for that name, `WithAnother(name, value)`
adds one beside the existing values (the repeated name `QueryValues` already models), and
`Without(name)` drops one. The alternative — one options type per page, or an object handed across
the boundary — is the thing this is deliberately not.

### What the embedded page sees

**The caller's request, with one value changed.** The embedded page runs against the same request
snapshot the caller runs under — same method, same form, same headers, same cookies — and the only
difference is the query: the options when they are given, the caller's own when they are not. That is
what makes an embed a render of the request rather than a second, invented one: a `POST` that renders
a summary gives the summary the `POST` and the form it submitted.

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

Its `IPageLayout` slots fold **outward**: `AddHead` and `AddStylesheet` reach the calling layer's
document slots (after the embedded render, before the layout chain reads them), and `Title` does not —
an embed has no document to name. That is how an embedded widget contributes its stylesheet without
being wrapped in the site chrome.

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

`Wrap` is held to the same role rules as the layout it replaces: a page calls it at most once, and on
an **embedded** layer it is ignored rather than refused — a page must not have to know it is
embedded, and an embed is a fragment, so the wrapper simply does not run. The ignore is logged at
Debug, because it is the expected shape of an embeddable page, not a mistake. The wrapper is a layout
layer — response withheld, no flush, no writes or jobs — because it is markup around the page the
visitor asked for. A wrapper must have code: a no-code page has no way to read `IPageLayout.Body`, so
its stored source would replace the page instead of wrapping it.

### The chain is built on this render

The layout chain is not a second way to run a page: it is a **policy over the same layer render**, and
the page now says when to enter it.

- One layer renderer takes a key, a request, a body slot and a role, and answers the body it wrote.
  The **content** role owns the response; the **layout** role withholds it and is handed the body it
  wraps; the **embedded** role withholds it and takes no slot of its own.
- A request is then: render the requested key as content; if it called `Wrap`, render the key it
  named as a layout with the content's body; if that layout called `Wrap`, do it again; write the
  outermost body. `PageChain` is that loop, over the same render an embed uses instead of a duplicate
  of it.
- The chain's rules do not disappear. The head and sheets merge in one order, the outermost layer still
  owns the document, and a wrapper that cannot be served is an error naming the key. The **title is the
  wrapper's concern now**: `IPageLayout.Title` becomes nullable, the host stops refusing the chain for
  a missing one, and a wrapper decides whether to supply a default, refuse, or compose. What changes is
  only **who asks**: the page, not `Pages:Layout`.
- `IPageLayout.Layout` is deleted. `IPageLayout` keeps `Body`, `Title`, `Head` and `Stylesheets` — the
  slots a wrapper reads and the page contributes to. `Pages:Layout` stays as the key that `Wrap()`
  means, so a page does not carry a key the owner can move.
- `Wrap()` keeps the default's fallback: a default layout that cannot be served serves the page
  unwrapped with a warning, because the page did not name it. `Wrap(key)` is an error, as a named
  layout is today.
- The embed writes through the calling layer's **`IPageResponse`**, not its `IContext`: the response
  split already made the output a thing a helper can take on its own, and this is the first
  capability to use it. The only other thing the embed asks of the caller's layer is its
  `PageLayoutState`, to fold a head or a sheet outward.
- The mechanics are small: the endpoint opens the render session before it builds the layers, or the
  embed capability takes a late-bound session. Either way one slot and one deadline still covers every
  layer of the request, as today.

That is the part worth doing now rather than later: an embed added beside the chain would be a second
way to render a page inside a page, which is the duplication this idea exists to remove.

The shell policy it lets us state: **the shell's own documents are bootstrap and recovery surfaces,
not the site's views.** They may be plain HTML without the site menu. The menu belongs to the stored
`/_layout`, which is then the only place it is written; the shell's copy exists only because the
error, health and login routes must answer when the page pipeline itself is broken. This job trims
the menu from those emergency documents and leaves **one way home**: a link to `/`, plus a link to
`/admin` when the document is at or under `/admin`. The admin link is the one piece of the old menu
that still earns its place, because the pages under that prefix are the ones an owner moves between.

One piece of coverage goes with the duplication: the integration test that compares the shell's
document with the page's stops comparing two menus, because there is one. What replaces it is the
assertion that a shell document carries the home link, and the admin link exactly when its route is
under `/admin`. That is a smaller assertion and belongs in the plan, not in a footnote.

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
  → /_layout renders into buffer B with IPageLayout.Body = A
  → /_layout may call Wrap("/_outer") → buffer C with Body = B
  → write the outermost buffer
```

Every layer is a real render under the request's one slot and deadline, exactly as today; the
difference is that each layer names its parent instead of the host.

**When the handover happens.** It is a **declaration**: `Wrap(key)` records the key, and the host
runs the chain after `HandleAsync` returns, where it runs today. The page writes it once, near the
top, and every exit path is wrapped — including a guard that writes a short body and returns. It is
not `WrapAsync`, because it does not await the wrapper; the waiting is the host's.

The literal alternative was considered and rejected: `WrapAsync(key)` renders the wrapper immediately,
replaces the page's body with the wrapper's, and ends the page, so any later use of `ctx` throws. It
reads as the sentence it is and makes the chain a call stack, but every exit path has to reach the
call — an early `return` that skips it silently serves a bare fragment — and the page must set its
status and headers before it. A silent trap in the common guard shape is too high a price for the
wording.

**What the wrapper sees.** The same request the content saw — same method, form, headers and cookies,
with no options of its own — `ctx.Path` is the wrapper's key, `ctx.Request.Path` is still the
visitor's page, and `IPageLayout.Body` is the content's finished output. It reads `Head` and
`Stylesheets` as the layout does today, and it reads `Title` as `string?`: supplying a default,
composing it, or refusing a page that set none is the wrapper's decision, because that is the one
place that knows what the document needs. Its own `SetStatus`, `SetHeader`, `SetContentType` and
`Redirect` are ignored and logged; its `AllowScripts` is unioned into the content's response, because
the wrapper's script is part of the document.

**What the host no longer does.** No automatic wrap, no `Pages:Layout` applied to a page that did not
ask, no `IPageLayout.Layout`. The config value stays as the key `Wrap()` means. The shell's own
documents simply do not call it.

**A page can be both a document and a partial.** `Wrap()` on an embedded layer is ignored, so a page
calls it unconditionally: when a visitor asks for the page it is the document, and when another page
embeds it, it is the fragment. That is the property that makes composition worth having, and it is why
the ignore is not an error. `Wrap` after the first flush is refused, because a committed body cannot
be handed over; the check that already stops a page that would be wrapped from streaming covers the
other order.

One consequence worth naming: a page that forgets its `Wrap()` is served as a bare fragment — valid
HTML the browser renders, without the site's chrome, title or sheets. That is the cost of the page
owning the decision, and the editor's new-page template plus `/help` are where it is made unlikely.

### Recursion and cost

Three bounds, because a cycle is not the only way one page becomes many:

- **An ancestor stack, not a set of everything rendered.** `PageChain` owns the layers currently on
  the stack — the requested page, each layout it wraps, each page embedded and still running — and an
  embed whose **key and options** are already on it is refused with the chain in the message. A page
  may embed the same card twenty times, because each instance finishes and leaves the stack; only an
  ancestor closes a cycle. The pair matters: `/foo` embedding `/foo` is an infinite loop and refused,
  while `/foo?q=1` embedding `/foo?q=2` is a recursive component — a comment tree or a directory —
  and allowed, with the caps below as its bound. It is the machinery the layout chain already has,
  shared instead of duplicated, and a layout that embeds the content it wraps is caught too.
- **The recursive case is counted, so it can be watched.** An embed that repeats an ancestor's key
  with different options counts `page.embed.recursive` and logs the chain at Debug. It is legal and
  occasionally brilliant, but a page that does it is worth knowing about, and the ordinary case pays
  nothing for the instrument.
- **`Pages:Render:MaxEmbedDepth`** bounds nesting, separate from `MaxLayoutDepth` because a widget
  is not chrome and the two costs are chosen independently. It is also the real bound on a
  data-driven recursion, where every level passes different options and the cycle check never fires.
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

### Capture, deliberately later

"Probably just a call to embed" is the first shape, and capture is skipped for now. A capture variant
— returning the markup so the caller can transform it — would charge the body allowance twice: once
where the embedded page writes, and again where the caller writes the string back through `WriteRaw`.
If a real page needs the string, the additive answer is a method that charges once, not a change to
the primitive.

## The manual and the docs, in the same change

- `/help` gains the capability beside `IPageLayout`, with snippets for `EmbedAsync` and `Wrap`, the
  "options are the query" rule, the bounds, the refusals, and the two warnings: an embedded page runs
  the caller's method and form, and its `ctx.Path` is its own key so its links must be site-absolute.
  It also says plainly that a page that does not call `Wrap` is served as a bare fragment, that a
  form inside an embed must post to the embedded page's own key rather than to the document's URL,
  that storage is bound to the key so two instances of one widget share state unless the page keys it
  on its options, and that **the wrapper owns the title check** now — the host no longer refuses a
  missing one.
- Every seeded page with code gains its `Wrap()` line, and the editor's new-page template carries it,
  because that is where a page that would otherwise forget is most often born. The seeded `/_layout`
  supplies a default title when the page set none, which is the wrapper's job now.
- The assistant's prompt lists the capability, its two calls, the "`Wrap` is ignored in an embed"
  rule, so it does not write a guard around every wrap, and the nullable title.
- `docs/pages.md` adds the capability to the list, the two option shapes and the constructor with
  `With`/`WithAnother`/`Without`, and says `IPageLayout.Layout` is gone and `Title` is nullable;
  `docs/shell.md` says the chain is policy over the layer render, entered by the page, that the shell's
  documents are bootstrap surfaces with one way home — `/`, plus `/admin` at or under that prefix —
  and that the stored layout is the site's menu. `docs/metrics.md` names `page.embed.recursive`, so an
  operator can see the one page that re-enters itself.
- The glossary gains **embed** and **wrap**, distinct from asset, library and layout.
- `PublicAPI.Unshipped.txt` in `Sdk` gains the capability and the options value, and records
  `IPageLayout.Layout`'s removal and `Title`'s nullable type.

## Decisions a plan would propose

Names only, not yet taken: an embedded page is a content render whose response is withheld
(pages-an-embed-is-a-content-render); an embed sees the caller's request with the options as its
query (pages-an-embeds-options-are-its-query); a page chooses the layout that wraps it
(pages-a-page-chooses-its-layout); the chain and the embed are one layer render with a role
(pages-one-render-three-roles); the wrapper owns the title check (pages-the-wrapper-owns-the-title);
the shell's documents are bootstrap and recovery surfaces, and the stored layout is the site's menu
(shell-documents-are-bootstrap); a shell document carries one way home (shell-a-shell-document-carries-one-way-home).

## Not in scope

- **A slot API beyond `Body`** (named regions an embedded page fills, the way `Body` is the wrapper's
  slot). The first shape is whole-body composition; a wrapper can embed its own partials around
  `Body`, and a second slot is a different contract that waits for a page that cannot be written
  without it.
- **A data bag in the options.** Options are the query; a typed payload would be a new value type on
  the page contract, and a page that needs structure can use repeated query values or its own
  storage. If a real page needs it, it is an additive idea.
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

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Pages

A page is compiled when it is saved, loaded into a collectible context when it is requested, and run
under a budget the host owns. These are the rules that keep that safe: what a page may call, what it
may not reach, and what happens when it does not finish. [Architecture](architecture.md) explains
where this code lives and why; [the shell](shell.md) is what wraps a page's answer.

## Page contract

A page is a runtime, not a renderer: it reads the request and shapes the response, but every value it
touches is a plain record in `Sdk`. `Request` carries the method, path, query, form, headers and the
visitor's own cookies; the shell's three cookies (`a`, `f`, `s`) are filtered out, because they are
the shell's identity and one visitor's state rather than anything a page owns. `IContext` is the
surface every page has — `Request`, `Path`, `EditorPath`, `User`, `Aborted`,
`Log`/`LogWarning`/`LogError`, `Write`/`WriteRaw`, `SetStatus`, `SetContentType`, `SetHeader`,
`AllowScripts`, `AssetUrl`, `Redirect`, `RedirectToLogin`, `AntiforgeryField` and `Capabilities`.
`EditorPath` is the editor page's key, which `Pages:Editor:Key` decides, so a page that links to the
editor uses it rather than naming the key.

The request is a value rather than a live ASP.NET object. `Query`, `Form` and `Headers` hold one
value per name — a repeated name keeps its first value — and `QueryValues` and `FormValues` hold
every value in the order the request sent them, so a multi-select or `?tag=a&tag=b` loses nothing
(`pages-the-api-policy`). `Path` is always the canonical key.

The handler is asynchronous: `IHandler.HandleAsync` returns a `Task`, the generator always emits an
`async` method, and `await` is therefore an ordinary expression in a code block and inside a write
expression. Any member that does I/O — on `IContext` or on a capability — returns a `Task` and takes
no cancellation token: the context owns `Aborted` and the render's deadline, and an implementation
links them per call (`pages-page-io-is-async`).

`IContext.Capabilities` is how a page asks for a capability this render may or may not offer.
`TryGet<T>` reports whether one is there, `Get<T>` returns it or `null`, and `GetRequired<T>`
returns it or throws naming the interface, so the page decides whether absence is fatal. The host
decides what is registered — a page cannot add or replace a capability — and a capability is either
registered or absent, never `null` (`pages-capabilities-are-one-seam`). The type that declares them is `IPageCapabilities`;
the old `Features` spelling was removed once its release was over, so a page that still says
`ctx.Features` no longer compiles (`pages-capabilities-are-one-seam`).

Eleven capabilities are registered today — seven that do I/O or work and are bounded by the render,
one that is the page's slots in its document, two that are pure renderers, and one that is a fact about
the build:

- `IBuildInfo` says which build is answering: `Commit` is the full git commit hash, or `null` when
  the build did not come from a checkout. It is a capability because the page API denies both the
  assembly and the environment, and the seeded `/about` page links the commit.
- `IPageStorage` is the page's own key/value store. It is bound to the page's key, so a page cannot
  name another page's state; the handle is the isolation. Values are JSON round-tripped by the
  host, keys are ordinal, and writes are last-write-wins. A `HEAD` render may read but never write
  (`pages-page-storage`).
- `IPageSession` is one visitor's key/value store, separate from `IPageStorage`: a page session
  value belongs to the browser that wrote it, is bounded per visitor, and goes when the visitor
  signs out. `TryGetValueAsync<T>(key)` reads the caller's own key and
  `TryGetValueAsync<T>(pagePath, key)` reads another page's by path, which is how a page shows what
  another page left for it. `SetAsync`, `DeleteAsync` and `KeysAsync` are bound to the caller's
  path, so a page cannot write, delete or list another page's keys; `AllKeysAsync` returns
  `PageSessionKey` (`Page` and `Key`) pairs because a bare key cannot be read. Values are JSON like
  page storage, keys follow the same rule, and the caps are per session (8 keys and 16 KiB
  anonymously, 32 keys and 64 KiB signed in). A session is minted on the first write, lives in an
  `HttpOnly` cookie the shell owns, slides for 30 minutes (8 hours signed in) and is cleared on
  sign-out. A `HEAD` render and a layout may read and not write. The identity, the ownership rule
  and the caps are `pages-the-session`; see [storage](storage.md#page-sessions).
- `IHttpFetch` makes one outbound HTTP request from the host, not the browser, to any `http` or
  `https` host the URL names: there is no host allow-list (`pages-outbound-fetch`). Every request carries
  `moosemorals.com (page)` as its `User-Agent`, set by the host after the page's own headers, so a
  page cannot claim to be another client. `GetAsync` and `PostAsync` sit over
  `SendAsync(HttpFetchRequest)`; `StreamAsync` opens the same request rules and returns an
  `HttpFetchStream` that reads decoded text lines with `ReadLineAsync()`, exposes response metadata
  and failure values, and can be closed explicitly. Open streams close when the render ends or is
  abandoned. The render's shared request count, the deadline and abandonment bound the request
  (`pages-outbound-fetch`, `pages-capabilities-are-one-seam`). A request may name an operator-configured credential with
  `Credential`: the host supplies the header, only to that credential's host and only from the page
  the credential is bound to, and the page never reads the value. An unknown name is an
  `InvalidRequest` (`pages-fetch-credentials`). Response headers use `Pages:Fetch:TimeoutSeconds`; a stream's body
  also has independent `StreamSilenceSeconds`, `StreamMaxSeconds` and `MaxStreamBytes` limits.
- `IPageJobs` starts a **worker** as a **job**, talks to it, reads what it published and stops it.
  A job outlives the request that started it, so it has no response and no visitor waiting on it: it
  is work a page can start, watch and cancel. `StartAsync(workerKey, firstMessage)` answers a
  `PageJobStart` whose `Refusal` says why a key that is not a worker, a reserved key, an anonymous
  render or a full queue was refused; `SendAsync`, `GetAsync`, `ListAsync`, `ReadEventsAsync` and
  `CancelAsync` are the rest. Every call is filtered to the signed-in visitor the render was bound to,
  so another account's job is an unknown one rather than a refusal that says it exists (`pages-a-page-may-save-itself`). A
  `HEAD` is offered the reads and not the start, send or cancel, and a layout is offered nothing.
  See [jobs](#jobs).
- `IPageCatalog` reads the page store: `ListAsync(prefix)` lists the heads under a subtree,
  `ReadAsync(key)` reads a live page's template, `RevisionsAsync(key)` lists a page's history and
  `ReadRevisionAsync(key, hash)` reads one retained revision. Each `PageEntry` marks the head
  `IsDeleted`, `IsReserved`, `IsHtml` and `IsAsset`, so a view filters rather than re-deriving the
  host's rules, and carries the head's image state in `State` — the host's own words, judged against
  the running build — so a page's listing says what the editor's says without reading source
  (`pages-a-page-has-a-state`). The class the shell's own list marks it with stays in the shell: how a view presents a
  state is the view's business. It reads and never writes. The read is
  as public as the page that calls it — there is no gate, and a page that wants privacy writes the
  ordinary `ctx.User` guard — and the render's deadline, abandonment and the shared chain budgets
  bound it (`pages-the-store-is-read-through-one-seam`, `pages-reading-the-store-is-public`, `pages-a-listing-carries-facts`, `product-manual-is-a-page`, `pages-delete-install-and-restore`, `pages-page-first`). See [source storage](storage.md#source-storage).
- `IPageWriter` saves the calling page's own source: `SaveAsync(template)` answers with a
  `PageSaveResult` — the new revision's hash and the compiler's diagnostics as `PageDiagnostic`
  values — and the handle is bound to `ctx.Path`, so a page writes itself and nothing else. It
  compiles before it stores, so an error is stored and reported and a warning still serves. **The
  write requires a signed-in visitor**: unlike the read, it refuses an anonymous render before it
  stores anything, and a `HEAD`, an ended render or a second save in one render is a page bug. A
  save belongs to the next request, and a save that has started finishes even if the render times
  out. A layout is offered the read and not the write (`pages-a-page-may-save-itself`, `pages-layouts-are-markup`, `pages-scratch-saves-itself`). See
  [source storage](storage.md#source-storage).
- `IPageEditor` is the editor's authority, and it is offered to one render only: the page stored at
  `Pages:Editor:Key` (`/pages` as shipped). No other key gets it and a layout never does, so the
  registration is the authority rather than a check inside the handle (`pages-the-editor-capability`). It answers with
  values the compiler produced — `PageEditResult` carries the outcome, the new hash and
  `PageDiagnostic`s — and it refuses an anonymous render, a `HEAD` write and a second action in one
  render. `Resolve` says whether a key is usable, `SaveAsync(key, template, expectedHash)` is the
  conditional save whose `null` expectation creates a page, `DeleteAsync` tombstones,
  `RestoreRevisionAsync` and `InstallSeedAsync` answer with the revision they stored, and
  `SeedSourceAsync` reads the bundled source so the install confirmation can show a diff. Like the
  page's own write, a save belongs to the next request and a started save finishes (`pages-the-editor-capability`, `pages-delete-install-and-restore`).
- `IPageLayout` is the page's slots in the document it is served in — `Layout`, `Title`, `Head`,
  `Stylesheets`, `AddHead`, `AddStylesheet` and `Body`. It is a capability rather than more
  `IContext` because it is a set of slots a render offers, and because a page that never wants a
  layout never meets one (`pages-a-layout-is-a-capability`). See [layouts](shell.md#layouts).
- `IMarkdownRenderer` renders Markdown to HTML with `ToHtml`. It is pure and needs no `await`; its
  result is HTML, so the page writes it with `<%== %>`. The page profile is CommonMark plus pipe
  tables and auto-links, and raw HTML passes through: this is the author's renderer, not a
  sanitiser, and the same warning is in `IMarkdownRenderer` and `/help` (`pages-markdown`). A table's header
  cells carry `scope="col"` and no inline style.
- `IDiffRenderer` renders the line changes between two documents with `ToHtml(before, after)`. It
  is pure and needs no `await`, and its result is written with `<%== %>`. Every source line is
  escaped, so unlike Markdown it is safe for a visitor's text, a fetched body or page state: the
  input is data, not markup. The result is a `table.diff` with line numbers, change markers and a
  sideways scroll container; an unchanged pair says so, and a comparison too different to align
  says why. A page compares two revisions by reading both with `IPageCatalog.ReadRevisionAsync`
  (`pages-diff`).

A page's log entries are filed under the single `uk.osric.asp.Pages` category with the page's key on
a `Path` scope, and `SimpleContext` is where that enters the shell, so `Core` never names a logging
type. [Logging](logging.md#scopes) has the category rule and why it cannot name the path.

Two rules keep the response safe:

- The body is buffered by default. Status, content type and headers are applied after `HandleAsync`
  completes, so a page that throws sends the error page rather than half a document. A page can opt
  into sending its body while it runs with `ctx.FlushAsync()`; the first flush is the commit point
  (see [Streaming](#streaming)).
- Headers are bounded by an allow-list in `SimpleContext`: `Content-Language` and any `X-` name.
  `Set-Cookie`, `Location`, `Content-Type`, the framing headers and the caching headers
  (`Cache-Control`, `ETag`, `Expires`, `Vary`) are refused, so a page cannot clobber the editor's
  authentication, antiforgery or session cookie, or claim a cache policy the host overrides.
  Redirects go through `Redirect`; content type through `SetContentType`, and the script permission
  through `AllowScripts` — the `Content-Security-Policy` header itself stays shell-owned. The
  extension is only a default: a key's last segment selects the initial content type through
  `Pages:ContentTypes` (`pages-content-types`), which ships `.html`, `.js`, `.mjs`, `.css`, `.json`, `.svg` and
  `.txt` (`pages-content-types`), and `SetContentType` always wins. Each row also carries a `ScriptPolicy`
  (`none` or `self`); it follows the extension, not the changed response type, and the shell writes
  it before the page runs. A page with code may widen `none` with `AllowScripts`, but never narrows
  `self`. The shipped `.html` row is `none`.

An unversioned page URL is `Cache-Control: no-store` with no validator, whoever asked: a page with
code can keep per-visitor state, so its body has no business in a shared cache and no business
inviting a conditional request. `PageEndpoint` writes the header after the page's own and removes `ETag`, so a
page cannot weaken it, and `If-None-Match` is ignored for an unversioned request. A page redirect is
`no-store` too. The host still sets `Content-Length` from the encoded body for a response that may
carry one, and a buffered `HEAD` retains the representation length; a streamed one has none.

### Streaming

A page can send its body while it runs by calling `await ctx.FlushAsync()`. The first flush is the
commit point and the whole of the opt-in:

- The status, content type, headers and script policy are applied, `no-store` and any session or
  antiforgery cookie already minted for the response are written, and the response starts. From then
  on `SetStatus`, `SetContentType`, `SetHeader`, `AllowScripts`, `Redirect` and `RedirectToLogin`
  throw, and reading `AntiforgeryField` throws.
- Writes after a flush are buffered until the next flush; whatever is still buffered when the
  handler returns is sent then. The response has no `Content-Length`, because its length is not known
  when the headers go.
- A flushing page is served alone. A layout needs the whole body before it can run, so a flush while
  the page would be wrapped throws and names the remedy: clear `IPageLayout.Layout` and write the
  page's own document before the first flush. A non-HTML page is never wrapped, so the layout value
  does not matter to it. A layout itself cannot flush.
- `Write` still HTML-encodes and `WriteRaw` still writes the caller's markup, before and after a
  flush. Streaming is not a way past the encoder.
- A failure after the commit truncates the response. An exception is logged at Error and the
  connection is aborted; the render deadline is logged at Warning and aborts instead of attempting a
  `504`, because the headers are already sent. Before the first flush the ordinary guarantees apply:
  an exception is the error page and the deadline is a `504`.
- `HEAD` commits the response but writes no body, and a streamed `HEAD` carries no length.
- `AntiforgeryField` must be read before the first flush; after it, the field's cookie can no longer
  be sent. A first session write after the commit is refused for the same reason; a write with a
  session the visitor already has is ordinary, because it needs no header.
- The commit ends the render's budget and starts the stream's own clocks, so a stream outlives
  `Pages:Render:TimeoutSeconds` and is bounded by `Pages:Stream` instead (see [the page execution
  budget](#page-execution-budget)). A committed stream gives its render slot back and takes a stream
  slot, so one that is mostly waiting costs the render pool nothing; when every stream slot is taken
  the first flush throws `PageStreamRefusedException` before anything is committed, and the endpoint
  answers `503`. This is the shape a page needs for progressive HTML, long-poll and server-sent
  events; it does not make an unbounded stream safe, which is the app's own problem.

#### Relaying an upstream stream

When an upstream may be slow, write and flush the response before opening it or waiting for a line.
That commits the client response, so `Pages:Stream` bounds the client connection while
`Pages:Fetch:StreamSilenceSeconds`, `StreamMaxSeconds` and `MaxStreamBytes` independently bound the
upstream body. A failure after the flush cannot change the response status; report it in the stream
instead:

```html
<%
IHttpFetch fetch = ctx.Capabilities.GetRequired<IHttpFetch>();
ctx.Capabilities.GetRequired<IPageLayout>().Layout = null;
ctx.SetContentType("text/event-stream; charset=utf-8");
ctx.SetHeader("X-Accel-Buffering", "no");
ctx.WriteRaw(": connected\n\n");
await ctx.FlushAsync();

HttpFetchStream upstream = await fetch.StreamAsync(
    new HttpFetchRequest("https://api.example.com/events"));
try {
    string? line;
    while ((line = await upstream.ReadLineAsync()) is not null) {
        ctx.WriteRaw($"data: {line}\n\n");
        await ctx.FlushAsync();
    }

    string? failure = upstream.Truncated
        ? "truncated"
        : upstream.Failure == HttpFetchFailure.None ? null : upstream.Failure.ToString();
    if (failure is not null) {
        ctx.WriteRaw($"event: error\ndata: {failure}\n\n");
        await ctx.FlushAsync();
    }
} finally {
    await upstream.CloseAsync();
}
%>
```

`ReadLineAsync()` returns `null` at end of body or when the stream fails; inspect `Failure` and
`Truncated` after the read loop. The host closes any stream a page leaves open when the render ends
or is abandoned. The example uses `WriteRaw` because this response is SSE, not HTML; each upstream
line is framed as an SSE `data` field.

### Jobs

A **worker** is a stored source whose only blocks are declarations and which declares exactly one
public type implementing `IWorker` with a parameterless constructor; it is compiled when it is saved
and never served (see [assets and versioned URLs](#assets-and-versioned-urls)). A **job** is one run
of one, started by a page:

```html
<%
IPageJobs jobs = ctx.Capabilities.GetRequired<IPageJobs>();
PageJobStart start = await jobs.StartAsync("/_work/answer", "the question");
if (!start.Started) { ctx.Write("Could not start: " + start.Refusal); return; }
%>
<p>Started <%= start.Id %>.</p>
```

The job takes a job slot rather than a render slot, because it is answering nobody: `Pages:Jobs`
bounds how many wait (`QueueCapacity`), how many run (`MaxConcurrent`), how long one may be quiet
(`SilenceSeconds`), how long one may live (`MaxDurationSeconds`), what it may be sent
(`MaxMessageBytes`, `MaxPendingMessages`), and what its journal keeps (`MaxEvents`, `MaxEventBytes`,
`RetentionMinutes`). The runner gives each job its own scope and load context and unloads the
worker's assembly when the job ends; a worker whose library moved is compiled again before it runs.

The running worker gets an `IWorkerContext`, which is deliberately not an `IContext`: `Id`, `Path`
(the starting page), `Worker`, `User`, `Input`, `Aborted`, `Capabilities`, `Log`/`LogWarning`/
`LogError`, `ReceiveAsync` and `Publish(type, dataJson)`. Its `Capabilities` offer `IPageStorage`
bound to the **starting page** — the data, so the job and the view that shows it share one store —
and `IHttpFetch`, `IPageCatalog` and `IPageJobs` bound to the **worker** — the code, so a credential
bound to the worker stays the worker's. A worker is offered no `IMarkdownRenderer`, no `IPageWriter`
and no `IPageLayout`: it publishes text or JSON, and the page renders it.

`ReceiveAsync(waitSeconds)` pauses the silence clock while it waits, so a worker waiting for its
owner is bounded by the ceiling rather than by silence; a timed wait returns and the clock resumes,
which is how a worker does periodic work without a sleep primitive. A worker that neither publishes
nor receives for `SilenceSeconds` is cut, and one that outlives `MaxDurationSeconds` is stopped.
`CancelAsync` ends a job at its next `await`, and the host stopping does the same.

The journal is in memory, bounded and replayable: `ReadEventsAsync(jobId, after, waitSeconds)` answers
the events after a sequence, waits briefly for the next one, and reports `Reset` when the caller's
sequence has been dropped — the view starts again from `Next` rather than treat the gap as delivered.
A restart loses every job and every event; a worker that must not lose what it has done writes it to
the starting page's storage, and that state is the record. A page that finds its own state says
running while `GetAsync` returns `null` knows the host restarted.

## Assets and versioned URLs

Every stored revision has a kind, judged from its source and stored beside it
(`PageKind`): **text** (no C#), **page** (C# that renders), **library** (only declaration blocks, for
other sources to name) and **worker** (only declaration blocks, declaring one entry point a page
starts). A kind is a property of the source rather than of the key, so editing a page can change what
it is; there is no configuration for it. Generation answers for all but the last: whether declarations
are a worker is a question about types, so the compiler refines a declaration-only source to worker
when it declares exactly one public type implementing `IWorker` with a parameterless constructor. Two
entry points, or an entry point that is not public or cannot be constructed, is a `PAGE006` error and
no image.

A page with no code is never compiled. Generation reads the tokens it already produced, and a
template made only of text has nothing to compile: the revision is stored with no image, its kind is
text, and every request writes the stored source out byte for byte.
Such a page has only the template-shape warnings `TemplateShapeCheck` can find without compiling
(`pages-the-shape-check`); a clean text page has none.

A no-code page is a complete representation: it is never wrapped, so its stored source is the
response even for an HTML key. It gets no banner, no skip link, no base sheet and no title
requirement; if it wants chrome, it adds a code block and becomes a fragment.

A **library** is a template whose only tokens are declaration blocks. It has nothing to render, so it
is never served: its key answers `404` and it is never an asset, never pinned with `?v=` and never
wrapped. It is checked when it is saved — Roslyn analyses it and the diagnostics are stored beside the
source, with no assembly — and a page API violation inside it is reported at the library's own line
and key. A library declares at least one type in a namespace, which is how a page names it; a page
may not declare a namespace.

A **worker** is a library that declares one entry point: a public type implementing `IWorker` with a
parameterless constructor. It is compiled to an assembly when it is saved, so its image is stored
beside its source, and its diagnostics are the compiler's as any page's are. It is never served, never
wrapped and never an asset, and it is not a library: the closure a page or a library is compiled with
is the live libraries alone, so a source that names a worker's namespace fails with the compiler's
missing-namespace diagnostic. A worker's own closure is recorded like a page's, so a worker whose
library moved is compiled again before it is run.

A library lives under **`/_lib/`** by convention: `/_lib/document` is the seeded document writer,
and a site's own helpers belong beside it rather than in the keys a visitor might ask for. The
convention is a name, not a rule — nothing refuses a library at another key, and its kind comes from
its source — but it keeps code out of the site's page tree, gives a library a folder the page list
groups, and lets `Pages/Seeds/_lib/document` seed the key it is served from. A library's key is never
served, so nothing about it is public except the source an owner can read in the editor.

A page is compiled with its **closure**: every library its own `using` directives name, and the
libraries those name in turn, resolved to a fixed point from an index of the live libraries'
namespaces. Only the closure is compiled and checked, so a broken library cannot reach a page that
does not name it. Each library is a syntax tree of its own in the page's compilation, so nothing is
spliced and a library's types are its own; the page API policy walks every tree, which is what makes
the page's use of a library type trusted. The closure's keys and revision hashes are stored with the
image as a fingerprint, and a page whose closure has moved is compiled again before it runs — the
heads are read, the sources only when something actually compiles.

An **asset** is exactly a page with no code. `PageAssetPolicy.IsAsset` is the one answer the
endpoint, `ctx.AssetUrl` and the head resolver use. There is no document extension list.

`?v=<hash>` on a page's own URL selects one retained revision. The value is the stored 64-character
lowercase hexadecimal revision hash itself (`RevisionHash`); the path is unchanged, so `ctx.Path`,
relative URLs, cookies and headers all mean what they did before. An unknown, empty, repeated,
wrong-case or malformed hash is a `404`, and so is a revision stored under another path: a versioned
URL never falls back to the head. A method other than `GET` or `HEAD` is a `405` with
`Allow: GET, HEAD`.

A versioned asset is written with `Cache-Control: public, max-age=31536000, immutable`, a strong
`ETag` (the hash) and `Content-Length`; `If-None-Match` answers `304` with the same cache headers and
no body. The hash selects the revision; the verdict decides the promise. A versioned URL that names
a revision with code is rendered normally with `no-store`.

`ctx.AssetUrl(href)` versions a bare site-relative link when its target is a live, no-code asset and
returns it unchanged otherwise; queries, fragments, absolute and protocol-relative URLs all keep
their spelling, so an unversioned fallback still works. A page's own markup is never rewritten by
the host. A page that writes a document of its own resolves its base sheets and icon with
`ctx.AssetUrl`, as the seeded document library does, and the shell's own views resolve their sheets
and scripts the same way.

Pages run for `GET`, `POST` and `HEAD`; any other verb is answered 405 by routing. A request body
over 1 MiB is refused with 413 before it is read. A buffered page that outruns its execution budget is
answered `504` with the same document every other failure uses; a page that has already committed is
truncated instead. See [error responses](shell.md#error-responses) and
[the execution budget](#page-execution-budget) for the deadline itself. Keys the site's own routes
answer for are listed in `Pages:ReservedPaths`, refused by the editor and never served.

## Compiled pages

Compiling is the expensive part of serving a page, so it happens when a page is saved rather than
when it is viewed. A save — from `EditorEndpoint`, from the assistant's tools, or from a page saving
itself — goes through `PageSaver`, which compiles the template through `PageImageBuilder` and stores
the emitted assembly beside the template in the same row; `PageEndpoint` then reads the image and runs
it through `PageRenderer`. Four derived columns carry the result: `TemplateHash`, `RuntimeId`, `Image` and `Diagnostics`.

A stored image is trusted only when both keys match:

- `TemplateHash` is the SHA-256 of the template (`TemplateFingerprint` in `Core`), so an edit that
  bypasses the editor cannot be answered with stale code.
- `RuntimeId` is the compile format version, the page policy version and the module version ids of
  `uk.osric.asp.Sdk` and `uk.osric.asp.Core`, so a build with a different page contract, a changed
  generator, or a tightened policy never runs an image it did not produce. Debug and Release builds
  differ, as they should.

A page whose image is missing or stale — a row written before images existed, a direct database
write, or every row after an upgrade that touched `Sdk` or `Core` — is compiled on its next request and
stored. That is the only compile outside a save: there is no startup warm-up, so the first visitor
after a deploy pays one compile per page. A failure is stored too, so a broken page serves its
diagnostics without recompiling, and the editor shows them after the save instead of losing the
edit. `Diagnostics` is a JSON array of positioned, severity-tagged `PageDiagnostic` values; a
warning-only page stores its image and reports "Saved with warnings." A compiler fault stores only
the source, so the author's edit survives and the next request retries.

**A compiled page is nullable-enabled and its generated scaffold is not.** `PageCompiler` sets
`NullableContextOptions.Enable`, so a page is analysed exactly as the product's own code is and
`ctx.User` is `PageUser?` in a page. `TemplateSourceGenerator` wraps every line it wrote itself in
`#nullable disable` and every region it mapped to the template in `#nullable enable`, with the
directive outside the `#line` region so the author's line and column do not move. The scaffold is
oblivious because `PageCompiler.MapDiagnostics` drops an unmapped warning: without the wrapper, a
warning from generated code would reach nobody, and a page would lose a diagnosis rather than gain
one. The promise is held by `ScaffoldTests` in `Runtime.Tests`, which compiles every token shape
under the production options and fails the build if the wrapper starts warning; making an unmapped
warning fatal instead would turn a new Roslyn release into a `500` on a working page. A page may
still write `#nullable disable` in its own code, and within its own block that wins.

**A warning never stops a page.** Emit either succeeds or reports an error, so `CompileResult.Image`
is null exactly when the page did not compile and a stored image with bytes carries warnings only.
A page that compiles with warnings is served, stored and hashed exactly as a clean one; the editor,
the history and the assistant all say `Compiled with warnings`, and no path treats it as a failure.

A page's state is one of five values, described in one place (`PageImageState` in `Web`): `Not
compiled` (no image record), `Stale` (an image from another `RuntimeId`), `Compile failed` (a record
with no assembly), `Compiled with warnings` and `Compiled`. The editor page lists it in a State
column, and the assistant's `list_pages` carries the same word per page. Staleness is judged on
`RuntimeId` alone — hashing every template to answer a listing is not worth it — and a retained
revision is history rather than stale, so only the listing and the assistant pass the running build.

Compilation happens before the database write, so the lazy path stores its result only while the
page still holds the content it compiled, and re-reads the page when a save landed in between. A
page that keeps changing is served from the last compile rather than failing.

`IPageStore` owns both halves: `GetPageAsync`, `SavePageAsync` and the conditional
`TrySetCompilationAsync`. `Runtime` knows only how to turn generated source into bytes and bytes into
a loaded handler (`IPageCompiler`, `LoadedPage`); it never sees a page.

## Page API policy

Stored pages are C#, so the surface they may touch is constrained twice.

- `PageApiPolicy` in `Core` is the default-deny allow-list: the SDK namespace, a short list of base
  class library types, and any `System.*` type whose name ends in `Exception`, because catching one
  should not need an entry of its own. Each entry is checked against the symbol the page actually
  resolved. It is data plus a pure function, so the list is readable and the diagnostic names the
  API to add. `System.Object` is deliberately absent: allowing it would allow `GetType()`.
- Membership is decided by capability, not by which feature last needed an entry. A type is admitted
  when its only effect is to compute over a value the page already holds or to shape a value the page
  already returns; it is refused when it adds a way to reach a resource — a file, the network, a
  process, the environment, an assembly, reflection or process-wide state. A type that bundles a way
  to reach with a useful helper is admitted with the reaching members denied by name, which is how
  `Task` is admitted and `Task.Run` is not (`pages-admit-what-adds-no-capability`). That rule admits
  `StringComparer`, `HashCode` and `Random`, and refuses `System.Environment`, whose `NewLine` would
  arrive attached to `Exit` and `GetEnvironmentVariable`.
- A refusal is one diagnostic per type. The checker reports the symbol a node resolves to and the
  type it resolves to, so a member of a refused type would otherwise arrive twice; the member is the
  one reported, because it is what the author wrote. A refusal of a member also names its type —
  `PAGE001: pages may not use System.IO.File.ReadAllText. The type System.IO.File is not part of the
  page API.` — because the type is what an entry would change
  (`pages-admit-what-adds-no-capability`).
- `System.Threading.Tasks.Task` is admitted by *type*, and ten of its members are denied by name —
  `Run`, `Factory`, `ContinueWith`, `Wait`, `WaitAll`, `WaitAny`, `Result`, `GetAwaiter`, `Delay`
  and `Yield` — because each starts work the render deadline cannot stop or turns an `await` back
  into a blocked thread. The namespace stays denied, so `TaskCompletionSource`, `TaskScheduler`,
  `Parallel` and `ValueTask` are `PAGE001` without being named (`pages-the-api-policy`).
- A narrow set of `System.Text.Json` types is allowed, so a fetched JSON body can be read, built and
  shaped: `JsonDocument`, `JsonDocumentOptions`, `JsonElement`, `JsonSerializer`,
  `JsonSerializerOptions`, `JsonSerializerDefaults`, `JsonValueKind`, `JsonException`,
  `JsonNamingPolicy`, and the `System.Text.Json.Nodes` namespace, which holds `JsonNode`,
  `JsonObject`, `JsonArray` and `JsonValue`. The `System.Text.Json.Serialization` attributes are not,
  and `System.Net` stays denied — `IHttpFetch` is the page's only egress (`pages-the-api-policy`).
- Cryptography is admitted by type: `System.Text.Encoding` to turn text into bytes, `SHA256`,
  `SHA384` and `SHA512` to hash, `HMACSHA256`, `HMACSHA384` and `HMACSHA512` to sign,
  `RandomNumberGenerator` for a nonce and `CryptographicOperations.FixedTimeEquals` to compare a
  signature. Nothing else in `System.Security.Cryptography` is, so a page cannot name `MD5`, an
  asymmetric primitive or a certificate (`pages-the-api-policy`).
- `PageApiChecker` in `Runtime` walks the generated syntax tree, projects Roslyn symbols onto
  `ApiReference` values, rejects `stackalloc`, `dynamic`, pointers, function pointers and `unsafe`,
  and enforces the shape rule: the generated `GeneratedPage`, last, plus the page's own type
  declarations ahead of it. That rule is what stops a page closing the method to declare statics,
  its own entry point or a module initializer. A declaration block that holds a namespace, an
  assembly attribute, a statement or a member outside a type, or that names its type `GeneratedPage`,
  is `PAGE002` with the specific message and, where the node has one, the template position.
  Declarations are not a way around the list: a banned type named inside a declared type is
  `PAGE001`, and a `using` directive grants nothing by itself.

The compiler's reference set is the page SDK plus the base class library: every other
`uk.osric.asp.*` assembly and `Microsoft.*` is left out, so naming the shell, the host's compiler
support or Roslyn fails with CS02xx before the policy runs. The trim
cannot remove `System.IO` or `System.Environment`, which live in `System.Private.CoreLib`; that is the
policy's job. A post-emit scan of the image's type references was considered and dropped, because it
could only find what the semantic walk already reports, at the price of false rejections for
compiler-emitted helpers.

The set is built from the runtime's `TRUSTED_PLATFORM_ASSEMBLIES` when the compiler is constructed,
and `Program` constructs it before the host listens. A runtime that does not describe its platform
assemblies — a single-file or trimmed deployment — therefore fails the start with a message naming
the value, instead of compiling every page against nothing.

The policy is versioned, because it is applied when a page compiles. `RuntimeId` is
`{FormatVersion}:{PolicyVersion}:{SdkMvid}:{CoreMvid}`, so tightening the list invalidates every
stored image and each page is rechecked on its next request. Both module version ids are in the
value: the SDK's covers what a page binds against, and Core's covers the generator, so a change
to the generated source invalidates an image even when the contract is unchanged.

## Load isolation

`LoadedPage` loads each image into a `PageLoadContext`, a collectible `AssemblyLoadContext`
that refuses `uk.osric.asp.Core`, `uk.osric.asp.Runtime`, `uk.osric.asp.Web`,
`Microsoft.AspNetCore*`, `Microsoft.EntityFrameworkCore*` and `Microsoft.Data.Sqlite`, and lets
everything else resolve through the default context. The SDK is allowed through on purpose: the
fallback hands the page the same `IContext` type the host is using. It throws rather than returning `null`, because `null` means "ask the
default context" — the exact escape the type exists to close.

This is isolation from the site's internals, not a sandbox: a page still runs in-process with the
site's identity and the whole base class library. The process boundary is [SEC-03](jobs/SEC-03.md),
and it is deliberately not built — see "Page execution budget" below for what stands in its place.

## Page execution budget

A page is arbitrary C#, and .NET cannot interrupt arbitrary C#: `Thread.Abort` does not exist. So the
budget bounds the *response*, not the page. `PageRenderer.RenderAsync` runs the handler on a worker and
waits for it against one deadline that covers both queueing and running:

- `Pages:Render:Slots` renders may run at once; further requests wait asynchronously, holding no
  thread. A request that waits past the deadline is shed: it answers `504` immediately without ever
  running the page.
- `Pages:Render:TimeoutSeconds` is the whole budget, and may be fractional: the deadline is honoured
  in milliseconds, and a value below `PageBudget.MinimumTimeoutSeconds` is refused at startup, so a
  zero cannot switch the limit off. When it expires the site answers `504`, calls
  `SimpleContext.Abandon` so the still-running page cannot write any more, and gives up on it. A
  response that has already committed cannot become a `504`: the deadline truncates it and aborts
  the connection instead (see [Streaming](#streaming)). A
  render that never returns cannot be reclaimed: it is counted, logged with the page's path and the
  elapsed time, and recorded on `page.render.abandoned`. A page that is *waiting* on an `await`
  returns its thread to the pool while it waits, so abandonment costs a task rather than a thread;
  when the deadline fires its next `await` throws and it unwinds, in the same instant the deadline
  itself does — so which of the two the renderer sees first is a race, and the answer is `504`
  either way. A page that blocks synchronously is unchanged, and is escaped only by abandonment and
  the container's restart. A request that spends the budget before the page starts — waiting on
  storage, or waiting for a compile slot — answers `504` the same way, but nothing is abandoned
  because nothing ran.
- Compiling has its own slot and deadline (`Pages:Compile:Slots`, `Pages:Compile:TimeoutSeconds`).
  Waiting for a slot is bounded by that deadline, because an unbounded wait would park a request
  thread — and the render slot it already holds — behind the compiler. In a render the wait also ends
  at the render's deadline, since nothing has been compiled yet and the next request can try again; on
  a save it ends with a `PAGE003` error instead. Roslyn observes a cancellation token between
  operations rather than inside one, so the deadline is a coarse bound, and a compile that trips it is
  stored as a `PAGE004` error so the editor shows it and the page is not recompiled on every request.
- A compile is *not* cancelled by the render's deadline. Throwing the work away would leave the page
  unservable — every request would pay the same compile — so it finishes, stores its image, and the
  request answers `504` without starting the page. The next request is served from the store.
- `Pages:Render:MaxBodyChars` caps the buffered body, so a page that writes in a loop fails quickly
  instead of growing the process. The cap stops at the first flush: a streamed body is already on the
  wire and is not charged, so a post-commit loop is bounded by the stream's clocks and the container
  rather than by this setting.
- A committed stream is bounded by `Pages:Stream` instead of by the render budget. The commit ends
  the render's deadline and starts two clocks: `SilenceSeconds` truncates a stream that flushes
  nothing for that long, and `MaxSeconds` truncates one that is still busy that far past its commit,
  so neither a page that stopped talking nor one that never stops can hold a slot forever. Every
  flush restarts the silence clock. `Slots` caps how many streams may be committed at once; a render
  whose first flush finds none is answered `503` and never commits. Each truncation is logged with
  its reason and recorded as the stream's outcome on `page.stream.duration`.

Every key in this section is required: a missing or out-of-range value stops the site at startup
with a sentence naming the key, its range and the shipped value. See
[configuration](architecture.md#configuration).

The handler still runs in the site's process, and the `IContext` it writes to is unchanged, so a page
can still mutate process-wide state; that is the residual SEC-03 records.

## Health and restarts

The budget cannot reclaim a lost thread, so the supervisor is the kill switch.

`/heath` reports two checks: `storage`, and `pages`, which is unhealthy once
`Pages:Render:AbandonedLimit` renders have been abandoned and are still running. A page that returns
late decrements the count, so a one-off stall does not ask for a restart.

The quadlet runs the same probe as podman's health check
(`wget -q -O /dev/null -T 3 http://127.0.0.1:8080/heath`, every 10 s, three failures) with
`HealthOnFailure=restart`, so a wedged process is restarted within about 35 s. That turns the two
worst outcomes into a short outage instead of a permanent one: a page that loops forever against a
thread pool, and a page that calls `Environment.Exit`. The container is also capped
(`--memory=2g` with `--memory-swap=2g`, `--pids-limit=512`, `--cpus=3`), so a page that
allocates without bound meets the OOM killer inside the container rather than paging or taking the
host.

`StartLimitBurst=3` in 60 s still applies: a page that wedges on every request restarts the container
three times and then leaves the unit failed until it is reset and the page is fixed.

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
the shell's identity and one visitor's state rather than anything a page owns. `IContext` is what a
page reads and asks for — `Request`, `Path`, `EditorPath`, `User`, `Aborted`,
`Log`/`LogWarning`/`LogError`, `AssetUrl`, `Capabilities` and `Response`. Everything a page writes
goes through `ctx.Response`, an `IPageResponse`: `Write`/`WriteRaw`, `FlushAsync`, `SetStatus`,
`SetContentType`, `SetHeader`, `AllowScripts`, `Redirect`, `RedirectToLogin` and `AntiforgeryField`
(`pages-the-response-is-one-object`). It is contract rather than a capability, because every page has
one, and a helper can take an `IPageResponse` parameter when all it needs is the output. The old
spelling — `ctx.Write` and the rest — still compiles for one release and reports a `PAGE009` warning.
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

Thirteen capabilities are registered today — eight that do I/O or work and are bounded by the render,
one that is the page's slots in its document, one that renders other pages into this one, two that are
pure renderers, and one that is a fact about the build:

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
  `moosemorals.com (page) fetchmoose@fluffypeople.com` as its `User-Agent`, set by the host after the
  page's own headers, so a page cannot claim to be another client. `GetAsync` and `PostAsync` sit over
  `SendAsync(HttpFetchRequest)`; `StreamAsync` opens the same request rules and returns an
  `HttpFetchStream` that reads decoded text lines with `ReadLineAsync()`, exposes response metadata
  and failure values, and can be closed explicitly. Open streams close when the render ends or is
  abandoned. The render's shared request count, the deadline and abandonment bound the request
  (`pages-outbound-fetch`, `pages-capabilities-are-one-seam`). A request may name an operator-configured credential with
  `Credential`: the host supplies the header, only to that credential's host and only from the page
  the credential is bound to — or from a page under it, when the entry's `Page` ends in `/*`, so a
  page family and its worker can share one token. The page never reads the value, and an unknown
  name is an `InvalidRequest` (`pages-fetch-credentials`). Response headers use `Pages:Fetch:TimeoutSeconds`; a stream's body
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
- `IPageCatalog` reads the page store and answers what the shell can do: `ListAsync(request)` lists one
  slice of the heads under a subtree, `ReadAsync(key)` reads a live page's template, `RevisionsAsync(key)` lists a
  page's history, `ReadRevisionAsync(key, hash)` reads one retained revision, and `ShellLinks()` returns
  the shell's links as values. Each `PageEntry` marks the head
  `IsDeleted`, `IsReserved`, `IsHtml` and `IsAsset`, so a view filters rather than re-deriving the
  host's rules, and carries the head's image state in `State` — the host's own words, judged against
  the running build — so a page's listing says what the editor's says without reading source
  (`pages-a-page-has-a-state`). The class the shell's own list marks it with stays in the shell: how a view presents a
  state is the view's business. `Libraries` is the keys of the head's closure, transitively — what a
  save to one of those keys would recompile — parsed from the revision's stored fingerprint where that
  fingerprint is written, so the two cannot spell one fact twice; a library the page has not
  recompiled without is still named and the row's `State` says what happened to the compile
  (`pages-a-page-names-its-libraries`). A listing is a `PageListingRequest(prefix, after, limit)` answered by a
  `PageListing`: `after` is the last key the caller was given and is exclusive, so the next call resumes
  where the last one stopped and a save between two calls neither skips nor repeats a row; `limit` is
  bounded by `Pages:Read:MaxRowsPerList`, which an absent limit takes, and a larger one is refused with
  the setting's name rather than trimmed; and `Truncated` says whether anything stands after the last
  row, so a caller walks a store of any size one slice at a time (`pages-a-listing-resumes-from-a-key`).
  `PageListing.Version` is the fingerprint of every head under the prefix, read in the same snapshot
  as the rows; pass it back as `PageListingRequest.Version` and `PageListing.Changed` says whether
  the list moved. The token is opaque and compared for equality, a save under another prefix does not
  move it, and it covers the head rows rather than what they compiled to
  (`pages-a-listing-carries-a-fingerprint`).
  It reads and never writes. The read is
  as public as the page that calls it — there is no gate, and a page that wants privacy writes the
  ordinary `ctx.User` guard — and the render's deadline, abandonment and the shared chain budgets
  bound it (`pages-the-store-is-read-through-one-seam`, `pages-reading-the-store-is-public`, `pages-a-listing-carries-facts`, `product-manual-is-a-page`, `pages-delete-install-and-restore`, `pages-page-first`). See [source storage](storage.md#source-storage).
- `ShellLinks()` is the one synchronous member: a `ShellLink` carries an `Href`, a `Text`, the
  `ShellSection` it belongs to, the `ShellAudience` it is for and the `ShellMatch` the shell uses to
  mark it current. The host returns the whole list — both sections, every audience, the sign-in link
  carrying this request's way back — and the page filters, orders, relabels, marks current and writes
  its own markup. There is no rendered menu, no `IsCurrent` and no footer: the seeded `/_layout`
  draws the site's menu from the same values, and the footer stays the layout's own chrome
  (`pages-the-catalog-knows-the-shell`). A worker has no request and no visitor, so its list is
  empty.
- `IPageCheck` answers "would this source check" for text the caller already holds, and stores
  nothing: no revision, head, image, closure fingerprint or seed record, and none of the recompiling a
  save would cause. `CheckAsync(language, source)` names its language from the closed set in
  `PageCheckLanguages` — `template` and `javascript`, and never inferred from a key's extension — and
  answers a `PageCheckResult`: what generation judged the source to be in `Kind` (text, page, library
  or worker, or `null` for a script or a check that ran out of time), whether it would be served in
  `Valid` (a warning leaves it valid), and the compiler's diagnostics as `PageDiagnostic` values, each
  naming a library when the problem is not in the page itself. A `javascript` check is the
  template-blanking skeleton and the parser the assistant already uses: syntax only, no imports
  resolved, nothing executed, and the positions are template space. There is no key: a caller that wants a saved page
  checked reads it with `IPageCatalog.ReadAsync` and passes the template, so a source that has never
  been saved is checked exactly as one that has. It needs a signed-in visitor, because a compile is a
  shared resource; it spends the same `Pages:Compile:Slots` and `Pages:Compile:TimeoutSeconds` a save
  does; and a check that waits past the render's deadline or the compile timeout answers a `PAGE010`
  error rather than hanging, because it has no author's edit to protect
  (`pages-a-check-stores-nothing`, `pages-a-check-names-its-language`, `pages-a-check-needs-a-visitor`).
- `IPageWrites` is the store's write capability, and every page that is not a layout is offered it,
  and a worker is offered it bound to the starting page and the credential that started the job:
  the authority is the presented credential's, not the page's (`pages-any-page-may-write-the-store`).
  Each of its members is one write and, under the seam, a transaction of one: it compiles, checks its
  condition and commits before it answers. A write made while the render has a transaction open joins
  that transaction instead, answering Staged, so an embedded page's write lands with the page's commit
  and is discarded by its rollback. `BeginTransaction()` answers an `IPageTransaction` carrying
  the same members: the writes are staged, each save compiles as it is staged, and `CommitAsync()`
  proves every condition again and applies them in one store transaction or none of them, answering one
  `PageWriteResult` per operation — the key, the operation, the outcome, the hash and the diagnostics.
  A refusal writes nothing: the operations are applied in order and each is proved against what the
  ones before it wrote, so a create, a move and a delete of one page in one transaction all succeed,
  while a write that refused rolls everything back and the report says RolledBack for what was applied
  and undone and NotAttempted for what was never reached. Contention is a row the page answers, and the
  host does not retry on its behalf. A transaction that is disposed without a commit
  writes nothing, one page holds one open at a time, and a render may commit as many as it has work
  for. A staged save reads the libraries staged before it as well as the store's, so staging order is
  what a page gets and nothing is reordered behind it.
  It has two saves. `SaveAsync(template)` saves the calling page's own source, bound to `ctx.Path`, and
  answers a `PageSaveResult` — the new revision's hash and the compiler's diagnostics as
  `PageDiagnostic` values. `SaveAsync(key, template, expectedHash)` saves any key the shell does not
  route, only while the head still has the hash the form was drawn from, and answers a `PageEditResult`
  whose outcome is Saved, Stale or Unchanged; a `null` expectation means the form was drawn from a
  page with no live source, so a create cannot overwrite one. Both compile before they store, so an
  error is stored and reported and a warning still serves. `MoveAsync(from, to, expectedHash)` renames
  a page: every revision is re-filed under the new key with the hash it was saved with, the anchor,
  the content hash and the page state move with it, nothing is compiled, and nothing is left at the old
  key. It answers a `PageMoveResult` — Moved, Unchanged, Stale, SourceNotFound, TargetExists or
  FolderNotEmpty with the keys below the old one — while a reserved key, a library and the page moving
  itself throw, because no argument makes those legal. **The write requires a signed-in visitor**:
  it refuses an anonymous render before it stores anything, and a `HEAD`, an ended render, a reserved
  key or a second write in one render is a page bug. A write belongs to the next request, and a write
  that has started finishes even if the render times out. A layout is offered the read and not the
  write. `Pages:Editor:Key` says only where the editor is served — it is not an authority — and the
  editor's own key is an ordinary key, so `/admin/seeds` is the way back from a deleted editor. The
  rest is the editor's toolbox: `Resolve`, `MoveAsync`, `DeleteAsync`, `RestoreRevisionAsync`,
  `AdoptSeedAsync`,
  `InstallSeedAsync` — which refuses a row the seed gate does not allow — and `SeedSourceAsync`
  (`pages-a-page-may-save-itself`, `pages-layouts-are-markup`, `pages-scratch-saves-itself`,
  `pages-delete-install-and-restore`). See [source storage](storage.md#source-storage).
- `IPageLayout` is the page's own slots in the document it is served in — `Title`, `Head`,
  `Stylesheets`, `AddHead`, `AddStylesheet` — and `Content`, the page inside it when this page wraps
  another. It is a capability rather than more `IContext` because it is a set of slots a render
  offers, and because a page that never wants a layout never meets one
  (`pages-a-layout-is-a-capability`). The host composes nothing behind the outer page's back, so a
  wrapper folds `Content.Title`, `Content.Head` and `Content.Stylesheets` itself
  (`pages-a-page-inside-a-page-is-a-capture`). See [layouts](shell.md#layouts).
- `IPageComposition` renders stored pages beside and around this one. `EmbedAsync(key, request?)`
  renders the whole page — body, every wrapper it declares, its own key, storage and session, the
  visitor's identity, one budget and one slot — writes the outermost body where the call is and
  returns the outermost `PageFrame`; `CaptureAsync` is the same render returned as a `PageCapture`
  without the write; `Wrap(key, request?)` and `Wrap()` declare the pages that wrap this one,
  innermost first. The input in both directions is a `PageRequest`, with `WithQuery`,
  `WithAnotherQuery` and `WithoutQuery` as its copy-and-change helpers
  (`pages-an-embed-is-a-render`, `pages-a-composed-page-gets-a-request`,
  `pages-a-capture-returns-the-body`, `pages-a-page-chooses-its-layout`). See
  [composition](shell.md#composition).
- `IMarkdownRenderer` renders Markdown to HTML with `ToHtml`. It is pure and needs no `await`; its
  result is HTML, so the page writes it with `<%== %>`. The page profile is CommonMark plus heading
  anchors, pipe tables and auto-links, and raw HTML passes through: this is the author's renderer, not a
  sanitiser, and the same warning is in `IMarkdownRenderer` and `/help` (`pages-markdown`). A heading
  carries an `id` derived from its text, with a numbered suffix when two headings share one, so a page
  can link to a section. `MarkdownOptions` switches the three features per call — an option belongs to
  the render, not to the shared capability — and `MarkdownOptions.Default` is the page profile
  (`pages-markdown-options-are-per-call`). A table's header cells carry `scope="col"` and no inline
  style.
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
  into sending its body while it runs with `ctx.Response.FlushAsync()`; the first flush is the commit point
  (see [Streaming](#streaming)).
- Headers are bounded by an allow-list in `PageResponseState`: `Content-Language` and any `X-` name.
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

A page can send its body while it runs by calling `await ctx.Response.FlushAsync()`. The first flush is the
commit point and the whole of the opt-in:

- The status, content type, headers and script policy are applied, `no-store` and any session or
  antiforgery cookie already minted for the response are written, and the response starts. From then
  on `SetStatus`, `SetContentType`, `SetHeader`, `AllowScripts`, `Redirect` and `RedirectToLogin`
  throw, and reading `AntiforgeryField` throws.
- Writes after a flush are buffered until the next flush; whatever is still buffered when the
  handler returns is sent then. The response has no `Content-Length`, because its length is not known
  when the headers go.
- A flushing page is served alone. A wrapper needs the whole body before it can run, so a flush by a
  page that declared `Wrap` throws and names the remedy: write the page's own document and declare no
  wrapper. A `Wrap` call after the first flush throws for the same reason. A non-HTML page is never
  wrapped. A wrapper itself cannot flush, and neither can an embedded page.
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
// No Wrap call: a page that streams writes its own document.
ctx.Response.SetContentType("text/event-stream; charset=utf-8");
ctx.Response.SetHeader("X-Accel-Buffering", "no");
ctx.Response.WriteRaw(": connected\n\n");
await ctx.Response.FlushAsync();

HttpFetchStream upstream = await fetch.StreamAsync(
    new HttpFetchRequest("https://api.example.com/events"));
try {
    string? line;
    while ((line = await upstream.ReadLineAsync()) is not null) {
        ctx.Response.WriteRaw($"data: {line}\n\n");
        await ctx.Response.FlushAsync();
    }

    string? failure = upstream.Truncated
        ? "truncated"
        : upstream.Failure == HttpFetchFailure.None ? null : upstream.Failure.ToString();
    if (failure is not null) {
        ctx.Response.WriteRaw($"event: error\ndata: {failure}\n\n");
        await ctx.Response.FlushAsync();
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
if (!start.Started) { ctx.Response.Write("Could not start: " + start.Refusal); return; }
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
bound to the worker stays the worker's. A worker is offered the writer bound to the **starting page**
and to the credential that started the job, and no `IMarkdownRenderer` and no `IPageLayout`: it
publishes text or JSON, and the page renders it.

The credential a job acts with is the one the starting render presented — the sign-in's ticket key, or
the id of the run whose view presented the request — and the worker never sees it, because the writer
is constructed around it. A commit asks it the way a request asks it: the account exists with the stamp
it captured, and the ticket row or the run still resolves. When the answer is no, nothing is written
and every operation answers `Revoked`; the job keeps its storage, journal and channel and the worker
decides what to do. A start is checked the same way, so a job may start a job and the child inherits
the credential. A worker makes one write at a time, as a page does.

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
text, and every request writes the stored source out byte for byte, less any comment it holds.
Such a page has only the template-shape warnings `TemplateShapeCheck` can find without compiling
(`pages-the-shape-check`); a clean text page has none. Two of those are about a block that says
nothing: `TEMPLATE006` for one that holds no code and `TEMPLATE007` for one that holds only a comment,
each saying so loudly when it is the block that decided what the template is — a page, or a library
that is never served. A block that ends inside a literal or a comment is `TEMPLATE008`, reported at the
`%>` that ended the block rather than at the literal's opening quote or at the text that follows: that
delimiter is the character the fix changes, and a long literal can put it hundreds of columns from the
quote.

A **comment**, `<%# … %>`, counts as neither code nor text (`pages-a-comment-is-not-code`): it
writes no output, it is emitted into no generated source, it makes no page a page and no library a
page, and it never reaches a visitor — a text page's body is its source with every comment construct
removed (`TemplateComments`), and a page's body never contained it. Its braces are not the page's
braces, so a note that mentions `{` is not an unbalanced one, and `<%#if DEBUG %>` is a note rather
than a directive.

A no-code page is a complete representation: it is never wrapped, so its stored source is the
response even for an HTML key. It gets no banner, no skip link, no base sheet and no title
requirement; if it wants chrome, it adds a code block and becomes a fragment.

A **library** is a template whose only tokens are declaration blocks, plus any comment and any
whitespace between them. It has nothing to render, so it
is never served: its key answers `404` and it is never an asset, never pinned with `?v=` and never
wrapped. It is checked when it is saved — Roslyn analyses it and the diagnostics are stored beside the
source, with no assembly — and a page API violation inside it is reported at the library's own line,
columns and key. A library declares at least one type in a namespace, which is how a page names it; a
page may not declare a namespace.

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
answer for are the shell's route table, refused by the editor and never served.

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
edit. `Diagnostics` is a JSON array of positioned, severity-tagged `PageDiagnostic` values, each with
the run it covers and the key of the library it is in when the problem is not in the page itself
(`pages-a-diagnostic-names-its-source`); a
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
site's identity and the whole base class library. The process boundary is [`sec-page-runs-in-process`](backlog/sec-page-runs-in-process.md),
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
  A page's check spends the same slot and timeout but stores nothing, so its wait and its compile also
  end at the render's deadline, and either timeout answers a `PAGE010` error instead.
- A compile is *not* cancelled by the render's deadline. Throwing the work away would leave the page
  unservable — every request would pay the same compile — so it finishes, stores its image, and the
  request answers `504` without starting the page. The next request is served from the store.
- `Pages:Render:MaxBodyChars` caps the buffered body, so a page that writes in a loop fails quickly
  instead of growing the process. The cap stops at the first flush: a streamed body is already on the
  wire and is not charged, so a post-commit loop is bounded by the stream's clocks and the container
  rather than by this setting. An embed renders into a view of what is left, and the caller is charged
  once when it writes the result, so the embedded text is neither counted twice nor able to skip the
  cap.
- `Pages:Render:MaxLayoutDepth` bounds the pages that wrap one page, and `Pages:Render:MaxEmbedDepth`
  bounds the embeds nested below the layer that is running, so a page composed out of data-driven
  recursion stops. They are chosen apart because a widget is not chrome.
  `Pages:Render:MaxEmbeds` bounds how many embeds one request may make, because depth alone would let
  a listing embed a thousand siblings under one slot and one deadline.
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
can still mutate process-wide state; that is the residual `sec-page-runs-in-process` records.

## Health and restarts

The budget cannot reclaim a lost thread, so the supervisor is the kill switch.

`/health` reports two checks: `storage`, and `pages`, which is unhealthy once
`Pages:Render:AbandonedLimit` renders have been abandoned and are still running. A page that returns
late decrements the count, so a one-off stall does not ask for a restart.

The quadlet runs the same probe as podman's health check
(`wget -q -O /dev/null -T 3 http://127.0.0.1:8080/health`, every 10 s, three failures) with
`HealthOnFailure=restart`, so a wedged process is restarted within about 35 s. That turns the two
worst outcomes into a short outage instead of a permanent one: a page that loops forever against a
thread pool, and a page that calls `Environment.Exit`. The container is also capped
(`--memory=2g` with `--memory-swap=2g`, `--pids-limit=512`, `--cpus=3`), so a page that
allocates without bound meets the OOM killer inside the container rather than paging or taking the
host.

`StartLimitBurst=3` in 60 s still applies: a page that wedges on every request restarts the container
three times and then leaves the unit failed until it is reset and the page is fixed.

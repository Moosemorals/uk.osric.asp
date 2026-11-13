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
(DEC-257). `Path` is always the canonical key.

The handler is asynchronous: `IHandler.HandleAsync` returns a `Task`, the generator always emits an
`async` method, and `await` is therefore an ordinary expression in a code block and inside a write
expression. Any member that does I/O — on `IContext` or on a capability — returns a `Task` and takes
no cancellation token: the context owns `Aborted` and the render's deadline, and an implementation
links them per call (DEC-123).

`IContext.Capabilities` is how a page asks for a capability this render may or may not offer.
`TryGet<T>` reports whether one is there, `Get<T>` returns it or `null`, and `GetRequired<T>`
returns it or throws naming the interface, so the page decides whether absence is fatal. The host
decides what is registered — a page cannot add or replace a capability — and a capability is either
registered or absent, never `null` (DEC-142). The type that declares them is `IPageCapabilities`;
the old `Features` spelling was removed once its release was over, so a page that still says
`ctx.Features` no longer compiles (DEC-228, DEC-254).

Nine capabilities are registered today — five that do I/O and are bounded by the render, one that
is the page's slots in its document, two that are pure renderers, and one that is a fact about
the build:

- `IBuildInfo` says which build is answering: `Commit` is the full git commit hash, or `null` when
  the build did not come from a checkout. It is a capability because the page API denies both the
  assembly and the environment, and the seeded `/about` page links the commit.
- `IPageStorage` is the page's own key/value store. It is bound to the page's key, so a page cannot
  name another page's state; the handle is the isolation. Values are JSON round-tripped by the
  host, keys are ordinal, and writes are last-write-wins. A `HEAD` render may read but never write
  (DEC-125, DEC-126, DEC-127).
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
  and the caps are DEC-233–DEC-235; see [storage](storage.md#page-sessions).
- `IHttpFetch` makes one outbound HTTP request from the host, not the browser, to any `http` or
  `https` host the URL names: there is no host allow-list (DEC-216). Every request carries
  `moosemorals.com (page)` as its `User-Agent`, set by the host after the page's own headers, so a
  page cannot claim to be another client. `GetAsync` and `PostAsync` sit over
  `SendAsync(HttpFetchRequest)`; every failure is a value rather than an exception, and the
  render's shared request count, the deadline and abandonment all bound it (DEC-216, DEC-129,
  DEC-143). A request may name an operator-configured credential with `Credential`: the host
  supplies the header, only to that credential's host and only from the page the credential is
  bound to, and the page never reads the value. An unknown name is an `InvalidRequest` (DEC-230).
- `IPageCatalog` reads the page store: `ListAsync(prefix)` lists the heads under a subtree,
  `ReadAsync(key)` reads a live page's template, `RevisionsAsync(key)` lists a page's history and
  `ReadRevisionAsync(key, hash)` reads one retained revision. Each `PageEntry` marks the head
  `IsDeleted`, `IsReserved`, `IsHtml` and `IsAsset`, so a view filters rather than re-deriving the
  host's rules, and carries the head's image state in `State` — the host's own words, judged against
  the running build — so a page's listing says what the editor's says without reading source
  (DEC-258). The class the shell's own list marks it with stays in the shell: how a view presents a
  state is the view's business. It reads and never writes. The read is
  as public as the page that calls it — there is no gate, and a page that wants privacy writes the
  ordinary `ctx.User` guard — and the render's deadline, abandonment and the shared chain budgets
  bound it (DEC-168–DEC-175). See [source storage](storage.md#source-storage).
- `IPageWriter` saves the calling page's own source: `SaveAsync(template)` answers with a
  `PageSaveResult` — the new revision's hash and the compiler's diagnostics as `PageDiagnostic`
  values — and the handle is bound to `ctx.Path`, so a page writes itself and nothing else. It
  compiles before it stores, so an error is stored and reported and a warning still serves. **The
  write requires a signed-in visitor**: unlike the read, it refuses an anonymous render before it
  stores anything, and a `HEAD`, an ended render or a second save in one render is a page bug. A
  save belongs to the next request, and a save that has started finishes even if the render times
  out. A layout is offered the read and not the write (DEC-189–DEC-196). See
  [source storage](storage.md#source-storage).
- `IPageLayout` is the page's slots in the document it is served in — `Layout`, `Title`, `Head`,
  `Stylesheets`, `AddHead`, `AddStylesheet` and `Body`. It is a capability rather than more
  `IContext` because it is a set of slots a render offers, and because a page that never wants a
  layout never meets one (DEC-148). See [layouts](shell.md#layouts).
- `IMarkdownRenderer` renders Markdown to HTML with `ToHtml`. It is pure and needs no `await`; its
  result is HTML, so the page writes it with `<%== %>`. The page profile is CommonMark plus pipe
  tables and auto-links, and raw HTML passes through: this is the author's renderer, not a
  sanitiser, and the same warning is in `IMarkdownRenderer` and `/help` (DEC-163). A table's header
  cells carry `scope="col"` and no inline style.
- `IDiffRenderer` renders the line changes between two documents with `ToHtml(before, after)`. It
  is pure and needs no `await`, and its result is written with `<%== %>`. Every source line is
  escaped, so unlike Markdown it is safe for a visitor's text, a fetched body or page state: the
  input is data, not markup. The result is a `table.diff` with line numbers, change markers and a
  sideways scroll container; an unchanged pair says so, and a comparison too different to align
  says why. A page compares two revisions by reading both with `IPageCatalog.ReadRevisionAsync`
  (DEC-218, DEC-219).

A page's log entries are filed under the single `uk.osric.asp.Pages` category with the page's key on
a `Path` scope, and `SimpleContext` is where that enters the shell, so `Core` never names a logging
type. [Logging](logging.md#scopes) has the category rule and why it cannot name the path.

Two rules keep the response safe:

- The body is buffered. Status, content type and headers are applied after `HandleAsync` completes,
  so a page that throws sends the error page rather than half a document.
- Headers are bounded by an allow-list in `SimpleContext`: `Content-Language` and any `X-` name.
  `Set-Cookie`, `Location`, `Content-Type`, the framing headers and the caching headers
  (`Cache-Control`, `ETag`, `Expires`, `Vary`) are refused, so a page cannot clobber the editor's
  authentication, antiforgery or session cookie, or claim a cache policy the host overrides.
  Redirects go through `Redirect`; content type through `SetContentType`, and the script permission
  through `AllowScripts` — the `Content-Security-Policy` header itself stays shell-owned. The
  extension is only a default: a key's last segment selects the initial content type through
  `Pages:ContentTypes` (DEC-144), which ships `.html`, `.js`, `.mjs`, `.css`, `.json`, `.svg` and
  `.txt` (DEC-197), and `SetContentType` always wins. Each row also carries a `ScriptPolicy`
  (`none` or `self`); it follows the extension, not the changed response type, and the shell writes
  it before the page runs. A page with code may widen `none` with `AllowScripts`, but never narrows
  `self`. The shipped `.html` row is `none`.

An unversioned page URL is `Cache-Control: no-store` with no validator, whoever asked: a page with
code can keep per-visitor state, so its body has no business in a shared cache and no business
inviting a conditional request. `PageEndpoint` writes the header after the page's own and removes `ETag`, so a
page cannot weaken it, and `If-None-Match` is ignored for an unversioned request. A page redirect is
`no-store` too. The host still sets `Content-Length` from the encoded body for a response that may
carry one, and `HEAD` retains the representation length.

## Assets and versioned URLs

A page with no code is never compiled. Generation reads the tokens it already produced, and a
template made only of text has nothing to compile: the revision is stored with no image, its
verdict is `TemplateHasCode = false`, and every request writes the stored source out byte for byte.
Such a page has no diagnostics to show in the editor, because nothing is compiled.

A no-code page is a complete representation: it is never wrapped, so its stored source is the
response even for an HTML key. It gets no banner, no skip link, no base sheet and no title
requirement; if it wants chrome, it adds a code block and becomes a fragment.

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
the host. `PageDocument.Head` takes an optional resolver for its base sheets and icon, and the
shell's own views resolve their sheets and scripts the same way.

Pages run for `GET`, `POST` and `HEAD`; any other verb is answered 405 by routing. A request body
over 1 MiB is refused with 413 before it is read. A page that outruns its execution budget is answered
`504` with the same document every other failure uses — see [error responses](shell.md#error-responses)
and [the execution budget](#page-execution-budget) for the deadline itself. Keys the site's own routes
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
with no assembly), `Compiled with warnings` and `Compiled`. The `/edit` listing shows it in a State
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
- `System.Threading.Tasks.Task` is admitted by *type*, and ten of its members are denied by name —
  `Run`, `Factory`, `ContinueWith`, `Wait`, `WaitAll`, `WaitAny`, `Result`, `GetAwaiter`, `Delay`
  and `Yield` — because each starts work the render deadline cannot stop or turns an `await` back
  into a blocked thread. The namespace stays denied, so `TaskCompletionSource`, `TaskScheduler`,
  `Parallel` and `ValueTask` are `PAGE001` without being named (DEC-124).
- A narrow set of `System.Text.Json` types is allowed, so a fetched JSON body can be parsed:
  `JsonDocument`, `JsonElement`, `JsonSerializer`, `JsonSerializerOptions`,
  `JsonSerializerDefaults`, `JsonValueKind` and `JsonException`. The
  `System.Text.Json.Serialization` attributes are not, and `System.Net` stays denied — `IHttpFetch`
  is the page's only egress (DEC-130).
- Cryptography is admitted by type: `System.Text.Encoding` to turn text into bytes, `SHA256`,
  `SHA384` and `SHA512` to hash, `HMACSHA256`, `HMACSHA384` and `HMACSHA512` to sign,
  `RandomNumberGenerator` for a nonce and `CryptographicOperations.FixedTimeEquals` to compare a
  signature. Nothing else in `System.Security.Cryptography` is, so a page cannot name `MD5`, an
  asymmetric primitive or a certificate (DEC-257).
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
  in milliseconds, and the configuration is clamped at `PageBudget.MinimumTimeoutSeconds` so a zero
  cannot switch the limit off. When it expires the site answers `504`, calls
  `SimpleContext.Abandon` so the still-running page cannot write any more, and gives up on it. A
  render that never returns cannot be reclaimed: it is counted, logged with the page's path and the
  elapsed time, and recorded on `page.render.abandoned`. A page that is *waiting* on an `await`
  returns its thread to the pool while it waits, so abandonment costs a task rather than a thread;
  when the deadline fires its next `await` throws and it unwinds. A page that blocks synchronously is
  unchanged, and is escaped only by abandonment and the container's restart. A request that spends the budget before the page
  starts — waiting on storage, or waiting for a compile slot — answers `504` the same way, but
  nothing is abandoned because nothing ran.
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
  instead of growing the process.

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

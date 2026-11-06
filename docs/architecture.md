<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Architecture

## Functional core, imperative shell

The boundary is pure rules over values, with effects isolated in their own project where the compiler
can enforce it.

- **`src/uk.osric.asp.Sdk`** is the page contract: `IContext`, `PageRequest`, `PageResponse`,
  `IHandler` and `UserFacingException`. It is what a compiled page binds against and the only
  product assembly a page may name. It has no references at all, so nothing it exposes can drag the
  host in.
- **`src/uk.osric.asp.Core`** holds the host's pure half: the template tokenizer and source
  generator, the page diagnostics, the API policy, page identity and the chart helpers. It has no
  project or package references at all, so it cannot reach for I/O, a clock, DI or ASP.NET. Its only
  dependency is the base class library.
- **`src/uk.osric.asp.Runtime`** holds the imperative half of page execution: `PageCompiler` reads
  platform assembly paths, generates assembly names, emits to a stream, and loads and invokes the
  compiled handler in a collectible `AssemblyLoadContext`. It also applies the page API policy
  (`PageApiChecker`) before emitting, and hands each image a `PageLoadContext` that refuses the
  shell's assemblies. It references `Core`, `Sdk` and Roslyn, and nothing else — in particular no
  ASP.NET, storage or logging types, because it is not built on the Web SDK.
- **`src/uk.osric.asp.Web`** owns HTTP endpoints, hosting, configuration, `SimpleContext`, and source
  storage. EF Core, SQLite, database migrations and `IPageStore` belong here.

The dependency direction is one-way: `Web` → `Runtime` → `Core`, `Web` → `Sdk`, and
`Runtime` → `Sdk`. Nothing points back up. `PageCompiler` returns a `CompileResult` of an
optional image and `PageDiagnostic` values rather than Roslyn `EmitResult`/`Diagnostic` values, so
the shell never sees the compiler's types.

Nothing the author types is an exception either. A template that does not parse comes back from
`TemplateSourceGenerator` as a `TemplateGenerationResult` with no source and a `TEMPLATE`-coded
`PageDiagnostic`, so it is stored beside the source exactly like a compile error: the editor shows
it, the page answers "Handler failed to compile.", and it is not recompiled on every request.

A template is text, `<% %>` code blocks, `<%! %>` declaration blocks, and expression blocks:
`<%= expression %>` writes a string through `ctx.Write` (HTML-encoded), while
`<%== expression %>` uses `ctx.WriteRaw` (unencoded). Expressions must be strings; convert other
values explicitly, such as with `.ToString()`. The generator emits the corresponding call, trimming
expression edges and removing at most one trailing semicolon. Declarations are hoisted above
`GeneratedPage.Handle` in template order, so a page's `using` directives and types are visible to
its whole body. Declarations are compile-time only: they write no output and are never conditional,
even inside an `if` in the template. Template order is also the using order, so a `using` written
after a type is `CS1529` on the line that wrote it.

Diagnostics live in template space: `PageDiagnostic.Position` is a 1-based `TemplatePosition` in the
page the author typed, or `null` when there is no position. The generator wraps each code block in
`#line` directives that name the template line and pads the first line to the template column.
Expression code is mapped separately from its generated `ctx.Write(...)` or `ctx.WriteRaw(...)`
wrapper, so expression errors point to the expression and wrapper errors map to the opener's line.
For a wrapper diagnostic, Roslyn's column identifies the generated method name (for example,
`Write`), not the opener itself. The closing braces are mapped to one line past the template, which
turns an unclosed block into a positioned author error. A Roslyn error with no mapped position, and
no mapped error explaining it, is a fault in generated code: `PageCompiler` throws instead of
returning a diagnostic, `PageEditor` preserves the source and rethrows, and the host answers `500`.
Warnings are author-facing only when they are mapped; a warning never stops a page compiling.

Author-facing codes are handed out once and never reused for a different meaning, because the editor
and the logs quote them:

| Code | Raised by | Means |
| --- | --- | --- |
| `TEMPLATE001` | `TemplateTokenizer` | a statement, escaped-write, raw-write or declaration opener has no `%>` |
| `TEMPLATE002` | `TemplateSourceGenerator` | a `<%=` or `<%==` expression is empty |
| `PAGE001` | `PageApiPolicy` | the page used an API it may not use |
| `PAGE002` | `PageApiChecker` | page code escaped its method, or a declaration block declared something other than a type |
| `PAGE003` | `PageImageBuilder` | the page waited too long for a compile slot |
| `PAGE004` | `PageImageBuilder` | the page took too long to compile |

## Where does code go?

| Kind of code | Home |
| --- | --- |
| Business rules, calculations, parsing, formatting, validation | `Core`, as pure functions |
| Values passed between layers | `Core`, as `record`/`readonly record struct` |
| Emitting a page assembly, loading it and invoking the handler | `Runtime` |
| Deciding when to compile, store and run a page | `Web`, in `PageImageBuilder`, `PageRenderer` and `PageEditor` |
| HTTP binding, status codes, responses, hosting, `SimpleContext` | `Web` |
| I/O, time, randomness, configuration | `Runtime` or `Web`, turned into plain values before calling the core |
| Source storage: the EF Core context and its migrations | `Web`, under `Storage` |
| A table's EF entity | `Web`, with the vertical that owns it (`Users`, `Authentication`, `Pages`, `Agent`) |
| The SQLite store behind a vertical's interface — `SqlitePageStore`, `SqliteUserStore`, `SqliteAgentConversationStore` | `Web`, beside the interface it implements |
| Local accounts, password hashing and the `users` command | `Web`, under `Users` |
| Cookie authentication, the `Editor` policy, antiforgery and login throttling | `Web`, under `Authentication` |
| The page contract: `IContext`, `PageRequest`, `PageResponse`, `IHandler`, `UserFacingException` | `Sdk`, as plain values a compiled page binds against |
| The page policy: `PageApiPolicy`, `ApiReference` | `Core.Compilation`, as a pure rule over strings |
| The Roslyn walk that applies the page policy | `Runtime` |
| The template language and its diagnostics | `Core.Compilation` |
| The `/status` charts | `Core.Charting` |
| Text whose case a key ignores — the page key and an account name | `Core.Text`, as `LowercaseText` |

When something in the shell needs a decision from a pure rule, pass the data in and return a value;
do not hand that rule `IPageStore`, a database context, a logger, or `HttpContext`. The runtime passes
`IContext` to a compiled handler; that is the deliberate seam between compiled page code and the
host, and `SimpleContext` is its only implementation.

## Visibility

Nothing is `public` unless another product assembly needs it. `Sdk` exposes the page contract and
nothing else; `Core` exposes the diagnostics and the pure helpers `Runtime` and `Web` call; `Runtime`
exposes the compiler seam `Web` uses; everything else is `internal`. Every type in `Web` is
`internal`, because it is the application assembly and its consumers are its own endpoints and the
host.

Test projects are the one deliberate exception: they reach internals through `InternalsVisibleTo`
in the project file rather than by widening a type. A member that only a test uses stays `internal`
too. `Program` is `internal` for the same reason; `WebApplicationFactory<Program>` only needs the
type's assembly, and the integration tests see it through `InternalsVisibleTo`.

EF Core generated migrations come back `public`: change the new `partial class ... : Migration` to
`internal` to keep the rule. The designer and snapshot halves are already `internal` by default, and
the EF tools discover internal migrations and an internal `StorageDbContext` without trouble.

## Page contract

A page is a runtime, not a renderer: it reads the request and shapes the response, but every value it
touches is a plain record in `Sdk`. `IContext` is the whole surface — `Request` (method, path, query,
form, headers, cookies), `Path`, `Aborted`, `Log`/`LogWarning`/`LogError`, `Write`/`WriteRaw`,
`SetStatus`, `SetContentType`, `SetHeader` and `Redirect`.

A page's log entries are filed under `uk.osric.asp.Pages.<path>`, the category `SimpleContext`
derives from the page's key, so one page can be filtered or levelled without touching another. That
format is operator-facing — filters, alerts and log queries are written against it. `SimpleContext`
is where a page's logging enters the shell, so `Core` never names a logging type.

Two rules keep the response safe:

- The body is buffered. Status, content type and headers are applied after `Handle` returns, so a
  page that throws sends the error page rather than half a document.
- Headers are bounded by an allow-list in `SimpleContext`: `Cache-Control`, `Content-Language`,
  `ETag`, `Expires`, `Vary` and any `X-` name. `Set-Cookie`, `Location`, `Content-Type` and the
  framing headers are refused, so a page cannot clobber the editor's authentication, antiforgery or
  session cookie. Redirects go through `Redirect`; content type through `SetContentType`.

Successful page responses receive a strong `ETag` computed from the UTF-8 bytes of the rendered
body. The host also sets `Content-Length` from those encoded bytes for responses that may carry a
body; `HEAD` retains the representation length, while `304` has no body or length. `GET` and `HEAD`
honor `If-None-Match` with a bodyless `304`; the page still runs to produce the representation, so
this is HTTP revalidation rather than an execution cache. The host writes the validator after page
headers, so a page cannot preserve a stale one after an edit.

Pages run for `GET`, `POST` and `HEAD`; any other verb is answered 405 by routing. A request body
over 1 MiB is refused with 413 before it is read. A page that outruns its execution budget is answered
`504` with no body — see "Page execution budget" below. Keys the site's own routes answer for are listed
in `Pages:ReservedPaths`, refused by the editor and never served.

## The document shell

Everything the site itself serves — the editor, the listing, sign-in, status, the error branches and
the confirmations — is one document shape, written by `Html/ShellDocument.cs`. `BeginAsync` sets the
content type, the status and `Cache-Control: no-store`, writes the head (the title, `/site.css`, the
view's own sheet where it has one, then `/layout.css`, then the icon), writes the skip link and opens
`<main id="main">`; `EndAsync` closes the landmark and the document. A view writes its own body
content and nothing else, so the title, the landmark, the skip link and the sheet order cannot drift
apart from one view to the next.

- The skip link is the first thing in the document and targets `#main`, which is the only `main` on
  the page — one landmark per document, and the one thing a keyboard reader can jump to.
- `/layout.css` is a seeded page and is linked **last** (DEC-109). Seeded pages are never overwritten,
  so a new sheet is how a rule the markup needs reaches an installation that has been running since
  before it existed, and being last it also beats a stale copy of `/editor.css` on that installation.
  `site.css` owns look and `/layout.css` owns layout.
- `PageLayout` keeps the fragments: `Gutter`, `Diagnostics` (a `ul` with `role="list"`, DEC-108) and
  `Nav`. `Html/` keeps `NavLink`, `DiagnosticLine`, `AntiforgeryField` and `PageUrls` beside it, so a
  named nav, an antiforgery field and an editor URL are each built once rather than concatenated at
  every endpoint.
- A page written by the owner gets none of this: it owns its whole response, and giving it a layout
  would be a product change rather than a semantics fix.

Every one of these views is rendered once by `ShellViews` and checked against the shared accessibility
rules — see [testing](testing.md#the-four-suites-and-the-planned-fifth) — with what a string cannot
prove left to a recorded hand pass (DEC-110).

## AI page agent

The optional `/assistant` shell is disabled by default. When enabled it is an authenticated owner
feature, separate from stored pages: the views are host code under the reserved `/assistant` prefix,
and `/assistant.js` plus `/assistant.css` are seeded, protected assets. Every conversation operation
is filtered by the signed-in owner.

A submitted message and its queued run are committed together. A hosted worker owns the run after the
request returns; the browser is only a viewer and can disconnect without cancelling work. SQLite is
the transcript and lifecycle record. A bounded in-memory journal carries live deltas and reconnect
replay; on restart, active runs become interrupted, unresolved approvals fail closed, and dangling
tool calls receive synthetic results rather than being replayed to the provider.

The provider boundary is an explicitly configured OpenAI-compatible streaming chat-completions
endpoint. Enabling the feature sends the system prompt, conversation transcript, and page source read
by tools to that provider. No provider SDK is used. Agent tools have access only to the page store
through `PageEditor`: list/read/search, bounded template-source line edits with an atomic revision-hash
check, whole-page writes, tombstone deletes, and a read-only virtual shell. There is no process,
filesystem, git, package, or outbound-network tool. The per-conversation approval mode defaults to
`AlwaysApprove`; `RequireApproval` waits for an individual owner decision on each flagged mutation.
Unresolved, stale, stopped, or restarted approvals never execute.

The feature bounds run duration, request duration, concurrency, queue depth, steps, tool calls,
arguments, page size, results, context and event replay. Provider content and reasoning are never
logged; reasoning is display-only and is omitted from subsequent provider requests. Agent metrics use
only low-cardinality model, tool, outcome, and token-kind tags.

## Authentication and the editor

The editor and the `/admin` route group are behind a sign-in; pages stay public. `/admin/restore`
shows the bundled source for an asset and, on confirmation, saves it through the ordinary revision
path. It can restore a deleted seeded page without discarding its earlier revisions.

- Accounts live in the `Users` table in the same SQLite database. There is no registration endpoint
  and no account UI: the `users` command in the Web app creates, lists, resets and removes accounts
  through the same `StorageDbContext` and migrations as the site.
- Passwords are PBKDF2-HMAC-SHA256 hashes from `PasswordHasher<LocalUser>`; the plaintext exists only
  at the CLI prompt and in the login request. The policy is a 12-character minimum and a 1024
  maximum, with no composition rules.
- The session is a cookie from `Microsoft.AspNetCore.Authentication.Cookies`. It slides for 30 days
  of inactivity, and the login form's **Keep me signed in** checkbox makes it persistent across
  browser restarts.
- The cookie carries a random 128-bit key, not the identity: the framework's own data-protected
  ticket lives in the `AuthTickets` table in the same SQLite database, so the request header stays
  small and serialisation stays the framework's. A sign-in always mints a new key; only a sliding
  renewal keeps the one the browser has. Sign-out deletes the row, and an hourly sweep drops tickets
  that expired with no request to remove them.
- Long-lived cookies are revocable: `users passwd` rotates the account's `SecurityStamp`, and
  `OnValidatePrincipal` rejects any cookie whose stamp no longer matches the row. Removing the
  account has the same effect on the next request. Signing in again does **not** rotate the stamp,
  so several browsers or devices can hold a valid session at the same time; only a password change
  or account removal revokes them all.
- `/edit/**` and `/status` require the `Editor` policy, which is just an authenticated account.
  `/login`, `/logout`, `/heath` and `/metrics` are the other routes outside the page catch-all, and
  every one of them is a reserved page key.
- Sign-in is throttled by a fixed window of ten attempts a minute (`LoginRateLimitOptions`), and a
  request over the limit is answered `429` before any password is hashed. The window is one global
  partition on purpose: behind Caddy every request arrives from the loopback, so a per-address
  partition would be one bucket with more moving parts.
- Login, logout and editor POSTs validate an antiforgery token. A rejected request is answered
  `400` before it reads the form or touches storage. The editor's save message lives in the session,
  not in a cookie, and a successful save answers `303` so the browser follows with a `GET`.
- The data-protection key ring is persisted on a volume. That is what lets the 30-day cookie and
  in-flight antiforgery tokens survive a redeploy.
- Every response carries one set of security headers, written by `SecurityHeaders` when the response
  starts (DEC-93). The editor view is the only one that widens the policy to `script-src 'self'`, for
  the `/editor.js` module that keeps its line gutter true; every other response forbids scripts
  outright. `/editor.css` and `/editor.js` are seeded pages like `/site.css`, so they stay editable.

## Page API policy

Stored pages are C#, so the surface they may touch is constrained twice.

- `PageApiPolicy` in `Core` is the default-deny allow-list: the SDK namespace, a short list of base
  class library types, and any `System.*` type whose name ends in `Exception`, because catching one
  should not need an entry of its own. Each entry is checked against the symbol the page actually
  resolved. It is data plus a pure function, so the list is readable and the diagnostic names the
  API to add. `System.Object` is deliberately absent: allowing it would allow `GetType()`.
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
- `Pages:Render:TimeoutSeconds` is the whole budget. When it expires the site answers `504`, calls
  `SimpleContext.Abandon` so the still-running page cannot write any more, and gives up on the thread.
  The abandoned thread is a known loss: it is counted, logged with the page's path and the elapsed
  time, and recorded on `page.render.abandoned`. A request that spends the budget before the page
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

## Source storage

The Web layer owns EF Core SQLite source storage and applies migrations asynchronously during
startup, before serving requests. `ConnectionStrings:Storage` defaults to
`Data Source=data/storage.db`; deployment overrides it to `Data Source=/data/storage.db` on a durable
rootless Podman named volume. Database access and migration lifecycle stay in the shell.

This is template storage: `EditorEndpoint` writes a page's template, `PageEndpoint` reads it by route
path and runs its compiled image. Pure rules still receive the values loaded by the shell rather
than being given `IPageStore`.

`PageRevisions` holds every save as a separate row, including its source, revision hash, creation
runtime id and optional compilation result. A save inserts the revision and advances its
`PageHeads` row in one transaction. The head is the authoritative current-page pointer; reads do not
guess the current row from timestamps. The source identity and hash stay fixed while conditional
compilation updates may fill or refresh only the compilation fields of that revision. Reusing an
image requires both a matching source fingerprint and the current runtime id, so matching images
are reused and stale ones are recompiled lazily.

Revision hashes address retained code but are not authorization tokens or rendered-body ETags. An
indexed hash lookup exists for future immutable asset serving; it is an internal storage operation,
not a public endpoint, and the asset endpoint is not implemented. Ordinary page reads follow the
head and ignore deleted pages.

Deletion marks a head as a tombstone without removing its revision pointer or history. Saving again
at the same path appends a revision and clears the tombstone. Startup seeding checks whether a head
exists, so a deleted seeded asset stays deleted across restarts. Explicit scrub removes the head and
all revisions for one path, but only after deletion; live pages must be deleted first. Scrub then
performs SQLite secure-delete and WAL checkpoint cleanup where applicable. This is best-effort
cleanup, not forensic erasure: it cannot revoke running code or erase browser/CDN copies, exported
archives, backups or filesystem snapshots. Rotate exposed secrets and clean external copies
separately. Runtime-change cleanup of obsolete images and symbols is future work; source and
revision identity are retained unless explicitly scrubbed.

Built-in page templates live in `src/uk.osric.asp.Web/Pages/Seeds`, included as content in build and
publish output. Each file is a complete page template, including its response content type and cache
headers; its filename becomes the page path. `/site.css` holds the site's look, `/layout.css` the
rules the document shell's markup needs (see "The document shell"), while `/status.css` holds
dashboard styles and is linked only by `/status`. Editing a seed file changes the initial content for
a never-created page, not an existing database page. The authenticated restore action applies the
bundled source to an existing or tombstoned page as a new revision.

## Compiled pages

Compiling is the expensive part of serving a page, so it happens when a page is saved rather than
when it is viewed. `EditorEndpoint` hands the template to `PageEditor`, which compiles it through
`PageImageBuilder` and stores the emitted assembly beside the template in the same row; `PageEndpoint`
then reads the image and runs it through `PageRenderer`. Four derived columns carry the result: `TemplateHash`, `RuntimeId`, `Image` and `Diagnostics`.

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

Compilation happens before the database write, so the lazy path stores its result only while the
page still holds the content it compiled, and re-reads the page when a save landed in between. A
page that keeps changing is served from the last compile rather than failing.

`IPageStore` owns both halves: `GetPageAsync`, `SavePageAsync` and the conditional
`TrySetCompilationAsync`. `Runtime` knows only how to turn generated source into bytes and bytes into
a loaded handler (`IPageCompiler`, `LoadedPage`); it never sees a page.

## Instrumentation

`PageMetrics` owns a `Meter` named `uk.osric.asp` and records:

| Instrument | Type | Unit | Tags |
| --- | --- | --- | --- |
| `page.render.duration` | histogram | s | `page.path` (`(not-found)` for a miss), `outcome` (`ok`, `compile-error`, `error`, `not-found`, `timeout`), `cache` (`hit`, `miss`, `none`) |
| `page.render.wait` | histogram | s | — time spent waiting for a render slot; site-wide, not per page |
| `page.render.abandoned` | counter | — | `page.path` — renders given up on at the deadline |
| `page.compile.duration` | histogram | s | `page.path`, `outcome`, `reason` (`save`, `lazy`) |
| `page.handler.load.duration` | histogram | s | `page.path` |
| `page.handler.invoke.duration` | histogram | s | `page.path`, `outcome` |
| `page.save.duration` | histogram | s | `page.path`, `outcome` |
| `page.image.size` | histogram | By | `page.path` |
| `page.storage.payload.size` | observable gauge | By | — logical UTF-8 bytes for every revision text column plus image/symbol blobs; excludes indexes and SQLite overhead |
| `page.storage.database.size` | observable gauge | By | — whole database `page_count * page_size`, including indexes and freelist pages; excludes the separate WAL |
| `page.storage.volume.capacity` | observable gauge | By | — total capacity of the filesystem volume containing the database |
| `page.cache.lookups` | counter | — | `result` (`hit`, `stale`, `absent`) |
| `page.cache.staleness` | counter | — | `page.path`, `reason` (`never-compiled`, `template-changed`, `runtime-changed`) |

A miss is tagged `page.path="(not-found)"`. The requested path is unauthenticated and unbounded, so
tagging it verbatim would let a scan mint one time series per URL; the real path is in the access log.
The wait histogram carries no path tag for the same reason, and because queueing is a property of the
site rather than of a page.

Durations are seconds to match OpenTelemetry and ASP.NET Core's own hosting instruments, so do not
add a request-duration metric of our own. On a cache miss `page.compile.duration` is a subset of
`page.render.duration`; never add the two together. `page.path` is bounded by the number of stored
pages, which is small — if that ever stops being true, drop the tag and keep the path in logs.
Recording costs nothing until an exporter listens, so the instruments are always on. For a quick
look while the site runs: `dotnet-counters monitor --counters uk.osric.asp <pid>`.

The bundled SQLite build does not provide `dbstat`, so exact allocated bytes for an individual table
and its indexes/overflow pages are not available without adding a dependency. The payload gauge is
explicitly logical size, not allocated table size. `PageStorageSampler` samples at startup and about
every 60 seconds, and a scrub requests a coalesced refresh. It uses a short-lived database scope,
bounded SQLite commands and cancellation; observable gauge callbacks return the last cached sample
without database or filesystem I/O. Failed samples are logged and retain the prior values. Compare
logical payload with volume capacity only as an operational estimate; use the whole-database gauge
for database growth. Review retention manually when payload exceeds half the volume capacity; no
automatic pruning is performed.

### Export and the status page

One exporter listens: OpenTelemetry's Prometheus exporter, registered for metrics only — no tracer
provider, no logger provider, and no instrumentation packages, because the framework's own meters are
already there (`Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.Server.Kestrel`,
`System.Runtime`).

- **Naming is the contract.** `page.render.duration` becomes `page_render_duration_seconds_bucket`, and
  the dashboard hard-codes that. Renaming an instrument or changing its unit empties a panel. Tag
  names are sanitised the same way — the tag `page.path` is the label `page_path` — and PromQL will
  not parse the dotted form, so queries and legends use the underscore.
- **The buckets are ours.** Every duration shares one explicit scale whose top boundary is past the
  render budget, so a render recorded at its deadline lands in a real bucket rather than `+Inf`;
  `page.image.size` has its own byte scale. The SDK's default starts at 0–5 s for a seconds
  instrument, which would make every render one column and every percentile meaningless.
- **Two gates on `/metrics`.** The app publishes on the loopback only (DEC-61), and Caddy answers
  `404` for the path on the public origin. Prometheus scrapes `app:8080` over the shared rootless
  network; see [deployment](../deploy/README.md#metrics-and-the-status-page).
- **`/status` is a shell endpoint.** It queries Prometheus over that same network and draws SVG from
  pure `Core` functions (`LineChart`, `Heatmap`, `NiceTicks`); it is owner-only, cached for fifteen
  seconds, and a Prometheus failure is a banner rather than a `500`, because the page is what gets
  opened when something is broken. The window is chosen by a `duration` token — `1h`, `3h`, `8h`,
  `1d`, `7d` or `30d`; the token is matched against that list, never parsed as a length of time, and
  anything unrecognised is the default hour. The nav shows every token, with the current one as text
  rather than a link to itself. A token sets three things: how far back the queries reach, the chart
  width (wider for a longer window) and the sample step, which widens in whole minutes so a thirty-day
  view asks for a few hundred points rather than the 43,200 a minute step would, because Prometheus
  refuses a range query over 11,000 points. Each window is cached separately, so switching back does
  not re-query. Stored data stays in seconds, but a duration axis whose whole
  range is below a second is drawn in milliseconds — `0.00025` is a worse label than `0.25`.
  The heatmap carries a legend from zero to the busiest cell and gives every cell its own value in a
  `<title>`, so a shade can be read without guessing; and "Pages slower than 0.5 s" attributes the
  slow rows to a page, which is the question the heatmap provokes. Units appear twice on purpose: in
  the title of every panel and on the vertical axis, and the heatmap's legend names the cell unit
  (`renders/s`) because its cells are rates, not counts.
- **The image carries tzdata.** Only the axis labels are local; the positions, queries and samples are
  UTC. The base is `aspnet:10.0-alpine-extra` (DEC-82) because plain alpine has no
  `/usr/share/zoneinfo` and resolving `Europe/London` inside it throws.

`make test-web` runs the Web storage tests, and `make test-integration` runs the same storage through
the real host; both are included in `make test`. Schema changes are scaffolded with
`make migration NAME=...` using the pinned local EF tool into the Web project's
`Storage/Migrations` directory. Back up the database before deploying startup migrations; see
[deployment storage and backup guidance](../deploy/README.md#storage-and-migrations).

## Shapes

- `record` (or `readonly record struct` for small values) over classes.
- `struct` for small immutable values where allocation or identity genuinely matters; measure before
  reaching for one.
- `static` classes of pure functions when there is no state to carry.
- `class` only when the framework demands it (controllers) or the type has identity.
- Prefer composition over inheritance; there is no inheritance in the core today and little need for it.

## Simplicity first

`n` is small. Prefer the obvious loop, the linear scan, and the in-memory dictionary over clever data
structures or caches. Optimise only what a measurement shows to be slow, and optimise it then.

Reach for `System.*` and `Microsoft.*` first. Write the remaining code ourselves rather than adding a
package: a dependency is a long-term liability, and most of what we need is short.

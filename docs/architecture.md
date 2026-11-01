<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Architecture

## Functional core, imperative shell

The boundary is pure rules over values, with effects isolated in their own project where the compiler
can enforce it.

- **`src/uk.osric.asp.Core`** holds the pure half: the template tokenizer, the source generator, and
  the `IContext`/`IHandler` contracts. It has no project or package references at all, so it cannot
  reach for I/O, a clock, DI or ASP.NET. Its only dependency is the base class library.
- **`src/uk.osric.asp.Runtime`** holds the imperative half of template execution: `TemplateCompiler`
  reads platform assembly paths, generates assembly names, emits to a stream, and loads and invokes
  the compiled handler in a collectible `AssemblyLoadContext`. It also applies the page API policy
  (`PageApiChecker`) before emitting, and hands each image a `PageLoadContext` that refuses the
  shell's assemblies. It references `Core` and Roslyn, and nothing else — in particular no ASP.NET,
  storage or logging types, because it is not built on the Web SDK.
- **`src/uk.osric.asp.Web`** owns HTTP endpoints, hosting, configuration, `SimpleContext`, and source
  storage. EF Core, SQLite, database migrations and `IStorage` belong here.

The dependency direction is one-way: `Web` → `Runtime` → `Core`, and `Web` → `Core`. Nothing points
back up. `TemplateCompiler` returns a `CompileResult` of an optional image and
`PageDiagnostic` values rather than Roslyn `EmitResult`/`Diagnostic` values, so the shell never sees
the compiler's types.

Nothing the author types is an exception either. A template that does not parse comes back from
`TemplateSourceGenerator` as a `TemplateParseResult` with no source and a `TEMPLATE`-coded
`PageDiagnostic`, so `PageService` stores it beside the source exactly like a compile error: the
editor shows it, the page answers "Handler failed to compile.", and it is not recompiled on every
request.

Diagnostics live in template space: `PageDiagnostic.Position` is a 1-based `TemplatePosition` in the
page the author typed, or `null` when there is no position. The generator wraps each code block in
`#line` directives that name the template line and pads the first line to the template column, so
Roslyn reports the author's position rather than a generated one. The closing braces are mapped to
one line past the template, which turns an unclosed block into a positioned author error. A Roslyn
error with no mapped position, and no mapped error explaining it, is a fault in generated code:
`TemplateCompiler` throws instead of returning a diagnostic, `PageService` preserves the source and
rethrows, and the host answers `500`. Warnings are author-facing only when they are mapped; a
warning never stops a page compiling.

Author-facing codes are handed out once and never reused for a different meaning, because the editor
and the logs quote them:

| Code | Raised by | Means |
| --- | --- | --- |
| `TEMPLATE001` | `TemplateTokenizer` | a `<%` has no matching `%>` |
| `PAGE001` | `PageApiPolicy` | the page used an API it may not use |
| `PAGE002` | `PageApiChecker` | page code escaped its method or declared types or members |
| `PAGE003` | `PageService` | the page waited too long for a compile slot |
| `PAGE004` | `PageService` | the page took too long to compile |

## Where does code go?

| Kind of code | Home |
| --- | --- |
| Business rules, calculations, parsing, formatting, validation | `Core`, as pure functions |
| Values passed between layers | `Core`, as `record`/`readonly record struct` |
| Compiling templates, loading assemblies, invoking handlers | `Runtime` |
| HTTP binding, status codes, responses, hosting, `SimpleContext` | `Web` |
| I/O, time, randomness, configuration | `Runtime` or `Web`, turned into plain values before calling the core |
| Source storage, `IStorage`, EF Core context and migrations | `Web`, under `Storage` for implementation |
| Local accounts, password hashing and the `users` command | `Web`, under `Users` |
| Cookie authentication, the `Editor` policy, antiforgery and login throttling | `Web`, under `Authentication` |
| The page contract: `IContext`, `PageRequest`, `PageResponse`, `PageApiPolicy` | `Core`, as plain values and pure rules |
| The Roslyn walk that applies the page policy | `Runtime` |

When something in the shell needs a decision from a pure rule, pass the data in and return a value;
do not hand that rule `IStorage`, a database context, a logger, or `HttpContext`. The runtime passes
`IContext` to a compiled handler; that is the deliberate seam between compiled page code and the
host, and `SimpleContext` is its only implementation.

## Visibility

Nothing is `public` unless another product assembly needs it. `Core` exposes the page contract, the
diagnostics and the pure helpers `Runtime` and `Web` call; `Runtime` exposes the compiler seam `Web`
uses; everything else in both is `internal`. Every type in `Web` is `internal`, because it is the
application assembly and its consumers are its own endpoints and the host.

Test projects are the one deliberate exception: they reach internals through `InternalsVisibleTo`
in the project file rather than by widening a type. A member that only a test uses stays `internal`
too. `Program` is `internal` for the same reason; `WebApplicationFactory<Program>` only needs the
type's assembly, and the integration tests see it through `InternalsVisibleTo`.

EF Core generated migrations come back `public`: change the new `partial class ... : Migration` to
`internal` to keep the rule. The designer and snapshot halves are already `internal` by default, and
the EF tools discover internal migrations and an internal `StorageDbContext` without trouble.

## Page contract

A page is a runtime, not a renderer: it reads the request and shapes the response, but every value it
touches is a plain record in `Core`. `IContext` is the whole surface — `Request` (method, path, query,
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

Pages run for `GET`, `POST` and `HEAD`; any other verb is answered 405 by routing. A request body
over 1 MiB is refused with 413 before it is read. A page that outruns its execution budget is answered
`504` with no body — see "Page execution budget" below. Keys the site's own routes answer for are listed
in `Pages:ReservedPaths`, refused by the editor and never served.

## Authentication and the editor

The editor is the only part of the site that writes, so it is the only part behind a sign-in. Pages
stay public.

- Accounts live in the `Users` table in the same SQLite database. There is no registration endpoint
  and no account UI: the `users` command in the Web app creates, lists, resets and removes accounts
  through the same `StorageDbContext` and migrations as the site.
- Passwords are PBKDF2-HMAC-SHA256 hashes from `PasswordHasher<LocalUser>`; the plaintext exists only
  at the CLI prompt and in the login request. The policy is a 12-character minimum and a 1024
  maximum, with no composition rules.
- The session is a cookie from `Microsoft.AspNetCore.Authentication.Cookies`. It slides for 30 days
  of inactivity, and the login form's **Keep me signed in** checkbox makes it persistent across
  browser restarts.
- Long-lived cookies are revocable: `users passwd` rotates the account's `SecurityStamp`, and
  `OnValidatePrincipal` rejects any cookie whose stamp no longer matches the row. Removing the
  account has the same effect on the next request. Signing in again does **not** rotate the stamp,
  so several browsers or devices can hold a valid session at the same time; only a password change
  or account removal revokes them all.
- `/edit/**` requires the `Editor` policy, which is just an authenticated account. `/login`,
  `/logout` and `/heath` are the only other routes outside the page catch-all, and each one is a
  reserved page key.
- Sign-in is throttled by a fixed window of ten attempts a minute (`LoginRateLimitOptions`), and a
  request over the limit is answered `429` before any password is hashed. The window is one global
  partition on purpose: behind Caddy every request arrives from the loopback, so a per-address
  partition would be one bucket with more moving parts.
- Login, logout and editor POSTs validate an antiforgery token. A rejected request is answered
  `400` before it reads the form or touches storage. The editor's save message lives in the session,
  not in a cookie, and a successful save answers `303` so the browser follows with a `GET`.
- The data-protection key ring is persisted on a volume. That is what lets the 30-day cookie and
  in-flight antiforgery tokens survive a redeploy.

## Page API policy

Stored pages are C#, so the surface they may touch is constrained twice.

- `PageApiPolicy` in `Core` is the default-deny allow-list: the SDK namespace, a short list of base
  class library types, and any `System.*` type whose name ends in `Exception`, because catching one
  should not need an entry of its own. Each entry is checked against the symbol the page actually
  resolved. It is data plus a pure function, so the list is readable and the diagnostic names the
  API to add. `System.Object` is deliberately absent: allowing it would allow `GetType()`.
- `PageApiChecker` in `Runtime` walks the generated syntax tree, projects Roslyn symbols onto
  `ApiReference` values, rejects `stackalloc`, `dynamic`, pointers, function pointers and `unsafe`,
  and enforces the shape rule: one type, `GeneratedPage`, with one method, `Handle`. That rule is
  what stops a page closing the method to declare statics, its own entry point or a module
  initializer.

The compiler's reference set is trimmed to the base class library: `uk.osric.asp.*` and `Microsoft.*`
are not referenced, so naming the shell or Roslyn fails with CS02xx before the policy runs. The trim
cannot remove `System.IO` or `System.Environment`, which live in `System.Private.CoreLib`; that is the
policy's job. A post-emit scan of the image's type references was considered and dropped, because it
could only find what the semantic walk already reports, at the price of false rejections for
compiler-emitted helpers.

The policy is versioned, because it is applied when a page compiles. `RuntimeId` is
`{FormatVersion}:{PolicyVersion}:{CoreMvid}`, so tightening the list invalidates every stored image
and each page is rechecked on its next request.

## Load isolation

`CompiledTemplate` loads each image into a `PageLoadContext`, a collectible `AssemblyLoadContext`
that refuses `uk.osric.asp.Web`, `uk.osric.asp.Runtime`, `Microsoft.AspNetCore*`,
`Microsoft.EntityFrameworkCore*` and `Microsoft.Data.Sqlite`, and lets everything else resolve
through the default context. It throws rather than returning `null`, because `null` means "ask the
default context" — the exact escape the type exists to close.

This is isolation from the site's internals, not a sandbox: a page still runs in-process with the
site's identity and the whole base class library. The process boundary is [SEC-03](jobs/SEC-03.md),
and it is deliberately not built — see "Page execution budget" below for what stands in its place.

## Page execution budget

A page is arbitrary C#, and .NET cannot interrupt arbitrary C#: `Thread.Abort` does not exist. So the
budget bounds the *response*, not the page. `PageService.RenderAsync` runs the handler on a worker and
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
(`--memory=768m`, `--pids-limit=256`, `--cpus=1`), so a page that allocates without bound takes the
container and not the host.

`StartLimitBurst=3` in 60 s still applies: a page that wedges on every request restarts the container
three times and then leaves the unit failed until it is reset and the page is fixed.

## Source storage

The Web layer owns EF Core SQLite source storage and applies migrations asynchronously during
startup, before serving requests. `ConnectionStrings:Storage` defaults to
`Data Source=data/storage.db`; deployment overrides it to `Data Source=/data/storage.db` on a durable
rootless Podman named volume. Database access and migration lifecycle stay in the shell.

This is source storage: `EditorEndpoint` writes a page's source, `MainEndpoint` reads it by route
path and runs its compiled image. Pure rules still receive the values loaded by the shell rather
than being given `IStorage`.

## Compiled pages

Compiling is the expensive part of serving a page, so it happens when a page is saved rather than
when it is viewed. `EditorEndpoint` hands the source to `PageService`, which compiles it and stores
the emitted assembly beside the source in the same row; `MainEndpoint` then reads the image and runs
it. Four derived columns carry the result: `ContentHash`, `RuntimeId`, `Image` and `Diagnostics`.

A stored image is trusted only when both keys match:

- `ContentHash` is the SHA-256 of the source (`ContentFingerprint` in `Core`), so an edit that
  bypasses the editor cannot be answered with stale code.
- `RuntimeId` is the compile format version, the page policy version and the module version id of
  `uk.osric.asp.Core`, so a build with a different page contract — or a tightened policy — never runs
  an image it did not produce. Debug and Release builds differ, as they should.

A page whose image is missing or stale — a row written before images existed, a direct database
write, or every row after an upgrade that touched `Core` — is compiled on its next request and
stored. That is the only compile outside a save: there is no startup warm-up, so the first visitor
after a deploy pays one compile per page. A failure is stored too, so a broken page serves its
diagnostics without recompiling, and the editor shows them after the save instead of losing the
edit. `Diagnostics` is a JSON array of positioned, severity-tagged `PageDiagnostic` values; a
warning-only page stores its image and reports "Saved with warnings." A compiler fault stores only
the source, so the author's edit survives and the next request retries.

Compilation happens before the database write, so the lazy path stores its result only while the
page still holds the content it compiled, and re-reads the page when a save landed in between. A
page that keeps changing is served from the last compile rather than failing.

`IStorage` owns both halves: `GetPageAsync`, `SavePageAsync` and the conditional
`TrySetCompilationAsync`. `Runtime` knows only how to turn generated source into bytes and bytes into
a loaded handler (`ITemplateCompiler`, `CompiledTemplate`); it never sees a page.

## Instrumentation

`PageMetrics` owns a `Meter` named `uk.osric.asp` and records:

| Instrument | Type | Unit | Tags |
| --- | --- | --- | --- |
| `page.render.duration` | histogram | s | `page.path`, `outcome` (`ok`, `compile-error`, `error`, `not-found`, `timeout`), `cache` (`hit`, `miss`, `none`) |
| `page.render.wait` | histogram | s | `page.path` — time spent waiting for a render slot |
| `page.render.abandoned` | counter | — | `page.path` — renders given up on at the deadline |
| `page.compile.duration` | histogram | s | `page.path`, `outcome`, `reason` (`save`, `lazy`) |
| `page.handler.load.duration` | histogram | s | `page.path` |
| `page.handler.invoke.duration` | histogram | s | `page.path`, `outcome` |
| `page.save.duration` | histogram | s | `page.path`, `outcome` |
| `page.image.size` | histogram | By | `page.path` |
| `page.cache.lookups` | counter | — | `result` (`hit`, `stale`, `absent`) |
| `page.cache.staleness` | counter | — | `page.path`, `reason` (`never-compiled`, `content-changed`, `runtime-changed`) |

Durations are seconds to match OpenTelemetry and ASP.NET Core's own hosting instruments, so do not
add a request-duration metric of our own. On a cache miss `page.compile.duration` is a subset of
`page.render.duration`; never add the two together. `page.path` is bounded by the number of stored
pages, which is small — if that ever stops being true, drop the tag and keep the path in logs.
Recording costs nothing until an exporter listens, so the instruments are always on. For a quick
look while the site runs: `dotnet-counters monitor --counters uk.osric.asp <pid>`.

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

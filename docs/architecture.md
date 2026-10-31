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
back up. `TemplateCompiler` returns a `CompileResult` of booleans and strings rather than Roslyn
`EmitResult`/`Diagnostic` values, so the shell never sees the compiler's types.

## Where does code go?

| Kind of code | Home |
| --- | --- |
| Business rules, calculations, parsing, formatting, validation | `Core`, as pure functions |
| Values passed between layers | `Core`, as `record`/`readonly record struct` |
| Compiling templates, loading assemblies, invoking handlers | `Runtime` |
| HTTP binding, status codes, responses, hosting, `SimpleContext` | `Web` |
| I/O, time, randomness, configuration | `Runtime` or `Web`, turned into plain values before calling the core |
| Source storage, `IStorage`, EF Core context and migrations | `Web`, under `Storage` for implementation |
| The page contract: `IContext`, `PageRequest`, `PageResponse`, `PageApiPolicy` | `Core`, as plain values and pure rules |
| The Roslyn walk that applies the page policy | `Runtime` |

When something in the shell needs a decision from a pure rule, pass the data in and return a value;
do not hand that rule `IStorage`, a database context, a logger, or `HttpContext`. The runtime passes
`IContext` to a compiled handler; that is the deliberate seam between compiled page code and the
host, and `SimpleContext` is its only implementation.

## Page contract

A page is a runtime, not a renderer: it reads the request and shapes the response, but every value it
touches is a plain record in `Core`. `IContext` is the whole surface — `Request` (method, path, query,
form, headers, cookies), `Path`, `Aborted`, `Log`/`LogWarning`/`LogError`, `Write`/`WriteRaw`,
`SetStatus`, `SetContentType`, `SetHeader` and `Redirect`.

A page's log entries are filed under `uk.osric.asp.Pages.<path>`, the category `SimpleContext`
derives from the page's key, so one page can be filtered or levelled without touching another. That
format is operator-facing — filters, alerts and log queries are written against it. `SimpleContext`
is the only type that touches `ILogger`, so `Core` never names a logging type.

Two rules keep the response safe:

- The body is buffered. Status, content type and headers are applied after `Handle` returns, so a
  page that throws sends the error page rather than half a document.
- Headers are bounded by an allow-list in `SimpleContext`: `Cache-Control`, `Content-Language`,
  `ETag`, `Expires`, `Vary` and any `X-` name. `Set-Cookie`, `Location`, `Content-Type` and the
  framing headers are refused, so a page cannot clobber the editor's flash cookie or the future auth
  cookie. Redirects go through `Redirect`; content type through `SetContentType`.

Pages run for `GET`, `POST` and `HEAD`; any other verb is answered 405 by routing. A request body
over 1 MiB is refused with 413 before it is read. Keys the site's own routes answer for are listed
in `Pages:ReservedPaths`, refused by the editor and never served.

## Page API policy

Stored pages are C#, so the surface they may touch is constrained twice.

- `PageApiPolicy` in `Core` is the default-deny allow-list: the SDK namespace plus a short list of
  base class library types, checked against the symbol the page actually resolved. It is data plus a
  pure function, so the list is readable and the diagnostic names the API to add. `System.Object` is
  deliberately absent: allowing it would allow `GetType()`.
- `PageApiChecker` in `Runtime` walks the generated syntax tree, projects Roslyn symbols onto
  `ApiReference` values, rejects `stackalloc`, `dynamic`, pointers and `unsafe`, and enforces the
  shape rule — one type, `GeneratedPage`, with one method, `Handle`. That rule is what stops a page
  closing the method to declare statics, its own entry point or a module initializer.

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
site's identity and the whole base class library. Execution budget and a process boundary are
[SEC-03](issues/SEC-03.md).

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
edit.

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
| `page.render.duration` | histogram | s | `page.path`, `outcome` (`ok`, `compile-error`, `error`, `not-found`), `cache` (`hit`, `miss`, `none`) |
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

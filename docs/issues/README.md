<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Issues

One file per distinct open problem. Each file has a short ID (`SEC-01`, `BUG-05`, ...) to use in
commits, branches and discussion. Severity is the reviewer's assessment of risk, not an
implementation order; the suggested order is at the bottom.

Raised by the architecture review and the code-health scan of 2026-10-30.

| ID | Severity | Problem |
| --- | --- | --- |
| [SEC-01](SEC-01.md) | Critical | The editor is unauthenticated, so anyone can execute arbitrary code |
| [SEC-02](SEC-02.md) | High | Editor POST has no antiforgery protection and its flash cookie is unhardened |
| [SEC-03](SEC-03.md) | Medium | Page code has no execution budget and no process boundary |
| [ARCH-03](ARCH-03.md) | High | No execution timeout, cancellation or concurrency limit for page code |
| [ARCH-06](ARCH-06.md) | Medium | There is no way to list pages |
| [ARCH-07](ARCH-07.md) | Medium | Page content has no history, so an edit cannot be undone |
| [BUG-05](BUG-05.md) | Medium | Compile errors point at generated source line numbers, not the template |
| [BUG-06](BUG-06.md) | Medium | An unterminated code block produces a generic 500 |
| [BUG-07](BUG-07.md) | Low | Concurrent saves race and can fail with a primary-key violation |
| [BUG-08](BUG-08.md) | Low | Missing `TRUSTED_PLATFORM_ASSEMBLIES` silently yields an empty reference set |
| [BUG-12](BUG-12.md) | Low | Unused Roslyn Workspaces packages ship in the web app |
| [OPS-02](OPS-02.md) | Low | There is no health endpoint for the container |
| [OPS-03](OPS-03.md) | Medium | There is no CI pipeline |
| [OPS-04](OPS-04.md) | Low | EF Core logs every SQL command at Information in production |
| [STYLE-03](STYLE-03.md) | Low | Two top-level types share `TemplateTokenizer.cs`; `Token` is a class record |

## Suggested order

1. SEC-01, SEC-02 — close the trust boundary before anything else. If authentication is added,
   data-protection keys must be persisted (see `deploy/README.md`).
2. ARCH-03, SEC-03 — give page code a budget, then a boundary if it ever needs one.
3. ARCH-06, ARCH-07 — a page index and revision history.
4. OPS-03, BUG-12, STYLE-03, STYLE-04 — hygiene, so the next review starts from an accurate
   baseline.

## Closed

Resolved since the review; the files were deleted and their history is in git.

| ID | Fix |
| --- | --- |
| ARCH-02 | Pages are a restricted runtime: `IContext` carries a plain `PageRequest` snapshot and asks for `SetStatus`/`SetContentType`/`SetHeader`/`Redirect`, all applied after the buffered handler returns. `SimpleContext` is the only implementation, headers are allow-listed so a page cannot clobber the site's cookies, pages run only for `GET`/`POST`/`HEAD`, and a body over 1 MiB is refused. Documented in `docs/architecture.md` |
| ARCH-04 | `CompiledTemplate.Invoke` unwraps the reflection wrapper with `ExceptionDispatchInfo`, so the shell sees the page's own exception and no longer knows about `TargetInvocationException`. The `NoInlining` attributes were removed with a note on what they would have been protecting |
| ARCH-05 | `PagePath` normalises every key in `Core` (lowercase, NFC, no dot segments, no trailing slash), the shell redirects to the canonical URL with 308 and stores only canonical keys, and `Pages:ReservedPaths` keeps page keys off the site's own routes. Storage tests use canonical keys |
| PERF-01 | Pages are compiled when they are saved, and the emitted assembly is stored beside the source in the same row. `ContentHash` (SHA-256 of the source) and `RuntimeId` (format version plus `Core`'s module version id) decide whether a stored image may be used; a page with no usable image is compiled on its next request and stored, and a stored compile failure is served without recompiling. Roslyn's metadata references are built once per process, `ITemplateCompiler` makes compilation injectable, and `PageMetrics` records compile, render, load, invoke, save, image-size and cache instruments on the `uk.osric.asp` meter. `Web.IntegrationTests` register a recording compiler and assert that a second request does not compile |
| ARCH-08 | `test/uk.osric.asp.Web.IntegrationTests` drives the real host through `WebApplicationFactory<Program>` against a throwaway database: routing, middleware, the error branch, static files, startup migrations and the editor round-trip; `make test-integration` is part of `make test`. The 401/403 cases wait for SEC-01 |
| OPS-01 | `UseForwardedHeaders` trusts `X-Forwarded-Proto`/`X-Forwarded-Host` from any peer, which the loopback-only publish makes safe; HSTS now emits and the scheme is honest. Covered by `ForwardedHeadersTests` |
| ARCH-09 | The `Specs` suite only restated `TemplateCompiler` unit tests, so it was deleted with Reqnroll; the compiler stays covered by `Runtime.Tests` |
| BUG-01 | `UseExceptionHandler` now uses an inline branch, so a throwing `POST` returns 500 |
| BUG-02 | `ErrorEndpoint` sets 500 itself; `/error` is no longer a public route (direct request is a 404) |
| BUG-03 | The 404 response sets `text/plain; charset=utf-8` |
| BUG-04 | Compilation diagnostics are HTML-encoded and wrapped in `<pre>` |
| BUG-09 | A page's log entries are filed under `uk.osric.asp.Pages.<path>`, the category `SimpleContext` creates from the page's key, so a page can be filtered or levelled on its own. The message template no longer prefixes the path, `MainEndpoint` hands over `ILoggerFactory`, and `IContext` gained `LogWarning`/`LogError` |
| BUG-10 | `make format-check` passes |
| BUG-11 | Stale `*.feature.cs` deleted; Reqnroll 3 generates in memory |
| STYLE-01 | `EditorEndpoint` validates through a `SaveAsync` helper instead of `goto` |
| STYLE-02 | `IStorage.ListFilesAsync` returns `IReadOnlyList<string>` |
| DOC-01..05 | Instructions, architecture, deployment, testing and `.editorconfig` match the code |
| STYLE-04 | Exception tests assert with `await Assert.ThrowsAsync`/`Assert.CatchAsync` instead of `Assert.That(async ...)`; the convention is recorded in `docs/testing.md` |

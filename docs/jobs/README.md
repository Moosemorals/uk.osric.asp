<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Jobs

One file per distinct job — something the site gets wrong (`SEC-`, `BUG-`, `OPS-`, `ARCH-`) and
something it could usefully become (`IDEA-`). Each file has a short ID (`SEC-03`, `IDEA-004`, ...)
to use in commits, branches and discussion.

Two measures, and neither is a plan. **Severity** is the reviewer's assessment of risk, and is blank
for an idea because an idea is not something that has gone wrong. **Size** is how much work it looks
like: `S` a sitting, `M` a day or so, `L` big enough to want a plan of its own. The size is the
useful column, because a good few of these are an hour's work and the order at the bottom spends its
first effort there.

The defect and operation jobs were raised by the architecture review and the code-health scan of
2026-10-30; BUG-13 came out of the BUG-06 fix, ARCH-11 came out of the BUG-05/BUG-13 planning, and
BUG-14 came out of the metrics dashboard planning. The `IDEA-` jobs were added on 2026-11-01.

| ID | Severity | Size | Job |
| --- | --- | --- | --- |
| [SEC-03](SEC-03.md) | Medium | L | Page code has no process boundary |
| [ARCH-06](ARCH-06.md) | Medium | S | There is no way to list pages |
| [ARCH-07](ARCH-07.md) | Medium | L | Page content has no history, so an edit cannot be undone |
| [ARCH-10](ARCH-10.md) | Medium | S | A page cannot be deleted |
| [ARCH-11](ARCH-11.md) | Low | S | The editor cannot show the generated C# for a page |
| [ARCH-12](ARCH-12.md) | Medium | M | A template has no shorthand for writing a value |
| [ARCH-13](ARCH-13.md) | Low | M | A page cannot stream its response |
| [BUG-07](BUG-07.md) | Low | S | Concurrent saves race and can fail with a primary-key violation |
| [BUG-08](BUG-08.md) | Low | S | Missing `TRUSTED_PLATFORM_ASSEMBLIES` silently yields an empty reference set |
| [OPS-02](OPS-02.md) | Low | M | The health endpoint exists; it has no monitor or alerting behind it |
| [OPS-03](OPS-03.md) | Medium | M | There is no CI pipeline |
| [OPS-05](OPS-05.md) | Medium | M | No outbound notifications when the site breaks |
| [IDEA-001](IDEA-001.md) | — | L | The editor is a plain textarea |
| [IDEA-002](IDEA-002.md) | — | M | A page cannot call an outbound HTTP API |
| [IDEA-003](IDEA-003.md) | — | M | A page has nowhere to keep state |
| [IDEA-004](IDEA-004.md) | — | L | `Core` is both the page SDK and the host's compiler support |
| [IDEA-005](IDEA-005.md) | — | L | A page cannot declare a type, so shared code has to be copied |
| [IDEA-006](IDEA-006.md) | — | S | `site.css` is a static file rather than a page |
| [IDEA-007](IDEA-007.md) | — | S | The site has no favicon |
| [IDEA-008](IDEA-008.md) | — | L | Writing pages by hand is the only way to write pages |

An `IDEA-` file says `## Idea` and `## Sketch` where a defect says `## Problem` and
`## Fix outline`; both carry the same table under the title, with `Size` in place of `Severity`.

## A rough order

Rough on purpose: the point is the first few, and the rest will move as they are worked. Small and
self-contained comes first, because these are the ones that never get scheduled otherwise.

1. **The small defects, a sitting each.** BUG-07 (a primary-key race on save) and BUG-08 (an empty
   reference set that fails like a compile error). No design needed, both fit in an afternoon, and
   both bite the moment the site has a second visitor.
2. **IDEA-007, then IDEA-006** — the icon and then the stylesheet as pages. Both small, and together
   they answer the question the product work depends on: can a page serve CSS and an image as
   happily as HTML? Do them back to back; the second tells you what caching needs fixing.
3. **The editor trio: ARCH-10, ARCH-11, ARCH-06.** Delete a page, show the generated C#, list what
   exists. Each is small, and together they turn `/edit` from "type the URL you already know" into
   something usable — and ARCH-06 makes the site's contents discoverable for the first time.
4. **OPS-03, CI** — before the tree grows, so `make format-check`, `make test` and the licence check
   run without being asked.
5. **ARCH-12** — the write shorthand. A small language change, a large effect on how a page reads.
6. **IDEA-002, fetch** — the biggest single improvement to what a page can do. Allow-listed, bounded
   and testable; take it before persistence, which has more open questions and less immediate payoff.
7. **IDEA-003, persistence**, then **IDEA-004, the split.** Persistence is useful sooner; the split
   is the unlock for the three large ideas, so it follows immediately.
8. **ARCH-07, history** — undo for edits, once there is enough content to be worth versioning.
9. **IDEA-005, page types** — with IDEA-004 in place, the shared-code question has a real answer.
10. **IDEA-001, a proper editor** — by now there are pages to justify it, and IDEA-008 needs it.
11. **IDEA-008, drafting** — last of the ideas, because every one of the others makes it better.
12. **ARCH-13, streaming** — when a page actually needs to answer before it has finished.
13. **OPS-02 and OPS-05** — monitoring and alerting, once there is something worth being woken for.
14. **SEC-03** — the process boundary, only if page authors ever stop being the owner.

## Closed

Resolved jobs; the files were deleted and their history is in git.

| ID | Fix |
| --- | --- |
| ARCH-03 | Renders have a deadline, a slot count and a body cap, compiles have their own slot and deadline, an abandoned render is logged and counted, and `/heath` reports it so podman restarts a wedged container. See [the plan](../plans/page-execution-budget.md). SEC-03 (the process boundary) is deliberately still open |
| ARCH-02 | Pages are a restricted runtime: `IContext` carries a plain `PageRequest` snapshot and asks for `SetStatus`/`SetContentType`/`SetHeader`/`Redirect`, all applied after the buffered handler returns. `SimpleContext` is the only implementation, headers are allow-listed so a page cannot clobber the site's cookies, pages run only for `GET`/`POST`/`HEAD`, and a body over 1 MiB is refused. Documented in `docs/architecture.md` |
| ARCH-04 | `CompiledTemplate.Invoke` unwraps the reflection wrapper with `ExceptionDispatchInfo`, so the shell sees the page's own exception and no longer knows about `TargetInvocationException`. The `NoInlining` attributes were removed with a note on what they would have been protecting |
| ARCH-05 | `PagePath` normalises every key in `Core` (lowercase, NFC, no dot segments, no trailing slash), the shell redirects to the canonical URL with 308 and stores only canonical keys, and `Pages:ReservedPaths` keeps page keys off the site's own routes. Storage tests use canonical keys |
| PERF-01 | Pages are compiled when they are saved, and the emitted assembly is stored beside the source in the same row. `ContentHash` (SHA-256 of the source) and `RuntimeId` (format version plus `Core`'s module version id) decide whether a stored image may be used; a page with no usable image is compiled on its next request and stored, and a stored compile failure is served without recompiling. Roslyn's metadata references are built once per process, `ITemplateCompiler` makes compilation injectable, and `PageMetrics` records compile, render, load, invoke, save, image-size and cache instruments on the `uk.osric.asp` meter. `Web.IntegrationTests` register a recording compiler and assert that a second request does not compile |
| ARCH-08 | `test/uk.osric.asp.Web.IntegrationTests` drives the real host through `WebApplicationFactory<Program>` against a throwaway database: routing, middleware, the error branch, static files, startup migrations and the editor round-trip; `make test-integration` is part of `make test`. The authentication tests sign in through the real `/login` form and cover the 401/403 paths |
| OPS-01 | `UseForwardedHeaders` trusts `X-Forwarded-Proto`/`X-Forwarded-Host` from any peer, which the loopback-only publish makes safe; HSTS now emits and the scheme is honest. Covered by `ForwardedHeadersTests` |
| ARCH-09 | The `Specs` suite only restated `TemplateCompiler` unit tests, so it was deleted with Reqnroll; the compiler stays covered by `Runtime.Tests` |
| BUG-01 | `UseExceptionHandler` now uses an inline branch, so a throwing `POST` returns 500 |
| BUG-02 | `ErrorEndpoint` sets 500 itself; `/error` is no longer a public route (direct request is a 404) |
| BUG-03 | The 404 response sets `text/plain; charset=utf-8` |
| BUG-04 | Compilation diagnostics are HTML-encoded and wrapped in `<pre>` |
| BUG-06 | A template that does not parse is a value, not an exception: `TemplateTokenizer.Tokenize` returns `TokenizeResult` and `TemplateSourceGenerator.GenerateTemplateSource` returns `TemplateParseResult`, whose `TEMPLATE001` message names the line and column of the unclosed `<%`. `PageService` stores it as a compile failure, so the editor shows it inline and the page answers "Handler failed to compile." instead of the generic error page |
| BUG-09 | A page's log entries are filed under `uk.osric.asp.Pages.<path>`, the category `SimpleContext` creates from the page's key, so a page can be filtered or levelled on its own. The message template no longer prefixes the path, `MainEndpoint` hands over `ILoggerFactory`, and `IContext` gained `LogWarning`/`LogError` |
| BUG-10 | `make format-check` passes |
| BUG-11 | Stale `*.feature.cs` deleted; Reqnroll 3 generates in memory |
| STYLE-01 | `EditorEndpoint` validates through a `SaveAsync` helper instead of `goto` |
| STYLE-02 | `IStorage.ListFilesAsync` returns `IReadOnlyList<string>`. The query was later removed as unused when listing pages became ARCH-06 |
| DOC-01..05 | Instructions, architecture, deployment, testing and `.editorconfig` match the code |
| SEC-01 | The editor requires a local account. Accounts live in the `Users` table and are created only by the `users` command; `POST`/`GET /edit/**` require the `Editor` policy. Passwords are PBKDF2 hashes, the cookie slides for 30 days and is persistent, and a security stamp revokes it on a password change or account removal. See [the plan](../plans/editor-authentication.md) |
| SEC-02 | Login, logout and editor POSTs validate an antiforgery token and answer `400` before reading the form or storing anything. The editor's flash message moved from a cookie to server-side session state, and saves answer `303` |
| STYLE-04 | Exception tests assert with `await Assert.ThrowsAsync`/`Assert.CatchAsync` instead of `Assert.That(async ...)`; the convention is recorded in `docs/testing.md` |
| BUG-05 | Compile diagnostics carry a `TemplatePosition` mapped from `#line` directives, so the editor shows the line and column the author typed. Generated-code errors with no mapped author error are compiler faults: they throw instead of becoming page diagnostics, and the host answers `500`. See [the plan](../plans/compiler-diagnostics.md) |
| BUG-13 | `PageDiagnostic` carries severity, `CompileResult.Success` is derived from errors, and warning-only pages store an image, render, and report "Saved with warnings." Warnings are stored, carried through `PageCompilation` and styled separately in the editor |
| STYLE-03 | `Token` and `TokenType` moved to their own files; `Token` is now a `readonly record struct` carrying a `TemplatePosition` |
| BUG-12 | `Web` no longer references the Roslyn Workspaces packages, and the publish output loses `Microsoft.CodeAnalysis.Workspaces*` plus the MSBuild `BuildHost-*` folders. EF Core Design still pulls them in transitively, where their exact `Microsoft.CodeAnalysis.Common` dependency pins that package to 5.0.0 and conflicts with the 5.9.0 `Runtime` compiles against, so `Directory.Packages.props` enables central transitive pinning and pins both Workspaces packages to 5.9.0 |
| OPS-04 | `appsettings.json` raises `Microsoft.EntityFrameworkCore` to `Warning` while `Default` stays `Information`, so EF no longer logs every statement to the journal; `Microsoft.AspNetCore` stays at `Warning` and an explicit `uk.osric.asp` at `Information` keeps the app's own lines and the startup lines. `appsettings.Development.json` puts `Microsoft.EntityFrameworkCore.Database.Command` back at `Information`, so `make run` still shows SQL locally |
| BUG-14 | A redirect keeps the query string. `Redirects` encodes each part of a target with the type that understands it — appending a query to a path before `PathString` sees it turns the `?` into `%3F`, which is a 404 rather than a redirect with parameters — so a canonical redirect carries the request's query (`/Hello?q=a%20b` → `308 /hello?q=a%20b`) and a page's `Redirect` target keeps its query and fragment. The same helper serves the login form's `returnUrl`, which is why `/login?returnUrl=%2Fedit%2Fhello%3Ftab%3Dsource` returns to `/edit/hello?tab=source`. Covered in `MainEndpointTests`, `EditorEndpointTests`, `LoginEndpointTests` and `ShellTests` |

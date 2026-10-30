<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Issues

One file per distinct problem found in the architecture review of 2026-10-30. Each file has a
short ID (`SEC-01`, `BUG-03`, ...) to use in commits, branches and discussion. Severity is the
reviewer's assessment of risk, not an implementation order; the suggested order is at the bottom.

| ID | Severity | Problem |
| --- | --- | --- |
| [SEC-01](SEC-01.md) | Critical | The editor is unauthenticated, so anyone can execute arbitrary code |
| [SEC-02](SEC-02.md) | High | Editor POST has no antiforgery protection and its flash cookie is unhardened |
| [ARCH-01](ARCH-01.md) | High | `Core` is not the functional core: it owns Roslyn, I/O and assembly loading |
| [ARCH-02](ARCH-02.md) | High | `IContext` is too narrow to be the page runtime's public API |
| [ARCH-03](ARCH-03.md) | High | No execution timeout, cancellation or concurrency limit for page code |
| [ARCH-04](ARCH-04.md) | Medium | Reflection wrapper leaks `TargetInvocationException` into the shell |
| [ARCH-05](ARCH-05.md) | Medium | Storage keys are raw route values with no canonical path rule |
| [ARCH-06](ARCH-06.md) | Medium | There is no way to list pages; `ListFilesAsync` is dead code |
| [ARCH-07](ARCH-07.md) | Medium | Page content has no history, so an edit cannot be undone |
| [ARCH-08](ARCH-08.md) | High | The shell seam (routing, middleware, error handling) has no integration tests |
| [ARCH-09](ARCH-09.md) | Medium | The specification suite describes the compiler, not business behaviour |
| [PERF-01](PERF-01.md) | High | Every request compiles the page from source; nothing is cached |
| [BUG-01](BUG-01.md) | High | An exception during a POST returns 404 "File not found" |
| [BUG-02](BUG-02.md) | Medium | `GET /error` returns HTTP 200 |
| [BUG-03](BUG-03.md) | Low | The 404 response has no `Content-Type` |
| [BUG-04](BUG-04.md) | Low | Compilation diagnostics are written raw and unencoded |
| [BUG-05](BUG-05.md) | Medium | Compile errors point at generated source line numbers, not the template |
| [BUG-06](BUG-06.md) | Medium | An unterminated code block produces a generic 500 |
| [BUG-07](BUG-07.md) | Low | Concurrent saves race and can fail with a primary-key violation |
| [BUG-08](BUG-08.md) | Low | Missing `TRUSTED_PLATFORM_ASSEMBLIES` silently yields an empty reference set |
| [BUG-09](BUG-09.md) | Low | Page log entries are categorised as `MainEndpoint`, not the page |
| [BUG-10](BUG-10.md) | Low | `make format-check` fails on the current tree |
| [BUG-11](BUG-11.md) | Low | Stale `*.feature.cs` files sit in the source tree |
| [BUG-12](BUG-12.md) | Low | Unused Roslyn Workspaces packages ship in the web app |
| [OPS-01](OPS-01.md) | Medium | No forwarded-headers support, so HSTS is a no-op behind Caddy |
| [OPS-02](OPS-02.md) | Low | There is no health endpoint for the container |
| [OPS-03](OPS-03.md) | Medium | There is no CI pipeline |
| [OPS-04](OPS-04.md) | Low | EF Core logs every SQL command at Information in production |
| [DOC-01](DOC-01.md) | Medium | `.github/copilot-instructions.md` still describes MVC, Razor and a reference-free core |
| [DOC-02](DOC-02.md) | Medium | `docs/architecture.md` says storage and the editor are not wired up |
| [DOC-03](DOC-03.md) | Medium | `deploy/README.md` repeats the stale wiring claim and the MVC trimming rationale |
| [DOC-04](DOC-04.md) | Low | `docs/testing.md` omits the web test project |
| [DOC-05](DOC-05.md) | Low | `.editorconfig` still has a Razor section |
| [STYLE-01](STYLE-01.md) | Low | `EditorEndpoint` uses `goto` and repeats validation |
| [STYLE-02](STYLE-02.md) | Low | `IStorage` exposes `IEnumerable<string>` instead of `IReadOnlyList<string>` |
| [STYLE-03](STYLE-03.md) | Low | Two top-level types share `TemplateTokenizer.cs`; `Token` is a class record |
| [STYLE-04](STYLE-04.md) | Low | Several tests use `void` methods around `Assert.That(async ...)` |

## Suggested order

1. SEC-01, SEC-02 — close the trust boundary before anything else. If authentication is added,
   data-protection keys must be persisted (see `deploy/README.md`).
2. ARCH-08 with BUG-01 and BUG-02 — write integration tests first, then fix the error path they
   expose.
3. OPS-01 — make HTTPS, HSTS and absolute URLs correct behind the proxy.
4. ARCH-02 — settle the page runtime API before content depends on it.
5. ARCH-01 and PERF-01 — split the runtime out of `Core` and stop compiling per request.
6. ARCH-05, ARCH-06, ARCH-07 — canonical paths, a page index and revision history.
7. OPS-03, DOC-01..DOC-05, BUG-10..BUG-12, STYLE-01..STYLE-04 — hygiene, so the next review
   starts from an accurate baseline.

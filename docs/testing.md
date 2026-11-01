<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Testing

Work test-first. Write the failing test, make it pass, then refactor — the refactor step is where the
core stays pure and the shell stays thin.

## The four suites

| Project | Tool | Scope | Speed |
| --- | --- | --- | --- |
| `test/uk.osric.asp.Core.Tests` | NUnit | Individual rules in the functional core | Milliseconds |
| `test/uk.osric.asp.Runtime.Tests` | NUnit | The template compiler: emit, load and run | Milliseconds to seconds |
| `test/uk.osric.asp.Web.Tests` | NUnit | The endpoints and storage of the web shell | Milliseconds to seconds |
| `test/uk.osric.asp.Web.IntegrationTests` | NUnit + `WebApplicationFactory` | The composed pipeline: routing, middleware, error handling, startup | Seconds |

- **Core tests** cover one rule at a time and exercise `Core` directly.
- **Runtime tests** cover what the core cannot: Roslyn emitting a handler, loading the assembly and
  invoking it. They also cover the page API policy — one test per banned construct, plus a page of
  ordinary code that must still compile — and the load context, which is reachable from the tests
  through `InternalsVisibleTo`. They reference `Runtime`, never `Web`.
- **Web tests** cover the shell: endpoints driven through a hand-built `HttpContext` with an
  in-memory `IStorage`, a `TestSession`, a `TestAntiforgery` and the real compiler, and storage
  against a real SQLite database file. The page-cache tests swap in a counting compiler wrapped
  around the real one, so "a second request does not compile" is asserted rather than inferred.
  `PageScopeTests` lives here because it names the shell's own types to prove the trimmed reference
  set cannot resolve them.
- **Integration tests** cover what only exists when the host runs: route matching and ordering, the
  exception-handler branch, sign-in and authorisation, antiforgery rejection, status codes and
  content types set by middleware, static files falling through to the page route, startup
  migrations, DI wiring and the fact that a page is compiled once. They reference `Web` and
  `Runtime`, start the real application through `WebApplicationFactory<Program>`, and use a
  throwaway SQLite file per fixture that is deleted with it. The host runs as `Production`, so
  failures are answered by the error page rather than the developer exception page, and the client
  speaks HTTPS so the auth, antiforgery and session cookies round-trip. Every fixture seeds one
  account through `IUserStore` and signs in through the real `/login` form; there is no test
  authentication scheme. A recording compiler is registered in place of the real one so a test can
  count compilations.

Each project references only the layers it exercises, so the reference graph stops a test from
reaching past its subject: `Core.Tests` has no ASP.NET or Roslyn to call, `Runtime.Tests` has no
endpoints, and the web suites use `Runtime` only through `ITemplateCompiler` and the
`TemplateCompiler` that implements it. `Web.Tests` and `Web.IntegrationTests` split on what is under
test rather than what is referenced: one proves a handler in isolation, the other proves the wiring
between handlers.

Every test is an NUnit test. Assert the behaviour, not the method, and keep one behaviour per test.

## Running them

```bash
make test              # all four suites; no browser needed
make test-core         # core tests only
make test-runtime      # runtime tests only
make test-web          # web tests only
make test-integration  # the composed pipeline only, against a throwaway database
```

## Conventions

- Tests live in the namespace matching the project and use `using` directives inside the namespace, like
  production code.
- Name tests for the behaviour, not the method.
- One behaviour per test, with arrange/act/assert separated by blank lines.
- Prefer `[TestCase]` over copy-pasted tests for input tables.
- Assert exceptions raised by async work with `await Assert.ThrowsAsync<T>(() => ...)` for an exact type
  and `await Assert.CatchAsync<T>(() => ...)` when a derived type is acceptable — NUnit 5 returns a
  `Task`, so an unawaited assertion is never evaluated. Never wrap the call in
  `Assert.That(async () => ..., Throws...)`: the await hides inside a constraint and is easy to read
  wrongly.
- Test assemblies get the repository root embedded as `RepoRoot` assembly metadata
  (`Directory.Build.targets`), so infrastructure code never relies on the working directory.

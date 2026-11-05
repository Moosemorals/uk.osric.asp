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
| `test/uk.osric.asp.Runtime.Tests` | NUnit | The page compiler: emit, load and run | Milliseconds to seconds |
| `test/uk.osric.asp.Web.Tests` | NUnit | The endpoints and storage of the web shell | Milliseconds to seconds |
| `test/uk.osric.asp.Web.IntegrationTests` | NUnit + `WebApplicationFactory` | The composed pipeline: routing, middleware, error handling, startup | Seconds |

- **Core tests** cover one rule at a time and exercise `Core` directly. `Sdk` has no suite of its
own: it is five declarations with documentation and no behaviour, and what depends on it is asserted
by the suites that use it (`SimpleContextTests`, `ErrorEndpointTests`, `PageScopeTests`,
`PageLoadContextTests`, `PageReferenceSetTests`).
- **Runtime tests** cover what the core cannot: Roslyn emitting a handler, loading the assembly and
  invoking it. They also cover the page API policy — one test per banned construct, plus a page of
  ordinary code that must still compile — the reference set the compiler is built on, and the load
  context, which is reachable from the tests through `InternalsVisibleTo`. They reference `Runtime`
  and `Sdk`, never `Web`.
- **Web tests** cover the shell: endpoints driven through a hand-built `HttpContext` with an
  in-memory `IPageStore`, a `TestSession`, a `TestAntiforgery` and the real compiler, and storage
  against a real SQLite database file. Storage tests cover legacy migration, append-only revision
  identity, concurrent saves, tombstones, scrub and SQLite cleanup. `PageStorageMetricsTests` checks
  logical payload accounting, cached observable gauges, failed-sample retention and bounded sampling.
  The page-cache tests swap in a counting compiler wrapped around the real one, so "a second request
  does not compile" is asserted rather than inferred.
  `PageScopeTests` lives here because it names the shell's own types — and `Core`'s — to prove the
  trimmed reference set cannot resolve them.
- **The assistant** is covered at the same boundaries: `Core.Tests` checks transcript trimming,
  stream accumulation, strict tool arguments, virtual-shell parsing, title generation and authored
  template-source line edits. `Web.Tests` uses migrated SQLite files for conversations, sequence
  allocation, approvals and restart repair; fake HTTP handlers for provider SSE; and scripted model
  streams for the run processor. `Web.IntegrationTests` runs the real authenticated host with a
  scripted provider to verify disabled routing, conversation streaming, approval gating and writes.
  No suite contacts a real model provider.
- **The document shell and accessibility** are asserted over rendered markup rather than in a browser.
  `ShellViews` renders all eighteen shell views once each — the sign-in form and its failure, the
  listing, the editor clean and with diagnostics, history, a saved revision, generated source, the
  delete confirmation, the restore confirmation, status, the error page, the error page with details,
  the compile failure, and the assistant list, chat, reasoning chat, and delete confirmation — and
  `AccessibilityTests` applies ten shared rules to every document: one
  `main`, one `h1`, no skipped heading level, a distinct name on every `nav`, `scope` on every `th`, a
  label on every control, no role that repeats an implicit one (bar the documented `role="list"`), no
  focusable element inside an `aria-hidden` subtree, no inline style, and a name for every image and
  every `svg role="img"`. `MarkupReader` is a hand-written reader rather than a parser dependency,
  which is sound only because this repository encodes every value at the boundary — and is why it has
  to fail loudly when it meets something it does not expect. `ShellDocumentTests` covers the document
  itself: the language, the title, the sheet order, the status, the cache header and the skip link
  before `main`. **What a string cannot show** — the accessibility tree, tab order, focus visibility,
  zoom and reflow, contrast, and how a chart is read aloud — is a written hand pass (DEC-110), with its
  findings recorded beside the plan.
- **The execution budget** is tested in `Web.Tests` (`PageRendererTests` for the render deadline, the
  render queue, the abandoned context and the body cap; `TemplateCompilerTests` for the compile queue
  and the compile deadline; `AbandonedRenderHealthCheckTests` for the health signal) and once end to end in `Web.IntegrationTests/RenderBudgetTests`, which runs the
  host on a one-second budget and asserts `504`. A slow page there burns CPU against a wall clock
  rather than sleeping, because the page API policy refuses `Thread.Sleep` and `Task.Delay`. Nothing
  asserts on the podman restart: that belongs to the supervisor, not to `WebApplicationFactory`.
- **The metrics export and the dashboard** split the same way. `Web.Tests` proves the Prometheus
  client against recorded `query_range` bodies (values as strings, `NaN`/`±Inf`, empty results, error
  payloads) and the dashboard against a stubbed `IMetricsSource`, including the fifteen-second cache on
  a test clock. `Core.Tests` pins the chart markup and the tick tables. `Web.IntegrationTests` renders
  a page and reads `/metrics`, and asserts that `/status` sends an anonymous visitor to `/login`; no
  test needs a running Prometheus.
- **Integration tests** cover what only exists when the host runs: route matching and ordering, the
  exception-handler branch, sign-in and authorisation, antiforgery rejection, status codes and
  content types set by middleware, startup-seeded page assets and their conditional responses,
  migrations, DI wiring and the fact that a page is compiled once. Editor integration coverage
  distinguishes `/edit` from the `/edit/` root editor, checks command authorization and antiforgery,
  verifies generated-source inspection does not execute a page, and confirms deleted seeded assets
  remain tombstoned across restarts. The suites reference `Web` and
  `Runtime`, start the real application through `WebApplicationFactory<Program>`, and use a
  throwaway SQLite file per fixture that is deleted with it. The host runs as `Production`, so
  failures are answered by the error page rather than the developer exception page, and the client
  speaks HTTPS so the auth, antiforgery and session cookies round-trip. Every fixture seeds one
  account through `IUserStore` and signs in through the real `/login` form; there is no test
  authentication scheme, and the only substitution on the sign-in path is the hasher:
  `AspApplication` registers `FastPasswordService` so a sign-in costs one SHA instead of 600,000
  PBKDF2 rounds. `EditorAuthenticationTests` opts back into the real hasher, because proving it is
  wired into the host is what that fixture is for. A recording compiler is registered in place of the
  real one so a test can count compilations.

Each project references only the layers it exercises, so the reference graph stops a test from
reaching past its subject: `Core.Tests` has no ASP.NET, Roslyn or `Sdk` to call, `Runtime.Tests`
has no endpoints, and the web suites use `Runtime` only through `IPageCompiler` and the
`PageCompiler` that implements it. `Web.Tests` and `Web.IntegrationTests` split on what is under
test rather than what is referenced: one proves a handler in isolation, the other proves the wiring
between handlers.

Every test is an NUnit test. Assert the behaviour, not the method, and keep one behaviour per test.

## Running them

```bash
make check             # the gate: formatting, licences, package audit, all four suites, Release publish
make ci                # the same from `make clean`, plus "the build wrote no tracked file"
make test              # all four suites; no browser needed for the assertions they make
make test-core         # core tests only
make test-runtime      # runtime tests only
make test-web          # web tests only
make test-integration  # the composed pipeline only, against a throwaway database
```

**No browser needed** covers what the suites assert, not everything the editor does. Alignment of the
gutter numbers with the text rows, the native resize handle, the caret coming back after a save and a
clean console under the policy are browser behaviour: the modules are seeded pages, and no assertion
here can prove them. They are checked by hand against [the plan](plans/editor-line-gutter.md#verification)
until the Playwright suite that plan describes gets its own job and make target.

## The gate

There is no CI service in this repository: `make check` is the gate, run on demand. `make deploy` runs
it before the image is built, and `make ci` adds a clean tree at both ends — nothing left over from an
earlier run may hide a missing input, and a build that rewrites a tracked file fails rather than being
noticed weeks later (BUG-11).

`make ci` also runs `image-check`: two fresh publishes and two uncached builds of one commit must
produce one image id, because a reproducible image is the only reason a commit tag can stand for the
build the host is running. It needs podman and the pinned base image, which is why it lives in `ci`
rather than in `check`.

Warnings are errors (`Directory.Build.props`), so a project that builds locally will pass the gate;
`make audit` fails on a vulnerable or deprecated NuGet package, and every suite runs under
`--blame-hang-timeout 5m` so a wedged run fails instead of sitting there. The timeout is generous
because the budget tests burn a wall clock deliberately.

## Parallelism

NUnit runs a suite one test at a time until it is told otherwise. Each project says what it allows in
its `TestAssembly.cs`:

- `Core.Tests` and `Runtime.Tests` run every test in parallel (`ParallelScope.All`). Core is pure
  functions over immutable values, and a Runtime test compiles into its own assembly and its own load
  context through a stateless compiler.
- `Web.Tests` and `Web.IntegrationTests` run every *fixture* in parallel (`ParallelScope.Fixtures`) and
  the tests inside a fixture one at a time. A fixture owns its services and storage — and in the
  integration suite its host and its database — so fixtures are independent, but the tests inside one
  are not: the budget tests measure a wall clock against a page that burns CPU, the storage fixtures
  migrate a database in `[SetUp]`, and a fixture signs in once per test.
- `PageMetricsTests` is `[NonParallelizable]`, because a `MeterListener` hears every instrument in the
  process, including the page meter another fixture is recording into. `MetricsEndpointTests` reads
  the same process-wide meter through the exporter, but asserts that a series is present rather than
  what it counts, so it is safe beside other fixtures.
- `PageRendererTests`, `TemplateCompilerTests` and the one budget test in `PageEndpointTests` are
  `[NonParallelizable]`, for the same reason fixtures are the unit of parallelism: a page that
  outruns its budget cannot be stopped, so it keeps a core busy while the deadline continuation
  queues for a thread. Run beside another fixture's compiles, the answer lands late and a timing
  assertion fails on load rather than behaviour.

Parallelism is worth less here than it looks. Roslyn already uses every core inside a single compile,
so fixtures that compile at the same time contend rather than scale; and `Pages:Render:TimeoutSeconds`
is a whole number, so every test of the budget costs at least a second of wall clock. That second is
what makes `Web.Tests` the slow suite — the budget tests are about half of it — and it is why
per-test parallelism is not used there. A sub-second budget is the change that would buy more.

The four projects are independent processes, so `make -j4 test` runs them at once as well: about 27 s
against 38 s on a four-core machine, even though each suite then reports a slightly longer duration.

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

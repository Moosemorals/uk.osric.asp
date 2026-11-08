<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Testing

Work test-first. Write the failing test, make it pass, then refactor — the refactor step is where the
core stays pure and the shell stays thin.

## The five suites

| Project | Tool | Scope | Speed |
| --- | --- | --- | --- |
| `test/uk.osric.asp.Core.Tests` | NUnit | Individual rules in the functional core | Milliseconds |
| `test/uk.osric.asp.Runtime.Tests` | NUnit | The page compiler: emit, load and run | Milliseconds to seconds |
| `test/uk.osric.asp.Web.Tests` | NUnit | The endpoints and storage of the web shell | Milliseconds to seconds |
| `test/uk.osric.asp.Web.IntegrationTests` | NUnit + `WebApplicationFactory` | The composed pipeline: routing, middleware, error handling, startup | Seconds |
| `test/uk.osric.asp.Web.BrowserTests` | NUnit + Playwright | The real host driven in Firefox: the shell pages and the assistant | Seconds to a minute |

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
  trimmed reference set cannot resolve them. `PageEndpointTests` proves the page authentication
  rules through the endpoint: a guarded anonymous visit is redirected with the canonical path and
  raw query, an authenticated `POST` without a valid antiforgery pair is refused before the page
  runs, and an authenticated response is `no-store` with no validator. `LoginRedirectTests` pins the
  `returnUrl` it builds, and `SimpleContextTests` the guard's status per method, the one-token-per-
  render rule and the empty field an abandoned render gets. The logging is asserted rather than
  assumed: `RequestLogScopeTests` pins the request scope — the exchange, the account and the route's
  ids — and `RunProcessorTests` proves a provider failure is logged at Warning with its exception and
  the run's scope. `Web.IntegrationTests/ErrorHandlingTests` proves the framework's own
  unhandled-exception line carries the request scope, which is what fixes the order of the middleware.
- **The assistant** is covered at the same boundaries: `Core.Tests` checks transcript trimming,
  stream accumulation, strict tool arguments, virtual-shell parsing, title generation, authored
  template-source line edits and the `BudgetSnapshot` the page and the stream both read.
  `Web.Tests` uses migrated SQLite files for conversations, sequence
  allocation, approvals and restart repair; fake HTTP handlers for provider SSE; and scripted model
  streams for the run processor. `Web.IntegrationTests` runs the real authenticated host with a
  scripted provider to verify disabled routing, conversation streaming, approval gating and writes,
  and compares the last `budget.updated` a run published with the page the reload renders. The
  browser suite watches a budget figure move while the run is still busy.
  No suite contacts a real model provider.
- **The document shell and accessibility** are asserted over rendered markup rather than in a browser.
  `ShellViews` renders all twenty-four shell views once each — the sign-in form and its failure, the
  sign-out confirmation, the listing with an edited seed and a matching one so its restore buttons
  are swept too, the editor clean and with diagnostics, history, a saved revision, the delete
  confirmation, status, the error page, the error page with details, a not-found page, the compile
  failure, the assistant list, chat, reasoning chat, delete confirmation, the export list, the
  upload form, a rejected archive, the import review, the import progress, and the stored
  administration index as the layout wraps it — and `AccessibilityTests` applies twelve shared rules to every document: one
  `main`, one `h1`, no skipped heading level, a distinct name on every `nav`, `scope` on every `th`, a
  label on every control, every `form.form` field in its `dd` with its label in a `dt` (or a
  textarea's label in a fieldset legend), no role that repeats an implicit one (bar the documented
  `role="list"`), no focusable element inside an `aria-hidden` subtree, no inline style, and a name
  for every image and every `svg role="img"`, and a skip link that is the body's first focusable
  element and targets `#main`. The layout chain is its own suite, `PageLayoutChainTests`, which
  drives `PageEndpoint` with a stored page and its layouts: the default wrap, the opt-out, the
  nesting, the depth limit, the cycle, the one slot and deadline, the shared budgets, the fallbacks
  and the response the content owns; `SiteNavigationTests` covers the menu items, and the
  integration suite's `SiteChromeTests` proves the shipped `Pages:Layout` through the real host and
  compares the shell's menu with the layout's. `MarkupReader` is a hand-written reader rather than a parser dependency,
  which is sound only because this repository encodes every value at the boundary — and is why it has
  to fail loudly when it meets something it does not expect. `PageDocumentTests` covers the document
  itself — the language, the title, the encoding, the sheet order and deduplication, the refusal of
  an off-site sheet, the skip link and the banner — and `ShellDocumentTests` covers the response
  around it: the status, the cache header, the view's sheets, and the banner before the one `main`.
  **What a string cannot show** — the accessibility tree, tab order, focus visibility, zoom and
  reflow, contrast, and how a chart is read aloud — is a written hand pass (DEC-110), with its
  findings recorded beside the plan.
- **The execution budget** is tested in `Web.Tests` (`PageRendererTests` for the render deadline, the
  render queue, the abandoned context and the body cap; `PageImageBuilderTests` for the compile queue
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
  a stored page that widens its own policy with `ctx.AllowScripts()` while a page that redirects or
  throws does not, migrations, DI wiring and the fact that a page is compiled once. Editor
  integration coverage distinguishes `/edit` from the `/edit/` root editor, checks command
  authorization and antiforgery, restores a deleted seed from the page list, and confirms deleted
  seeded assets remain tombstoned across restarts. `PageAuthenticationTests` runs the whole
  private-page round trip: a guarded page saved through the editor, the redirect to `/login` with its
  query, signing in back to that page, its form with and without `ctx.AntiforgeryField`, sign-out and
  a password change making an existing cookie anonymous, and an external `returnUrl` still falling
  back to the editor. The archive suites split the same way: `PageArchiveWriterTests` and
  `PageArchiveReaderTests` round-trip real serialized SQLite bytes and reject every hostile shape
  the reader names, `SqlitePageBatchStoreTests` proves the batch is invisible until the commit and
  rolls back whole, and `ArchiveEndpointTests` exports and re-imports through the real host. The suites reference `Web` and
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
make test              # the four suites that need no browser
make test-core         # core tests only
make test-runtime      # runtime tests only
make test-web          # web tests only
make test-integration  # the composed pipeline only, against a throwaway database
make browsers          # download the Firefox build the browser suite drives, once
make test-browser      # the real host in Firefox, with a scripted provider
make test-browser-live # the tests that call the configured provider and cost tokens
make mutation          # mutation-test Core: does the suite notice a change in the code?
```

Every suite writes its results under `artifacts/test-results`, so a run leaves the working tree alone.

**No browser needed** covers what the four suites assert, not everything the site does. Alignment of
the gutter numbers with the text rows, the native resize handle, the caret coming back after a save, a
clean console under the policy and an answer that arrives one delta at a time are browser behaviour:
the modules are seeded pages, and no assertion in those suites can prove them.

## The browser suite

`test/uk.osric.asp.Web.BrowserTests` is opt-in and out of `make check`, for the same reason
`image-check` is: it needs a downloaded browser, it takes about a minute, and a suite that can flake is
a suite people stop running. It references neither the app nor the test doubles the other suites use.
Instead it starts the built app as a child process on a loopback port, with a throwaway SQLite file and
a scripted provider, and drives the pages it serves with Playwright and Firefox — the browser the site
is read in.

- `BrowserHost` starts the app, reads the port it bound, waits for `/heath` and seeds one account
  through the real `users` command. It runs the host as `Staging`: not `Production`, so the auth and
  antiforgery cookies are accepted over plain HTTP, and not `Development`, so a failure is answered by
  the error page the public site would show.
- `StubProvider` is a local, OpenAI-compatible server that streams server-sent events on request. A
  test scripts it: words per delta, a delay between them, a tool call, a token-limit finish, or a stall
  long enough to outlive a deadline. It records the request bodies, so a test can also assert what the
  server sent.
- `BrowserTest` gives each test its own context, page and script, and fails it on any console error or
  page error the browser reported while it ran — which is how the seeded icon was found rendering as a
  black silhouette under the page content security policy.
- `PageScriptTests` saves a JavaScript page and an HTML page that calls `ctx.AllowScripts()`, then
  proves the browser loaded the script. That is the enforcement the header alone cannot show.
- Fixtures that need different assistant limits start their own `BrowserHost`; the shared one signs in
  once and every test starts from that browser state, so a suite run does not spend the sign-in
  throttle's allowance on itself.
- The `[Explicit]` fixtures talk to the configured provider instead: they need `deploy/deepseek.token`
  or `ASSISTANT_LIVE_TOKEN`, run only under `make test-browser-live`, and are the only tests here that
  can show what a model does when it thinks for a while.

## Mutation testing

`make mutation` probes the suite rather than the code: Stryker changes `Core` on purpose — a `<` to a
`<=`, a `+` to a `-`, a returned value to another — reruns `Core.Tests` and reports every change the
tests did not notice. A survivor is either a gap in the suite or an equivalent mutant, so each one is
judged from the report rather than counted as a failure.

It is opt-in and out of `make check`, like the browser suite: the project is rebuilt and the suite
rerun once per mutant, so a full run is minutes. The configuration lives beside the tests it drives
(`test/uk.osric.asp.Core.Tests/stryker-config.json`) and ignores string mutations — a test that pins
the exact text of an exception message is testing the wording, not the behaviour. Stryker runs from
the test project directory; started beside the solution file it treats the whole solution as its
scope. `dotnet-stryker` is a local tool, so a fresh checkout needs `dotnet tool restore` (the target
does it).

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
`make audit` fails on a vulnerable or deprecated NuGet package, and every suite runs under a
five-minute session timeout (`TEST_HANG_TIMEOUT`, in milliseconds) so a wedged run fails instead of
sitting there. The timeout is generous because the budget tests burn a wall clock deliberately. It is
VSTest's `RunConfiguration.TestSessionTimeout` rather than `--blame-hang`, because the blame collector
warns on every run that ends normally that it wrote no sequence file: that is the good outcome said
as a problem, and a warning nobody can act on is one everybody learns to skip.

## Parallelism

NUnit runs a suite one test at a time until it is told otherwise. Each project says what it allows in
its `TestAssembly.cs`:

- `Core.Tests` and `Runtime.Tests` run every test in parallel (`ParallelScope.All`). Core is pure
  functions over immutable values, and a Runtime test compiles into its own assembly and its own load
  context through a stateless compiler.
- `Web.Tests` and `Web.IntegrationTests` run every *fixture* in parallel (`ParallelScope.Fixtures`) and
  the tests inside a fixture one at a time. A fixture owns its services and storage — and in the
  integration suite its host and its database — so fixtures are independent, but the tests inside one
  are not: the budget tests measure a wall clock against a page that burns CPU, a storage test opens
  the database file the fixture holds the template for, and a fixture signs in once per test.
- `PageMetricsTests` is `[NonParallelizable]`, because a `MeterListener` hears every instrument in the
  process, including the page meter another fixture is recording into. `MetricsEndpointTests` reads
  the same process-wide meter through the exporter, but asserts that a series is present rather than
  what it counts, so it is safe beside other fixtures.
- `Web.BrowserTests` runs every test one at a time. Each test owns a browser page, but they share one
  host, one database and one assistant queue, and the tests that measure streaming cannot do that
  beside another test's compile.
- `PageRendererTests`, `PageImageBuilderTests` and the one budget test in `PageEndpointTests` are
  `[NonParallelizable]`, for the same reason fixtures are the unit of parallelism: a page that
  outruns its budget cannot be stopped, so it keeps a core busy while the deadline continuation
  queues for a thread. Run beside another fixture's compiles, the answer lands late and a timing
  assertion fails on load rather than behaviour.

Parallelism is worth less here than it looks: Roslyn already uses every core inside a single compile,
so fixtures that compile at the same time contend rather than scale, and the non-parallel fixtures run
one at a time beside nothing else.

What the budget tests cost is the budget itself, so they run a quarter of a second rather than the
five a visitor gets: the behaviour under test is the deadline, not its length. `TimeoutSeconds` is a
`double` for exactly that reason, and `PageBudget.MinimumTimeoutSeconds` keeps a configured zero from
switching the limit off. A page that outruns its budget still burns CPU until it stops on its own, so
the slow pages in those tests end shortly after the deadline and the fixtures that run them keep the
process to themselves.

The storage fixtures do not migrate a database per test either: each migration costs about fifty
milliseconds and every test needs its own file, so a fixture migrates one `DatabaseTemplate` and each
test starts from a copy.

The four projects are independent processes, so `make -j4 test` runs them at once as well: about 15 s
against 30 s on an eight-core machine, even though each suite then reports a slightly longer duration.

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

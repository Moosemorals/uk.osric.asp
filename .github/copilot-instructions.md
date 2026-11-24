<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Copilot Instructions

ASP.NET Core **minimal endpoints** on .NET 11 — no MVC controllers, no Razor views: pages are C#
held in the database, compiled when saved, and rendered through `IContext`. Developed on **Linux** —
assume Linux tooling, paths and scripts, with `/` separators.

- `src/uk.osric.asp.Sdk` — the page contract a compiled page binds against; no references at all.
- `src/uk.osric.asp.Core` — host support: pure functions and values; no references at all.
- `src/uk.osric.asp.Runtime` — page compiler and assembly loading; Roslyn, nothing web-facing.
- `src/uk.osric.asp.Web` — imperative shell: endpoints, hosting, storage, configuration.
- `test/` — one NUnit project per product project with behaviour, plus `Web.IntegrationTests`
  against the real host. `Sdk` has no suite of its own.
- `docs/` — architecture and its subsystems ([pages](../docs/pages.md), [the shell](../docs/shell.md),
  [storage](../docs/storage.md), [metrics](../docs/metrics.md), [assistant](../docs/assistant.md)), plus
  decisions, testing and logging. `deploy/` — cloud-init, quadlets, Caddyfile.

Dependency direction is `Web` → `Runtime` → `Core`, `Runtime` → `Sdk` and `Web` → `Sdk`; keep `Core`
and `Sdk` reference-free.
Build output goes to `artifacts/` (`UseArtifactsOutput`) — never reference or commit it. Shared
settings live in the top-level `Directory.*` files and the SDK is pinned in `global.json`.

## Tasks

The `Makefile` is the only entry point — `make help` lists every target; don't invent parallel
scripts. The ones you need most: `make build` (the default), `make test`, `make format`, `make run`.
New files need their SPDX tags or `make license-check` fails; files that cannot take a comment go in
`REUSE.toml`.

## Plans and decisions

A plan is written before a large change (`product-plan-before-building`), as a `## Plan` section of
its item's file, and it proposes the decision it records by the name that decision will carry. A
decision has a name rather than a number — `area-subject`, from the fixed list of areas — and the name
is taken when the work merges, not when it is planned: until then the plan states the name it intends,
and the merge writes the entry in `docs/decisions.md`, points the plan at it, marks the file's Status
merged and moves it — an item to `docs/backlog/archive/` with its plan, one of the seven plans written
before 2026-11-24 to `docs/plans/archive/`. A name is permanent once merged, is cited in backticks
with no number, and is never disambiguated with a suffix: if two branches land one name, the merge
folds them into one entry or narrows one of the names, and updates every citation in that branch.
`docs/archive/decisions.md` is frozen — a `DEC-` number written before 2026-11-15 means what it meant then.
`make decisions` is the gate's check on the record: a name outside the area list, a name used twice, an
entry with no "Recorded in" line, a retired name taken again and a citation of a decision that is not
there all fail it. See [decisions](../docs/decisions.md).

Every step of the programme is proposed and waits for the owner: the questions an item or a plan puts
to them, before anything is planned or built from it; then the plan, before anything is built; then
the merge, for every step, once its gate is run (`product-steps-wait-for-the-owner`). An item or a
plan that carries `## Open questions` — or puts the same choice under another heading — is asked
first: writing the plan or the code and asking afterwards is the wrong order. Working on a branch in
its own worktree, a green `make check`, a finished plan and an obvious next step are not permission to
merge, to push, or to start the next step — ask, and stop.

The work itself is the backlog in `docs/backlog`: an item is a slug and a title, where a defect's
slug states its kind and an idea's states none, and `job` is the host's word for a `PageJob` rather
than for an item. An item with no answers yet is `unshaped` — a whim is one of those, not a row in a
separate table — and it has no row in the order until it is shaped. `make backlog` is the gate's check
on the record; [its index](../docs/backlog/README.md) says what an item carries and how it is ordered.

## Core and shell

See `docs/architecture.md`. Rules, calculations, parsing and formatting are short, reusable **pure**
functions over immutable values in `Core` — no I/O, clock, configuration, DI, ASP.NET or logger.
Endpoints are a thin shell: bind the request, call the core, shape the response. Pass data in and get
values back; never hand the core a service, logger or `HttpContext`.

A new view is a page before it is an endpoint. The shell is for what a page cannot do or must not be:
minting an identity or setting a cookie, reading or writing the page store, framing and security
headers, work that outlives the request, and the error, health and metrics routes that have to answer
when the page pipeline itself is broken. A view that ships with the site is a seeded page under
`Pages/Seeds`, so the owner can edit it. When a missing page capability is the only thing in the way,
add it to `docs/backlog/` as an idea rather than growing the shell by default.

The author manual is the seeded `/help` page, linked from the editor. A change that adds or alters
what a page author can do — a context member, a capability, a layout slot, an editor action, a
content type or a response rule — updates `/help` in the same change, and its plan says so beside
the tests and the architecture notes.

## Configuration

A **policy value** comes from configuration, never from a literal and never from a fallback in code
(`code-policy-values-are-configuration`). A policy value is a limit, timeout, quota, retry count, cache or sweep interval, feature
switch, host or credential list, or a name that decides routing or authority.

- The shape is an options record in `Web`, with a `FromConfiguration` factory that reads every value
  through `ConfigurationReader` (under `Web/Configuration`), and a test for the shipped value, an
  override and a refusal. `appsettings.json` is the shipped source of truth; a production override
  may be partial because providers merge key by key.
- Nothing has a code fallback. A missing, blank, malformed or out-of-range value stops the process at
  startup with a `ConfigurationException` that names the key, the accepted range and the recommended
  value. Every section's problems are collected into one report before it is made, so an operator
  fixes a list rather than one section per restart. `Program.Main` writes that report to standard
  error and exits non-zero.
- A range is a safety net, not a straitjacket: wide enough that only a mistake meets it. It exists so
  a bad value cannot switch a guard off, not to ration a legitimate deployment, and the refusal names
  the range so the operator can see what is allowed.
- `Core` never reads configuration: the shell turns a setting into a plain value and passes it in.
- A genuinely optional value is declared and documented as optional. Absent is not the same as
  blank, and blank is accepted only where blank has a meaning.
- An **invariant** stays in code: a cryptographic or wire-format constant, a schema constraint, an
  algorithm work limit, or the floor and ceiling that keep a configured value from switching a guard
  off. A security policy may be configured only in the direction that keeps or tightens the shipped
  guarantee; one that configuration could only weaken stays an invariant until a decision says
  otherwise, and a number the product fixes as its own rule — a password's bounds — is not a policy
  value at all. [Configuration](../docs/architecture.md#configuration) lists what stays and why.

## JavaScript

Seeded scripts under `Pages/Seeds` and any new JavaScript are native ES modules: no bundler, no npm,
no transpiler. Use `import`/`export` rather than globals, and end a side-effect-only script with
`export {};`. Load every script with `<script type="module" src="/…"></script>` — modules are already
deferred, so no `defer` or document-position workaround is needed. A page that serves JavaScript sets
its own `text/javascript` content type and `Cache-Control: no-cache`; a stored page that loads one
opts in with `ctx.AllowScripts()`.

## JSON

Every JSON the host writes or reads goes through the one set of options in
`Core.Text.JsonSerialization` (`code-json-options-are-one-set`): web defaults, a relaxed encoder that
writes markup and non-ASCII text
as themselves, and enums by name. A call site names that instance; it never constructs, copies or
mutates options of its own. A second setting is added to the type only when a shape has to differ, and
the reason is written on the member. A page's own code may build its own options, because its types
and its API are its own.

## Testing

See `docs/testing.md`. Test-first: write the failing test, make it pass, then refactor. Every test is
NUnit — one behaviour per test, named for the behaviour. Anything a unit test can prove does not
belong in a browser test.

## Dependencies

Reach for `System.*` and `Microsoft.*` first: they are positively encouraged. Strongly prefer writing
the code ourselves; a third-party package needs a clear justification and a pinned version in
`Directory.Packages.props`. The sanctioned exception is the test tooling: NUnit and its adapter.

## Simplicity

`n` is small. Prefer the obvious loop, linear scan or in-memory dictionary over clever data
structures, caching or premature abstraction. Optimise only what a measurement shows is slow.

## Deployment

Detail is in `deploy/README.md`. The quadlets, the Caddyfile and the CI files under `deploy/ci` are
the source of truth for the server — edit them there, not on the host. A push to `trunk` on the host
runs `make ci` in a separate `ci` account and publishes the result at
`https://moosemorals.com/ci/status.json`; `make deploy` is still the release command, and
`make ci-install` prepares that account. The image is built by the SDK's container targets
(`dotnet publish -t:PublishContainer`) around the publish output for
`linux-musl-arm64`/`linux/arm64` (the Hetzner CAX21 host; override both in the Makefile for x86),
the SDK never enters the image, the app runs rootless as the `deploy` user, and Caddy is the only
rootful unit. Keep the build reproducible: pinned base digest, `SOURCE_DATE_EPOCH` from the commit,
nothing in the image that records the time of the build. Do not enable trimming: a trimmed ASP.NET
Core host dies at startup.

## C# style

`.editorconfig` is authoritative, the build enforces it (warnings are errors, and
`EnforceCodeStyleInBuild` is on) and `make format-check` asks `dotnet format` only for the layout the
build cannot see — the whitespace rules and using-directive order. Layout, naming, SPDX headers and
framework conventions are deliberately not restated here. The judgement calls it cannot express:

- Declare only the usings a file adds beyond the SDK's: `ImplicitUsings` supplies the built-in
  namespaces everywhere and test projects take `NUnit.Framework` project-wide, so a file never
  repeats `System.*`, the hosting namespaces or the test framework.
- `record`/`record struct` for immutable data and value objects, `class` for behaviour, `struct` only
  for small immutable values. Primary constructors for classes that only capture dependencies.
- Prefer interfaces over abstract base classes as extension points.
- Order inside a type: constants, static fields, instance fields, constructors, properties, methods,
  nested types.
- Immutable state: `init` setters, and `IReadOnlyList<T>`/`IReadOnlyDictionary<TKey, TValue>` on the
  public surface. Validate public entry points with `ArgumentNullException.ThrowIfNull` and friends.
- Guard clauses and early returns over nested `if`s. Exceptions only for exceptional conditions, with
  specific types; no silent catches, and no `catch (Exception)` unless it rethrows or logs.
- Async: `Async` suffix, `CancellationToken` last and forwarded, never block, and don't add
  `async`/`await` when the method can simply return the task. `ConfigureAwait(false)` is unnecessary
  here.
- `StringComparison.Ordinal`/`OrdinalIgnoreCase` unless linguistic comparison is intended.
- Structured logging with message templates and arguments, never interpolation.
- Comment the non-obvious *why*; do not narrate the code. XML-document the public surface of `Core`;
  the product assemblies emit their documentation, so an undocumented public member is a build
  failure (CS1591). The shell is not documented member by member.
- The full `CA` rule set is on (`AnalysisMode=All`) and a warning is an error. A rule this codebase
  does not follow is turned off in `.editorconfig` beside its reason; a `#pragma` at a call site
  carries one too. Never mute a warning without saying why, and never add an exception to save a
  single line. The tainted-data family (`CA3001`-`CA3012`) is the one configuration-dependent part
  (`Directory.Build.targets`): it runs everywhere except Debug, because it is most of `Web`'s compile
  time — so a Debug build will not find a rule there and `make check`'s Release publish will.

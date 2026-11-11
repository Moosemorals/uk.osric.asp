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

A plan is written before a large change (DEC-09), but the decision it records is numbered when the
work merges, not when it is planned: refer to it in prose until then, take the next `DEC-` number
from `docs/decisions.md` on trunk when the work lands, and update every citation in the branch in
that merge. See [decisions](../docs/decisions.md).

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
add it to `docs/jobs/` as an `IDEA-` job rather than growing the shell by default.

The author manual is the seeded `/help` page, linked from the editor. A change that adds or alters
what a page author can do — a context member, a capability, a layout slot, an editor action, a
content type or a response rule — updates `/help` in the same change, and its plan says so beside
the tests and the architecture notes.

## JavaScript

Seeded scripts under `Pages/Seeds` and any new JavaScript are native ES modules: no bundler, no npm,
no transpiler. Use `import`/`export` rather than globals, and end a side-effect-only script with
`export {};`. Load every script with `<script type="module" src="/…"></script>` — modules are already
deferred, so no `defer` or document-position workaround is needed. A page that serves JavaScript sets
its own `text/javascript` content type and `Cache-Control: no-cache`; a stored page that loads one
opts in with `ctx.AllowScripts()`.

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

Detail is in `deploy/README.md`. The quadlets in `deploy/` are the source of truth for the server —
edit them there, not on the host. The image is built by the SDK's container targets
(`dotnet publish -t:PublishContainer`) around the publish output for
`linux-musl-arm64`/`linux/arm64` (the Hetzner CAX21 host; override both in the Makefile for x86),
the SDK never enters the image, the app runs rootless as the `deploy` user, and Caddy is the only
rootful unit. Keep the build reproducible: pinned base digest, `SOURCE_DATE_EPOCH` from the commit,
nothing in the image that records the time of the build. Do not enable trimming: a trimmed ASP.NET
Core host dies at startup.

## C# style

`.editorconfig` is authoritative and `dotnet format` (or `make format-check` in CI) enforces it —
layout, naming, SPDX headers and framework conventions are deliberately not restated here. The
judgement calls it cannot express:

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
  the shell is not documented member by member.

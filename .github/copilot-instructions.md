<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Copilot Instructions

ASP.NET Core **minimal endpoints** on .NET 10 — no MVC controllers, no Razor views: pages are C#
held in the database, compiled when saved, and rendered through `IContext`. Developed on **Linux** —
assume Linux tooling, paths and scripts, with `/` separators.

- `src/uk.osric.asp.Core` — pure functions and values; no references at all.
- `src/uk.osric.asp.Runtime` — template compiler and assembly loading; Roslyn, nothing web-facing.
- `src/uk.osric.asp.Web` — imperative shell: endpoints, hosting, storage, configuration.
- `test/` — one NUnit project per `src/` project, plus `Web.IntegrationTests` against the real host.
- `docs/` — architecture and testing notes. `deploy/` — cloud-init, quadlets, Caddyfile.

Dependency direction is `Web` → `Runtime` → `Core`, plus `Web` → `Core`; keep `Core` reference-free.
Build output goes to `artifacts/` (`UseArtifactsOutput`) — never reference or commit it. Shared
settings live in the top-level `Directory.*` files and the SDK is pinned in `global.json`.

## Tasks

The `Makefile` is the only entry point — `make help` lists every target; don't invent parallel
scripts. The ones you need most: `make build` (the default), `make test`, `make format`, `make run`.
New files need their SPDX tags or `make license-check` fails; files that cannot take a comment go in
`REUSE.toml`.

## Core and shell

See `docs/architecture.md`. Rules, calculations, parsing and formatting are short, reusable **pure**
functions over immutable values in `Core` — no I/O, clock, configuration, DI, ASP.NET or logger.
Endpoints are a thin shell: bind the request, call the core, shape the response. Pass data in and get
values back; never hand the core a service, logger or `HttpContext`.

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
edit them there, not on the host. The image is built by podman from `deploy/Containerfile` around the
publish output (the SDK never enters the image), runs rootless as the `deploy` user, and Caddy is the
only rootful unit. Keep the build reproducible: pinned base digest, `--timestamp` from the commit,
nothing in the image that records the time of the build. Do not enable trimming: a trimmed ASP.NET
Core host dies at startup.

## C# style

`.editorconfig` is authoritative and `dotnet format` (or `make format-check` in CI) enforces it —
layout, naming, SPDX headers and framework conventions are deliberately not restated here. The
judgement calls it cannot express:

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

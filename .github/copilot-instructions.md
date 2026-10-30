<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Copilot Instructions

## Repository status

ASP.NET Core **MVC** (controllers + Razor views) on .NET 10, developed on **Linux** — assume Linux for
all tooling, paths, and scripts; use `/` separators in MSBuild paths.

```
src/uk.osric.asp.Core      functional core: pure functions and values, no references at all
src/uk.osric.asp.Runtime   template compiler and assembly loading; Roslyn, nothing web-facing
src/uk.osric.asp.Web       imperative shell: controllers, Razor views, hosting, configuration
test/uk.osric.asp.UnitTests  NUnit, tests the core directly
test/uk.osric.asp.Specs      Reqnroll/Gherkin, describes business behaviour
test/uk.osric.asp.WebTests   NUnit, tests the runtime and the web shell
docs/                        architecture and testing notes
```

- Dependency direction is `Web` → `Runtime` → `Core`, plus `Web` → `Core`. `Core` stays
  reference-free; Roslyn lives in `Runtime` only, so the shell never names a compiler type.

- Build output never lives next to the source: `Directory.Build.props` sets `UseArtifactsOutput`, so
  `bin`/`obj` for every project land under `artifacts/`. Never reference or commit them.
- Shared settings belong in the top-level `Directory.*` files, not in individual projects:
  `Directory.Build.props` (build policy), `Directory.Build.targets` (test-project conventions and
  embedded `RepoRoot` metadata), `Directory.Packages.props` (Central Package Management versions).
  Project files should stay as small as possible.
- The SDK is pinned in `global.json`.

## Build, test, and lint

The `Makefile` is the task runner; use it instead of inventing parallel scripts.

- Build (default target): `make build` — only runs MSBuild when a source, project, or build file
  changed since the last build (stamp in `artifacts/`). Use `make -B build` to force a rebuild.
- Tests: `make test` (unit + specifications), `make test-unit`, `make test-spec`
- Run the site: `make run`
- Format: `make format`; verify: `make format-check`
- Licence headers: `make license-check` (REUSE, needs the `reuse` tool)
- Clean (deletes `artifacts/`): `make clean`
- NuGet restore: automatic — `build` restores only when `global.json`, the solution, `Directory.*`
  files, a `.csproj`, or a NuGet config is newer than `artifacts/restore.stamp`. Use `make restore`
  to force one.

## Architecture: functional core, imperative shell

See `docs/architecture.md`. In short:

- Business rules, calculations, parsing, and formatting live in `Core` as short, reusable **pure**
  functions over immutable values — no I/O, clock, configuration, DI, or ASP.NET.
- Controllers and views are a thin shell: bind the request, call the core, shape the response.
- `Core` must not gain project or package references. If a change needs one, it belongs in the shell.
- Effects that are not HTTP — compiling and loading pages — live in `Runtime`, between the two.
- Pass data into the core and get values back; never hand the core a service, logger, or `HttpContext`.

## Testing

Test-driven. See `docs/testing.md`.

- Write the failing test first for anything in `Core`, then make it pass, then refactor.
- NUnit for individual rules, Reqnroll for business behaviour in plain language.
- Anything a unit test can prove does not belong in a browser test.

## Dependencies

- Reach for `System.*` and `Microsoft.*` first: they are positively encouraged.
- Strongly prefer writing the code ourselves over adding a third-party package; a new dependency needs
  a clear justification and its version pinned in `Directory.Packages.props`.
- The sanctioned third-party exceptions are the test tools: NUnit and Reqnroll.

## Simplicity

- `n` is small. Prefer the obvious loop, linear scan, or in-memory dictionary over clever data
  structures, caching, or premature abstraction. Optimise only what a measurement shows is slow.
- Prefer short, reusable pure functions over long methods and shared mutable state.

## Deployment

- `make image` builds the OCI image with the SDK's container support (`-t:PublishContainer`) into
  podman; `make deploy HOST=deploy@host` ships the image and the quadlets and restarts them.
- `deploy/` holds the cloud-init bootstrap, the podman quadlets, and the Caddyfile. The quadlets are
  the source of truth for the server — edit them there, not on the host.
- The app runs **rootless** as a user quadlet for the `deploy` user and publishes only
  `127.0.0.1:8080`; Caddy is the single rootful unit (host network, 80/443) and proxies to it.
- There is deliberately no Dockerfile: the image is built by the SDK, and the host runs podman.
- Images go through `registry.osric.uk`; the build is framework-dependent on Alpine so only the
  177 kB app layer moves per release. All container knobs live in the `image` target. Do not enable
  trimming — MVC does not support it.

## C# style

`.editorconfig` is authoritative for everything a formatter can decide — run `dotnet format` (or `dotnet format --verify-no-changes` in CI) instead of hand-formatting. The conventions below cover the judgement calls it cannot express.

### Baseline

- Use the current SDK and latest language version; write modern C#, never the legacy equivalent of a modern construct.
- K&R braces: opening brace on the same line, `else`/`catch`/`finally` on the closing-brace line, braces always used.
- UTF-8 without BOM, 4-space indentation, a single trailing newline.
- Explicit types everywhere — no `var` in declarations, `foreach`, `using`, `out`, or patterns. Use target-typed `new()` when the type is already stated or obvious from the signature.
- No redundant `this.`.
- Keep `if` statements as `if` statements: do not collapse branching statements into `?:`. Conditional expressions are fine where an expression is required (initializers, arguments, expression-bodied members).

### Files, namespaces, and usings

- Every file carries the two SPDX tags (`SPDX-FileCopyrightText` and `SPDX-License-Identifier: MIT`)
  in its own comment syntax; C# is enforced by `file_header_template` (IDE0073). Files that cannot
  take comments (JSON, the licence text itself) are covered by `REUSE.toml`, and licence texts live
  in `LICENSES/`. Keep it REUSE-clean when adding files.
- One top-level type per file, named after the file.
- File-scoped namespace matching the folder path.
- `using` directives inside the namespace, one alphabetical group, `System.*` not privileged.
- No `global using` beyond the implicit SDK usings; prune unused usings as you edit.
- Order inside a type: constants, static fields, instance fields, constructors, properties, methods, nested types.

### Types and members

- Primary constructors for classes that only capture dependencies; promote to a field only when a different name, validation, or mutation is needed.
- `record`/`record struct` for immutable data and value objects, `class` for behaviour, `struct` only for small immutable values.
- Prefer interfaces over abstract base classes as extension points.
- Expression-bodied properties, indexers, and accessors; block bodies for methods, constructors, and operators.
- `readonly` fields wherever possible, initialised at the declaration; use `const` for compile-time constants and `static readonly` otherwise.
- Prefer immutable state: `init` setters, and `IReadOnlyList<T>`/`IReadOnlyDictionary<TKey, TValue>` for exposed data.
- Nullable reference types enabled; never suppress with `!` without a comment proving the invariant.
- Return empty collections rather than `null`. Validate public entry points with `ArgumentNullException.ThrowIfNull` or `ArgumentException.ThrowIfNullOrWhiteSpace`.
- `is null` / `is not null`, never `== null` / `!= null`.

### Control flow

- Guard clauses and early returns over nested `if`s.
- `switch` expressions and pattern matching over `if`/`else if` chains testing the same value.
- Collection expressions (`[]`, `[a, b]`) for arrays, `List<T>`, and spans.
- No `#region`, and no `#pragma warning disable` or `[SuppressMessage]` without a comment explaining why.

### APIs and async

- Public members expose `IEnumerable<T>`, `IReadOnlyList<T>`, or `IReadOnlyDictionary<TKey, TValue>` — not concrete collections or arrays.
- Specify `StringComparison.Ordinal`/`OrdinalIgnoreCase` unless linguistic comparison is intended.
- `Async` suffix on asynchronous methods; return `Task`/`ValueTask`, never `async void` except event handlers.
- Take `CancellationToken` as the last parameter and forward it.
- Never block: no `.Result`, `.Wait()`, or `GetAwaiter().GetResult()`.
- Do not add `async`/`await` when the method can simply return the task. `ConfigureAwait(false)` is unnecessary in ASP.NET Core.
- Exceptions only for exceptional conditions, with specific exception types; no silent catches, and no `catch (Exception)` outside a boundary that rethrows or logs.
- Structured logging with message templates and arguments, never interpolation.

### Comments

- Comment the non-obvious *why*; do not narrate the code.
- XML-document the public surface of `Core`; the shell is not documented member by member.

Naming is also enforced by `.editorconfig`: `_camelCase` instance fields, `s_camelCase` private static fields, `PascalCase` constants and public/static readonly fields, `I`-prefixed interfaces, `T`-prefixed type parameters, `PascalCase` types and members, `camelCase` locals and parameters.

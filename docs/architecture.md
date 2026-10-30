<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Architecture

## Functional core, imperative shell

The boundary is pure rules over values, with effects isolated in their own project where the compiler
can enforce it.

- **`src/uk.osric.asp.Core`** holds the pure half: the template tokenizer, the source generator, and
  the `IContext`/`IHandler` contracts. It has no project or package references at all, so it cannot
  reach for I/O, a clock, DI or ASP.NET. Its only dependency is the base class library.
- **`src/uk.osric.asp.Runtime`** holds the imperative half of template execution: `TemplateCompiler`
  reads platform assembly paths, generates assembly names, emits to a stream, and loads and invokes
  the compiled handler in a collectible `AssemblyLoadContext`. It references `Core` and Roslyn, and
  nothing else — in particular no ASP.NET, storage or logging types, because it is not built on the
  Web SDK.
- **`src/uk.osric.asp.Web`** owns HTTP endpoints, hosting, configuration, `SimpleContext`, and source
  storage. EF Core, SQLite, database migrations and `IStorage` belong here.

The dependency direction is one-way: `Web` → `Runtime` → `Core`, and `Web` → `Core`. Nothing points
back up. `TemplateCompiler` returns a `CompileResult` of booleans and strings rather than Roslyn
`EmitResult`/`Diagnostic` values, so the shell never sees the compiler's types.

## Where does code go?

| Kind of code | Home |
| --- | --- |
| Business rules, calculations, parsing, formatting, validation | `Core`, as pure functions |
| Values passed between layers | `Core`, as `record`/`readonly record struct` |
| Compiling templates, loading assemblies, invoking handlers | `Runtime` |
| HTTP binding, status codes, responses, hosting, `SimpleContext` | `Web` |
| I/O, time, randomness, configuration | `Runtime` or `Web`, turned into plain values before calling the core |
| Source storage, `IStorage`, EF Core context and migrations | `Web`, under `Storage` for implementation |

When something in the shell needs a decision from a pure rule, pass the data in and return a value;
do not hand that rule `IStorage`, a database context, a logger, or `HttpContext`. The runtime passes
`IContext` to a compiled handler; that is the deliberate seam between compiled page code and the
host, and `SimpleContext` is its only implementation.

## Source storage

The Web layer owns EF Core SQLite source storage and applies migrations asynchronously during
startup, before serving requests. `ConnectionStrings:Storage` defaults to
`Data Source=data/storage.db`; deployment overrides it to `Data Source=/data/storage.db` on a durable
rootless Podman named volume. Database access and migration lifecycle stay in the shell.

This is source storage: `MainEndpoint` reads a page's source by route path, compiles it and runs it,
and `EditorEndpoint` writes it back. Pure rules still receive the values loaded by the shell rather
than being given `IStorage`.

`make test-web` runs the Web storage tests and is included in `make test`. Schema changes are
scaffolded with `make migration NAME=...` using the pinned local EF tool into the Web project's
`Storage/Migrations` directory. Back up the database before deploying startup migrations; see
[deployment storage and backup guidance](../deploy/README.md#storage-and-migrations).

## Shapes

- `record` (or `readonly record struct` for small values) over classes.
- `struct` for small immutable values where allocation or identity genuinely matters; measure before
  reaching for one.
- `static` classes of pure functions when there is no state to carry.
- `class` only when the framework demands it (controllers) or the type has identity.
- Prefer composition over inheritance; there is no inheritance in the core today and little need for it.

## Simplicity first

`n` is small. Prefer the obvious loop, the linear scan, and the in-memory dictionary over clever data
structures or caches. Optimise only what a measurement shows to be slow, and optimise it then.

Reach for `System.*` and `Microsoft.*` first. Write the remaining code ourselves rather than adding a
package: a dependency is a long-term liability, and most of what we need is short.

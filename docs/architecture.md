<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Architecture

## Functional core, imperative shell

The intended boundary is pure rules over values, with effects coordinated by the web shell. The
current implementation does not make all of `Core` pure.

- **`src/uk.osric.asp.Core`** contains the compiler/runtime and its handler/context contracts. The
  compiler depends on Roslyn, reads runtime assembly metadata, generates assembly names, writes
  streams, and loads and invokes compiled handlers. That is imperative infrastructure, not a pure
  function with deterministic outputs.
- **`src/uk.osric.asp.Web`** owns HTTP endpoints, hosting, configuration, `SimpleContext`, and source
  storage. EF Core, SQLite, database migrations and `IStorage` belong here, not in `Core`.

The project dependency is one-way: `Web` references `Core`, never the reverse. `Core` currently has
Roslyn package dependencies; it is not reference-free. Pure business rules should still accept plain
values and return values, so they can be tested without a web host, database or browser.

## Where does code go?

| Kind of code | Home |
| --- | --- |
| Business rules, calculations, parsing, formatting, validation | `Core`, as pure functions |
| Values passed between layers | `Core`, as `record`/`readonly record struct` |
| HTTP binding, status codes, responses, hosting, `SimpleContext` | `Web` |
| I/O, time, randomness, configuration | `Web`, turned into plain values before calling the core |
| Source storage, `IStorage`, EF Core context and migrations | `Web`, under `Storage` for implementation |
| Compiler/runtime and handler/context contracts | Currently `Core`, imperative and Roslyn dependent |

When something in the shell needs a decision from a pure rule, pass the data in and return a value;
do not hand that rule `IStorage`, a database context, a logger, or `HttpContext`. The existing runtime
passes `IContext` to a compiled handler; that imperative execution contract is not a pure rule.

## Source storage

The Web layer owns EF Core SQLite source storage and applies migrations asynchronously during
startup, before serving requests. `ConnectionStrings:Storage` defaults to
`Data Source=data/storage.db`; deployment overrides it to `Data Source=/data/storage.db` on a durable
rootless Podman named volume. Database access and migration lifecycle stay in the shell.

This is storage infrastructure only. It does not yet connect routes to stored scripts, persist editor
changes, or expose storage to the script runtime or `SimpleContext`. Pure rules receive the values
loaded by the shell rather than being given `IStorage`.

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

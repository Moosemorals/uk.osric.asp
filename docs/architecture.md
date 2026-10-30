<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Architecture

## Functional core, imperative shell

The solution splits every feature into two halves.

- **`src/uk.osric.asp.Core` — the functional core.** Pure functions and immutable values. No I/O, no
  clock, no configuration, no dependency injection, no ASP.NET. Same inputs, same outputs, every time.
- **`src/uk.osric.asp.Web` — the imperative shell.** MVC controllers, Razor views, hosting, and
  configuration. Controllers stay thin: bind the request, call the core, shape the response.

The dependency rule is one-way and absolute: `Web` references `Core`, never the reverse, and `Core`
references nothing at all. `uk.osric.asp.Core.csproj` has no references, packages or otherwise — if a
change needs one, the code belongs in the shell.

Keeping the core pure is what makes the business logic testable without a web host, a database, or a
browser, and it is why the unit and specification suites run in milliseconds.

## Where does code go?

| Kind of code | Home |
| --- | --- |
| Business rules, calculations, parsing, formatting, validation | `Core`, as pure functions |
| Values passed between layers | `Core`, as `record`/`readonly record struct` |
| HTTP binding, status codes, view models, Razor | `Web` |
| I/O, time, randomness, configuration | `Web`, turned into plain values before calling the core |

When something in the shell needs a decision from the core, pass the data in and return a value —
do not hand the core a service, logger, or `HttpContext`.

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

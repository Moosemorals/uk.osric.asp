<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-03 — No execution timeout, cancellation or concurrency limit for page code

| | |
| --- | --- |
| Severity | High |
| Area | Architecture, runtime |
| Location | `src/uk.osric.asp.Core/IHandler.cs`, `src/uk.osric.asp.Core/IContext.cs`, `src/uk.osric.asp.Core/TemplateCompiler.cs:42-61` |

## Problem

`IHandler.Handle` is synchronous and `IContext` has no `CancellationToken`. `TemplateCompiler.Run`
invokes the handler directly on the request thread. Consequences:

- A page containing `while (true) { }` pins a thread-pool thread forever; enough such requests
  exhaust the pool and take the site down.
- Client disconnects (`RequestAborted`) are ignored while a page runs; the work continues.
- Compilation itself has no bound: a pathological template keeps the request (and a core) busy.
- Graceful shutdown waits on in-flight requests with no way to interrupt a page.

The runtime executes code written by a human who makes mistakes, so "it always terminates" is not a
safe assumption.

## Fix outline

1. Pass `CancellationToken` into the page API (`IContext.CancellationToken` or a request value) so
   cooperating pages can observe aborts.
2. Wrap handler execution in a bounded wait: run it on a worker and return 503/504 after a
   configurable timeout. Cancellation cannot forcibly stop arbitrary C#, so the token lets good
   pages stop and the timeout bounds the damage.
3. Throttle compilation and execution with a `SemaphoreSlim` (or `RateLimiter`) so a burst of
   requests cannot saturate every core.
4. Consider `Parallel.For`-style bounded concurrency only if measurements justify it; a simple
   semaphore matches the project's "n is small" rule.
5. Log a warning when a page exceeds the timeout, with the path and elapsed time.
6. Test: a sleeping page returns 504 within the limit; a cancelled request stops cooperatively.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-04 — Reflection wrapper leaks `TargetInvocationException` into the shell

| | |
| --- | --- |
| Severity | Medium |
| Area | Architecture, error handling |
| Location | `src/uk.osric.asp.Core/TemplateCompiler.cs:57`, `src/uk.osric.asp.Web/ErrorEndpoint.cs:12-18` |

## Problem

`TemplateCompiler.Run` calls the handler through `MethodInfo.Invoke`, which wraps any exception in
`TargetInvocationException`. The shell then has to know that detail: `ErrorEndpoint` pattern-matches
`TargetInvocationException { InnerException: UserFacingException }` to show a safe message.

That coupling is wrong in three ways:

- The shell encodes a reflection implementation detail of the runtime.
- Only one wrapper level is unwrapped; `AggregateException` or a future wrapper defeats it.
- The original stack trace is nested and, once flattened, harder to read; `TargetInvocationException`
  also shows up in logs and in tests (`MainEndpointTests` asserts on it).

## Fix outline

1. In `Run`, wrap `handle.Invoke` in `try`/`catch (TargetInvocationException ex) when (ex.InnerException is not null)`
   and rethrow the inner exception with `ExceptionDispatchInfo.Capture(ex.InnerException).Throw()`.
2. Then `ErrorEndpoint` only needs to inspect the exception itself, not reflection wrappers; delete
   the `TargetInvocationException` case.
3. Update `MainEndpointTests.StoredHandlerIsCompiledAndExecutedAsync` to expect
   `InvalidOperationException` directly.
4. Keep `UserFacingException` as the only "safe to display" signal; document that contract on the
   type.

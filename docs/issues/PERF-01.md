<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# PERF-01 — Every request compiles the page from source; nothing is cached

| | |
| --- | --- |
| Severity | High |
| Area | Performance, runtime |
| Location | `src/uk.osric.asp.Web/MainEndpoint.cs:22-38`, `src/uk.osric.asp.Core/TemplateCompiler.cs:16-40` |

## Problem

`MainEndpoint` runs tokenizer → source generator → Roslyn compile → `Emit` → load → invoke on every
request, even when the page has not changed. Measured against the running app for a one-line page:

```
0.501  0.310  0.132  0.278  0.475   seconds
```

That is 130–500 ms of CPU per page view for a string that could have been compiled once. It also
churns Gen2 with a fresh assembly and a collectible `AssemblyLoadContext` per request, and creates
~200 `MetadataReference`s each time (`GetMetadataReferences()` is called inside `Compile`).

There is no cache keyed by content, so the server does the same work for every visitor.

## Fix outline

1. Compile once per content version, not per request. Two viable shapes:
   - **Compile on save** in the editor: store the emitted bytes (or the diagnostics) alongside the
     source, so rendering is a load-and-run and the editor can show errors immediately.
   - **In-memory cache** in the runtime keyed by `(path, SHA-256(content))`, bounded by count or
     size, with the compiled `byte[]` (not an `AssemblyLoadContext`) as the value.
2. Cache the metadata references in a `static readonly` field (created once per process) instead of
   rebuilding them per compile.
3. Keep the ALC per execution only if needed for unload; otherwise load the cached bytes into a
   long-lived context and accept that pages are replaced only on restart. Measure the memory.
4. Expose a cache hit/miss counter or log line so the improvement is observable.
5. Add a performance test or a simple benchmark that asserts a second request for the same page does
   not compile (e.g. a counter on the compiler abstraction from ARCH-01).
6. Only then measure again; do not add further optimisations without a number.

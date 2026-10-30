<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-08 — Missing `TRUSTED_PLATFORM_ASSEMBLIES` silently yields an empty reference set

| | |
| --- | --- |
| Severity | Low |
| Area | Runtime |
| Location | `src/uk.osric.asp.Core/TemplateCompiler.cs:16-25` |

## Problem

`GetMetadataReferences` returns `[]` when `AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")` is not
a string:

```csharp
if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa) {
    metadataPaths.AddRange(tpa.Split(Path.PathSeparator));
} else {
    return [];
}
```

The caller then compiles with no platform references and every page fails with confusing errors such
as "The type 'object' is defined in an assembly that is not referenced". The cause is invisible in
the diagnostics. This can happen when the runtime is hosted unusually (single-file, some test hosts,
trimmed deployments), and the failure mode is a diagnostic puzzle rather than a clear error.

## Fix outline

1. Throw an `InvalidOperationException` with an explicit message when the TPA data is missing, so the
   startup or first request fails loudly with the real cause.
2. Alternatively, fall back to `typeof(object).Assembly.Location` plus the app's dependency
   assemblies from `Assembly.GetEntryAssembly()?.GetReferencedAssemblies()`.
3. Log which assemblies were referenced at startup (Debug level) so the set is diagnosable.
4. Add a unit test by invoking the private method through a refactor (make it internal or extract
   `ReferenceSet.ForRuntime()`), asserting a non-empty set.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-12 — Unused Roslyn Workspaces packages ship in the web app

| | |
| --- | --- |
| Severity | Low |
| Area | Dependencies, build |
| Location | `src/uk.osric.asp.Web/uk.osric.asp.Web.csproj:14-15` |

## Problem

The web project references:

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" PrivateAssets="all" />
<PackageReference Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" PrivateAssets="all" />
```

Nothing in the repository uses either namespace. `grep` for `Workspaces` finds only these two lines and
their versions in `Directory.Packages.props`. They are leftovers (likely from an abandoned
design-time compilation idea), but they are copied into the build output —
`artifacts/bin/uk.osric.asp.Web/debug` contains `Microsoft.CodeAnalysis.Workspaces.dll`,
`Microsoft.CodeAnalysis.Workspaces.MSBuild.dll` and
`Microsoft.CodeAnalysis.ExternalAccess.RazorCompiler.dll` — and they enlarge the publish output.

The only Roslyn package the solution needs is `Microsoft.CodeAnalysis.CSharp`, referenced by
`Runtime`; the shell names no compiler type at all, because compilation diagnostics reach the
endpoints as `PageDiagnostic` values.

## Fix outline

1. Delete both `PackageReference` lines and drop their versions from `Directory.Packages.props`.
2. Add nothing in their place: `Web` uses `Runtime` through `ITemplateCompiler`, and
   `Microsoft.CodeAnalysis.CSharp` stays a `Runtime` dependency.
3. Run `dotnet list package --include-transitive` and the publish size comparison to confirm.

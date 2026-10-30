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

Nothing in the repository uses either namespace. `grep` for `Workspaces` finds only these two lines.
They are leftovers (likely from an abandoned design-time compilation idea), but they are copied into
the build output — `artifacts/bin/uk.osric.asp.Web/debug` contains
`Microsoft.CodeAnalysis.Workspaces.dll`, `Microsoft.CodeAnalysis.Workspaces.MSBuild.dll` and
`Microsoft.CodeAnalysis.ExternalAccess.RazorCompiler.dll` — and they enlarge the publish output.

The only Roslyn package actually needed is `Microsoft.CodeAnalysis.CSharp` (referenced by `Core` and
used for `Diagnostic`/`EmitResult` in `MainEndpoint`).

## Fix outline

1. Delete both `PackageReference` lines.
2. If Web needs `Diagnostic`/`EmitResult` types explicitly, either keep the transitive reference via
   `Core` (the default for project references) or add `Microsoft.CodeAnalysis.Common`/`CSharp`
   explicitly — not the Workspaces packages.
3. Run `dotnet list package --include-transitive` and the publish size comparison to confirm.

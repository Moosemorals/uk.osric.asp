<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-01 — `Core` is not the functional core: it owns Roslyn, I/O and assembly loading

| | |
| --- | --- |
| Severity | High |
| Area | Architecture, project layout |
| Location | `src/uk.osric.asp.Core/uk.osric.asp.Core.csproj`, `src/uk.osric.asp.Core/TemplateCompiler.cs` |

## Problem

The documented rule is that `Core` holds pure functions over immutable values with no references,
and that effects live in the shell (`docs/architecture.md`, `.github/copilot-instructions.md`).
Reality:

- `Core` has a `PackageReference` to `Microsoft.CodeAnalysis.CSharp`.
- `TemplateCompiler` reads platform assembly paths, writes streams, generates GUID assembly names,
  creates an `AssemblyLoadContext`, and invokes handlers through reflection. None of that is pure.

The architecture document has been amended to concede this ("The current implementation does not
make all of `Core` pure") while the instructions still claim "no references at all". The boundary no
longer means anything, so it cannot guide where new code goes.

The pure and impure halves are already separable: `TemplateTokenizer`, `TemplateSourceGenerator`,
`IContext`, `IHandler` and `UserFacingException` are pure values/contracts; `TemplateCompiler` is the
only impure type.

## Fix outline

1. Add `src/uk.osric.asp.Runtime` (or `uk.osric.asp.Compiler`) containing `TemplateCompiler` and its
   Roslyn reference.
2. `Web` references both `Core` and `Runtime`; `Runtime` references `Core` for the contracts;
   `Core` keeps no package or project references.
3. Move the Roslyn package version to `Directory.Packages.props` (already there) and drop it from
   `Core`.
4. Keep `Core`'s unit tests as they are; move the compile-and-run tests (`TemplateSourceGeneratorTests`)
   to a runtime test project or leave them with the unit tests if they still only test the pure
   generator (they currently also compile).
5. Update `docs/architecture.md` to describe the three projects and the dependency direction, and
   update the repository-status block in `.github/copilot-instructions.md`.

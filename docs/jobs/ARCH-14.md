<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-14 — Page code is compiled without nullable analysis, and the assembly surface is wider than it needs to be

| | |
| --- | --- |
| Severity | Low |
| Area | Core, Runtime, web shell |
| Location | `src/uk.osric.asp.Runtime/PageCompiler.cs`, `src/uk.osric.asp.Core/Compilation/TemplateSourceGenerator.cs`, every product assembly |

Plan: [Page diagnostics and assembly visibility](../plans/page-diagnostics-and-visibility.md).

## Problem

Three things that are really one habit — treating a convention as if it were enforced.

**Pages are compiled nullable-oblivious.** `PageCompiler` builds
`new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)` and never sets
`NullableContextOptions`, so a page that dereferences `ctx.User` with no guard compiles silently,
even though `IContext.User` is `PageUser?` and the whole page contract is annotated. Page code
therefore gets *less* checking than the product's own code, which is the wrong way round: the page
is the part written in a textarea by someone who has not read the source. The consequence is also
that compiler warnings — already mapped, already stored, already shown in the editor, already sent to
the assistant — never have anything to say.

**Nothing holds the warning invariant.** A stored image with bytes carries warnings only, because
`CompileResult.Image` is null whenever emit failed, and `SqlitePageStore` already leans on that when
it reads a pre-severity row. Nothing states it and nothing tests it, so a change that stored an image
beside an error would be noticed by a reader of one comment rather than by the build.

**The surface is wider than the rule.** `docs/architecture.md` says nothing is `public` unless
another product assembly needs it, and that every type in `Web` is `internal`. Two types in
`Web/Html` break it. At member level, `Core` publishes 232 members on public types and `Runtime` 16,
and no scan has ever decided which of them another assembly actually binds to. The rule is a
paragraph, so it drifts.

**Guards follow no rule at all.** 222 `Argument*Exception.ThrowIf*` statements are spread across the
four assemblies, 174 of them in `Web`, where nearly every type is internal and the compiler has
already checked the call. Fewer than a dozen sit in non-public members, where they cannot fire
except for a `null!` — and the public entry points that *can* be reached with anything are not
systematically covered. Neither absence nor presence is enforced by anything.

## Fix outline

1. Compile pages with `NullableContextOptions.Enable`, and make the generated scaffold oblivious so
   a diagnostic the author is shown is always about the author's code. The `#nullable` directive is a
   line-level directive, so it goes *outside* each `#line` region or the reported columns shift.
2. Hold the scaffold promise with a test rather than by making unmapped warnings fatal, and state
   and test the image-carries-warnings-only invariant.
3. Give a page a state — `Not compiled`, `Stale`, `Compile failed`, `Compiled with warnings`,
   `Compiled` — show it in a State column on the `/edit` listing, and pass the same words to the
   assistant's `list_pages` and its prompt.
4. Tighten the surface by making members `internal` and letting the compiler name the cross-assembly
   callers; `InternalsVisibleTo` means the tests keep compiling. Then hold it with
   `Microsoft.CodeAnalysis.PublicApiAnalyzers` and an empty `PublicAPI.Shipped.txt` in `Web`.
5. Validate documented entry points — public members of public types, the `Sdk` implementations and
   the endpoint handlers — and remove the guards from `internal` and `private` members.
6. Bump the compile `FormatVersion` so stored images are rebuilt, and let the new `Stale` state say so
   while they are.

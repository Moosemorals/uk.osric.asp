<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The page SDK, and the names that go with it

Implementation plan for [IDEA-004](../jobs/IDEA-004.md), plus the structural tidy-up that goes with it:
vertical slices instead of a flat namespace per project, and names that say whether a thing is a
template (what the author edits) or a page (what the compiler produces).

## Status

Not started. Plan written 2026-11-03 against the tree at that commit.

Baseline before any change: `make test` green — 154 core, 45 runtime, 318 web, 54 integration
(571 tests).

## The short version

1. `Core` stops being two things at once. The five types a compiled page binds against move to a
   new reference-free assembly, `uk.osric.asp.Sdk`; everything else in `Core` stays host support and
   splits into three namespaces.
2. `Template` means "what the author edits", `Page` means "what the compiler produces". The names
   follow: `TemplateCompiler` (the Web-side one) takes a template; `PageCompiler` (the Runtime one)
   takes generated source and emits a page image.
3. Web becomes vertical slices. An endpoint lives with the types it uses, not in an `Endpoints/`
   folder that pulls handlers away from their options and view models.
4. `PageService` — 369 lines doing four jobs — becomes `TemplateCompiler`, `PageRenderer` and
   `PageEditor`, each named for what it does.

No behaviour change is intended beyond the one the idea asks for: page code can no longer name
anything in `Core`, because `Core` is no longer what a page compiles against.

## Decisions

### D1 — `uk.osric.asp.Sdk` is the page contract, and it is exactly five types

| Type | Why a page needs it |
| --- | --- |
| `IContext` | the whole seam: request in, response out |
| `PageRequest` | returned by `IContext.Request` |
| `PageResponse` | the shape `IContext` collects; keeps the contract whole |
| `IHandler` | the entry point every generated page implements |
| `UserFacingException` | the only exception whose message a page may show |

`PageResponse` is not *reachable* from page code — a page never names it — but it is part of the page
contract and costs a tiny record with no dependencies. The [AOT spike](aot-page-binary-spike.md)
independently found this exact five-type closure when it measured a "Mini" driver, which is good
evidence that the line is in the right place.

The assembly is reference-free and flat: one namespace, so the policy's "everything in the SDK is
allowed" rule and the namespace are the same set. It has no test project of its own; it has no
behaviour to test, and every behaviour it takes part in is already pinned by `SimpleContextTests`,
`ErrorEndpointTests`, `PageScopeTests` and `PageLoadContextTests` (see [D12](#d12--no-fifth-test-suite)).

### D2 — `Core` stays reference-free

`Core` does not reference the SDK. Nothing in it needs an SDK type: the generator emits the
namespace as text, and the policy compares strings. Keeping the documented "references nothing"
property is worth more than a compile-time binding for one string.

That leaves the SDK's namespace written twice, in `TemplateSourceGenerator` (the generated
`using`) and `PageApiPolicy` (`SdkNamespace`). Both are private constants with a comment pointing at
the other, and `PageScopeTests.SdkTypesAreStillReferenced` fails the moment either drifts: a template
that uses `IContext` stops compiling, or starts reporting `PAGE001`.

### D3 — `Core` becomes three slices

| Namespace | Holds | Used by |
| --- | --- | --- |
| `uk.osric.asp.Core.Compilation` | the template language (`TemplateTokenizer`, `TemplateSourceGenerator`, `TemplateLines`, `TemplateParseResult`, `Token`, `TokenType`, `TokenizeResult`), template space (`TemplatePosition`), diagnostics (`PageDiagnostic`, `PageDiagnosticSeverity`), the page policy (`PageApiPolicy`, `ApiReference`) and `ContentFingerprint` | Web (compile path), Runtime (policy walk) |
| `uk.osric.asp.Core.Charting` | `ChartAxis`, `ChartPoint`, `ChartScale`, `ChartSeries`, `ChartSize`, `ChartTick`, `Heatmap`, `HeatmapRow`, `LineChart`, `NiceTicks`, `SvgText`, `ValueUnit` | Web (the `/status` dashboard) |
| `uk.osric.asp.Core.Pages` | `PagePath` | Web |

`ContentFingerprint` goes with compilation because its only job is deciding whether a stored image
matches the source it was built from.

### D4 — `RuntimeId` includes both module version ids

Today `RuntimeId` is `{format}:{policy}:{Core mvid}`, and `Core`'s mvid happens to cover both the
page contract and the generator. After the split they are different assemblies, and only tracking the
SDK would be wrong: a change to `TemplateSourceGenerator` changes the generated source without
changing anything a page binds against, and would leave every stored image looking current.

```text
{FormatVersion}:{PolicyVersion}:{SdkMvid}:{CoreMvid}
```

- `FormatVersion` goes from `2` to `3`, because the shape of the value changes.
- `PolicyVersion` stays `1`; the policy does not tighten in this job, it moves.
- `SdkMvid` is the contract: `typeof(IHandler).Assembly.ManifestModule.ModuleVersionId`.
- `CoreMvid` is the generator and diagnostics: `typeof(PageDiagnostic).Assembly.ManifestModule.ModuleVersionId`.

The cost is the status quo: any `Core` change — even a chart tweak — recompiles every page on its
next request. That is already true today, and `Core` is small. Precision can come later if a release
is ever dominated by recompiles.

### D5 — `PageLoadContext` refuses `Core`

This is the half of the idea that makes the split structural rather than cosmetic. Today the load
context deliberately lets `Core` through because it is the SDK; after the split it is host support, so
it joins `Runtime` and `Web` in the denied list. `Sdk` resolves through the default context, which is
what keeps the `IContext` type identity the host uses and the one the page binds to identical.

A page compiled before this change that named a `Core` helper no longer compiles (`CS0234`, because
the reference set no longer contains `Core`). That is the intended behaviour change. The `RuntimeId`
change means such an image is recompiled before it is run, so in practice the author sees a compile
error in the editor rather than a load failure on a request.

### D6 — Runtime names follow the vocabulary

| Today | Becomes | Why |
| --- | --- | --- |
| `ITemplateCompiler` | `IPageCompiler` | its input is generated page source, not a template |
| `TemplateCompiler` | `PageCompiler` | ditto; it produces a page image |
| `CompiledTemplate` | `CompiledPage` | it *is* the compiled page |
| `CompileResult` | `PageCompileResult` | with two compile steps in the tree, the extra word says which one produced it |
| `PageReferenceSet.ForRuntime()` | `PageReferenceSet.ForPages()` | it is the reference set a page is compiled against |

The name `TemplateCompiler` is then free for the Web-side class that really does compile a template
(see [D8](#d8--pageservice-becomes-three-classes)).

### D7 — Web is vertical slices, and `src` folders are namespaces

| Namespace | Holds |
| --- | --- |
| `uk.osric.asp.Web.Pages` | the page feature: `PageEndpoint`, `PageRenderer`, `TemplateCompiler`, `SimpleContext`, `PageRequestSnapshot`, `IPageStore`, `StoredPage`, `PageCompilation`, `RenderOptions`, `CompileOptions`, `RenderResult`, `RenderStatus`, `ReservedPaths`, `PageBudget`, `PageHealth`, `PageMetrics` |
| `uk.osric.asp.Web.Editor` | `EditorEndpoint`, `PageEditor` |
| `uk.osric.asp.Web.Authentication` | `LoginEndpoint` plus the existing policy, rate-limit and extension types |
| `uk.osric.asp.Web.Errors` | `ErrorEndpoint` |
| `uk.osric.asp.Web.Metrics` | `StatusEndpoint` plus the dashboard, source and options |
| `uk.osric.asp.Web.Health` | the health checks and `HealthReportWriter` |
| `uk.osric.asp.Web.Storage` | `SqlitePageStore`, `StorageDbContext`, `StoredFile`, migrations |
| `uk.osric.asp.Web.Users` | the account slice |
| `uk.osric.asp.Web.Html` | `PageLayout` (was `Framework`): the shared document shell |
| `uk.osric.asp.Web.Http` | `NotFound`, `Redirects`: shared response helpers |

`Endpoints/` disappears. `Program.cs` is still the one place the route table lives, which is what
makes routing reviewable in one screen.

`Web.Tests` keeps one flat namespace (`uk.osric.asp.Web.Tests`). It is a test project, not a product
surface, and its fixtures reach across slices by design (a fake store is used by both renderer and
editor tests). Folders there are optional and can come later.

### D8 — `PageService` becomes three classes

| Class | Namespace | Owns |
| --- | --- | --- |
| `TemplateCompiler` | `Web.Pages` | template → `PageCompilation`: source generation, compile slot, compile deadline, `PAGE003`/`PAGE004`, `page.compile.duration`, `page.image.size`, compiler-fault logging, content hash and runtime id |
| `PageRenderer` | `Web.Pages` | request → response: render slot and deadline, cache lookup and staleness, lazy compile-and-store, loading and invoking, abandoning, `page.render.*`, `page.handler.*`, `page.cache.*`, health |
| `PageEditor` | `Web.Editor` | the editor's needs: read a page, save one, `page.save.duration` |

Method allocation is exact: `GetAsync`/`SaveAsync` → `PageEditor`; `RenderAsync`/`LoadAsync`/
`RunAsync`/`Abandon`/`StalenessReason` → `PageRenderer`; `Compile` → `TemplateCompiler`;
`IsCurrent` → `PageCompilation.IsCurrent(contentHash, runtimeId)`, because it is a pure rule over the
record and both the renderer and a test want to ask it.

`PageRenderer` keeps two collaborators with "compiler" in the name, and that is the point of the
vocabulary:

```csharp
internal sealed class PageRenderer(
    IPageStore store,
    IPageCompiler compiler,      // loads an image, and says which build it is
    TemplateCompiler templates,  // turns a template into a stored compilation
    PageMetrics metrics, PageBudget budget, PageHealth health,
    ILogger<PageRenderer> logger)
```

`TemplateCompiler` uses `PageBudget.CompileSlots` and `CompileOptions`; `PageRenderer` uses
`RenderSlots` and `RenderOptions`. `PageMetrics`, `PageBudget` and `PageHealth` stay singletons;
`TemplateCompiler`, `PageRenderer`, `PageEditor` and `IPageStore` are scoped, because the store is.

### D9 — `IStorage` is `IPageStore`

It has only ever stored pages and their images (`GetPageAsync`, `SavePageAsync`,
`TrySetCompilationAsync`). `SqliteStorage` becomes `SqlitePageStore`, and the in-memory test double
`FakeStorage` becomes `FakePageStore`.

### D10 — The SDK is the version boundary, not a semver

There is no `[AssemblyVersion]` to bump. The module version id in `RuntimeId` is what invalidates a
stored image, which is the only versioning the product needs. The assembly is the contract: any
change to it invalidates every stored image.

### D11 — `PageLayout` and `PageEndpoint`

`Framework` is not a framework, and `MainEndpoint` serves neither "main" nor "the main page". They
become `PageLayout` (`Header`, `Footer`, `Diagnostics`) and `PageEndpoint`. `GetHeader`/`GetFooter`
drop the `Get` prefix — the methods return markup, they do not read state.

### D12 — No fifth test suite

`testing.md` lists four suites, and a suite exists to prove behaviour. The SDK has none: it is five
declarations with documentation. Adding `uk.osric.asp.Sdk.Tests` would create a project whose only
tests restate the declarations. The behaviour that depends on the SDK is already asserted where it
happens:

- `SimpleContextTests` — the only `IContext` implementation;
- `ErrorEndpointTests` — the only consumer of `UserFacingException`;
- `PageScopeTests` — a page can name SDK types and cannot name anything else;
- `PageLoadContextTests` — the SDK resolves through the default context;
- `PageReferenceSetTests` — the set the pages are built on.

## Target tree

```text
src/
  uk.osric.asp.Sdk/
    IContext.cs  IHandler.cs  PageRequest.cs  PageResponse.cs  UserFacingException.cs

  uk.osric.asp.Core/
    Compilation/
      ApiReference.cs  ContentFingerprint.cs  PageApiPolicy.cs
      PageDiagnostic.cs  PageDiagnosticSeverity.cs  TemplatePosition.cs
      TemplateLines.cs  TemplateTokenizer.cs  TemplateSourceGenerator.cs  TemplateParseResult.cs
      Token.cs  TokenType.cs  TokenizeResult.cs
    Charting/
      ChartAxis.cs  ChartPoint.cs  ChartScale.cs  ChartSeries.cs  ChartSize.cs  ChartTick.cs
      Heatmap.cs  HeatmapRow.cs  LineChart.cs  NiceTicks.cs  SvgText.cs  ValueUnit.cs
    Pages/
      PagePath.cs

  uk.osric.asp.Runtime/
    CompiledPage.cs  IPageCompiler.cs  PageCompileResult.cs  PageCompiler.cs
    PageApiChecker.cs  PageLoadContext.cs  PageReferenceSet.cs

  uk.osric.asp.Web/
    Program.cs
    Html/PageLayout.cs
    Http/NotFound.cs  Http/Redirects.cs
    Pages/  (as D7, plus PageEndpoint.cs, PageRenderer.cs, TemplateCompiler.cs)
    Editor/EditorEndpoint.cs  Editor/PageEditor.cs
    Authentication/  Errors/  Health/  Metrics/  Storage/  Users/

test/
  uk.osric.asp.Core.Tests/        uk.osric.asp.Runtime.Tests/
  uk.osric.asp.Web.Tests/         uk.osric.asp.Web.IntegrationTests/
```

`uk.osric.asp.Runtime` keeps its namespace and its flat file layout: seven files that are all about
one thing, and a `Runtime/Pages/` folder would add a namespace that says no more than the project
name already does. Revisit only if it grows.

## Where each `Core` type goes

| Type | New namespace |
| --- | --- |
| `IContext`, `IHandler`, `PageRequest`, `PageResponse`, `UserFacingException` | `uk.osric.asp.Sdk` |
| `TemplateTokenizer`, `TemplateSourceGenerator`, `TemplateLines`, `TemplateParseResult`, `Token`, `TokenType`, `TokenizeResult`, `TemplatePosition` | `uk.osric.asp.Core.Compilation` |
| `PageDiagnostic`, `PageDiagnosticSeverity`, `PageApiPolicy`, `ApiReference`, `ContentFingerprint` | `uk.osric.asp.Core.Compilation` |
| `ChartAxis`, `ChartPoint`, `ChartScale`, `ChartSeries`, `ChartSize`, `ChartTick`, `Heatmap`, `HeatmapRow`, `LineChart`, `NiceTicks`, `SvgText`, `ValueUnit` | `uk.osric.asp.Core.Charting` |
| `PagePath` | `uk.osric.asp.Core.Pages` |

`ApiReference` gets its own file. It is a value type with its own documentation, and it was only in
`PageApiPolicy.cs` because the policy was written first.

## Phases

Each phase ends green (`make check`) and is committed on its own, so the split — which the idea asks
for in one commit — is not buried under renames.

### Phase 0 — Baseline

- `make check` green; record the test counts above.
- No code change.

### Phase 1 — Names that follow the vocabulary

Mechanical, behaviour-free, and first so the split is reviewed against a stable tree.

1. `src/uk.osric.asp.Runtime/ITemplateCompiler.cs` → `IPageCompiler.cs`; interface `IPageCompiler`.
2. `src/uk.osric.asp.Runtime/TemplateCompiler.cs` → `PageCompiler.cs`; class `PageCompiler`.
3. `src/uk.osric.asp.Runtime/CompiledTemplate.cs` → `CompiledPage.cs`; class `CompiledPage`,
   including its internal constructor and `Dispose` documentation.
4. `src/uk.osric.asp.Runtime/CompileResult.cs` → `PageCompileResult.cs`; record `PageCompileResult`
   (and its `Success` property) through `IPageCompiler`, the Web compile path and every test that
   names it.
5. `PageReferenceSet.ForRuntime()` → `ForPages()`; update the remarks to say what it is.
6. `TemplateSourceGenerator.GenerateTemplateSource(string)` → `Generate(string)`.
7. `NiceTicks.Time`/`Value` → `ForTime`/`ForValue`, so the two table builders read as verbs.
8. Fix every reference: `PageService`, `Program.cs`, the recording compilers in both test projects,
   `PageScopeTests`, `MainEndpointTests`, `TemplateCompilerTests` → `PageCompilerTests`, and any XML
   `<see cref>` or prose that names the old types.
9. Update prose that names the old types in `docs/architecture.md`, `docs/decisions.md` (DEC-26),
   `README.md`, and the job files in `docs/jobs/` that point at them.

Verify: `make check`. The test count must not change.

### Phase 2 — Split out `uk.osric.asp.Sdk`

One commit, because a half-split namespace is worse than either state.

1. Add `src/uk.osric.asp.Sdk/uk.osric.asp.Sdk.csproj`: `Microsoft.NET.Sdk`, SPDX header, no
   references, no packages.
2. Add it to `uk.osric.asp.slnx` under `/src/`.
3. Move the five contract files into it and change their namespace to `uk.osric.asp.Sdk`, carrying
   the SPDX tags and the XML docs unchanged.
4. Add `ProjectReference` to the SDK in `Runtime`, `Web`, `Runtime.Tests` and `Web.Tests`. Do **not**
   add it to `Core` (D2). `Web.IntegrationTests` reaches it through `Web`; `Core.Tests` names no SDK
   type, so it needs no reference and its "no project but the core" property survives.
5. `TemplateSourceGenerator`: emit `using uk.osric.asp.Sdk;` in the prelude.
6. `PageApiPolicy`: `SdkNamespace` becomes `"uk.osric.asp.Sdk"`, with a comment naming
   `TemplateSourceGenerator` as the other copy.
7. `PageLoadContext`: add `uk.osric.asp.Core` to the denied prefixes; update the remarks to say the
   SDK is what a page binds against and `Core` is host support.
8. `PageCompiler.RuntimeId`: `FormatVersion` → `"3"`, append `{CoreMvid}` after `{SdkMvid}`, and
   update both version comments to say what invalidates an image now.
9. Update `using uk.osric.asp.Core;` where the type is now in the SDK, and add
   `using uk.osric.asp.Sdk;` where a file names SDK types.
10. Tests:
    - `Core.Tests/PageApiPolicyTests`: `("uk.osric.asp.Sdk", "IContext", "Write", true)`; add a case
      that a `Core` namespace is now rejected (update its spelling to `Core.Charting` in Phase 3).
    - `Core.Tests/TemplateSourceGeneratorTests`: assert `using uk.osric.asp.Sdk;`.
    - `Runtime.Tests/PageLoadContextTests`: add `uk.osric.asp.Core` to the refused cases.
    - `Runtime.Tests/PageReferenceSetTests`: assert no `uk.osric.asp.Core.dll` in the set and that
      the SDK is present.
    - `Web.Tests/PageScopeTests`: add a `uk.osric.asp.Core` template that must fail with `CS02xx`,
      and keep `SdkTypesAreStillReferenced`.
11. Docs: `architecture.md` project list, dependency direction, "Where does code go?", policy and
    load-isolation paragraphs, and the `RuntimeId` sentence; `testing.md` project table and reference
    graph; `README.md`; `deploy/README.md` (the deploy that recompiles every page now names the SDK).

Verify: `make check`. Expect 2–4 new runtime/web assertions, no changed behaviour.

### Phase 3 — Slices: folders become namespaces

Moves only, plus the names that go with the move.

1. `Core`: create `Compilation/`, `Charting/`, `Pages/`; move the files per the table; change the
   namespace in each to match the folder.
2. `Web`: create `Html/`, `Http/`; move `MainEndpoint`→`Pages/PageEndpoint.cs`,
   `EditorEndpoint`→`Editor/`, `LoginEndpoint`→`Authentication/`, `ErrorEndpoint`→`Errors/`,
   `StatusEndpoint`→`Metrics/`; move `SimpleContext`, `PageRequestSnapshot`, `PageCompilation`,
   `StoredPage`, `IStorage`(→`IPageStore`) into `Pages/`. Delete `Endpoints/`.
3. Rename in the same commit, because the move already touches the file:
   `Framework` → `PageLayout` (`Header`, `Footer`, `Diagnostics`), `MainEndpoint` → `PageEndpoint`,
   `IStorage` → `IPageStore`, `SqliteStorage` → `SqlitePageStore`.
4. Update every `using`; the compiler finds them all.
5. Delete the dead `PageApiPolicy.AllowedNamespaces` and `AllowedTypes`: `internal`, no caller, no
   test. If the editor ever needs the list for completion, it can come back with the shape the
   editor wants.
6. Remove the `InternalsVisibleTo` grants in `Core` for `uk.osric.asp.Runtime.Tests` and
   `uk.osric.asp.Web.Tests`. Neither names a `Core` internal; only `Core.Tests` uses `Token`,
   `TokenizeResult` and `TemplateLines`. Keep `Runtime`'s grant to `Web.Tests` — `MainEndpointTests`
   reads `PageCompiler.RuntimeId`.
7. `.editorconfig`: add
   ```ini
   [src/**/*.cs]
   dotnet_style_namespace_match_folder = true:warning
   ```
   so the convention is enforced rather than remembered. Scoped to `src` because the test projects
   keep one namespace each.
8. Tests: `FakeStorage`→`FakePageStore`, `SqliteStorageTests`→`SqlitePageStoreTests`; usings; the
   `Core` rejection case in `PageApiPolicyTests` now names the real slice namespace
   (`uk.osric.asp.Core.Charting`).

Verify: `make check`. Test count must not change.

### Phase 4 — `PagePath.Path` becomes `PagePath.Key`

The docs already call the value "the canonical key"; `path.Path` reads badly. It is wide and purely
mechanical, so it gets its own commit and its own review:

1. Rename the property on `PagePath`; keep the private field and the record semantics unchanged.
2. Update every reader — the page and editor endpoints, the page services, storage, the tests and the
   XML docs. Every one of them is a `path.Path` that becomes `path.Key`.
3. Do **not** rename `PageRequest.Path`: it is the page key as a page sees it, and the page contract
   is not part of this tidy-up.

Verify: `make check`. Test count must not change.

### Phase 5 — `PageService` becomes `TemplateCompiler`, `PageRenderer`, `PageEditor`

1. Add `src/uk.osric.asp.Web/Pages/TemplateCompiler.cs` with `Compile(...)`, moved from
   `PageService.Compile` and now calling `TemplateSourceGenerator.Generate(...)` (Phase 1's rename).
   It throws on a compiler fault exactly as today; the caller decides what to do with the source.
   Its remark names the instruments it records: `page.compile.duration` and `page.image.size`.
2. Add `src/uk.osric.asp.Web/Pages/PageRenderer.cs` with `RenderAsync`, `LoadAsync`, `RunAsync`,
   `Abandon`, `StalenessReason`, and the private `LoadedPage` record; its remark names
   `page.render.*`, `page.handler.*` and `page.cache.*`.
3. Move `PageService.IsCurrent` onto `PageCompilation` as
   `IsCurrent(contentHash, runtimeId)`; the renderer calls it and the rule gets its own test.
4. Add `src/uk.osric.asp.Web/Editor/PageEditor.cs` with `GetAsync` and `SaveAsync`; `SaveAsync`
   still stores the source without a compilation when the compiler faults, so the author's edit
   survives. Its remark names `page.save.duration`.
5. Delete `Pages/PageService.cs`.
6. `Program.cs`: register `TemplateCompiler`, `PageRenderer` and `PageEditor` as scoped, keep
   `IPageCompiler` and the shared budget/health/metrics singletons.
7. Tests — this is the largest mechanical part:
   - `PageServiceTests` splits into `PageEditorTests` (save/get, keep-the-source-on-fault, warnings,
     parse errors), `PageRendererTests` (cache hits/staleness, lazy compile, budget, abandon, body
     cap) and `TemplateCompilerTests` (compile slot and compile deadline diagnostics).
   - `PageMetricsTests`, `MainEndpointTests` and `EditorEndpointTests` construct the class they
     exercise instead of one `PageService`.
   - `PageScopeTests` and the integration fixtures only need the type names.
   - `PageCompilationTests` pins `IsCurrent` for matching content, changed content and a changed
     runtime id.
8. Keep every test's name and assertion; a test that changes meaning is a bug in the split.

Verify: `make check`; the counts should be unchanged (fixtures renamed, not tests deleted), plus the
new `IsCurrent` cases.

### Phase 6 — Docs, decisions and job bookkeeping

- `docs/architecture.md`: the new project and graph, the slices, `PageLoadContext` now refusing
  `Core`, the new `RuntimeId`, and the note that a page-compiled-before-the-split is recompiled.
- `docs/testing.md`: the project table (SDK has no suite — say why), the reference graph, and the
  `PageScopeTests` sentence about naming shell types.
- `docs/decisions.md`: update DEC-10 (dependency direction), DEC-26 (`RuntimeId`), DEC-35 (policy
  namespace), DEC-39 (load context), and add decisions for the SDK boundary, the
  Template/Page vocabulary and the folder-is-namespace rule.
- `README.md` and `deploy/README.md`: the project sentence and the recompile-on-release sentence.
- `docs/jobs/IDEA-004.md`: delete it and drop its row from `docs/jobs/README.md`, as closed jobs have
  been handled before.
- `docs/jobs/*.md` and `docs/plans/*.md`: fix every link and path that now points at a moved file
  (ARCH-11, ARCH-12, ARCH-13, IDEA-002, IDEA-003, IDEA-005, and the `PagePath`/`PageApiPolicy` links
  in `decisions.md`). Historical prose stays historical; broken links do not.
- Mark this plan's status.

Final verify: `make check`, then `make ci` once (clean tree, reproducibility).

## Tests that prove the split

These are the assertions that turn "we moved some files" into "the page cannot reach the host":

| Test | Asserts |
| --- | --- |
| `PageScopeTests` (new `Core` case) | naming `uk.osric.asp.Core.*` from a page fails with `CS02xx`, not `PAGE001` |
| `PageScopeTests.SdkTypesAreStillReferenced` | a page can still use `IContext`, `PageRequest` and friends |
| `PageLoadContextTests` (new `Core` case) | a page cannot load `uk.osric.asp.Core` by name even if it tries |
| `PageLoadContextTests.TheSdkResolvesThroughTheDefaultContext` | the host and the page share one `IContext` type identity |
| `PageReferenceSetTests` (new cases) | the SDK is referenced; `Core`, `Runtime` and `Web` are not |
| `PageApiPolicyTests` (both projects) | the policy allows the SDK namespace and denies `Core` |

## Risks and what to do about them

- **Every stored image is invalidated** by the `RuntimeId` change. That is expected and is the
  existing release behaviour: one compile per page, on its first visit. `ContentHash` still protects
  against stale content, and the integration suite already covers lazy compile-and-store.
- **Pages that legally used `Core` helpers stop compiling.** `PageApiPolicy` allows the whole
  `uk.osric.asp.Core` namespace today, so this is a real (if unlikely) capability removal. It is the
  point of the idea; the editor will show a `CS0234`, and the fix is to write the code in the page.
- **The `TemplateCompiler` name moves between assemblies.** Phase 1 frees it from Runtime before
  Phase 5 uses it in Web, so no commit ever has two types with the same name in scope.
- **`PageServiceTests` is the one place a refactor can quietly weaken coverage.** Keep the test
  names and assertions; split the fixture, not the behaviour.
- **`dotnet format` and namespace-match warnings** can flood a commit if Phase 3 misses a file.
  Because warnings are errors, the build stops at the first mismatch, which is what we want; do the
  moves with `git mv` so history follows.

## Additional changes, agreed 2026-11-03

Each is folded into a phase above rather than left as a suggestion.

| # | Change | Lands in |
| --- | --- | --- |
| 1 | Delete the dead `PageApiPolicy.AllowedNamespaces`/`AllowedTypes` | Phase 3 |
| 2 | `PagePath.Path` → `PagePath.Key` | Phase 4 |
| 3 | Leave `StoredFile` alone | recorded, no work |
| 4 | Drop the unused `InternalsVisibleTo` grants in `Core` | Phase 3 |
| 5 | `PageCompilation.IsCurrent(contentHash, runtimeId)` | Phase 5 |
| 6 | Say which `page.*` instruments each new class records | Phase 5 |
| 7 | `CompileResult` → `PageCompileResult` | Phase 1 |

On 3: `StoredFile` is an EF-mapped entity and its type name is embedded in the migration snapshot.
Renaming it means a no-op migration and a hand-edited snapshot for no functional gain; it is a
storage implementation detail, and the port (`IPageStore`) is where the good name lives.

On 6: this is documentation, not behaviour. Each new class gets one remark sentence naming the
instruments it records, so a reader asking "where is `page.compile.duration` recorded?" does not have
to grep. The meter is one instrument set; the recording site is what should be obvious.

On 7: the extra word earns its place because there are two compile steps — `TemplateCompiler`
(template → `PageCompilation`) and `PageCompiler` (source → image). `PageCompileResult` says which
one produced it.

## Rejected alternatives

- **A third assembly for the compiler support** (`uk.osric.asp.Compiler`). It would give `Core` a
  single identity again, but it adds a project, a reference edge and a test suite for fourteen files
  that are already pure and already tested. `Core` plus slices is enough.
- **`uk.osric.asp.PageSdk`.** Rejected: `Sdk` is the vocabulary the idea and the architecture
  already use, and `PageSdk` invites a `Page` prefix on every type inside it.
- **Making `Core` reference `Sdk` to bind the namespace.** Rejected in D2: one string is not worth
  giving up "the core references nothing", and the existing compile test covers the drift.
- **Putting `PageDiagnostic` and `TemplatePosition` in the SDK.** A page never sees a diagnostic;
  they are the shell's view of a compile. They stay in `Core.Compilation`.
- **Keeping `IStorage`.** It only ever stored pages. A name that could mean files, users or settings
  makes the next reader check.

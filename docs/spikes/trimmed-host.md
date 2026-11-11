<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Spike: trimming the host

Measured on 2026-11-11 on the dev box (linux-x64, SDK 11.0.100, trunk `961dfdf`, `runtime-async`
on) against an otherwise idle site. Every configuration below is a `Release` publish of
`uk.osric.asp.Web` with `PublishTrimmed=true`, a fixed RID (`linux-x64` for the runs,
`linux-musl-arm64` for the deploy-sized pair) and
`TreatWarningsAsErrors=false`, since the first `IL2026` would otherwise stop the build. Trimming
forbids the shared framework, so every trimmed configuration is self-contained and carries the
runtime in its own output.

**Verdict: trimming saves about a third of the process's memory (266 → 174 MB) and does not pay for
itself.** A working host needs `System.Private.CoreLib`, `Microsoft.EntityFrameworkCore` and the
migrations assembly rooted, plus reflection-based `System.Text.Json` re-enabled — which is to say
that the things trimming would have shrunk are the things that had to be kept, and the
page compiler still compiles against a reference set that shrinks with every trim. It also cannot
share the base image's framework, so the release moves 58 MB instead of the current 26.7 MiB layer.

This matters beyond size: it is the first measurement of trimming on this codebase, and it replaces
the one-line claim in [DEC-60](../decisions.md) and
[operations](../../deploy/operations.md#image-layout-and-size)
("dies at startup with `TypeLoadException`")
with the actual chain, which is neither that exception nor one failure.

## Question

Would publishing trimmed — without going as far as NativeAOT — cut the image or the memory enough
to be worth the work? The [AOT spike](aot-page-binary-spike.md) answered a different question (a
process per render) and left this one inherited from DEC-60. Trimming is worth its own record
because it is separable from AOT: a trimmed app still JITs, so the page compiler and the load
context keep working, and only AOT takes those away.

## Hypotheses

| # | Hypothesis | Threshold | Result |
| --- | --- | --- | --- |
| H1 | A trimmed host starts and serves pages | works | **fail** — needs three roots and a JSON switch, and the third root exists only because of the page compiler |
| H2 | Trimming cuts the deployed bytes | smaller than 26.7 MiB | **fail** — 54–58 MB self-contained, 106 MB with ReadyToRun |
| H3 | Trimming cuts resident memory | ≥ 25% | **pass** — 266 → 174 MB with pages compiling |
| H4 | Trimming keeps startup | ≤ untrimmed | **fail** — 1.07 → 1.77 s; **pass with R2R** at 0.71 s but 106 MB |
| H5 | The trim warnings point at what will break | all | **fail** — 78 warnings, none of which is the first thing that breaks |

## Method

A throwaway worktree based on trunk `961dfdf`; nothing was changed on trunk, and the worktree is
gone — the roots it added are at the end of this record. Each build was published to `/tmp` and run
against a copy of one database that the *untrimmed* build had migrated, seeded and rendered a page
into, so migration and seeding do not appear in the trimmed runs. `/heath` is the readiness probe and `/about` the page that has to
compile, render and be stored; both are timed from process start, and RSS is read from
`/proc/<pid>/status` after `/about` has answered.

Two things worth writing down because each cost a run:

- **A page image is keyed by the page contract's module versions, not by the host.** The runtime id
  is the `ModuleVersionId` of `Sdk` and of `Core`'s `PageDiagnostic` (see
  [PageCompiler.cs:45](../../src/uk.osric.asp.Runtime/PageCompiler.cs)), so two builds that trim
  those the same way serve each other's images. One configuration appeared to compile pages for a
  while only because an image built by a *differently* trimmed host was still current; forcing the
  template to change produced the `CS0656` below. Any trimmed deployment would hide this class of
  failure on every page that has not been edited since.
- **Publishing without an explicit `-r`** produced a build that started, logged nothing and never
  listened. `make image` already passes `-r $(CONTAINER_RID)`; the trimmed configuration was only
  reproducible with the RID stated.

## What breaks, in order

| Configuration | Warnings | Output | Starts | `/heath` | `/about` |
| --- | --- | --- | --- | --- | --- |
| framework-dependent (control, as deployed) | — | 27 MB of files | yes | 200, 1.07 s | 200 |
| `PublishTrimmed`, `TrimMode=full` | 78 | 54 MB | **no** | — | — |
| + EF/Sdk roots, migrations kept, JSON switch | 94 | 56 MB | yes, 1.73 s | 200 | **500** (compile) |
| + `System.Private.CoreLib` rooted | 89 | 58 MB | yes, 1.77 s | 200 | **200** (1.25 s cold, 6 ms cached) |
| + `PublishReadyToRun` | — | 106 MB | yes, 0.71 s | 200 | not exercised |
| `TrimMode=partial` | 193 | 66 MB | yes, 2.53 s† | 200 | **500** (compile) |

† On a fresh database, so migration and seeding are in that number; every other trimmed row ran
against the pre-migrated copy.

Sizes are the raw files `podman build` packs into a layer (the 26.7 MiB in
[operations](../../deploy/operations.md#image-layout-and-size) is the same number for the deployed
`linux-musl-arm64` build, which measures 27 MB here). The deploy-shaped pair is unambiguous: 27 MB
framework-dependent against 55 MB trimmed and self-contained, because the ~164 MB runtime leaves the
base image's cached layers and joins the layer that is pushed on every release. The intermediate
rootings — EF, then the migrations — were measured on the pre-`runtime-async` trunk and failed
with the same exceptions they fail with here; they are EF and ILLink behaviour, not async.

1. **EF model validation.** Not a `TypeLoadException`:
   `MissingMethodException: Constructor on type 'Microsoft.EntityFrameworkCore.ChangeTracking.EntryCurrentValueComparer`1[[System.Int64]]' not found`,
   from `ModelValidator.ValidateTypeMapping` → `CurrentValueComparerFactory.Create` →
   `Activator.CreateInstance`. The trimmer removed the constructor EF reaches by reflection.
2. **Migration discovery.** Once EF builds a model:
   `InvalidOperationException: ... MigrationsNotFound: No migrations were found in assembly 'uk.osric.asp.Web'`.
   The migrations are only reachable from `MigrationsAssembly`'s reflection over the assembly, so
   every migration class and its `Designer` are trimmed away.
3. **Migration execution.** Once the migrations are retained, generating the up-SQL throws
   `NullReferenceException` inside `RelationalSqlGenerationHelper.EscapeIdentifier`. This only
   happens when a migration actually has to run — against an up-to-date database the step is
   skipped, which is why rooting EF and then the migrations looked like enough.
4. **`System.Text.Json`.** With the app up, every JSON path throws
   `InvalidOperationException: Reflection-based serialization has been disabled`: `/heath` at
   [ReportWriter.cs:44](../../src/uk.osric.asp.Web/Health/ReportWriter.cs), a page read at
   [SqlitePageStore.cs:437](../../src/uk.osric.asp.Web/Pages/SqlitePageStore.cs). The publish switch
   restores it, at the cost of the trim safety it was turned off for.
5. **The page compiler.** With JSON working, pages fail to compile:
   `CS0656: Missing compiler required member 'System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitOnCompleted'`.
   Roslyn compiles a page against the platform assemblies in the trimmed output, and the trimmer
   removed a member the compiler needs for the generated `async Task`. Rooting
   `System.Private.CoreLib` fixes it; that is the only root in the table that is about the compiler
   rather than the host.

`runtime-async` does not help here, because
[PageCompiler.cs:62](../../src/uk.osric.asp.Runtime/PageCompiler.cs) builds its own
`CSharpCompilationOptions` and the `Features` property in [Directory.Build.props](../../Directory.Build.props)
only reaches the product's own compilation. Pages are compiled with the classic async state machine
whatever the host does.

## Memory and startup

RSS from `/proc/<pid>/status` once the host had answered `/about`, so the page compiler and its
reference set are loaded:

| Configuration | RSS | first `/heath` 200 |
| --- | --- | --- |
| untrimmed (compiled `/about`) | 266 MB | 1.07 s |
| trimmed, no corelib root (compile failed) | 159 MB | 1.73 s |
| trimmed + corelib root (compiled `/about`) | 174 MB | 1.77 s |
| trimmed + ReadyToRun (page already current) | 199 MB | **0.71 s** |

The memory saving is real and is the only thing in trimming's favour: 92 MB, a third of the process.
The startup column is trimming's other cost — the shared framework is ReadyToRun-compiled and a
trimmed app is not, so trimming alone makes the host 0.7 s slower to answer; ReadyToRun buys that
back and more, at 106 MB of output.

## Scope: trimming one project instead of the solution

Trimming is a publish-time decision about the published application, not a property of a project:
`Runtime` and `Core` are libraries the entry point pulls in, so "trim Runtime" is not a thing that
can be asked for. The levers that come closest were measured rather than assumed:

- **Framework-dependent is refused.** `--self-contained false -p:PublishTrimmed=true` fails with
  `error NETSDK1102: Optimizing assemblies for size is not supported for the selected publish
  configuration. Please ensure that you are publishing a self-contained app.` So trimming always
  drags the runtime out of the base image and into the layer moved on every release.
- **`TrimMode=partial` is the nearest thing to "only our code".** It trims assemblies that opt in
  with `IsTrimmable` metadata. Our assemblies and most of the BCL and ASP.NET Core opt in; EF and
  Roslyn do not. Result: 66 MB, 193 warnings, and pages still fail with the same `CS0656`, because
  `System.Private.CoreLib` is one of the assemblies that opts in. EF survives, the page compiler
  does not.
- **Per-assembly control exists but does not escape the trap.** `TrimmerRootAssembly` and
  `TrimmerRootDescriptor` decide what to keep; `TrimMode=partial` plus `TrimmableAssembly` decides
  what to shrink. Every working configuration needed corelib, EF, the migrations and JSON kept, so
  the parts that would have shrunk are exactly the parts that could not be.

## The warnings

78 at baseline: 70 `IL2026` (a call into an API marked `RequiresUnreferencedCode`), 4 `IL2104`
rollups — `Microsoft.EntityFrameworkCore`, `.Relational`, `.Sqlite` and `Microsoft.CodeAnalysis`
all report themselves as not trim-safe — and 4 in
[LoadedPage.cs](../../src/uk.osric.asp.Runtime/LoadedPage.cs):
`IL2077`/`IL2080` for `GetMethod` and `Activator.CreateInstance` on a `Type` with no trim
annotations, `IL2026` for `AssemblyLoadContext.LoadFromStream`.

Concentrations are honest about ownership: `SqlitePageStore` 12, `StorageDbContext` 11,
`LoadedPage` 5, then the assistant's JSON writers. None of them predicted the failures above, and
none is a bug while the host is not trimmed:

- The `LoadedPage` warnings are about an assembly that is loaded from bytes and therefore never
  trimmed; the annotations would be there to satisfy an analyzer, not the runtime.
- The assistant's reflection prompt already has a drift test — `PageAuthorPromptTests` asserts the
  reflected `IContext` members all appear — so a trimmed or reordered contract fails a test rather
  than quietly producing a worse prompt.
- `Binder` calls and EF call sites are "this API is not trim-safe", which is a property of the
  platform packages, not of this code.

The one warning with product weight is
[PageStorageScope.cs:41](../../src/uk.osric.asp.Web/Pages/PageStorageScope.cs):
`JsonSerializer.Deserialize<T>` where `T` is chosen by page code. No source-generated context can
cover it, ever, so any move towards trimming or AOT has to change the page-facing storage API to a
closed type (a `JsonElement`, say) — a contract change, not a cleanup.

## What this means

- **Do not trim the host now.** It buys 92 MB of resident memory and costs EF roots, a migrations
  story that has to leave the startup path, reflection JSON back on, a corelib-sized hole in the
  trim, and 31 MB more per release.
- **DEC-60's conclusion stands, its reason does not.** "Trimming dies at startup" is true, but the
  first failure is EF's value comparer, and the page compiler is the last one, not the first.
- **The AOT spike is unaffected.** Trimming still JITs; the page compiler, the collectible load
  context and the reflection invoke all work under a trimmed host once corelib is kept. AOT removes
  the JIT, which removes pages entirely — trimming being feasible-ish makes that no easier.
- **If trimming is ever revisited**, the order is: take migrations out of the startup path, source
  generate the host's JSON, decide what a page may name (and therefore what must never be trimmed),
  then root corelib and keep ReadyToRun for startup.

## Re-check triggers

1. ILLink or Roslyn gains a supported way to compile a page against a fixed reference set — a ref
   pack shipped beside the app — so the page surface stops tracking the trim.
2. EF's trim support reaches the point where the model, the migrations and the SQL generator need no
   roots, which would remove the EF and migrations steps.
3. The deployment stops sharing a framework image (a non-container host, or one where the 164 MB
   runtime is paid per deploy), which is the case where moving a self-contained binary is cheaper
   rather than dearer.
4. Pages stop being C# compiled at runtime. Trimming then has no compiler to break and becomes an
   ordinary size exercise.
5. `runtime-async` becomes the default for Roslyn's `CSharpCompilationOptions`, so a page no longer
   needs `AsyncTaskMethodBuilder.AwaitOnCompleted` and the corelib root stops being about the
   compiler.

## Reproducing

```bash
# From a worktree of trunk, with the experiment's roots in the csproj:
dotnet publish src/uk.osric.asp.Web/uk.osric.asp.Web.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishTrimmed=true -p:TrimMode=full \
  -p:TreatWarningsAsErrors=false -p:JsonSerializerIsReflectionEnabledByDefault=true -o out

# Migrate and seed with an untrimmed build first; the trimmed host cannot do it itself.
dotnet publish src/uk.osric.asp.Web/uk.osric.asp.Web.csproj -c Release -o plain
ASPNETCORE_URLS=http://127.0.0.1:5185 ConnectionStrings__Storage="Data Source=$PWD/data/db" \
  ./plain/uk.osric.asp.Web &  # then curl /about once, and stop it

# Measure: start to /heath 200, then /about, then /proc/<pid>/status VmRSS.
ASPNETCORE_URLS=http://127.0.0.1:5184 ConnectionStrings__Storage="Data Source=$PWD/data/db-copy" \
  ./out/uk.osric.asp.Web
```

The whole of the experiment's change to the build is the roots below, all conditioned on
`PublishTrimmed`, plus a descriptor that keeps the migrations EF finds by reflection:

```xml
<ItemGroup Condition="'$(PublishTrimmed)' == 'true'">
  <TrimmerRootAssembly Include="Microsoft.EntityFrameworkCore" />
  <TrimmerRootAssembly Include="Microsoft.EntityFrameworkCore.Relational" />
  <TrimmerRootAssembly Include="Microsoft.EntityFrameworkCore.Sqlite" />
  <TrimmerRootAssembly Include="Microsoft.Data.Sqlite" />
  <TrimmerRootAssembly Include="uk.osric.asp.Sdk" />
  <TrimmerRootAssembly Include="System.Private.CoreLib" />
  <TrimmerRootDescriptor Include="$(MSBuildProjectDirectory)/trim-roots.xml" />
</ItemGroup>
```

```xml
<!-- trim-roots.xml -->
<linker>
  <assembly fullname="uk.osric.asp.Web">
    <type fullname="uk.osric.asp.Web.Storage.Migrations.*" />
  </assembly>
</linker>
```

None of it is worth carrying while the host is not trimmed.

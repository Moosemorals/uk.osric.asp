<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Page diagnostics and assembly visibility

Status: **implemented** 2026-11-09, in the order the phases below set out. Written 2026-11-08 for
review. Filed as ARCH-14, which is now closed and recorded in [jobs](../../jobs/README.md#closed).

Three things landed differently from the plan as written, and are recorded here rather than left
for a reader to discover:

- **The scaffold is nullable-enabled only inside a mapped region.** The plan's rule is unchanged,
  but a page's own `#nullable disable` wins only within its own block: the generator re-enables each
  region, because a directive inside the region would move the author's positions. A page that wants
  to be oblivious for its whole body writes the directive in each block.
- **The narrowing found sixteen members, not a large surface.** Every declaration in `Core` was made
  `internal` and the compiler named back what another assembly binds; `Runtime` kept its whole
  surface because all of it satisfies an interface or is named by `Web`, and `Web` lost its one
  exported type. The analyzer, not the count, is what holds the rule.
- **The prompt is `page-author-v10`, not `v9`.** `v9` already existed, so moving the version is what
  makes the nullable prose tell two prompts apart.

## The short version

- **Compiled pages get nullable analysis.** `NullableContextOptions.Enable` on the page
  compilation, so `ctx.User` is `PageUser?` in a page the way it is in `Core`.
- **The scaffold is not the page.** The generator wraps every line it wrote itself in
  `#nullable disable` and every region it mapped to the template in `#nullable enable`. The wrapper
  is nullable-oblivious by construction, so a Roslyn warning can only ever be about page code — which
  matters because [mapDiagnostics](../../../src/uk.osric.asp.Runtime/PageCompiler.cs) silently drops
  warnings from unmapped lines.
- **Warnings become ordinary.** A warning never stops an image being emitted, stored or run; the
  editor already lists them, the assistant already receives them, and both already say "warnings".
  What is missing is that nullable gives them something to say.
- **Pages get a state, and the listing shows it.** `Compiled`, `Compiled with warnings`,
  `Compile failed`, `Not compiled`, and `Stale` for an image built by another build. The `/edit`
  listing becomes a table with a **State** column; the assistant's `list_pages` gains a `status`.
- **The assembly surface is tightened and then held.** At type level the rule is already true bar two
  types in `Web`, which become `internal`; at member level nothing has ever been decided, so every
  public member another product assembly does not bind to becomes `internal` too.
  `Microsoft.CodeAnalysis.PublicApiAnalyzers` makes the result a build failure to break, rather than
  a paragraph in `architecture.md`.
- **Validation moves to the boundary and stops there.** Every public entry point on a public type
  validates its arguments; `internal` and `private` members trust their callers; the guards on
  non-public members come out. The total will not fall — the point is where each one sits, not how
  many there are.

## Goals

- A page author sees the same class of mistakes in a page that they see in the product's own code,
  in the template at the line they wrote.
- A page that compiles with warnings is served, is listed, and is visibly not finished.
- The assistant can tell "this page is fine" from "this page compiles but complains", and is told
  that the second one is its work.
- Nothing is `public` that does not have to be, and the build says so when that changes.
- An argument checked at a boundary is checked once, where the unchecked value enters.

## Non-goals

- **Warnings as errors.** A page that warns is runnable on purpose. `TreatWarningsAsErrors` stays on
  for the product and off for pages.
- **A warning budget or a gate on publishing.** No "you may not save with N warnings"; the state is
  information, not a lock.
- **Migrating the owner's pages.** Turning nullable on does not edit a single stored template. Pages
  that now warn are the owner's to fix, and every existing guard idiom still compiles.
- **Nullable for the product assemblies.** Already on everywhere through
  [Directory.Build.props](../../../Directory.Build.props). This plan changes only what a *page* is
  compiled as.
- **A per-page nullable switch.** The directive is not offered in the page API; a page that wants it
  off can already write `#nullable disable` itself, and nothing here takes that away.
- **A new page capability.** No `IContext`, `PageResponse` or `Sdk` change. `Sdk` is untouched.
- **Storing a diagnostic count.** The listing shows the state word, not "n warnings". Cheap to add
  later; it needs a projection the store does not have today.

## What exists today

Checked against the code on 2026-11-08.

- **Pages are nullable-oblivious.** [PageCompiler.Compile](../../../src/uk.osric.asp.Runtime/PageCompiler.cs)
  parses with default options and builds `new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)`,
  so `NullableContextOptions` is `Disable`. The generator emits no `#nullable` directive and
  [TemplateSourceGenerator](../../../src/uk.osric.asp.Core/Compilation/TemplateSourceGenerator.cs)
  emits each `<% %>` block verbatim between `#line` directives.
- **Warnings already exist end to end, without anything to say.** `MapDiagnostics` maps warning and
  error diagnostics alike through `GetMappedLineSpan`; unmapped *errors* are a fatal compiler fault
  and unmapped *warnings* are dropped silently. `CompileResult.Image` is null only when emit failed,
  so **a stored image with diagnostics carries warnings only** — an invariant
  [SqlitePageStore](../../../src/uk.osric.asp.Web/Pages/SqlitePageStore.cs) already relies on when it
  reads a pre-severity row and infers "no image means errors".
- **No warning is produced in practice.** With nullable off, the generator's `#pragma warning disable
  CS1998` suppresses the only one the wrapper used to raise, and page code has little else to trip.
  `JavaScriptValidator` is the only source of warnings today, and it runs in the assistant's
  `check_script`, not in compilation.
- **The editor already shows warnings.** [WriteEditorAsync](../../../src/uk.osric.asp.Web/Editor/EditorEndpoint.cs)
  renders `page.Image.Diagnostics` through `PageLayout.Diagnostics`, which has a `diagnostic-warning`
  class and links each line to its gutter number; the save message already has a "Saved with warnings."
  branch.
- **The assistant already receives them.** `read_page` returns `compileStatus` and a `diagnostics`
  array with `code`, `severity`, `line`, `column` and `message`; `edit_page` returns diagnostics;
  [PageAuthorPrompt](../../../src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs) says "Check compile
  diagnostics after either write and fix **errors**."
- **The listing is a list, not a table.** [WriteListingAsync](../../../src/uk.osric.asp.Web/Editor/EditorEndpoint.cs)
  renders three `<ul role="list">` sections — live, seeded, deleted — with links and a restore form
  per item. Nothing on it says whether the page compiled. `PageHeadSummary` carries `Path` and
  `IsDeleted` and nothing else, and `ListPagesAsync` projects `PageHead` alone.
- **The history view is already a table** with a **State** column (Current) and a **Compilation**
  column (the `PageImageStatus` words), so the vocabulary the listing needs already exists one view
  over.
- **`PageImageStatus` is four strings.** `NotCompiled`, `CompileFailed`, `CompiledWithDiagnostics`,
  `Compiled`, chosen from `(runtimeId, hasImage, hasDiagnostics)`. It is the single source of those
  words, used by the history table, the revision view and the assistant's `compileStatus`.
- **Nothing tells the listing whether an image is stale.** `PageImage.IsCurrent` compares the
  template hash and the `RuntimeId`, and the render path uses it to recompile lazily; the listing
  never asks.
- **Visibility is a documented rule, not an enforced one.**
  [architecture.md's "Visibility"](../../../docs/architecture.md) says nothing is `public` unless
  another product assembly needs it and that every type in `Web` is `internal`. Measured today:

  | Assembly | Types | `public` types | `public` members on public types | `public` members on non-public types |
  | --- | --- | --- | --- | --- |
  | `Core` | 64 | 56 | 232 | 25 |
  | `Runtime` | 9 | 5 | 16 | 3 |
  | `Sdk` | 14 | 14 | 84 | 0 |
  | `Web` | 181 | **2** | 11 | 964 |

  Two violations: `DiagnosticLine` and `NavLink` in `Web/Html` are `public`. Type-level narrowing
  has **no candidates** — every public type in `Core` and `Runtime` is bound from another assembly.
  The work is at member level and inside `Web`.
- **Guards are everywhere, by convention.** 222 `Argument*Exception.ThrowIf*` statements: 40 in
  `Core`, 7 in `Runtime`, 1 in `Sdk`, 174 in `Web`. Fewer than a dozen sit in non-public members.
  There is no `AnalysisMode` or `CA1062` setting in
  [Directory.Build.props](../../../Directory.Build.props), so nothing enforces either the presence or
  the absence.

## What this touches

| Job or decision | What it already says | What this plan does |
| --- | --- | --- |
| ARCH-14 | This job | **Implements it**; the job is closed |
| DEC-31, "Compiled pages" | A warning-only page stores its image and reports "Saved with warnings." | Makes that sentence true in practice by giving the compiler something to warn about, and makes it visible in the listing |
| "Visibility" in [architecture.md](../../../docs/architecture.md) | Nothing is `public` unless another product assembly needs it | Turns the paragraph into a build failure, and narrows the surface it already describes |
| [IDEA-001](../../jobs/IDEA-001.md) | The editor has a gutter and linked diagnostics | The listing gains the state column that makes the run of those links worth clicking |
| [IDEA-015](../../jobs/README.md#closed) | The listing and history are shell views because a page cannot read the store | Unchanged: the listing stays a shell view, and this plan fixes its shape before a page takes it over |
| [SEC-03](../../jobs/SEC-03.md) | Page code has no process boundary | Guards at public entry points are not a boundary and are not presented as one |
| [the shell](../../shell.md#error-responses) | Every failure is a document a person can read | The listing stays a document; the new column is text, not an icon |

## Design

### D1 — Page code is nullable-enabled; the scaffold is nullable-oblivious

`PageCompiler` sets `NullableContextOptions.Enable` on the compilation options, and
`TemplateSourceGenerator` emits the directive that makes the wrapper oblivious regardless:

```
#nullable disable
using uk.osric.asp.Sdk;
#pragma warning disable CS1998
    public class GeneratedPage : uk.osric.asp.Sdk.IHandler {
    public async global::System.Threading.Tasks.Task HandleAsync(uk.osric.asp.Sdk.IContext ctx) {
#nullable enable
#line 4 "page"
   if (ctx.User is not { } user) { return; }
#line default
#nullable disable
ctx.WriteRaw(@"
<p>Signed in as ");
#nullable enable
#line 5 "page"
           ctx.Write(user.Name);
#line default
#nullable disable
...
```

The rule is one sentence: **`#nullable enable` immediately before each `#line` directive that opens
a page region, and `#nullable disable` immediately after each `#line default` that closes one.** The
directive is outside the mapped region, so it does not shift the line and column the author sees —
which it would if it went inside, because `#line` maps every line that follows it until the next
`#line`.

Why not just rely on the compilation option, with the whole file enabled? Because then a warning from
a line the generator wrote is *unmapped*, and `MapDiagnostics` throws on an unmapped error but
silently discards an unmapped warning. A generator bug would become an invisible hiccup instead of a
test failure. Making the scaffold oblivious means the wrapper cannot warn in the first place, and
the claim "a diagnostic you are shown is about your page" holds by construction rather than by
luck. The alternative — a blanket `#pragma warning disable CS86xx` over the scaffold — is a list to
maintain and hides exactly the faults worth seeing.

A page may still write `#nullable disable` or `#nullable enable` in its own code; the last directive
before a line wins.

### D2 — The compiler is the judge; the test is the guarantee

`MapDiagnostics` keeps its behaviour: mapped warnings are reported, unmapped errors are a fatal
compiler fault, unmapped warnings are dropped. Attached to that is the promise D1 buys, held by a
test rather than by runtime policy: **the scaffold the generator writes compiles warning-free under
nullable enable, for every token shape.** If a future Roslyn adds a warning to something the wrapper
does, that test fails in `Runtime.Tests` instead of a page quietly losing a line.

### D3 — A warning never stops a page

The invariant to state and test: **a stored `PageImage` with `Bytes is not null` carries warnings
only.** Emit either succeeds or reports an error, and `CompileResult.Image` is null whenever it did
not. Nothing else changes: the page is served, the `ETag` is unaffected, the render outcome is
`ok`, and the body cap does not care.

### D4 — `PageImageStatus` becomes a state, with the same words

Replace the string constants with `PageImageState`:

| State | Words | When |
| --- | --- | --- |
| `NotCompiled` | Not compiled | no image record |
| `Stale` | Stale | an image from another `RuntimeId` |
| `CompileFailed` | Compile failed | an image record with no assembly |
| `CompiledWithWarnings` | Compiled with warnings | an assembly with diagnostics |
| `Compiled` | Compiled | an assembly with none |

`CompiledWithDiagnostics` is renamed because, given D3, "diagnostics" on a stored image *are*
warnings; the old word was hedging.

One description method takes the runtime id it should judge against:

```csharp
PageImageState Describe(string? runtimeId, bool hasImage, bool hasDiagnostics, string? currentRuntimeId = null);
```

`currentRuntimeId` null means "do not judge staleness", which is what the history table wants: a
retained revision from an earlier build is not *stale*, it is history. The listing passes it; the
assistant's `compileStatus` passes it; the revision view does not.

### D5 — `Stale` is worth having, and this plan is why

An image is stale when its `RuntimeId` differs from the running build, which after any deploy is
every page, until each is next viewed. That is exactly the window this plan creates. A listing that
said "Compiled" for a page whose diagnostics predate nullable would be lying at the one moment the
owner is looking for pages that need attention, so the state is part of the change rather than a
follow-up. Staleness is judged on `RuntimeId` only: comparing template hashes would mean hashing
every template in the listing.

### D6 — One listing record, one query

`IPageStore.ListPagesAsync` returns `PageListing(PagePath Path, bool IsDeleted, PageImageState State)`
instead of `PageHeadSummary`. The query joins the head's current revision the way `ListRevisionsAsync`
already does and projects `RuntimeId`, `Image != null` and `Diagnostics != null && Diagnostics != "[]"`,
then computes the state client-side, because `PageImageState` cannot be translated into SQL. This is
the shape `ListRevisionsAsync` already uses and the reason it is written that way.

`PageHeadSummary` stays as it is. It answers "does this page exist and is it a tombstone", which the
seeder's existence check, the restore path and the editor's "was this deleted?" all ask cheaply, and
none of them wants a join.

The callers of `ListPagesAsync` — the listing, `ExportEndpoint.ListAsync`, `ImportEndpoint` and the
assistant's `list_pages`, `search_pages` and its page walk — take the new type. The two admin views
ignore `State`; the assistant's three are where the status becomes useful. `FakePageStore` follows.

### D7 — The listing is one table, three sections, one State column

`WriteListingAsync` renders live, seeded and deleted through a single `WritePageTableAsync`:

| Page | State | |
| --- | --- | --- |
| `/about` | Compiled | Edit · History · Delete |
| `/blog` | Compiled with warnings | Edit · History · Delete |
| `/borked` | Compile failed | Edit · History · Delete |
| `/site.css` | Compiled | Edit · History · Delete · Restore seed |

The State cell carries the words from D4 with a class per state so `editor.css` can mark the two that
matter. **The seed is content**: an installation that ran before this change keeps the old
`/editor.css` until the owner restores it, so the words must read correctly with no styling at all.

Sorting stays as it is — by key, ordinal — because a table that reorders itself under an `aria-sort`
the shell does not write is a promise it cannot keep. The deleted section gains the same columns so
the page reads as one table rather than three shapes; its actions get a scrub link where a live page
has a delete link.

### D8 — The assistant gets the same words, and is told what they mean

- `list_pages` gains `status` per page, always, so the model can find the pages that need cleaning up
  without reading each one. The list is capped at 500 today and stays capped.
- `read_page`'s `compileStatus` already exists and will say "Compiled with warnings"; no shape change.
- `PageAuthorPrompt` to `page-author-v9`: nullable is on and `ctx.User` is `PageUser?`, so the guard
  forms a local (`is not { } user`) — the example already does this and the prose catches up; a page
  that compiles with warnings is runnable but not finished, and fixing them is part of the job it was
  given; the diagnostics it reads back are the authority.
- The prompt stays a prompt: no new tool, no new argument. A model that ignores the warnings is
  visible in the state column, which is the point of putting it there.

### D9 — The surface is tightened by the compiler, then held by the analyzer

**The method is "make it `internal` and let the compiler find the callers."** `Core` and `Runtime`
publish their internals to their test projects through `InternalsVisibleTo`, so a member made
`internal` still compiles for the tests that use it; only a genuine cross-product-assembly use
fails. That makes the loop exact and mechanical: narrow a member, build, and either it compiles or
the compiler names the one caller that needs it. No grep, no guesswork, and the answer is the
tightest surface that exists rather than an estimate of it.

Order of work:

1. **`Web` first, because it is free.** `DiagnosticLine` and `NavLink` become `internal`; the other
   964 public members are already on internal types and need no change.
2. **`Runtime`** — 16 public members on 5 public types. `IPageCompiler` and `CompileResult` are the
   seam Web uses; `PageCompiler`, `LoadedPage` and `JavaScriptValidator` are the question.
3. **`Core`** — 232 public members on 56 public types. The pure helpers that `Runtime` and `Web`
   bind to stay public; the rest go `internal`.
4. **`Sdk` is not touched.** Every one of its 14 types and 84 members is the contract a stored page
   binds against, and a page is compiled outside the product's assembly boundary.

One gap in the loop: `Core` grants internals to `Core.Tests`, `Runtime.Tests` and `Web.Tests`, but
not to `Web.IntegrationTests`, so an integration test that reaches a `Core` type directly would fail
the build after narrowing. That is the loop working — the answer is another `InternalsVisibleTo`, as
`Core` already has three of — and worth knowing before the first one looks like a mistake.

**Held by the analyzer.** `Microsoft.CodeAnalysis.PublicApiAnalyzers`, pinned in
[Directory.Packages.props](../../../Directory.Packages.props), with `PublicAPI.Shipped.txt` in `Core`,
`Runtime` and `Web`. This is the one new package, and the justification is the one the rules ask
for: it is the only way to make "nothing is `public` unless another assembly needs it" a build
failure rather than a paragraph, it is Microsoft's own, and it turns a 232-entry list into a file the
build maintains. With `TreatWarningsAsErrors` already on, adding public API becomes RS0016 and
removing it RS0017 — both errors.

`Web`'s `PublicAPI.Shipped.txt` is **empty**, which is the whole architecture rule in one file: any
`public` type or member in the application assembly fails the build. `Sdk` gets the analyzer too,
with every symbol declared shipped, so that widening the page contract stays a deliberate edit.

If the analyzer is rejected at review, the fallback is a reflective `PublicApiTests` per assembly
comparing `GetExportedTypes()` and their members against an inline expected array — the same
guarantee, hand-rolled, at the cost of a large literal and no "removed API" detection.

### D10 — Validation belongs at the boundary and stops there

- **Public members of public types in `Core`, `Runtime` and `Sdk`** validate: every reference-type
  parameter with `ArgumentNullException.ThrowIfNull`, every string that must carry something with
  `ArgumentException.ThrowIfNullOrWhiteSpace`, and the numeric ranges that are contractual with
  `ArgumentOutOfRangeException.ThrowIf*`. These are the members a different assembly, a test, or
  reflective code can call with anything.
- **The `Sdk` contract's implementation** — `SimpleContext` and the capabilities in `Web` — validates
  the same way, because a page is compiled outside the product's boundary and can pass `null` to
  anything.
- **Endpoint handlers** validate what they bind from the request: `HandleAsync(HttpContext context, …)`
  and its siblings check their parameters, because ASP.NET is a boundary the compiler does not type.
- **`internal` and `private` members do not validate.** The compiler has checked the call; a guard
  there only fires for a `null!` or a reflective caller, and the roughly dozen that exist today come
  out. Members that a test reaches through `InternalsVisibleTo` are not a reason to keep one.
- **The one exception is a DI-injected constructor.** The container is the only caller and does not
  pass null, so a guard there is dead code. Guards stay where a constructor is *also* reached with
  data the container did not supply.

The rule is written down in [architecture.md](../../../docs/architecture.md) beside "Visibility", so the
next reviewer does not have to re-derive it.

### D11 — `FormatVersion` moves, once

`PageCompiler.FormatVersion` goes from `"5"` to `"6"`. Its own comment says to bump it when "the
compiler options" change, and `NullableContextOptions` is a compiler option. In practice `RuntimeId`
already moves with every build, because it carries `Core`'s module version id — so this is the
declared contract rather than the only trigger. The effect is the intended one: every stored image is
recompiled on its next request, and until then the listing says `Stale`.

## Considered and rejected

**`NullableContextOptions.Warn`.** Annotations without flow warnings changes nothing a page author
can act on; it is `Enable` or nothing.

**A blanket `#pragma warning disable` over the scaffold.** Hides genuine generator faults, and the
list of ids to suppress grows with every Roslyn release.

**Treating an unmapped warning as a fatal compiler fault.** Tempting, because it would make D1's
promise structural. Rejected: any new Roslyn warning would turn a working page into a `500`, and the
generator's output is not the author's code. The test in D2 gives the same assurance at the right
moment — the build, not the request.

**Failing the save on warnings.** The ask is explicit that a warning-only page is runnable, and the
editor already has a "Saved with warnings." branch that would become unreachable.

**Adding `Code` to `PageDiagnostic`.** The assistant splits the code out of the message at the first
colon today, which is fragile but works; the change would rewrite the stored diagnostics JSON for
every revision to tidy a field nothing queries. Deferred, recorded here so it is a decision.

**A warning count in the listing.** Needs a projection the store does not have (`Diagnostics` is a
JSON column, so a count means parsing every row); the state word is what makes a page findable, and
the editor is one click away.

**Narrowing `Sdk`.** It is the page contract; a page compiles against it from outside the product's
assembly graph. Its surface is the one thing that must stay open, which is why it gets the analyzer
rather than the narrowing.

**A hand-rolled surface test instead of the analyzer.** The fallback, not the choice: written out in
D9 so the review can take it.

**Showing the state on the page's own view.** A visitor does not care that the owner's page warns; the
author does, and the author is in the editor.

## Components

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Runtime/PageCompiler.cs` | `WithNullableContextOptions(Enable)`; `FormatVersion` `5`→`6`; the scaffold rule in the comment beside `MapDiagnostics` |
| `src/uk.osric.asp.Core/Compilation/TemplateSourceGenerator.cs` | `#nullable disable` at the top and around every generated line; `#nullable enable` before each opening `#line`, `#nullable disable` after each `#line default` |
| `src/uk.osric.asp.Sdk/**` | **unchanged** |
| `src/uk.osric.asp.Web/Pages/PageImageStatus.cs` | becomes `PageImageState`: the enum, the words, the description method with `currentRuntimeId` |
| `src/uk.osric.asp.Web/Pages/PageListing.cs` | **new** — `PagePath`, `IsDeleted`, `PageImageState` |
| `src/uk.osric.asp.Web/Pages/IPageStore.cs`, `Pages/SqlitePageStore.cs` | `ListPagesAsync` returns `PageListing`, joining the current revision |
| `src/uk.osric.asp.Web/Editor/PageEditor.cs` | `ListAsync` returns `PageListing` |
| `src/uk.osric.asp.Web/Editor/EditorEndpoint.cs` | one `WritePageTableAsync` for the three sections; the State column; the revision view calls `Describe` without staleness |
| `src/uk.osric.asp.Web/Admin/ExportEndpoint.cs`, `Admin/ImportEndpoint.cs` | take `PageListing`, ignore `State` |
| `src/uk.osric.asp.Web/Assistant/PageToolExecutor.cs` | `list_pages` gains `status`; `ImageStatus` passes the current runtime id |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | `page-author-v9`: nullable, the guard idiom, warnings are the job |
| `src/uk.osric.asp.Web/Pages/Seeds/editor.css` | the per-state classes |
| `src/uk.osric.asp.Core/**`, `Runtime/**`, `Web/**` | the narrowing and the guards of D9 and D10 |
| `Directory.Packages.props`, `src/uk.osric.asp.*/uk.osric.asp.*.csproj` | `Microsoft.CodeAnalysis.PublicApiAnalyzers` and the `PublicAPI.*.txt` files; `PublicAPI.Shipped.txt` empty in `Web` |
| `test/**` | see "Tests"; `FakePageStore` follows the new listing shape |
| `docs/architecture.md`, `docs/decisions.md`, `docs/testing.md`, `docs/jobs/` | see "Documentation and decisions" |

The seeded `editor.css` is content: an installation that ran before this change keeps its old copy
until the owner restores it, and the State column degrades to unadorned text in the meantime.

## Tests

**`Runtime.Tests` — `PageCompilerTests`** (existing file)

- `APageThatDereferencesTheIdentityWithoutAGuardWarns`
- `APageThatGuardsTheIdentityIntoALocalDoesNotWarn`
- `AWarningDoesNotStopTheImageBeingEmitted`
- `AWarningIsReportedAtTheTemplateLineTheAuthorWrote`
- `ADiagnosticFromTheGeneratedScaffoldIsNotReportedAsThePages`
- `AnObliviousPageStillCompiles`

**`Runtime.Tests` — a new scaffold test**

- `TheScaffoldCompilesWithoutWarningsUnderNullableEnable` — generated source for a text-only, a
  write-only, a code-only and a declaration template, compiled with the production options
- `EveryMappedRegionIsNullableEnabled`
- `EveryGeneratedLineIsNullableOblivious`

**`Core.Tests` — `TemplateSourceGeneratorTests`** (existing file)

- `TheNullableDirectiveIsWrittenOutsideTheMappedRegion`
- `APageThatDisablesNullableForItselfKeepsItDisabled`

**`Web.Tests` — `PageImageStateTests`** (new)

- `AnImageWithDiagnosticsSaysCompiledWithWarnings`
- `AnImageWithNoDiagnosticsSaysCompiled`
- `ARecordWithNoImageSaysCompileFailed`
- `APageWithNoRecordSaysNotCompiled`
- `AnImageFromAnotherBuildSaysStale`
- `AnImageIsNotStaleWhenNoCurrentBuildIsGiven`

**`Web.Tests` — `SqlitePageStoreTests`** (existing file)

- `TheListingCarriesTheCurrentRevisionsState`
- `TheListingSaysNotCompiledForAPageWithNoImage`
- `ADeletedPagesListingStillCarriesItsState`

**`Web.Tests` — `EditorEndpointTests`** (existing file)

- `ThePageListIsATableWithAPageAndStateColumn`
- `APageCompiledWithWarningsSaysSoInTheList`
- `APageThatFailedToCompileSaysSoInTheList`
- `AStalePageSaysSoInTheList`
- `TheDeletedListKeepsItsActions`
- `TheListSaysUnavailableForAReservedKey`

**`Web.Tests` — `PageToolExecutorTests`** (existing file)

- `ListPagesIncludesEachPagesState`
- `ReadPageStillReportsCompiledWithWarnings`
- `WritingAPageThatWarnsIsASuccessfulCall`

**`Web.Tests` — `PageAuthorPromptTests`** (existing file)

- `ThePromptIsVersionedForNullablePages`
- `ThePromptSaysAWarningIsUnfinishedWork`
- `ThePromptNamesTheNullableGuardIdiom`

**`Web.Tests` — `PageImageTests`** (existing file)

- `AnImageIsNeverStoredWithErrors`

**`Core.Tests` / `Runtime.Tests` — `PublicApiTests`** (only if D9's analyzer is rejected)

- one per assembly, comparing the exported surface to an inline expected array

**`Web.IntegrationTests`** (existing files)

- `APageWithWarningsIsServedAndItsDiagnosticsAreStored` — save, render, read back
- `APageThatGuardsTheIdentityIsServedUnderNullable`

**The browser suite** gains nothing: every behaviour here is provable in `Web.Tests` against the
markup, and `docs/testing.md`'s rule is that anything a unit test can prove does not belong in a
browser test.

## Phases

Each phase ends with `make test` green and `make format-check` clean.

1. **The compiler and the scaffold.** D1, D2, D11: the nullable option, the generator's directives,
   `FormatVersion`, and the compiler tests. No page behaves differently; a page that would warn now
   *says* so instead of saying nothing.
2. **The state and the surfaces.** D3–D8: `PageImageState`, `PageListing`, the store projection, the
   listing table, the assistant's `list_pages`, `page-author-v9`, `editor.css`. Everything with a
   `PageImageState` in it is tested against fabricated diagnostics, so this phase does not depend on
   phase 1 having produced a real warning.
3. **The tightening.** D9: `Web`'s two types, then `Runtime`, then `Core`, by the narrow-and-build
   loop; then the analyzer and the `PublicAPI` files. No behaviour change at all, which is what makes
   it safe to land on its own.
4. **The guards.** D10: remove the guards on non-public members, add them at public entry points,
   write the rule into `architecture.md`. The codebase settles into the convention the same commit
   that states it.
5. **Teach it and record it.** The docs, the decisions, the job table, and the release note that says
   pages may now warn after the deploy.

Phase 1 is small and worth landing alone. Phase 2 depends on nothing but its own tests. Phase 3 is
large and mechanical, and is the one to split across several commits — one per assembly — so a
review can see the surface change apart from the guards.

## Documentation and decisions

- **DEC-177**: a compiled page is nullable-enabled and its generated scaffold is not. The generator
  writes `#nullable enable` outside each mapped region and `#nullable disable` around every line it
  wrote itself, so a diagnostic the author sees is always about the author's code. `Core` and `Sdk`
  do not change; the promise is held by a compiler test rather than by runtime policy, because
  an unmapped warning is dropped and making one fatal would turn a Roslyn change into a `500`.
- **DEC-178**: a warning never stops a page. A stored image with bytes carries warnings only; the
  page is served, listed and hashed exactly as one that compiles cleanly. The editor, the history and
  the assistant all say "Compiled with warnings", and no path treats it as a failure.
- **DEC-165**: a page has a state — `Not compiled`, `Stale`, `Compile failed`, `Compiled with
  warnings`, `Compiled` — and the `/edit` listing shows it in a State column. Staleness is judged on
  `RuntimeId` alone, because hashing every template to answer a listing is not worth it, and a
  retained revision is history rather than stale.
- **DEC-166**: nothing is `public` unless another product assembly binds to it, and the build
  enforces it. The narrowing loop is "make it `internal`, build, and let the compiler name the
  hidden caller"; the guarantee is `Microsoft.CodeAnalysis.PublicApiAnalyzers` with an empty
  `PublicAPI.Shipped.txt` in `Web`. `Sdk` is the exception and is declared shipped in full.
- **DEC-167**: an argument is validated where it enters the assembly — public members of public
  types, the `Sdk` implementations, and the endpoint handlers — and trusted everywhere after that.
  `internal` and `private` members do not validate, and a DI-injected constructor does not either,
  because the container is the only caller and it does not pass null.
- `docs/architecture.md`: "Compiled pages" gains the nullable rule and the warning invariant;
  "Visibility" gains the enforcement and the validation rule.
- `docs/testing.md`: one sentence naming the scaffold and state suites.
- `docs/jobs/README.md`: the ARCH-14 row; IDEA-001 and IDEA-015 note that the listing now carries
  state.
- `README.md`: if it documents page writing, one line that nullable is on and a page may warn.

## Risks

- **The owner's pages warn after the deploy.** Every `ctx.User` in a page that is dereferenced
  without a bound local, and every other nullable slip, now says so. Nothing stops working — the
  render path is untouched — but the listing will show a wall of "Compiled with warnings" until they
  are fixed, and that wall is the point. The prompt, the docs and the editor's existing links are the
  whole mitigation.
- **Every image recompiles once.** `FormatVersion` and the module version id both move, so the first
  request per page pays a compile. That is the documented lazy path, not a new behaviour, and the
  listing's `Stale` state is what makes it visible rather than a surprise.
- **The narrowing is a large diff with a small semantic change.** The mitigation is that it is
  compiler-checked in both directions: a member narrowed too far fails the build, and the analyzer
  fails it again if the surface later widens. Splitting by assembly keeps the review tractable.
- **The analyzer adds a file to edit for every new public member.** That is the cost of the rule
  working, and it is worth stating so the first person to hit it does not remove the analyzer.
- **`Stale` after every deploy may look alarming.** It is accurate: the diagnostics shown for that
  page predate the running build. If it proves noisy the answer is to hide the state for a page whose
  image arrived in this process, not to remove the state.
- **Compile time rises.** Nullable analysis is real work per page. It is bounded by the existing
  compile slot and deadline, and one extra analysis pass on a small template is not measured to be
  significant — measure before optimising.
- **A guarded page that the compiler cannot narrow.** `ctx.User.Name` after `if (ctx.User is null)`
  does not warn on Roslyn 5.9, but a shape it cannot follow — a check on one receiver and a use on
  another, or a value captured in a lambda — will. The prompt teaches the local, which always works.
- **The seeded `editor.css` does not update itself.** An installation keeps its old copy, so the
  State column is plain text until the owner restores the seed. That is how seeds already behave.

## Open questions

1. **DI-injected constructors.** D10 exempts them. If the intent is "every public surface validates,
   no exceptions", that is 40-odd extra guards in `Web` and `Core` which can never fire.
2. **The `PublicAPI` analyzer.** It is a new package, and the one place this plan asks for one. The
   fallback in D9 is a hand-rolled test with the same guarantee and less precision.
3. **`Stale` in the listing.** In scope above because this plan creates the window it describes; say
   so now if it is noise rather than information.
4. **Whether the assistant should get a `warnings` roll-up** — "three pages compile with warnings" as
   a single fact in the prompt — or whether `list_pages` with a status per page is enough.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Capability, not feature

| | |
| --- | --- |
| Status | Planned, not started |
| Size | Medium — two `Sdk` renames behind an obsolete alias, a mechanical rename through `Web` and its suites, seven seeded pages (`/help` among them), and the documents that teach the name |
| Spike | None. The split is already in the code, and the obsolete behaviour is settled: a warning where a page uses the old name, and no warning where the host implements it |
| Decisions | DEC-228 (the word, the alias, and the release that removes it), recorded with this plan |
| Backlog | Nothing waits on this. Phase 3 opens IDEA-026, the removal of the alias, and phase 4 is that release |

## The short version

The seam that lets a page ask for storage, fetch, the page store, a layout or a renderer is called
two things today. In prose, in the decisions, in `/help` and in the test names it is a **capability**;
in the type system it is a **feature** — `IContext.Features`, `IPageFeatures`, `PageFeatures`. Both
names describe the same eight things, and a reader has to work out that they are the same eight.

This plan keeps one word:

> **A capability is an interface a page asks for through `ctx.Capabilities`.** The host decides what
> a render offers; a page cannot register one; a capability is either present or absent, never null.

`Features` survives for one release as a deprecated alias on the `Sdk` surface, because a stored page
is compiled source and this site is its pages. Every page that still says `ctx.Features` keeps
working and starts warning, at the position where it says it, with the replacement named. The host
and the tests rename outright — nothing outside the process binds to them. The alias goes in a later
release, once the page store has no page that needs it.

Nothing a page author can do changes. `IPageStorage`, `IHttpFetch`, `IPageCatalog`, `IPageWriter`,
`IPageLayout`, `IMarkdownRenderer`, `IDiffRenderer` and `IBuildInfo` are the same eight capabilities,
reached through a differently named property.

## Why capability

The three candidates were weighed against what the seam actually is: an ability the host grants one
render and withholds from the next, carrying exactly the authority it was given — `IPageWriter` bound
to `ctx.Path`, `IPageStorage` bound to the page's key, `IPageCatalog` read-only, the write requiring
sign-in (DEC-189, DEC-190). That is a capability in the ordinary security sense, and the word says
so without a footnote.

| Word | Verdict |
| --- | --- |
| **capability** | The noun already used by every human-facing surface. Names the grant-and-withhold relation, and the authority a handle carries. Adopt. |
| **feature** | Collides with the assistant as "the feature" (`docs/assistant.md`, "Enabling the feature", `AssistantOptions`'s master switch) and with ASP.NET Core's `HttpContext.Features`, which `Web` already uses a few lines away from `PageFeatures`. Suggests permanence where the contract is per render. Retire from the page vocabulary. |
| **extension** | Two established meanings here already — a key's file extension (`Pages:ContentTypes:Extensions`, DEC-144) and an extension method or `Microsoft.Extensions.*` (about 115 uses). A third would imply third-party extensibility, which `PageApiPolicy`'s default-deny and host-only registration exist to prevent. Reject. |

The repo counts agree with the vocabulary: about 380 occurrences of *capability*, about 550 of
*feature* (most of them the identifier `Features`), about 260 of *extension*, none of which describe
this seam.

## Goals

1. One word for the seam in the `Sdk` contract, the `Web` implementation, the tests, `/help`, the
   assistant prompt and the documents.
2. Stored pages keep compiling. `ctx.Features` and `IPageFeatures` become `[Obsolete]` warnings, not
   errors, and the warning names the replacement.
3. The rename is one mechanical pass in `Web` — no second spelling left behind in an internal type.
4. The glossary owns the word, with the "not a feature, not an extension" sentence in it, so the next
   reader does not reopen the question.
5. A recorded release in which the alias is deleted, so "for one release" stays true.

## Non-goals

- **Changing what a page can do.** No capability is added, split, merged or re-scoped. The eight stay
  eight and their members do not move.
- **Rewriting history.** `docs/decisions.md` rows, `docs/plans/archive/` and `docs/spikes/` keep the
  words they were written with. DEC-228 records the change; older rows are what the code said then.
- **Renaming anything else called a feature.** The assistant is still a feature in the product sense,
  and `Microsoft.Extensions.*`, `HttpContext.Features`, `Pages:ContentTypes:Extensions` and podman's
  `DropCapability=` are not this.
- **A compatibility shim in `Web`.** The page contract is `Sdk`; everything inside `Web` renames in
  one pass.

## What exists today

| Surface | Says | Example |
| --- | --- | --- |
| `Sdk` | feature | `IContext.Features` returns `IPageFeatures`; the type's own summary calls them "the optional capabilities this render offers" |
| `Web` | feature | `PageFeatures`, `IRenderBoundFeature`, `RenderBoundFeature`, `SimpleContext._features`, the `features` parameter through `PageChain` and `PageHarness` |
| `Web` (ASP.NET) | feature, unrelated | `Program.cs` imports `Microsoft.AspNetCore.Http.Features`; test helpers call `context.Features.Set<…>` |
| Prose, decisions, `/help` | capability | `/help`'s "Context and capabilities"; DEC-142, DEC-143, DEC-148, DEC-168, DEC-189, DEC-190, DEC-211, DEC-219 |
| Tests | capability | every method in `PageFeaturesTests` — `GetRequiredReturnsARegisteredCapability`, `AttachDeadlineReachesABoundCapability`, … |
| Seeded pages and the prompt | feature, at the call site | 24 uses of `ctx.Features` across seven seeds; 17 in `PageAuthorPrompt` |

The mixed file is the small proof of the problem: `IRenderBoundFeature` summarises itself as "A
capability the render can stop" and then documents its member as "Gives the feature the render's
deadline".

## Design

### D1 — The new name is the definition, the old name is an alias

`Sdk` gains `IPageCapabilities` as the type that declares the three members, and `IContext` gains
`Capabilities` as the accessor. `IPageFeatures` becomes an empty interface deriving from the new one,
and `IContext.Features` keeps returning it, so even a stored page that wrote
`IPageFeatures features = ctx.Features;` still compiles.

```csharp
// Sdk/IPageCapabilities.cs — the definition
public interface IPageCapabilities {

    public bool TryGet<T>([NotNullWhen(true)] out T? capability) where T : class;

    public T? Get<T>() where T : class =>
        TryGet(out T? capability) ? capability : null;

    public T GetRequired<T>() where T : class =>
        TryGet(out T? capability)
            ? capability
            : throw new InvalidOperationException($"This render does not offer {typeof(T).Name}.");
}
```

```csharp
// Sdk/IPageFeatures.cs — the alias, for the release stored pages catch up in
/// <summary>The capabilities this render offers.</summary>
/// <remarks>Deprecated: every member is inherited from <see cref="IPageCapabilities"/>.</remarks>
[Obsolete(Deprecated, DiagnosticId = "PAGE003")]
public interface IPageFeatures : IPageCapabilities;
```

```csharp
// Sdk/IContext.cs
/// <summary>The capabilities this render offers.</summary>
public IPageCapabilities Capabilities { get; }

/// <summary>The capabilities this render offers. Deprecated: use <see cref="Capabilities"/>.</summary>
[Obsolete(Deprecated, DiagnosticId = "PAGE003")]
public IPageFeatures Features { get; }
```

`Deprecated` is one internal constant, in `Sdk/PageCapabilityDeprecation.cs`, so the two messages cannot
drift and both name both replacements:

> `"Use IPageCapabilities and IContext.Capabilities: the page contract calls these capabilities now."`

The constant is internal rather than a member of the page contract, so the message is not itself part
of the API a page binds against.

Four consequences, each deliberate:

1. **The members are declared once**, on the new type, with the `Get`/`GetRequired` default
   implementations. The alias inherits them; it declares nothing.
2. **`TryGet`'s parameter is renamed** `feature` → `capability`. No seed, test template or prompt uses
   a named argument there, so nothing breaks in practice; the plan records it because it is the one
   source-level change the alias does not smooth over.
3. **`Features` keeps its return type.** Returning `IPageCapabilities` from the deprecated property
   would be tidier and would break an explicit `IPageFeatures` declaration; the point of the alias is
   that nothing breaks.
4. **`PageCapabilities` implements both.** The class declaration carries one scoped suppression, with
   the reason beside it; the alternative — marking the class `[Obsolete]` — would warn at every use
   in `Web` instead of at one declaration.

```csharp
// Web/Pages/PageCapabilities.cs
#pragma warning disable CS0618 // Implements the deprecated alias so a page that names IPageFeatures still compiles.
internal sealed class PageCapabilities : IPageCapabilities, IPageFeatures {
#pragma warning restore CS0618
```

Verified before writing this plan: an `[Obsolete]` **member** may name an `[Obsolete]` type in its
signature without warning, and implementing an obsolete interface member produces no warning. So the
only warnings in a `make build` after this change are uses of the old names, which is the whole point.
`SimpleContext.Features` therefore carries `[Obsolete]` too, and needs no pragma. That matters
because `Directory.Build.props` sets `TreatWarningsAsErrors`, so the host would not build if the
deprecation leaked into its own code: the suppression above is the only one, and it is one line with
its reason beside it.

### D2 — A warning, never an error

`[Obsolete]` defaults to `error: false`, and the default is kept. An error would be a sight-unseen
outage: changing `Sdk` moves its module version id, so `RuntimeId` changes and **every** stored page
image is invalid, recompiled lazily from its stored source on the next request (`docs/pages.md`,
"compiled pages"). With a warning, that recompile succeeds for every page and records one diagnostic
where the page says `ctx.Features`; with an error, every stored page fails to compile and the site
answers diagnostics instead of pages.

The warning is also the migration: a page that compiles with warnings stores its image and says
"Compiled with warnings" in the editor and the page list, and the diagnostic points at the line in
the author's template.

### D3 — The diagnostic has a page code, and it names the replacement

`DiagnosticId = "PAGE003"` puts the deprecation in the same space as the contract's other two
(`PAGE001` API denials, `PAGE002` structure). A page author sees `PAGE003` at the position of use,
with a message that names `ctx.Capabilities`; the assistant sees the same string in the diagnostics
its save returns, so it can fix the page it is working on without being told separately.

`PageCompiler` maps a warning back to the template only when the position is mapped, and this one
always is: the obsolete warning lands inside the page's own code block, which the generator wraps in
`#line`. A stored page that says `ctx.Features` in a declaration block is covered the same way.

### D4 — `Web` renames outright

Nothing outside the process names `Web`'s types, so there is no alias to keep and no phase to stagger.
The rename is exhaustive, so `rg -i feature src/uk.osric.asp.Web/Pages` afterwards finds only
ASP.NET's `HttpContext.Features` and prose about the assistant.

| Today | After |
| --- | --- |
| `Web/Pages/PageFeatures.cs`, `PageFeatures` | `Web/Pages/PageCapabilities.cs`, `PageCapabilities` |
| `Web/Pages/IRenderBoundFeature.cs`, `IRenderBoundFeature` | `Web/Pages/IRenderBoundCapability.cs`, `IRenderBoundCapability` |
| `Web/Pages/RenderBoundFeature.cs`, `RenderBoundFeature` | `Web/Pages/RenderBoundCapability.cs`, `RenderBoundCapability` |
| `IRenderBoundPageStorage` | unchanged name, new base interface |
| `SimpleContext._features`, the `features` parameter | `_capabilities`, `capabilities` |
| `PageChain`'s local `features`, `PageHarness`'s local `features` | `capabilities` |
| `SimpleContextTests.FeaturesFor` | `CapabilitiesFor` |
| `Web.Tests/PageFeaturesTests.cs` | `Web.Tests/PageCapabilitiesTests.cs` |

The two property implementations and the `AttachDeadline`/`Abandon` forwards on `SimpleContext` go
with them, and the layout error message in `PageChain` — `"Set one with ctx.Features.GetRequired<IPageLayout>()…"`
— becomes `ctx.Capabilities`.

### D5 — Stored pages and seeds

Seeding only inserts a key that has no head (`PageAssetSeeder.SeedAsync`), so an edited copy of a
seeded page is not overwritten by a release. The upgrade path is therefore:

- **The seeds themselves change**, so a fresh site teaches `ctx.Capabilities` and a restored seed
  replaces an old copy through the page list's restore action.
- **An existing copy keeps working**, with a `PAGE003` warning, until the owner edits it or restores
  it from its seed. There is no background rewrite of page source: the store belongs to the owner.
- **`/help` changes in the same release**, because it is the manual for exactly this word, and it is
  seeded like everything else.

Seven seeds use the old name: `about`, `admin`, `_layout`, `help`, `pages`, `recent` and `scratch`.
`PageAuthorPrompt` teaches it in seventeen places, and its version goes `page-author-v17` →
`page-author-v18` so a stored transcript is attributable to the prompt that taught the old spelling.

### D6 — What keeps its name

- **`HttpContext.Features`, `IFeatureCollection`, `Microsoft.AspNetCore.Http.Features`** — ASP.NET
  Core's request features, a different mechanism that `Web` already uses. Unchanged.
- **The assistant "feature"** — `docs/assistant.md`, `AssistantOptions`, the feature flag. Product
  vocabulary, not page vocabulary.
- **File extensions** — `Pages:ContentTypes:Extensions`, `PageContentTypes`, the "extension map" in
  `/help`. Unchanged.
- **`DropCapability=`** in the quadlets — a Linux capability, in the deploy vocabulary.
- **Extension methods** — `AuthenticationExtensions` and friends. Unchanged.

### D7 — The glossary and the decision

`docs/glossary.md` gains **Capability**, between *Budget* and *Conversation*, in the house shape: what
it is, what it is not, and what owns it.

> **Not** a feature or an extension. A feature is something the product has and an extension is
> something a third party adds; a capability is something one render offers a page, and it may be
> absent from the next. The accessor was called `ctx.Features` until DEC-228.

That entry is the rule the glossary already states — "the code is authoritative" — made true again,
and it is where the Deprecated constant's sentence comes from.

## What this touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Sdk/IPageCapabilities.cs` | New: the three members, moved from `IPageFeatures` |
| `src/uk.osric.asp.Sdk/IPageFeatures.cs` | Becomes the empty obsolete alias |
| `src/uk.osric.asp.Sdk/PageCapabilityDeprecation.cs` | New, internal: the one deprecation message both attributes use |
| `src/uk.osric.asp.Sdk/IContext.cs` | `Capabilities` added; `Features` deprecated, return type unchanged |
| `src/uk.osric.asp.Sdk/PublicAPI.Shipped.txt` | Unchanged — both names stay public |
| `src/uk.osric.asp.Sdk/PublicAPI.Unshipped.txt` | `Capabilities`, `IPageCapabilities` and its three members; three `*REMOVED*` lines for the members the alias no longer declares |
| `src/uk.osric.asp.Web/Pages/PageFeatures.cs` → `PageCapabilities.cs` | Renamed; implements both interfaces behind one suppression |
| `src/uk.osric.asp.Web/Pages/IRenderBoundFeature.cs` → `IRenderBoundCapability.cs` | Renamed; its summary and member docs say capability throughout |
| `src/uk.osric.asp.Web/Pages/RenderBoundFeature.cs` → `RenderBoundCapability.cs` | Renamed |
| `src/uk.osric.asp.Web/Pages/IRenderBoundPageStorage.cs` | Base interface renamed; the type's name does not change |
| `src/uk.osric.asp.Web/Pages/SimpleContext.cs` | `_capabilities`, `Capabilities`, deprecated `Features`; class summary says capability |
| `src/uk.osric.asp.Web/Pages/PageChain.cs` | Local renamed; the layout-title error names `ctx.Capabilities` |
| `src/uk.osric.asp.Web/Pages/PageHttpFetch.cs`, `PageLayoutState.cs`, `PageCatalogScope.cs`, `PageWriterScope.cs`, `PageStorageScope.cs` | Comments that say "the feature" say "the capability" |
| `src/uk.osric.asp.Web/Pages/Seeds/{about,admin,_layout,help,pages,recent,scratch}` | `ctx.Features` → `ctx.Capabilities`; `/help`'s section text follows |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | Seventeen call sites and the prose around them; `Version` to `page-author-v18` |
| `test/uk.osric.asp.Web.Tests/PageFeaturesTests.cs` → `PageCapabilitiesTests.cs` | Renamed; names already say capability |
| `test/uk.osric.asp.Web.Tests/{PageHarness,SimpleContextTests}.cs` | Locals, helper and type names |
| `test/**` (render, scope, integration, browser suites) | `ctx.Features` → `ctx.Capabilities` in test templates, about 100 uses over 19 files |
| `docs/pages.md` | The contract paragraph and the capability list |
| `docs/architecture.md`, `docs/storage.md`, `docs/glossary.md` | The `Sdk` surface line, two call sites, the new entry |
| `docs/decisions.md` | DEC-228 |
| `README.md` | Three `ctx.Features` examples and the editor-manual sentence |
| `docs/jobs/IDEA-022.md`, `IDEA-024.md`, `IDEA-025.md`, `docs/jobs/README.md` | Current-state prose only; archive plans untouched |

## Tests

**`Runtime.Tests`** — the compatibility promise, proved by the compiler:

- `APageThatSaysFeaturesCompilesAndWarns` — a template using `ctx.Features.GetRequired<IPageStorage>()`
  compiles, and the warnings are exactly one `PAGE003` whose message names `ctx.Capabilities`.
- `APageThatSaysCapabilitiesCompilesWithoutWarnings` — the same template with the new spelling is
  clean, so the new name is not quietly deprecated too.
- `TheDeprecatedWarningCarriesTheTemplatePosition` — the diagnostic's line and column are the
  `ctx.Features` token, so the editor highlight is right.

**`Web.Tests`**

- `PageCapabilitiesTests` — the existing twelve tests, renamed type, no assertion changes.
- `BothAccessorsHandOutTheSameRegistry` — `ctx.Capabilities` and `ctx.Features` are the same object,
  and `ctx.Features` is assignable to `IPageCapabilities`.
- `TheOldNamesAreMarkedObsolete` — reflection over `IContext.Features` and `IPageFeatures`, so a
  later edit cannot drop the attribute and leave stored pages on a name with no warning.
- `FeaturesFor` → `CapabilitiesFor`; the render, storage, fetch, markdown, diff, catalog and writer
  suites render the same templates through the new spelling.
- `PageAuthorPromptTests` — the prompt teaches `ctx.Capabilities`, never `ctx.Features`, and
  `Version` is `page-author-v18`.

**`Web.IntegrationTests`**

- `AStoredPageThatSaysFeaturesStillServes` — a page saved with `ctx.Features` renders its body on the
  real host, and its stored diagnostics carry `PAGE003`, which is the release's central promise.
- The existing seed, layout, chrome and self-save flows, with the seeds on the new spelling.

## Phases

### Phase 0 — the `Sdk` surface (half a day)

`IPageCapabilities` and `IContext.Capabilities`; `IPageFeatures` and `IContext.Features` deprecated;
`PublicAPI.Unshipped.txt`; the three `Runtime.Tests` above written first. `make build` is clean, and a
page using either spelling compiles.

### Phase 1 — the shell and its suites (half a day)

The internal rename, one pass, mechanical. `PageFeaturesTests` becomes `PageCapabilitiesTests`, the
test templates move to `ctx.Capabilities`, and the same behaviour passes.

### Phase 2 — what the author reads (half a day)

The seven seeds, `/help`'s capability section, the assistant prompt and its version, the error
message in `PageChain`. A fresh site and a restored seed both teach the new name.

### Phase 3 — the documents (half a day)

`docs/pages.md`, `docs/architecture.md`, `docs/storage.md`, the glossary entry, `README.md`, the live
job files, DEC-228 and **IDEA-026** in the backlog, so the alias's removal is a job rather than a
sentence.

### Phase 4 — the release that removes the alias (a later release, not this plan)

IDEA-026's work: delete `IPageFeatures`, `IContext.Features`, `Capabilities`'s dual implementation and
the suppression. Gated on a query of the page store for heads whose source still contains
`ctx.Features` (and `IPageFeatures`), so the deletion is decided by data, not by a date.

## Verification

1. `make build` — the public API analyzer accepts the alias and the three removals, and nullable
   analysis is happy with the dual implementation.
2. `make test-runtime` — the three compatibility tests.
3. `make test-web`, `make test-integration` — the renamed suites and the stored-page promise.
4. `make format-check` and `make license-check` — the renamed files keep their headers.
5. `make check` before each commit, and `rg -i 'feature' src/uk.osric.asp.Web/Pages` shows only
   ASP.NET's `HttpContext.Features` and comments about the assistant.

## Rejected options

| Option | Why not |
| --- | --- |
| Standardise on **feature** | Rewrites about 380 prose uses, `/help`, the prompt and the decisions, keeps the collision with the assistant as "the feature", and keeps the collision with `HttpContext.Features` in the same folder. The noun would still not say "this render may not offer it". |
| Standardise on **extension** | Already means a file extension and an extension method here, and implies third-party extensibility that `PageApiPolicy` and host-only registration forbid. |
| Rename outright, no alias | Every stored page that says `ctx.Features` stops compiling the moment `Sdk` changes, because the image's `RuntimeId` is invalid. The site's own pages are stored source; an obsolete warning is the only way to move them without an outage. |
| Deprecate as `error: true` | Same outage, with a shorter window: there is no build in which a page can be fixed while serving. |
| Make `IPageFeatures` the base and `IPageCapabilities` the derived alias | The new name would inherit its documentation from the type being retired, and `PublicAPI` churn lands on the name that is meant to last. |
| Keep the split and document it | Cheapest today, and it is what the code does now: `IPageFeatures`'s own summary already calls them "the optional capabilities this render offers". The glossary's "the code is authoritative" cannot then be true. |
| Rename `Get`/`GetRequired`/`TryGet` as well | They read correctly for a capability already (`Get<IPageStorage>()`), and changing member names is a source break the alias cannot absorb. |

## Risks

| Risk | Mitigation |
| --- | --- |
| **A page that cannot be edited is left on the old name.** The alias is permanent and the vocabulary is half-undone. | Phase 4 is called out as a release with an IDEA behind it, and its gate is a query over the page store, not an intention. |
| **Warnings look like a regression.** After deploy, every stored page that says `ctx.Features` shows "Compiled with warnings" in the page list. | It is the migration signal, and `PAGE003` says what to do. The release notes say so; the seven seeds are fixed in the same release, so a fresh site is clean. |
| **The obsolete noise reaches the assistant.** A run sees `PAGE003` in a save result and treats it as failure. | The message names the replacement, and the prompt's warning section already says a warning does not stop the page. `PageAuthorPromptTests` holds the prompt to the new name so new pages are written clean. |
| **The dual implementation is forgotten.** Someone deletes the pragma, or `PageCapabilities` stops implementing the alias, and a stored page fails with a type error rather than a warning. | The `Sdk` and `Web` builds fail first, because `SimpleContext.Features` cannot return the alias from an object that does not implement it; `APageThatSaysFeaturesCompilesAndWarns` then proves the promise end to end, and the suppression carries its reason on the line above. |
| **A later `Sdk` change removes `Features` by accident.** | It is a compile error for `Web` and a missing-API error in the analyzer; the removal is only ever the planned one. |
| **The rename collides with the parallel churn.** | The work is one commit per phase, phase 0 and 1 touch disjoint files, and `make build` proves each phase before the next begins. |

## Documentation and decisions

- **DEC-228** — The seam that offers a page storage, fetch, the page store, a layout and the pure
  renderers is called a **capability**: `IPageCapabilities`, reached through `IContext.Capabilities`.
  `IPageFeatures` and `IContext.Features` remain for one release as `[Obsolete]` warnings, because
  stored pages are compiled source and a change to `Sdk` invalidates every image; the host renames
  outright and the alias is removed in the release IDEA-026 describes. A capability is not a feature
  — something the product has, or ASP.NET Core's request features — and not an extension: a file
  extension, an extension method, or third-party extensibility, which the page contract does not
  offer.
- [pages](../pages.md) states the contract with `ctx.Capabilities` and keeps the count at eight.
- [architecture](../architecture.md) names the `Sdk` surface correctly in the layer table.
- [storage](../storage.md) shows the page-store read and the self-save through the new accessor.
- [glossary](../glossary.md) defines **Capability**, with the "not a feature, not an extension" clause
  and the note that the accessor was `Features` until DEC-228.
- [testing](../testing.md) needs no suite name change; it does not list `PageFeaturesTests`.
- [the jobs](../jobs/README.md) and IDEA-022/024/025 use the current spelling, and IDEA-026 is added
  as the alias's removal; archive plans and decision rows written before this change keep theirs.

## Delivered when

- `ctx.Capabilities` is how a page reads a capability, and `IPageCapabilities` is the type that
  declares them; `docs/pages.md`, `/help` and the assistant prompt all say so.
- `ctx.Features` and `IPageFeatures` compile with a `PAGE003` warning that names the replacement, and
  a stored page using them serves normally on the real host.
- No `Feature`-named type is left in `Web.Pages` except ASP.NET's own, and `PageCapabilitiesTests`
  is the renamed suite.
- The seven seeds, `/help` and `page-author-v18` teach the new name; a fresh site and a restored seed
  are clean, with no deprecation warning.
- DEC-228 and the glossary's **Capability** entry exist, and IDEA-026 is in the backlog with the
  query that gates it.
- `make check` passes.

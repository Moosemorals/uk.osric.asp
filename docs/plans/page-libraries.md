<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Page libraries

| | |
| --- | --- |
| Status | Planned, not started |
| Size | Large — a kind in the compiler, a column and its migration, an index and a closure, a per-tree policy, a source on every diagnostic, and the end of `PageDocument` in `Sdk` |
| Spike | None. The tokenizer already reports the declarations, `TemplateHasCode` already proves a verdict can come from generation, and Roslyn already compiles more than one tree |
| Decisions | cited by name until this merges; DEC-240's rule is kept and its column replaced, DEC-156 is superseded in phase 4, DEC-151's comparison test is extended to the document; D12 costs a shared assembly and leaves it out |
| Backlog | [IDEA-005](../jobs/IDEA-005.md) — the layout half landed; this is the shared-code half, and it is the last of it |

## The short version

A page can declare types, but nothing it declares can be used by another page. The only place shared
code can live today is `Sdk`, which is why a document builder sits in the assembly that is supposed
to be nothing but the page contract.

This plan gives a page two new answers and no new syntax:

> **A page is one of three kinds** — no code (its source is the response), code (it renders), or a
> **library** (it is only something another page compiles against). The kind is a property of the
> source, judged by generation, exactly as "no code" is.

> **A library is named by a `using`.** A page's `<%! using Site.Document; %>` pulls in the library
> page that declares that namespace, and the compiler includes it. There is no directive, no import
> list and nothing new to learn: the dependency is ordinary C#.

The document builder then has somewhere else to live. It merges into `ShellDocument`, the seeded
`/_layout` calls a library document, and `Sdk` goes back to being a contract.

## Goals

1. A page's declared types can be used by another page, without adding a directive or a second
   template language.
2. The dependency is visible in the source, in the language the page is written in.
3. A page that names a library is the only page a library change can invalidate.
4. A library is never served, never wrapped, never given an `IContext`, and never counted as a
   representation.
5. The page API policy still governs library code, and a page still cannot use a library to reach
   something the policy denies.
6. `PageDocument` leaves `Sdk`; the shell keeps its own document, and drift between the shell's
   document and the seeded layout's is a test rather than a shared type.

## Non-goals

- **A shared assembly at runtime.** Every library is compiled into each page that names it. No shared
  statics, no load-context registry, no second lifetime. See D9.
- **A new token or directive.** `<%@ use … %>` was considered and rejected: the `using` the page
  already writes is the same statement, in C#.
- **Transitive packaging or a dependency order.** Libraries are syntax trees in one compilation, so
  mutual references are legal and order is not a concept.
- **A library editor or a second listing.** A library is a page: the editor, history, seeds and scrub
  all work on it unchanged.
- **A per-page opt-out of the default layout, or a library that renders.** A library has no handler
  to render.
- **Publishing libraries between sites.** Import and export already carry the source by key; nothing
  new is needed and nothing new is promised.
- **A cache of library sources.** Measured first; a compile is rare and `n` is small.

## A page is one of three kinds

| Kind | The rule | Served | Compiled | Wrapped | What it may name |
| --- | --- | --- | --- | --- | --- |
| No code | text only | its source, byte for byte | never | never | nothing |
| Code | any code, write or mixed declaration | rendered | to a handler | the default layout | the API policy, its own types, its closure |
| Library | only `<%! %>` blocks, and text that is whitespace | `404` | checked, never invoked | never | the API policy, its own types, the libraries it names |

The last line is the whole of what "pure C#" means: the page has nothing to render, so the render
mechanisms are not offered to it. It is the mirror of DEC-239's no-code page, which is a complete
representation and so gets no chrome.

## Decisions

### D1 — the verdict is one kind, not a second flag

`TemplateGenerationResult` already answers `HasCode` from the tokens (DEC-240). It grows a **kind**,
and the column becomes `PageRevisions.TemplateKind` (nullable; `0` text, `1` code, `2` library). The
migration derives it from `TemplateHasCode` and then drops that column: `0 → text`, `1 → code`,
`NULL → NULL`. A second nullable bool would let "library" and "has code" disagree, and the state words
and the asset rule would each have to remember the invariant.

DEC-240's rule is kept — the verdict comes from generation, is stored beside the source, and a
`NULL` means never judged — and its column is replaced in the same change. `PageAssetPolicy.IsAsset`
becomes `kind is PageKind.Text`; `PageEntry.IsAsset` keeps its meaning and gains `IsLibrary`.

The verdict itself is one token check in `TemplateGenerationResult`:

- **text** — no token but `Text`;
- **library** — at least one `Declaration`, every `Text` token is whitespace, and no `Code`, `Write`
  or `WriteRaw` token;
- **code** — anything else.

A page with declarations *and* code stays a page, which is what every page with `<%! %>` does today.

### D2 — a library is named by a `using`

A library declares a namespace, and that namespace is its name. The compiler builds an index from
every live library page it can see (`namespace → key`), then resolves the page's own `using`
directives against it, and repeats for the libraries those pulled in, to a fixed point.

- **Why not the key.** A key is a URL; a namespace is the C# identifier the author writes anyway. The
  index is what makes the two meet, and a library may declare more than one namespace.
- **Why not a directive.** `<%@ use /_lib/document %>` was the other candidate: one token, save-time
  validation and no index. It also invents a second way to import code into a language that already
  has one, and the author has to learn it. The `using` is visible, checked by Roslyn, and works for a
  fully qualified name that a future feature might add.
- **A fully qualified use without a `using` does not pull the library in.** Roslyn answers `CS0246`
  and the author adds the `using`; nothing is silent, and it is the error a C# author already knows.
- **A page may not declare a namespace** (unchanged, `PAGE002`), so a page cannot become a library by
  accident or pass itself off as one. A **library** may, because the rule exists only so the generated
  `GeneratedPage` can be found, and a library has no handler.
- **Two libraries with one type name** are `CS0101` at the second declaration, mapped to its own key.
  `n` is small; a namespace convention is an author's answer, not the host's.

### D3 — each library is its own syntax tree in the page's compilation

The page's tree and one tree per library go into the same `CSharpCompilation`. Nothing is spliced.

- **The using-order trap disappears.** Splicing library declarations into the page's unit would put
  the library's `using` directives after the page's types (`CS1529`) and need a text split the
  generator cannot do without parsing C#.
- **A mapped path per library.** Each library tree writes `#line 1 "lib:/_lib/document"` around its
  own blocks, so Roslyn reports that file's positions and the shell can name the library.
- **One compilation, one emit.** The libraries are copied into the page's image, so the page binds to
  them exactly as it binds to its own declarations.
- **`Compile` takes candidates and answers which it used.** `IPageCompiler.Compile` gains the library
  sources and returns the keys it pulled in, so the caller can record the closure (D7) without
  re-deriving it. Resolution is a syntax-only walk in `Runtime`; `Core` stays Roslyn-free.

### D4 — the policy trusts the page and its closure, and checks every tree

`PageApiChecker.Check` takes one tree, and `IsDeclaredIn` treats a symbol as page code only when it is
declared in that tree. A library type would therefore look foreign and every use of it would be a
`PAGE001`.

The policy runs over every authored tree — the page and each library it pulled in — and
`IsDeclaredIn` accepts a symbol declared in **any** of them. That is safe because each tree is itself
walked: a library that wraps `System.IO` is refused at its own key and line, whether or not a page
calls it. The deny-list is unchanged; nothing about `System.Net`, reflection or the shell loosens.

### D5 — a library's shape rule

The page's shape rule stays as it is (`GeneratedPage` last, everything before it a declaration). A
library tree gets its own, because it has no handler:

- only `using` directives, namespaces and type declarations;
- at least one type declaration, or nothing is being shared;
- no `GeneratedPage` — it would collide with the dependent's handler in the same assembly;
- no assembly attributes — they would attach to every dependent's image;
- no statements: a library is declarations, not code.

The last one means a library cannot have a module initializer or a static constructor that does work
at load: a static field initializer still runs when the dependent first touches the type, and that is
the author's business, exactly as it is inside a page's own declaration block.

### D6 — a diagnostic names the page it belongs to

`PageDiagnostic` (both copies, `Core` and `Sdk`) gains a nullable `Source` key: `null` is the page
being compiled, a key is the library it came from. `PageCompiler.MapDiagnostics` reads the mapped
path (`page` or `lib:/key`) and sets it. `SortedForDisplay` groups by source first — the page's own
diagnostics, then each library's by key — and then by line and column.

The shell maps it in `PageDiagnostics.ToSdk`; the editor's diagnostic list says "in `/_lib/document`"
for a library's, and links to that page's editor URL; the assistant's save and read answers carry it
as they carry the rest of the diagnostic. This is the one change here that reaches every place an
author reads a diagnostic, and it is what makes a closure bigger than one page readable.

### D7 — the closure is recorded with the image, and nothing recompiles eagerly

`PageRevisions` gains `TemplateLibraries`, an opaque, sorted fingerprint of the closure
(`key@revisionHash` per library, joined). It is written with the image and checked with it:

- `PageImage` gains the fingerprint and `IsCurrent` takes it;
- the staleness reason gains "a library changed", counted on the existing metric;
- a page whose closure moved is recompiled on its next request by the ordinary lazy path, inside the
  compile slots, one compile per page — the same shape as the recompile after a deploy.

Saving a library recompiles nothing. Its dependents go stale when their next request compares
fingerprints, which is honest and needs no fan-out.

### D8 — a library stores source, verdict and diagnostics, not an assembly

Saving a library compiles it alone (Sdk and platform references; no handler, so nothing to emit) and
stores the revision with its kind and its diagnostics. `Bytes` stays null and the state says
"Library" rather than "Compile failed" — the kind decides, which is why D1 made it one value. The
image columns are otherwise the ones every revision already has.

### D9 — nothing is shared at runtime

Each dependent's image contains its own copy of the library's types. There is no shared static state,
no registry of loaded library assemblies, and no unload story to get wrong. The cost is a copy per
dependent and a compile per dependent when a library changes; the benefit is that the runtime stays
one collectible context per render, exactly as it is today.

A shared library assembly — compile once, load once, share the code — is costed in D12 and not built.
The numbers that would reopen it are in "What we will watch".

### D10 — `PageDocument` merges into `ShellDocument` in the last phase

The type is the shell's document. Sixteen uses across eleven files in `Web`, nine of them `Nav` and
two `IsSiteRelative`, and it is in `Sdk` only because the seeded layout has to write the document.
Once a library can write one, the page side does not need the shell's copy.

It moves into `src/uk.osric.asp.Web/Html/` and into `ShellDocument` itself; the public type leaves
`Sdk` and `PublicAPI.Shipped.txt`. The shell keeps writing its own document, because the error,
sign-in and status routes must answer when the page pipeline is broken (DEC-151) — that is the drift
being accepted, deliberately.

The mitigation is the test DEC-151 already uses for the menu: an integration test that renders a shell
view and the seeded layout and compares the document shape, so the skip link, the one `main`, the
sheet order and the icon cannot drift apart silently.

This phase is detachable: it can be dropped or taken first, and the library work does not depend on it.

### D11 — the seeded layout calls a library document

The seed stops calling the SDK. In the same phase it becomes a page with a `using` for the library
document, so the owner can edit the document itself in the editor and restore it from `/admin/restore`.
The library writes through `IContext` — `Write` for every value, `WriteRaw` for the frame — because
`System.Net.WebUtility` is denied to page code and no widening is worth an author-editable encoder.

`/help` gains the library page kind (phase 2, when the kind exists), the `using` rule (phase 3) and
the layout's new shape (phase 4), each in the change that makes it true.

### D12 — a shared library assembly is costed, and not built

"Shared" is three different designs, and only one of them shares anything:

| | Compile | Runtime | Isolation |
| --- | --- | --- | --- |
| This plan | N page compiles, library text included | one assembly per render | one context per render |
| Compile once, load per render | one library compile, N page compiles | two assemblies per render | one context per render |
| Compile once, load once per revision | one library compile, N page compiles | one assembly per render, JIT once | **one copy for every page and visitor** |

**The compile saving is smaller than it looks.** Every shape still recompiles each dependent when the
library moves, because a page's image binds the library's types when it compiles. What the other two
save is compiling the library's *text* N times and its *bytes* in N images — not the N page compiles.
The only way to avoid those is to pin each page to the library revision it was compiled against, and
then a library fix takes effect only as each page is recompiled later; that is a staleness rule of its
own, and it is not what this plan does.

**Versioned URLs are not at risk.** `?v=` on a revision with code renders it rather than promising a
byte sequence — the `immutable` promise belongs to a no-code asset (DEC-239, DEC-241) — so a moved
library makes that revision stale and it recompiles from its own source. Only a design that pins
library revisions would need a retention rule, and nothing here pins them.

**What loading once would cost**, on top of this plan:

- a registry and a collectible context per library revision, refcounted against the page contexts using
  it, with the render that outlives its deadline (`PageRenderer.Abandon`) extending its delayed
  disposal to a second assembly;
- an assembly identity per revision, because a page's metadata reference names exactly one, so the load
  path can fail where today it cannot — and it must turn that failure into "recompile this page", not
  a `500`;
- a portable PDB retained per library revision, or a stack trace in library code has no lines;
- and **shared statics**: one copy of the library's code makes a static field process-wide mutable
  state, shared by every page and every visitor. Pages cannot do that today, one context per render is
  why, and the API policy cannot see a field. That is a data boundary, not a performance trade.

Loading per render keeps the isolation but gives up the only two things sharing is for — one JIT and
one copy in memory — while still paying for the registry, the identity and the symbols.

So it is not built. Reopen it only on the numbers below, and if the answer is "the library is large
enough that N compiles hurt", cost the load-per-render shape properly rather than the shared one.

## What we will watch

No new instrument: every number is already recorded per `page.path` in `PageMetrics`.

- `page.compile.duration` — Roslyn's time for one page, tagged with its reason (`save`, `lazy`,
  `import`), so a dependent page's own code and its closure can be told apart by comparing before and
  after it names the library.
- `page.image.size` — bytes of the emitted assembly, the direct answer to "is the library dominating
  the image?".
- `page.load.duration` — loading a stored image into its context, which is where image size actually
  costs a visitor.
- `page.cache.staleness` — why an image was not reused; phase 3 adds the reason `library-changed`,
  which is also the count of pages one library edit invalidates.

Phase 3 and phase 4 each record the numbers in this plan's Status: the same page before and after it
names the library, a page with a library against one without, `/_layout` before and after it calls the
library document, and the staleness count after one library edit.

The trigger to reopen D12 is a render that spends more time compiling than running
(`page.compile.duration` against `page.invoke.duration`), or an image that is mostly library. Until
then it is a number on a dashboard, not a design.

## Phases

Each phase is its own branch and stops for the owner's merge. Phase 0 and 1 do not touch storage;
phase 2 is the migration; phase 3 is the mechanism; phase 4 is the seed and the document.

### Phase 0 — the kind (Core)

- `PageKind` in `Core.Pages`: `Text`, `Page`, `Library`.
- `TemplateGenerationResult.Kind`, judged by the token check in D1, with `HasCode` kept as a derived
  convenience or removed in favour of the kind.
- Tests beside `TemplateSourceGeneratorTests`: a declarations-only template is a library; declarations
  with whitespace are a library; one byte of text, one code block or one write makes it a page; an
  empty template is text.

### Phase 1 — generating and checking a library (Core + Runtime)

- `TemplateSourceGenerator.GenerateLibrary(template, key)` emits the page's own declarations, each
  `#line`-mapped to `lib:<key>`, and nothing else.
- `PageApiChecker` grows the library shape rule (D5); `Runtime` compiles a library alone for its
  diagnostics with no handler and no emit.
- Tests: a namespace and a type compile; a statement, a `GeneratedPage` and an assembly attribute are
  refused at the library's own position; a banned API inside a library is refused at its line; a
  library's `#nullable` handling matches a page's.

### Phase 2 — the kind in storage and serving (Web + migration)

- The migration: add `TemplateKind`, derive it from `TemplateHasCode`, drop that column.
- `PageImage`, `PageTemplate`, `PageImageState`, `PageEntry` and the read store follow the kind;
  `IsLibrary` joins `IsAsset` on `PageEntry`.
- `PageEndpoint`: a library key answers `404`, and so does a versioned URL naming one; nothing is
  rendered, wrapped or titled.
- The editor's listing says "Library" and offers no View link; the assistant's listing and read say
  the same word.
- `/help`: the three kinds, and that a library is not addressable.
- Tests: the endpoint and its versioned URL; the listing's word and its missing link; a library
  revision never satisfies `IsAsset`.

### Phase 3 — the closure (Runtime + Web)

- The namespace index, the fixed-point resolution, and the page's compilation with one tree per
  library (D2, D3).
- The policy's authored-tree set (D4).
- `Source` on both diagnostics, the mapping, the editor and the assistant (D6).
- `TemplateLibraries`, the fingerprint, `IsCurrent`, the staleness reason and metric (D7), and the
  `docs/metrics.md` row that lists the reasons.
- The callers: `PageImageBuilder` takes the candidates and the closure, `PageRenderer` reads them for
  the lazy path, `PageSaver` for the save path, `ImportProcessor` compiles the libraries in a batch
  first so a dependent imported beside its library is not reported as broken.
- `PageCompiler.FormatVersion` bumps once, so every stored image recompiles after the deploy.
- Tests: a page that uses a library compiles and runs; the closure is transitive; a library change
  makes exactly its dependents stale; a page with no library is untouched; a banned API in a library
  is reported at the library's key and line; a missing namespace is Roslyn's own error.
- The first numbers: `page.compile.duration` and `page.image.size` for a page with and without a
  library, and the `library-changed` staleness count after one edit, recorded in this plan's Status.

### Phase 4 — the seed, the manual, the document

- A seeded document library, if it earns its place: `PageAssetSeeder` gains a recursive read of
  `Pages/Seeds` and `FindSeedFile` allows a nested key, or the library is seeded at a root key.
- `/help`: the library kind end to end, with the static-state caveat and the `WebUtility` example of
  what the policy still denies.
- `PageDocument` merges into `ShellDocument` (D10), the seed calls the library (D11), the drift test
  lands, DEC-156 is superseded and IDEA-018 closes with it.
- The second numbers: `/_layout`'s compile duration and image size before and after it calls the
  library document, recorded beside phase 3's.
- The documents: [pages](../pages.md) (three kinds, the closure), [shell](../shell.md) (the layout is
  a page and the document is not `Sdk`'s), [storage](../storage.md) (two columns), [glossary](../glossary.md)
  (library page), [testing](../testing.md) if the suite's description moves.

## Storage

`PageRevisions` gains `TemplateKind` (nullable int) and `TemplateLibraries` (nullable text). The
migration:

1. adds both columns;
2. writes `TemplateKind` from `TemplateHasCode`, leaving `NULL` where it was never judged;
3. drops `TemplateHasCode`.

`TemplateLibraries` is written with the image and is an opaque fingerprint: nothing reads it back as
data, and a null or empty value means "no libraries", which is the truth for every page today. Old
rows recompile on their next request (the runtime id moved), so nothing is backfilled.

## Risks and unknowns

- **The index costs reads.** Resolving a closure means reading the live library pages on every
  compile. `n` is small and a compile is rare, and `page.compile.duration` will show it. A cache keyed
  by revision hash is the measured answer, not the first one.
- **A broken library is visible in several places.** Every dependent stores a compile error naming the
  library until it is fixed. That is honest, but it makes one edit look like several failures in the
  editor's list; the diagnostic's source is what keeps them readable.
- **Static state is per dependent.** A library's `static` fields are copied per page. `/help` must say
  so, because it is the one way a library behaves unlike a class in a normal project.
- **Image size and compile time are watched, not pre-optimised.** A library is copied into each
  dependent (D9). `page.image.size`, `page.compile.duration` and `page.load.duration` are the
  evidence, and D12 records what a shared assembly would cost if they ever say so.
- **Removing a public `Sdk` type recompiles the site.** The module version id changes, every image is
  stale, and a page that called `PageDocument` stores a compile error instead of serving. That is
  recoverable in the editor and from `/admin/restore`, and it is the accepted cost of D10.
- **Import order.** Compiling library entries first in the batch is a small ordering rule that the
  import processor has to hold; without it a dependent is reported broken for one run and fixes itself
  on the next request.

## What proves it

`make check` at every phase, and these behaviours specifically:

- a declarations-only page is not served, not wrapped, not titled, and is listed as a library;
- a page that writes `using Site.Document;` compiles, runs and calls the library's type;
- a library change makes its dependents stale and leaves every other page alone;
- a banned API in a library is refused at the library's key and line;
- a `GeneratedPage` or an assembly attribute in a library is refused at the library's position;
- the seeded layout's document matches the shell's, item for item;
- a library revision is never an asset, and never answers a versioned URL.

## Status

Nothing built. Phases 0–4 are unstarted, and the measurements phase 3 and phase 4 take are recorded
here when they land, beside the decision they would reopen (D12).

## Open questions

- **The seeded example.** A library seed is worth it only if the owner will read it; if not, `/help`
  is enough and the recursive seeder waits for a reason.
- **The state words.** "Library", "Library with warnings" and "Library failed" are the proposal;
  the editor, the assistant and `read_page` must agree on the spelling, and the decision is cheap to
  change while it is one function.

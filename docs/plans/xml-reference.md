<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The reference

| | |
| --- | --- |
| Status | **Revised from the owner's answers of 2026-11-14** — waiting for the go-ahead to build. Nothing is built |
| Size | Large — one reader and normaliser, the `inheritdoc` bridge, one embedded stylesheet, two public routes under `/admin`, the admin menu, the `/help` link audit, one seeded sheet, one assistant tool, the prose and focused tests; no migration, no configuration key, no build change. Without the copy-down it is a Medium; that is the lever |
| Spike | None. The facts below are measured on this branch: the publish carries the four XML files, their element set is known, a scratch project confirmed `XslCompiledTransform` transforms a document on .NET 11 with no package, and the Microsoft Learn URL shapes were checked against the live API browser |
| Decisions | The decision this plan records, numbered from `trunk` when the work merges: the documentation a build carries is served at `/admin/reference` for all four product assemblies and read by the assistant through one `reference` tool over the page contract only; the corpus is read once per process from the XML beside the assembly and rendered by one embedded stylesheet. The audit in [architecture](../architecture.md#where-a-view-lives) gains the fourth reason below |
| Backlog | [IDEA-034](../jobs/IDEA-034.md) — this plan is its development |

## The short version

[The analyzers work](analyzers.md) turned `GenerateDocumentationFile` on (DEC-290), so every product
assembly now emits its XML documentation, `dotnet publish` carries it, and the image packs it. Nothing
reads it. This plan gives it two readers:

- **A person** gets a public `/admin/reference`, in the admin menu and linked from `/help`: an index
  of the four assemblies this build carries, and one page per assembly listing every type and member
  with the words its author wrote — an `<inheritdoc/>` entry carrying the source's words and a line
  naming where they came from. A reference to a type the corpus does not hold links to its page on
  Microsoft Learn when the URL can be computed, and a link that leaves the site carries an arrow. It
  describes the build that is actually serving the site, not a version on GitHub, and the page
  contract's half is what a page author writes against.
- **The page assistant** gets a read-only `reference` tool over the **page contract only**, for the
  member's own words, parameters and returns. The prompt already carries a reflection-derived
  signature block; the tool carries what the signature cannot.

The corpus is read once per process, normalised once into an XML document with the type owner,
display names and anchors resolved, and then rewritten twice: by an embedded stylesheet into the
page's fragment, and by a small flattener into the tool's text. There is one parser and two outputs.

## What the build carries today

Measured from `artifacts/bin/uk.osric.asp.Web/release` on this branch. "Entries" is every `<member>`
element; the type's own documentation is one of them, and the page counts types and entries
separately so the two numbers are never confused.

| Assembly | Types | Entries | Of which members | File |
| --- | ---: | ---: | ---: | ---: |
| `uk.osric.asp.Sdk` | 34 | 194 | 160 | 99 kB |
| `uk.osric.asp.Core` | 119 | 572 | 453 | 226 kB |
| `uk.osric.asp.Runtime` | 14 | 47 | 33 | 29 kB |
| `uk.osric.asp.Web` | 326 | 1,509 | 1,183 | 549 kB |

The idea's member counts were taken at an earlier revision and counted the type entry as a member;
the table above is what the files hold now. The four files are ~900 kB, all four are present in the
`linux-musl-arm64` publish and in the Web and integration test outputs, and no build change is needed
to keep them there.

The facts that shape the design, each counted from the files:

| Fact | Consequence |
| --- | --- |
| The element set in use is `summary`, `remarks`, `param`, `paramref`, `typeparam`, `typeparamref`, `returns`, `exception`, `see` (`cref` and `langword`), `c`, `para`, `strong`, `em`, `list`/`item`/`description` (`type="number"`), and `inheritdoc` | Those are the elements the stylesheet must render. There is no `code`, `br`, `href`, table list or `value` today; the stylesheet still handles each, and an unknown element renders its children so no text is lost |
| Ten type ids are nested types (`Pages.PageSeedInstaller.SeedResult`), and `T:` entries interrupt the outer type's members | A member's owner is computed from its id, never from the nearest preceding `T:` |
| One explicit interface implementation has `#` in its id (`Web.Logging.LogScope.System#Collections#IEnumerable#GetEnumerator`), generic ids carry `` ` ``, `{`, `}` and `@` | A raw id is not a safe URL fragment: `#` truncates it and the rest need percent-encoding. The anchor is a readable slug derived from the id; the raw id is kept as the `title` |
| `inheritdoc` appears 0, 1, 5 and 151 times (Sdk, Core, Runtime, Web), and 147 of them carry nothing else | The copy-down pass takes the source's words where the source is ours; the rest name the source, or keep a one-line note when reflection cannot |
| 4, 9, 19 and 62 `cref`s name a type the document does not hold (the BCL, another product assembly, or a package) | A `cref` to a carried assembly links across the corpus; a `System.*` or `Microsoft.*` target becomes a Microsoft Learn link; anything else (`Markdig.*`) stays `code`. The link text is the shortened name (`string.Empty`, `Task`) |
| The XML holds documented `internal` types as well as public ones, because the file has no visibility metadata | The page says it is not a filtered public surface. Filtering would need a public-surface list, which is a job of its own |

## Where the view lives

The `/admin/changes` precedent applies exactly: the changelist is a file the image carries, and a
page cannot read a file beside the assembly. The XML documentation is the same shape of thing —
generated per build, packed beside the assembly, never owner-editable content. `/admin/reference` is
therefore a shell route, recorded as a **fourth reason** in the audit rather than folded into
bootstrap:

> **The build's own facts.** The changelist and the XML documentation describe the build that is
> serving the request. A page cannot read either file, and no capability should exist for one view.

`/admin/changes` moves from the bootstrap row to this one, because it is the same fact and saying it
twice hides the rule. The audit's opening sentence changes from three reasons to four, and the
glossary's **Bootstrap route** entry loses `/admin/changes` and gains a **Build fact** entry naming
both routes. The owner has accepted the label, noting that neither the changelist nor the reference
is really bootstrap; the short name is worth more than the precise sentence.

The assistant's access is a tool, not the page. It already sees every reserved key, but `cat` and
`read_page` answer the host-written `ShellRoutes` record — methods, access, purpose — not the route's
output, and `fetch`-ing `127.0.0.1:8080` is approval-gated, returns a large HTML body and is an
accident rather than an interface.

## The design

### D1 — Two public routes, one assembly per page

`GET /admin/reference` is the index. `GET /admin/reference/{slug}` is one of `sdk`, `core`, `runtime`
and `web`, matched exactly and case-sensitively; anything else answers `404` through
[`ErrorResponses`](../../src/uk.osric.asp.Web/Errors/ErrorResponses.cs) with a message that names the
slug and offers the index and the home page. Both are framed by `ShellDocument` like `/status`, and
therefore carry the security headers, `no-store`, the skip link, the landmark and the site menu
without writing any of them.

Both are **public**, even though the key is under `/admin`: the URL is a filing cabinet, not an
authorisation, and the reference describes the build serving the request as `/about` does. The
repository is published to be read, so the site's `internal` types are on GitHub either way. The one
consequence to write down is that [`docs/shell.md`](../shell.md) says "`/admin/**` requires the
`Editor` policy"; it gains the `/admin/reference` exception in this change.

Both pages also carry the admin menu (`AdminNavigation`, the "Page administration" landmark) with
its new Reference item marked current, because the route lives in the admin area: an anonymous
reader sees links to the owner's views and is sent to sign in by them, which is the same thing the
site menu does with its own owner links. The seeded sheet is `/reference.css`, not
`/admin/reference.css`, because every feature sheet is at the root (`/admin.css`, `/status.css`) and
the reserved prefix is `/admin/reference` and everything beneath it.

The assembly table is a constant in `Web/Documentation`, not configuration: the names are the
assembly names, and a wrong path is a build fault, not an operator's setting.

| Slug | Assembly | Title | One line |
| --- | --- | --- | --- |
| `sdk` | `uk.osric.asp.Sdk` | The page contract | What a stored page may name and use |
| `core` | `uk.osric.asp.Core` | The host's pure half | The template language, diagnostics, the API policy and the chart helpers |
| `runtime` | `uk.osric.asp.Runtime` | The page compiler | Emitting, loading and running a page |
| `web` | `uk.osric.asp.Web` | The site | Endpoints, hosting, storage and configuration |

### D2 — One corpus, loaded and normalised once

`ReferenceLibrary` is a singleton. Its first use reads `{AssemblyName}.xml` from
`AppContext.BaseDirectory` — the directory the changelist is read from — parses each file with DTDs
prohibited and a null resolver, and builds one normalised `XDocument` per assembly. It caches the
result for the process: the XML cannot change while the image runs.

A missing file is an assembly this build does not carry: it is absent from the index, and
`/admin/reference/{slug}` for it is a `404`. If no file is present at all, the index says so in a sentence,
as `/admin/changes` does — never a `500`. A malformed file is treated as absent and logged once with
a structured warning naming the assembly; the page still answers.

Loading is lazy so a host that never serves `/admin/reference` and never calls the tool pays nothing.

### D3 — The owner and the display are computed from the id

`ReferenceDisplay` is the pure part: string rules over an id, with no I/O. It is the only fiddly code
here, and it is tested member by member.

**The owner.** Strip the parameter list — everything from the first `(` — then the last `.`-separated
segment. `M:Ns.Outer.Inner.Method(...)` belongs to `Ns.Outer.Inner`; `M:Ns.Outer.Method(...)` belongs
to `Ns.Outer`; an explicit interface implementation leaves its `#` segments in the member name, so
`M:Ns.Type.Ns#IFoo#Bar(...)` still belongs to `Ns.Type`. A member whose owner has no `T:` entry gets
a synthesised type section, which is why the rule is not "the nearest preceding type".

**The type's namespace and name.** A type loses the assembly's root namespace:
`uk.osric.asp.Core.Charting.ChartChrome` is `ChartChrome` in namespace `Charting`, and
`uk.osric.asp.Sdk.IContext` is `IContext` in the global namespace. A nested type is recognised as a
type whose relative name starts with another type's relative name and a dot, so
`Pages.PageSeedInstaller.SeedResult` is `PageSeedInstaller.SeedResult` in the namespace `Pages`, with
local name `SeedResult`. Types are grouped by namespace, the global namespace first and the rest in
ordinal order; types within a namespace are in ordinal order by name; members are in the order the
compiler emitted them.

**The display.** A type keeps its name, with its documented type parameters (`PageValue<T>`). A
generic method shows its type parameters after its name (`TryGetValueAsync<T>`), from its
`<typeparam>` elements or `T`, `T1` when they are not documented. A member is its name, parameters
and shortened parameter list. The rules:

| Raw id | Display |
| --- | --- |
| `T:uk.osric.asp.Sdk.IContext` | `IContext` |
| `P:uk.osric.asp.Sdk.IContext.Request` | `Request` |
| ``M:uk.osric.asp.Sdk.IPageStorage.TryGetValueAsync``1(System.String)`` | `TryGetValueAsync<T>(string key)` |
| ``M:uk.osric.asp.Sdk.IPageCapabilities.TryGet``1(``0@)`` | `TryGet<T>(ref T capability)` |
| `M:uk.osric.asp.Sdk.HttpFetchRequest.#ctor(System.String)` | `HttpFetchRequest(string Url)` |
| ``T:uk.osric.asp.Sdk.PageValue`1`` | `PageValue<T>` |
| `M:uk.osric.asp.Web.Logging.LogScope.System#Collections#IEnumerable#GetEnumerator` | `System.Collections.IEnumerable.GetEnumerator()` |
| `F:uk.osric.asp.Sdk.HttpFetchFailure.None` | `None` |

The shortening: the assembly's root namespace is dropped from parameter types as well;
`System.String` and the other primitives become keywords; `IReadOnlyList{X}` becomes
`IReadOnlyList<X>` (`{`/`}` become `<`/`>`); `Nullable{X}` becomes `X?`; an array stays `X[]`; a
trailing `@` becomes `ref `; a generic placeholder `` `0 `` becomes the member's documented
`<typeparam>` name, or `T`; `#ctor` becomes the type's local name and `#cctor` its `static` form.
Parameter names are zipped in from the `<param>` elements when its count matches the id's parameter
count, and omitted when it does not. By-ref parameters render as `ref` because the id does not say
whether it was `ref`, `out` or `in`; the summary is where that is said.

### D4 — Stable anchors, and `cref` resolved across the corpus and the platform

An anchor is derived from the id, not from a position. The `t3`/`m4` ordinal this plan first
proposed would move whenever a member was added, and `/help` now links to anchors, which makes them a
published interface rather than an implementation detail.

- A type is `t-` and its relative name, with each `` `n `` written `-n`: `t-IContext`,
  `t-Pages.PageSeedInstaller.SeedResult`, `t-PageValue-1`.
- A member is `m-`, the relative type name, `.`, the member name, then one `-` segment per parameter
  type: `m-IContext.Request`, `m-IContext.FlushAsync`, `m-IContext.Redirect-string-int`,
  `m-IPageStorage.TryGetValueAsync-g1-string`,
  `m-Logging.LogScope.System.Collections.IEnumerable.GetEnumerator`. The member's own arity is
  `-g{n}` so `Foo` and `Foo<T>` differ; a `@` parameter is `-ref`, an array `-array`, a nullable
  `-nullable`; `#ctor` is `ctor` and `#` is `.`.

The raw id stays in a `title` attribute so a shortened display never loses information, and it stays
the key of the lookup map. The mapping is injective for real ids because a type name cannot contain
the characters that are sanitised away; the build still asserts uniqueness and, in the impossible
case, appends `-2` in document order. The real test is that the four documents produce no duplicate
anchor. Raw ids cannot be anchors at all: `#` truncates a URL fragment, and backtick, `{`, `}` and
`@` are not fragment characters.

Anchors are assigned while each document is built; one pass then builds an
`id → (slug, anchor, text)` map across all four documents and resolves every `<see cref>`:

1. **In the corpus.** The target is a type or member of any carried assembly: the link is
   `/admin/reference/{slug}#{anchor}` and the text is the target's name.
2. **On Microsoft Learn.** An unresolved target in a `System.*` or `Microsoft.*` namespace gets a
   link to the API browser. The path is the id without its kind, parameter list and `~` return
   suffix, lowercased invariant, with each type segment's `` `n `` written `-n` and a member's own
   arity dropped. A field, event or constructor links to its declaring type's page instead of trying
   its own: Learn has no page for an enum member or a nested constructor, and the type's page lists
   every member.
3. **Not computable.** A `Markdig.*` type, an id with `#`, a type of ours with no documented entry,
   or any other namespace stays `code` with the shortened name.

The base is `https://learn.microsoft.com/en-us/dotnet/api/`, with no `view` query: Learn serves its
current version, and a pinned version becomes a 404 as it ages. Nothing is fetched to build a link;
the URL is computed from the id, so the page works with no network and no test needs one. A computed
URL can still 404 if Learn renames a page; that is the cost of not checking, and the link is worth it
for the common case.

The shapes were checked against Learn while this plan was written: `system.string`,
`system.collections.generic.ireadonlylist-1`, `system.collections.generic.dictionary-2.enumerator.current`,
`system.uri.idnhost`, `system.string.chars`, `system.string.join` (with `join-1` a 404, so method arity
must go), `system.collections.generic.list-1.-ctor`, `system.string.op_implicit`,
`microsoft.aspnetcore.http.iresult`, and `system.string` for `String.Empty` (with
`system.text.regularexpressions.regexoptions.nonbacktracking` and
`system.environment.specialfolder.desktop` 404s, so a field goes to its type).

Cross-assembly links are the point of building one corpus: `Web.xml` has 62 crefs to the BCL and to
`Core`, and a reader who meets `PageDiagnostic` should land on its entry.

This is an annotation pass over the normalised tree, not a second model: the stylesheet sees an
element that already knows its `href` and `text`. A document looks like this on the way in:

```xml
<reference slug="sdk" assembly="uk.osric.asp.Sdk" title="The page contract">
  <namespace name="">
    <type id="T:uk.osric.asp.Sdk.IContext" anchor="t-IContext" name="IContext" local="IContext">
      <summary>What a page can read from the request and ask of the response.</summary>
      <remarks>…</remarks>
      <member id="P:uk.osric.asp.Sdk.IContext.Request" kind="Property" anchor="m-IContext.Request" display="Request">
        <summary>The request snapshot.</summary>
      </member>
      <member id="M:uk.osric.asp.Sdk.IContext.Write(System.String)" kind="Method"
              anchor="m-IContext.Write-string" display="Write(string message)">
        <summary>Writes escaped HTML to the output.</summary>
        <param name="message">The text to encode and write.</param>
      </member>
    </type>
  </namespace>
</reference>
```

### D5 — The stylesheet is the renderer

`Documentation/reference.xslt` is an embedded resource of `Web`, loaded once with
`XsltSettings.Default` (script and `document()` off) and a null resolver, and applied with
`XslCompiledTransform`. `System.Xml.Xsl` is in the framework; no package is added. A C# node model
and renderer would be a second escaping path and more code, and browser-side XSLT would lose the
server cache and the accessibility tests.

The fragment is:

- a `<nav class="type-index" aria-label="Types in this assembly">`, with a namespace label and a
  list of links to each type section; the global namespace's label is the assembly's name;
- one `<div class="namespace">` per namespace, with the same non-heading label;
- one `<section class="type">` per type containing an `<h2 id="t-IContext" title="T:…">` and the
  type's own summary and remarks;
- one `<div class="member">` per member containing an `<h3 id="m-IContext.Request" title="P:…">`, a
  kind label, the signature in a `<code>`, the summary, then `<h4>` blocks for parameters, type
  parameters, returns, exceptions and remarks when the member has them.

The endpoint writes the `h1`, the assembly's count line and the link back to the index; the fragment
follows inside the landmark. The seeded `reference.css` styles the type index, the namespace labels,
the signature and kind lines, the parameter lists and the code blocks; nothing about the page depends
on it, and a deployment that lost the sheet still reads.

The sheet also marks a link that leaves the site:

```css
a[href^="http"]::after {
    content: " ➸" / "";
}
```

The arrow is `➸` (U+27B8), the owner's preference: U+2197 is the conventional external-link mark but
it is ugly, and the glyph is a one-line change if this one looks wrong on the page. The empty
alternative text keeps a decorative glyph out of the link's name where the browser supports it, and
where the browser does not the declaration is dropped and the arrow is simply absent. The rule is in
`reference.css` rather than `site.css`, so it marks the Learn links and the commit link on these two
pages and nothing else; moving it to the base sheet makes it site-wide. The link opens in the same
tab: an arrow is a signal, not a reason to open a window the reader did not ask for.

A type is a `<section>` with the `<h2>` as its heading and no `aria-labelledby`: an unnamed section
has no region role, so the page does not add 326 landmarks while the markup still says a type is a
section of the document. A member is a `<div>`: 1,183 named regions would be noise in a screen
reader's landmark list, and the headings already carry the structure. The accessibility rules see an
`h1` in the endpoint followed by `h2`, `h3` and `h4` without a skip.

Inline doc elements map as follows:

| Doc element | HTML |
| --- | --- |
| `see cref` | `<a href>` to the corpus entry or to Microsoft Learn; `<code>` with the shortened name when the target is not computable |
| `see langword` | `<code>` |
| `see href` | `<a href>`, the text or the URL |
| `see` with content | `<a>`/`<code>` around its own text |
| `paramref`, `typeparamref` | `<code>` |
| `c` | `<code>` |
| `code` | `<pre class="source" tabindex="0"><code>` |
| `para` | `<p>` |
| `b`, `strong` / `i`, `em` | `<strong>` / `<em>` |
| `br` | `<br>` |
| `list type="bullet"\|"number"` | `<ul>` / `<ol>` with `<li>` |
| `list type="table"` | `<table>` with `<thead>` and `th scope="col"` |
| `inheritdoc` | nothing of its own; when it is the only content, one quiet sentence |
| anything else | its children, so no text is silently dropped |

No inline styles, no scripts, no forms, every `nav` named: the shared rules in
[`AccessibilityTests`](../../test/uk.osric.asp.Web.Tests/AccessibilityTests.cs) apply unchanged.

### D6 — Each fragment is rendered once

The `XDocument` cannot change while the process runs, so each assembly's fragment is transformed on
its first request and held as a string for the life of the process. `Web.xml` is 549 kB of source and
its fragment is a few megabytes; one copy is cheaper than a transform per request, and a `Lazy<T>`
with the default publication mode makes concurrent first requests share one transform. The index
reads its counts from the normalised documents and never transforms.

The cost is bounded and one-off: the corpus is ~5 MB of trees and the fragments are a few MB each.
Nothing is persisted and nothing survives a restart, which is right for a description of the running
build.

### D7 — The assistant's tool is the contract only

`Assistant/Tools/ReferenceTool.cs` is read-only, approval-free (`Writes = false`,
`NeedsApproval = false`, `Verb = "Reference"`) and answers from the **`uk.osric.asp.Sdk` document
only**.
The page API admits the contract and a slice of the BCL; documenting `Core`, `Runtime` and `Web` to
the model would invite code the compiler refuses. The human page still covers all four.

Two arguments, exactly one per call:

- `reference type="IPageStorage"` answers the type's summary, remarks, type parameters and every
  member with its signature, summary, remarks, parameters, returns and exceptions.
- `reference query="cookie"` searches type names, member names and summaries, case-insensitively, and
  answers the type that holds each match so the next call can ask for it.

A third, optional `limit` bounds the query's matches with `Assistant:DefaultListLimit` and
`Assistant:MaxListLimit`, the pair every listing already uses; `truncated` says there is more. A
refusal for both, neither or an unknown type names the accepted form and points at `query`; a type
that is in another assembly says the tool reads the page contract only. The whole envelope is still
bounded by `Assistant:MaxToolResultChars`, so no tool can bypass the configured cap. A build with no
`Sdk` document — a publish that lost the file — refuses with a sentence saying so, rather than
answering an empty type.

A type lookup accepts the local name (`IPageStorage`), the full relative name
(`uk.osric.asp.Sdk.IPageStorage`), a generic form (`PageValue<T>`, with arity and arguments ignored)
or the raw `T:` id. The lookup consults the whole corpus so a `Core` or `Web` type is refused by name
and assembly — "`PageDiagnostic` is in `uk.osric.asp.Core`; this tool reads the page contract" —
rather than as an unknown string; the answer is still the contract only. An ambiguous short name is
refused with its candidates named; a missing type is refused with a suggestion to use `query`.

`ReferenceText` flattens the same document for the tool: paragraphs separated by a blank line,
`see` as its resolved text, `paramref`/`typeparamref` as the name, `c`/`code` as their text, lists as
`- item` or `1. item`, `strong`/`em` as their text, `inheritdoc` as nothing, and an unknown element
as its children. Whitespace is collapsed inside a paragraph and preserved in a code block.

`ToolServices` gains `Reference`, resolved from the call's scope only when a tool reads it, so a tool
that does not need the corpus never creates one. `ToolRegistry.Default` gains the tool at the end of
the list (the order is part of the provider's cached prefix, so appending does not disturb it), and
`ToolSpecsTests` moves from eighteen to nineteen.

### D8 — The prompt says where to look

One paragraph in `PageAuthorPrompt`, beside the page API policy: `reference type=…` when the type is
known and `reference query=…` when it is not; it answers the contract's own words, so prefer it to
guessing a signature; it covers the page contract only, and `/admin/reference` is where a reader sees the
host's own assemblies. The shell-files paragraph gains `/admin/reference`. `Version` becomes
`page-author-v28`, so a transcript says which prompt a run was given.

The `IContext` signature block `PageAuthorPrompt` derives by reflection stays: it is cheap, always
present and always true. The tool is for the member's own words, which the signature cannot carry.

### D9 — What the page says about itself

Two facts are stated on the index rather than left to be discovered:

- the reference is the documentation the compiler emitted, not a filtered public surface: a
  documented `internal` type is shown, and an `<inheritdoc/>` entry either carries the source's words
  with a line saying where they came from or, when the source is not ours, its signature and a note;
- it describes the build serving this request, and the commit it was made from is linked, as
  `/about` does.

### D10 — No configuration, migration or build change

The files are found beside the assembly by their fixed names; there is no new setting. The database is
untouched. The image is unchanged: the publish already carries the four XML files and the container
targets copy the publish output as it is. The only project-file edit is the embedded stylesheet, and
every new type is `internal`, so the `PublicAPI` files are untouched.

The seeded sheet is `/reference.css`, which the reserved prefix does not shadow: `ReservedPaths`
matches `/admin/reference` exactly and everything beneath `/admin/reference/`, and `/reference.css` is neither.

### D11 — `inheritdoc` is copied down where the source is ours

151 Web entries are `<inheritdoc/>` and 147 carry nothing else, so a signature with no words is the
common case, not the exception. The owner would rather read the source's words, and for a member of
one of our types the source is in the corpus: 89 entries name a source by member name and signature
alone, and reflection can name the rest exactly.

`ReferenceDocIds` is a pure bridge from a `MemberInfo` or `Type` to the id Roslyn writes. Its test
rebuilds every id in the four real files — about 2,300 — from reflection and compares, so an entry
the bridge cannot name fails the suite rather than silently losing text. `ReferenceInheritance` uses
it to find an `<inheritdoc/>` source: an override's `GetBaseDefinition()`, the interface member the
implementation maps to through `GetInterfaceMap` (with the accessor path for a property or event), or
a type's base type. Five migration types inherit from EF Core's `Migration`, which is not in the
corpus, so they are the type-level case of the note below.

The source's text is copied, its `<param>` and `<typeparam>` names rewritten by position from the
implementation's own metadata (so the page's parameter names are the implementation's, not the
interface's), and the entry records `inherited-from` plus a link to the source's anchor. The page
shows the copied words and one line: "Inherited from `IPageStore.GetAsync`.".

Three cases come out differently:

- **The source is in the corpus.** Copy the text and link the line to the source entry, across
  assemblies where that is where it lives.
- **The source is outside it** — `IEnumerable.GetEnumerator`, `Dispose`, `ISecureDataFormat`'s
  members, EF Core's `Migration`. Nothing is copied; the line names the source and links to Microsoft
  Learn when its URL is computable.
- **Reflection cannot name it.** The entry keeps the note, "Inherits its documentation.", and the
  page still reads.

The copy is best-effort: an assembly, type or member that does not resolve is a missing copy, never a
failure. The XML and the assemblies are one build, and a test asserts the bridge over the real corpus.
Recursion follows an `<inheritdoc/>` source to a small depth. This is the one part that reflects over
the product assemblies; it is read-only, over types the process already loaded, and never leaves the
four names. The tool is unaffected: `Sdk` has no `<inheritdoc/>` entries.

## What it touches

| Area | Change |
| --- | --- |
| `src/uk.osric.asp.Web/Documentation/` | New: `ReferenceAssemblies`, `ReferenceDisplay`, `ReferenceDocument`, `ReferenceDocIds`, `ReferenceInheritance`, `ReferenceLibrary`, `ReferenceRenderer`, `ReferenceText`, `ReferenceEndpoint`, `reference.xslt` |
| [`uk.osric.asp.Web.csproj`](../../src/uk.osric.asp.Web/uk.osric.asp.Web.csproj) | The stylesheet as an `EmbeddedResource` |
| [`Program.cs`](../../src/uk.osric.asp.Web/Program.cs) | The `ReferenceLibrary` singleton and the two `MapGet`s, before the page catch-all |
| `Assistant/Tools/ReferenceTool.cs` | New tool |
| [`ToolRegistry.cs`](../../src/uk.osric.asp.Web/Assistant/Tools/ToolRegistry.cs), [`ToolServices.cs`](../../src/uk.osric.asp.Web/Assistant/Tools/ToolServices.cs) | Register the tool; resolve the corpus lazily |
| [`PageAuthorPrompt.cs`](../../src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs) | The reference paragraph, `/admin/reference` in the shell-file list, `v28` |
| [`Pages/ReservedPaths.cs`](../../src/uk.osric.asp.Web/Pages/ReservedPaths.cs), [`appsettings.json`](../../src/uk.osric.asp.Web/appsettings.json) | `/admin/reference` |
| [`Admin/AdminNavigation.cs`](../../src/uk.osric.asp.Web/Admin/AdminNavigation.cs) | A Reference item before Help, marked current on the two reference pages |
| [`Assistant/ShellRoutes.cs`](../../src/uk.osric.asp.Web/Assistant/ShellRoutes.cs) | One record, one line, pointing the model at the tool |
| `Pages/Seeds/reference.css` | New seeded sheet, including the external-link arrow |
| [`Pages/Seeds/help`](../../src/uk.osric.asp.Web/Pages/Seeds/help) | The reference link beside the page API paragraph, and a link on the first mention of each type and member in a section (DEC-173) |
| [`docs/shell.md`](../shell.md), [`docs/assistant.md`](../assistant.md) | The routes and the tool; the `/admin/**` sentence gains its `/admin/reference` exception |
| [`docs/architecture.md`](../architecture.md#where-a-view-lives), [`docs/glossary.md`](../glossary.md) | The audit's fourth reason, the **Build fact** entry, bootstrap's list |
| [`docs/jobs/IDEA-034.md`](../jobs/IDEA-034.md), [`docs/jobs/README.md`](../jobs/README.md) | The plan link now; the job moves to Closed when the work merges |
| [`docs/decisions.md`](../decisions.md) | One entry, numbered from `trunk` at the merge |
| `test/uk.osric.asp.Web.BrowserTests/` | One test: the external-link arrow, which no string proves |

## Phases

Each phase is test-first and leaves the tree green. Phases 1–3 build nothing a visitor can see;
phase 4 is the first visible change; phases 5 and 6 finish the tool and the record.

### 1 — The corpus

`ReferenceAssemblies`, `ReferenceDisplay`, `ReferenceDocument`, `ReferenceLibrary` and the target map,
with no route, no DI and no stylesheet.

Tests, using inline XML strings and a temp directory:

- the owner rule: a plain member, a nested type's member, a constructor, an explicit interface
  implementation, and a member whose owner has no `T:` entry;
- the namespace and name rule: a global-namespace type, a namespaced type, a nested type, and an
  ordinal order that does not depend on culture;
- the display rule: every row of the table above, plus `Nullable`, an array, `{`/`}`, `@`, `` `0 ``,
  `#ctor`, `#cctor` and the parameter-name zip with matching and mismatching counts;
- the anchor rule: every example in D4, an overload pair (`Redirect-string`,
  `Redirect-string-int`), a generic member (`-g1`), an explicit interface member, and the same
  document built twice giving the same anchors while an inserted member moves no other anchor;
- the Learn URL: a type, a generic type (`-1`), a nested type, a property, an indexer, a method, a
  generic method (arity dropped), a field and a constructor (the declaring type's page), a
  `Microsoft.*` type, and a `Markdig` type and an id with `#` (no link);
- the document: nested `T:` entries interrupt the outer members and the owner is still right;
  members stay in source order; anchors are unique across the four real documents; types and members
  are counted;
- `cref`: a same-assembly link, a cross-assembly link, a `System.*` Learn link, an unresolvable
  package cref that stays `code`, and a `see` with its own text;
- the library: a missing file is absent, a malformed file is absent and logged, the corpus is read
  once for two calls, and an empty directory is an empty corpus.

### 2 — `inheritdoc` copied down

`ReferenceDocIds`, `ReferenceInheritance` and the provenance fields, with no route.

Tests: the bridge rebuilds every documented id in the four real files from reflection; an override
copies its base's text; an interface implementation copies the interface's text across assemblies; a
property accessor maps as well as a method; `<param>` names are rewritten to the implementation's;
a source outside the corpus names the source and links to Learn; a member reflection cannot resolve
keeps the note and does not throw; recursion stops at the depth cap; a fixture document with no
matching assembly copies nothing.

### 3 — The stylesheet

`reference.xslt`, `ReferenceRenderer`, the embedded resource and the fragment cache.

Tests: the type/member/heading structure, a `<section>` per type and a `<div>` per member; text and
attribute escaping; a corpus `cref` link and a Learn `cref` link, and an unresolvable one as `code`;
`langword`, `paramref`, `typeparamref`, `c`; `para`; both list kinds and a table list with
`thead`/`th scope`; `strong`/`em`; inherited content with its "Inherited from" line and link, and the
note when there is no source; an unknown element keeping its text; no `<style>`, no `style`
attribute, no script; the same document rendered twice is one transform.

### 4 — The human page

`ReferenceEndpoint`, the two routes under `/admin`, `ReservedPaths`, the shell record,
`AdminNavigation`, `reference.css`, the `/help` audit, `ShellViews` and the accessibility suite, and
the docs.

Tests: the index lists this build's four assemblies with their counts and the commit link; an empty
corpus says so; `/admin/reference/sdk` names the title and shows a type and a member; an unknown slug
is a `404` error document; `no-store` and the content type; the seeded sheet is linked; the admin
menu marks Reference current; the shell record says the contract half is read with the tool; every
accessibility rule over both documents; the shell route's record and the reserved list stay in step.

The `/help` audit links the first mention of a type or member in a section — not inside a code block,
and not a second time in the same section, so the page does not become a field of links. A test reads
the seed, extracts every `/admin/reference/{slug}#{anchor}` link and asserts the anchor exists in the
rendered page for that slug, so a rewritten help page cannot rot. A changed seed reaches an existing
deployment through `/admin/restore`; a fresh one gets it at install.

Integration: `/admin/reference` and `/admin/reference/sdk` answer `200` to an anonymous client — the
route is public although its key is under `/admin` — and the SDK page contains `IContext` and a
member of `IPageStorage`.

Browser: on `/admin/reference/sdk`, a Learn link's `getComputedStyle(link, '::after').content`
carries the arrow and an internal corpus link's is `none`, which is the one thing a string cannot
prove.

### 5 — The assistant's tool

`ReferenceTool`, the registry, `ToolServices`, the prompt, and `docs/assistant.md`.

Tests: lookup by local name, relative full name, generic form and raw id; an ambiguous name; an
unknown type; both, neither and a blank argument; a query that matches a type name, a member name and
a summary; an empty query result; the limit and `truncated`; a result bounded by
`MaxToolResultChars`; a type in another assembly refused with the SDK-only sentence; the flattener's
paragraphs, `see`, `paramref`, `code` and lists; the spec is read-only and approval-free; the
registry holds nineteen tools; the prompt names the tool, the `/admin/reference` path and `v28`.

### 6 — Close-out

The job moves to Closed in [jobs](../jobs/README.md) with a summary, `docs/decisions.md` gains the
entry with the next `DEC-` number from `trunk`, and every citation in the branch is updated in the
same merge.

## Verification

1. `make check` — format, licences, audit, the seeded-page check, every suite and the Release publish.
   The new XSLT needs its SPDX header or `make license-check` fails.
2. `make image`, then an export that confirms `/app/uk.osric.asp.*.xml` is still there and a request
   to `/admin/reference` from the running container. The image gains one file, the seeded sheet; the
   stylesheet is inside the assembly and the XML files are rebuilt with it. If the XML files are
   missing, the page says so rather than failing.
3. `make test-browser`, which runs the arrow test against the real host.
4. `make run` and a read of `/admin/reference`, `/admin/reference/sdk` and `/admin/reference/web` in a browser: the
   fragment, the type index, the anchors, the Learn links with their arrow and the sheet.
5. With the assistant enabled, one run that asks a question the contract answers (`IPageStorage`) and
   one that does not know the type (`cookie`), and a read of the transcript's tool calls.

## Risks

- **XSLT 1.0 is old.** Whitespace, attribute-value templates and output escaping are the sharp edges.
  The rule set above is small and every element has a test; the stylesheet is loaded with script and
  `document()` off, so the worst case is ugly markup, not code execution.
- **The page is large.** `Web` is 326 types and 1,183 members in one response. It is a reference, not
  a landing page; the type index is the navigation, and the fragment is cached. A per-namespace page
  is a later job if the page proves too big to use.
- **Learn links are computed, not checked.** A page Learn renames, or a member it never documents,
  answers `404`; the namespace guard and the declaring-type fallback keep the known shapes correct,
  and everything else stays text. A link checker over the rendered pages would be a separate job, and
  a network call at render time is not one.
- **Memory.** The corpus and the fragments are held for the process. The measured source is ~900 kB;
  the trees and fragments are a few MB. This is the price of a per-process cache and is bounded by
  the four fixed assemblies.
- **The XML is not a public surface.** A documented `internal` type is shown, and a member whose
  source is outside the corpus has no inherited text. The page says both; filtering needs a generated
  public-surface list and copying BCL text needs the BCL's own XML, each a job of its own.
- **The `inheritdoc` bridge reflects over a build.** If the XML and the assemblies disagree, entries
  lose their copy rather than the page failing; the round-trip test makes that loud, and a generic or
  explicit member the bridge cannot name keeps the note.
- **A public route under `/admin`.** The key says administration and the access is anyone;
  [`docs/shell.md`](../shell.md) records the exception and an integration test pins the anonymity.
  If that is wrong, owner-only is one `RequireAuthorization`.
- **The tool can be wrong about modifiers.** An id says by-ref, not `ref`/`out`/`in`, so the
  signature says `ref` and the summary carries the truth. The alternative is a second source of
  parameter facts, which the XML does not have.
- **Prompt drift.** The prompt paragraph and the tool's description must agree about the SDK-only
  boundary; a test asserts the prompt names the tool, and the tool's refusal names the boundary.

## The owner's answers

The owner answered the six questions on 2026-11-14; this revision is the result.

1. **The route** is `/admin/reference`, not `/reference`.
2. **Public**, yes: the code is on GitHub anyway. The GitHub source links the owner wants are a later
   job, below.
3. **All four assemblies**, as proposed.
4. **The fourth audit reason**, accepted grudgingly: neither the changelist nor the reference is
   really bootstrap, but the precise reason is too long for a label.
5. **The admin menu and `/help`**, and the help page links the types and members it names.
6. **Stable anchors** (synthetic, not human-facing), **`<section>` for types**, cross-assembly
   `crefs` strongly wanted, and **`inheritdoc` copied down** where it can be.

Three things follow from the answers that the questions did not ask:

- **The anchors changed shape.** An ordinal `t3`/`m4` shifts when a member is added, and a `/help`
  link makes the anchor a published thing, so D4 derives it from the id instead.
- **The copy-down needs a reflection bridge** (D11), which is why the size is Large. Dropping it
  returns the job to Medium and leaves the note on the 147 entries that carry nothing else.
- **The arrow is `➸`**, not `U+2197`, which the owner called ugly.

## Not in scope

- **Versioned documentation for older builds.** The image carries one build's XML; that is the one
  the reference describes.
- **Filtering internals by accessibility, or generating a public-surface list.** The page states the
  fact; filtering is its own job if it is wanted.
- **Copying a BCL member's text into an `<inheritdoc/>` entry.** The BCL's XML is not in the image,
  so the entry names the source and links to Learn instead.
- **A link to each type's or member's source on GitHub.** The XML carries no file or line, so it
  needs a generated id → file:line map at build time; that is the owner's TODO of 2026-11-14 and
  enough plumbing to be its own job.
- **Searching across assemblies in the human page.** The page is a document per assembly; the query
  action belongs to the assistant tool and is SDK-only. A reader navigates by the type index.
- **Editing the documentation from the site.** The XML is build output; the source comments are the
  thing to edit.
- **A page capability to read the XML.** A new public `Sdk` surface for one generated artifact, when
  the shell already owns the build artifacts a page cannot read.

## Considered and rejected

- **Browser-side XSLT** (`<?xml-stylesheet?>` on the raw XML): no server cache, no server tests, a raw
  XML response with its own content type and CSP questions, and the rendering is invisible to the
  shared accessibility rules. The oldschool part worth keeping is the stylesheet, not the browser
  doing the work.
- **A C# doc-node model and renderer in `Core`**: this is a tree rewrite, which XSLT expresses in
  templates; C# stays for the one thing XSLT 1.0 is bad at — parsing the member ids. The tool's text
  view is a flattener over the same tree, not a second model.
- **Putting the reader in `Core`**: the normaliser is pure, and the architecture's table says parsing
  lives in `Core`. But this parses one view's build artifact, both of its consumers are in `Web`, and
  the precedent is `Admin/ChangesEndpoint`, which parses `changes.txt` beside the view it serves.
  Putting it in `Core` would make it public surface for one assembly, which DEC-11 does not want.
- **Raw ids as anchors**: `#` truncates the fragment and generic ids carry characters a URL fragment
  cannot hold. The readable slug costs one pure function and keeps the raw id as `title`.
- **An ordinal `t3`/`m4` anchor**: stable only while nothing is added before it, and `/help` links to
  anchors now, so a new member would break a published link.
- **Name-and-signature matching for `inheritdoc`**: 89 entries resolve to exactly one candidate and
  19 to several, but a match can be the wrong interface's words. Reflection names the source, which
  is why D11 pays for the bridge.
- **Named regions for the type sections**: 326 `region` landmarks is noise; an unnamed `<section>`
  keeps the semantics without them.
- **Serving the raw XML files as pages or assets**: they are an internal representation, not one a
  reader wants, and `/admin/reference` exists to do the transformation once.
- **A stored page generated at build time**: it would freeze at install, duplicate the seed/restore
  machinery, and stop matching the running build.
- **A condensed SDK index in the system prompt**: a permanent token cost on every step that still
  carries no summaries, and the model would call the tool anyway for the member's own words.
- **A configuration key for the directory**: the file names are the assembly names. A setting would
  let a typo switch the reference off silently, and the changelist's key exists because `make
  changelist` chooses the file's name.

**Origin:** the owner's question of 2026-11-14, recorded as [IDEA-034](../jobs/IDEA-034.md) after
[the analyzers plan](analyzers.md) turned the XML documentation on (DEC-290) and the image was found
to carry it. The plan was revised the same day from the owner's answers: the admin route and menu,
the public access, stable anchors, the help audit, the `inheritdoc` copy-down, and the Microsoft
Learn links and external-link arrow that the second review added.

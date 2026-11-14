<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-034 — The XML documentation is built and packed, and nothing reads it

| | |
| --- | --- |
| Size | M — a shell route, one stylesheet, one normaliser, one assistant tool, a seeded sheet and the prose; no migration |
| Area | Web (documentation, assistant), seeds, docs |
| Location | `src/uk.osric.asp.Web/Documentation/`, `src/uk.osric.asp.Web/Assistant/Tools/`, `src/uk.osric.asp.Web/Pages/Seeds/`, `src/uk.osric.asp.Web/Program.cs`, `src/uk.osric.asp.Web/appsettings.json`, `docs/shell.md`, `docs/assistant.md` |

## Idea

[The analyzers work](../plans/analyzers.md) turned `GenerateDocumentationFile` on (DEC-290) and made
`CS1591` a build failure, so every product assembly emits its documentation. The files are already
where a running site can reach them:

| Assembly | Types | Members |
| --- | ---: | ---: |
| `uk.osric.asp.Sdk` | 34 | 193 |
| `uk.osric.asp.Core` | 119 | 572 |
| `uk.osric.asp.Runtime` | 14 | 47 |
| `uk.osric.asp.Web` | 320 | 1,490 |

`dotnet publish` puts each XML beside its DLL, and the SDK's container pack copies the publish output
as it is: a fresh arm64 image exported on 2026-11-14 carries all four at `/app/`. No build or image
change is needed. **Nothing reads them.** The files ride in the image at the cost of about 900 kB and
are never served, searched or shown to anyone.

Two readers would use them:

- **A person** reading the site wants a reference for the build that is actually serving it, the way
  `/about` names the commit and `/admin/changes` lists the history. The page contract in particular is
  what a page author writes against, and today its documentation exists only in the source.
- **The page assistant** writes pages and already carries a hand-written summary of the contract in
  `PageAuthorPrompt`; it cannot read the member's own words, its parameters or its remarks. The XML is
  the one place those are kept in step with the code.

The shell already has the precedent for the human page. The `/admin/changes` row of the
[view-lives audit](../architecture.md#where-a-view-lives) is a shell route because the changelist is a
file the image carries, which a page cannot read. The documentation files are the same shape of thing:
generated per build, beside the assembly, not owner-editable content.

The assistant's route is different. It already sees every key in `Pages:ReservedPaths`, but `cat` and
`read_page` answer the host-written `ShellRoutes` record — methods, access, purpose — not the route's
output, because the image carries the assembly and not the source tree. `fetch` could pull the HTML
from `127.0.0.1:8080`, but that is approval-gated, returns a large HTML body and is an accident rather
than an interface. The useful access is a tool over the same corpus.

## Sketch

1. **Load and normalise once.** `Web/Documentation/ReferenceDocuments.cs` finds the four XML files
   beside the assembly (`AppContext.BaseDirectory`), reads them with DTDs prohibited, and builds one
   normalised document per assembly:

   ```xml
   <reference assembly="uk.osric.asp.Sdk" display="The page contract">
     <type id="T:uk.osric.asp.Sdk.IContext" name="IContext" namespace="" local="IContext">
       <member id="T:…" kind="T" display="IContext">…the doc elements…</member>
       <member id="P:…IContext.Request" kind="P" display="Request">…</member>
       …
     </type>
   </reference>
   ```

   Roslyn emits members grouped by type in declaration order, with one exception: a documented nested
   type interrupts the outer type's members (four cases in `Web.xml`, e.g.
   `PageSeedInstaller.SeedResult`). A member is therefore attached to the **owner computed from its
   id**, not to the nearest preceding `T:`: strip the parameter list, then the last segment, and
   `M:Ns.Outer.Inner.Method(...)` belongs to `Ns.Outer.Inner` while `M:Ns.Outer.Method(...)` belongs
   to `Ns.Outer`. A member whose type has no `T:` entry still gets a section.

   Types and members get a display name here, where the string handling is testable: a type loses the
   assembly prefix (`Charting.ChartChrome`), a constructor becomes its type's local name, and a
   member's BCL types are shortened (`System.String` → `string`, `IReadOnlyList{X}` →
   `IReadOnlyList<X>`, `Nullable{X}` → `X?`, `{`/`}` → `<`/`>`). The raw id stays the anchor and the
   `title`, so a shortened display never loses information.

2. **The human page.** `GET /reference` is an index of the four assemblies this build carries, with
   their counts and the commit the running build was made from; `GET /reference/{sdk|core|runtime|web}`
   is one assembly. Anything else is `404` and the ordinary error page. Both are public and framed by
   `ShellDocument` (head, menu, skip link, landmark, security headers, `no-store`), like `/status` and
   `/admin/changes`. `/reference` joins `Pages:ReservedPaths`, gets a `ShellRoutes` record, and is
   linked from the page-API paragraph of the seeded `/help`.

3. **The stylesheet.** One embedded `reference.xslt` renders the fragment: an `h1`, a namespace-grouped
   type index in a named `nav`, then one `section` per type (`h2`) and one per member (`h3`). Inline
   doc elements become HTML — `see` (`cref` links only when the target is in the same document, else
   `code`; `langword` and `href` handled), `paramref`, `typeparamref`, `c`, `code` (as the existing
   `pre class="source" tabindex="0"`), `para`, `list` (bullet, number and table), `b`/`i`, `br` — and
   `inheritdoc` renders nothing of its own. No inline styles, no scripts, no forms, every `nav` named,
   so the shared rules in `AccessibilityTests` apply unchanged. `XslCompiledTransform` is in the
   framework (`System.Xml.Xsl`), loaded once with `XsltSettings.Default` and a null resolver, so
   script and `document()` are off. It adds no package.

   The view links one new seeded sheet, `/reference.css`, through the normal asset map. The seed is
   installed on the next start for an existing deployment.

4. **Cache the fragment.** The XML cannot change while the image runs, so each rendered assembly is
   computed once and held for the process. `Web.xml` is 540 kB of source and the largest fragment is a
   few megabytes of HTML; one copy of each is cheaper than a transform per request. The index reads its
   counts from the same loaded documents. A missing file is a page that says this build carries none,
   as `/admin/changes` does, rather than a 500.

5. **The assistant tool.** `Assistant/Tools/ReferenceTool.cs` is a read-only tool over the **`Sdk`
   corpus only**:

   - `reference type="IPageStorage"` answers the type's summary, remarks and every member with its
     signature, its own words, parameters and returns.
   - `reference query="cookie"` searches type and member names, displays and summaries when the model
     does not know the type to ask for.
   - Exactly one of `type`/`query`; a refusal names the accepted form and points at `query`.
   - `Writes = false`, `NeedsApproval = false`, `Verb = "Read"`; bounded by the existing
     `Assistant:MaxToolResultChars` and the `DefaultListLimit`/`MaxListLimit` pair.
   - The text view is a small flattener over the same normalised document: paragraphs, `see`,
     `paramref`, `c`, `code` and lists become plain text. One corpus, two outputs, no second parser.
   - **SDK only.** The page API admits the contract and a slice of BCL; documenting `Core`, `Runtime`
     and `Web` to the model invites code the compiler refuses. The human page still covers all four.

   `ToolServices` resolves the corpus lazily, so a tool that does not need it never instantiates one.
   `ToolRegistry.Default` gains the tool; `ToolSpecsTests` pins the count, which goes from eighteen to
   nineteen.

6. **Teach the model where it is.** One line in `PageAuthorPrompt` — `reference type=…` when the type
   is known, `reference query=…` when it is not — and `Version` to `v28`. The `/reference` shell record
   says the SDK half is read with the tool, so `cat /reference` and `grep` teach the same thing. The
   `IContext` signature block the prompt already derives by reflection stays: it is cheap, always
   present and always true.

7. **Prove it.** Tests for the owner and display rules; normalisation including the nested-type
   interruption and an orphan member; the rendered type/member structure, escaping, `cref` links,
   lists and `inheritdoc`; the index, an unknown slug and a missing file; type lookup by short name,
   full name and id, an unknown type, both/neither argument, query matches and truncation; both
   documents under every accessibility rule; and an integration test that the real host serves both
   routes from the published layout. The gate is `make check`, then `make image` and an export that
   confirms `/app/*.xml` is still present.

## Open questions

1. **The route's name.** `/reference` is the recommendation; `/api` reads as an HTTP API and `/docs`
   collides with the repository's own vocabulary. Owner-only was considered and rejected — the
   reference describes the build that is serving the site, as `/about` does.
2. **XSLT or C#.** The stylesheet is the recommended renderer: the source is a tree and the job is a
   tree rewrite, it keeps the presentation in one readable file, and it adds no package. A C# doc-node
   model and renderer in `Core` would give more control over markup and tests, at the cost of more code
   and a second escaping path. Browser-side XSLT was rejected: no server cache, no server tests, and
   the rendering would be invisible to the accessibility rules.
3. **All four assemblies, or the contract only.** The owner asked for the whole build; the index makes
   the extra assemblies a choice rather than a wall. The contract alone would be a smaller page and a
   cleaner "what may a page use" answer.
4. **Documented internals, and `inheritdoc`.** The XML has no visibility metadata: a documented
   `internal` type is in the file (Core's `ChartChrome`, for example), so the page shows it unless
   something filters. A public-only filter needs another source — a generated public-surface list, or
   `PublicAPI.Shipped.txt`, which is empty for `Web` — and is a job of its own. `Web.xml` also carries
   148 `<inheritdoc/>` entries, which render as a signature with no text; resolving them needs the
   interface or base member, and the page does not do it. Both facts should be said on the page rather
   than discovered.
5. **Whether the assistant tool earns its place.** It is the recommended shape and the user chose it;
   the alternative was a condensed index in the system prompt, which pays tokens on every step and
   still carries no summaries. The tool's `query` action is what makes it useful when the model does
   not know the type name.
6. **Whether `/reference` belongs in the site menu.** The recommendation is `/help` only, because the
   menu is the public site's and the reference is a developer's tool.

## Not in scope

- **Versioned documentation for older builds.** The image carries one build's XML; that is the one the
  reference describes.
- **Resolving `<inheritdoc/>`, filtering internals by accessibility, or generating a public-surface
  list.** Named in the questions above; each is its own job if it is wanted.
- **Searching across assemblies in the human page.** The page is a document per assembly; the query
  action belongs to the assistant tool and is SDK-only.
- **Editing the documentation from the site.** The XML is build output; the source comments are the
  thing to edit.

## Considered and rejected

- **Browser-side XSLT** (`<?xml-stylesheet?>` on the raw XML): no server cache, no server tests, a raw
  XML response with its own content type and CSP questions, and the rendering is invisible to the
  shared accessibility rules. The oldschool part worth keeping is the stylesheet, not the browser doing
  the work.
- **A C# doc-node model and renderer in `Core`**: this is a tree rewrite, which XSLT expresses in
  templates; C# stays for the one thing XSLT 1.0 is bad at — parsing the member ids. (The assistant
  tool's text view is the exception that proves it: a small flattener over the same normalised
  document, not a second model.)
- **A page capability to read the XML** (`IApiDocumentation`): a new public `Sdk` surface for one
  generated artifact, and the shell already owns build artifacts a page cannot read.
- **A stored page generated at build time**: it would freeze at install, duplicate the seed/restore
  machinery, and stop matching the running build.
- **A condensed SDK index in the system prompt**: a permanent token cost on every step that still
  carries no summaries, and the model would call the tool anyway for the member's own words.
- **Serving the raw XML files as pages or assets**: they are an internal representation, not a
  representation a reader wants, and `/reference` exists to do the transformation once.

**Origin:** the owner's question of 2026-11-14. The XML documentation had just been turned on by the
analyzers work (DEC-290) and the image was found to carry it; the question was what should read it, and
whether the page assistant could read the SDK half usefully. The route, the shell-route justification,
the normalisation, the stylesheet and the SDK-only tool are this job's.

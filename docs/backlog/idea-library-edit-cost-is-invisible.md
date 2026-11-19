<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Nothing says which pages a library edit will recompile

| | |
| --- | --- |
| Difficulty | contented-capybara |
| Utility | medium |
| Status | open |
| Blocked by | — |
| Plan | [ide-host](../plans/ide-host.md) |
| Area | Sdk, Web (pages, assistant), docs |
| Location | `src/uk.osric.asp.Sdk/PageEntry.cs`, `src/uk.osric.asp.Sdk/IPageCatalog.cs`, `src/uk.osric.asp.Web/Pages/PageCatalogScope.cs`, `src/uk.osric.asp.Web/Pages/PageRevision.cs`, `src/uk.osric.asp.Web/Pages/PageImage.cs`, `src/uk.osric.asp.Web/Pages/PageLibraries.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help` |

## Idea

A page may name a library, and a library's edit recompiles every page that names it, transitively
(`page-libraries` D7). The store records that: `PageRevision.TemplateLibraries` is the sorted
`key@hash` fingerprint of a page's closure, `PageImage.IsCurrent` compares it with the live library
heads, and the staleness reason "a library changed" is already counted on a metric. The fact is stored,
maintained and used — and no page can see it.

What that costs:

- **The tree cannot warn.** Editing `/_lib/document` recompiles every page that names it and the tree
  cannot mark them, so the editor's own help text says "this may break other pages" instead of naming
  them. The IDE plan lists this as a wish with the workaround "none".
- **The assistant cannot check before it edits.** It knows a library is shared only by having read it;
  nothing answers "what breaks if I change this".
- **A bulk operation cannot order itself.** A batch save ([IDEA-043](closed.md)) that touches a
  library and its dependents must write the library first, and today it has no way to know which are
  the dependents.
- **Seed provenance cannot tell a moved library from an edited one**, because the fingerprint is
  written but not read back ([IDEA-036](../plans/archive/seed-provenance.md)).

The wider class is "what does this change invalidate" — a question every writer wants and no reader
answers.

## Sketch

1. **The fact is already per revision.** `TemplateLibraries` holds it, written with the image and left
   alone by every other write. Nothing needs to be migrated or recomputed; the gap is a read.

2. **Two shapes, and the tree picks one.** Either the closure's *keys* join `PageEntry` (one more fact
   on a row the tree already holds, so the tree is one request and no second read), or a
   `DependentsAsync(key)` returns the keys whose closure names a library (one request per library, and
   the answer the editor actually wants when it saves one). The listing is the cheaper answer for the
   tree and the more expensive one for a store of three hundred pages; the reverse read is the opposite.
   The plan should pick by which question is asked more often — the tree asks it on every render.

3. **The fingerprint is opaque, so the read must translate.** `TemplateLibraries` is a joined string of
   `key@hash`; a page needs keys, not the string. Parsing lives where the fingerprint is written, so a
   change to its format cannot desynchronise the two.

4. **Transitivity comes free.** The fingerprint is the closure the compiler used, which already reaches
   a library that names another library, so a dependent of a dependent is in it. Nothing walks a graph
   at read time.

5. **Correctness is the compiler's, not a second parser's.** A page that names a library in code the
   compiler dropped as unreachable still lists it, because the closure is what compiled. That is the
   right answer for "what recompiles".

6. **The documentation** is a `/help` sentence beside the library section (`product-manual-is-a-page`),
   a `PageAuthorPrompt` line with its `Version` bumped, and a line in [pages](../pages.md).

## Open questions

1. **A fact on every row, or a read about one key.** See the sketch. The tree's need is decisive unless
   the closure is large enough to bloat a listing of three hundred rows; it is a string of keys, and a
   page naming three libraries costs under a hundred bytes.
2. **Whether the fact names a library that is gone.** A tombstoned library is still in the closure until
   the dependent recompiles, and the tree should mark the break rather than hide it — the answer is
   probably "report the key, and let the dependent's compile state say the rest"
   (`pages-a-page-has-a-state`).
3. **Whether the reverse read includes stale dependents.** A page whose closure names the library but
   which has not recompiled still names it; an editor warning wants that page too, so yes — with the
   state beside it.
4. **Whether a worker's closure is included.** A worker is compiled with the libraries its own source
   names; a library edit invalidates it too, and a page listing does not show workers. Either the read
   includes them or `/help` says it does not.

## Not in scope

- **Eager recompilation.** Saving a library recompiles nothing and dependents go stale lazily
  (`page-libraries` D7). This job reads a fact; it does not change when compiles happen.
- **A shared library assembly.** Rejected in `page-libraries` D9/D12 as a runtime change with no
  measured need.
- **A graph view.** A page can draw one from two listings; the host supplies the edges.

**Origin:** the owner's comparison of the site's IDE plan (`/ide-plan`, a stored page that is not in this
repository) with the job list of 2026-11-15. The IDE plan asks for "which pages name a library" and its
workaround is a sentence in the tree's help text; `page-libraries` D7 is where the fingerprint that
answers it was designed.

The plan that places it is [host support for the IDE](../plans/ide-host.md); this is slice 2 there.

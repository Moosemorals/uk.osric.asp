<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page cannot read a revision's diagnostics

| | |
| --- | --- |
| Difficulty | eager-beaver |
| Utility | medium |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Sdk, Web (pages, storage), docs |
| Location | `src/uk.osric.asp.Sdk/PageSource.cs`, `src/uk.osric.asp.Sdk/PageRevisionInfo.cs`, `src/uk.osric.asp.Sdk/IPageCatalog.cs`, `src/uk.osric.asp.Web/Pages/IPageReadStore.cs`, `src/uk.osric.asp.Web/Pages/SqlitePageReadStore.cs`, `src/uk.osric.asp.Web/Pages/PageCatalogScope.cs`, `src/uk.osric.asp.Web/Pages/SqlitePageStore.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `docs/pages.md` |
| Spike | — |

## Idea

Every compile's answer is already kept. `Diagnostics` is a JSON column on the page revision, written
with the image on a save and rewritten when a stale page is recompiled on its next request; the
store reads it back whenever a revision is loaded. It is what lets a broken page serve its
diagnostics without compiling again, and what the assistant's `read_page` and the shell's `500` page
report.

Nothing page-facing can read it. `IPageCatalog.ReadAsync` answers `PageSource` — key, hash,
saved-at, source — and `ReadRevisionAsync` the same for a retained revision; `RevisionsAsync`
answers `PageRevisionInfo`, a hash, a time and whether it is the head; and a listing row carries only
`PageEntry.State`, the words "Compile failed" or "Compiled with warnings"
(`pages-a-page-has-a-state`). The diagnostics themselves are reachable only as the by-product of a
write: the save that produced them, or the assistant's own host-side read.

Three callers hit the gap:

- **The editor** shows the diagnostics once, from the save response, and loses them on the next GET;
  a reloaded editor cannot say what the page failed on even though the row holds the answer.
- **A history view** can mark a revision as failed and not say why — the state is a word and the
  reason is in a column nobody exposes.
- **A page explaining a broken dependency** — a library it names no longer compiles — must recompile
  to find out what the compiler said, or say nothing.

The fresh question is [`check-without-storing`](check-without-storing.md)'s: does this text compile
now. This is the other one: what did the stored revision compile to. They are different answers and
both are wanted; the stored one costs a read, and it is the only one that exists for a revision
whose text the caller does not hold.

## Sketch

1. **The value carries it.** `PageSource` gains `IReadOnlyList<PageDiagnostic> Diagnostics`, empty
   when the revision was never compiled or has nothing to compile; `ReadAsync` and
   `ReadRevisionAsync` answer it. `PageRevisionInfo` gains the state word — `PageEntry.State`'s twin
   — so a history list can mark a row without fetching every revision, and the detail stays with
   `ReadRevisionAsync`.
2. **The mapping exists.** The stored JSON is the compiler's
   `Core.Compilation.PageDiagnostic`, and `PageDiagnostics.ToSdk` is what a save already uses to hand
   a page its answer. The catalog reuses it; nothing new is shaped, and the display ordering comes
   with it.
3. **The seam is already there.** `IPageReadStore` is the read-only, own-context seam the catalog
   reads through (`pages-the-store-is-read-through-one-seam`), so the change is a field on its
   records and one projection that stops treating `Diagnostics` as a boolean. The deserialiser and
   its pre-severity fallback currently live privately in `SqlitePageStore`; they move to one place
   both stores read.
4. **It needs a bound.** A broken page can answer with a thousand Roslyn diagnostics, which is why
   the assistant's check wants `Assistant:MaxCheckDiagnostics`. The read needs the same kind of
   answer — a count or a byte charge — and the choice between cutting and refusing is an open
   question below.
5. **It is public for free.** The catalog already publishes a page's source
   (`pages-reading-the-store-is-public`), and a diagnostic is about that source; a diagnostic that
   names a library names a key whose source is readable too. Nothing new is disclosed.
6. **The documentation** is the catalog's section in `/help` (`product-manual-is-a-page`), plus the
   read in [pages](../pages.md) beside the compile columns it already describes.

## Open questions

Wait for the plan — the owner's answers choose the shape, not the intent.

1. **`PageSource` or a new value.** Extending `PageSource` keeps one read per revision; a
   `PageRevisionSource` carrying the text and its verdict would let the read answer "source only" or
   "source and what it compiled to" without a caller ignoring a field.
2. **Count, bytes or the source allowance.** The budget it is charged to, and whether a hit cuts the
   list (the assistant's rule, `assistant-tool-results-are-cut-not-refused`) or refuses the read
   (the catalog's rule for source).
3. **How much a history row carries.** The state word only, a count, or the list — the first is
   cheap, the last makes the history of a broken page expensive.
4. **Whether the assistant's revision read follows here.** `read_page` reports head diagnostics
   through its own seam; the same tool with a `hash` reports only `imageState`, because a retained
   revision carries no diagnostics of its own. [`check-without-storing`](check-without-storing.md) has
   merged the two reads, so this item decides whether that read grows what it answers.
5. **Whether a stale revision's answer is marked.** The stored diagnostics are the last compile, and
   a library move can make them out of date before anything recompiles the page; the listing's state
   already ignores that, and this read should say what it is answering about rather than imply
   freshness.

## Not in scope

- **The fresh answer.** That is [`check-without-storing`](check-without-storing.md): it compiles the
  text it is given and stores nothing.
- **A page's own diagnostics during its render.** A page that does not compile is not rendered; the
  shell answers with its error page, and that is the shell's.
- **A job's failure text.** `PageJobRunner` already reduces a worker's diagnostics to the first
  error's message, and a job is not a revision.
- **Writing a diagnostic.** There is no path from a page to the column, and there should not be.

**Origin:** the owner's question of 2026-11-19, checking who could read a page's diagnostics after it
had been saved, and their instruction to record the read while the check capability was being
designed: "add updating IPageCatalog to return the diagnostics for a revision to the backlog".

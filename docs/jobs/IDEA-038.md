<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-038 — A page cannot search the store's source

| | |
| --- | --- |
| Size | M — one read on the catalog, one request and result value, a budget of its own, the seam lifted out of the assistant, and the tests and manual entry for each |
| Area | Sdk, Core, Web (pages, assistant), docs |
| Location | `src/uk.osric.asp.Sdk/IPageCatalog.cs`, `src/uk.osric.asp.Web/Pages/PageCatalogScope.cs`, `src/uk.osric.asp.Web/Assistant/PageSourceSearch.cs`, `src/uk.osric.asp.Web/Assistant/PageStoreReader.cs`, `src/uk.osric.asp.Core/Pages/PageScope.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` |

## Idea

A page can list keys and read one source at a time. Nothing searches what the sources say, so the only
way to find a string is `ReadAsync` per key, and that is charged to
`Pages:Read:MaxSourceCharsPerRender` — 512 KiB across the whole chain, of which the seeded `/pages`
already spends a third before it lists anything, and a seeded row now costs one read rather than
three ([IDEA-036](../plans/archive/seed-provenance.md)). A site of thirty pages works
by accident; a site of three hundred cannot be searched at all.

The host can already do it, three times over, and no page can:

- **`PageSourceSearch.Find`** is the one line scan, and `PageStoreReader.SourcesAsync` decides which
  keys a scope may read and in what order. Together they answer `search_pages` and the virtual shell's
  `grep`, with `Assistant:MaxSearchMatches` and `Assistant:ShellMatchLimit` bounding the
  answer.
- **`PageScope`** and `PageTree` in `Core` are the one statement of "below", "directly below" and "inside
  this prefix", and `pages-the-store-is-a-tree` says every front-end asks them.
- **Neither is on the page contract**, because the seam was built for the assistant's tools and the
  shell, and `pages-the-store-is-read-through-one-seam` deliberately made it a read seam in `Web`.

The wider class is every question of the form *where does the site say this*: find in files for the
editor, "who names this library" before it is changed, a link or a phrase audit after a move, a check
that a seed's text still appears after a harvest, and the assistant's own search before it edits.
Encoding that as the editor's find box is what makes it look like editor work.

## Sketch

1. **The host scans; the page gets matches.** `SearchAsync(request)` returns matching lines — key, line,
   column, the text of the line — and whether a limit stopped the scan. The page never receives a
   source, so the render's source allowance is not the currency and a search cannot be used to read the
   store for free. This is the shape `PageSourceSearch.Find` already answers.

2. **The request is a value, not nine arguments.** A scope (a prefix, a subtree or the whole store), a
   pattern, whether the match is case-sensitive, whether it is a literal or a regular expression, a
   limit and a cursor. The default is a case-insensitive literal over the whole store, because that is
   what a search box means.

3. **One allowance of its own.** A scan reads sources the caller never sees, so it needs a bound that is
   not the render's: `Pages:Search:MaxSourceChars` and `Pages:Search:MaxMatches`, both required, with the
   refusal naming which one was reached and what to narrow. A scan that hits the ceiling answers
   "there is more" rather than a truncated answer — the rule `pages-a-listing-carries-facts` already
   applies to a listing.

4. **A cursor in key-then-line order.** Results are ordered by key and then by line, and the cursor names
   the last match returned, so the next call resumes rather than repeats. That is [IDEA-039](IDEA-039.md)
   applied to a scan; the two ship best together, because a scan with no cursor is a scan of the first
   page and nothing else.

5. **One scan, not two.** `PageSourceSearch.Find` and `SourcesAsync` are lifted out of
   `Web/Assistant` into the seam the catalog already uses, with the two line rules kept apart — the
   assistant's tools count physical template lines because `read_page` names them, and merging the two
   rules would make an edit address the wrong line
   (`pages-the-store-is-a-tree` records why they stayed apart). The tools' JSON shapes and the shell's
   output do not change.

6. **The documentation** is a `/help` section (`product-manual-is-a-page`), a `PageAuthorPrompt` section
   with its `Version` bumped, and a line in [pages](../pages.md) for the tenth read.

## Open questions

1. **What a pattern may be.** A substring is trivial; a regular expression invites catastrophic
   backtracking, which is the one thing that cannot be stopped inside .NET (`SEC-03`). A literal with a
   case option and a whole-word flag may be the whole of the first version, with a regular expression
   left to a `timeout`-bounded match or declined.
2. **Whether a scan is charged to the assistant's bounds or its own.** The assistant has
   `MaxSearchMatches`; a page would have `Pages:Search:MaxMatches`. Two settings for one behaviour is
   the drift `pages-a-listing-carries-facts` exists to stop, so one section should serve both.
3. **Whether the answer carries the matching line only, or a window.** A line is enough for a find list
   and is what the tools print; a window of ±1 line is more useful in a diff panel and multiplies the
   result size. Start with the line and its position.
4. **Whether a tombstone is a searchable source.** It is never a source today
   (`pages-the-store-is-a-tree`): a deleted page's history is readable by hash but its source is not in
   a scan. A search across a restored page's history would need a different request, not a flag.

## Not in scope

- **Replace.** A search that edits is IDEA-043's batch with this scan choosing the edits; the two are
  separate capabilities and separate decisions.
- **An index.** FTS5 over page source is the mechanism IDEA-033 chose for per-account data, and it is a
  store migration with triggers to maintain. `n` pages is a linear scan until a measurement says
  otherwise (`code-n-is-small`).
- **Searching a revision's history.** One live source per key, as the tools do.
- **The editor's own find within the open tab.** That is the browser's, and it needs no host.

**Origin:** the owner's comparison of the site's IDE plan (`/ide-plan`, a stored page that is not in this
repository) with the job list of 2026-11-15. The IDE plan names "find in files" and its workaround is
the key filter; the observation that a worker may outlive the render is true but does not lift the
budget, because a worker's `ReadBudget` is built once per job (`PageJobRunner.cs`), not once per
request.

The plan that places it is [host support for the IDE](../plans/ide-host.md); this is slice 5 there, after the
cursor it resumes from ([IDEA-039](IDEA-039.md)).

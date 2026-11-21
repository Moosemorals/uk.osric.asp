<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page cannot search the store's source

| | |
| --- | --- |
| Difficulty | caffeinated-squirrel |
| Utility | medium |
| Status | planned |
| Blocked by | — |
| Plan | [page-search](../plans/page-search.md) |
| Area | Sdk, Core, Web (pages, assistant), docs |
| Location | `src/uk.osric.asp.Sdk/IPageCatalog.cs`, `src/uk.osric.asp.Web/Pages/PageCatalogScope.cs`, `src/uk.osric.asp.Web/Pages/PageSourceScan.cs`, `src/uk.osric.asp.Web/Pages/PageSearchOptions.cs`, `src/uk.osric.asp.Web/Pages/IPageReadStore.cs`, `src/uk.osric.asp.Web/Assistant/PageStoreReader.cs`, `src/uk.osric.asp.Web/Assistant/Tools/PageTools.cs`, `src/uk.osric.asp.Web/Assistant/Shell/Applets/GrepApplet.cs`, `src/uk.osric.asp.Core/Pages/GrepPattern.cs`, `src/uk.osric.asp.Core/Compilation/TemplateLines.cs`, `src/uk.osric.asp.Core/Pages/PageScope.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` |

## Idea

A page can list the tree and read one source at a time. Nothing searches what the sources say, so the
only way to find a string is `ReadAsync` per key, and every such read is charged to
`Pages:Read:MaxSourceCharsPerRender` — 512 KiB across the whole chain and shared by every layer of it,
refused rather than trimmed past that (`pages-a-listing-carries-facts`). A site of thirty pages works
by accident; a site of three hundred cannot be searched at all.

A page cannot ask a question of the whole store either: "every page that names this library", "every
heading at the third level", "every link that still points at the old key". The one place a pattern is
read today is the assistant's shell `grep`, and a page cannot reach it, so a page that wants a pattern
reads the store itself and pays the budget above.

The host can already do it, and no page can:

- **`PageSourceSearch.Find`** is the one line scan, and `PageStoreReader.SourcesAsync` decides which
  keys a scope may read and in what order — live pages and the site's own route records, with a
  tombstone never among them. Together they answer `search_pages` and the virtual shell's `grep`, with
  `Assistant:MaxSearchMatches` and `Assistant:ShellMatchLimit` bounding the answer.
- **`GrepPattern`** is the one pattern compiler, and `assistant-the-shell-is-posix` already records what
  it means: a query that is only text is a substring test, and anything else is compiled with
  `RegexOptions.NonBacktracking`, which matches in linear time and refuses what it cannot carry.
- **`PageScope`** and `PageTree` in `Core` are the one statement of "below", "directly below" and "inside
  this prefix", and `pages-the-store-is-a-tree` says every front-end asks them.
- **None of them is on the page contract**, because the seam was built for the assistant's tools and the
  shell, and `pages-the-store-is-read-through-one-seam` deliberately made it a read seam in `Web`.

The wider class is every question of the form *where does the site say this*: find in files for the
editor, "who names this library" before it is changed, a link or a phrase audit after a move, a check
that a seed's text still appears after a harvest, and the assistant's own search before it edits.
Encoding that as the editor's find box is what makes it look like editor work.

## Sketch

1. **The host scans; the page gets matches.** `SearchAsync(PageSearchRequest)` returns a `PageSearch` of
   matching lines — key, line, the column the match starts at, and the text of the line — and the cursor
   the next slice starts from. The page never receives a source, so the render's source allowance is not
   the currency and a search cannot be used to read the store for free. This is the shape
   `PageSourceSearch.Find` already answers, with a column and a cursor added.

2. **The request is a value, not nine arguments.** A `Prefix` and a `Recursive` flag — the listing
   request's own pair (`pages-a-listing-descends-unless-asked`), so a subtree and one level are asked for
   the same way in both — a pattern and its kind (a literal substring or a basic regular expression),
   whether the match ignores case, a `Limit` and a cursor. The default is an ignore-case literal over the
   whole store, because that is what a search box means; the kind decides which test runs, so a literal
   search for `foo.` is a literal search rather than a surprise pattern.

3. **One pattern compiler, and it is the shell's.** `GrepPattern` moves out of `Core/Assistant` and is
   what the page's search, the shell's `grep` and the assistant's tool all compile with, so a query means
   the same thing in all three. That is the language `assistant-the-shell-is-posix` records: `(`, `)`,
   `+`, `?` and `{` are literal because a page key and a snippet of HTML are full of punctuation, and a
   construct the non-backtracking engine cannot carry is refused when the pattern is compiled, before a
   source is read. Widening the language later — making one-or-more and optional operators rather than
   literal characters, adding a word boundary — is one change in that class and reaches all three at
   once.

4. **One allowance, and it is measured in time.** The scan stops after the configured duration because a
   count of matches bounds the answer while the work is done whether or not anything matches. The
   duration covers the whole call: the tree walk, the reads and the matching, so the seam is stepped one
   source at a time rather than materialising every source before the first line is tested, which is what
   `SourcesAsync` does today. One `Pages:Search:` section carries the duration and the default and
   largest `Limit`, and `Assistant:MaxSearchMatches` becomes that section's largest slice rather than a
   second opinion about one behaviour. A scan that reaches the duration answers "there is more" rather
   than a truncated answer — the rule `pages-a-listing-carries-facts` already applies to a listing.

5. **A cursor in key-then-line order, and a cursor the store can invalidate.** Results are ordered by key
   and then by line, and the cursor is the position the scan reached — the key and the line after the
   last one tested — so a slice that finds nothing still advances and a short duration cannot spin on
   the same source. That makes the cursor a claim about one store, so the request carries the version
   token that [`listing-does-not-date-itself`](closed.md) gives a listing and the
   answer carries the version it read; a different version invalidates the cursor and the walk starts
   again. That is the simple version of the choice made there — a version and a restart, not a read
   snapshot held across requests — and it is enough here because a search walk is a handful of requests
   rather than a listing walked for minutes. The token landed with that item
   (`pages-a-listing-carries-a-fingerprint`), so this is a caller of it rather than a second reader, and
   it is the prefix's fingerprint whatever depth the walk asked for, because a one-level answer is still
   a slice of that prefix's list (`pages-a-listing-answers-the-site-tree`).
   [`listing-has-no-cursor`](closed.md) is the other half: a scan with no cursor is a scan of the first
   slice, and a cursor that cannot be trusted is a scan of a store that has moved.

6. **One scan, not two.** `PageSourceSearch.Find` and `SourcesAsync` are lifted out of `Web/Assistant`
   into the seam the catalog already uses, with the two line rules kept apart — the assistant's tools
   count physical template lines because `read_page` names them, and merging the two rules would make an
   edit address the wrong line (`pages-the-store-is-a-tree` records that each front-end keeps its own
   line rule; `PageSourceSearch`'s remarks record why). The page's search counts lines the way
   `read_page` and the editor do, so a match's line is one the author can act on; the shell keeps its own
   rule for its own output, and the assistant's JSON shapes and the shell's output do not change. The
   source set does not change either: the page's scan reads live pages and the site's own route records,
   as `search_pages` and `grep` do, and no tombstone. `search_pages` gains the pattern flag and its
   `query` description stops saying "a literal substring", with the literal kind its default so no query
   already written changes meaning.

7. **The documentation** is a `/help` section (`product-manual-is-a-page`) that teaches the pattern
   language as well as the capability, a `PageAuthorPrompt` section with its `Version` bumped, and a line
   in [pages](../pages.md) for `IPageCatalog`'s fifth read.

## What the owner answered

Put to the owner before any plan, and answered on 2026-11-20, with one answer added on 2026-11-21 after
trunk changed the seam under it.

1. **A regular expression, matched by the platform's linear-time engine rather than one we write.** The
   article the owner pointed at ([Regular Expression Matching Can Be Simple And
   Fast](https://swtch.com/~rsc/regexp/regexp1.html)) is the argument for simulating an automaton over
   the text rather than backtracking through the pattern, and .NET ships that guarantee behind
   `RegexOptions.NonBacktracking`; this host already compiles the shell's `grep` with it. There is no
   engine to write and no timer to police: a pattern costs at most linear time in the line by
   construction, and a construct the engine cannot carry — a backreference, a lookaround — is refused
   when the pattern is compiled, before a source is read. The language is the shell's, so one query
   means one thing everywhere (point 3 of the sketch).
2. **One bound, and it is measured in time.** A common limit for both front-ends, expressed as a
   duration — around 30 ms — rather than a count, because a count bounds the answer while the scan's work
   is done whether or not anything matches. One `Pages:Search:` section rather than two settings for one
   behaviour.
3. **The line and its position, and no window.** A line is what a find list shows and what the tools
   print; a caller that wants context reads the page through the read it already has.
4. **A tombstone is not a source.** It is the panic button and stays invisible to day-to-day work, so a
   scan never reads one — which is what `SourcesAsync` already does.
5. **The site's own routes are sources.** Answered on 2026-11-21, after trunk made it visible that the
   seam has carried the site's route records since it was built, and that `search_pages` and `grep`
   already search them. A scan reads those records as well as live pages, so a query finds a link where
   the site says it whether a page wrote it or a route does. A match carries the key, and the listing
   marks a key a route answers (`PageEntryKind.Route`), so a page can tell the two apart with the read it
   already has.

## After MVP: a revision's history

The owner's vote, also 2026-11-20: a later version may search history as well, behind a flag in the call
that defaults to the live source only, and possibly extended to a date range. It waits for after MVP, and
nothing here forecloses it — the request is a new value, so a member added later is not a contract break.
The order a history walk runs in and the line rule a revision's text is counted by are that version's
business; a tombstone stays out of it either way.

## Not in scope

- **Replace.** A search that edits is IDEA-043's batch with this scan choosing the edits; the two are
  separate capabilities and separate decisions.
- **An index.** FTS5 over page source is the mechanism `no-account-store` chose for per-account data, and it is a
  store migration with triggers to maintain. `n` pages is a linear scan until a measurement says
  otherwise (`code-n-is-small`).
- **The editor's own find within the open tab.** That is the browser's, and it needs no host.

**Origin:** the owner's comparison of the site's IDE plan (`/ide-plan`, a stored page that is not in this
repository) with the job list of 2026-11-15. The IDE plan names "find in files" and its workaround is
the key filter; the observation that a worker may outlive the render is true but does not lift the
budget, because a worker's `ReadBudget` is built once per job (`PageJobRunner.cs`), not once per
request.

The plan that places it is [host support for the IDE](../plans/ide-host.md); this is step 4 there, after the
cursor it resumes from ([`listing-has-no-cursor`](closed.md)). Its own plan, written from the owner's answers
on 2026-11-21, is [page search](../plans/page-search.md).

The owner refined the shape on 2026-11-20, before any plan: a pattern as well as a literal, so the shell's
language reaches a page; one bound measured in time rather than a count; the line and its position and no
window; no tombstones; a cursor that a store version invalidates rather than a snapshot that holds one
open; and history behind a flag, after MVP. One answer was added on 2026-11-21: a scan reads the site's
route records as well as live pages, because `search_pages` and `grep` already do.

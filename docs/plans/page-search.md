<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page can search the store's source

| | |
| --- | --- |
| Status | Proposed 2026-11-21 — waits for the owner. Nothing below is built; the five open questions are asked first |
| Size | One capability, one branch: the value, the stepped read, the scan, both assistant front-ends, the manual and the measurements. A half of it has no caller, so it is not sliced |
| Spike | None. Every claim about what exists was read off `trunk` on 2026-11-21 (`c10e1d5`): `IPageCatalog`, `PageCatalogScope`, `PageStoreReader`, `PageSourceSearch`, `GrepPattern`, `PageScope`/`PageTree`, `Pages:Read`, `AssistantOptions`, `PageMetrics`, `StatusCatalogue` |
| Decisions | None recorded yet. This plan states the name it intends: **pages-a-search-returns-matches**. It also edits two entries (a moved file and a stale sentence), below |
| Backlog | [`cannot-search-the-store`](../backlog/cannot-search-the-store.md) — step 4 of [host support for the IDE](ide-host.md), which places it and keeps its own record of the programme |

## The short version

The item says what the capability is and the owner answered its questions on 2026-11-20 and 2026-11-21. This
plan is the implementation of those answers:

- **The contract.** `IPageCatalog.SearchAsync(PageSearchRequest)` answers `PageSearch` — matching lines, each with a
  key, a 1-based line, a 1-based column and the line's text — plus a cursor, `Truncated`, the prefix's
  fingerprint and `Changed`. Five `Sdk` values do it; the page never meets a source, a hash or a template.
- **The scan.** `PageSourceSearch.Find` becomes `PageSourceScan`: one matcher over a *stepped* source sequence,
  with a time allowance, a cursor in key-then-line order, a column, and three callers — the page, `search_pages`
  and `grep` — that keep their own line rule and their own projection. `GrepPattern` moves to `Core.Pages` and
  grows a literal/basic switch and a column; it stays the one pattern language (`assistant-the-shell-is-posix`).
- **The read.** Both read seams gain a stepped read: `IPageReadStore.ScanAsync` yields live templates one at a time
  from one SQL statement (the page's), and `IPageStoreReader.ScanAsync` yields the scope's sources one at a time
  (the assistant's). `SourcesAsync` becomes the materialisation of its seam's stepped read, so there is one read
  per seam rather than two.
- **The bound.** One new section, `Pages:Search`, carries the duration (30 ms shipped), the default and the largest
  `Limit` (100 and 1000). `Assistant:MaxSearchMatches` goes; the assistant's cap is the section's largest slice.
  The duration covers the version read, the reading and the matching.
- **The measurements.** `page.search.duration` is a histogram tagged `caller`; `page.search.requests` is a counter
  tagged `caller` and `outcome` — `completed`, or `cut-off` when the timer stopped the scan. Each gets one panel,
  and the status page reads them like every other instrument (`metrics-one-panel-per-instrument`).
- **The manual.** `/help` gains a section that teaches the pattern language as well as the call, the prompt gains a
  paragraph and moves to `page-author-v57`, and [pages](../pages.md), [storage](../storage.md),
  [assistant](../assistant.md) and [metrics](../metrics.md) say what changed.

The scan's loop, the read it stands on, and the two values it carries are the whole of the risk; everything else
is wiring, a setting and a page of prose.

## What exists today

A page can list the tree and read one source at a time. Nothing searches what the sources say, so a pattern costs
`ReadAsync` per key, and every such read is charged to `Pages:Read:MaxSourceCharsPerRender` — 512 KiB across the
whole layout chain and shared by every layer, refused rather than trimmed past that
([`pages-a-listing-carries-facts`](../decisions.md#pages-a-listing-carries-facts)). A site of thirty pages works
by accident; a site of three hundred cannot be searched at all.

The host can already do it and no page can:

| Where | What it is | Why the page cannot reach it |
| --- | --- | --- |
| [`PageSourceSearch.Find`](../../src/uk.osric.asp.Web/Assistant/PageSourceSearch.cs) | one line scan over a list of sources: the caller's line rule, the caller's test, a limit that stops one match past the answer | it is `Web/Assistant`'s, internal, and takes the whole list at once |
| [`PageStoreReader.SourcesAsync`](../../src/uk.osric.asp.Web/Assistant/PageStoreReader.cs) | the scope read: live pages and the site's own routes, in key order, a tombstone never among them | it is the assistant's seam, and it materialises every source before the first line is tested |
| [`GrepPattern`](../../src/uk.osric.asp.Core/Assistant/GrepPattern.cs) | the one pattern compiler: a literal when the query has no metacharacter, otherwise a basic regular expression compiled `NonBacktracking`, refused when the engine cannot carry it | `Core.Assistant`, reachable from the shell and the tools, not from a page |
| [`PageScope`](../../src/uk.osric.asp.Core/Pages/PageScope.cs), [`PageTree`](../../src/uk.osric.asp.Core/Pages/PageTree.cs) | the one statement of "below", "directly below" and "inside this prefix" | reachable; the listing and the search both ask them (`pages-the-store-is-a-tree`) |
| [`IPageCatalog`](../../src/uk.osric.asp.Sdk/IPageCatalog.cs) | four reads: a listing, one source, a history, one revision; and `ShellLinks()` | the seam was built for the literal questions, not for a scan |

The rest of the frame is fixed: a listing's cursor is a key and its version token is the prefix's fingerprint
([`pages-a-listing-carries-a-fingerprint`](../decisions.md#pages-a-listing-carries-a-fingerprint)); a listing
descends unless asked not to ([`pages-a-listing-descends-unless-asked`](../decisions.md#pages-a-listing-descends-unless-asked));
the read is public as the page that calls it ([`pages-reading-the-store-is-public`](../decisions.md#pages-reading-the-store-is-public));
the read capability sits on `IPageReadStore` because a page's task outlives the request
([`pages-the-store-is-read-through-one-seam`](../decisions.md#pages-the-store-is-read-through-one-seam)); and every
limit is configuration, never a literal ([`code-policy-values-are-configuration`](../decisions.md#code-policy-values-are-configuration)).

## The shape

### The contract

Five values in [`uk.osric.asp.Sdk`](../../src/uk.osric.asp.Sdk), so a page can name them and nothing else:

```csharp
/// <summary>How a search reads its pattern: the text itself, or the shell's basic regular expression.</summary>
public enum PageSearchPatternKind { Literal, BasicRegularExpression }

/// <summary>A position in a walk: the key, and the 1-based line the next slice starts at.</summary>
public readonly record struct PageSearchCursor(string Key, int Line);

/// <summary>One slice of the store a page asks to have searched.</summary>
public sealed record PageSearchRequest(
    string Pattern,
    string Prefix = "",
    bool Recursive = true,
    PageSearchPatternKind Kind = PageSearchPatternKind.Literal,
    bool IgnoreCase = true,
    PageSearchCursor? After = null,
    int? Limit = null,
    string? Version = null);

/// <summary>One matching line: its key, its 1-based line, its 1-based column and the line itself.</summary>
public readonly record struct PageSearchMatch(string Key, int Line, int Column, string Text);

/// <summary>One slice of matching lines, the position to resume at, and whether the store moved.</summary>
public sealed record PageSearch(IReadOnlyList<PageSearchMatch> Matches, PageSearchCursor? Cursor, bool Truncated) {
    public string Version { get; init; } = string.Empty;
    public bool Changed { get; init; }
}
```

and one member:

```csharp
Task<PageSearch> SearchAsync(PageSearchRequest request);
```

The defaults are the request: an ignore-case literal over the whole store, descending. The order of the
parameters follows the listing's — `Prefix`, `Recursive`, then the slice's `After`, `Limit` and `Version` — so a
page that has learned `PageListingRequest` finds the same three words in the same places. The kind and the case
sit after the scope and before the slice, because they are about the pattern rather than about where the walk is.

The rules the doc comments state, and the tests prove:

| Rule | What it means |
| --- | --- |
| **Matches, not sources** | A row is a line that matched, with its position and text. No `PageSource`, no hash, no template and no image crosses the seam, so a search cannot be turned into a bulk read and no `ReadBudget` is charged |
| **One row per line** | Several matches on one line are one row, carrying the column of the *first*; a caller that wants the others searches a narrower pattern |
| **Key, then line** | The order is ordinal key and then ascending line, and the cursor is in that order, so a walk of any store neither skips nor repeats a position |
| **The cursor is the first position not included** | A limit that stops the scan returns the position of the confirmed extra match, because a scan that stopped one line early would lose it. A timer that stops it returns the line it did not reach. Either way, one rule: ask again from `Cursor` |
| **A changed store restarts the walk** | `Version` is the prefix's fingerprint at any depth. If the request carried a token and this answer reads a different one, `Changed` is true, the cursor is *not trusted*, and the scan starts at the beginning. The listing only warns; a scan reads text, so a moved store can silently skip a *match* rather than a row, and the honest answer is to start again |
| **The scope is the listing's** | `Prefix` is a subtree, never a string prefix; `Recursive` descends unless asked not to; `""` and `/` are the whole store (`pages-the-store-is-a-tree`) |
| **Sources are live pages and the site's routes** | A tombstone is never a source. A key the shell's routes answer is searched as the route's record, and a stored head it shadows is not read twice |
| **The pattern is refused before a source is read** | An empty pattern, a key that cannot name one, a cursor outside its range, a limit outside its range and a pattern the engine cannot carry are `ArgumentException`/`ArgumentOutOfRangeException` — page bugs, as the listing's are — and the last two name the setting to raise |
| **Public as the page** | No gate, as `pages-reading-the-store-is-public` says. A page that wants its search private writes the ordinary `ctx.User` guard |

`Truncated` says the scan stopped before the end of its scope — the limit or the duration — and `Cursor` names
where to carry on; `false` means the scope ran out and `Cursor` is null. That is the listing's shape with one
difference: for a listing `Truncated` means a row stands after the last, and for a search it means *the scan did
not finish*, which is the only thing the scan can honestly say when a timer stopped it before it knew.

### The scan

[`PageSourceSearch`](../../src/uk.osric.asp.Web/Assistant/PageSourceSearch.cs) becomes
`Web/Pages/PageSourceScan.cs`: one class, three callers, each keeping its own line rule and its own projection.

```csharp
internal enum PageSearchCaller { Page, Tool, Shell }

/// <summary>Where a scan ended: the scope ran out, the caller's limit, or the time allowance.</summary>
internal enum PageSearchEnd { Exhausted, Limit, Duration }

internal sealed record PageSourceText(string Key, string Text);
internal sealed record PageScanMatch(string Key, int Line, int Column, string Text);
internal sealed record PageScanResult(
    IReadOnlyList<PageScanMatch> Matches, PageSearchCursor? Cursor, PageSearchEnd End, string Version, bool Changed);

/// <summary>One question, as the scan reads it: the compiled pattern, the slice, the line rule and who asked.</summary>
internal sealed record PageScanQuery(
    GrepPattern Pattern, int Limit, PageSearchCursor? After, string? Version,
    Func<string, string[]> Lines, PageSearchCaller Caller);

internal sealed class PageSourceScan(PageSearchOptions options, TimeProvider clock, PageMetrics metrics) {

    public async Task<PageScanResult> RunAsync(
        PageScanQuery query,
        Func<CancellationToken, Task<string>>? readVersion,     // null when the caller cannot resume
        Func<PageSearchCursor?, CancellationToken, IAsyncEnumerable<PageSourceText>> sources,
        CancellationToken cancellationToken);
}
```

- **The clock starts first.** `readVersion` is awaited under the allowance, then the sources are read; the
  allowance covers the fingerprint, the stepped reads and the matching, which is the owner's answer 2 and the
  reason the read is stepped rather than materialised.
- **`readVersion` is null for the tool and the shell**, which never resume a walk and so never pass a token. The
  page's is `IPageReadStore.VersionAsync(prefix)`.
- **The loop** is a plain `await foreach` over the sources, one source in memory at a time, and per line: check
  the clock, test the pattern, and either record a match or stop on the limit. The position of the line being
  tested is the cursor if the loop stops there; a finished source leaves no position behind.
- **The line rule is a parameter**, a `Func<string, string[]>`. The page and `search_pages` pass
  `TemplateLines.Split` (the physical lines the editor and `read_page` count); `grep` passes `ShellText.Split`
  (the POSIX count). They stay two rules for the reason
  [`pages-the-store-is-a-tree`](../decisions.md#pages-the-store-is-a-tree) records: merging them makes an edit
  address the wrong line in a page that does not end with a newline.
- **Two sources of text, one shape.** `PageSourceText` is the scan's input. The catalog's is
  `IPageReadStore.ScanAsync` merged with the route records from `ShellCatalogue.Text`; the assistant's is
  `IPageStoreReader.ScanAsync`, which already includes the route records because it reads the tree the shell
  reads.

### The read it stands on

Two stepped reads, one per seam, both in key order:

```csharp
// Web/Pages/IPageReadStore.cs — the page's read, one statement, live heads only.
IAsyncEnumerable<PageReadSource> ScanAsync(
    string prefix, string? from, bool recursive, CancellationToken cancellationToken = default);

// Web/Assistant/PageStoreReader.cs — the assistant's read, over the tree it already builds.
IAsyncEnumerable<PageRead> ScanAsync(
    IReadOnlyList<PageScope> scopes, CancellationToken cancellationToken);
```

`IPageReadStore.ScanAsync` is the one new statement. It is the listing's query without the limit and without the
fingerprint: `HeadsUnder(prefix, recursive)`, live heads only, `Path >= from`, joined to the revision for the
template, ordered by path, and streamed with `AsAsyncEnumerable()`. `from` is **inclusive**, unlike the listing's
exclusive `after`, because the cursor resumes inside the source it names. The deferred read transaction of
`InReadSnapshotAsync` is unnecessary: one streaming statement holds SQLite's snapshot for as long as it runs,
nothing is written and nothing is pinned beyond it.

`SourcesAsync` becomes the materialisation of the assistant seam's stepped read, so the scope rules, the
ordering and the route records stay in the one place they already are. The assistant's stepped read yields a
`PageRead` from `ReadAsync` per node, exactly what `SourcesAsync` did in a loop; the difference is that the loop
now hands each source on instead of collecting all of them.

### The two front-ends

**`search_pages`** ([`PageTools.cs`](../../src/uk.osric.asp.Web/Assistant/Tools/PageTools.cs)) keeps its JSON
shape — `query`, `matches` of `path`/`line`/`text`, `truncated` — and gains one argument:

| Argument | Meaning |
| --- | --- |
| `query` | The text or pattern; the description stops saying "a literal substring" |
| `pattern` | `false` (the default) matches the query literally; `true` reads it as a basic regular expression |
| `prefix`, `caseSensitive` | Unchanged |

It asks for `Pages:Search:MaxMatches` (1000 shipped), so an answer does not shrink from today's, and it passes
`PageSearchCaller.Tool`. Its `description` says `truncated` means "there is more to read", because the timer can
stop a scan that would have found nothing further.

**`grep`** ([`GrepApplet.cs`](../../src/uk.osric.asp.Web/Assistant/Shell/Applets/GrepApplet.cs)) keeps its
output to the character. The line answer goes through the scan with `ShellMatchLimit` and `ShellText.Split`; `-l`
and `-c` keep their per-key fold, because their answer is about a key rather than a line, and the depth hint keeps
its second recursive read. The stop sentence is unchanged: "there are more" is true of a timer's answer too, and
the item fixes the shell's output.

**`PageCatalogScope`** ([`PageCatalogScope.cs`](../../src/uk.osric.asp.Web/Pages/PageCatalogScope.cs)) normalises
the prefix and the cursor, compiles the pattern, checks the limit against `Pages:Search:MaxMatches`, calls the
scan with the catalog's own source (the store's stepped read plus `reserved.Keys`), and maps the result to
`PageSearch`. It keeps the two refusals every member has — an ended render throws
`InvalidOperationException`, and the store's deadline is linked into the token.

### Configuration

One section, three values, read through `ConfigurationReader` into a `PageSearchOptions` record beside
`PageReadOptions`:

```json
"Search": {
  "MaxDurationMs": 30,
  "DefaultMatches": 100,
  "MaxMatches": 1000
}
```

| Key | Shipped | Range | Why |
| --- | --- | --- | --- |
| `MaxDurationMs` | 30 | 1 to 60 000 | The owner's answer: a bound measured in time, because a count bounds the answer while the scan's work is done whether anything matches. The top of the range is the longest render the site accepts (`Pages:Render:TimeoutSeconds`'s own maximum), so a mistake cannot make a search outlive the thing it serves |
| `DefaultMatches` | 100 | 1 to 10 000, and never above `MaxMatches` | What `Limit: null` means. Refused above `MaxMatches` with a `FailIf`, the way `PageStorageOptions` refuses a page smaller than a value |
| `MaxMatches` | 1000 | 1 to 10 000 | The largest slice the page contract will answer, and the cap `search_pages` asks for. `Assistant:MaxSearchMatches` goes, because one behaviour has one setting |

`Assistant:MaxSearchMatches` is removed from `AssistantOptions`, `appsettings.json` and the prompt's caps. A
leftover key in an operator's override is **refused at startup** by `AssistantOptions.FromConfiguration`, naming
`Pages:Search:MaxMatches`, for `Pages:ReservedPaths`' reason: an operator who kept it believes it still narrows
something, and it does not.

### Measurements

Two instruments on the Pages meter, beside the ones that already describe the store:

| Instrument | Type | Unit | Tags |
| --- | --- | --- | --- |
| `page.search.duration` | histogram | s | `caller` (`page`, `tool`, `shell`) — one scan, from the fingerprint to the last line tested |
| `page.search.requests` | counter | — | `caller`, `outcome` (`completed`, `cut-off`) — `cut-off` is the timer, and `completed` is every scan that ended on its own terms, including one the caller's limit stopped |

- **The histogram is the shape of the work.** Its buckets are `SearchBuckets`, one scale for a scan:
  `0.001, 0.0025, 0.005, 0.01, 0.015, 0.02, 0.025, 0.03, 0.04, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10`. The
  `0.03` boundary is the shipped allowance, so "how many scans finish inside the budget" is a cell rather than
  an interpolation, and the top covers the tool timeout a `search_pages` call runs under.
- **The counter answers the question the timer asks.** A rise in `cut-off` against a flat `completed` says the
  allowance is too small for the store; a rise in both says the site is busier. The two labels partition every
  call, so the panel is a stacked area, and the histogram is a heatmap (`metrics-one-panel-per-instrument`).
- **One panel each** in [`StatusCatalogue`](../../src/uk.osric.asp.Web/Metrics/StatusCatalogue.cs), the
  histogram beside the other durations and the counter beside the other counters; `Program` adds the
  `ExplicitBucketHistogramConfiguration`, and `docs/metrics.md` gains the two rows and the counts.
- **No path label.** A search names a prefix a visitor typed, which is the cardinality trap `page.render.wait`
  and `page.asset.requests` already avoid; the access log names the page that asked.
- **Recorded once, by the scan**, so the page, the tool and the shell cannot measure one scan differently. Only a
  call that reached the store is recorded: a refusal is a page bug, not a slow search.

## What this plan does differently from the item's sketch

- **`SourcesAsync` moves *behind* the scan rather than into `IPageReadStore`.** The item's sketch lifts it "into
  the seam the catalog already uses". The assistant's seam reads through `PageEditor`, and the scope rules and
  route records are what its tests and its `grep -l`/`-c` depend on. So each seam gains a stepped read and
  `SourcesAsync` is its materialisation: one read per seam, one scan over both. Moving the assistant's source
  set onto `IPageReadStore` would rewrite every search fixture and buy no behaviour.
- **The timer applies to all three callers, not only the page.** The owner's answer says one bound for the
  behaviour; the shell keeps its match limit and its output, but its scan is stopped by the same allowance.
- **`search_pages` does not gain a cursor.** The item gives the page a cursor and the tool a pattern flag. A
  cursor for the tool belongs to [`tool-surface-is-one-shell`](../backlog/tool-surface-is-one-shell.md), which is
  blocked by this item and already owns the tool surface's shape.
- **`grep -l` and `-c` stay on the per-key fold.** Their answer is a key or a count, and folding a line scan
  would cut a count at the match limit rather than at `ShellMatchLimit`. They read the stepped seam and
  materialise it, so the read is one implementation but the answer is unchanged.

## The work, in order

One branch, five commits, the gate at the end. Each commit leaves the build green and its tests failing without
it.

### 1. The values and the language

- `Core/Pages/GrepPattern.cs` (moved from `Core/Assistant`): a `literal` parameter on `TryCompile` so a literal
  `foo.` stays literal and a basic one keeps the dot — a named `bool` rather than the `Sdk` enum, because `Core`
  cannot reference the page contract; `TryMatch(line, out int column)` for the first match's 1-based column;
  refusals return the reason without advice, and each caller adds its own sentence. The shell's auto-detect
  disappears: POSIX `grep` is a basic expression, and a query with no metacharacter is already the literal path.
- `Core/Compilation/TemplateLines.cs`: `Split(string)` carries the physical-line rule `PageToolData` holds today;
  `TemplateSourceLines` delegates (and its callers move to `TemplateLines.Split` in step 5).
- `Sdk`: the five types above, `IPageCatalog.SearchAsync`, and the `PublicAPI.Unshipped.txt` entries the analyzer
  asks for.

### 2. The stepped reads

- `IPageReadStore.ScanAsync` and `SqlitePageReadStore`'s implementation; `FakePageReadStore`'s in-memory twin.
- `IPageStoreReader.ScanAsync` over `TreeAsync` + `ReadAsync`; `SourcesAsync` becomes
  `[.. await ScanAsync(scopes, ct).ToListAsync(ct)]`.
- The read store's tests: order, tombstone, inclusive `from`, one level, and the unchanged `SourcesAsync`.

### 3. The scan and the catalog

- `PageSourceScan` (the file moved and the class replaced), `PageSearchCaller`, `PageScanQuery`, `PageScanMatch`,
  `PageScanResult`, `PageSourceText`.
- `PageSearchOptions` + `TestOptions.Search` + `ShippedConfigurationTests` + `PageSearchOptionsTests`.
- `PageCatalogScope.SearchAsync`, the `Sdk` mapping, and the constructors that hand a catalog its scan:
  `PageChain`, `PageJobRunner`, `PageEnvironment`, `PageJobEnvironment`, `PageHarness`, `Program`.
- `PageMetrics`: the two instruments, `RecordSearch`, `SearchBuckets`; `Program`'s view; the two
  `StatusCatalogue` panels.
- The catalog's tests and the scan's own suite.

### 4. The assistant's two front-ends

- `SearchPagesTool`: the `pattern` argument, the shared scan, `Pages:Search:MaxMatches`, the description's
  "there is more to read".
- `GrepApplet`: the scan for the line answer; `ShellContext` gains the scan; `ShellRunner.RunAsync` takes it from
  `ShellTool`/`ShellKernelHarness`.
- `AssistantOptions`: `MaxSearchMatches` removed, and the leftover key refused; `appsettings.json`; the
  `AssistantOptionsTests` change.
- The tool and shell tests, including a literal `foo.` and a `pattern: true` dot.

### 5. The manual, the prompt and the record

- `/help`'s `Reading the page store` section gains `Searching the store`: the call, the cursor, the version, the
  kinds, and the language table (a literal is literal; `^`, `$`, `.`, `*`, `[abc]` are metacharacters, `\|` is
  alternation, everything else — `(`, `)`, `+`, `?`, `{` included — is the character).
- `PageAuthorPrompt`: a paragraph in `## Page store read`, the "All four are awaited" sentence becomes five, and
  `Version` moves to `page-author-v57`. Three tests hold the literal `page-author-v56` and move together.
  [assistant-shell-tool](assistant-shell-tool.md) also intends v57; it is blocked by this item, so this plan
  lands first and that plan's merge takes v58.
- [pages](../pages.md), [storage](../storage.md), [assistant](../assistant.md) and [metrics](../metrics.md).
- The record: at the merge, the entry **pages-a-search-returns-matches**, the moved `GrepPattern` link in
  `assistant-the-shell-is-posix`, and the `PageSourceSearch` sentence in `pages-the-store-is-a-tree`.

## Tests

One suite per layer, beside the thing it changes, in the existing projects.

**`Core.Tests`**

- `GrepPatternTests`: a literal kind treats `foo.` as three characters and a basic kind as a dot; `TryMatch`
  reports the first match's 1-based column in both kinds and both cases; a query the engine cannot carry is
  refused with a reason and no caller's advice.
- `TemplateLinesTests`: `Split` counts the physical lines the editor shows — `\r\n`, `\n` and a lone `\r` are
  breaks, a trailing break ends a line rather than starting one, and an empty template is one empty line.

**`Web.Tests` — the scan and the catalog**

- `PageSourceScanTests`: a literal is found in a scope; order is key then line; a line carries its column; one
  row per matching line even when it matches twice; a tombstone is not a source; a route is searched and a stored
  head it shadows is read once; the limit stops on the confirmed extra match and the cursor names it; a cursor
  resumes mid-source with no repeat and no gap; a slice that finds nothing still advances; a fake clock expires
  the allowance and the cursor names the untested line; a changed fingerprint discards the cursor and restarts;
  a scan past the cap is refused naming `Pages:Search:MaxMatches`; an empty pattern and an unreadable one are
  refused before any source is read.
- `PageCatalogScopeTests`: a search answers the store's matching lines; the answer carries no source or hash; a
  page can search and still read its whole source allowance (no `ReadBudget` charge); an abandoned render throws;
  the read is still public; a cursor that cannot name a key is refused.
- `PageSearchOptionsTests`: the shipped values; an override; a missing section naming `Pages:Search:MaxDurationMs`;
  a duration of zero and one above the ceiling; `DefaultMatches` above `MaxMatches`; `MaxMatches` above the
  ceiling.
- `PageMetricsTests`: one search records one duration and one `completed`; a fake clock that expires records
  `cut-off`; the `caller` tag distinguishes the three.
- `PageAuthorPromptTests`: the search paragraph is there, the three version literals move, the example compiles.
- `HelpReferenceLinksTests`: every new `/help` link resolves, including `#m-IPageCatalog.SearchAsync-PageSearchRequest`.
- `FakePageReadStore` implements `ScanAsync`; `PageHarness` wires the scan; `ShippedConfigurationTests` reads the
  new factory.

**`Web.IntegrationTests`**

- `PageCatalogTests` gains a search page and a walk: a literal finds a seeded phrase on the line it is on; a basic
  expression finds it too; an anonymous visitor gets the same answer; walking with the cursor visits every match
  once and ends on an empty answer; a save between two slices sets `Changed` and the walk starts again; a route's
  record is found; the answer carries no hash or source.
- `MetricNamesTests` picks the two instruments up with no edit, which is the check that the catalogue and the
  meter agree.

**`Web.Tests` — the assistant**

- `PageToolExecutorTests`: the JSON shape is unchanged; `foo.` is literal by default and a dot with `pattern:
  true`; the cap is `Pages:Search:MaxMatches`; shell records are searched and tombstones skipped; a timer cut-off
  sets `truncated`.
- `ShellPatternAndSearchTests`: `grep`'s lines, `-l`, `-c` and the depth hint are unchanged; a fake clock's
  cut-off prints the stop sentence.
- `SqlitePageReadStoreTests`: the stepped read's order, tombstone, inclusive `from` and one level.

## Docs

- `/help` — `product-manual-is-a-page`; the section, the language and the refusals.
- [pages](../pages.md#page-contract) — `IPageCatalog`'s fifth read, in the bullet and the public-by-default
  sentence.
- [storage](../storage.md) — the stepped read, the uncharged search, the fingerprint and the `Pages:Search`
  bounds.
- [assistant](../assistant.md) — `search_pages`'s pattern and the shared scan, and `grep`'s unchanged output.
- [metrics](../metrics.md) — the two rows, the buckets and the counts.
- [decisions](../decisions.md) — the new entry at the merge, the moved `GrepPattern` link and the
  `PageSourceSearch` sentence.
- The item and the order: `Plan` becomes this file, [the order](../README.md#the-order) links it, and
  [ide-host](ide-host.md) step 4 points here.

## Risks

- **A match's text is the whole line.** A page's line can be as large as a page (`Assistant:MaxPageChars`
  allows 256 KiB), so `MaxMatches` of them is 256 MiB in the host's memory before the response cap sees it. The
  open question below asks for the decision; the recommendation is a configured clip.
- **The fingerprint is part of the budget.** `VersionAsync` reads every head under the prefix — about 1.15 ms
  for 300 heads — so a 30 ms allowance spends a measurable share before the first line. The `cut-off` counter
  and the duration histogram are how an operator sees it, and the allowance is theirs to raise.
- **A busy store can restart a walk forever.** A page that edits between slices sees `Changed` every time. The
  answer does not hide it; a caller that wants a bound on its own work counts restarts. The listing makes the
  same trade one step softer, and `pages-a-listing-carries-a-fingerprint` records why a warning rather than a
  refusal.
- **`search_pages`'s `truncated` changes meaning.** It now says "there is more store to read" as well as "there
  are more matches"; a model that treated it as "narrow the query" may now be told to narrow a query that is
  already narrow. The tool's description is the fix, and it is in this change.
- **Removing `Assistant:MaxSearchMatches` moves a value an operator may have set.** The refusal at startup is
  deliberate; it is listed here because it is the one change an existing deployment will notice.
- **The prompt version is contended.** [assistant-shell-tool](assistant-shell-tool.md) also intends v57; the merge order settles it, and
  the number is moved in one place and asserted in three.

## Open questions

The item's own questions are answered, and this plan does not reopen them. These are the choices it cannot
settle from the item, and they are put before anything is built.

1. **Is a match's line returned whole, or clipped at a configured length?** The item says "the line and its
   position, and no window", and a whole line is what a find list shows. The recommendation: return it whole —
   it is what `search_pages` shows today — and add `Pages:Search:MaxLineChars` (512 shipped) with a clipped line
   ending in `…` if the owner would rather bound the answer's size. The alternative is to charge the text to the
   render's `ReadBudget`, which the item's answer explicitly rules out.
2. **Does the time allowance apply to `search_pages` and `grep` as well as to a page?** The recommendation: yes —
   one behaviour, one bound — with `grep`'s existing stop sentence and `search_pages`'s existing `truncated` key,
   and no new wording. The alternative is a page-only allowance, which leaves the assistant's scan unbounded.
3. **Is 30 ms right, with 100 default and 1000 largest?** The duration came from the owner's answer; the two
   limits are this plan's proposal, chosen so `search_pages` does not shrink from today's 1000-match answer.
4. **Does `search_pages` stay without a cursor?** The recommendation: yes, for the reason above; the tool's cap
   is the section's largest slice.
5. **Is `page-search` the right file and *pages-a-search-returns-matches* the right name?** The item pre-names
   the decision; the plan's file name is new and is written before the merge, as `product-plan-before-building`
   asks.

## Not in scope

- **Replace.** A search that edits is a batch with this scan choosing the edits; the two are separate
  capabilities and separate decisions.
- **An index.** FTS5 over page source is a store migration with triggers to maintain; `n` pages is a linear scan
  until a measurement says otherwise ([`code-n-is-small`](../decisions.md#code-n-is-small)).
- **The editor's own find within the open tab.** That is the browser's, and it needs no host.
- **A revision's history.** The owner's later vote puts it after the MVP, behind a flag in the request that defaults
  to the live source. The request is a value, so a member added later is not a contract break, and a tombstone
  stays out of it either way.
- **A cursor for `search_pages`.** `tool-surface-is-one-shell` owns the tool surface and is blocked by this item.

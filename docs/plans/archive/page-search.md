<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page can search the store's source

| | |
| --- | --- |
| Status | Merged 2026-11-21 — `pages-a-search-returns-matches`. Proposed 2026-11-21, agreed with five answers and the shell deferred to its rewrite, and built on `idea/cannot-search-the-store` |
| Size | One capability, one branch: the value, the stepped read, the scan, the catalog's projection, the manual and the measurements. A half of it has no caller, so it is not sliced |
| Spike | None. Every claim about what exists was read off `trunk` on 2026-11-21 (`c10e1d5`): `IPageCatalog`, `PageCatalogScope`, `PageStoreReader`, `PageSourceSearch`, `GrepPattern`, `PageScope`/`PageTree`, `Pages:Read`, `AssistantOptions`, `PageMetrics`, `StatusCatalogue` |
| Decisions | `pages-a-search-returns-matches` is new; `assistant-the-shell-is-posix` and `pages-the-store-is-a-tree` are edited in place, as `GrepPattern` moves and a second walker stays until the shell's rewrite |
| Backlog | [`cannot-search-the-store`](../../backlog/closed.md) — step 4 of [host support for the IDE](../ide-host.md), which places it and keeps its own record of the programme |

## The short version

The item says what the capability is and the owner answered its questions on 2026-11-20 and 2026-11-21. This
plan is the implementation of those answers:

- **The contract.** `IPageCatalog.SearchAsync(PageSearchRequest)` answers `PageSearch` — matching lines, each with a
  key, a 1-based line, the match's 1-based column, a snippet of the line around the match and the snippet's own
  column — plus a cursor, `Truncated`, the prefix's fingerprint and `Changed`. Five `Sdk` values do it; the page
  never meets a source, a hash or a template.
- **The scan.** A new `PageSourceScan` walks a *stepped* source sequence: one matcher, a time allowance, a cursor
  in key-then-line order, the match's column and length, and the caller's own line rule and projection. The page
  is its one caller today; the assistant's two arrive with their rewrite. `GrepPattern` moves to `Core.Pages` and
  grows a literal/basic switch and a match position; it stays the one pattern language
  (`assistant-the-shell-is-posix`), and `PageSourceSearch` stays where it is until the assistant's rewrite.
- **The read.** `IPageReadStore` gains a stepped read: `ScanAsync` yields live templates one at a time from one
  SQL statement, so the allowance covers the reading as well as the matching. The assistant's read seam is
  untouched until its rewrite; `SourcesAsync` keeps materialising for `grep` and `search_pages` until then.
- **The bound.** One new section, `Pages:Search`, carries the duration (30 ms shipped, a starting point the
  metrics tune), the default and largest `Limit` (100 and 1000) and the default and largest snippet `Context` (64
  and 1024). The duration covers the version read, the reading and the matching.
- **The measurements.** `page.search.duration` is a histogram tagged `caller`; `page.search.requests` is a counter
  tagged `caller` and `outcome` — `completed`, or `cut-off` when the timer stopped the scan. Each gets one panel,
  and the status page reads them like every other instrument (`metrics-one-panel-per-instrument`). `caller` is
  `page` today and gains `shell` with the rewrite.
- **The shell is not touched.** The owner's instruction: the virtual shell is rewritten next. This plan changes no
  applet, no `ShellResult` and no shell output. `grep` keeps its unbounded walk and its match cap, and the POSIX
  interruption the owner asked about — exit 2, a diagnostic on standard error, the selected lines on standard
  output — lands with that rewrite, where the shell can hold an exit status and an error channel at all.
- **The manual.** `/help` gains a section that teaches the pattern language, the snippet and the call; the prompt
  gains a paragraph and moves to `page-author-v57`; and [pages](../../pages.md), [storage](../../storage.md) and
  [metrics](../../metrics.md) say what changed.

The scan's loop, the read it stands on, and the values it carries are the whole of the risk; everything else is
wiring, a setting and a page of prose.

## What exists today

A page can list the tree and read one source at a time. Nothing searches what the sources say, so a pattern costs
`ReadAsync` per key, and every such read is charged to `Pages:Read:MaxSourceCharsPerRender` — 512 KiB across the
whole layout chain and shared by every layer, refused rather than trimmed past that
([`pages-a-listing-carries-facts`](../../decisions.md#pages-a-listing-carries-facts)). A site of thirty pages works
by accident; a site of three hundred cannot be searched at all.

The host can already do it and no page can:

| Where | What it is | Why the page cannot reach it |
| --- | --- | --- |
| [`PageSourceSearch.Find`](../../../src/uk.osric.asp.Web/Assistant/PageSourceSearch.cs) | one line scan over a list of sources: the caller's line rule, the caller's test, a limit that stops one match past the answer | it is `Web/Assistant`'s, internal, and takes the whole list at once |
| [`PageStoreReader.SourcesAsync`](../../../src/uk.osric.asp.Web/Assistant/PageStoreReader.cs) | the scope read: live pages and the site's own routes, in key order, a tombstone never among them | it is the assistant's seam, and it materialises every source before the first line is tested |
| [`GrepPattern`](../../../src/uk.osric.asp.Core/Assistant/GrepPattern.cs) | the one pattern compiler: a literal when the query has no metacharacter, otherwise a basic regular expression compiled `NonBacktracking`, refused when the engine cannot carry it | `Core.Assistant`, reachable from the shell and the tools, not from a page |
| [`PageScope`](../../../src/uk.osric.asp.Core/Pages/PageScope.cs), [`PageTree`](../../../src/uk.osric.asp.Core/Pages/PageTree.cs) | the one statement of "below", "directly below" and "inside this prefix" | reachable; the listing and the search both ask them (`pages-the-store-is-a-tree`) |
| [`IPageCatalog`](../../../src/uk.osric.asp.Sdk/IPageCatalog.cs) | four reads: a listing, one source, a history, one revision; and `ShellLinks()` | the seam was built for the literal questions, not for a scan |

The rest of the frame is fixed: a listing's cursor is a key and its version token is the prefix's fingerprint
([`pages-a-listing-carries-a-fingerprint`](../../decisions.md#pages-a-listing-carries-a-fingerprint)); a listing
descends unless asked not to ([`pages-a-listing-descends-unless-asked`](../../decisions.md#pages-a-listing-descends-unless-asked));
the read is public as the page that calls it ([`pages-reading-the-store-is-public`](../../decisions.md#pages-reading-the-store-is-public));
the read capability sits on `IPageReadStore` because a page's task outlives the request
([`pages-the-store-is-read-through-one-seam`](../../decisions.md#pages-the-store-is-read-through-one-seam)); and every
limit is configuration, never a literal ([`code-policy-values-are-configuration`](../../decisions.md#code-policy-values-are-configuration)).

## The shape

### The contract

Five values in [`uk.osric.asp.Sdk`](../../../src/uk.osric.asp.Sdk), so a page can name them and nothing else:

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
    int? Context = null,
    string? Version = null);

/// <summary>One matching line: its key and line, the match's column, and the snippet around it.</summary>
/// <param name="Text">The snippet, centred on the match and never shorter than it.</param>
/// <param name="TextColumn">The 1-based column in the line where <paramref name="Text"/> starts.</param>
/// <param name="Clipped">Whether <paramref name="Text"/> is a fragment of the line rather than the whole of it.</param>
public readonly record struct PageSearchMatch(string Key, int Line, int Column, string Text, int TextColumn, bool Clipped);

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

The defaults are the request: an ignore-case literal over the whole store, descending, with the host's default
window (64 characters shipped).
The order of the parameters follows the listing's — `Prefix`, `Recursive`, then the slice's `After`, `Limit` and
`Version` — with `Context` beside `Limit`, because it is the other thing that bounds the answer. The kind and the
case sit after the scope and before the slice, because they are about the pattern rather than about where the
walk is.

The rules the doc comments state, and the tests prove:

| Rule | What it means |
| --- | --- |
| **Matches, not sources** | A row is a snippet of a line that matched, with its position. No `PageSource`, no hash, no template and no image crosses the seam, so a search cannot be turned into a bulk read and no `ReadBudget` is charged |
| **A match carries a snippet, not the line** | `Text` is the line cut to `Context` — or `Pages:Search:DefaultContextChars` when the request names none — centred on the match and never shorter than the match itself. `TextColumn` is the 1-based column where the snippet starts in the line and `Clipped` says it is a fragment, so a caller finds the match inside the snippet at `Column - TextColumn`. The search itself reads the whole line; only a page's projection is cut |
| **One row per line** | Several matches on one line are one row, carrying the column of the *first*; a caller that wants the others searches a narrower pattern |
| **Key, then line** | The order is ordinal key and then ascending line, and the cursor is in that order, so a walk of any store neither skips nor repeats a position |
| **The cursor is the first position not included** | A limit that stops the scan returns the position of the confirmed extra match, because a scan that stopped one line early would lose it. A timer that stops it returns the line it did not reach. Either way, one rule: ask again from `Cursor` |
| **A changed store restarts the walk** | `Version` is the prefix's fingerprint at any depth. If the request carried a token and this answer reads a different one, `Changed` is true, the cursor is *not trusted*, and the scan starts at the beginning. The listing only warns; a scan reads text, so a moved store can silently skip a *match* rather than a row, and the honest answer is to start again |
| **The scope is the listing's** | `Prefix` is a subtree, never a string prefix; `Recursive` descends unless asked not to; `""` and `/` are the whole store (`pages-the-store-is-a-tree`) |
| **Sources are live pages and the site's routes** | A tombstone is never a source. A key the shell's routes answer is searched as the route's record, and a stored head it shadows is not read twice |
| **The pattern is refused before a source is read** | An empty pattern, a key that cannot name one, a cursor outside its range, a limit or context outside its range and a pattern the engine cannot carry are `ArgumentException`/`ArgumentOutOfRangeException` — page bugs, as the listing's are — and an out-of-range limit or context names the setting to raise |
| **Public as the page** | No gate, as `pages-reading-the-store-is-public` says. A page that wants its search private writes the ordinary `ctx.User` guard |

`Truncated` says the scan stopped before the end of its scope — the limit or the duration — and `Cursor` names
where to carry on; `false` means the scope ran out and `Cursor` is null. That is the listing's shape with one
difference: for a listing `Truncated` means a row stands after the last, and for a search it means *the scan did
not finish*, which is the only thing the scan can honestly say when a timer stopped it before it knew.

### The scan

[`PageSourceSearch`](../../../src/uk.osric.asp.Web/Assistant/PageSourceSearch.cs) becomes
`Web/Pages/PageSourceScan.cs`: one class, three callers, each keeping its own line rule and its own projection.

```csharp
internal enum PageSearchCaller { Page, Shell }   // Shell arrives with the shell's rewrite

/// <summary>Where a scan ended: the scope ran out, the caller's limit, or the time allowance.</summary>
internal enum PageSearchEnd { Exhausted, Limit, Duration }

internal sealed record PageSourceText(string Key, string Text);
internal sealed record PageScanMatch(string Key, int Line, int Column, int Length, string Text);
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
- **The line rule is a parameter**, a `Func<string, string[]>`, because the two rules must stay apart: the page
  passes `TemplateLines.Split` (the physical lines the editor and `read_page` count) and the shell will pass
  `ShellText.Split` (the POSIX count) when it uses the scan. They stay two rules for the reason
  [`pages-the-store-is-a-tree`](../../decisions.md#pages-the-store-is-a-tree) records: merging them makes an edit
  address the wrong line in a page that does not end with a newline.
- **The scan answers the line, the catalog makes the snippet.** `PageScanMatch.Text` is the whole line and
  `Length` is the match's length, so a caller that prints lines — the assistant's, when the rewrite brings them —
  prints them whole, and the catalog sizes a window without running the pattern twice.
- **The window is a pure function.** `Core/Pages/LineSnippet` takes the line, the match's index and length and
  the requested context, and answers the snippet, its 1-based start and whether it was clipped:
  `width = min(max(context, length), line.Length)`, started at `index - (width - length) / 2` and clamped to
  `0 .. line.Length - width`. The arithmetic is proved in `Core`'s suite with no store and no harness.
- **One source of text, for now.** `PageSourceText` is the scan's input. Today the catalog's is
  `IPageReadStore.ScanAsync` merged with the route records from `ShellCatalogue.Text`; when the assistant's
  rewrite moves onto the scan, `IPageStoreReader` answers the same shape, because it already reads the route
  records as part of the tree the shell reads.

### The read it stands on

One stepped read, in key order:

```csharp
// Web/Pages/IPageReadStore.cs — the page's read, one statement, live heads only.
IAsyncEnumerable<PageReadSource> ScanAsync(
    string prefix, string? from, bool recursive, CancellationToken cancellationToken = default);
```

`IPageReadStore.ScanAsync` is the one new statement. It is the listing's query without the limit and without the
fingerprint: `HeadsUnder(prefix, recursive)`, live heads only, `Path >= from`, joined to the revision for the
template, ordered by path, and streamed with `AsAsyncEnumerable()`. `from` is **inclusive**, unlike the listing's
exclusive `after`, because the cursor resumes inside the source it names. The deferred read transaction of
`InReadSnapshotAsync` is unnecessary: one streaming statement holds SQLite's snapshot for as long as it runs,
nothing is written and nothing is pinned beyond it.

`IPageStoreReader` is deliberately unchanged: it keeps `SourcesAsync` and its tree walk, so `grep`,
`search_pages` and every fixture that reads through the assistant's seam behave exactly as they do today. When
the rewrite brings those two onto the scan, this read is what the page and the shell then share (the rewrite's
own work; see [Not in scope](#not-in-scope)).

### Who calls it

**`PageCatalogScope`** ([`PageCatalogScope.cs`](../../../src/uk.osric.asp.Web/Pages/PageCatalogScope.cs)) is the one
caller this plan adds. It normalises the prefix and the cursor, compiles the pattern, checks the limit against
`Pages:Search:MaxMatches` and the context against `Pages:Search:MaxContextChars`, calls the scan with the catalog's
own source (the store's stepped read plus `reserved.Keys`), cuts each match with `LineSnippet`, and maps the
result to `PageSearch`. It keeps the two refusals every member has — an ended render throws
`InvalidOperationException`, and the store's deadline is linked into the token.

**The assistant's two front-ends wait.** The owner's answer is that the shell is rewritten next, and
`search_pages` goes with it (`tool-surface-is-one-shell`), so this plan changes no applet and no tool surface: the
only edit under `Tools` is `PageToolData.TemplateSourceLines` delegating to `Core`, so the physical-line rule has
one implementation while the tool lives. `PageSourceSearch` stays in `Web/Assistant` for them until the rewrite,
and `GrepPattern` is still the one pattern language for all three because the page's scan uses it too. The timer
therefore bounds the page's scan now and the assistant's scan from the rewrite; until then `grep` and
`search_pages` keep today's match caps, their output and their unbounded walk. **When they are rewritten they use
this scan**, and the shell's POSIX interruption — exit 2, the diagnostic on standard error, the selected lines on
standard output — lands with that rewrite, because the shell has neither an exit status nor an error channel to
put it in today.

### Configuration

One section, five values, read through `ConfigurationReader` into a `PageSearchOptions` record beside
`PageReadOptions`:

```json
"Search": {
  "MaxDurationMs": 30,
  "DefaultMatches": 100,
  "MaxMatches": 1000,
  "DefaultContextChars": 64,
  "MaxContextChars": 1024
}
```

| Key | Shipped | Range | Why |
| --- | --- | --- | --- |
| `MaxDurationMs` | 30 | 1 to 60 000 | The owner's answer: a bound measured in time, because a count bounds the answer while the scan's work is done whether anything matches. 30 was picked from the air and the metrics are how it is tuned. The top of the range is the longest render the site accepts (`Pages:Render:TimeoutSeconds`'s own maximum), so a mistake cannot make a search outlive the thing it serves |
| `DefaultMatches` | 100 | 1 to 10 000, and never above `MaxMatches` | What `Limit: null` means. Refused above `MaxMatches` with a `FailIf`, the way `PageStorageOptions` refuses a page smaller than a value |
| `MaxMatches` | 1000 | 1 to 10 000 | The largest slice the page contract will answer, and the cap `search_pages` asks for |
| `DefaultContextChars` | 64 | 64 to 16 384, and never above `MaxContextChars` | What `Context: null` means, and the owner's floor: a window is at least 64 characters, or the match, whichever is longer |
| `MaxContextChars` | 1024 | 64 to 16 384 | The widest window a caller may ask for; above it the call is refused naming the key. The match is never cut, so a match longer than the window is answered whole |

`Assistant:MaxSearchMatches` **stays**. `search_pages` is deleted by
[`tool-surface-is-one-shell`](../../backlog/tool-surface-is-one-shell.md) within days of this plan, and removing its
setting now would rewrite a tool that is about to vanish; the plan that removes the tool removes the key beside
it. There is then one search behaviour left in the assistant — `grep` — and it keeps `Assistant:ShellMatchLimit`.

### Measurements

Two instruments on the Pages meter, beside the ones that already describe the store:

| Instrument | Type | Unit | Tags |
| --- | --- | --- | --- |
| `page.search.duration` | histogram | s | `caller` (`page`, `tool`, `shell`) — one scan, from the fingerprint to the last line tested |
| `page.search.requests` | counter | — | `caller`, `outcome` (`completed`, `cut-off`) — `cut-off` is the timer, and `completed` is every scan that ended on its own terms, including one the caller's limit stopped |

- **The histogram is the shape of the work**, and the tuning instrument the owner named: 30 ms is a starting
  point, not a derived number. Only the page reports until the shell's rewrite, so `caller` has one value now and
  the instrument does not change when it has two. Its buckets are `SearchBuckets`, one scale for a scan:
  `0.001, 0.0025, 0.005, 0.01, 0.015, 0.02, 0.025, 0.03, 0.04, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10`. The
  `0.03` boundary is the shipped allowance, so "how many scans finish inside the budget" is a cell rather than
  an interpolation, and the top covers the tool timeout a `search_pages` call runs under.
- **The counter answers the question the timer asks.** A rise in `cut-off` against a flat `completed` says the
  allowance is too small for the store; a rise in both says the site is busier. The two labels partition every
  call, so the panel is a stacked area, and the histogram is a heatmap (`metrics-one-panel-per-instrument`).
- **One panel each** in [`StatusCatalogue`](../../../src/uk.osric.asp.Web/Metrics/StatusCatalogue.cs), the
  histogram beside the other durations and the counter beside the other counters; `Program` adds the
  `ExplicitBucketHistogramConfiguration`, and `docs/metrics.md` gains the two rows and the counts.
- **No path label.** A search names a prefix a visitor typed, which is the cardinality trap `page.render.wait`
  and `page.asset.requests` already avoid; the access log names the page that asked.
- **Recorded once, by the scan**, so the page, the tool and the shell cannot measure one scan differently. Only a
  call that reached the store is recorded: a refusal is a page bug, not a slow search.

## What this plan does differently from the item's sketch

- **The assistant's read seam is untouched.** The item's sketch lifts `SourcesAsync` "into the seam the catalog
  already uses". The owner's instruction to skip the shell makes that premature: the assistant's seam reads
  through `PageEditor`, and the scope rules, the route records and the per-key folds are what its tests and its
  `grep -l`/`-c` depend on. So the page's seam gains the stepped read, the page's scan sits on it, and
  `PageSourceSearch`/`SourcesAsync` stay for the assistant until its rewrite, which will put both on this scan.
- **The assistant's two front-ends wait for the shell's rewrite.** The item gives `search_pages` a pattern flag
  and moves its cap, and has `grep` share the scan. The owner's instruction is that the shell is rewritten next
  and `search_pages` goes with it (`tool-surface-is-one-shell`), so this plan rewires neither: `PageSourceSearch`
  stays for them, they keep their caps and output, and the owner's POSIX interruption — exit 2 and stderr — lands
  with the rewrite, because the shell has neither today.
- **The assistant's scan is unbounded until then.** `grep` and `search_pages` keep the walk they have; the time
  allowance reaches them with the same rewrite, and `Pages:Search:MaxDurationMs` is already the value they will
  read.

## The work, in order

One branch, four commits, the gate at the end. Each commit leaves the build green and its tests failing without
it.

### 1. The values, the language and the window

- `Core/Pages/GrepPattern.cs` (moved from `Core/Assistant`): a `literal` parameter on the compile so a literal
  `foo.` stays literal and a basic one keeps the dot — a named `bool` rather than the `Sdk` enum, because `Core`
  cannot reference the page contract; `TryMatch(line, out int index, out int length)` for the first match's
  position; refusals return the reason without advice, and each caller adds its own sentence. The existing
  auto-detecting entry point stays for `grep` — the shell is not touched here — and delegates to the explicit
  one, so the language still has one implementation.
- `Core/Pages/LineSnippet.cs`: the window arithmetic, `(Text, Start, Clipped)`.
- `Core/Compilation/TemplateLines.cs`: `Split(string)` carries the physical-line rule `PageToolData` holds today,
  and `TemplateSourceLines` delegates to it, so the rule has one implementation while the tool lives.
- `Sdk`: the five types above, `IPageCatalog.SearchAsync`, and the `PublicAPI.Unshipped.txt` entries the analyzer
  asks for.

### 2. The stepped reads

- `IPageReadStore.ScanAsync` and `SqlitePageReadStore`'s implementation; `FakePageReadStore`'s in-memory twin.
- The read store's tests: order, tombstone, inclusive `from`, one level, and the unchanged `SourcesAsync`.

### 3. The scan and the catalog

- `PageSourceScan`, a new file beside the catalog (`PageSourceSearch` stays for the assistant), with
  `PageSearchCaller`, `PageScanQuery`, `PageScanMatch`, `PageScanResult` and `PageSourceText`.
- `PageSearchOptions` + `TestOptions.Search` + `ShippedConfigurationTests` + `PageSearchOptionsTests`, the five
  values and their two cross-checks.
- `PageCatalogScope.SearchAsync`, the `Sdk` mapping (including `LineSnippet`), and the constructors that hand a
  catalog its scan: `PageChain`, `PageJobRunner`, `PageEnvironment`, `PageJobEnvironment`, `PageHarness`,
  `Program`.
- `PageMetrics`: the two instruments, `RecordSearch`, `SearchBuckets`; `Program`'s view; the two
  `StatusCatalogue` panels.
- The catalog's tests and the scan's own suite.

### 4. The manual, the prompt and the record

- `/help`'s `Reading the page store` section gains `Searching the store`: the call, the cursor, the version, the
  snippet and its context, the kinds, and the language table (a literal is literal; `^`, `$`, `.`, `*`, `[abc]`
  are metacharacters, `\|` is alternation, everything else — `(`, `)`, `+`, `?`, `{` included — is the
  character).
- `PageAuthorPrompt`: a paragraph in `## Page store read`, the "All four are awaited" sentence becomes five, and
  `Version` moves to `page-author-v57`. Three tests hold the literal `page-author-v56` and move together.
  [assistant-shell-tool](../assistant-shell-tool.md) also intends v57; it is blocked by this item, so this plan
  lands first and that plan's merge takes v58.
- [pages](../../pages.md), [storage](../../storage.md) and [metrics](../../metrics.md).
- The record: at the merge, the entry **pages-a-search-returns-matches** — the snippet and its offsets, the one
  pattern language, the time allowance and the cursor — the `GrepPattern` link in `assistant-the-shell-is-posix`
  as the file moves, and the `PageSourceSearch` sentence in `pages-the-store-is-a-tree` as the second walker
  stays for now.

## Tests

One suite per layer, beside the thing it changes, in the existing projects.

**`Core.Tests`**

- `GrepPatternTests`: a literal kind treats `foo.` as three characters and a basic kind as a dot; `TryMatch`
  reports the first match's index and length in both kinds and both cases; a query the engine cannot carry is
  refused with a reason and no caller's advice.
- `LineSnippetTests`: a window is centred on the match; a window at the start or the end of the line is clamped;
  a match longer than the context is answered whole; a line shorter than the context is answered whole; the
  start column and the clipped flag describe what was cut.
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
- `PageCatalogScopeTests`: a search answers snippets whose `Column - TextColumn` finds the match; the answer
  carries no source or hash; a page can search and still read its whole source allowance (no `ReadBudget`
  charge); no `Context` takes the default and a larger one widens the window; a context above the cap is refused
  naming `Pages:Search:MaxContextChars`; an abandoned render throws; the read is still public; a cursor that
  cannot name a key is refused.
- `PageSearchOptionsTests`: the shipped values; an override; a missing section naming `Pages:Search:MaxDurationMs`;
  a duration of zero and one above the ceiling; `DefaultMatches` above `MaxMatches`; `MaxMatches` above the
  ceiling; `DefaultContextChars` above `MaxContextChars` and below 64; `MaxContextChars` above its ceiling.
- `PageMetricsTests`: one search records one duration and one `completed`; a fake clock that expires records
  `cut-off`; the record carries `caller=page`.
- `PageAuthorPromptTests`: the search paragraph is there, the three version literals move, the example compiles.
- `HelpReferenceLinksTests`: every new `/help` link resolves, including `#m-IPageCatalog.SearchAsync-PageSearchRequest`.
- `FakePageReadStore` implements `ScanAsync`; `PageHarness` wires the scan; `ShippedConfigurationTests` reads the
  new factory.
- `SqlitePageReadStoreTests`: the stepped read's order, tombstone, inclusive `from`, one level, and the unchanged
  `SourcesAsync`.

**`Web.IntegrationTests`**

- `PageCatalogTests` gains a search page and a walk: a literal finds a seeded phrase on the line it is on; a basic
  expression finds it too; an anonymous visitor gets the same answer; walking with the cursor visits every match
  once and ends on an empty answer; a save between two slices sets `Changed` and the walk starts again; a route's
  record is found; the answer carries no hash or source.
- `MetricNamesTests` picks the two instruments up with no edit, which is the check that the catalogue and the
  meter agree.

**The assistant's suites do not change.** `PageToolExecutorTests` and `ShellPatternAndSearchTests` pass as they
stand, which is the proof that no tool and no applet moved; the rewrite that replaces them brings their own tests.

## Docs

- `/help` — `product-manual-is-a-page`; the section, the language and the refusals.
- [pages](../../pages.md#page-contract) — `IPageCatalog`'s fifth read, in the bullet and the public-by-default
  sentence.
- [storage](../../storage.md) — the stepped read, the uncharged search, the fingerprint and the `Pages:Search`
  bounds.
- [assistant](../../assistant.md) — no behavioural change, so no edit; the shell's rewrite writes its half.
- [metrics](../../metrics.md) — the two rows, the buckets and the counts.
- [decisions](../../decisions.md) — the new entry at the merge, the moved `GrepPattern` link and the
  `PageSourceSearch` sentence.
- The item and the order: the item's answers 3 and 6 record the snippet and the deferral of the interruption,
  `Plan` names this file, [the order](../../README.md#the-order) links it, and [ide-host](../ide-host.md) step 4 points
  here.

## Risks

- **A match can be longer than the window.** The owner's rule is that the window contains the match, so a
  pattern that matches a whole line — `.*` does — answers the whole line however small the context. The snippet
  bounds the common case rather than every case, and the line's own cap (`Assistant:MaxPageChars`, 256 KiB)
  bounds the rest. The scan holds one line at a time whether or not a caller prints it.
- **The fingerprint is part of the budget.** `VersionAsync` reads every head under the prefix — about 1.15 ms
  for 300 heads — so a 30 ms allowance spends a measurable share before the first line. The `cut-off` counter
  and the duration histogram are how an operator sees it, and the allowance is theirs to raise.
- **A busy store can restart a walk forever.** A page that edits between slices sees `Changed` every time. The
  answer does not hide it; a caller that wants a bound on its own work counts restarts. The listing makes the
  same trade one step softer, and `pages-a-listing-carries-a-fingerprint` records why a warning rather than a
  refusal.
- **The assistant's scan stays unbounded for a while.** `grep` and `search_pages` keep their walk until the
  rewrite, so the one thing this plan does not fix is the cost of an assistant search over a large store. The
  setting that will bound it is already here, and the rewrite is the next step.
- **The prompt version is contended.** [assistant-shell-tool](../assistant-shell-tool.md) also intends v57; the merge order settles it, and
  the number is moved in one place and asserted in three.
- **The shipped thirty milliseconds is tight under load.** Built as written, but the integration suite runs
  eight fixtures at once and the allowance expired between two lines of a store of a few pages, so the
  fixture now sets `Pages:Search:MaxDurationMs` to the maximum the range accepts, for the reason it already
  widens the render and compile deadlines: a fixture is not about a deadline it did not ask for. The
  shipped value keeps its own tests, and `page.search.requests{outcome="cut-off"}` is how the owner decides
  whether it should move.
- **The status page's markup budget moved.** The two panels took the synthetic thirty-day page from under
  4.0 MB to about 4.11 MB, so `StatusPageSizeTests`'s ceiling was raised deliberately to 4.15 MB, still
  under the 4 MiB body cap the render refuses past.

## What the owner answered

Put to the owner before anything is built, and answered on 2026-11-21. The plan above is written to these answers;
no question is open.

1. **A match carries a snippet, and the caller chooses its width.** The owner's wording: "return context,
   `max(64, match.length)` wide, centred on the match", with the match's column; and "maybe give the caller the
   choice of the context size". The reading taken here: `PageSearchRequest.Context` is that choice, defaulting to
   `Pages:Search:DefaultContextChars` (64) and capped by `Pages:Search:MaxContextChars` (1024), with the match
   never cut. `Column`, `TextColumn` and `Clipped` describe where the snippet and the match sit.
2. **An interruption is POSIX's error, and the shell's rewrite carries it.** The owner asked whether POSIX `grep`
   has an obvious way of saying the results were interrupted. It has: an exit status above 1 — GNU uses 2 — with
   the diagnostic on standard error and the selected lines left on standard output. The owner then said to leave
   the shell alone because it is rewritten next, so nothing in this plan prints that status or that diagnostic;
   the rewrite adds both, and moves `grep` and `search_pages` onto this scan.
3. **30 ms is a starting point.** The owner picked it from the air and the metrics are what it is tuned by, so
   the plan stops arguing for the number and points at `page.search.duration` and
   `page.search.requests{outcome="cut-off"}` instead.
4. **`search_pages` is going, so it is not touched.** It keeps its arguments, its cap, its JSON and its walk until
   [`tool-surface-is-one-shell`](../../backlog/tool-surface-is-one-shell.md) deletes it; the shell's rewrite moves
   what is left onto this scan.
5. **The plan's name and the decision's name stand.** `page-search` is the file; **pages-a-search-returns-matches**
   is the entry the merge writes.
6. **Skip the shell.** "We'll do it when the shell gets its rewrite (soon!)" — so no applet, no `ShellResult`, no
   `IShellOutput` and no shell test moves in this plan, and the assistant's two front-ends read the new scan from
   that rewrite rather than from here.

## Not in scope

- **Replace.** A search that edits is a batch with this scan choosing the edits; the two are separate
  capabilities and separate decisions.
- **An index.** FTS5 over page source is a store migration with triggers to maintain; `n` pages is a linear scan
  until a measurement says otherwise ([`code-n-is-small`](../../decisions.md#code-n-is-small)).
- **The editor's own find within the open tab.** That is the browser's, and it needs no host.
- **A revision's history.** The owner's later vote puts it after the MVP, behind a flag in the request that defaults
  to the live source. The request is a value, so a member added later is not a contract break, and a tombstone
  stays out of it either way.
- **A cursor for `search_pages`.** It is deleted by `tool-surface-is-one-shell`; a cursor for it would be a shape
  written twice. The shell's `grep` has no cursor either.
- **The shell's rewrite.** The owner's instruction, and the next step: no applet changes here, and the POSIX
  interruption — exit 2, the diagnostic on standard error, the selected lines on standard output — plus the
  assistant's move onto this scan belong to it.

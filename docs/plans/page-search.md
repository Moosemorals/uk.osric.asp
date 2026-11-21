<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page can search the store's source

| | |
| --- | --- |
| Status | Proposed 2026-11-21 — the owner answered the five questions on 2026-11-21, this revision is those answers, and it waits for a yes. Nothing below is built |
| Size | One capability, one branch: the value, the stepped read, the scan, both assistant front-ends, the manual and the measurements. A half of it has no caller, so it is not sliced |
| Spike | None. Every claim about what exists was read off `trunk` on 2026-11-21 (`c10e1d5`): `IPageCatalog`, `PageCatalogScope`, `PageStoreReader`, `PageSourceSearch`, `GrepPattern`, `PageScope`/`PageTree`, `Pages:Read`, `AssistantOptions`, `PageMetrics`, `StatusCatalogue` |
| Decisions | None recorded yet. This plan states the name it intends: **pages-a-search-returns-matches**. It also edits two entries (a moved file and a stale sentence), below |
| Backlog | [`cannot-search-the-store`](../backlog/cannot-search-the-store.md) — step 4 of [host support for the IDE](ide-host.md), which places it and keeps its own record of the programme |

## The short version

The item says what the capability is and the owner answered its questions on 2026-11-20 and 2026-11-21. This
plan is the implementation of those answers:

- **The contract.** `IPageCatalog.SearchAsync(PageSearchRequest)` answers `PageSearch` — matching lines, each with a
  key, a 1-based line, the match's 1-based column, a snippet of the line around the match and the snippet's own
  column — plus a cursor, `Truncated`, the prefix's fingerprint and `Changed`. Five `Sdk` values do it; the page
  never meets a source, a hash or a template.
- **The scan.** `PageSourceSearch.Find` becomes `PageSourceScan`: one matcher over a *stepped* source sequence,
  with a time allowance, a cursor in key-then-line order, a column, and three callers — the page, `search_pages`
  and `grep` — that keep their own line rule and their own projection. `GrepPattern` moves to `Core.Pages` and
  grows a literal/basic switch and a column; it stays the one pattern language (`assistant-the-shell-is-posix`).
- **The read.** Both read seams gain a stepped read: `IPageReadStore.ScanAsync` yields live templates one at a time
  from one SQL statement (the page's), and `IPageStoreReader.ScanAsync` yields the scope's sources one at a time
  (the assistant's). `SourcesAsync` becomes the materialisation of its seam's stepped read, so there is one read
  per seam rather than two.
- **The bound.** One new section, `Pages:Search`, carries the duration (30 ms shipped, a starting point the
  metrics tune), the default and largest `Limit` (100 and 1000) and the default and largest snippet `Context` (64
  and 1024). The duration covers the version read, the reading and the matching.
- **The measurements.** `page.search.duration` is a histogram tagged `caller`; `page.search.requests` is a counter
  tagged `caller` and `outcome` — `completed`, or `cut-off` when the timer stopped the scan. Each gets one panel,
  and the status page reads them like every other instrument (`metrics-one-panel-per-instrument`).
- **The interruption.** A scan the timer stopped is interrupted results, so `grep` keeps its matches on standard
  output, writes one diagnostic to standard error and exits 2 — POSIX `grep`'s error band. The virtual shell gains
  the error channel and the exit code, and the shell tool's JSON carries both.
- **The manual.** `/help` gains a section that teaches the pattern language, the snippet and the call; the prompt
  gains a paragraph and moves to `page-author-v57`; and [pages](../pages.md), [storage](../storage.md),
  [assistant](../assistant.md) and [metrics](../metrics.md) say what changed.

The scan's loop, the read it stands on, and the values it carries are the whole of the risk; everything else is
wiring, a setting and a page of prose.

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

[`PageSourceSearch`](../../src/uk.osric.asp.Web/Assistant/PageSourceSearch.cs) becomes
`Web/Pages/PageSourceScan.cs`: one class, three callers, each keeping its own line rule and its own projection.

```csharp
internal enum PageSearchCaller { Page, Tool, Shell }

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
- **The line rule is a parameter**, a `Func<string, string[]>`. The page and `search_pages` pass
  `TemplateLines.Split` (the physical lines the editor and `read_page` count); `grep` passes `ShellText.Split`
  (the POSIX count). They stay two rules for the reason
  [`pages-the-store-is-a-tree`](../decisions.md#pages-the-store-is-a-tree) records: merging them makes an edit
  address the wrong line in a page that does not end with a newline.
- **The scan answers the line, the catalog makes the snippet.** `PageScanMatch.Text` is the whole line and
  `Length` is the match's length, so `grep` and `search_pages` print what they print today and the catalog can
  size a window without running the pattern twice.
- **The window is a pure function.** `Core/Pages/LineSnippet` takes the line, the match's index and length and
  the requested context, and answers the snippet, its 1-based start and whether it was clipped:
  `width = min(max(context, length), line.Length)`, started at `index - (width - length) / 2` and clamped to
  `0 .. line.Length - width`. The arithmetic is proved in `Core`'s suite with no store and no harness.
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

**`search_pages`** ([`PageTools.cs`](../../src/uk.osric.asp.Web/Assistant/Tools/PageTools.cs)) is one merge away
from deletion: [`tool-surface-is-one-shell`](../backlog/tool-surface-is-one-shell.md), which this item blocks,
removes it and folds its work into the shell. This plan therefore leaves its surface exactly as it is — no
`pattern` argument, no cursor — and only rewires it onto the shared scan so `PageSourceSearch` can go. It keeps
its literal match, its `Assistant:MaxSearchMatches` cap, its `path`/`line`/`text` JSON and its 240-character
trim. Its `truncated` now says the timer or the cap stopped it, and its description says "there is more to read".

**`grep`** ([`GrepApplet.cs`](../../src/uk.osric.asp.Web/Assistant/Shell/Applets/GrepApplet.cs)) keeps POSIX output
and gains POSIX's interruption. The line answer goes through the scan with `ShellMatchLimit` and
`ShellText.Split`, and prints the whole line, so `key:line: text` is unchanged; `-l`, `-c` and the depth hint keep
their folds. A match-limit stop keeps the `Stopped` note on standard output, because a cap is not an error. A timer
stop is different: the matches already found stay on standard output, one diagnostic goes to standard error, and
the exit status is 2.

**`PageCatalogScope`** ([`PageCatalogScope.cs`](../../src/uk.osric.asp.Web/Pages/PageCatalogScope.cs)) normalises
the prefix and the cursor, compiles the pattern, checks the limit against `Pages:Search:MaxMatches` and the
context against `Pages:Search:MaxContextChars`, calls the scan with the catalog's own source (the store's
stepped read plus `reserved.Keys`), cuts each match with `LineSnippet`, and maps the result to `PageSearch`. It
keeps the two refusals every member has — an ended render throws `InvalidOperationException`, and the store's
deadline is linked into the token.

### The interruption

POSIX `grep` says an error with an exit status above 1 — GNU uses 2 — and writes the diagnostic to standard error
while the lines it did select stay on standard output. A process a signal killed reports 128 + the signal, but a
program that notices it must stop reports the error status, and that is the case here: the timer is ours, not the
shell's signal. The virtual shell has one output and no status today, so it grows the two things that sentence
needs:

- `IShellOutput.Interrupted(note)` writes the note to standard error and marks the answer truncated — `Stopped`'s
  twin for the case where the note is a diagnostic; `ShellOutput` keeps `Text` and `Stderr` apart, so the note is
  not the answer.
- `ShellResult` gains `ExitCode` (0 by default) and the `ShellResult.Interrupted(summary)` call an applet makes
  for it. `IsSuccess` stays true, because there is an answer to read.
- The kernel copies the result's `ExitCode` and the output's `Stderr` into `ShellOutcome`, which gains both, so
  an applet writes text in one place and status in another.
- `ShellTool`'s JSON gains `exitCode` and `stderr` beside `output` and `truncated`, so the model sees the status
  rather than a sentence buried in the answer. That shape change is the one place the item's "the shell's output
  does not change" is superseded by the owner's answer.

`truncated` and the exit code then agree: an interrupted answer is marked truncated *and* exits 2, where a
match-limit answer is marked truncated and exits 0. A refusal stays the tool's failure as it is today: it produced
no answer at all.

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
[`tool-surface-is-one-shell`](../backlog/tool-surface-is-one-shell.md) within days of this plan, and removing its
setting now would rewrite a tool that is about to vanish; the plan that removes the tool removes the key beside
it. There is then one search behaviour left in the assistant — `grep` — and it keeps `Assistant:ShellMatchLimit`.

### Measurements

Two instruments on the Pages meter, beside the ones that already describe the store:

| Instrument | Type | Unit | Tags |
| --- | --- | --- | --- |
| `page.search.duration` | histogram | s | `caller` (`page`, `tool`, `shell`) — one scan, from the fingerprint to the last line tested |
| `page.search.requests` | counter | — | `caller`, `outcome` (`completed`, `cut-off`) — `cut-off` is the timer, and `completed` is every scan that ended on its own terms, including one the caller's limit stopped |

- **The histogram is the shape of the work**, and the tuning instrument the owner named: 30 ms is a starting
  point, not a derived number. Its buckets are `SearchBuckets`, one scale for a scan:
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
  behaviour; the shell keeps its match limit and the lines it prints, but its scan is stopped by the same
  allowance, and a stop is an interruption rather than a note on the answer.
- **`search_pages` is left alone.** The item gives it a pattern flag and moves its cap to the new section. The
  owner's answer is that the tool is deleted by [`tool-surface-is-one-shell`](../backlog/tool-surface-is-one-shell.md)
  within days, so this plan only rewires it onto the shared scan — literal match, own cap, JSON untouched — and
  leaves `Assistant:MaxSearchMatches` for the plan that removes the tool.
- **The shell gains an exit code and an error channel.** The item says the shell's output does not change; the
  owner's answer says an interrupted search must say so POSIX's way, and that needs both.
- **`grep -l` and `-c` stay on the per-key fold.** Their answer is a key or a count, and folding a line scan
  would cut a count at the match limit rather than at `ShellMatchLimit`. They read the stepped seam and
  materialise it, so the read is one implementation but the answer is unchanged.

## The work, in order

One branch, five commits, the gate at the end. Each commit leaves the build green and its tests failing without
it.

### 1. The values, the language and the window

- `Core/Pages/GrepPattern.cs` (moved from `Core/Assistant`): a `literal` parameter on `TryCompile` so a literal
  `foo.` stays literal and a basic one keeps the dot — a named `bool` rather than the `Sdk` enum, because `Core`
  cannot reference the page contract; `TryMatch(line, out int index, out int length)` for the first match's
  position; refusals return the reason without advice, and each caller adds its own sentence. The shell's
  auto-detect disappears: POSIX `grep` is a basic expression, and a query with no metacharacter is already the
  literal path.
- `Core/Pages/LineSnippet.cs`: the window arithmetic, `(Text, Start, Clipped)`.
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
- `PageSearchOptions` + `TestOptions.Search` + `ShippedConfigurationTests` + `PageSearchOptionsTests`, the five
  values and their two cross-checks.
- `PageCatalogScope.SearchAsync`, the `Sdk` mapping (including `LineSnippet`), and the constructors that hand a
  catalog its scan: `PageChain`, `PageJobRunner`, `PageEnvironment`, `PageJobEnvironment`, `PageHarness`,
  `Program`.
- `PageMetrics`: the two instruments, `RecordSearch`, `SearchBuckets`; `Program`'s view; the two
  `StatusCatalogue` panels.
- The catalog's tests and the scan's own suite.

### 4. The assistant's two front-ends and the interruption

- `SearchPagesTool`: rewired onto the shared scan, its surface and cap untouched, its description saying what
  `truncated` now means.
- `GrepApplet`: the scan for the line answer, the whole line on standard output, and the interrupted result —
  matches on stdout, a diagnostic on stderr, exit 2 for the timer and only the timer.
- The shell's channel: `IShellOutput.Diagnostic`, `ShellOutput.Stderr`, `ShellResult`/`ShellOutcome`'s `ExitCode`
  and `Diagnostic`, `ShellTool`'s `exitCode` and `stderr`; `ShellContext` gains the scan and `ShellRunner.RunAsync`
  takes it from `ShellTool`/`ShellKernelHarness`.
- The tool and shell tests, including the exit status and the stderr split.

### 5. The manual, the prompt and the record

- `/help`'s `Reading the page store` section gains `Searching the store`: the call, the cursor, the version, the
  snippet and its context, the kinds, and the language table (a literal is literal; `^`, `$`, `.`, `*`, `[abc]`
  are metacharacters, `\|` is alternation, everything else — `(`, `)`, `+`, `?`, `{` included — is the
  character).
- `PageAuthorPrompt`: a paragraph in `## Page store read`, the "All four are awaited" sentence becomes five, and
  `Version` moves to `page-author-v57`. Three tests hold the literal `page-author-v56` and move together.
  [assistant-shell-tool](assistant-shell-tool.md) also intends v57; it is blocked by this item, so this plan
  lands first and that plan's merge takes v58.
- [pages](../pages.md), [storage](../storage.md), [assistant](../assistant.md) and [metrics](../metrics.md).
- The record: at the merge, the entry **pages-a-search-returns-matches** — the snippet and its offsets, the one
  pattern language, the time allowance, the cursor and the interrupted result — the moved `GrepPattern` link in
  `assistant-the-shell-is-posix`, and the `PageSourceSearch` sentence in `pages-the-store-is-a-tree`.

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

- `PageToolExecutorTests`: `search_pages`'s JSON shape and arguments are unchanged and it is still a literal
  match; shell records are searched and tombstones skipped; a timer cut-off sets `truncated`; the shell tool's
  answer carries `exitCode` 0 for an ordinary command.
- `ShellPatternAndSearchTests`: `grep`'s lines, `-l`, `-c` and the depth hint are unchanged; with a fake clock a
  cut-off leaves the matches on stdout, writes the diagnostic to stderr and exits 2, while a `ShellMatchLimit`
  stop still writes its `Stopped` note to stdout and exits 0.
- `SqlitePageReadStoreTests`: the stepped read's order, tombstone, inclusive `from` and one level.

## Docs

- `/help` — `product-manual-is-a-page`; the section, the language and the refusals.
- [pages](../pages.md#page-contract) — `IPageCatalog`'s fifth read, in the bullet and the public-by-default
  sentence.
- [storage](../storage.md) — the stepped read, the uncharged search, the fingerprint and the `Pages:Search`
  bounds.
- [assistant](../assistant.md) — the shared scan, `search_pages` unchanged, and `grep`'s exit status and stderr.
- [metrics](../metrics.md) — the two rows, the buckets and the counts.
- [decisions](../decisions.md) — the new entry at the merge, the moved `GrepPattern` link and the
  `PageSourceSearch` sentence.
- The item and the order: the item's answers 3 and 6 record the snippet and the interruption, `Plan` names this
  file, [the order](../README.md#the-order) links it, and [ide-host](ide-host.md) step 4 points here.

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
- **`search_pages`'s `truncated` changes meaning.** It now says "there is more store to read" as well as "there
  are more matches"; a model that treated it as "narrow the query" may now be told to narrow a query that is
  already narrow. The tool's description is the fix, and the tool itself is deleted within days.
- **The shell's JSON grows two keys.** `exitCode` and `stderr` are new to the model's view of the shell; nothing
  that reads the old keys changes, but a prompt or a transcript that says the result is `output`/`truncated` is
  now incomplete.
- **The prompt version is contended.** [assistant-shell-tool](assistant-shell-tool.md) also intends v57; the merge order settles it, and
  the number is moved in one place and asserted in three.

## What the owner answered

Put to the owner before anything is built, and answered on 2026-11-21. The plan above is written to these answers;
no question is open.

1. **A match carries a snippet, and the caller chooses its width.** The owner's wording: "return context,
   `max(64, match.length)` wide, centred on the match", with the match's column; and "maybe give the caller the
   choice of the context size". The reading taken here: `PageSearchRequest.Context` is that choice, defaulting to
   `Pages:Search:DefaultContextChars` (64) and capped by `Pages:Search:MaxContextChars` (1024), with the match
   never cut. `Column`, `TextColumn` and `Clipped` describe where the snippet and the match sit.
2. **An interruption is POSIX's error.** The owner asked whether POSIX `grep` has an obvious way of saying the
   results were interrupted. It has: an exit status above 1 — GNU uses 2 — with the diagnostic on standard error
   and the selected lines left on standard output. The timer's answer is that, so `grep` exits 2 and writes
   stderr; the limit's answer stays the `Stopped` note on stdout, because a cap is not an error.
3. **30 ms is a starting point.** The owner picked it from the air and the metrics are what it is tuned by, so
   the plan stops arguing for the number and points at `page.search.duration` and
   `page.search.requests{outcome="cut-off"}` instead.
4. **`search_pages` is going, so it is not grown.** It keeps its arguments, its cap and its JSON until
   [`tool-surface-is-one-shell`](../backlog/tool-surface-is-one-shell.md) deletes it; this plan only moves it onto
   the shared scan.
5. **The plan's name and the decision's name stand.** `page-search` is the file; **pages-a-search-returns-matches**
   is the entry the merge writes.

## Not in scope

- **Replace.** A search that edits is a batch with this scan choosing the edits; the two are separate
  capabilities and separate decisions.
- **An index.** FTS5 over page source is a store migration with triggers to maintain; `n` pages is a linear scan
  until a measurement says otherwise ([`code-n-is-small`](../decisions.md#code-n-is-small)).
- **The editor's own find within the open tab.** That is the browser's, and it needs no host.
- **A revision's history.** The owner's later vote puts it after the MVP, behind a flag in the request that defaults
  to the live source. The request is a value, so a member added later is not a contract break, and a tombstone
  stays out of it either way.
- **A cursor for `search_pages`.** It is deleted by `tool-surface-is-one-shell`; a cursor for it would be a shape
  written twice. The shell's `grep` has no cursor either.

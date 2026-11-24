<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The site counts bytes

| | |
| --- | --- |
| Status | Merged 2026-11-24 — built as one change; proposed and answered the same day: bytes are the site's one size unit, the human-facing numbers keep their values and the manual states the smaller floor, the search snippet window moves to bytes with the rest, and the size shows in `stat` and as a column in `ls -l` |
| Size | Medium — a `Core` helper, eleven settings renamed, the sites that compare them, a field on two listing records, the surfaces that print a size, and the manual |
| Area | Core (pages, assistant), Sdk, Web (pages, assistant, configuration), docs, seeds |
| Spike | None. The counts were read off `trunk` on 2026-11-24: eleven setting names ending in `Chars` across thirteen entries, 62 `.Length` comparisons of which 11 are truncation slices, and every shape built after the storage model already limits in UTF-8 bytes. The files read: [`ContextBudget`](../../../src/uk.osric.asp.Core/Assistant/ContextBudget.cs), [`TokenEstimator`](../../../src/uk.osric.asp.Core/Assistant/TokenEstimator.cs), [`ToolResults`](../../../src/uk.osric.asp.Web/Assistant/Tools/ToolResults.cs), [`PageCatalogScope`](../../../src/uk.osric.asp.Web/Pages/PageCatalogScope.cs), [`BodyBudget`](../../../src/uk.osric.asp.Web/Pages/BodyBudget.cs), [`PageEntry`](../../../src/uk.osric.asp.Sdk/PageEntry.cs) |
| Backlog | [`a-page-has-no-size`](../../backlog/archive/a-page-has-no-size.md) is this plan, and it blocked [`tool-surface-is-one-shell`](../../backlog/tool-surface-is-one-shell.md) until the size a `stat` reports landed with it |
| Depends on | Nothing new. `TokenEstimator` already divides UTF-8 bytes by tokens per byte, and objects, messages, secrets, jobs, declared values and the archive already limit in bytes |
| Decisions | New names proposed: **code-a-size-is-bytes**, **pages-a-listing-row-carries-its-size** and **code-a-clip-ends-on-a-boundary**. Edits `pages-a-listing-carries-facts` in place (a row carries the head's size), `pages-allowances-are-a-rate` in place (a read's rate is bytes per second), `assistant-one-budget-decides-what-fits` in place (its shares are measured in bytes) and `assistant-tool-results-are-cut-not-refused` in place (the cut is at a byte limit, on a boundary). Relies on `code-policy-values-are-configuration`, `assistant-the-request-budget-is-tokens`, `assistant-the-budget-is-window-minus-reply`, `pages-a-listing-resumes-from-a-key` and `pages-a-folder-is-a-key-not-a-row` |

## The short version

The site counts two units. The newer shapes — objects, messages, secrets, jobs, the journal, declared
values, the archive — limit themselves in UTF-8 bytes, and so does the assistant's own estimator: a
token is `BytesPerToken` bytes, a request's anchor is its exact UTF-8 size. The older limits count
`string.Length`, which is UTF-16 code units, and they are named `...Chars`.

Five things are load-bearing:

1. **One unit, and it is bytes.** A limit that bounds text bounds its UTF-8 encoding, and the setting
   that holds it says `Bytes`. `string.Length` never guards a limit again.
2. **The store is what makes it cheap.** A page's size is `length(CAST(Template AS BLOB))` in the join
   a listing already makes — answered from the row's record header, with no column, no migration and
   no backfill. Bytes are the only unit the store can answer without reading the text.
3. **A clip ends on a boundary.** Cutting text to a byte limit must not split a code point; one
   helper in `Core` does every cut.
4. **The human numbers stay.** A name, a key or a question keeps its shipped number; the manual says
   what a byte buys (about a third of the room for CJK, a quarter for emoji). The search window moves
   to bytes like the rest.
5. **The two latent bugs close with it.** `ContextBudget` says a token is 1.6 bytes and the tool
   result cut compares `json.Length`, so a result of astral characters is cut later than its own
   budget allows; and `assistant-the-request-budget-is-tokens` already describes the estimator in
   bytes while the limits beside it count units.

## What exists when this starts

- **The byte-based shapes.** [`IObjectStore`](../../../src/uk.osric.asp.Sdk/IObjectStore.cs) limits bytes
  per object and per subject; messages, secrets, jobs, the journal, declared values and the archive
  all limit `Encoding.UTF8.GetByteCount` or the encoded array's length. None of them changes.
- **The assistant already counts bytes where it matters.** `TokenEstimator` divides a request's exact
  UTF-8 size by its token count
  ([`TokenEstimator.cs:82`](../../../src/uk.osric.asp.Core/Assistant/TokenEstimator.cs:82)), and
  `ContextBudget.BytesPerToken` is 1.6
  ([`ContextBudget.cs:20`](../../../src/uk.osric.asp.Core/Assistant/ContextBudget.cs:20)). But
  `ToolResultChars`, `MessageChars` and `MemoryCharsPerRun` are named for characters and are produced
  by `CharsOf(tokens) = tokens × BytesPerToken`
  ([`ContextBudget.cs:108`](../../../src/uk.osric.asp.Core/Assistant/ContextBudget.cs:108)), and the cut
  compares `json.Length` ([`ToolResults.cs:53`](../../../src/uk.osric.asp.Web/Assistant/Tools/ToolResults.cs:53)).
- **The char-named settings.** `Assistant:MaxPageChars`, `MaxToolArgumentChars`, `MaxMemoryChars` and
  `MaxQuestionChars`; `Pages:Render:MaxBodyChars`;
  `Pages:Read:SourceCharsPerSecond` and `Pages:Jobs:SourceCharsPerSecond`;
  `Pages:Stores:MaxKeyChars`, `Pages:Stores:Secrets:MaxNameChars`,
  `Pages:Stores:Records:MaxNameChars` and `Pages:Stores:Records:MaxResultChars`;
  `Pages:Search:DefaultContextChars` and `MaxContextChars`. Each has a shipped value, an options
  record, a `FromConfiguration` refusal and a test.
- **The char-guarded sites.** `PageCatalogScope` charges a read with
  `source.Template.Length` ([`PageCatalogScope.cs:181`](../../../src/uk.osric.asp.Web/Pages/PageCatalogScope.cs:181));
  `ReadBudget.TryCharge(int chars)`
  ([`ReadBudget.cs:62`](../../../src/uk.osric.asp.Web/Pages/ReadBudget.cs:62)); `BodyBudget.TryCharge`
  from `text.Length` ([`PageBody.cs:104`](../../../src/uk.osric.asp.Web/Pages/PageBody.cs:104));
  `ToolArguments.Parse` and the assistant's page, memory and question limits; the declared table's
  result limit ([`SqliteRecordStore.cs:464`](../../../src/uk.osric.asp.Web/Pages/SqliteRecordStore.cs:464));
  the search snippet window ([`PageCatalogScope.cs:127`](../../../src/uk.osric.asp.Web/Pages/PageCatalogScope.cs:127)).
- **Two name limits that are not settings.** `PageTopic.MaxNameChars` is a constant in `Sdk`
  ([`PageTopic.cs:21`](../../../src/uk.osric.asp.Sdk/PageTopic.cs:21)), and
  `PageSearchOptions.MaximumContextChars` is an invariant
  ([`PageSearchOptions.cs:31`](../../../src/uk.osric.asp.Web/Pages/PageSearchOptions.cs:31)); both keep
  their numbers and take the new name.
- **A listing row has no size.** `PageEntry` carries the key, the kind, the head's hash, saved time,
  state, libraries and seed — and no length
  ([`PageEntry.cs:50`](../../../src/uk.osric.asp.Sdk/PageEntry.cs:50)); `PageRevisionInfo` carries the
  hash, saved time and current mark ([`PageRevisionInfo.cs:13`](../../../src/uk.osric.asp.Sdk/PageRevisionInfo.cs:13)).
  Both queries join `PageRevisions`, whose `Template` is the text the answer would measure.
- **What prints a size today.** `stat` prints `{Template.Length} chars` for a page and a line/char
  count for a site route; `ls -l` prints the mode and the key; the conversation page's budget panel
  labels a figure `chars`.

## Goals

1. Every limit that bounds text bounds UTF-8 bytes, the setting says `Bytes`, and the retired key is
   refused at startup with its replacement named.
2. Every cut ends on a code-point boundary, and a truncated result says how many bytes it kept.
3. A listing row carries the head's stored byte length, so `ls`, `stat` and any view answer a page's
   size without reading it; a revision carries its own.
4. The size appears where the owner asked: `stat` reports it, and `ls -l` has a size column.
5. The manual, the prompt and every visible label say bytes where they said characters.
6. The assistant's shares and cuts are measured in the unit the estimator already uses.

## Non-goals

- **Grapheme clusters.** A reader's "character" is a cluster; counting them needs a library and
  answers nothing a limit can use.
- **Token accounting.** The window is still tokens, the estimator still anchors on
  `prompt_tokens`, and no token number moves.
- **Byte limits that are already bytes.** Objects, messages, secrets, jobs and the archive.
- **Rows, matches, lines and items.** A count of rows is a count, not a size, and keeps its unit.
- **Retuning the shipped numbers.** The owner's answer: the human-facing values keep theirs, and the
  manual states the floor. A value that proves wrong for CJK is a later item.
- **A stored size column.** The size is derived in the query; nothing is kept that can drift.

## The design

### D0 — The unit is bytes, and it is the only one

A size limit bounds the UTF-8 encoding of the text it guards. The setting that holds it is named for
the unit, and the refusal names it too: `Assistant:MaxPageBytes is 262144 bytes; the recommended value
is 262144`. `string.Length` stops being a limit anywhere — it stays what it is, an index into a
`string` — and a comparison against a configured number is a byte count.

Bytes rather than code points or UTF-16 units, for three reasons that are the code's, not a
preference:

- **The database can answer it.** SQLite stores a value's payload size in the record header, so
  `length(CAST(Template AS BLOB))` is the byte count without reading the text. A character count has
  to scan, and a UTF-16 count cannot be asked for at all.
- **The newer shapes already use it.** Objects, messages, secrets, jobs, the journal, declared values
  and the archive; switching the rest converges instead of adding a third unit.
- **The assistant's estimator already uses it.** A token is a byte ratio
  (`assistant-the-request-budget-is-tokens`), so a byte limit is the same unit as the budget it
  protects.

The name **code-a-size-is-bytes** records the rule: one unit, bytes, in the name of every setting that
holds a size.

### D1 — The helper, and the clip rule

Two functions in `Core`, beside the text values that already live there:

- `Utf8.SizeInBytes(string)` — `Encoding.UTF8.GetByteCount`, named once so no site spells the unit
  differently.
- `Utf8.Clip(string text, int maxBytes)` — the longest prefix whose UTF-8 encoding is at most
  `maxBytes`, counted in whole code points. It never returns a lone surrogate and never splits a
  multi-byte sequence. `Clip(text, SizeInBytes(text))` is `text`; a limit below one code point's size
  is the empty string, and the caller's note says so.

The encoding rules are .NET's: an unpaired surrogate encodes as U+FFFD, three bytes, and the helper
counts and clips the way `Encoding.UTF8` does, so the number a refusal quotes equals the number the
encoder will write. The clip walks `EnumerateRunes` and adds each rune's `Utf8SequenceLength`, which is
allocation-free.

**code-a-clip-ends-on-a-boundary** records the rule: wherever text is cut to a limit, the cut is the
helper's and ends on a boundary.

### D2 — The settings, renamed and refused

| Setting | Shipped | Becomes |
| --- | ---: | --- |
| `Assistant:MaxPageChars` | 262144 | `Assistant:MaxPageBytes` |
| `Assistant:MaxToolArgumentChars` | 270336 | `Assistant:MaxToolArgumentBytes` |
| `Assistant:MaxMemoryChars` | 1000 | `Assistant:MaxMemoryBytes` |
| `Assistant:MaxQuestionChars` | 500 | `Assistant:MaxQuestionBytes` |
| `Pages:Render:MaxBodyChars` | 4194304 | `Pages:Render:MaxBodyBytes` |
| `Pages:Read:SourceCharsPerSecond` | 104858 | `Pages:Read:SourceBytesPerSecond` |
| `Pages:Jobs:SourceCharsPerSecond` | 104858 | `Pages:Jobs:SourceBytesPerSecond` |
| `Pages:Stores:MaxKeyChars` | 200 | `Pages:Stores:MaxKeyBytes` |
| `Pages:Stores:Secrets:MaxNameChars` | 100 | `Pages:Stores:Secrets:MaxNameBytes` |
| `Pages:Stores:Records:MaxNameChars` | 64 | `Pages:Stores:Records:MaxNameBytes` |
| `Pages:Stores:Records:MaxResultChars` | 262144 | `Pages:Stores:Records:MaxResultBytes` |
| `Pages:Search:DefaultContextChars` | 64 | `Pages:Search:DefaultContextBytes` |
| `Pages:Search:MaxContextChars` | 1024 | `Pages:Search:MaxContextBytes` |

The shipped values do not move. Each options record reads the new key, its doc comment says bytes,
and the retired key is refused at startup in the collected report (`code-policy-values-are-configuration`):
the report names the old key, the new key and the shipped value, so an operator's partial override is
one edit rather than a restart per section. `Pages:Read` and `Pages:Jobs` carry the minimum rates and
the key constant the refusal quotes; those constants take the new name.

`PageTopic.MaxNameChars` becomes `MaxNameBytes` (64 stays), and `PageSearchOptions.MaximumContextChars`
becomes `MaximumContextBytes` (16384 stays). `PageSearchRequest.Context` is a byte window with the
same range and a refusal that says bytes.

The two places that compare one setting with another — `MaxToolArgumentBytes` must exceed
`MaxPageBytes` ([`AssistantOptionsValidator.cs:32`](../../../src/uk.osric.asp.Web/Assistant/AssistantOptionsValidator.cs:32))
— keep their rule and take the new names.

### D3 — The limit sites

Mechanical where the count is one, and careful where the text is already encoded:

- **A read is charged in the bytes it pulls in.** `PageCatalogScope.Charge` counts
  `Utf8.SizeInBytes(source.Template)` for a live read and a revision read;
  `ReadBudget.TryCharge(int bytes)` keeps its bucket and its rate, now bytes per second
  (`pages-allowances-are-a-rate` edited in place). A template larger than the burst is still refused
  whole rather than half-read.
- **A body is charged the bytes it writes.** The buffered path counts the encoded text;
  `BodyBudget` takes bytes and `PageBody` charges `Encoding.UTF8.GetBytes(text).Length` where it has
  already encoded, rather than scanning the string a second time.
- **The assistant's limits move with their settings.** `ToolArguments.Parse`, the memory normaliser,
  the question limits and the page-content checks all compare byte counts; the model-facing refusals
  name the byte limit.
- **The declared table's result** is measured in bytes (`MaxResultBytes`).
- **The search window is a byte window.** The snippet is cut around the match to the requested bytes
  by the helper, so a match near the edge of a CJK line is not split; `Clipped` still says the line
  continued.
- **The conversation page's budget panel** says bytes, and the figures it shows are the byte counts
  the limits use.

### D4 — The listing fact

Two records gain the head's stored size, both from the query that already joins `PageRevisions`:

- `PageEntry.SourceBytes` — `int?`, the stored template's UTF-8 byte count; null when the key has no
  head, so a folder has none. A tombstone carries the size of the revision it retains, because those
  bytes still exist at `/snapshots`.
- `PageRevisionInfo.SourceBytes` — the same count for the revision the row names, so a history
  answers it and `ls -l /snapshots` can show one.

Both are projected as `length(CAST(revision."Template" AS BLOB))` in
[`ListAsync`](../../../src/uk.osric.asp.Web/Pages/SqlitePageReadStore.cs:61) and the history query, so
the value cannot drift from the source, needs no column, no migration and no backfill, and old rows
answer exactly as new ones do.

**pages-a-listing-row-carries-its-size** records the contract: a listing row carries the head's size
in bytes, derived from the source, and a row with no head has none. It edits
`pages-a-listing-carries-facts` in place, because that decision enumerates what a row carries.

### D5 — The surfaces

- **`stat`** prints the size in bytes for a page and for a revision, and a site route still has no
  size: it has no stored source.
- **`ls -l`** gains a size column, read from the row: `<mode> <bytes> <key>` today, with the shell
  plan free to add the state and the hash beside it when it rewrites the applets. A folder and a site
  route print a dash where a size would be, as a shell does for what it cannot measure.
- **`list_pages`** answers `bytes` per row until the shell plan removes it; `read_page` keeps its
  line range and does not add one, because a caller that has the source can measure it.
- **`/help` and the prompt** say bytes wherever they bounded characters, and the author prompt's page
  and store sections gain one sentence: a limit is bytes, not characters, so a page of CJK reaches
  it sooner. The prompt's version moves to the next one; `update-the-prompt` rewrites it afterwards.
- **The pages** — [architecture](../../architecture.md#configuration) for the unit and the refusal,
  [pages](../../pages.md#page-contract) for the listing fact, [assistant](../../assistant.md) for the
  budgets, [storage](../../storage.md) for the object and result limits that were already bytes.

### D6 — What does not change

`Allowance`, `FetchBudget` and the fetch rate; the token estimate and every token number; the
declared-table result shape; the archive; the object store; `PageMode`; the row limits (`MaxRowsPerList`
is rows), match limits and listing cursors. The page API policy is untouched: a page writes text, and
the byte count is the host's.

## What it touches

| Where | What changes |
| --- | --- |
| `src/uk.osric.asp.Core/` | the `Utf8` helper and its tests; `ContextBudget`'s shares take byte names; `PageTopic.MaxNameBytes` |
| `src/uk.osric.asp.Sdk/` | `PageEntry.SourceBytes`, `PageRevisionInfo.SourceBytes`, and the doc comments that say characters |
| `src/uk.osric.asp.Web/Configuration/`, `AssistantOptions`, `PageReadOptions`, `PageJobOptions`, `PageSearchOptions`, `RenderOptions`, `PageStoresOptions` | the renamed keys, their refusals, the minimums and the invariants |
| `src/uk.osric.asp.Web/Pages/` | `PageCatalogScope`'s charge and snippet, `ReadBudget`, `BodyBudget`, `PageBody`, `SqlitePageReadStore`, `SqliteRecordStore`, `PageValueStore`, `StoreSecretSql`, `PageSearch` |
| `src/uk.osric.asp.Web/Assistant/` | `ToolResults`' cut, `ToolArguments.Parse`, the memory and question limits, the budget panel, `PageAuthorPrompt` |
| `src/uk.osric.asp.Web/Assistant/Shell/Applets/` | `StatApplet`, `LsApplet` |
| `src/uk.osric.asp.Web/Pages/Seeds/help` | every limit and snippet sentence, in bytes |
| `docs/` | architecture's configuration section, pages, assistant, storage; the decisions' edits |
| `test/…Core.Tests/`, `test/…Web.Tests/` | the helper's cases, each renamed key's shipped/override/refusal triple, the listing fact, the surfaces |

## Phases

Each phase is a commit or a short series, and each ends with its tests green.

0. **The helper.** `Utf8.SizeInBytes` and `Utf8.Clip` with their cases. No behaviour changes.
1. **The settings.** The thirteen entries renamed in `appsettings.json` and every options record; the
   retired keys refused with the replacement named; the invariants and minimums renamed. Every site
   that compares a setting now compares bytes.
2. **The cuts.** `ToolResults`, the body, the snippet and the remaining truncation sites use the
   helper; each note says bytes.
3. **The contract.** `PageEntry.SourceBytes` and `PageRevisionInfo.SourceBytes`, projected in the
   listing and history queries; the docs on both records.
4. **The surfaces.** `stat`, the `ls -l` column, `list_pages`, `/help`, the prompt, the pages.
5. **The measurement.** A listing and a `stat` over a store at the shipping size, before and after, so
   the "derived, not stored" decision has a number, and the byte counts of a page with CJK and with
   emoji checked by hand against `wc -c`.

## Tests

- **The helper.** ASCII, two-, three- and four-byte code points; a lone surrogate; text exactly at
  the limit; a limit below one code point; `Clip` is a prefix and `SizeInBytes(Clip(t, n)) <= n` for
  every case.
- **Each setting.** The shipped value, an override, and a refusal that names the key, the bytes and
  the recommended value; the retired key refused with its replacement.
- **The charges.** A page of emoji spends four bytes a character from `ReadBudget`; a body over its
  byte limit is refused; a tool result of astral characters is cut at the byte limit and its note
  says bytes; a search snippet never splits a code point.
- **The contract.** A listing row's `SourceBytes` equals `Encoding.UTF8.GetByteCount(source)` for a
  page, a revision, a tombstone and a site route; a folder has none; a query and a read answer the
  same number.
- **The surfaces.** `stat` prints the size; `ls -l` prints the column; a site route prints a dash;
  the prompt and `/help` name bytes.

## Risks

- **A clip splits a code point.** The one failure that corrupts the store, and the reason the helper
  is the only cut: a property test over every case, and the search snippet and tool cut as its two
  live callers.
- **A byte count costs a pass.** `string.Length` is free and a count is not; the body and tool paths
  charge an array they already encoded rather than scanning twice, and the listing derives its size
  in SQL, where it is a header read. The measurement in phase 5 is what says whether anything else
  needs attention.
- **An override uses the old key.** The process refuses to start with the replacement named; that is
  the policy value rule, not a regression, and it is the reason the refusal lists every problem.
- **The human numbers look cruel.** 200 bytes is 66 CJK characters for a key and 500 bytes is 166 for
  a question. The manual says so; the owner chose to keep the numbers, so a bad fit is a later item
  rather than a silent retune now.
- **Two units creep back.** A new limit written as `.Length` would pass every test. The decision and
  the manual name the unit; a review that sees `Chars` in a setting name sees the bug.

## Backlog items and what happens to them

| Item | Disposition |
| --- | --- |
| [`a-page-has-no-size`](../../backlog/archive/a-page-has-no-size.md) | this plan |
| [`tool-surface-is-one-shell`](../../backlog/tool-surface-is-one-shell.md) | blocked by this item; the size a `stat` reports is what it was missing, and its plan follows |
| [`nothing-checks-page-assets`](../../backlog/nothing-checks-page-assets.md) | a checker's input is a page's source; it is unaffected |
| [`the-page-api-signs`](../../backlog/the-page-api-signs.md), [`a-page-sees-its-request`](../../backlog/a-page-sees-its-request.md) | their body limits are already bytes; unchanged |
| [`secrets-are-readable-at-rest`](../../backlog/secrets-are-readable-at-rest.md), [`object-access-is-only-its-hash`](../../backlog/object-access-is-only-its-hash.md) | already in bytes; unchanged |

## Close-out

- The three names are written into [the record](../../decisions.md) with their "Recorded in" lines, and
  `pages-a-listing-carries-facts`, `pages-allowances-are-a-rate`,
  `assistant-one-budget-decides-what-fits` and `assistant-tool-results-are-cut-not-refused` are
  edited in place.
- [Pages](../../pages.md), [assistant](../../assistant.md), [storage](../../storage.md),
  [architecture](../../architecture.md#configuration) and
  [the manual](../../../src/uk.osric.asp.Web/Pages/Seeds/help) say bytes; no live document says a limit
  is measured in characters.
- `make check` is green, including `make decisions`, `make backlog` and `make check-pages`.
- `tool-surface-is-one-shell` is unblocked, and its plan's `stat` row changes from a gap to a fact.

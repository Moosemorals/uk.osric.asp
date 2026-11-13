<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Page store reader

| | |
| --- | --- |
| Status | Planned — implements [ARCH-16](../jobs/ARCH-16.md) |
| Size | Medium — one Core tree and scope value, one read seam in `Web`, the eight applets and four tool types rewired, and the tests and documents for each |
| Area | Core (Pages), Web (assistant tools and virtual shell) |
| Backlog | None. The edit-safety work has landed (DEC-269–DEC-273); this is the seam that work showed was missing |
| Decisions | D1–D7 below, referred to in prose until the merge takes `DEC-` numbers |

## The short version

The assistant's tools and its virtual shell both read the page store, and both decide what it
contains. Three listings, two source walks and four copies of the scope rule agree today only by
inspection. This change gives them one reader:

- **`IPageStoreReader`** answers the five questions a reader of the store has: the tree, the sources a
  scope may read, one key, a page's history, and one revision of one path.
- **`PageNode`** carries the facts — whether a key is a shell file or a tombstone, whether a save
  would be accepted, and the page's `PageImageState` — and both front-ends pick their own words.
- **`PageTree` and `PageScope`** move to `Core` as the one statement of "below", "directly below" and
  "inside this prefix".
- **`PageSourceSearch.Find`** is one line scan shared by `search_pages` and `grep`, which keep their
  own line rule, predicate, projection and wording.

The tools' JSON shapes, the applets' output and every write are unchanged.

## Goals

1. One place decides what the store contains, which keys are reserved, which tombstones are visible,
   what order keys come in and what a scope means.
2. The seam carries facts rather than pre-formatted words, so the shell's `ls -l` and the tool's
   `mode` column are the same fact spelled by the same function.
3. The tools answer revision hashes, saved times, compile state, diagnostics and history through the
   same seam the applets use, and stop reaching for `PageEditor` to read.
4. Every rule the seam owns is proved once, in the seam's own suite, instead of through both a tool
   and an applet.
5. Nothing a caller sees changes: no tool argument, no JSON field, no line of shell output.

## Non-goals

- **A write seam.** `write_page`, `edit_page`, `delete_page`, `restore_revision`, the editor page and
  the admin routes keep their own writes through `PageEditor` and `ToolPaths`. The seam is read-only.
- **One line rule.** `PageToolData.TemplateSourceLines` counts physical template lines that
  `read_page` and `edit_page` name; `ShellText.Split` counts terminated lines as POSIX does. Merging
  them would make `edit_page` replace the wrong line at a file that does not end in a newline.
- **Moving a route, a tool or an applet.** This is a seam, not a feature.
- **Caching.** `SourcesAsync` reads a page per key today and still does. `n` is small.
- **A generic file-system seam.** The store is a page store: the seam speaks keys, pages, revisions
  and compile state, not bytes and inodes.

## What exists today

| Piece | Where | What it answers |
| --- | --- | --- |
| `IPageStoreView` | `Assistant/Shell/PageStoreView.cs` | `NodesAsync`, `SourcesAsync(scopes, recursive)`, `ReadAsync` for the applets |
| `PageToolData.EntriesAsync` | `Assistant/Tools/PageTools.cs` | the `list_pages` rows: reserved keys first-class, tombstones on request |
| `SearchPagesTool.SourcesAsync` | `Assistant/Tools/PageTools.cs` | the `search_pages` walk, with its own `IsInScope`, `IsBelow`, `IsDirectlyBelow` |
| `PageToolData.IsUnderPrefix` | `Assistant/Tools/PageTools.cs` | a third spelling of "at or below" |
| `ShellTree` | `Assistant/Shell/ShellPath.cs` | `IsBelow`, `IsDirectlyBelow`, `FolderOf` |
| `PagePathPolicy.IsWithin` | `Assistant/PagePathPolicy.cs` | a fourth spelling, for the writable prefix |
| `PageEditor` | `Editor/PageEditor.cs` | pages, heads, histories and revisions, reached directly by every read tool |
| `PageImageStatus.Describe` | `Pages/PageImageState.cs` | the words a state is said in, called from three places |

The facts live in two shapes that have drifted: `ShellPath` pre-formats `Mode` and `ShellFile`
pre-formats `Status`, while the tools format `PageMode.Describe` and `PageImageStatus.Describe` again
from the page they read.

## Decisions

### D1 — One read seam, five questions, constructed where it is used

`IPageStoreView` is replaced by `IPageStoreReader` and one implementation, `PageStoreReader`, over
`PageEditor` and `PagePathPolicy`:

| Member | Answers |
| --- | --- |
| `TreeAsync(includeDeleted, ct)` | every live key, every reserved key, and every folder above them, in ordinal order — plus tombstones when asked |
| `SourcesAsync(scopes, ct)` | the live pages and shell records a scope may read, in key order |
| `ReadAsync(path, ct)` | one key: its node, its text, and the page when it has one |
| `HistoryAsync(path, ct)` | whether the head exists, whether it is a tombstone, and every retained revision newest first |
| `RevisionAsync(path, hash, ct)` | one revision, and null unless it belongs to that path |

It is constructed beside the caller — `ShellRunner` keeps building one per run and `ToolServices`
builds one per call — so no DI lifetime moves and no test double changes shape. Writes are not on the
seam (see the non-goals).

### D2 — The tree carries facts, and each front-end picks the words

`PageNode(Path, IsFolder, IsPage, IsShellFile, IsDeleted, CanWrite, State)` replaces `ShellPath`.
`PageRead(Node, Text, Page)` replaces `ShellFile` and `ShellSource`. `Mode` and `Status` strings leave
the seam: `ls -l` and `stat` format with `PageMode.Describe`/`DescribeFolder`, and the tools format
with `PageMode.Describe` and `PageImageStatus.Describe`. `PageImageStatus` stays the only spelling of
the words (DEC-258).

### D3 — One state for a page, whichever read reports it

`read_page` computes a compile state of its own through `PageToolData.ImageStatus`, which does not
pass the revision's `TemplateHasCode`; the listing does, so a page with no code reads "Not compiled"
from `read_page` and "Stored as source" from `list_pages`. The seam carries the listing's judgement —
`PageListing.State`, decided by `PageImageStatus` against the running build — and `read_page` reports
it. The answer for a no-code page therefore changes from "Not compiled" to "Stored as source", which
is what DEC-258 already says the state means.

### D4 — Scope is one value in `Core`

`PageTree` (from `ShellTree`) and `PageScope(Path, IncludeSelf, Recursive)` with `Contains(PagePath)`
move to `Core/Pages`, where `PagePath` already lives and where the rule can be tested without a store.
`PageScope` is the only statement of "at this key", "directly below it" and "anywhere below it":
`ls`, `find`, `ShellExpansion`, `grep`, `search_pages` and `list_pages` all ask it, and
`PageStoreView.InScope`, `SearchPagesTool.IsInScope`/`IsBelow`/`IsDirectlyBelow` and
`PageToolData.IsUnderPrefix` go. `PagePathPolicy.IsWithin` keeps its name — it answers whether a save
is inside `Assistant:WritablePathPrefix`, which is not a read scope — and asks `PageScope` rather than
spelling the rule a fifth time.

### D5 — Sources are reads in scope, and a tombstone is never one

`SourcesAsync` decides which keys a search reads and in what order, and every caller asks it: `grep`
without `-r` and `search_pages` read a level, `grep -r` descends. A tombstone is in the tree and is
never a source. The line scan itself becomes `PageSourceSearch.Find`, which walks sources in key
order, splits each with the caller's own line rule, keeps the caller's own predicate and stops one
match past the limit, answering the matches and whether there were more. `search_pages` and `grep`
keep their own projections (`{path, line, text}` capped at 240 characters, and `key:line: text`), and
`grep -l` and `grep -c` keep their own fold over the same sources.

### D6 — History and one revision are seam questions

`list_revisions`, `read_revision`, `restore_revision` and `diff_page` ask `HistoryAsync` and
`RevisionAsync` instead of `PageEditor`'s four reads, and keep their JSON shapes, cursors and refusals.
`HistoryAsync` answers the head's existence and deleted flag with the history, so "no such page" and
"a tombstoned page with a history" stop being three separate pairings. `RevisionAsync` filters by path
once, so `diff_page` no longer re-checks the two hashes itself. The writes stay `PageEditor.SaveAsync`,
`SaveIfRevisionAsync` and `DeleteAsync`.

### D7 — The seam is proved once, the front-ends keep their suites

`PageStoreReaderTests` owns every rule the seam states: a reserved key is a shell row even when the
store holds a page there; a tombstone is in the tree only when asked and is never a source; keys and
folders are ordinal; a scope includes the path itself or not, and descends or not, as asked; a history
is newest first; `RevisionAsync` answers null for another path's hash; a shell record is the read of a
reserved key; a no-code page's state is Stored as source. `PageTreeTests` and `PageScopeTests` own the
tree rule in `Core`. The existing suites — `PageToolExecutorTests`, `PageToolExecutorHistoryTests`,
`PageToolExecutorDiffTests`, `ShellPatternAndSearchTests`, `ShellRegistryTests`, `ShellRoutesTests` —
pass with type renames and the one assertion D3 changes.

## Phases

Each phase ends with a green `make check` — or, while the tree is mid-move, with the build and the
suites that its phase touches — and its tests written first.

### Phase 0 — `Core` learns the tree (half a day)

- `PageTree` and `PageScope` move into `Core/Pages`, with `PublicAPI.Unshipped.txt` entries.
- `PageTreeTests`, `PageScopeTests`.
- `PagePathPolicy.IsWithin` asks `PageScope`. The shell keeps its own `ShellTree` reference until
  phase 2, or switches in the same commit if that is smaller.

**Done when** the rule is stated once in `Core` and the build is green.

### Phase 1 — the seam exists (one day)

- `IPageStoreReader`, `PageStoreReader`, `PageNode`, `PageRead`, `PageHistory` and
  `PageSourceSearch`/`LineMatch` in `Web/Assistant`.
- `PageStoreReaderTests` over the in-memory store, for every rule in D5 and D7.
- Nothing calls it yet.

**Done when** the seam's own suite proves each rule and the existing suites are untouched.

### Phase 2 — the shell asks the seam (one day)

- `ShellPath`, `ShellFile` and `ShellSource` go; `ShellContext.Paths` and `Store` carry `PageNode`
  and `IPageStoreReader`.
- `ShellExpansion`, `ShellReading`, `ShellRunner` and the eight applets move to the seam, formatting
  `Mode` from `CanWrite` and `Status` from `State`.
- `grep` uses `SourcesAsync` and `PageSourceSearch.Find`.

**Done when** `ShellPatternAndSearchTests` and the shell cases in `PageToolExecutorTests` pass with
type renames only, and `ls -l`, `stat`, `cat -n` and `grep` print what they printed before.

### Phase 3 — the tools ask the seam (one day)

- `ToolServices.Store` replaces read access to `PageEditor`; `PageToolData.EntriesAsync`,
  `PageToolData.ImageStatus`, `SearchPagesTool.SourcesAsync` and the private scope copies go.
- `HistoryTools` and `DiffTool` move to `HistoryAsync`/`RevisionAsync`.
- The one assertion D3 changes is updated, with its reason in the test comment.

**Done when** the tool suites pass, `make check` is green and no tool reads `PageEditor` except to
write.

### Phase 4 — documents (half a day)

- [assistant](../assistant.md): the tools and the applets read through one seam, and what it answers.
- [architecture](../architecture.md): the seam in `Web`, the tree and the scope in `Core`.
- [ARCH-16](../jobs/ARCH-16.md) closes; the [programme](../jobs/README.md) records step 6 as done.
- `/help` is unchanged: none of this is a page-authoring capability.

## Risks

| Risk | Answer |
| --- | --- |
| A rule changes silently while three copies become one | The existing suites are the check: they must pass with type renames, and the one assertion that moves is D3's, recorded and tested. |
| A tombstone appears in `ls` or `find` | `TreeAsync` defaults to live keys; the only caller that asks for tombstones is `list_pages`. |
| The shell grows a dependency on a tool-shaped type | The seam's types are named for the store, not for either front-end; `ShellContext` still hands an applet only nodes, reads and the registry. |
| Reading a page per source is expensive | It is what both walks do today, and the store is small; a batch read is a separate idea. |
| The `read_page` state change surprises a page author | It is the state the page list and the editor already show, and it says what DEC-258 says. |

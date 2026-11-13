<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-16 — The assistant asks the page store the same questions twice

| | |
| --- | --- |
| Severity | Low |
| Area | Assistant (Web): tools and virtual shell |
| Location | `src/uk.osric.asp.Web/Assistant/Shell/PageStoreView.cs`, `ShellTree.cs`, `ShellExpansion.cs`, `ShellReading.cs`, `Applets/`; `src/uk.osric.asp.Web/Assistant/Tools/PageTools.cs`, `HistoryTools.cs`, `DiffTool.cs`, `ToolServices.cs` |

This job was written before the assistant edit-safety work (DEC-269–DEC-273). That work changed what
a read answers — every source line is numbered, the diagnostics the tools report include the
template-shape warnings, and `edit_page` answers with a diff — while the history tools ask the store
for revisions the shell's view does not know about. The seam has to carry the facts those projections
need, so the job is restated here against the tools as they stand.

## Problem

The shell and the page tools each decide what the page store contains. `IPageStoreView` answers
`NodesAsync`, `SourcesAsync` and `ReadAsync` for every applet, over `PageEditor` and
`PagePathPolicy`; the tools answer the same questions beside it:

- `list_pages` walks the store through `PageToolData.EntriesAsync`.
- `search_pages` walks it through `SearchPagesTool.SourcesAsync`.
- `read_page` reads `PageEditor.GetAsync` directly and works out the revision hash, saved time,
  compile state and diagnostics itself.
- `list_revisions`, `read_revision`, `restore_revision` and `diff_page` reach `PageEditor`'s history
  reads directly, because the view answers no history at all.

The rules are therefore written more than once and agree today only by inspection, not by
construction:

- **Reserved keys.** `PageStoreView.NodesAsync` lists every key in the policy's `Reserved` and
  skips a page stored at one; `PageToolData.EntriesAsync` repeats both rules; and
  `SearchPagesTool.SourcesAsync` repeats them a third time with `ShellRoutes.Text`.
- **Tombstones.** `NodesAsync` and the tool's source walk skip a tombstone; `EntriesAsync` includes it
  when the caller asks, and the tool filters the rest. Visibility is a parameter in one place and a
  branch in the others.
- **Order.** `NodesAsync` collects into a `SortedSet`, `EntriesAsync` sorts its rows and the tool's
  source walk sorts its tuples. They agree because every one of them spells `CompareOrdinal`.
- **Scope.** `PageStoreView.InScope`, `SearchPagesTool.IsInScope`/`IsBelow`/`IsDirectlyBelow`,
  `PageToolData.IsUnderPrefix` and `ShellTree` each state "at this key", "directly below it" or
  "anywhere below it". `PagePathPolicy.IsWithin` is a fifth spelling, though it answers a different
  question: whether a save is inside `Assistant:WritablePathPrefix`.
- **Facts and words.** `ShellPath.Mode` and `ShellFile.Status` are pre-formatted; the tools format
  the same facts again with `PageMode.Describe` and `PageImageStatus.Describe`. The seam should carry
  the fact — `CanWrite`, `PageImageState` — and let each front-end pick its words.
- **History.** `PageEditor.GetHeadAsync`, `ListRevisionsAsync`, `GetRevisionAsync` and
  `GetRevisionTemplateByHashAsync` are the only answers, but every tool re-pairs the head, the
  deleted flag and the revision, and `diff_page` re-checks that each hash belongs to the path.

Nothing compares the implementations, so a change to reserved-path matching, tombstone visibility,
ordering or scope has to be found and made in several places, and the next read tool will copy a
fourth. The edit-safety work added another projection of the same store — numbered source,
diagnostics, a diff — without adding a shared reader, which is the drift this job exists to stop.

## What the seam must answer

| Question | Today | Callers |
| --- | --- | --- |
| the tree: stored keys, shell keys and the folders above them | `IPageStoreView.NodesAsync`, `PageToolData.EntriesAsync` | `ls`, `find`, `ShellExpansion`, `list_pages` |
| the sources a scope may read | `IPageStoreView.SourcesAsync`, `SearchPagesTool.SourcesAsync` | `grep`, `search_pages` |
| one key: page source or shell record, and its facts | `IPageStoreView.ReadAsync`, `ReadPageTool`, `ShellReading` | `cat`, `stat`, `head`, `tail`, `wc`, `diff`, `read_page`, `edit_page`, `restore_revision` |
| a page's history: the head and its revisions | `PageEditor.GetHeadAsync`, `ListRevisionsAsync` | `list_revisions`, `diff_page` |
| one revision of one path | `PageEditor.GetRevisionAsync`, `GetRevisionTemplateByHashAsync` | `read_revision`, `restore_revision`, `diff_page` |

## Fix outline

1. **One read seam, widened.** Replace `IPageStoreView` with `IPageStoreReader` and one
   implementation, `PageStoreReader`, over `PageEditor` and `PagePathPolicy`; it is constructed where
   it is used, as the view is now, so no DI lifetime moves. It answers:
   - `TreeAsync` — every key the store holds, every reserved key and every folder above them, in
     ordinal order. A node carries `Path`, `IsFolder`, `IsPage`, `IsShellFile`, `IsDeleted`,
     `CanWrite` and the page's `PageImageState?`. A tombstone is present and marked; the shell's
     listing hides it and `list_pages` shows it only when asked.
   - `SourcesAsync(scope)` — the live pages and shell records a search may read, in key order.
   - `ReadAsync(path)` — the page template or the shell record with its node, or null when nothing is
     stored at the key.
   - `HistoryAsync(path)` — the head (exists, deleted) and every retained revision newest first.
   - `RevisionAsync(path, hash)` — the revision only when it belongs to that path.
2. **One tree and one scope.** Move `ShellTree` to `Core/Pages/PageTree` — it is pure and already
   speaks `PagePath` — with its public API entry, and add `PageScope(PagePath Path, bool IncludeSelf,
   bool Recursive)` with `Contains(PagePath)` and its entry too. `ls`, `find`, `ShellExpansion`,
   `list_pages`, `grep` and `search_pages` all ask it. Delete `PageStoreView.InScope`,
   `SearchPagesTool.IsInScope`/`IsBelow`/`IsDirectlyBelow` and `PageToolData.IsUnderPrefix`.
   `PagePathPolicy.IsWithin` stays, named as the writable-prefix rule rather than a scope.
3. **One source walk and one match loop.** `SourcesAsync` owns which sources are read and in what
   order. `search_pages` (a literal substring with a case flag) and `grep` (a basic regular
   expression, with `-r` or without) keep their own predicate, projection and wording; they share
   the walk, the "stop one past the limit, then say truncated" loop and the limit itself, not the
   result shape.
4. **History and revisions on the seam.** `list_revisions`, `read_revision`, `restore_revision` and
   `diff_page` ask `HistoryAsync` and `RevisionAsync` and keep their JSON shapes, cursors and
   refusals. `diff_page` takes its default pair from `HistoryAsync` and its two sources from
   `RevisionAsync(path, hash)`, so "this hash is not a revision of this path" has one answer.
   `restore_revision`'s no-op check reads the live page through `ReadAsync`; the save stays
   `PageEditor.SaveAsync`.
5. **Carry facts, not words.** A node carries `CanWrite` and `PageImageState?`, not `Mode` and
   `Status` strings; a read carries the `PageTemplate` with its hash, saved time, image and
   diagnostics. The shell formats with `PageMode.Describe`/`DescribeFolder` and
   `PageImageStatus.Describe`; the tools format with the same functions and then shape their JSON.
   `PageToolData.ImageStatus` and `ShellFile.Status` go away; the tool's diagnostic projection stays
   the tool's.
6. **Leave the front-ends alone.** A tool keeps its JSON schema, approval flag, write fact and
   declaration order (DEC-231); an applet keeps `ShellSyntax`, POSIX text and the sorted registry
   (DEC-224, DEC-226). The two line rules stay apart: `PageToolData.TemplateSourceLines` counts
   physical template lines that `read_page` and `edit_page` name, and `ShellText.Split` counts
   terminated lines as POSIX does. `read_page`/`read_revision` numbering and `cat -n` numbering are
   projections of the same text, not the seam's.
7. **Writes stay outside.** The seam is read-only. `write_page`, `edit_page`, `delete_page` and
   `restore_revision` keep going through `PageEditor.SaveAsync`/`SaveIfRevisionAsync`/`DeleteAsync`
   and `ToolPaths`; the seam may return the revision a conditional save checks, but it never saves.
8. **Prove it once.** Add `PageStoreReaderTests` for what the seam now owns: a reserved key is a
   shell row even when the store holds a page there; a tombstone is present with its flag; keys and
   folders are ordinal; `PageScope` includes the path itself or not, and descends or not, as asked;
   a history is newest first and `RevisionAsync` answers null for another path's hash; a shell record
   is the read of a reserved key. The page-tool suites (`PageToolExecutorTests`,
   `PageToolExecutorHistoryTests`, `PageToolExecutorDiffTests`) and the shell suites
   (`ShellPatternAndSearchTests`, and the shell cases in `PageToolExecutorTests`) should pass with
   only type renames; a changed assertion means the seam changed a rule and needs a decision.
9. **Documents.** [assistant](../assistant.md) says the tools and the applets read through one seam;
   [architecture](../architecture.md) places the seam in `Web` and `PageTree`/`PageScope` in `Core`.
   `/help` is unchanged: this is not an authoring capability.

**Not in this job:** a write seam, one line rule for the shell and the tools, a new tool or applet,
and any change to a tool's arguments or an applet's output.

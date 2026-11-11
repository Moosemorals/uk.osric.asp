<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-16 — The assistant asks the page store the same questions twice

| | |
| --- | --- |
| Severity | Low |
| Area | Assistant |
| Location | `src/uk.osric.asp.Web/Assistant/Shell/PageStoreView.cs`, `src/uk.osric.asp.Web/Assistant/Tools/PageTools.cs`, `src/uk.osric.asp.Web/Assistant/Tools/ToolServices.cs` |

## Problem

The shell and the page tools each decide what the page store contains. `IPageStoreView` answers
`NodesAsync`, `SourcesAsync` and `ReadAsync` for every applet, over `PageEditor` and
`PagePathPolicy`; the page tools reimplement the same three answers beside it. `list_pages` walks
the store through `PageToolData.EntriesAsync`, `search_pages` through `SearchPagesTool.SourcesAsync`,
and both carry private copies of the scope rule (`IsUnderPrefix`, `IsInScope`, `IsBelow`,
`IsDirectlyBelow`) that `ShellTree` states for the shell. `read_page` and `diff_page` reach
`PageEditor` directly because the view does not answer a revision hash, a saved time, a page's
diagnostics or its retained revisions.

The rules are therefore written twice and agree today only by inspection, not by construction: a
reserved key is listed as its own row and is never a stored page; a tombstone is skipped unless a
listing asks for deleted rows; keys are visited in ordinal order; and a scope is itself plus what is
below it, or only its immediate children when the search does not descend. Nothing compares the two
implementations, so a change to reserved-path matching, to tombstone visibility or to ordering has to
be found and made in both places, and the next read tool will copy a third.

## Fix outline

1. Give the tools one read seam rather than two. Extend `IPageStoreView` — or add a reader beside it
   if the shell's surface should stay narrow — with what a tool needs and an applet does not: a
   listing that can include tombstones, a page's revision hash, saved time, compile state and
   diagnostics, and the retained revisions of one key. `PageTools` then asks the seam and never
   reaches for `PageEditor`.
2. Let the seam answer scope once. "At this key", "directly below" and "anywhere below" become one
   question over `ShellTree` or a `Core` function, and the tool's private copies of the scope rule
   go away.
3. Have one match scan over the seam take a predicate, so `search_pages` (a literal substring, with
   its case flag) and `grep` (a basic regular expression) share the walk, the match limit and the
   truncation flag while keeping their own projection, wording and result shape.
4. Leave the two front-ends alone. A tool keeps its JSON schema, approval flag, write fact and
   declaration order; an applet keeps `ShellSyntax`, POSIX text and a sorted registry (DEC-231,
   DEC-224). The two line rules also stay apart: `PageToolData.TemplateSourceLines` counts physical
   template lines that the editor and `edit_page` name, and `ShellText.Split` counts terminated lines
   as POSIX does.
5. Prove the seam answers each rule once — reserved rows, tombstone visibility, key order — instead
   of proving it through both a tool and an applet. The four tool suites and the shell suite are
   expected to pass unmodified.

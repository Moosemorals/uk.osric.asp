<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-20 — A rendered heading has no anchor, and a page cannot choose the renderer's features

| | |
| --- | --- |
| Severity | Medium |
| Area | Page Markdown capability |
| Location | `src/uk.osric.asp.Runtime/MarkdownRenderer.cs`, `src/uk.osric.asp.Sdk/IMarkdownRenderer.cs` |

## Problem

A page that renders Markdown gets a heading with no `id`: `## Today` becomes `<h2>Today</h2>`, so
`#today` is a dead fragment and a table of contents cannot link to a section. The page profile is
also fixed — CommonMark, pipe tables and auto-links — and `IMarkdownRenderer` has one `ToHtml`, so a
page cannot say it wants that set without auto-links, or without anchors because it already supplies
its own IDs.

A page author can write the heading as raw HTML instead, but that loses the rest of Markdown and is
exactly the workaround a capability exists to avoid.

## Fix outline

- Give the page profile heading anchors. Markdig's `UseAutoIdentifiers` writes the `id` and numbers
  a repeated one, which is the behaviour a reader expects from a Markdown document. Its auto-link
  option stays off: `[Heading]` becoming a link is a parser change the author did not ask for.
- Let a page choose the profile's three features for one call with a `MarkdownOptions` value in `Sdk`
  and a second `ToHtml` overload. Options belong to the render rather than to the shared capability,
  so one page cannot change another's document.
- Keep the default the current profile plus anchors, so every existing page renders what it did and
  gains the fragment it was missing.
- Tests: an anchored heading and a repeated one; each option switched off; the default profile; a
  compiled page passing the options through the real capability; the prompt and the seeded manual.

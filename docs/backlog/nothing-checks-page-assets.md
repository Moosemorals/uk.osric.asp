<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Nothing checks a page's CSS, HTML or SVG

| | |
| --- | --- |
| Difficulty | persistent-salmon |
| Utility | low |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Core (Assistant), Runtime, Web (assistant), docs |
| Location | `src/uk.osric.asp.Runtime/` beside `JavaScriptValidator.cs`, `src/uk.osric.asp.Core/Assistant/`, `src/uk.osric.asp.Web/Assistant/Tools/CheckTool.cs`, `docs/assistant.md` |

## Idea

A page is four languages in one file: C#, the literal markup, the CSS in a `<style>` block or a
`.css` asset page, and the JavaScript in its script blocks. Two of the four are checked —
One item, [`check-without-storing`](check-without-storing.md), covers the C# by compiling the template,
and `JavaScriptValidator` covers the script — and the other two are checked by loading the page and
looking at it, which is the most expensive way to learn that a brace is missing.

The gap is not the assistant's alone. A validator is a pure function over text, so the same three serve
the editor's underline, the assistant's `check` and, one day, `make check-pages`. What does not exist
until something writes CSS in a page is a reason to pay for a parser.

## Sketch

1. **A checker is one type**, carrying its language, how it finds its regions in a template, how it
   maps a position back to a page line, and its validate — the shape the shell's applets settled on
   when there were twelve of them. `check`'s `switch` becomes this registry with the third language,
   and the tool's description renders the list from the registry so the prose cannot fall behind.

2. **The regions come from the blanking that already exists.** `TemplateScriptSkeleton` blanks template
   constructs with spaces and keeps every line break, so literal markup, `<style>` and `<svg>` survive
   it at their own line numbers. A source that is not a template — a `.css` or `.svg` asset page — has
   no constructs to blank and is wholly its own language.

3. **Order: CSS, then HTML, then SVG.** CSS is where a page's own mistakes concentrate and the
   smallest honest parser; HTML is the biggest piece of machinery for the smallest yield, because the
   page is a fragment by design; SVG is nearly free once XML is in reach.

4. **The same two answers as today.** A check returns `valid` plus diagnostics of line, column and
   message, invalid text is a successful call, and the result budget bounds the list — the rules
   `assistant-script-checks` already fixed for the script check.

5. **The `language` values are added, not renamed.** `check` gains `css`, `html` and `svg`; nothing
   else about the tool moves, and the `enum` `tool-arguments-say-nothing` emitted grows a value without the
   description being edited.

## What each costs

| Language | The check | The work |
| --- | --- | --- |
| `css` | tokenise, then parse rules, at-rules and declarations; a missing brace, a stray `;`, an unclosed comment, an unbalanced block | a parser written here: the framework has none. Property *names* and *values* are out of scope, so this is syntax and not correctness |
| `html` | tokenise and report the errors a tree builder would: an unclosed element, a mismatched end tag, an attribute with no name, a duplicate attribute | the largest piece, and it must accept a fragment, which is what a page is |
| `svg` | `System.Xml` well-formedness, plus the handful of rules that make well-formed SVG wrong: no root, a foreign element, an undeclared namespace prefix | the cheapest of the three, which is why it is on the list at all |

## Not in scope

- **Markdown.** It is hard to be wrong in, so a checker mostly reports style; if it is ever wanted it is
  a link-and-heading check with a name that says so, not a `language` value.
- **Conformance.** No specification's full rule set, no schema, no property validation: the answer is
  "this does not parse", not "this is not allowed".
- **A fixer.** Nothing here rewrites the author's text; the diff tools and the editor already do the
  rewriting, and a validator that also edits is two tools in one.
- **A browser or a DOM.** The check is a parse, so it never renders, resolves imports or fetches
  anything; that is what `view` is for.

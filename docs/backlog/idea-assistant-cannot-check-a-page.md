<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The assistant can check a script but not a page

| | |
| --- | --- |
| Difficulty | determined-mountain-goat |
| Utility | high |
| Status | blocked |
| Blocked by | `idea-check-without-storing` |
| Plan | — |
| Area | Core (Assistant), Web (assistant), docs |
| Location | `src/uk.osric.asp.Web/Assistant/Tools/ScriptTools.cs`, `ToolRegistry.cs`, `src/uk.osric.asp.Core/Assistant/ToolArguments.cs`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs`, `docs/assistant.md` |

## Idea

The assistant can ask whether a page's **JavaScript** parses, and nothing else. The question it asks
most — does this page compile — has one answer today, and that answer is a save: `write_page` and
`edit_page` compile because they store, so a template the assistant is unsure of reaches the store
before anyone knows whether it is good. [`idea-check-without-storing`](`idea-check-without-storing`.md) names exactly this and puts the answer
on the page contract as `CheckAsync(key, template)`; its first open question already says why the
assistant's case is the one that wants a read-only form of it.

Two things are wrong with the tool surface while that gap is open. The registry holds twenty tools and
a contract test pins the count — at the soft ceiling the provider guidance quotes — so a twenty-first
should replace something rather than be added; and two of the twenty are one tool each split by where
the text came from. `check_script` and `check_script_text` share `CheckScriptTool.CheckTemplate` and
differ only in `path` against `content`. `read_page` and `read_revision` share
`PageToolData.ProjectSource` and differ only in `hash`. In both pairs the axis the model cares about —
which validator, and therefore which language — is hidden behind a suffix naming the source.

## Sketch

1. **One tool, one axis.** `check { language, path?, content? }`, with exactly one of `path` and
   `content` and the same idiom `reference` already uses for `type` against `query`. `language` is
   required and is **never inferred** from the key's extension: a `.html` key is a template or an
   asset depending on whether it holds code, so the extension is not a contract.

2. **`javascript` is the pair, unchanged.** `path` is today's `check_script` and `content` is today's
   `check_script_text`, with the same blanking of template constructs, the same success-on-invalid
   rule and the same diagnostics. Nothing about the check moves; only the name it is asked by.

3. **`template` is the compile, borrowed and not rebuilt.** It calls [`idea-check-without-storing`](`idea-check-without-storing`.md)'s
   `CheckAsync` and answers its diagnostics, so `check(language: "template", content: X)` returns
   exactly what a save of `X` would, and there is one compile seam rather than two. That answers
   `idea-check-without-storing`'s first open question in the negative: the assistant needs no second capability, because a
   tool is host code and asks the same member a page would. This job therefore follows `idea-check-without-storing`; a tool
   that arrived first would have to grow the seam the page contract is already getting, which is the
   duplication the seam rule exists to prevent.

4. **The result is always a list.** `{ valid, checks: [{ language, valid, diagnostics }] }`, even when
   the list holds one entry, so the composite below can arrive without reshaping a result the
   provider's cached prefix has already been built against.

5. **Two checkers today is a `switch`, not a registry.** `n` is two; a table of one type per language
   is the shape [`idea-nothing-checks-page-assets`](`idea-nothing-checks-page-assets`.md) introduces with its third and fourth, exactly as the shell
   became applets when it had twelve commands rather than two. The refusal names the languages that
   are not there yet, in the wording `help` uses.

6. **`read_page` absorbs `read_revision`** in the same change: an optional `hash` selects a retained
   revision, the answer says which one it read, and `list_revisions` stays because a list is a
   different question from a read.

7. **Limits.** `Assistant:MaxCheckDiagnostics` is a new required policy value, because a broken page
   can answer with a thousand Roslyn diagnostics and a cap is a limit rather than an invariant; the
   validator's own twenty-hint cap stays in code. `content` is bounded by `Assistant:MaxPageChars`.

8. **Refusals.** A shell file is refused in every mode, by the same `ToolPaths.RequirePageSource` the
   script check already uses. Both tools are reads: no approval, no `Writes`, no `PathArgument`.

9. **The record.** A new name, proposed **assistant-a-check-names-its-language**, citing
   `assistant-script-checks` and `assistant-edit-page-is-safe`, both permanent and unedited. Its
   description, and every other tool's, is written by [`idea-tool-arguments-say-nothing`](`idea-tool-arguments-say-nothing`.md)'s slice rather than
   rewritten after it. The prompt
   loses the two lines that name `check_script_text` and `check_script` and gains one; `Version` moves.
   [assistant](../assistant.md) follows, and its "nineteen types" is already one behind the registry,
   so the count is corrected by the same edit. `/help` does not change: this is not a page capability,
   and the page-side check is `idea-check-without-storing`'s manual entry.

## The composite, flagged

Should `language: "template"` also run the embedded checkers over the script it finds, so one call
answers "is this page good"? It is tempting — the prompt already tells the model to check the script at
both ends of a change — but it spends a compile on runs that wanted syntax only, so it is a measurement
and not a preference.

- **The question.** Does a run call `check(language: "javascript")` and `check(language: "template")`
  against the same target, adjacently? If it does, one call is a saved step and a saved wait; if not,
  the composite is compile time nobody asked for.
- **The evidence already exists.** Tool calls and their arguments are stored in the transcript beside
  their results (`SqliteConversationStore`), so the question is a query over stored runs, not a new
  telemetry stream, and this job has to add nothing for it to be answerable later. A `language` tag on
  the existing tool metric would chart it, and the set is closed, so the cardinality is fine — but the
  transcript is sufficient.
- **How it flips.** The composite runs the embedded checkers, the `checks` list grows an entry, the
  description and the prompt say so, and the prompt version moves. No result shape changes.

## Why the read merge is in the same change

Both merges rewrite the tool definitions, and the tool list is part of the request body and so of the
provider's cached prefix (`assistant-a-tool-is-one-type`). One change pays one invalidated prefix, one
prompt version and one measurement; two changes pay it twice and ask the model to learn two namings in
sequence. [`idea-tool-arguments-say-nothing`](`idea-tool-arguments-say-nothing`.md) is in the same step for the same reason and a stronger one: its
argument descriptions would be written against definitions this job then deletes and merges.

## Not in scope

- **The CSS, HTML and SVG checkers.** Values of `language` that have no checker are refused by name
  until [`idea-nothing-checks-page-assets`](`idea-nothing-checks-page-assets`.md) lands; this job ships two.
- **An `enum` for `language`.** The closed set belongs in the schema rather than a sentence, and that is
  [`idea-tool-arguments-say-nothing`](`idea-tool-arguments-say-nothing`.md) — this plan's first slice, so `check` ships with the `enum` rather than
  gaining it later.
- **The page-side check.** `ctx.Capabilities` and `/help` are [`idea-check-without-storing`](`idea-check-without-storing`.md)'s.
- **Checking a fragment.** `content` is a complete template, for the reason the script check already
  records: a fragment can be invalid alone even when the edit that produced it is correct.
- **Inferring the language from the key.** The extension maps to a content type, not to a validator,
  and the map belongs to the host and may change.

## Where it lands

One plan, `docs/plans/assistant-tool-surface.md`, written once [`idea-check-without-storing`](`idea-check-without-storing`.md) has merged,
because by then the member it calls and the diagnostic it answers with are real rather than sketched. It
covers [`idea-tool-arguments-say-nothing`](`idea-tool-arguments-say-nothing`.md) as its first slice and this job as its second, cites
[the IDE's plan](../plans/ide-host.md) slices 0 and 3 as its prerequisites and restates neither, and
proposes both names — **assistant-a-tool-describes-its-arguments** and
**assistant-a-check-names-its-language**. `idea-tool-arguments-say-nothing` closes at that merge.

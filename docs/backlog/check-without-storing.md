<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A template cannot be checked without saving it, and the three callers ask a different way

| | |
| --- | --- |
| Difficulty | determined-mountain-goat |
| Utility | high |
| Status | planned |
| Blocked by | — |
| Plan | [one-check](../plans/one-check.md) |
| Area | Sdk, Runtime, Core (Assistant), Web (pages, editor, assistant), tools, docs |
| Location | `src/uk.osric.asp.Sdk/IPageCheck.cs` (new), `src/uk.osric.asp.Sdk/PageCheckResult.cs` (new), `src/uk.osric.asp.Sdk/PageDiagnostic.cs`, `src/uk.osric.asp.Sdk/IPageCapabilities.cs`, `src/uk.osric.asp.Runtime/IPageCompiler.cs`, `src/uk.osric.asp.Runtime/JavaScriptValidator.cs`, `src/uk.osric.asp.Web/Pages/PageCheckScope.cs` (new), `src/uk.osric.asp.Web/Pages/PageImageBuilder.cs`, `src/uk.osric.asp.Web/Assistant/Tools/ScriptTools.cs`, `src/uk.osric.asp.Web/Assistant/Tools/PageTools.cs`, `src/uk.osric.asp.Web/Assistant/Tools/ToolRegistry.cs`, `src/uk.osric.asp.Core/Assistant/ToolArguments.cs`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `tools/vscode-asp`, `docs/pages.md`, `docs/assistant.md` |
| Spike | [Teaching VS Code the template language](../spikes/vscode-template-language.md) |

## Idea

A page's diagnostics exist only as the answer to a save (`pages-a-page-may-save-itself`). To find out
whether a template compiles, the author has to store it: one new revision, one new head, a recompile
of every page that names it as a library, and a stale form in any other window that had it open.
"Check my work" is "add a revision to the history", and the history is the one thing the store keeps
forever.

The gap is not the editor's, and it is not one gap. Three callers want the same answer and each has a
different half of it:

- **A page** cannot ask at all. Nothing in the contract answers *would this compile*; the only way to
  ask is to save.
- **The assistant** can ask about **JavaScript**, and only through two tools split by where the text
  came from — `check_script` reads a stored page by path, `check_script_text` parses supplied text, and
  they share one implementation and differ only in `path` against `content`
  (`assistant-script-checks`). The question it asks most — does this page compile — still has one
  answer, and that answer is a save.
- **VS Code** has `make check-pages`, which compiles the seeds and prints their diagnostics after the
  file is written. Highlighting works; nothing arrives while the author types.

The compiler is fast enough that this is a protocol gap rather than a performance one: generation is
hundredths of a millisecond and a warm compile is 30–70 ms (`Pages:Compile:Slots` is 1 and
`Pages:Compile:TimeoutSeconds` is 5). Asking twice costs less than storing once.

One capability answers all three, with the language named by the caller. That is the whole of this
item; the shape below was settled with the owner on 2026-11-19, after the page, assistant and VS Code
items were folded into it.

## The capability

One member on a read-only Sdk capability, reached the way every capability is reached:

```csharp
// Sdk — offered only when the render has a signed-in visitor
public interface IPageCheck {
    Task<PageCheckResult> CheckAsync(string language, string source);
}

public sealed record PageCheckResult(bool Valid, IReadOnlyList<PageDiagnostic> Diagnostics);
```

- **The source is the subject, and nothing else is.** No key, no read of a stored page, no write: no
  revision, head, image, closure fingerprint, seed record or dependent invalidation. A template that
  has never been saved is checked exactly as one that has, and a caller that wants to check a saved
  page reads it (`IPageCatalog.ReadAsync`) and passes the text.
- **`language` is closed, and never inferred.** The values are `template` and `javascript` today;
  `css`, `html` and `svg` join later ([`nothing-checks-page-assets`](nothing-checks-page-assets.md)).
  A key's extension is a content type, not a validator choice: a `.html` page can be a template or an
  asset. The set lives in Sdk beside the diagnostic, so `/help`, the assistant's `enum` and the
  refusal all render from one declaration.
- **It writes nothing** (**pages-a-check-stores-nothing**, the name its plan proposes). That is the
  point of the exercise: history is not a scratchpad, and the dependents a save would recompile are
  untouched.
- **It names no key** — answered 2026-11-19. The closure a compile uses is decided by the source's
  `using` directives, not by a key: every library is a candidate and the compiler keeps the ones the
  usings reach. A key would only re-introduce the save's addressability rules, which are a different
  question.
- **It is a read, so it spends no action.** `PageWritesScope` never sees it, `pages-a-page-may-save-itself`
  needs no revision for it, and check-then-save is two calls rather than a batch question.
- **It needs a signed-in visitor** — answered 2026-11-19. A compile is a shared resource, and a check
  that anyone could call is a denial of service with a friendly face. The editor and the assistant
  both have a visitor; VS Code does not use the capability at all.
- **It shares the compile budget** — answered 2026-11-19. It takes a slot from the existing
  `Pages:Compile:Slots`, and its wait ends at the caller's deadline. Abandoning a check loses nothing
  because nothing is stored, unlike a save, which protects the author's edit.
- **The answer is a diagnostic that already exists.** `PageDiagnostic` carries an extent and a source
  key (`pages-a-diagnostic-names-its-source`), and `PageDiagnostics.ToSdk` is the mapping a save
  already uses. A check adds no new value, only a new question.
- **Two tiers, one answer.** `template` is generation, Roslyn and the page API policy against the
  store's libraries; `javascript` is the template-blanking skeleton and `JavaScriptValidator`. Both
  answer with `PageDiagnostic` values, so a caller never learns which ran. There is no checker registry
  yet: `n` is two, and the registry arrives with the third language
  ([`nothing-checks-page-assets`](nothing-checks-page-assets.md)), as the shell became applets when it
  had twelve commands rather than two.

The stored diagnostics are a different question, and are not this item:
[`revision-hides-its-diagnostics`](revision-hides-its-diagnostics.md) is what the *last compile* said
about a stored revision; a check is what *this text* compiles to now. Stored ones can be absent, or
stale because a library moved; a check is never either.

## What each caller gets

- **A page** reaches `ctx.Capabilities.GetRequired<IPageCheck>()`, and cannot reach it on an anonymous
  render. The editor's underline, and a save that refuses what certainly fails, are then the editor's
  own work on top of the capability. `/help` documents it in the same change
  (`product-manual-is-a-page`).
- **The assistant** gets one tool, `check { language, path?, content? }`, with exactly one of `path`
  and `content`, the same idiom `reference` uses for `type` against `query`. `javascript` is today's
  pair unchanged; `template` calls the capability. `read_page` absorbs `read_revision` in the same
  change: an optional `hash` selects a retained revision, the answer says which one it read, and
  `list_revisions` stays because a list is a different question from a read. The tool surface is at
  its soft ceiling, so the merge replaces two tools with one rather than adding a twenty-third.
- **VS Code** gets a Language Server Protocol server over the same `Runtime` tier, with its closure
  loaded from repository files rather than the store. That constraint — no store — is why the seam is
  split where it is: the pure tier and generation live below the Web adapter, so a process with no
  database can still answer with the same template-space diagnostics.

## Sketch

One plan, one branch, four slices that each ship on their own, in this order:

1. **The capability.** `IPageCheck`, `PageCheckResult` and the closed language set in Sdk; the Web
   adapter that generates, compiles and maps, sharing `Pages:Compile:Slots` and offered only to a
   signed-in render; the template language first, because it is the one that does not exist anywhere.
   Proves: a check stores nothing and the head hash is unchanged; a broken template answers its
   diagnostics; a library's error names the library; a template that was never saved answers exactly
   as a saved one does, because there is no key to miss; an anonymous render is not offered the
   capability; an abandoned check answers rather than writing; the compile budget is charged and a
   full queue is refused in the same words a save uses.
2. **`javascript`.** The second language, through `TemplateScriptSkeleton` and
   `JavaScriptValidator`; no new parser, no new diagnostics, no change to the tool yet.
3. **The assistant tool.** `check`, the `read_page`/`read_revision` merge, the closed `language`
   `enum` in the schema, `Assistant:MaxCheckDiagnostics`, the prompt's two lines becoming one and its
   `Version` moving, and [assistant](../assistant.md). [`tool-arguments-say-nothing`](tool-arguments-say-nothing.md)
   opens this slice, because both rewrite the tool definitions and so the provider's cached prefix;
   its other argument descriptions ride with it.
4. **VS Code.** The LSP server and client, on the same tier, from files. Last because it is
   developer-only and nothing else waits on it.

## The composite, flagged

Should `language: "template"` also run the embedded script checkers over the script it finds, so one
call answers "is this page good"? It is tempting — the prompt already tells the model to check the
script at both ends of a change — but it spends a compile on runs that wanted syntax only, so it is a
measurement and not a preference.

- **The question.** Does a run call `check(language: "javascript")` and `check(language: "template")`
  against the same target, adjacently? If it does, one call is a saved step and a saved wait; if not,
  the composite is compile time nobody asked for.
- **The evidence already exists.** Tool calls and their arguments are stored in the transcript beside
  their results (`SqliteConversationStore`), so the question is a query over stored runs, not a new
  telemetry stream, and this item has to add nothing for it to be answerable later. A `language` tag
  on the existing tool metric would chart it, and the set is closed, so the cardinality is fine — but
  the transcript is sufficient.
- **How it flips.** The composite runs the embedded checkers, the `checks` list grows an entry, the
  description and the prompt say so, and the prompt version moves. No result shape changes.

## Open questions

None that the capability's shape waits on. These wait where they are noted:

1. **Does `PageCheckResult` carry the discovered kind** — text, page, library or worker? Waits for
   slice 1. The recommendation is no: `Valid` and the diagnostics are the answer, and a stored page's
   kind is already on `PageEntry`.
2. **Where the closed set lives.** Waits for slice 1. The recommendation is an Sdk enum beside
   `PageDiagnostic`, because the manual, the tool schema and the refusal all render from it, and a
   second spelling would drift.
3. **The code a deadline-abandoned check reports.** Waits for the plan; the save path's `PAGE003` is
   the shape, and a check may want a code of its own so a dashboard can tell the two apart.
4. **`Assistant:MaxCheckDiagnostics`.** Waits for the tool slice; a broken page can answer with a
   thousand Roslyn diagnostics, and a cap is a policy value rather than an invariant.
5. **The composite.** Waits for the measurement above; its flip condition is written there.
6. **VS Code's completion** — and so the Workspaces reference and the reverse template map. Waits for
   that slice. The recommendation is diagnostics only first: completion is the expensive half, and a
   squiggle is what the spike proved.
7. **Whether the server is a `tools/` project or something the Web app hosts.** Waits for that slice.
   The recommendation is `tools/`: the site must not host an editor's server, and the extension is
   already there.

## Not in scope

- **The CSS, HTML and SVG checkers.** Values of `language` with no checker are refused by name until
  [`nothing-checks-page-assets`](nothing-checks-page-assets.md) lands; this item ships two.
- **Running the page.** A check compiles; it does not render, and a page that throws at render time is
  a runtime error with its own answer (`pages-a-warning-never-stops-a-page` is about the compile).
- **Checking a fragment.** `content` is a complete template, for the reason the script check already
  records: a fragment can be invalid alone even when the edit that produced it is correct.
- **A repository-wide check.** `make check-pages` already compiles the seeds; a page that checks every
  page in the store is a bulk compile, and it is the batch shape of [`one-write-per-render`](one-write-per-render.md),
  not this capability.
- **The batch write.** [`one-write-per-render`](one-write-per-render.md) borrows this compile and
  follows it; nothing here changes a save.
- **Stored diagnostics.** [`revision-hides-its-diagnostics`](revision-hides-its-diagnostics.md) reads
  what the last compile said; this is the fresh answer.
- **An LSP inside the site's own editor.** That is [WHIM-01](WHIMS.md): it shares the compiler with the
  VS Code slice and nothing else.

**Origin:** three items that grew separately — the page side (owner's comparison of the site's IDE
plan with the job list of 2026-11-15), the assistant side (its script tools and the tool ceiling), and
the VS Code side (the template-language spike). The owner folded them on 2026-11-19, having settled
the shared shape first: one capability, no key, signed-in only, sharing the compile budget, storing
nothing.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# One check, three callers

| | |
| --- | --- |
| Status | Proposed 2026-11-19 — waits for the owner. Nothing below is built |
| Size | Large as a programme, small as a step: one Sdk capability with two types and one closed set, a Web adapter over the compiler, the assistant's tool surface rewritten once, and an editor for the repository over the same tier. Four slices, each its own branch and each stopping for the owner |
| Area | Sdk, Runtime, Core (Assistant), Web (pages, editor, assistant), tools, docs |
| Spike | [Teaching VS Code the template language](../spikes/vscode-template-language.md) — the half that has not landed |
| Backlog | [`check-without-storing`](../backlog/check-without-storing.md) is this step; [`tool-arguments-say-nothing`](../backlog/tool-arguments-say-nothing.md) opens its third slice; [`nothing-checks-page-assets`](../backlog/nothing-checks-page-assets.md) and [`revision-hides-its-diagnostics`](../backlog/revision-hides-its-diagnostics.md) are its neighbours and neither is this plan |
| Decisions | New names proposed: **pages-a-check-stores-nothing**, **pages-a-check-names-its-language** and **pages-a-check-needs-a-visitor**. Relies on `pages-a-diagnostic-names-its-source`, `pages-a-page-may-save-itself`, `pages-reading-the-store-is-public`, `pages-the-store-is-read-through-one-seam`, `pages-a-warning-never-stops-a-page`, `code-policy-values-are-configuration`, `code-n-is-small`, `assistant-script-checks`, `assistant-a-tool-is-one-type`, `assistant-tool-results-are-cut-not-refused` and `product-manual-is-a-page` |

## The short version

One capability answers "would this source check", for a source the caller holds, in the language the
caller names. It stores nothing. A page, the assistant and VS Code each reach it, and none of them
grows a compile of its own.

```csharp
// Sdk — offered only when the render has a signed-in visitor
public interface IPageCheck {
    Task<PageCheckResult> CheckAsync(string language, string source);
}

public sealed record PageCheckResult(bool Valid, IReadOnlyList<PageDiagnostic> Diagnostics);
```

- **The source is the subject.** No key, no read of a stored page, no write. A caller that wants to
  check a saved page reads it through `IPageCatalog.ReadAsync` and passes the text.
- **The language is closed and named.** `template` and `javascript` now; `css`, `html` and `svg` when
  [`nothing-checks-page-assets`](../backlog/nothing-checks-page-assets.md) lands. Never inferred from a
  key's extension: a `.html` page is a template or an asset depending on whether it holds code.
- **Two tiers, one answer.** `template` is generation, Roslyn and the page API policy against the
  store's libraries. `javascript` is the template-blanking skeleton and `JavaScriptValidator`. Both
  answer in `PageDiagnostic` values, so a caller never learns which ran.
- **A read that spends a slot, never an action.** `PageWritesScope` never sees it, and
  `pages-a-page-may-save-itself` needs no revision for it. It takes a slot from
  `Pages:Compile:Slots` and its wait ends at the caller's deadline, because a check has no author's
  edit to protect.
- **Signed-in only.** A compile is a shared resource; a check anyone could call is a denial of
  service with a friendly face.

That shape was settled with the owner on 2026-11-19, question by question, before this plan was
written (`product-steps-wait-for-the-owner`). The record keeps it in one item,
[`check-without-storing`](../backlog/check-without-storing.md), which the owner made by folding the
page, assistant and VS Code items together.

## What exists today

Every claim below was read off `trunk` on 2026-11-19.

- **The compile is a save.** `PageSaver` compiles through `PageImageBuilder`, which generates, takes a
  compile slot and stores the emitted assembly and the diagnostics in the revision row. A failure is
  stored too, so the broken page serves its diagnostics without compiling again.
- **The closure is the source's, not the key's.** `PageImageBuilder.Candidates` offers every library
  and `IPageCompiler.Compile` keeps the ones the page's `using` directives reach. Two keys given the
  same text produce the same answer.
- **The diagnostic is already the right value.** `Core.Compilation.PageDiagnostic` carries a position
  and an extent, and the key of the library when the problem is not in the page itself;
  `PageDiagnostics.ToSdk` maps it to the Sdk value a page may name, ordered for display.
  `pages-a-diagnostic-names-its-source` is recorded.
- **The script check already exists, twice over.** `TemplateScriptSkeleton.Create` blanks template
  constructs and keeps line numbers; `JavaScriptValidator.Validate` parses the skeleton as a module.
  `CheckScriptTool` and `CheckScriptTextTool` share `CheckTemplate` and differ only in `path` against
  `content`.
- **The compile budget exists.** `PageBudget` holds `Pages:Compile:Slots` and
  `Pages:Compile:TimeoutSeconds`; a save waits, records `PAGE003` when the wait times out, and stores
  `PAGE004` when the compile itself does.
- **The capability pattern exists.** `IPageCapabilities` hands out per-render capabilities;
  `RenderBoundCapability` carries the abandonment rule; `IPageCatalog` is the read-only precedent,
  public by default and bounded by policy values.
- **The tool surface is at its ceiling and says so.** `ToolSpecsTests` pins the definitions; the
  registry is twenty-two types; a replacement is wanted rather than an addition.
- **The LSP half is a spike, not code.** `tools/vscode-asp` is the declarative extension and
  `make check-pages` the problem matcher; the live half is the item this plan carries as slice 4.

## The capability in detail

**Where it lives.** `IPageCheck` and `PageCheckResult` in `Sdk` beside `PageDiagnostic`, with the
closed set as an Sdk enum so one declaration feeds `/help`, the assistant's `enum` and the refusal.
The Web adapter (`PageCheckScope`) is a `RenderBoundCapability` registered with `IPageCapabilities`
only when `ctx.User` is signed in; it takes the runtime's compiler, the store's library heads and the
compile budget, exactly as `PageImageBuilder` does for a save.

**What a check does.** `template` follows the save's front half: generate the source, generate each
library in the shape a library is compiled in, take a compile slot, call
`IPageCompiler.Compile`/`CompileWorker`, and answer the mapped diagnostics — then release the slot and
return, storing nothing. `javascript` builds the skeleton and parses it, reporting the skeleton's own
diagnostics when the template cannot be blanked, as the tool does today.

**What a check does not do.** It does not load an image, run a handler, render, write a revision,
move a head, change a closure fingerprint, invalidate a dependent, spend the render's action, or offer
itself to an anonymous render.

**What it is charged to.** `Pages:Compile:Slots` and `Pages:Compile:TimeoutSeconds`, unchanged. No new
policy value: the check's cost is the save's cost, which is the honest description of it. A check that
waits past the caller's deadline answers with a diagnostic rather than by writing, and gets a code of
its own so a dashboard can tell an abandoned check from an abandoned save.

**What it is not.** It is not the stored diagnostic. A stored one describes the last compile of a
revision and can be absent or stale; [`revision-hides-its-diagnostics`](../backlog/revision-hides-its-diagnostics.md)
is the read of that, and this capability is the fresh answer.

## The slices

Each is one job, one branch, one pass through the gate and one stop for the owner. The order is by
dependency: the capability is what every later slice calls.

### 1. The capability and `template`

`IPageCheck`, `PageCheckResult` and the closed set in Sdk; `PageCheckScope` in Web; the template
tier through `TemplateSourceGenerator` and `IPageCompiler`; signed-in registration; the shared slot
and the deadline. `/help` gains the capability in the same change (`product-manual-is-a-page`), and
[pages](../pages.md) gains the paragraph.

Proves: a check of an unsaved template answers its diagnostics and leaves the head hash and the
revision count unchanged; a broken template answers an error; a library's error names the library; a
warning does not stop the answer being valid; a template that does not parse answers the tokenizer's
diagnostics; a template that was never saved answers exactly as a saved one does, because there is no key to miss; an
anonymous render is not offered the capability; a full compile queue answers rather than waiting for
ever; an abandoned check answers rather than writing.

### 2. `javascript`

The second value, routed to `TemplateScriptSkeleton` and `JavaScriptValidator`. No new parser, no new
diagnostic shape, no tool change: the capability gains the language the assistant already had.

Proves: the same text answers the same diagnostics as `check_script_text` does today; a template with
an unterminated block answers the skeleton's diagnostic rather than the parser's; a `.js` asset page
is a clean pass through the skeleton.

### 3. The assistant tool

`check { language, path?, content? }` with exactly one of `path` and `content`, replacing
`check_script` and `check_script_text`; `read_page` absorbs `read_revision` with an optional `hash`;
the closed set emitted as a schema `enum`; `Assistant:MaxCheckDiagnostics` as a required policy value
with its test for the shipped value, an override and a refusal; the prompt's two lines becoming one
and its `Version` moving; [assistant](../assistant.md) following.
[`tool-arguments-say-nothing`](../backlog/tool-arguments-say-nothing.md) opens this slice, because both
rewrite the tool definitions and so the provider's cached prefix; its other argument descriptions ride
with it.

Proves: one tool answers what two did, with the source named by the argument not by a suffix; the
language `enum` renders from the one declaration; a path and a content check of the same text agree; a
shell file is refused in every mode; `read_revision`'s hash reads a retained revision and says which;
the diagnostic list is cut rather than refused at the cap; the prompt version moves and the tool count
does not grow.

### 4. VS Code

A .NET console server speaking LSP over stdio, and a plain-JavaScript client in
`tools/vscode-asp`, over the same pure tier: generation, the compile and the diagnostic mapping, with
its closure loaded from the repository's seed files rather than the store. Diagnostics first;
completion is the expensive half and is not this slice unless the owner says otherwise.

Proves: a document's diagnostics arrive on `didChange` and are positioned in template space; a version
is never published out of order; the server answers the same diagnostics `make check-pages` does for
the same file; a library named by a seed is found in the repository and its error names its file.

## Tests

- **One suite per layer**, beside the thing it changes: `Web.Tests` for the adapter (the cancellation,
  the sign-in gate, the budget and the mapping), `Runtime.Tests` for anything the compile tier grows,
  and `Web.IntegrationTests` for a stored page that calls the capability end to end.
- **One assistant test per tool change**: `PageAuthorPrompt` is the assistant's only manual, and its
  `Version` bump is the signal that the prompt changed.
- **Nothing in a browser test** that a unit test can prove. The editor's underline is page work and is
  measured by whatever page the assistant builds on top.
- **The LSP suite proves the protocol, not Roslyn**: the position mapping and the document/version
  bookkeeping, with the transport thin enough to drive over pipes.

## Open questions

None blocks this plan; each names where it waits, and the recommendation the plan is written against.

1. **Does `PageCheckResult` carry the discovered kind** — text, page, library or worker? Waits for
   slice 1, and the recommendation is no: `Valid` and the diagnostics are the answer, and a stored
   page's kind is already on `PageEntry`.
2. **The code an abandoned check answers with.** Waits for slice 1; `PAGE003` is the shape, and a
   distinct code is recommended so an operator can tell a check from a save.
3. **`Assistant:MaxCheckDiagnostics`'s shipped value.** Waits for slice 3 — it is a policy value, so
   the plan fixes its name and the test, and `appsettings.json` carries the number.
4. **The composite** — whether `template` also runs the embedded script checkers. Waits for the
   measurement in the item: the transcript already records tool calls and their arguments, so it is a
   query, and the flip condition is written there.
5. **VS Code's completion** and so the Workspaces reference and the reverse template map. Waits for
   slice 4; diagnostics only first is recommended, because completion is the expensive half.
6. **Whether the server is a `tools/` project or something the Web app hosts.** Waits for slice 4; the
   recommendation is `tools/`, because the site must not host an editor's server.

## Record changes

- The merged item [`check-without-storing`](../backlog/check-without-storing.md) already carries the
  three callers and the settled shape; it takes this plan and becomes `planned`.
- [`tool-arguments-say-nothing`](../backlog/tool-arguments-say-nothing.md) takes this plan and stays
  `blocked` on the item whose tool it describes.
- [`ide-host`](ide-host.md) has lost slice 3 and says where it went; [the shell plan](assistant-shell-tool.md)
  no longer claims the check's name and consumes its tool instead.
- At each merge the names this plan intends are written into [the record](../decisions.md) with their
  "Recorded in" lines, and the items close or narrow to what is left.

## Close-out

The names, and the rule each states:

- **pages-a-check-stores-nothing** — a check compiles and answers; it writes no revision, head, image,
  closure fingerprint or seed record.
- **pages-a-check-names-its-language** — the caller names the language from a closed set; a key's
  extension is a content type and never a validator.
- **pages-a-check-needs-a-visitor** — the capability is offered only to a signed-in render, because a
  compile is a shared resource and a check spends one.

Each is recorded with the plan and the slice that took it, in backticks from the merge onwards. The
item's needs are above; nothing else in the record moves for this step.

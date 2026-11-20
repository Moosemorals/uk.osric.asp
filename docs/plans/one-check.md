<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# One check, three callers

| | |
| --- | --- |
| Status | Revised 2026-11-20 — the owner answered the six questions and said to build it. Slices 1–2 are merged and the three names are recorded; slices 3–4 are not started |
| Size | Large as a programme, small as a step: one Sdk capability with two types and one closed set, a Web adapter over the compiler, the assistant's tool surface rewritten once, and an editor for the repository over the same tier. Four slices, each its own branch and each stopping for the owner |
| Area | Sdk, Runtime, Core (Assistant), Web (pages, editor, assistant), tools, docs |
| Spike | [Teaching VS Code the template language](../spikes/vscode-template-language.md) — the half that has not landed |
| Backlog | [`check-without-storing`](../backlog/check-without-storing.md) is this step; `tool-arguments-say-nothing` opened its third slice; [`nothing-checks-page-assets`](../backlog/nothing-checks-page-assets.md) and [`revision-hides-its-diagnostics`](../backlog/revision-hides-its-diagnostics.md) are its neighbours and neither is this plan |
| Decisions | `pages-a-check-stores-nothing`, `pages-a-check-names-its-language` and `pages-a-check-needs-a-visitor`, recorded with slice 1. Relies on `pages-a-diagnostic-names-its-source`, `pages-a-page-may-save-itself`, `pages-reading-the-store-is-public`, `pages-the-store-is-read-through-one-seam`, `pages-a-warning-never-stops-a-page`, `code-policy-values-are-configuration`, `code-n-is-small`, `assistant-script-checks`, `assistant-a-tool-is-one-type`, `assistant-tool-results-are-cut-not-refused` and `product-manual-is-a-page` |

## The short version

One capability answers "would this source check", for a source the caller holds, in the language the
caller names. It stores nothing. A page, the assistant and VS Code each reach it, and none of them
grows a compile of its own.

```csharp
// Sdk — offered only when the render has a signed-in visitor
public interface IPageCheck {
    Task<PageCheckResult> CheckAsync(string language, string source);
}

public enum PageKind { Text, Page, Library, Worker } // Sdk's own, mapped from Core's

public sealed record PageCheckResult(
    PageKind? Kind, // what generation discovered; null for javascript
    bool Valid,
    IReadOnlyList<PageDiagnostic> Diagnostics);
```

- **The source is the subject.** No key, no read of a stored page, no write. A caller that wants to
  check a saved page reads it through `IPageCatalog.ReadAsync` and passes the text.
- **The answer says what the source is.** `Kind` is generation's verdict — text, page, library or
  worker — so a caller that expected a page and got a library learns it in the same answer rather
  than by a second read. A `javascript` check answers null, because a script has no page kind.
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
itself to an anonymous render. It does not run the script checker over a template: a caller that wants
a script checked hands us the script.

**What it is charged to.** `Pages:Compile:Slots` and `Pages:Compile:TimeoutSeconds`, unchanged. No new
policy value: the check's cost is the save's cost, which is the honest description of it. A check that
waits past the caller's deadline answers `PAGE010` rather than writing — a code of its own, so a
dashboard can tell an abandoned check from an abandoned save (`PAGE003`) or a save's compile timeout
(`PAGE004`). `PAGE005` is retired and never reused, so `PAGE010` is the next free code.

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
ever; an abandoned check answers `PAGE010` rather than writing; a source that is a library answers
`Kind = Library`, and a source with no code answers `Text`.

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
and its `Version` moving; [assistant](../assistant.md) following. The answer is
`{ valid, checks: [{ language, kind, valid, diagnostics }] }`, so the kind travels with the check that
discovered it.
`tool-arguments-say-nothing` opened this slice, because both
rewrite the tool definitions and so the provider's cached prefix; its other argument descriptions ride
with it.

Proves: one tool answers what two did, with the source named by the argument not by a suffix; the
language `enum` renders from the one declaration; a path and a content check of the same text agree; a
shell file is refused in every mode; `read_revision`'s hash reads a retained revision and says which;
the diagnostic list is cut rather than refused at the cap; the prompt version moves and the tool count
does not grow.

**The cap is high because Roslyn cannot stop early.** `Compilation.GetDiagnostics(CancellationToken)`
is the only compile entry point, and it returns the set when the compile ends;
`CSharpCompilationOptions` exposes no error limit, so the token — the caller's deadline — is the only
early stop. A lower cap would hide diagnostics without saving work, so the shipped
`Assistant:MaxCheckDiagnostics` is 100, `csc`'s own default error limit, and
`Assistant:MaxToolResultChars` may still cut the assembled answer sooner.

### 4. VS Code

A .NET console server speaking LSP over stdio, a `tools/asp-language-server` project of its own beside
`tools/vscode-asp`, and a plain-JavaScript client in the extension, over the same pure tier:
generation, the compile and the diagnostic mapping, with its closure loaded from the repository's seed
files rather than the store. The owner has no preference on completion, so the recommendation stands:
diagnostics only, and completion is a later slice.

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

## The owner's answers

The owner answered these on 2026-11-20; this revision carries the answers and waits for a yes.

1. **The kind is in the answer.** `PageCheckResult` carries `PageKind?`: a caller that expected a page
   and got a library learns it in the same answer rather than by a second read of `PageEntry`. A
   `javascript` check answers null, because a script has no page kind, and the assistant's check list
   carries the kind beside the language.
2. **A distinct code for an abandoned check.** `PAGE010`, so a dashboard can tell it from the save
   path's `PAGE003` and `PAGE004`. `PAGE005` is retired and never reused.
3. **The cap is high.** Roslyn's public API cannot stop at a diagnostic count:
   `Compilation.GetDiagnostics(CancellationToken)` returns when the compile ends,
   `CSharpCompilationOptions` has no error limit, and the token is the only early stop. A low cap
   would hide information without saving work, so `Assistant:MaxCheckDiagnostics` ships as 100 —
   `csc`'s own default error limit — and the result budget may cut sooner.
4. **No composite.** `template` does not run the script checker. A caller that wants a script checked
   hands us a script; the template check answers the template. The flagged measurement is closed.
5. **Completion: no preference.** The recommendation stands: diagnostics only first; completion, the
   Workspaces reference and the reverse map are a later slice.
6. **The server is a `tools/` project.** `tools/asp-language-server`, beside `tools/vscode-asp`,
   never the Web app: the site must not host an editor's server.

## Record changes

- The merged item [`check-without-storing`](../backlog/check-without-storing.md) already carries the
  three callers and the settled shape; it takes this plan and becomes `planned`.
- `tool-arguments-say-nothing` closed with slice 3: the schema's `enum` and the argument
  descriptions landed with the tool that needed them.
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

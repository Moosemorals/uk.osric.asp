<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# One tool, one type

| | |
| --- | --- |
| Status | Implemented 2026-11-11; phases 1–3 landed, and the name-taking `PageWrites.Written` was removed rather than kept, because nothing in the solution is packaged |
| Size | Medium — a tool type and its registry, three shared helpers, fifteen tools moved into five files by family, and the tests that pin the new wiring. No tool changes what it does |
| Spike | None. The shape is the shell's, proved in DEC-226, and the tool surface is already a table with one row per tool |
| Decisions | DEC-230 (a tool is one type and the registry is the only table), recorded with this plan. DEC-228 belongs to [capability naming](capability-naming.md) and is not claimed here |
| Backlog | Nothing waits on this. [ARCH-15](../../jobs/ARCH-15.md) rewrites the same pre-approval refusal and should land first, or phase 2 absorbs it |

## The short version

The assistant's fifteen tools live in one 939-line class,
[PageToolExecutor](../../../src/uk.osric.asp.Web/Assistant/PageToolExecutor.cs). The virtual shell had the
same shape until this week's rewrite (DEC-226): a command became one type carrying its name, its
usage, its syntax and its behaviour, and the kernel did the once-only work. This plan applies the
part of that idea that fits, and says why the rest does not.

Three things are wrong with the tools today, and the shell had the first and the third of them:

1. **A tool is named in four places.** A row in `s_tools`, a case in `ExecuteAsync`'s switch, a case
   in `PageToolAsync`'s switch, and a case in `Verb()`. A fifth list lives in `Core`, where
   [`PageWrites.IsWrite`](../../../src/uk.osric.asp.Core/Assistant/PageWrites.cs:22) hard-codes the two
   tools that change a page. Add a row and forget a case and it still **compiles**: the call falls
   through to `"Unknown tool."`, and the only thing that catches it is the test written for that
   tool.
2. **Two facts about a tool are inferred from its name.** "This tool writes a page" is a pair of
   strings in `Core`; "this tool names a path worth refusing before the owner is asked" is the
   presence of an argument called `path`. Both are true statements about the tool that the tool
   should carry, the way `NeedsApproval` already is.
3. **Six tools repeat the same gate.** `Validate` → `ShadowedKey` → "a shell file has no page
   source", then a refusal whose summary carries a hand-written verb. The verb itself is written
   three times for the same tool: in the method's own refusals, in `Verb()` for the pre-approval
   refusal, and again in the summary strings.

The shell's headline win — one grammar parsed once — is **already here** and must not be traded away:
`ParametersJson` and `ToolArguments.Parse` both derive from `s_tools`, so an argument cannot be
described to the model and then refused at execution.

## Why the applet shape only partly transfers

An applet carries three things the kernel cannot infer — its usage line, its description and its
notes — and `help` renders them, which is why the help text cannot drift. A tool already carries its
description, its argument schema and its approval flag in a table row, and nothing renders them back
except the row itself. That half of the shell's motivation is absent, and this plan does not pretend
otherwise.

What remains is **wiring and co-location**: a tool's behaviour should sit with the rest of what is
true about it, and the compiler should be the thing that notices a tool is unreachable. The shell's
own migration ended up grouping applets by family (`HeadAndTailApplets.cs` is two commands in one
file), so "one type per tool" does not mean "one file per tool" here either.

Where the analogy breaks hardest is dependencies. All ten applets take one `ShellContext`. The tools
do not: four memory tools need `IMemoryStore` from a per-call scope, `fetch` needs
`IAssistantFetcher`, `diff_page` needs `DiffRenderer`, and the rest need `PageEditor`. Any split has
to answer that question deliberately rather than by imitation, and phase 3 answers it with a
per-call services record — `ShellContext`'s counterpart — rather than by registering fifteen scoped
types and resolving each one on demand.

## What is wrong today, measured

| Where | Lines | What it is |
| --- | --- | --- |
| `PageToolExecutor.cs` | 939 | the whole tool surface |
| `s_tools` + `Definitions` | 50–150 | the table, and the model's list derived from it |
| `ExecuteAsync` | 154–182 | name switch one: five families, everything else to the page switch |
| `PageToolAsync` | 183–199 | name switch two: nine page tools, and a `default` that answers "Unknown tool." |
| `RefuseBeforeApproval` | 210–230 | looks for an argument literally called `path` |
| the fifteen tool methods | 231–708 | 478 lines |
| the helpers | 709–939 | 231 lines: results, listing, matching, modes, line splitting, JSON readers |
| `Verb()` | 844 | the fourth place a tool is named |
| `PageWrites.IsWrite` | `Core`, 22 | the fifth, and the only one outside this assembly |
| the four suites | 119 cases | `PageToolExecutorTests`, `…MemoryTests`, `…FetchTests`, `…DiffTests` |

Two more duplications the plan should not silently leave:

- The long-listing mode string is produced in two places —
  [`Mode`](../../../src/uk.osric.asp.Web/Assistant/PageToolExecutor.cs:842) for `list_pages` and
  `read_page`, and [`PageStoreView`](../../../src/uk.osric.asp.Web/Assistant/Shell/PageStoreView.cs:128)
  for `ls -l` and `stat` — and described in a third
  ([`PageAuthorPrompt`](../../../src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs:341)). One pure
  function in `Core` should answer it.
- There are two line-splitting rules and they are **not** the same:
  [`ReadLines`](../../../src/uk.osric.asp.Web/Assistant/PageToolExecutor.cs:893) adds a final empty line
  after a trailing break, because `read_page`/`edit_page` line numbers name physical lines of the
  stored template as the editor shows them, while
  [`ShellText.Split`](../../../src/uk.osric.asp.Web/Assistant/Shell/ShellText.cs:17) does not, because
  POSIX counts a terminated line as one line. Unifying them would be a bug in whichever caller lost.
  The plan renames `ReadLines` to say which rule it is and leaves both.

## Goals

1. A tool is declared once. Its name, description, argument schema, approval flag, refusal verb,
   whether it changes a page and the code that runs it are one type, and `Definitions` is derived
   from the same list the dispatcher uses.
2. Adding a tool that is registered but unreachable is a compile error or a failing contract test,
   not a runtime `"Unknown tool."`.
3. No fact about a tool is inferred from its name outside the tool.
4. The gate every path-naming tool repeats is one helper, and its refusal wording comes from the
   tool.
5. Behaviour is unchanged: no tool name, description, argument, approval flag, result envelope or
   refusal is altered. The 119 existing cases are the proof and are not rewritten to prove it.
6. `PageToolExecutor` becomes orchestration — registry, scope, call, bound — and nothing else.

## Non-goals

- **Moving the tools to `Core`.** They read the page store, the clock and the network. They are
  shell. `Core` stays the pure half.
- **Changing the model's tool list.** The order of `Definitions` is part of the request body and
  therefore of the provider's cached prefix. The registry keeps declaration order; it does not sort
  the way `ShellRegistry` does for `help`.
- **Touching a description or a prompt.** [PageAuthorPrompt](../../../src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs)
  names tools in prose; prose stays prose. A test that scanned the prompt for tool words would match
  "shell" and "fetch" as English, so it is not written.
- **`/help`.** It is the page author's manual. Tools are not a page capability, so nothing about this
  plan reaches it.
- **Reflection-based discovery.** Fifteen types written by one author do not need a scanner, and a
  scanner is what would make the list's order unstable.
- **A generic argument projection** (`ToolArguments.Parse<T>`). The table plus three readers is
  smaller than a source generator, and the C# policy forbids the reflection a binder would want.
- **Changing the approval flow.** A gated call parses its arguments twice today — once to refuse,
  once to run — and "Later" records the fix. Reordering approval around a parse is a change to the
  run, not to the tool, so it is not this plan's.
- **ARCH-15's question** — whether `Assistant:ProtectedPaths` should exist at all. This plan moves
  where the refusal lives, not what it permits.

## The shape

### Phase 1 — the tool is its own row (the whole of the wiring bug)

`ToolSpec` gains the behaviour and the two facts, and the two switches and `Verb()` go.

```csharp
internal sealed record ToolSpec(
    string Name,
    string Description,
    bool NeedsApproval,
    IReadOnlyList<ToolArgumentSchema> Arguments,
    string Verb,                                              // "Edit" — the one word its refusals use
    Func<ToolCall, ToolServices, CancellationToken, ValueTask<ToolExecutionResult>> Run,
    bool Writes = false,                                      // a successful call changes a stored page
    string? PathArgument = null);                             // the argument the policy refuses before a prompt
```

The table becomes the only list: `ExecuteAsync` is a `Find` plus `spec.Run`, and `PageToolAsync`
disappears. `Definitions` is derived exactly as it is now, in the same order. The tool methods move
out of the class unchanged — either as private members the rows point at, or as the static methods of
a `PageTools`/`MemoryTools`/`ShellTool`/`FetchTool`/`DiffTool` holder — whichever keeps the diff
readable. This phase alone removes the failure mode that motivated the plan, and it is shippable on
its own.

Two constraints that decide the details:

- `Definitions` is read statically by [AssistantEndpoint](../../../src/uk.osric.asp.Web/Assistant/AssistantEndpoint.cs:184)
  and twice by [RunProcessor](../../../src/uk.osric.asp.Web/Assistant/RunProcessor.cs:142), so the
  registry has to answer without an instance — which is why `Run` is a `Func` over a per-call
  `ToolServices` rather than a method on the singleton, and why the tools can stay static.
- `ToolServices` is built once per call from the singletons the executor already holds plus the one
  scope it already opens. It carries `ToolContext`, `PagePathPolicy`, `AssistantOptions`,
  `AssistantMetrics` and the four services a tool may need — `Pages` and `Memories` from the scope,
  `Fetcher` and `Diff` from the singletons.

`PageWrites` follows. Its public signature is in `PublicAPI.Shipped.txt`, so either:

- the name-taking `Written` stays for the public API and the call site stops using it, using the
  registry's `Writes` fact and a new overload `Written(bool changesPages, string? resultJson)`; or
- the name-taking overload is removed, with a `*REMOVED*` line in `PublicAPI.Unshipped.txt`.

Prefer the first: `Core` is a published surface with a shipped-file discipline, the change buys
nothing a caller can see, and an overload documents the migration in place.

### Phase 2 — the three helpers

1. **`ToolValues`** — `String`, `Integer`, `Boolean`, plus `Required…` variants that answer the same
   refusal `ToolArguments.Parse` would have. It wraps the validated dictionary, so no tool touches
   `JsonElement`.
2. **`ToolResults`** — `Success`, `Failure` and `Bounded`, bound to `AssistantOptions.MaxToolResultChars`.
   The envelope is written in one place, which it already is; the win is that it is no longer a
   private member a stray tool can bypass.
3. **`ToolPaths`** — the gate, in the one order it must happen:

   ```csharp
   (PagePath Path, ToolExecutionResult? Refusal) ResolveRead(ToolSpec spec, ToolValues values, string what);
   (PagePath Path, ToolExecutionResult? Refusal) ResolveWrite(ToolSpec spec, ToolValues values, string what);
   ToolExecutionResult? RequirePageSource(ToolSpec spec, PagePath path, string instead);
   ```

   `ResolveWrite` is `Validate(write: true)` and nothing more — the policy already refuses a reserved
   or protected key for a write, and the tools inherit that without repeating it. `ResolveRead` is
   `Validate(write: false)` followed by the shadow check, because a read is permitted beneath a
   reserved route by the policy and must not be by the tool. `RequirePageSource` is the
   "a shell file has no page source" refusal shared by `diff_page` and `check_script`, which today
   word it twice, and `list_pages` asks the same question through its `kind` column.

   `spec.Verb` supplies the summary's verb, so `"Edit refused"`, `"Diff refused"` and
   `"Search refused"` stop being literals three times over.

   The mode string moves to `Core` as `PageMode.Describe(bool canWrite)` and both callers use it.

### Phase 3 — one type per tool, grouped by family

Only worth doing after phases 1 and 2 have proved the wiring and the helpers. It moves `Run` off the
spec and onto the type, and leaves everything else on the spec where the registry already reads it:

```csharp
internal abstract class AssistantTool {

    public abstract ToolSpec Spec { get; }

    public virtual ValueTask<ToolExecutionResult> RunAsync(
        ToolCall call, ToolValues values, ToolServices services, CancellationToken cancellationToken) =>
        throw new NotSupportedException($"{Spec.Name} is not implemented.");
}
```

`ToolServices` is the counterpart of `ShellContext`: the caller (`ToolContext`), the policy, the
options, the metrics, and the four services a tool may need — `Pages`, `Memories`, `Fetcher` and
`Diff` — the first two from the scope `ExecuteAsync` already opens, the last two singletons. `ToolRegistry` holds the tools in
declaration order and derives `Definitions`; `PageToolExecutor` keeps the public seam
`ExecuteAsync(name, argumentsJson, context, token)` and `RefuseBeforeApproval(name, argumentsJson)`
and becomes well under two hundred lines.

Fifteen tools in five files: `PageTools` (`list_pages`, `read_page`, `edit_page`, `write_page`,
`delete_page`, `search_pages`), `DiffTool`, `ScriptTools` (`check_script`, `check_script_text`),
`MemoryTools` (four), and `ShellTool` and `FetchTool`. The long provider-visible descriptions move
beside the code they describe, which is the one real gain over the table.

## What this plan decides

> **DEC-230.** An assistant tool is one type: it carries its name, its description, its
> argument schema, its approval flag, the verb its refusals use, whether a successful call changes a
> stored page, the argument the path policy refuses before a prompt, and the code that runs it. The
> registry is the only list — the model's `Definitions`, the dispatcher, the approval gate and the
> transcript-level "this call changed a page" question all read it — and it keeps declaration order,
> because that order is part of the request body and of the provider's cached prefix. A tool's
> behaviour is unreachable only if it is unregistered, which is a compile error.

That sentence is what phases 1–3 make true. It is recorded with this plan rather than before it, as
DEC-09 asks.

## What each phase proves

Phase 1:

- `PageToolExecutorTests`, `…MemoryTests`, `…FetchTests` and `…DiffTests` pass **unmodified**. That
  is the whole behavioural proof, and any test that has to change is a signal the refactor moved
  something it should not have.
- New registry contract test: every tool's `Name` is unique; `Definitions` names equal the registry's
  names in declaration order; every `NeedsApproval` tool appears in the model's list; every tool that
  declares `Writes` is approval-gated (a write the owner never sees is a defect, not a preference).
- New contract test: `PathArgument` names an argument the tool actually declares, and is only set on
  an approval-gated tool.
- `PageWritesTests` gains the fact-based overload; the transcript case still reports the same keys.

Phase 2:

- Unit tests for `ToolPaths` covering the order of the gate: an unaddressable path is refused before
  a shadowed key is looked for; a read beneath a reserved route is refused by the shadow check; a
  read of a shell file is refused by `RequirePageSource` with the tool's own verb.
- A test that the refusal summary uses `spec.Verb`, so `Verb` cannot become a decoration.
- The mode function is proved once, and `ls -l`'s existing tests keep passing without edit.

Phase 3:

- The registry has fifteen entries, none abstract, and every one overrides `RunAsync` — asserted by
  reflection, so the default that throws cannot hide an unfinished tool.
- `PageToolExecutor` has no `switch` on a tool name: a source-level test is too fragile, so this is
  a review criterion, recorded in the plan rather than asserted.

Every phase ends with `make test`, `make format-check` and `make build`, and each is a commit of its
own so a bisect can tell the wiring change from the helper change.

## Risks

| Risk | Why it matters | Handling |
| --- | --- | --- |
| The model's tool order changes | It is part of the request body, and DEC-200's context row measures the request as sent; a reordering invalidates the provider's cache key for every run | The registry keeps declaration order and a contract test pins it. No `OrderBy` |
| A refusal's wording drifts | The model was taught the wording; the tests assert fragments like "changed since read_page" | Every wording string moves unchanged; the suites are the guard |
| `PageWrites` is shipped API | A signature change is a public surface change with an Unshipped entry | Prefer the overload; keep the name-taking member |
| The two line-splitting rules get "unified" | `read_page` line numbers and POSIX line counts are different questions | Renamed, documented in the plan and in the tool's own doc comment; no shared helper |
| ARCH-15 collides | It rewrites `AssistantOptions.ProtectedPaths`, `PagePathPolicy.IsProtected` and the refusal this plan moves into `ToolPaths` | Land first, or absorb: the refusal becomes `ToolPaths.ResolveWrite`, and ARCH-15's deletion becomes smaller, not larger |
| Ceremony for a six-line tool | `shell` is twelve lines and `check_script_text` is two | Phase 3 groups by family, and a tool's `RunAsync` may stay a one-liner. If a group file reads worse than the methods did, stop at phase 2 |
| A phase is abandoned half-done | The seam is public and the tests are shared | Phases 1 and 2 leave the class working with no half-migrated tools; phase 3 moves one family at a time, each its own commit |

## Migration

1. Phase 1 in one commit: `ToolSpec` grows, `s_tools` becomes the only list, both switches and
   `Verb()` go, `PageWrites` gains its fact. Nothing else moves.
2. Phase 2 in one commit per helper, each converting the call sites it touches.
3. Phase 3 one family per commit — memory, fetch, shell, script, diff, page — so the page family,
   which is where the line-number and store logic lives, lands last and alone.
4. When the work lands, record DEC-230 in [decisions.md](../../decisions.md) with the plan archived, and
   add one paragraph to [assistant.md](../../assistant.md) where it currently says the tools "reach the
   page store through `PageEditor`" — the paragraph gains the registry sentence, not a rewrite.

Nothing in `Pages/Seeds` changes, no configuration key moves, no migration is needed, and no stored
page is recompiled.

## Later, not now

- **One parse per call.** With the tool owning its approval refusal, `RunProcessor` could parse the
  arguments before the prompt and hand the validated values to the run afterwards, so an approved
  call parses its JSON once rather than twice. That is a change to the run's shape, and it should
  wait until the tools have stopped moving.
- **A tool's arguments as a type.** If a family ever grows a fifth memory tool, a `MemoryTool`
  base with the shared `id`/`content`/`version` shape is worth more than a generic binder.
- **Registry-level limits.** `MatchLimit` and `ListingLimit` are constants in the executor and
  defaults in `ShellOutput`. If they ever become configuration, they belong in one place; today they
  are not worth the move.

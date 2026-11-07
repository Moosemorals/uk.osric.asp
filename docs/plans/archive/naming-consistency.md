<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Naming consistency: type names and their members

What started as a question about the `Assistant` prefix turned into a rule, and the rule turned out
to be worth more than the prefix. This plan applies it to the verticals that reward it, records where
it does not apply, and carries the same idea down to members so that a reader six months cold can
predict a name instead of grepping for it.

**Status:** implemented on 2026-11-06, in eight commits — see [DEC-122](../../decisions.md#build-test-and-house-style).

## The short version

1. Strip `Assistant` from every type in the two verticals, except three where the bare noun is unsafe
   — and the validator that follows one of them.
2. Where stripping leaves a word too thin to identify anything — `Event`, `Usage`, `Paths` — substitute
   a **domain** qualifier that says what the thing is, not where it lives: `RunEvent`, `TokenUsage`,
   `PagePathPolicy`.
3. `Run` keeps its name. `Turn` and `Stanza` are rejected below, with evidence.
4. The rule applies at **any position** in a type name, not just the start. Across the rest of the
   solution that catches eighteen more names and changes one.
5. Seven types outside the assistant earn a rename too, and the rest of the solution is recorded as
   deliberately not applying — subsystem namespaces strip, domain namespaces do not.
6. Members get the same treatment: one boolean convention, one endpoint entry point, one result shape.
7. The database and the Prometheus history are both disposable, so migrations are squashed into one
   clean initial and metric names are fixed at the same time, rather than hand-editing a snapshot or
   living with names kept only for their history.
8. Nothing else on the wire moves — table names, config keys, event names, routes, asset ids and
   Prometheus labels stay exactly as they are. Blast radius: 901 identifier occurrences in `.cs` and
   68 in `.md`, across 68 source files and 5 documents.
9. What the dashboard *shows* is a separate job, [OPS-06](../../jobs/OPS-06.md): half the instruments the
   site records never reach a panel.

No behaviour change is intended. Every phase ends with a green build and its suites passing; if a
phase cannot be made green, it is reverted rather than debugged forward.

## The rule

Every name gets three tests, in order.

**1. Does the bare noun collide with something already in scope?**

| Type | Collides with | Verdict |
| --- | --- | --- |
| `AssistantOptions` | the `Microsoft.Extensions.Options` namespace, at every `IOptions<>` call site — including the file that implements `IValidateOptions<AssistantOptions>` and `Program.cs` | keep the prefix |
| `AssistantMetrics` | the `System.Diagnostics.Metrics` namespace, imported by its own file, and the `uk.osric.asp.Web.Metrics` namespace, which `Program.cs` imports alongside `Assistant` | keep the prefix |
| `AssistantEndpoint` | `Microsoft.AspNetCore.Http.Endpoint`, and bare `Endpoint` says nothing about which surface it serves | keep the prefix |

These three keep `Assistant` because the prefix is doing a qualifier's work, not an echo's.

**2. Is the bare noun specific enough to identify the thing inside the vertical?**

`Run`, `Message`, `Conversation`, `ToolCall`, `ToolApproval` and `ChatRequest` are. `Event`, `Usage`
and `Paths` are not — they describe a category, not a thing. Those get a domain word:

| Current | Bare would be | Proposed | Why |
| --- | --- | --- | --- |
| `AssistantEvent` | `Event` | `RunEvent` | one journaled event **of a run**; also too close to the `event` keyword |
| `AssistantUsage` | `Usage` | `TokenUsage` | it is exactly `(PromptTokens, CompletionTokens)` |
| `AssistantPaths` | `Paths` | `PagePathPolicy` | its own summary is "the rule for which pages the assistant may read and write"; it pairs with `PageApiPolicy` |

**3. Otherwise, bare.** `AssistantRun` → `Run`, `IAssistantConversationStore` → `IConversationStore`,
`SqliteAssistantConversationStore` → `SqliteConversationStore`.

The test is about the identifier, not the concept. `AssistantProviderException` becomes
`ProviderException` because the word "provider" already says whose exception it is; "assistant" adds
nothing.

### The rule applies at any position

A name can echo its namespace at the start, in the middle or at the end. The rule is not positional —
it only asked about prefixes because that is where the assistant vertical happens to put them.

Infix and postfix echoes across the rest of the solution, with what the other two rules do about them:

| Type | Namespace | Echo | Outcome |
| --- | --- | --- | --- |
| `SqliteAssistantConversationStore`, `IAssistantConversationStore` | `Assistant` | infix | already in [the mapping](#the-mapping) |
| `PrometheusMetricsSource` | `Metrics` | infix | **`PrometheusSource`** — the one new change |
| `IMetricsSource` | `Metrics` | infix | keep: `ISource` identifies nothing (rule 2) |
| `IPageStore`, `SqlitePageStore` | `Pages` | infix | keep: `IStore` identifies nothing (rule 2) |
| `IUserStore`, `SqliteUserStore` | `Users` | infix | keep: `IStore` again |
| `IUserConsole`, `SystemUserConsole` | `Users` | infix | keep: `Console` hits `System.Console` (rule 1) |
| `StorageHealthCheck`, `AbandonedRenderHealthCheck` | `Health` | infix | keep: `XxxHealthCheck` is the framework's name for an `IHealthCheck` |
| `PageStorageSampler`, `PageStorageRefresh` | `Storage` | infix | keep: `PageSampler` and `PageRefresh` do not say what is being sampled or refreshed (rule 2) |
| `PageEditor` | `Editor` | postfix | keep: removing `Editor` leaves `Page`, which is already a type |
| `LocalUser` | `Users` | postfix | keep: `Local` alone is an adjective |
| `LowercaseText`, `MessageText` | `Text` | postfix | keep: `Lowercase` is an adjective, and `Message` collides with the assistant's (rule 1 and 2) |
| `LineChart` | `Charting` | postfix | keep: `Line` identifies nothing (rule 2) |

Eighteen names reviewed, one changed. That is the whole justification for extending the rule: it is
safe *because* rules 1 and 2 catch almost everything, and it costs one rename to find out.

### Subsystem namespaces strip; domain namespaces do not

The rule does not generalise to the rest of the solution, and the reason is worth recording rather
than rediscovering. A namespace names either **where code lives** or **what code is about**.

| Namespace | Kind | `AssistantRun` / `PageHead` | Outcome |
| --- | --- | --- | --- |
| `Assistant`, `Metrics`, `Storage`, `Authentication`, `Health`, `Admin` | subsystem — an address | the type is a thing whose home is the module | strip where rules 1 and 2 allow |
| `Pages`, `Users`, `Charting`, `Text`, `Editor` | domain — the subject | the type is a **part of** the thing the namespace describes | keep |
| `Runtime`, `Sdk`, `Core.Compilation`, `Web.Html`, `Web.Http` | flat or unrelated | no echo exists | untouched |

`AssistantRun` is a run, and the assistant is where it lives. `PageHead` is the head **of** a page —
the page is what it describes. Stripping the first moves an address into the namespace, where it
already is; stripping the second destroys meaning, so rule 2 puts it back. `Core.Charting` is the
clean no-op: strip six types to `Axis`, `Series`, `Tick`, `Size`, `Point` and `Scale`, and rule 2
returns `Chart` to all six.

Applied solution-wide, 91 of 186 non-test types are echoes; 50 of those are the assistant. Outside
it, 41 reviews yield 7 changes and 34 stops. Inside it, 47 of 50 change. The rule is worth its cost
in one vertical and not in the others.

Two further limits, recorded so they are not mistaken for oversights:

- **Production namespaces only.** `Web.Tests` would turn `TestClock`, `TestSession` and `TestHttp`
  into `Clock`, `Session` and `Http`. There `Test` is a marker, not a module.
- **Rule 2 is not a general naming policy.** Applied to every thin noun rather than only to repair a
  strip, it would change `Token`, `Matrix`, `NotFound`, `Redirect` and more. This plan renames
  echoes; it does not rename everything vague.

## Terms this plan depends on

The decision below turns on the difference between words the docs use loosely. In the code they are
distinct:

| Term | What it is | Where it lives |
| --- | --- | --- |
| Conversation | one chat thread — an owner, a title, an approval mode and a conversation-wide message counter | `AssistantConversations` |
| Message | one transcript row, `user`, `assistant` or `tool`, numbered by `Sequence` which is unique **per conversation**, and tagged with the `RunId` that produced it | `AssistantMessages` |
| Run | one background execution of the processor over a conversation, started by a user message: queued, running, then completed, failed, cancelled or interrupted. At most one active run per conversation | `AssistantRuns` |
| Step | one provider round trip **inside** a run, bounded by `Assistant:MaxSteps`. A step ends with an assistant message; if that message carries tool calls, the run executes them and takes another step | no row; `StepCount`, the `step` field on every event |
| Tool call | a call the assistant emits in a message, executed once and stored as a `tool` message | `ToolCallsJson`, `ToolCallId` |
| Tool approval | the durable owner decision a flagged tool call waits for | `AssistantToolApprovals` |

So: a conversation has many runs, a run has many steps and many tool calls, and the transcript is the
sequence of messages they produce. The journal of events is not the transcript — it is the live view
and it is dropped after `Assistant:JournalTtlMinutes`; the transcript is the record.

**"Turn" is none of them.** It appears only in prose, in two different senses: a "multi-turn
conversation" (an exchange) and "the complete transcript sent to the provider each turn" (a step, in
[ai-page-assistant.md](ai-page-assistant.md#the-run-lifecycle)). One word, two units, and the
smaller of the two already has a name in the code. That is how a reader ends up asking what the
relationship is — and it is why `Turn` is not available as a type name below.

The rest of the vocabulary — page, key, path, head, revision, template, image, runtime, slot, budget,
staleness — is bigger than this plan and is recorded as [IDEA-013](../../jobs/IDEA-013.md).

## What `Run` should be called

**Keep `Run`.** The alternatives are concept changes dressed as renames.

**`Turn` is taken, and taken by something smaller.** [The terms above](#terms-this-plan-depends-on)
settle it: a turn in the prose is one provider round trip, which the code calls a *step*, and a run is
**many** steps. Renaming the execution to `Turn` would leave `StepCount` counting the steps inside a
turn — a contradiction rather than a rename.

**`Stanza` has no ground here.** Zero occurrences anywhere in the solution, and nothing in the
codebase's vocabulary — which is chat/provider/step — points at it. It is terminology from a different
family of chat systems, so it costs every future reader a word to learn and buys nothing `Run` does
not already carry.

**Bare `Run` is not actually thin.** All 58 uses sit in files that already import
`uk.osric.asp.Web.Assistant`, so the vertical is in front of the reader; there is no other `Run` type
in the solution; the word matches the prose, the metric names and the `AssistantRuns` table. The only
defensible alternative is `Execution`, which reads as the act rather than the stored record, and the
`Run*` family (`RunEvent`, `RunStream`, `QueuedRun`) would become `Execution*` for no gain.

If the stored row needs to read differently from the live runtime objects, `RunRecord` for the entity
alone is available — but that is a rename for its own sake, and it is not proposed here.

## Is `Tool` free? Yes

`Tool` is never a bare type name anywhere in the solution. The only near misses are `ToolCallBuilder`
(private, nested in the stream accumulator) and `ToolCallingClient` (private, nested in a test), plus
`StubResponse.Tool(...)`, a test factory method. There is no `Tool`, `ToolCall` or `ToolDefinition`
elsewhere, and no provider SDK is referenced (the host talks raw HTTP), so no dependency can shadow
them either.

So `ToolCall`, `ToolDefinition`, `ToolArgumentType`, `ToolApproval` and `ToolExecutionResult` stand on
their own. A reader who meets `ToolCall` knows it belongs to the conversation with the assistant,
because this is the only place in the product where tools exist.

## The mapping

### `Web/Assistant` — 32 types, 25 renamed

| Now | After |
| --- | --- |
| `AssistantConversation` | `Conversation` ⚑ |
| `AssistantMessage` | `Message` ⚑ |
| `AssistantRun` | `Run` ⚑ |
| `AssistantToolApproval` | `ToolApproval` ⚑ |
| `AssistantConversationListItem` | `ConversationListItem` |
| `AssistantMessageInput` | `MessageInput` |
| `AssistantEvent` | `RunEvent` |
| `AssistantRunStartStatus` | `RunStartStatus` |
| `AssistantRunStartResult` | `RunStartResult` |
| `AssistantRunStream` | `RunStream` |
| `AssistantRunQueue` | `RunQueue` |
| `AssistantQueuedRun` | `QueuedRun` |
| `AssistantRunRegistry` | `RunRegistry` |
| `AssistantRunProcessor` | `RunProcessor` |
| `AssistantRunWorker` | `RunWorker` |
| `AssistantRunJournalCleanupService` | `RunJournalCleanupService` |
| `AssistantToolApprovalCoordinator` | `ToolApprovalCoordinator` |
| `AssistantToolExecutionResult` | `ToolExecutionResult` |
| `AssistantPageToolExecutor` | `PageToolExecutor` |
| `AssistantApprovalResolutionResult` | `ApprovalResolutionResult` |
| `AssistantProviderException` | `ProviderException` |
| `IAssistantConversationStore` | `IConversationStore` |
| `SqliteAssistantConversationStore` | `SqliteConversationStore` |
| `AssistantPaths` | `PagePathPolicy` |
| `AssistantPathResult` | `PathValidationResult` |
| `AssistantOptions` | **unchanged** |
| `AssistantOptionsValidator` | **unchanged** |
| `AssistantMetrics` | **unchanged** |
| `AssistantEndpoint` | **unchanged** |

⚑ = EF-mapped entity; see [disposable data](#disposable-data).

`PageAuthorPrompt`, `IChatCompletionClient` and `OpenAiChatCompletionClient` are already right and do
not move.

`AssistantPageToolExecutor` → `PageToolExecutor`, not bare `ToolExecutor`, because it is the executor
of the *page* tools specifically and a second executor is the obvious next thing to add.

### `Core/Assistant` — 26 types, 22 renamed

| Now | After |
| --- | --- |
| `AssistantChatRequest` | `ChatRequest` |
| `AssistantChatResponse` | `ChatResponse` |
| `AssistantChatMessage` | `ChatMessage` |
| `AssistantChatStreamEvent` | `ChatStreamEvent` |
| `AssistantContentDelta` | `ContentDelta` |
| `AssistantReasoningDelta` | `ReasoningDelta` |
| `AssistantToolCallDelta` | `ToolCallDelta` |
| `AssistantUsageEvent` | `UsageEvent` |
| `AssistantFinished` | `Finished` |
| `AssistantChatStreamAccumulator` | `ChatStreamAccumulator` |
| `AssistantContextTrimmer` | `ContextTrimmer` |
| `AssistantContextTrimResult` | `ContextTrimResult` |
| `AssistantUsage` | `TokenUsage` |
| `AssistantApprovalMode` | `ApprovalMode` |
| `AssistantToolCall` | `ToolCall` |
| `AssistantToolDefinition` | `ToolDefinition` |
| `AssistantToolArguments` | `ToolArguments` |
| `AssistantToolArgumentType` | `ToolArgumentType` |
| `AssistantToolArgumentSchema` | `ToolArgumentSchema` |
| `AssistantToolArgumentsResult` | `ToolArgumentsResult` |
| `AssistantPageRangeEditor` | `PageRangeEditor` |
| `AssistantPageRangeEditResult` | `PageRangeEditResult` |
| `ConversationTitle` | **unchanged** |
| `VirtualShellParser` | **unchanged** |
| `VirtualShellCommand` | **unchanged** |
| `VirtualShellParseResult` | **unchanged** |

`Core` types are `public` within the assembly, so this is a breaking change to the assembly's surface
— but `Core`'s only consumers are `Runtime`, `Web` and the test projects, and all of them are in this
repository.

The existing test names are already half-converted and show the intent: `ChatStreamAccumulatorTests`
and `ConversationTitleTests` drop the prefix while `AssistantContextTrimmerTests` keeps it. This
change finishes that job in one direction.

### Outside the assistant — 7 types

| Now | After | Why |
| --- | --- | --- |
| `AdminRestoreEndpoint` | `RestoreEndpoint` | `Admin` is an address; `Restore` is the route |
| `AdminSeedsEndpoint` | `SeedsEndpoint` | same |
| `HealthReportWriter` | `ReportWriter` | the only report writer in the solution |
| `MetricsDashboard` | `Dashboard` | `Metrics` is an address; nothing else is a dashboard |
| `MetricsQueryResult` | `QueryResult` | sits with `Matrix` and `Unavailable`, which already drop it |
| `PrometheusMetricsSource` | `PrometheusSource` | the infix echo; `Prometheus` is the qualifier that matters |
| `PageRevisionHash` | `RevisionHash` | `Pages` is a domain, but this is a pure function of a revision, not a part of a page |

`IMetricsSource` keeps its name, so the pair becomes `IMetricsSource` and `PrometheusSource` — the
interface needs the qualifier, the implementation supplies a better one.

That is every change the rule finds outside the assistant. `AdminSeedsEndpoint` and
`AdminRestoreEndpoint` are both dereferenced in `Program.cs`, and are the only ones that appear in
more than one file.

## Metrics

Metric names are a flat namespace, exactly like table names and config keys, so `page.` and
`assistant.` stay. What the disposable Prometheus history buys is not de-qualification — it is the
freedom to fix the names that were only being kept because a rename would split the series.

Three things are wrong, and only three:

| Now | After | Why |
| --- | --- | --- |
| meter `uk.osric.asp` | `uk.osric.asp.Pages` | the assistant meter is already `uk.osric.asp.Assistant`; one of the two is qualified and one is not, and nothing says why |
| `page.handler.load.duration` | `page.load.duration` | the only two instruments with a third segment; the family is `page.<verb>.duration` |
| `page.handler.invoke.duration` | `page.invoke.duration` | same |

The meter name is only the `otel_scope_name` label, and both call sites in `Program.cs` use
`MeterName`, so that rename is contained. Neither renamed instrument appears in a panel today —
which is exactly [OPS-06's](../../jobs/OPS-06.md) complaint — so no PromQL moves with them.

The two instrument renames are still the sharpest edge in the plan, because `AddView` matches
instrument names **as strings**, in the views block of
[Program.cs](../../../src/uk.osric.asp.Web/Program.cs). A missed one silently falls back to the generic
OpenTelemetry bucket bounds instead of failing, and every histogram here supplies explicit bounds for
a reason. That is the one place in this plan where a mistake produces a chart that looks fine and is
wrong.

Deliberately unchanged:

- **`assistant.run.steps` beside `assistant.run.tool_calls`.** One is a word, the other is two joined
  by an underscore inside a dotted name. `step_count` and `tool_call_count` would avoid it, but
  Prometheus already appends `_count` to a histogram, so the series would read
  `assistant_run_step_count_count`. Left as it is.
- **`page.cache.lookups` beside `page.cache.staleness`.** The `result` and `reason` labels carry the
  distinction; renaming the subjects buys nothing.
- **Units.** `s`, `By`, `{step}`, `{call}`, `{token}` are UCUM and already uniform. The exporter turns
  them into the `_seconds` and `_bytes` suffixes the dashboard queries.

## Members

Names inside a type repeat their type and its namespace for the same reasons type names repeat a
namespace: it reads as a stutter and it tells the reader nothing they did not already know from the
surrounding line. Two patterns were checked and are already right, and are recorded so they are not
"fixed" later:

- **Parameters name the role, not the type.** `PageBudget(RenderOptions render, CompileOptions compile)`,
  `RunStream(string runId, AssistantOptions options, TimeProvider clock)`. This is the pattern to keep.
- **`Try*` for a bool answer.** `TryStart`, `TryReserve`, `TryEnqueue`, `TryEnqueueReserved`,
  `TryGetPath`, `TryGetRevisionHash`, `TryGetCommand`, `TryGetAccount` — nine declarations across seven
  names, all consistent.

### Booleans — one convention

The codebase has seven ways to name a boolean. `Is*` dominates with 25 properties; `Try*` handles
bool-returning methods; `Can*`, `Has*` and `Was*` appear once or twice; seven members are a bare noun
(`Success`); and two put the auxiliary in the middle.

| Now | After | Cost |
| --- | --- | --- |
| `AssistantEndpoint.PostIsValid` | `IsPostValid` | private, seven call sites in one file |
| `AssistantConversation.TitleIsGenerated` | `HasGeneratedTitle` | a column, a raw SQL string and a regenerated migration — see [disposable data](#disposable-data) |
| `AssistantContextTrimResult.WasTrimmed` | `IsTrimmed` | free; `TrimmingNote` reads better beside it |
| `Success` on seven result records | `IsSuccess` | free; makes the whole result family uniform |

The seventh way, `TitleIsGenerated`, is the one that reads worst at a call site —
`conversation.TitleIsGenerated` buries the verb. `PostIsValid` is the same shape and is free to fix.

**Options properties keep their names.** `Enabled`, `IncludeUsage`, `RequireApiKey` and
`AllowInsecureHttp` are not members so much as they are the other half of a config key; renaming them
renames `Assistant:Enabled`. Same reasoning as everything else in
[what must not change](#what-must-not-change).

### Result records — one shape

Six result types are already the same shape: a value, a `string? Error`, and a computed boolean that
means `Error is null`. `IsSuccess` makes them read identically:

`PathValidationResult`, `PageRangeEditResult`, `ToolArgumentsResult`, `TemplateGenerationResult`,
`TokenizeResult`, `CompileResult`.

`ToolExecutionResult` carries the same `Success` name as a positional parameter rather than as a
computed property, and renames with them.

No structural change is proposed — they are already consistent. The only thing worth writing down is
that the shape is deliberate, so the next result type copies it rather than inventing a fourth member.

### Endpoint entry points — one name per arity

Four endpoint classes expose their entry point and use three different names for it:

| Now | After |
| --- | --- |
| `ErrorEndpoint.HandleAsync` | unchanged |
| `StatusEndpoint.HandleAsync` | unchanged |
| `PageEndpoint.Handle` | `HandleAsync` |
| `EditorEndpoint.Handler` | `HandleAsync` |

Both renames are `async Task`, so they also break the `Async` suffix rule. They are the only two
members in the solution that break it, apart from `Program.Main`, which the framework names, and
`Program.AssistantDisabled`, which is left alone below.

The rule a cold reader can learn in one sentence: **an endpoint class with one route exposes
`HandleAsync`; one with several exposes a verb per route** (`LoginEndpoint.GetAsync`/`PostAsync`,
`AdminSeedsEndpoint.ListAsync`/`RestoreAsync`, `AssistantEndpoint.ListAsync`/`MessagesAsync`, …).

`Program.AssistantDisabled` is left alone: it is a local route handler in `Program.cs`, not a member
of an endpoint class, and the name says what the route does.

## What must not change

These strings are flattened out of their namespace, so the qualifier is load-bearing there. The
rename touches **identifiers only**.

| Thing | Example | Why it stays |
| --- | --- | --- |
| Table names | `AssistantConversations`, `AssistantRuns`, `AssistantMessages`, `AssistantToolApprovals` | the database is a flat namespace too; a table needs its qualifier for the same reason a config key does |
| Config keys | `Assistant:Enabled`, `Assistant:MaxSteps` | deployment config and `appsettings.json` |
| Prometheus labels | `page_path`, `outcome`, `reason`, `result`, `le` | the dashboard's `sum by (...)` clauses and the exporter's dot-to-underscore mapping |
| SSE event names | `run.completed`, `message.started`, `tool.finished` | the browser and its tests |
| Routes | `/assistant/{id:long}/…` | links and bookmarks |
| Asset and DOM ids | `assistant.js`, `assistant-*`, `approval-mode` | CSS, JS and the browser suite |
| Hash framing | `PageRevisionHash.FormatVersion` = `"page-revision-v1"` | the type moves, the hash format must not |
| JSON field names | the tool result envelope, `delta.reasoning_content` | the provider and the transcript |

Word-boundary matching is safe against the mechanical hazards here: `\bAssistantRun\b` does not match
inside `AssistantRuns`, and `\bAssistantConversation\b` does not match inside
`AssistantConversationListItem` or `AssistantConversations`. Verify with `rg -w` before and after
rather than trusting that.

## Disposable data

Both stores are disposable: alpha, one user, one deployment, nothing of value in either volume. That
removes the hard part of renaming an EF entity — the snapshot — instead of working around it, and it
lets the metric names change without splitting every series in two.

Renaming an entity changes its CLR type name inside
[StorageDbContextModelSnapshot.cs](../../../src/uk.osric.asp.Web/Storage/Migrations/StorageDbContextModelSnapshot.cs)
and EF compares entities *by name*, so a rename can look like one entity removed plus another added
and scaffold `DropTable`/`CreateTable` against the same table. The [page SDK plan](page-sdk-split.md#additional-changes-agreed-2026-11-03)
avoided exactly this for `StoredFile` and called it "a no-op migration and a hand-edited snapshot for
no functional gain".

With no data to keep, the answer is neither: **squash the history and generate one clean initial
migration**.

1. Delete everything under `Storage/Migrations/`.
2. Rename the four entities and the eight generic arguments in `StorageDbContext` — four `Entity<T>`
   calls and four `HasOne<T>` calls. The `ToTable(...)` strings do not move.
3. Rename `TitleIsGenerated` to `HasGeneratedTitle`, and the same string in the raw `UPDATE
   "AssistantConversations"` statement in
   [SqliteConversationStore.cs](../../../src/uk.osric.asp.Web/Assistant/SqliteConversationStore.cs).
4. `make migration NAME=InitialCreate`, which produces one migration whose `Up` is a plain
   `CreateTable` for every table and whose snapshot matches the model exactly.
5. `make test-integration`, which applies migrations to a fresh database and exercises the real host.

The cost is the loss of the migration history — `RenameAssistantTables` stops being replayable, and
the prose in [ai-page-assistant.md](ai-page-assistant.md) that describes it becomes the only record of
that rename. With a database that has never held anything worth keeping, that is the cheaper of the
two costs.

The deployment consequence needs a line in
[deploy/README.md](../../../deploy/README.md#storage-and-migrations), and it now covers two volumes:

- **`uk.osric.asp-storage`** has to be removed once, because its `__EFMigrationsHistory` does not know
  about `InitialCreate` and startup will find tables it did not create. It also holds the owner
  account, so the sequence is: stop the app, remove the volume, deploy, then `make remote-user-add`.
- **`prometheus-data`** has to be removed too. Retention is 30 days
  ([prometheus.container](../../../deploy/prometheus.container)), so without a reset the old and new
  names would sit side by side for a month and any panel that sums without filtering by name would
  count both. Removing the volume is one `podman volume rm` and no data that matters.

One deployment, two deletes, one account.

## Phases

One commit per phase, in this order. Each ends with `make build` and the suites named, and none starts
until the previous one is green.

| # | Phase | Types | Check |
| --- | --- | --- | --- |
| 1 | `Core/Assistant` | 22 | `make build`, `make test-core test-web test-integration` |
| 2 | `Web/Assistant`, excluding the four entities | 21 | `make build`, `make test-web test-integration` |
| 3 | The four entities, `HasGeneratedTitle`, and the squashed migration | 4 | `make migration NAME=InitialCreate`; `make test-integration` |
| 4 | The seven outside the assistant | 7 | `make build`, `make test` |
| 5 | Rename the test suites named after a renamed type | 9 | `make test` |
| 6 | Metrics: one meter, two instruments, and their `AddView` keys | — | `make test`, then read the `/metrics` scrape by eye |
| 7 | Members: the remaining booleans, `IsSuccess`, the two endpoint entry points | — | `make test` |
| 8 | Docs, DEC-122, the deployment note, and OPS-06 | — | `make check` |

Phase 2 excludes the entities so that the migration work happens in exactly one place, after the rest
of the vertical has settled.

Phase 4 includes `PrometheusMetricsSource` → `PrometheusSource` with its test file, which is the one
change the infix rule contributes.

Phase 5 renames only suites that name a type: `AssistantToolArgumentsTests` → `ToolArgumentsTests`,
`AssistantPageRangeEditorTests`, `AssistantContextTrimmerTests`, `AssistantPathsTests` →
`PagePathPolicyTests`, `AssistantRunRegistryTests`, `AssistantToolApprovalCoordinatorTests`,
`AssistantConversationStoreTests`, `AssistantPageToolExecutorTests`, `AssistantRunProcessorTests`, and
the `ShellAssistantConversationStore` fake. Feature-named suites — `AssistantChatTests`,
`AssistantFlowTests`, `AssistantApprovalTests`, `AssistantCutOffTests`, `AssistantDisabledTests`,
`AssistantOptionsTests`, `AssistantMetricsTests` — describe scenarios, not types, and keep their names.

Phase 6 is the only phase whose mistakes are silent, so it is the only one with a manual check: the
scrape has to be read, not just its tests passed. `PageMetricsTests`, `AssistantMetricsTests` and
`MetricsEndpointTests` pin the instrument names, and `MetricsDashboardTests` pins one query, so the
compiler will not help with the rest.

Phase 3 owns `HasGeneratedTitle` because it owns the schema; phase 7 does the rest of the members.

Phase 7 is last because it touches the most files for the least mechanical certainty: the mapping is
one row per judgement, and it is easier to argue about once the type names have stopped moving.

## Documentation

- [architecture.md](../../architecture.md) — the "where does code go?" table names
  `SqliteAssistantConversationStore`; it becomes `SqliteConversationStore`.
- [decisions.md](../../decisions.md) — DEC-122, recording the rule, the subsystem/domain line, and the
  old→new mapping, so that someone grepping for `AssistantRun` after the change finds the answer:

  > **DEC-122** — A type name does not repeat its namespace, at the start, in the middle or at the
  > end. A namespace names either where code lives or what code is about: a subsystem (`Assistant`,
  > `Metrics`, `Storage`, `Authentication`, `Health`, `Admin`) can lose its name; a domain (`Pages`,
  > `Users`, `Charting`, `Text`, `Editor`) cannot, because there the qualifier is the subject. Where
  > the bare noun collides with something in scope or identifies nothing, the qualifier stays or is
  > replaced by a domain word (`RunEvent`, `TokenUsage`, `PagePathPolicy`) — never by the vertical.
  > Members follow the same idea: `Is*`/`Has*` for booleans, `Async` on every `Task`, `HandleAsync`
  > on a single-route endpoint, and no member repeating its type. See
  > [naming consistency](naming-consistency.md).

  (That row lives in `decisions.md`; the link is written for that file, and here it points back at this
  plan.)
- [ai-page-assistant.md](ai-page-assistant.md) and [assistant-browser-tests.md](assistant-browser-tests.md)
  and [BUG-17](../../jobs/BUG-17.md) name types too. These describe work that is already finished, so
  they get one line under their status saying the type names predate DEC-122, rather than a rewrite
  that would misrepresent what was implemented.
- [deploy/README.md](../../../deploy/README.md#storage-and-migrations) — the once-only removal of both
  volumes and the account recreation after the squash.
- [jobs/README.md](../../jobs/README.md) — the OPS-06 and IDEA-013 rows, and the provenance sentence that
  says where they came from.

## Risks

| Risk | Mitigation |
| --- | --- |
| The squashed migration and an existing volume disagree | Remove `uk.osric.asp-storage` once and recreate the owner account; `make test-integration` proves a fresh database works |
| A renamed instrument keeps its old `AddView` key | The two renames and their views are one commit; phase 6 reads the scrape rather than trusting the tests, because the fallback is silent |
| Old and new metric names coexist for the retention window | Remove `prometheus-data` with the app volume, once, before the new version is deployed |
| A dashboard query keeps a stale metric name after the rename | `MetricsDashboardTests` pins one query; the rest are read against the scrape in phase 6, and [OPS-06](../../jobs/OPS-06.md) proposes the test that would catch it properly |
| `HasGeneratedTitle` drifts from the raw SQL copy of the column name | One phase, one commit, and `AssistantConversationStoreTests` covers the title-upgrade path |
| `IsSuccess` touches five `public` Core types | It is a property rename in one commit; the compiler finds every call site |
| `Message` and `ChatMessage` read alike where both appear | Only `RunProcessor` uses both, and only in `ToChatMessage(Message message)`, where the mapping is the point |
| Findability: `AssistantRun` stops matching anything in code | DEC-122 carries the mapping; `AssistantRuns` (the table) still greps |
| Blame and history churn across 68 source files and 5 documents | One commit per phase, so `git log --follow` needs at most one hop per file |
| A rename leaks into a wire string | The "what must not change" table, plus `rg -w` before and after, plus `make test-integration` and `make test-browser` |
| The member phase becomes a style debate | Its table is closed: four boolean renames and two endpoint names. Everything else is recorded as deliberately unchanged |

## Rejected alternatives

- **Keeping the prefix, for consistency.** Consistency with what? `Users`, `Storage`, `Compilation`,
  `Charting` and `Text` do not prefix their types; `Assistant` is the outlier. The current state is
  worse than either convention because it is 85% applied — `IChatCompletionClient` and
  `PageAuthorPrompt` sit beside `AssistantToolApprovalCoordinator`.
- **Stripping every prefix mechanically.** It produces `Event`, `Usage` and `Paths`, which identify
  nothing. Three of the strips need a replacement qualifier, not a deletion.
- **Applying the rule to every namespace.** Measured, not argued: 41 reviews outside the assistant
  produce 7 changes and 34 stops. `Core.Charting` is a complete no-op. The subsystem/domain line is
  the reason, and it is recorded rather than left to be rediscovered.
- **`Turn` for `Run`.** Rejected: "turn" already means one provider round trip in the plan of record,
  and a run spans many of them.
- **`Stanza` for `Run`.** Rejected: no occurrence, no precedent, new vocabulary for no gain.
- **Renaming `AssistantOptions` to `Options` and `AssistantMetrics` to `Metrics`.** Rejected: both
  bare nouns contend with a namespace that the same files already import.
- **Hand-editing the snapshot instead of squashing.** It was the plan while the database mattered. A
  disposable database makes one clean `InitialCreate` simpler and safer than eleven hand-edited
  strings.
- **Keeping the metric names for their history.** Every rename splits a series, so a name that is
  already recorded is usually cheaper to keep. Here the history is worth less than the consistency:
  one volume, thirty days of a site nobody is watching yet, and no alert rule that would be given a
  stale threshold by the change.
- **Renaming `assistant.run.steps` to `assistant.run.step_count`.** Rejected: Prometheus appends
  `_count` to a histogram, so the series would become `assistant_run_step_count_count`.
- **Renaming options properties to match the boolean rule.** Rejected: `Enabled` and `IncludeUsage`
  are config keys wearing a property's clothes.
- **Tidying the private helpers** (`Describe`, `Selected`, `NoStore`, `AssistantDisabled`). They are
  local to one file, name themselves from their call sites, and no reader has to guess them across
  types.

## Open questions

1. `PagePathPolicy` is a guess at what `AssistantPaths` is *for*. Its alternatives are
   `ProtectedPaths` (what it holds) and `PathPolicy` (thinner). Worth settling when phase 2 lands and
   the call sites are visible.
2. `AssistantFinished` → `Finished` is the mechanical strip, but `Finished` is the only one of the
   `ChatStreamEvent` subtypes that is not a noun. `ChatFinished` would be tidier at the cost of one
   extra word. Not worth blocking on.
3. `HasGeneratedTitle` is the only member rename that reaches stored data. If the migration squash is
   deferred for any reason, drop this row from phase 6 rather than hand-editing a column.

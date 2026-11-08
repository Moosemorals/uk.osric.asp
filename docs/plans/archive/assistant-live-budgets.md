<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Live assistant budgets

Status: **implemented 2026-11-08**. Written 2026-11-08 as a design and an implementation plan; this
is now the record of what was built. It began as a feasibility question — whether the conversation
page's budget list could move while a run is running — and settles on pushing the figures over the
run journal that the page already listens to. The decisions are DEC-159, DEC-160 and DEC-161: site
navigation has since claimed 148–158, so this plan takes the numbers after it rather than the three
it first reserved.

## The short version

- **One snapshot, measured once.** A new pure `BudgetSnapshot` in `Core.Assistant` measures every
  figure the budget list shows, using the trimmer's own counting rule. The page renders it; the run
  loop publishes it. A live figure and a reloaded one cannot disagree, because there is only one
  calculation.
- **Push, do not poll.** The run loop publishes `budget.updated` on the existing `RunStream` when it
  learns something — as a step starts and finishes, and around each tool call — so the update is
  journaled, replayed on reconnect and ordered with the deltas it belongs beside.
- **No new endpoint, no timer, no second mechanism.** The page already holds an SSE stream for the
  length of a run; that is the channel.
- **The rows gain names, not identifiers.** Each row is `data-budget="context"` and so on, which is
  what the script looks up. The existing `assistant-budget-message` ids are preserved by deriving
  them from the name.
- **The reload stays the last word.** `run.completed`, `run.failed`, `run.cancelled` and
  `stream.reset` still reload the page, so the live figures only have to be right, not final.

## Goals

- A reader watching a run sees steps, tool calls, arguments, results, context and output advance as
  the run spends them, rather than learning after the fact that it stopped on one (DEC-141's stated
  purpose).
- One measurement of a budget, used by the page and by the stream.
- No new dependency, no new route, no new background timer, and no change to what a run enforces.
- A budget the browser cannot measure — context — is published by the host that can.

## Non-goals

- **Per-token or per-character updates.** Output tokens move when the provider reports usage, which
  is at the end of a step; context moves when a request is built. Pretending otherwise means
  counting our own approximation and labelling it as the budget.
- **A budgets endpoint.** Polling would be a second mechanism beside an open stream, would have no
  replay, and would tear against the deltas it arrives between.
- **Making the browser measure context.** The DOM is not the transcript: tool summaries are clipped
  to 32 graphemes, a step that only called tools is never attached, and the system prompt is not in
  the page. A client-side figure would be a plausible lie.
- **A live region over the list.** Every count moving several times a run would be read aloud
  repeatedly; the numbers and the exceeded class are enough (see D7).
- **Alerting on a budget.** A run that stops on one already fails with its own code and message
  (DEC-141); this plan only shows the approach.
- **Live figures between runs.** Nothing is spent between runs, so the stored value that the reload
  shows is the correct one.

## What exists today

Checked against the code on 2026-11-08.

- **The list is rendered once**, in `WriteBudgetsAsync`
  ([AssistantEndpoint.cs](../../../src/uk.osric.asp.Web/Assistant/AssistantEndpoint.cs)), from three
  inputs: the stored `messages`, the tool calls parsed out of them by `ReadToolCalls`, and the
  counters on the `Run`. Seven rows: message, context, steps, tool calls, tool arguments, tool
  result, output.
- **Context is the untrimmed total**, measured with `ContextTrimmer.MeasureChars` over the system
  prompt plus every stored message — exactly the rule the trimmer counts by
  ([ContextTrimmer.cs](../../../src/uk.osric.asp.Core/Assistant/ContextTrimmer.cs)). It is not the
  length of the request that would be built after trimming.
- **Only the message row has ids** (`assistant-budget-message`, `assistant-budget-message-meter`),
  and only because [assistant.js](../../../src/uk.osric.asp.Web/Pages/Seeds/assistant.js) updates it
  from the textarea. The other six rows are anonymous markup.
- **The store is already live.** `RecordStepAsync` increments steps and tool calls and keeps the
  most recent non-null token counts on the `Run`
  ([SqliteConversationStore.cs](../../../src/uk.osric.asp.Web/Assistant/SqliteConversationStore.cs)),
  so a reload mid-run would show current figures. Only the page is stale.
- **The page reloads at every terminal event** — `run.completed`, `run.failed`, `run.cancelled`,
  and `stream.reset` — which is why the staleness has never been visible for longer than a run
  ([assistant.js](../../../src/uk.osric.asp.Web/Pages/Seeds/assistant.js)).
- **The stream is journaled and replayable.** `RunStream` keeps a bounded journal, `ReadEventsAsync`
  serves it from `after`, a slow subscriber is reset, and a heartbeat keeps an idle stream honest
  ([RunStream.cs](../../../src/uk.osric.asp.Web/Assistant/RunStream.cs),
  [AssistantEndpoint.cs](../../../src/uk.osric.asp.Web/Assistant/AssistantEndpoint.cs)).
- **The pieces a live figure needs are already on the wire, except usage.** `message.started` and
  `tool.started` carry `step`; `tool.finished` carries `arguments` and the stored `content`;
  `run.completed` carries the last step's `usage`. `Assistant:IncludeUsage` is on by default
  ([AssistantOptions.cs](../../../src/uk.osric.asp.Web/Assistant/AssistantOptions.cs)), so the provider
  reports usage per step — but nothing publishes it while the run is live.
- **The meter is a picture, not the reading.** `WriteBudgetAsync` clamps the meter and lets only the
  number exceed, and the meter is `aria-hidden` (DEC-141).
- **One renderer, one note.** The hint under the list already says which rows count the current (or
  most recent) run and which count the conversation, so a row that falls between steps is already
  explained in the page.

## What this touches

| Job or decision | What it already says | What this plan does |
| --- | --- | --- |
| DEC-141 | Every budget that can stop a run is shown as a used/available pair with a `<meter>`, "so a run that stops on one names it before it stops" | Completes the intent: the figure moves while the run does, so the reader watches it approach the limit |
| DEC-141 | The meter is a picture and the number beside it is the reading | Kept: the number text changes, the meter's `value` follows, and the meter stays `aria-hidden` |
| DEC-123, [BUG-17](../../jobs/BUG-17.md) | The run deadline measures the provider going quiet, and is re-armed by real events | Unchanged, and not reused: the update is published when the loop learns something, never by a timer |
| [ARCH-13](../../jobs/ARCH-13.md) | A page cannot stream its response | Unaffected — this is the assistant's host-owned stream, not a stored page's |
| [assistant.js](../../../src/uk.osric.asp.Web/Pages/Seeds/assistant.js) | Native ES module, side-effect only, ends `export {};` | Gains one listener and one small lookup table; no globals, no bundler |
| [assistant.css](../../../src/uk.osric.asp.Web/Pages/Seeds/assistant.css) | `.assistant-budget-exceeded` colours the count | Unchanged — the class is toggled, not added |

No job entry: this started as a design question rather than a defect or an idea, so it goes
straight to a plan. `docs/jobs/README.md` is not touched.

## Design

### D1 — One snapshot in `Core`, measured by the trimmer's own rule

`src/uk.osric.asp.Core/Assistant/BudgetSnapshot.cs`:

```csharp
public sealed record BudgetSnapshot(
    long ContextChars,
    int Steps,
    int ToolCalls,
    int LongestArguments,
    int LongestResult,
    int CompletionTokens) {
    public static BudgetSnapshot Measure(
        IReadOnlyList<ChatMessage> messages,
        int steps,
        int toolCalls,
        int? completionTokens);
}
```

- `ContextChars` is `ContextTrimmer.MeasureChars(messages)` — the same call, not a copy of the rule,
  so a change to `MessageLength` moves both.
- `LongestArguments` is the longest `function.arguments` string across every stored
  `ToolCallsJson`, parsed with the `JsonDocument` idiom `ReadToolCalls` already uses; 0 when the
  transcript holds no calls.
- `LongestResult` is the longest `Content` of a `role == "tool"` message; 0 when there are none.
- `Steps`, `ToolCalls` and `CompletionTokens` are carried through, because only the caller knows
  whether they come from a stored `Run` (the page) or from the loop's own counters (the stream). A
  null usage counts as zero, which is the loop's starting figure; from then on the loop keeps the
  last non-null value rather than replacing it, as `RecordStepAsync` does.
- Pure: no clock, no store, no options, no logger. The caller prepends the system prompt message,
  because `PageAuthorPrompt` is the shell's.

`ReadToolCalls` stays where it is. It answers a different question — the names to render beside a
tool result — and its output is a dictionary keyed by call id rather than a set of lengths. The two
parsers read the same stored JSON, so a test proves they agree on it rather than pretending they are
one.

### D2 — The run journal is the channel

The loop publishes `budget.updated` through `RunStream.Publish`, which already:

- assigns a sequence and an event id, so `Last-Event-ID`/`after` replay works and a reader who
  reloaded mid-run catches up on the figures rather than waiting for the next step;
- orders the update with `content.delta` and `tool.finished`, so a reader never sees a tool result
  counted before the tool that produced it;
- fails loudly if the stream is already complete, which is the guard that stops a late update from
  arriving after the run's terminal event.

Why not polling, a second endpoint, or a small fetch on an interval: each adds a mechanism beside a
connection the page already holds open, costs a store read and a full `MeasureChars` per tick, and
would interleave arbitrarily with the deltas. There is nothing to poll *for* — the loop is the only
thing that learns these numbers, and it can say so.

### D3 — What each row is, and what moves it

| Row (`data-budget`) | Limit | Moves when | Published from |
| --- | --- | --- | --- |
| `message` | `Assistant:MaxMessageChars` | the reader types | **Never** — the textarea is the value |
| `context` | `Assistant:MaxContextChars` | a request is built, so once per step | the transcript the loop has just read, measured with the system prompt prepended |
| `steps` | `Assistant:MaxSteps` | the step loop iterates | after `RecordStepAsync` |
| `tool-calls` | `Assistant:MaxToolCalls` | a tool call finishes | after each `tool.finished` |
| `tool-arguments` | `Assistant:MaxToolArgumentChars` | a call with longer arguments than any before | as `tool.started` is handled, because an approval can wait a long time and the cost is known before the wait |
| `tool-result` | `Assistant:MaxToolResultChars` | a tool result longer than any before is stored | as `tool.finished` is handled |
| `output` | `Assistant:MaxOutputTokens` | the provider reports usage at the end of a step | after `RecordStepAsync` |

Two consequences worth stating in the code:

- **The context figure is the conversation's, not the request's.** It counts the whole stored
  transcript, exactly as the page does, before trimming. A reading that dropped when turns were
  trimmed would disagree with the number the reload shows, and the trimmer's own warning already
  says when turns were dropped (`run.warning`, `context_trimmed`).
- **The output figure is the last step's.** `Run.CompletionTokens` is overwritten by each step that
  reports usage, so this row can fall when a new step starts. That is the same value the page shows
  after a reload, and the note under the list already says the row counts the current or most recent
  run.

### D4 — The loop keeps a snapshot and advances it

The loop already reads the transcript at the top of every step, which is where `MeasureChars` should
run, and already knows `step`, `toolCallCount`, each call's `arguments` and each `result.Content`.

```csharp
BudgetSnapshot budgets = BudgetSnapshot.Measure(messages, step - 1, toolCallCount, outputTokens);
```

and then, as it learns more, `budgets = budgets with { Steps = step, ToolCalls = toolCallCount,
LongestArguments = Math.Max(budgets.LongestArguments, call.Arguments.Length), ... }`, publishing
after each advance. Because the snapshot is a record, advancing it is a `with` and not a second
measurement; because it is measured from the fetched transcript, the conversation-wide maximum is
what gets published and no row ever goes backwards.

Publishing points, in order of the run:

1. as a step starts, beside `message.started` — the step count and the context measured for the
   request about to be made, so a slow answer is watched from the step it belongs to rather than
   from the one before it;
2. after `RecordStepAsync` — the stored counters and the usage that step reported, which is the only
   moment the output row can move;
3. as each `tool.started` is handled — the arguments, when they beat every call already stored,
   because an approval can wait a long time and a slow tool would otherwise hide the cost;
4. after each `tool.finished` — the longest result, when it beats every result already stored;
5. just before `run.completed` — the whole snapshot re-measured from the stored transcript with the
   stored counters, so the last figure a reader sees is exactly the one the reload will render.
   `Publish` throws once the stream is complete, so this is the last event the loop may send.

The step count is published as the step starts even though the store only counts it when the step's
response is recorded, so a reload taken mid-step can lag the live figure by that one step; the
completion figure is re-measured from the store and cannot.

The payload is keyed by the row names the markup uses, so the browser needs no translation table:

```json
{"budgets":{"context":118240,"steps":3,"tool-calls":2,"tool-arguments":412,"tool-result":1896,"output":513}}
```

`message` is absent on purpose (D6), and the property names of the record map to these keys once,
where the event is published.

Nothing is published on the failure paths: `run.failed`, `run.cancelled` and the terminal handling
all reload the page, and a figure that arrives in the same breath as a reload is wasted work. That
keeps `Publish`'s "no events after complete" invariant trivially true.

### D5 — The page renders the same snapshot

`WriteBudgetsAsync` takes a `BudgetSnapshot` instead of the messages, calls and run it takes today,
and `WriteBudgetAsync` takes the row's name rather than an optional id:

```csharp
await WriteBudgetAsync(context, "context", "Context", snapshot.ContextChars, options.MaxContextChars, "characters");
```

which writes `data-budget="context"`, `id="assistant-budget-context"` and
`id="assistant-budget-context-meter"`. Deriving the ids from the name reproduces today's message row
exactly, so the existing markup assertions and the browser test that fills the textarea keep
passing. The page computes the snapshot once, so its seven figures come from one measurement of one
transcript.

### D6 — The browser applies numbers; it does not decide them

In [assistant.js](../../../src/uk.osric.asp.Web/Pages/Seeds/assistant.js), beside the existing
`messageBudget`/`messageMeter`:

- build a `Map` once from `document.querySelectorAll('[data-budget]')`, keyed by the attribute, with
  the count span and the meter inside the row;
- on `budget.updated`, for each `[name, used]` in `data.budgets` — the keys are the `data-budget`
  values — skip an unknown name, set the count text with the `numbers` formatter that is already
  there, set `meter.value` to `Math.min(used, meter.max)`, and toggle `assistant-budget-exceeded` on
  the row by `used > meter.max` — the same two rules `WriteBudgetAsync` applies on the server;
- never touch `message`: no event carries it, so typing is never overwritten by a reconnect or a
  replayed journal.

The listener is registered with the others in `connect()`, so a replayed journal re-applies the
figures in order and the last one wins.

### D7 — What "updates" means for a reader

- **No live region.** The count is ordinary text inside the definition list; a screen reader that
  reads the section later gets the current numbers. Announcing six counts several times a run is
  noise, and the meter stays `aria-hidden` so nothing is read twice (DEC-141).
- **The exceeded state is the one thing worth noticing**, and it is the existing colour and weight
  rule, now reachable while the run is still going.
- **Formatting is unchanged**: `N0` invariant on the server, `Intl.NumberFormat('en-GB')` in the
  browser; both group thousands the same way, which is why the message row has never jumped on
  reload.
- **With JavaScript off** the list is still the load-time figure, which is what it is today; the
  `<noscript>` note already tells that reader to refresh.

## Considered and rejected

- **Poll `GET /assistant/{id}/budgets` every second or two.** The simplest thing to write, and still
  wrong for this page: a request per tick beside an open stream, no ordering against the deltas, no
  replay, and a full re-measure of the transcript per tick. It also duplicates the "which figures
  does the page show" rule in a second response shape.
- **Let the browser measure context from the transcript it is drawing.** The DOM holds clipped tool
  summaries, omits steps that only called tools, and has no system prompt, so the number would drift
  from the stored one in exactly the situations a reader cares about.
- **Count tool arguments and results in the browser from `tool.started`/`tool.finished`.** The events
  do carry what is needed, but the page's figure comes from the store — so a client count would
  need its own truncation and its own replay rules, and would disagree after a reconnect. One server
  snapshot is less code and one rule.
- **Put the list in `aria-live="polite"`.** Six rapidly changing counts are unusable read aloud.
- **htmx or an out-of-band swap.** No dependency is wanted, and the response is a stream, not a
  request/response pair.
- **Publish on a timer.** The value changes when the loop learns something; a timer would publish
  the same snapshot repeatedly and blur what "live" means (compare DEC-123's refusal to measure
  duration rather than progress).

## Components

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Core/Assistant/BudgetSnapshot.cs` | **new** — the six figures, measured from a transcript and counters |
| `src/uk.osric.asp.Web/Assistant/RunProcessor.cs` | **changed** — keep a snapshot, advance it on each step and each tool, publish `budget.updated` before `run.completed` |
| `src/uk.osric.asp.Web/Assistant/AssistantEndpoint.cs` | **changed** — `WriteBudgetsAsync` takes a snapshot; `WriteBudgetAsync` takes a row name and derives `data-budget` and both ids |
| `src/uk.osric.asp.Web/Pages/Seeds/assistant.js` | **changed** — the row map, the `budget.updated` listener, the exceed toggle |
| `src/uk.osric.asp.Web/Pages/Seeds/assistant.css` | **unchanged** |
| `src/uk.osric.asp.Core/Assistant/ContextTrimmer.cs` | **unchanged** — `MeasureChars` is reused as it is |
| `docs/architecture.md`, `docs/decisions.md`, `docs/testing.md` | **changed** — see "Documentation and decisions" |

## Tests

**`Core.Tests` — `BudgetSnapshotTests`** (new)

- `TheContextFigureIsWhatTheTrimmerMeasures`
- `TheContextFigureCountsTheToolCallJsonAsWellAsTheContent`
- `TheLongestArgumentIsTheLongestSerialisedCallInTheTranscript`
- `AMessageWithoutCallsContributesNoArgumentLength`
- `TheLongestResultCountsOnlyToolMessages`
- `TheCountersAreCarriedThroughUnchanged`
- `AMissingUsageCountsAsNoOutput`
- `AnEmptyTranscriptMeasuresOnlyWhatItIsGiven`

**`Web.Tests` — `AssistantBudgetTests`** (new)

- `TheSnapshotAndTheDisplayedToolCallsReadTheSameJson` — the parity test with `ReadToolCalls`, so the
  two parsers cannot drift silently. It lives here rather than in `Core.Tests` because
  `ReadToolCalls` is the shell's, and it is the reason that method is `internal`.

**`Web.IntegrationTests` — `AssistantChatLayoutTests`** (changed)

- `TheBudgetsSectionReportsWhatEachLimitHasHadSpentOnIt` gains the `data-budget` hooks and the
  derived ids, and keeps its existing figures.
- `ARunPublishesItsBudgetsAsItSpendsThem` (new) — the SSE body carries `event: budget.updated`, and
  the last payload names the steps and tool calls the answered run took.
- `TheBudgetsAfterTheReloadMatchTheLastLiveFigure` (new) — the final `budget.updated` and the
  rendered page agree, which is the whole point of one snapshot.

**`Web.BrowserTests` — `AssistantChatTests`** (changed)

- `TheStepBudgetCountsUpWhileTheAnswerStreams` — script a tool call and then a slow answer, and
  assert `#assistant-budget-steps` reaches `2` while `#assistant-chat` still has `aria-busy`, before
  the completion reload.
- `AToolCallRaisesTheToolCallBudgetBeforeTheRunCompletes`
- `TypingIsNotOverwrittenByABudgetUpdate` — the message row keeps the textarea's figure across a
  later `budget.updated`, proving `message` is never published. The first step is held open by the
  scripted provider so the typing lands between two updates rather than after the last one.

The browser cases lean on the scripted provider's delays, which `AssistantStreamTests` already uses
to keep a run in flight.

## Phases

Each phase ends with `make test` green and `make format-check` clean.

1. **The value.** `BudgetSnapshot` and its suite in `Core`, with no behaviour change anywhere else.
   This is the phase worth reviewing hardest, because everything later trusts it.
2. **The page renders it.** `WriteBudgetsAsync`/`WriteBudgetAsync` take the snapshot and the row
   name, the ids become derived, and the integration assertions gain the hooks. Nothing is published
   yet; the page is byte-identical in what a reader sees.
3. **The stream publishes it.** The loop keeps and advances the snapshot and publishes
   `budget.updated`; the integration tests for the event land with it.
4. **The browser applies it.** The row map and the listener, the exceed toggle, the three browser
   cases, and the documentation and decisions below.

Phase 2 depends on phase 1, phase 3 depends on phase 1, and phase 4 depends on 2 and 3. Phase 1 and
the write-up can land without any visible change.

## Documentation and decisions

[Site navigation](../site-navigation.md) reserves 148–158, so this plan takes the numbers after it.

- **DEC-159**: every assistant budget figure is one pure `BudgetSnapshot` in
  `Core.Assistant`, measured by `ContextTrimmer.MeasureChars` and the transcript's own tool calls and
  results, and read by both the page render and the run stream. The context row counts the untrimmed
  conversation, as the trimmer counts it; the output row counts the most recent step's completion
  tokens, as `RecordStepAsync` stores them.
- **DEC-160**: the conversation page's budgets are updated live over the existing run journal as
  `budget.updated`, published when the loop learns something and never on a timer. There is no
  budgets endpoint; a terminal event still reloads the page, and the reload is authoritative.
- **DEC-161**: a budget update changes the count text, the meter's `value` and the exceeded class,
  and nothing else — the meter stays `aria-hidden` and the list is not a live region (extends
  DEC-141).
- `docs/architecture.md`: "AI page assistant" gains a sentence that the budgets the page shows are
  the same snapshot the run publishes, and that the context figure is the conversation's rather than
  the trimmed request's.
- `docs/testing.md`: the assistant bullet names `BudgetSnapshotTests` and the browser case that
  watches a figure move mid-run.
- `deploy/`: nothing. No configuration, no new route, no new asset.

## Risks

- **Two figures that disagree.** The whole plan exists to prevent it, and the guard is one
  calculation plus the integration test that compares the last event with the reloaded page. A
  disagreement that survives both would be a bug in what "used" means, not in the wiring.
- **Publishing after the stream completes.** `RunStream.Publish` throws, and the throw would land in
  the run's failure path and turn a finished run into a failed one. The mitigation is structural:
  the update before `run.completed` is the last one, and the failure paths publish nothing.
- **A row that moves backwards.** `output` resets at the start of a step because the provider reports
  per-step usage; it is the number the page shows after a reload, and the note under the list
  already explains the semantics. Everything else only counts up: the counters increment, and
  context, tool arguments and tool results are maxima over the transcript.
- **Journal pressure.** One or two events per step and per tool call — well inside
  `JournalMaxEvents` (20,000) and `JournalMaxBytes` (4 MiB), and the payload is six numbers.
- **Browser cost.** One object per step, two attribute writes per row, no reflow beyond the meters.
- **The browser test racing the reload.** A slow scripted answer is the existing technique for
  keeping a run in flight; the assertion is made against a locator that exists before the completion
  reload, so a fast provider cannot make it flaky — only a *slow* one is needed.
- **A stale figure on a dead stream.** A dropped stream reconnects and replays; after five failures
  the page says so in the status line, and the register of budgets simply stops moving — which the
  status line already explains.

## Open questions for the review

1. **Should the event carry the limits too?** Recommended no: they come from `AssistantOptions`,
   fixed for the life of the process, and the page has already rendered them. Sending them invites
   the client to become a second source of truth about what a limit is.
2. **Should `message` be published as zero for a uniform payload?** Recommended no — it has no
   server-side value, and publishing `0` would fight the textarea on every step.
3. **Should a row announce itself when it is exceeded?** Recommended no to `aria-live`; the status
   line already speaks when a run stops, and DEC-141's reason is in the transcript on reload. If the
   review wants more, the narrow version is one `role="status"` sentence when a budget is first
   crossed, not a live list.
4. **Context per step, or only when it changes shape?** Recommended per step: it is one
   `MeasureChars` over a transcript the loop has already fetched and parsed for the trimmer, and
   skipping repeats would mean comparing two large figures to save a few bytes on the wire.
5. **Should the final event's output figure come from the completing step's usage, or from the
   retained counter?** Recommended the counter: `run.completed` already carries the last step's
   `usage`, but a provider that omits usage on its final step would leave the live figure disagreeing
   with the stored one, which `RecordStepAsync` keeps. The duplicate in the two payloads is harmless
   — the client reads `budget.updated` and ignores the rest.

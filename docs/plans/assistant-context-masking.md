<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Assistant context masking, with whole-turn trimming as the fallback

Status: **proposed**. Written 2026-11-09; checked against the code on the same day.

## The short version

- **The context row becomes the request the provider actually receives.** `SentBytes` is the UTF-8
  byte count of the serialized chat-completions body — model, system prompt, tool definitions,
  every message, and the small envelope — produced by the one serializer the client sends. It is
  not the untrimmed transcript, and it is not a character count.
- **The budget is the same number.** `Assistant:MaxContextChars` becomes
  `Assistant:MaxRequestBytes`, and the planner refuses a request that would exceed it. One budget,
  one measure, no page/run disagreement.
- **Masking comes first.** `ObservationMasker` replaces old tool results and the long string values
  inside old tool-call arguments with deterministic placeholders. The last eight tool results, the
  last two tool-call messages, every user message, every assistant message's own text, and every
  tool-call id and name are kept.
- **Trimming stays as the fallback.** `ContextTrimmer` drops the oldest whole user turns only after
  masking is exhausted, never the newest turn, and never splits a turn from its tool results.
- **The mask is derived, not stored.** The transcript the owner reads is still complete; the
  changed-pages card still reads real tool results; there is no migration.
- **One `ContextPlan` is built by the run, the completion and the page render.** The live figure and
  the reloaded one are the same function over the same stored transcript.

## Goals

- Make the number a reader watches mean the thing that can fail: the bytes of the request the
  provider is sent.
- Keep a long page-authoring conversation inside that budget without throwing away the goal, the
  plan, the decisions, or the recent work.
- Spend the cheapest information first: old payloads go before old reasoning, and old turns go only
  when masking cannot free enough.
- Preserve the invariants the provider requires: a `tool_calls` message always keeps its matching
  `tool` message, and a masked call is still valid JSON.
- Keep every rule pure and testable in `Core`; the shell still does the I/O and the provider call.
- Keep the page and the run measuring one thing, so a live figure and a reload cannot disagree.

## Non-goals

- **Summarising the conversation with a model.** That is compaction, not masking; it costs a second
  model call, is non-deterministic, and the evidence in [the JetBrains study][jetbrains] is that
  masking is at least as effective on coding agents. It remains a later escalation, not this plan.
- **Persisting the mask.** The stored transcript is the record of what the tools did. Rewriting it
  would lose data, complicate the changed-pages card, and make the page show less than the owner
  asked for.
- **Masking user text, assistant text, the system prompt, or the tool definitions.** These are the
  task, the reasoning, and the contract the model needs to call a tool. They are not observations.
- **A tokeniser dependency.** The budget is wire bytes, a conservative proxy for the provider's
  input tokens. The provider's own limit remains the final authority.
- **Changing the provider protocol.** The request is still OpenAI-compatible chat completions; only
  its size is measured and some old values are replaced.
- **A diagnostic view of the exact masked request.** The log line carries the counts; a future
  diagnostics view can rebuild the plan from the stored transcript.
- **A new page-author capability.** `/help` does not change, because nothing a page author can do
  changes.

## What exists today

Checked against the code on 2026-11-09.

- **The trimmer drops whole turns against a character count.**
  [`ContextTrimmer.Trim`](../../src/uk.osric.asp.Core/Assistant/ContextTrimmer.cs) keeps the system
  prompt and the newest whole turns that fit `maximumChars`. It groups a turn at each `user`
  message, never splits a turn, and returns an error when the system prompt and newest turn alone do
  not fit, which the run turns into `context_too_long`. `MeasureChars` counts
  `Content.Length + ToolCallsJson.Length`; it does not count the JSON envelope, the tool name or the
  tool-call id.
- **The run builds the provider request from the trimmer's output.**
  [`RunProcessor`](../../src/uk.osric.asp.Web/Assistant/RunProcessor.cs) loads the transcript and
  the system prompt, trims them, and constructs a `ChatRequest` around `trimmed.Messages`. It
  publishes `context_trimmed` as a `run.warning`.
- **The context row counts the untrimmed conversation.**
  [`BudgetSnapshot.Measure`](../../src/uk.osric.asp.Core/Assistant/BudgetSnapshot.cs) takes the
  system prompt plus every stored message and measures them with `ContextTrimmer.MeasureChars`.
  [DEC-159](../decisions.md) made that deliberate: the page render and the run stream measure the
  stored transcript so they cannot disagree. [DEC-141](../decisions.md) and
  [DEC-162](../decisions.md) describe the row and its compact presentation.
- **The page builds the same snapshot without the tools.** `AssistantEndpoint` calls
  `BudgetSnapshot.Measure([system, .. messages], ...)`; the tool definitions are in the request the
  run sends but not in the page's figure.
- **The request body is built in one private place.** `OpenAiChatCompletionClient.SerializeRequest`
  turns the `ChatRequest` into the JSON that `StringContent` sends. Nothing else can measure that
  body, so no meter can agree with it.
- **There is no masking.** The only mitigation for a long conversation is dropping old turns.
- **The configuration is character-shaped.** `Assistant:MaxContextChars` is 500,000 in
  `appsettings.json`, 120,000 in `AssistantOptions`, and has a 16,384 floor in the validator.

## What this touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Core/Assistant/ChatRequestJson.cs` | **new** — the one serializer and the byte measure |
| `src/uk.osric.asp.Core/Assistant/ConversationTurns.cs` | **new** — the whole-turn rule the trimmer uses |
| `src/uk.osric.asp.Core/Assistant/ObservationMasker.cs` | **new** — per-message observation and argument masking |
| `src/uk.osric.asp.Core/Assistant/ContextPlanner.cs` | **new** — measure, mask oldest message at a time, then trim |
| `src/uk.osric.asp.Core/Assistant/ContextTrimmer.cs` | **changed** — takes a `ChatRequest` and a byte budget; drops whole turns; no char measure |
| `src/uk.osric.asp.Core/Assistant/BudgetSnapshot.cs` | **changed** — `SentBytes` from the plan; argument and result rows still read the stored transcript |
| `src/uk.osric.asp.Web/Assistant/OpenAiChatCompletionClient.cs` | **changed** — sends `ChatRequestJson.Serialize(request)`; no private serializer |
| `src/uk.osric.asp.Web/Assistant/RunProcessor.cs` | **changed** — builds and sends the plan, warns once per run, measures the budget from the plan |
| `src/uk.osric.asp.Web/Assistant/AssistantEndpoint.cs` | **changed** — builds the plan for the next request; label, unit and note |
| `src/uk.osric.asp.Web/Assistant/AssistantOptions.cs` | **changed** — `MaxRequestBytes` and its floor |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | **changed** — one sentence about elided values; version `page-author-v10` |
| `src/uk.osric.asp.Web/appsettings.json` | **changed** — `MaxRequestBytes` replaces `MaxContextChars` |
| `test/uk.osric.asp.Core.Tests/` | **changed/new** — `ChatRequestJsonTests`, `ObservationMaskerTests`, `ContextPlannerTests`, rewritten `ContextTrimmerTests`, changed `BudgetSnapshotTests` |
| `test/uk.osric.asp.Web.Tests/` | **changed** — provider body parity, plan wiring, updated options |
| `test/uk.osric.asp.Web.IntegrationTests/` | **changed** — sent bytes, reload parity, masked-but-stored transcript |
| `docs/architecture.md`, `docs/decisions.md`, `docs/glossary.md`, `docs/testing.md` | **changed** — see "Documentation and decisions" |

No database schema changes and no new package. `System.Text.Json` is already used in `Core`.

## Design

### D1 — The request body is the unit of account

The context row must count what the provider is sent, not what the database holds:

```text
SentBytes = UTF8.GetByteCount(ChatRequestJson.Serialize(request))
```

The body includes `model`, `messages`, `stream`, `tools`, `tool_choice`, `temperature`,
`max_tokens`, and `stream_options` when usage is requested. It does not include HTTP headers or the
authorization token, because those are not input the model reads.

This is deliberately not a character count and deliberately not a token count:

- **Characters undercount.** The current measure misses the JSON envelope, the tool name and the
  tool-call id, and it misses JSON escaping. `System.Text.Json` escapes HTML-significant characters
  and non-ASCII, so a page source full of `<div>` becomes `\u003Cdiv\u003E` on the wire. The
  escaped body is what can trip a provider body-size limit and is a conservative upper bound on the
  input the provider tokenises.
- **The provider decodes the JSON before tokenising it,** so `SentBytes` over-counts escaped HTML
  and non-ASCII relative to the model's token count. That is the safe direction. It is also the
  number we can measure without a tokeniser, and the number that protects the request-size limit.
- **The budget must leave room for the output.** `SentBytes` is the input; `MaxOutputTokens` is the
  completion. On a 128k-token model with a 32k-token output reserve, there is only about 96k tokens
  of input. Because bytes are not tokens, the shipped 500,000 needs reviewing against the configured
  model rather than assuming it is safe. This plan keeps the number until the review settles it.

The common case measures once and returns. The expensive path is a request that is already over
budget, and that path measures once per message it masks and once per turn it drops.

### D2 — One serializer, used by the sender and the meter

`OpenAiChatCompletionClient.SerializeRequest` moves to
`Core.Assistant.ChatRequestJson.Serialize(ChatRequest)`, with
`MeasureBytes(ChatRequest)` as `UTF8.GetByteCount(Serialize(request))`. The client calls it once per
send, outside the retry loop, and `StringContent` still puts those exact bytes on the wire.

The point is not tidiness. If the meter and the client each build a request, a field added to one
and not the other makes the meter a lie. A Web test asserts that the captured request body is
exactly `ChatRequestJson.Serialize(request)` and that its length is `MeasureBytes(request)`.

`ChatRequestJson` is pure: it parses the stored `ToolCallsJson` and `ParametersJson`, builds the
body, and serializes it. It does no I/O, holds no state, and is in `Core` because both the sender
and the measure have to agree about it.

### D3 — Mask old payloads, not the record of what was done

Masking has two targets, both payloads the model has already consumed:

1. **A tool result.** A `tool` message's `Content` is replaced whole with
   `"[tool result elided: {n} chars]"`. Its `ToolCallId` and `ToolName` are kept, so the
   assistant's `tool_calls` message and this result still pair.
2. **A long string inside a tool call's arguments.** An `assistant` message's `ToolCallsJson` keeps
   its `id`, `type` and `function.name`; each string value in the parsed arguments object longer
   than 1,024 characters is replaced with `"[elided {n} chars]"`. Short values and all keys survive,
   so `{"path":"/blog","content":"[elided 12000 chars]"}` still says which page was written. Nested
   objects and arrays are walked to a bounded depth; a value whose placeholder would not be shorter
   is left alone.

Tool-call arguments matter as much as results here: `write_page` and `edit_page` carry the whole
page source, and in a page-authoring conversation those arguments are the largest thing in the
history. Masking only results would leave the biggest payload untouched.

Never masked: the system prompt, any `user` content, an `assistant` message's own `Content`, any
`tool_call` `id` or `name`, and the tool definitions. A call whose argument string is not valid
JSON is left exactly as it is; the tool executor would already have refused it, and the planner
must not turn a corrupt call into a new failure.

The masked call remains valid JSON and remains a valid `tool_calls` entry. That is the invariant
that makes the provider accept the next request.

### D4 — The windows are the last eight results and the last two calls

A user turn in this assistant is not a conversational exchange; it is a whole run, and a run may
contain hundreds of steps. Keeping the last two user turns unmasked would therefore mean a single
long page-authoring run can never be masked, which is exactly the case the budget exists for.

A result and a call's arguments are different currencies, so they get different windows:

- **Results.** The last eight `tool` messages are kept wherever they fall. Everything older is
  eligible to be masked. A result is an observation the model may still be reasoning about.
- **Arguments.** The last two assistant messages that carry `tool_calls` keep their argument
  payloads. Older calls are eligible. A call's arguments are an action, and only the most recent
  actions need their full payload; the assistant's own text and the tool name still say what it did.

User and assistant text are never masked, however old.

The windows are independent on purpose: a kept result may outlive its call's payload, because the
result is the outcome and the call's name and short values still say what it was.

Eight and two are constants in `ObservationMasker`, not configuration knobs. Eight results is
roughly one busy step's working set; two calls keep the current and previous action legible. They
are the first things to tune if a trace shows the model re-reading a page it just read or repeating
a write whose payload it can no longer see.

### D5 — Mask one old message at a time, oldest first, and measure

`ConversationTurns.Split` is the trimmer's rule: the system prompt is separate, and a turn begins at
each `user` message (or at the first message when there is no user message). The masker does not
need turns; it walks the message list and the observation window.

`ContextPlanner.Build(request, maximumBytes)`:

1. Measure the whole request. If it fits, return it unchanged. Nothing is masked, so the common
   request keeps its prefix byte-identical and the provider's prompt cache is untouched.
2. Ask `ObservationMasker` for the indices of maskable messages, oldest first. For each, replace
   that message's payloads — a `tool` result, or the long strings in an `assistant` message's
   arguments — measure the whole request again, and stop as soon as it fits.
3. If masking every eligible message is still not enough, hand the masked request to
   `ContextTrimmer.Trim`, which drops whole turns as the fallback.
4. If the smallest request — system prompt, tool definitions and the newest turn — still does not
   fit, return an error with the byte counts. The run fails before the provider is called.

A message is the unit because a tool result is one coherent payload and an assistant message's
arguments are one coherent call. A user turn is not: in this assistant it can contain a hundred
steps, and masking all of it to save a kilobyte would throw away the cold context masking exists to
keep. The loop is at most one serialization per masked message, only on the request that is already
over budget; if a measurement shows that cost matters, the next step is a per-message byte delta,
not a coarser unit.

The plan is deterministic. It walks messages in stored sequence order, never sorts by size, never
uses the clock or the culture, and serializes with fixed options. The same transcript and
configuration produce the same bytes, which is what makes a retry and a reload comparable.

### D6 — Trim whole turns only after masking is exhausted

`ContextTrimmer.Trim(ChatRequest request, long maximumBytes)` keeps its current policy, measured in
bytes instead of characters:

- Keep the system prompt.
- Drop whole turns from the oldest, never splitting a turn from its tool calls and results.
- Never drop the newest turn.
- If the newest turn alone cannot fit, return an error naming the bytes needed and the budget.

Masking runs first because it loses less: it keeps the user's words, the assistant's reasoning, the
tool names and the short arguments, and gives up only old payloads. Dropping a turn loses all of
that. The trimmer is the fallback for a conversation whose old *reasoning* is the bulk, not its old
tool output.

On the error path the trimmer still returns the smallest request it could build and its `SentBytes`.
The shell must not send it, but the page can render the over-budget figure, which is more useful
than an empty meter.

### D7 — The mask is derived, never stored

The transcript in SQLite stays complete. Masking is a request-shaping rule applied per step:

- The conversation page keeps showing full tool results, so the owner can inspect what a tool did.
- `PageWrites` keeps reading stored results, so the changed-pages card still names every page a
  write saved.
- No migration, no invalidation, and no stale mask when the window or the budget changes; the next
  step derives a new plan from the same stored messages.
- The only durable evidence that masking happened is the request size and a log line. The warning
  on the stream is transient, exactly as `context_trimmed` is today.

### D8 — One plan is read by the run, the completion and the page

`ContextPlanner.Build` is the only policy. It returns one `ContextPlan`:

```csharp
public sealed record ContextPlan(
    ChatRequest Request,
    int MaskedToolResults,
    int MaskedArguments,
    int DroppedMessages,
    long OriginalBytes,
    long SentBytes,
    string? Error) {

    public bool IsMasked => MaskedToolResults > 0 || MaskedArguments > 0;
    public bool IsTrimmed => DroppedMessages > 0;
}
```

`MaskedArguments` counts the argument strings elided, not the calls rewritten: one `write_page` call
usually has one, while a call with two long values has two.

- **The run** builds the plan at the start of each step, checks `Error`, publishes the warnings,
  and sends `plan.Request`. It does not trim before planning; the planner owns the order.
- **The completion** rebuilds the plan from the stored transcript before publishing the final
  `budget.updated`, so the last live figure is the request the next step would send; the reload
  builds the same plan, so it agrees with that figure.
- **The page** builds the same plan from the stored transcript and the same options, including
  `PageToolExecutor.Definitions`, and renders its `SentBytes`.

`BudgetSnapshot.Measure` changes to take the plan and the stored transcript:

```csharp
Measure(ContextPlan plan, IReadOnlyList<ChatMessage> stored, int steps, int toolCalls, int? completionTokens)
```

The context row is `plan.SentBytes`. The longest-argument and longest-result rows still measure
`stored`, not the plan: masking changes the request, not the cap a stored call or result had to
satisfy, and the page must keep showing the real stored values.

The row keeps its `context` key, because `assistant.js` finds rows by `data-budget`. Its label
becomes **Request** and its unit **bytes**.

### D9 — What the reader sees

- `Request` replaces `Context` as the row label; the figure is the exact request size and the meter's
  maximum is `MaxRequestBytes`.
- The note under the budget list changes from "the rest are this conversation" to: *"Request is
  the provider request as sent: the system prompt, the tool definitions and the messages after
  masking and trimming. The transcript above is complete. Steps, tool calls and output count the
  current or most recent run."*
- The exceeded class already marks a request over its budget; on the error path the page shows the
  over-budget figure rather than nothing.
- No `assistant.js` change: the row key, the meter, the count span and the exceeded toggle are all
  unchanged, and the unit is static markup.

### D10 — What the model is told

The system prompt gains one short paragraph, and `PageAuthorPrompt.Version` becomes
`page-author-v10`:

> When the conversation is long, an old tool result may be shown as
> `[tool result elided: N chars]` and a large old tool-call argument may be shown as
> `[elided N chars]`. Treat an elided value as unavailable, not as empty, and call the tool again if
> you need the detail.

Without it, a model can mistake a placeholder for an empty result and reason from a false negative.
The paragraph is part of every request and is counted by `SentBytes`, which is the point of having
one measure.

### D11 — Failure and warnings

- A request that cannot fit after masking and trimming fails with the existing `context_too_long`
  code and a message naming both figures: `"The request needs 512340 bytes and the budget is 500000.
  The system prompt, tool definitions and newest turn alone are too large."`
- `run.warning` gains `context_masked` with `"Earlier tool output and arguments were shortened to
  fit the request budget."` It is published once per run, the first time masking happens, not on
  every step.
- `context_trimmed` keeps its current text and is also published once per run.
- The run logs an Information line whenever the plan masks or drops, with the counts,
  `OriginalBytes` and `SentBytes`. No provider content is logged.

## Considered and rejected

- **LLM summarisation instead of masking.** A second model call per compaction, non-deterministic,
  and it invalidates the prompt cache from the rewrite point. The JetBrains study found observation
  masking cheaper and at least as reliable on coding agents; summarisation stays a later escalation
  for a conversation whose *reasoning* is the bulk. [Anthropic's compaction][anthropic] is the
  reference if that day comes.
- **Persisting the mask in the transcript.** Loses the record of what a tool did, breaks the
  changed-pages card, and makes the page show less than the owner asked for.
- **Masking every observation older than the window unconditionally.** It is simpler, but a request
  that is one kilobyte over would lose every old result. Masking oldest-first until the request fits
  keeps as much cold context as the budget allows.
- **Dropping individual messages.** It can orphan a `tool_calls` message from its results and make
  the next request invalid. A turn is the smallest safe unit.
- **Two budgets — `MaxContextChars` for the trimmer and `MaxRequestBytes` for the meter.** Two
  numbers that disagree is the bug this plan exists to remove.
- **A tokeniser dependency.** A token count would be more precise but adds a package, a model
  version and a cache; bytes are the wire truth and the provider's limit is the backstop.
- **`UnsafeRelaxedJsonEscaping` to make the body smaller.** It changes the wire format and the
  escaping the provider sees. If it is ever worth doing, it is a separate decision with its own
  measure, not a side effect of budgeting.
- **Request compression.** The provider tokenises the decoded body, so gzip would reduce transfer
  bytes without reducing the context. The budget is not a transfer budget.
- **Masking the tool definitions.** They are the contract the model needs to call a tool, they are
  constant, and a model without them cannot act.
- **Keeping the meter on the untrimmed transcript.** DEC-159 made the page and the run agree, but
  they agree on the wrong number. The request is the thing that fails.

## Components

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Core/Assistant/ChatRequestJson.cs` | **new** — `Serialize`, `MeasureBytes`; the only body builder |
| `src/uk.osric.asp.Core/Assistant/ConversationTurns.cs` | **new** — system plus turns, the trimmer's rule |
| `src/uk.osric.asp.Core/Assistant/ObservationMasker.cs` | **new** — `MaskableMessages`, `MaskMessage`; result placeholder, recursive argument elision |
| `src/uk.osric.asp.Core/Assistant/ContextPlanner.cs` | **new** — `Build`; measure, mask one message at a time, trim, report |
| `src/uk.osric.asp.Core/Assistant/ContextTrimmer.cs` | **changed** — `Trim(ChatRequest, long)`; whole turns; byte measure; no `MeasureChars` |
| `src/uk.osric.asp.Core/Assistant/BudgetSnapshot.cs` | **changed** — `SentBytes`; `Measure(ContextPlan, stored, …)` |
| `src/uk.osric.asp.Web/Assistant/OpenAiChatCompletionClient.cs` | **changed** — `ChatRequestJson.Serialize` once per send |
| `src/uk.osric.asp.Web/Assistant/RunProcessor.cs` | **changed** — build the plan, send `plan.Request`, warn once, measure budgets |
| `src/uk.osric.asp.Web/Assistant/AssistantEndpoint.cs` | **changed** — build the plan, `Request`/`bytes`, the note |
| `src/uk.osric.asp.Web/Assistant/AssistantOptions.cs` | **changed** — `MaxRequestBytes`, floor |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | **changed** — the elision paragraph, `page-author-v10` |
| `src/uk.osric.asp.Web/appsettings.json` | **changed** — `MaxRequestBytes` |

## Tests

**`Core.Tests` — `ChatRequestJsonTests`** (new)

- `TheBodyCarriesEveryFieldTheProviderNeeds`
- `MeasuringCountsUtf8BytesNotCharacters`
- `TheToolDefinitionsAreInTheBodyAndTheMeasure`
- `TheEnvelopeIsInTheMeasure`
- `TheSameRequestSerialisesToTheSameBytes`

**`Core.Tests` — `ObservationMaskerTests`** (new)

- `TheNewestEightResultsAreKept`
- `TheNewestTwoCallsKeepTheirArguments`
- `AnOlderResultIsReplacedWithAPlaceholderThatKeepsItsIdAndName`
- `AnOldCallKeepsItsNameAndLosesItsLongArgument`
- `AResultWhosePlaceholderWouldNotBeSmallerIsKept`
- `ALongArgumentStringIsElidedAndItsKeysAndShortValuesSurvive`
- `ANestedArgumentStringIsElided`
- `AnArgumentAtTheLimitIsKept`
- `InvalidArgumentJsonIsLeftAlone`
- `TheNewestUserMessageIsNeverMasked`
- `MaskingTheSameMessageTwiceGivesTheSameBytes`

**`Core.Tests` — `ContextPlannerTests`** (new)

- `ARequestUnderBudgetIsSentUnchanged`
- `MaskingIsUsedBeforeAnyTurnIsDropped`
- `TheOldestEligibleMessageIsMaskedFirst`
- `DroppingStartsAtTheOldestWholeTurn`
- `TheNewestTurnIsNeverDropped`
- `ADroppedTurnTakesItsToolMessagesWithIt`
- `TheSmallestRequestTooLargeIsAnErrorWithTheBytes`
- `ThePlanReportsTheBytesOfTheRequestItReturns`
- `ThePlanIsStableForTheSameInput`
- `ASystemPromptAndToolsTooLargeIsAnError`
- `ANonPositiveBudgetIsRefused`

**`Core.Tests` — `ContextTrimmerTests`** (rewritten around a tiny request helper)

- `KeepsTheSystemPromptAndNewestUserTurn`
- `DropsToolCallTurnsAsAUnit`
- `ReportsWhenTheNewestTurnCannotFit`
- `KeepsTheNewestWholeTurnWhenAnOlderTurnMustBeDropped`
- `KeepsEveryTurnWhenTheBudgetAllows`
- `ATranscriptWithoutASystemPromptKeepsTheNewestTurn`
- `ReportsWhenTheSystemPromptAloneIsTooLong`
- `ReportsWhenTheSystemFitsButTheNewestTurnDoesNot`
- `TheBudgetIsMeasuredInBytes`
- `TheEnvelopeCountsTowardsTheBudget`
- `ANullTranscriptIsRefused`
- `ANonPositiveBudgetIsRefused`

**`Core.Tests` — `BudgetSnapshotTests`** (changed)

- `TheRequestFigureIsThePlansSentBytes`
- `TheArgumentRowStillMeasuresTheStoredTranscript`
- `TheResultRowStillMeasuresTheStoredTranscript`
- The counter and empty-transcript cases carry through.

**`Web.Tests` — `AssistantProviderClientTests`** (changed)

- `TheBodySentIsTheOneChatRequestJsonSerialises`
- `TheMeasuredBytesAreTheBodyBytes`

**`Web.Tests` — `AssistantOptionsTests`** (changed)

- `ABudgetBelowTheFloorNamesMaxRequestBytes` (new) — the renamed floor is the one the error names.

**`Web.Tests` — `RunProcessorTests`** (changed)

- `ARunSendsThePlanItMeasured` — each `ChatRequest` the fake client records measures to the
  `budget.updated` context figure the step published when it started.
- `AnOverBudgetRunFailsBeforeTheProviderIsCalled` — `context_too_long`, no request recorded.
- `AnOverBudgetRunRaisesTheMaskedWarningOnce` — two over-budget steps, one warning.

The Web fixtures raise `MaxRequestBytes` above the fixed system prompt and tool definitions, because
those are real and large. A test that needs an over-budget condition derives the budget from a
measured baseline rather than hard-coding a number that the prompt can outgrow.

**`Web.IntegrationTests` — `AssistantFlowTests` / `AssistantChatLayoutTests`** (changed)

- `TheStepBudgetIsTheRequestThatStepSent` — each step's `budget.updated` equals
  `ChatRequestJson.MeasureBytes` of the `ChatRequest` the scripted client recorded for that step.
  The HTTP body parity is `AssistantProviderClientTests`' job, not this fixture's.
- `TheReloadShowsTheSameRequestFigureAsTheLastLiveUpdate` — the final figure is the next request the
  stored transcript would send, and the reload builds the same plan.
- `AnOldToolResultIsMaskedInTheRequestButNotInTheTranscript` — the recorded request carries the
  placeholder; the page still renders the full stored result.
- `ARunThatCannotFitFailsWithContextTooLong`
- `TheBudgetsSectionReportsWhatEachLimitHasHadSpentOnItAsync` gains the `Request` label, the `bytes`
  unit and a context figure that is the measured request, not the stored transcript.

No new browser test: the row is a number the script already applies, and the existing budget browser
cases cover the update path.

## Phases

Each phase ends with `make test` green and `make format-check` clean.

1. **One measure and one budget.** Add `ChatRequestJson`, `ConversationTurns` and `ContextPlanner`
   with only the trim step; `ContextTrimmer` takes a request and a byte budget;
   `BudgetSnapshot.SentBytes`; rename the option and the appsettings key; change the row label, unit
   and note. This phase fixes the meter and keeps the existing trim-only policy. It is shippable on
   its own.
2. **Masking.** Add `ObservationMasker` and call it from `ContextPlanner` before the trimmer; wire
   the run, the completion and the page through the plan; add the prompt paragraph and bump its
   version; add the warnings. The trimmer is now the fallback rather than the only policy.
3. **Proof and write-up.** Add the integration parity and stored-transcript cases; update
   `architecture.md`, `decisions.md`, `glossary.md` and `testing.md`; remove any dead char-based
   code and tests. Run the full suite and the format check.

Phase 2 depends on phase 1. Phase 3 depends on both. Phase 1 alone is a complete, useful change.

## Documentation and decisions

- **`docs/architecture.md`** — replace the paragraph that says the context row counts the whole
  untrimmed transcript "rather than the trimmed request that was actually sent". The new paragraph
  says the row is the wire request, and that masking runs before whole-turn dropping.
- **`docs/glossary.md`** — the transcript entry names `ContextPlanner`, not `ContextTrimmer`; add
  "masking" (replacing old observations in a request without changing the stored transcript) and
  "sent bytes" (the UTF-8 body the provider receives).
- **`docs/testing.md`** — the assistant bullet says "transcript trimming"; it becomes "request
  planning: observation masking, whole-turn trimming, and the byte measure the page and the stream
  share".
- **`/help`** — unchanged. This is host behaviour; no page author capability changes.
- **`deploy/`** — unchanged. No quadlet sets the key, but an operator with
  `Assistant__MaxContextChars` in its environment must rename it to `Assistant__MaxRequestBytes`;
  the old key is not aliased.

New decisions:

- **DEC-200** — The context row measures the provider request as sent: the UTF-8 bytes of the body
  from the one serializer the client uses, including the system prompt, tool definitions and the
  messages after masking and trimming. It supersedes DEC-159's untrimmed-transcript rule and
  DEC-141's "context" wording; the row key stays `context`, the label becomes "Request", the unit
  becomes "bytes", and `MaxContextChars` becomes `MaxRequestBytes`.
- **DEC-201** — Before any whole turn is dropped, `ObservationMasker` replaces old tool results and
  long tool-call argument strings with deterministic placeholders, keeping the newest eight tool
  results, the last two tool-call messages, and every user or assistant word. The mask is derived
  per request, never stored, so the transcript stays complete and the changed-pages card still reads
  real results.
- **DEC-202** — Masking is attempted one old message at a time, oldest first, and only when the
  request is over budget; a tool result or an assistant message's long arguments are masked as one
  payload, and `ContextTrimmer` drops whole user turns only after masking is exhausted. The newest
  turn is never dropped, and a request that still does not fit fails with `context_too_long` naming
  the bytes rather than being sent.

## Risks

- **The model loses an old observation it needed.** Mitigation: the last eight results and the last
  two calls keep their payloads, user and assistant text is never masked, the prompt tells the model
  to re-read, and the warning names what happened. If traces show re-reading, tune the windows
  before adding summary.
- **A provider rejects an elided tool-call argument.** The arguments stay valid JSON with the same
  keys where they were short, but a provider that validates arguments against the tool schema may
  refuse a placeholder string where it expected page source. Mitigation: test against the configured
  provider; if it refuses, disable argument elision and let the trimmer drop the turn instead.
- **Wire bytes are not tokens.** Escaped HTML inflates the figure and non-Latin text can make tokens
  grow faster than bytes per character. The budget is deliberately conservative, but it cannot
  promise the model's window; size `MaxRequestBytes` with the output reserve in mind and keep the
  provider's own error as the backstop.
- **Prompt-cache invalidation.** Masking changes the prefix from the oldest masked message. It only
  happens when the request is over budget, and a request that does not fit has no cache benefit to
  preserve.
- **Two request builders drift again.** The provider parity test and the single `ChatRequestJson`
  are the guard; any new field goes there, not into the client.
- **Serialization cost.** The planner may serialize the request once per masked message. `n` is
  bounded by the transcript and only the over-budget path pays it. If a measurement shows it
  matters, the next step is a per-message byte delta, not a coarser unit.
- **The page and the run disagree after a code change.** Both build the plan through the same
  function and options; the integration test compares the live figure with the reloaded one.
- **A long single run still exceeds the budget.** The rolling observation window is deliberately
  not turn-based, so old steps inside the current run are maskable. If the newest observations alone
  are too large, the run fails honestly rather than sending a request the provider will refuse.

## Open questions for review

1. **Eight results and two calls.** Are those windows right, or should the result window be one
   `MaxToolCallsPerStep` (32) and the call window longer? Eight is more aggressive but leaves room
   for the budget; 32 results may itself exceed it, and a longer call window keeps payloads a model
   is unlikely to need.
2. **Argument elision.** Is the configured provider willing to accept a `write_page` call whose
   `content` is `[elided N chars]`? If not, the fallback is result-only masking plus whole-turn
   dropping.
3. **The label.** "Request" is honest; "Context" is familiar. The row key stays `context` either
   way.
4. **The shipped budget.** The shipped `MaxRequestBytes` is 500,000 today. On a 128k-token model
   with `MaxOutputTokens` at 32,768, that may leave too little output headroom. The review should
   size it against the actual model window, or lower `MaxOutputTokens`.
5. **Should the masking warning be persisted?** Today it is transient, like `context_trimmed`. The
   transcript is complete and the request row is the durable evidence; a persisted note would be a
   new column and a new rendering rule.

[jetbrains]: https://blog.jetbrains.com/research/2025/12/efficient-context-management/
[anthropic]: https://www.anthropic.com/engineering/effective-context-engineering-for-ai-agents

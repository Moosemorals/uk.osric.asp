<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The assistant asks the owner and waits for the answer

| | |
| --- | --- |
| Status | Proposed 2026-11-18 — waits for the owner. Nothing below is built |
| Size | Large — one tool and its question values, one table and its migration, a coordinator, a route, the card in two renderers, three configuration values, a prompt version and six suites |
| Area | Core (Assistant), Web (assistant, storage), docs |
| Spike | [The ask tool's shape](../spikes/ask-tool-shape.md) read MCP elicitation, Claude Code, VS Code, Cline, Roo Code, OpenAI, DeepSeek, LangGraph and AutoGen; it recommends the schema, the caps and the lifecycle this plan adopts, and names the two levers — the description and the parameter descriptions — that the evidence says matter most |
| Backlog | [IDEA-052](../jobs/IDEA-052.md) |
| Decisions | New names proposed: **assistant-a-question-is-a-tool-call** and **assistant-a-question-pauses-its-run**. Edits `assistant-deadlines-bound-silence`, whose list of things that stop the clock gains the answer. Relies on `assistant-a-tool-is-one-type`, `assistant-a-run-is-not-resumed`, `code-policy-values-are-configuration` |

## The short version

The assistant can read, write, fetch and remember, and it can hear the owner twice: at the start of a
task and at a yes/no gate. A run that needs one fact only the owner holds — which of two page names,
what a heading should say, whether a section is still wanted — has no way to ask. It guesses, or it
ends its turn with the question in prose and loses the work it had queued behind the answer.

This adds the missing turn: a tool, `ask_owner`, whose **result is the owner's answer**. The owner sees
a card in the transcript with the questions, any options the model offered, and a box for their own
words. The run is stored `awaiting_answer` while it waits, exactly as it is stored `awaiting_approval`
at a gate, and carries on in the same step when the answer arrives. Nothing about it is a page
capability: a page is rendered and returned, and this is the assistant's own conversation.

The survey found no cross-vendor standard, but a strong convergence — one tool, one to four question
objects, options of a label and a description, free text always allowed, hard caps — and two levers
the vendor documentation agrees on more strongly than any schema: a description that says *when to use
it and when not*, and a description on each parameter. This site's tool schema emits no parameter
descriptions at all, so the plan adds the first one, for this tool's arguments only.

Three things follow from the evidence and are the plan's load-bearing choices:

1. **A question is not an approval.** It is not gated by `RequireApproval`, it has no yes/no, and its
   record carries the answer rather than a decision. The approval machinery is copied, not widened.
2. **The answer is the tool's result, read from the store.** A disconnected browser, a reload or a
   second device cannot change the outcome, and a restart fails the question closed the way it fails a
   pending approval.
3. **The wait is the owner's, so no clock judges it.** The tool timeout does not apply, the run's
   silence clock stops while the card waits, and the wait is not counted as tool time.

## What exists today

- **Twenty tools, one registry.** `ToolRegistry.Default` is the only list
  (`assistant-a-tool-is-one-type`); each tool carries its name, description, argument schema, approval
  flag, refusal verb and whether it writes. The model's definitions, the dispatcher and the gate all
  read it. `ToolSpecsTests` pins the count at twenty.
- **A wait, once.** `RunProcessor.ResolveApprovalAsync` creates a `ToolApproval` record, stores the run
  `awaiting_approval`, publishes `approval.required`, waits on `ToolApprovalCoordinator`, restores
  `running`, and answers approved or denied. `FailPendingApprovalsAsync` and
  `RecoverInterruptedRunsAsync` fail an unanswered record closed and write a synthetic tool result for
  the call that never completed. `WhilePaused` stops the run's silence clock for the wait.
- **A bounded schema with two levels.** `ToolArgumentSchema.Items` recurses, `ToolArguments.Parse`
  validates nested arrays of objects, and `edit_page`'s `edits` array is a nested object array in
  daily use against this provider. `ParametersJson` emits `type`, `properties`, `required` and
  `additionalProperties: false` — and **nothing else**: no per-parameter description, no `enum`, no
  `maxItems`.
- **The card is rendered twice.** Approvals: a pending card in the server's markup and one built by
  `assistant.js` from `approval.required`. Continuation: the page's card replaces the stored one,
  keyed by `data-budget`, rather than joining it. The transcript's per-turn cards (changed pages) are
  rendered from stored tool results.
- **Statuses are facts.** `queued`, `running`, `awaiting_approval`, `awaiting_continuation`,
  `completed`, `failed`, `cancelled`, `interrupted` are enumerated in code: the two active-run queries
  and `IsRunning` in `SqliteConversationStore`, `RecoverInterruptedRunsAsync`, and the unique index
  filter on `AssistantRuns.ConversationId`.
- **The owner's words are bounded by the prompt's share.** The compose box's `maxlength` and the
  endpoint's refusal are both `ContextBudget.MessageChars`, an eighth of the prompt's budget.

## Goals

1. One tool, `ask_owner`, answers the owner's reply as this call's result, so the run continues in the
   same step with nothing to reconstruct.
2. The run is stored `awaiting_answer` while it waits; a reload, another device or a dropped stream
   changes nothing; a restart fails the question closed and leaves a synthetic tool result.
3. The card asks every question the model sent, with the options it offered, and always allows the
   owner's own words. It is usable from the keyboard and readable by a screen reader.
4. The wait has no deadline of its own: the tool timeout and the run's silence clock do not apply,
   and the wait is not counted in `assistant.tool.duration`.
5. The question, the answer and the call are bounded by configuration, and every cap names itself in
   its refusal.
6. The model is told when to ask, when not to, what the result means, and that the answer is the owner
   speaking. The prompt says so too, and its version moves.
7. Nothing outside the assistant changes: no page capability, no `/help` edit, no provider change.

## Non-goals

- **A page capability.** `ctx.AskAsync(...)` would be a page that blocks its render on a visitor. If
  that is ever wanted it is a decision and an `IDEA-` of its own.
- **An approval that carries text.** Approvals stay yes/no; the owner's reason for a denial is not
  added here.
- **A timeout, an auto-answer or an "away" mode.** Approvals and continuations wait until the owner
  stops the run, deletes the conversation or restarts the host, and a question does the same. AutoGen's
  warning is about persistence, not about time; the record is persisted.
- **Asking from anywhere but a run.** There is no question endpoint without a run, no question on the
  conversation list, and no notification off the page.
- **Per-parameter descriptions for the other twenty tools.** The capability lands in `Core`; using it
  everywhere rewrites the provider's cached prefix and is a separate job with a measurement.
- **A secret scanner over the answer.** The compose box has none, the answer is the owner's own typing,
  and a false positive would block a legitimate reply with no override. The description and the prompt
  say not to ask for secrets instead.

## The design

### D1 — One tool, named for the person it asks

`ask_owner`, described in full, with this schema:

```
questions: array (required)
  question: string (required)        one sentence, the thing you need to know
  header: string (optional)          a few words: the card's label and the answer's key
  options: array (optional)          2..MaxQuestionOptions when present
    label: string (required)
    description: string (optional)   why the owner would pick it
  multiSelect: boolean (optional)    default false
```

Why this shape, from the survey:

- **Options are optional**, so one tool covers a free-text question, a single choice and a
  multi-select. VS Code's tool makes the same choice and it is the one that removes a second tool.
- **The owner's own words are always allowed.** Free text is a card affordance in Claude Code and VS
  Code, not a schema field; a `freeText` argument is one more thing to get wrong for no gain.
- **`header` is optional** and is presentation plus a stable key, as against Claude Code's requirement
  and its answer keyed by the question's exact text.
- **Two levels of nesting**, `questions[].options[]`. `edit_page` already proves one level at this
  provider; two is the risk the plan measures (R1), and the alternative — a separate `ask_one` tool —
  doubles the tool count to solve a problem the description can prevent.

The name is `ask_owner` rather than `ask` or `ask_user` because the prompt, the memory section and the
approval prose all call the human *the owner*; the word in the name is then the word in the system
prompt. `ask_owner` is legal under DeepSeek's name rule (`a-z`, `_`, at most 128 characters).

The description is a paragraph, not a phrase: Anthropic's guidance is at least three or four sentences,
and this tool needs every one. It says what the tool does, **when to use it** (a decision that is the
owner's and changes what happens next, or a fact only they hold), **when not to** (permission, which
the approval setting already covers; anything a tool can read), that the run stops until they answer so
the call belongs alone in its step, and what the result is — their answer, in the order asked, in their
words, and the one tool result that is *not* untrusted data because it is the owner speaking. It ends
by forbidding the one thing this tool must never collect: a password, key or token, because the answer
is stored and sent to the provider.

### D2 — A question record, and a run status that names it

`AssistantToolQuestions` is `AssistantToolApprovals`' shape with the decision replaced by content:

| Column | Type | Why |
| --- | --- | --- |
| `RunId` | TEXT, key | the run that asked; cascade on the run |
| `ToolCallId` | TEXT, key | the call whose result the answer becomes |
| `Status` | TEXT | `pending`, `answered`, `failed` |
| `QuestionsJson` | TEXT | what was asked, exactly as validated, so the card and the endpoint need no re-parse of the transcript |
| `AnswerJson` | TEXT NULL | the answers in the order asked, once given |
| `CreatedAt`, `ResolvedAt`, `ResolvedBy`, `FailureCode` | | who answered and when, and why it failed |

Storing the questions is deliberate duplication of the tool-call arguments: the card, the answer
endpoint's validation and the answered card all need the parsed question set, and reading it out of an
assistant message's `tool_calls` JSON would be a second parser for a shape the host itself wrote.

The run gains `awaiting_answer`. It joins every place the active statuses are enumerated — the two
active-run queries and `IsRunning`, `RecoverInterruptedRunsAsync`, and the partial unique index on
`ConversationId` — and the migration that adds the status to the index filter is the change the
migration test proves up, down and up.

`awaiting_answer` rather than reusing `awaiting_approval`: the status is a fact a log line, a metric
and a reader rely on, and a run waiting for an answer is not awaiting approval. The cost is the
migration, which is smaller than a status that lies.

### D3 — The wait is the approval's, so the tool is given a seam, not a special case

The processor must publish, set status and wait, but naming `ask_owner` in `RunProcessor` would put a
tool's name in a second place — the fault `assistant-a-tool-is-one-type` was written to end. So:

- `ToolSpec` gains `WaitsForOwner`, a fact about the tool beside `NeedsApproval` and `Writes`.
- `ToolServices` gains `Questions`, an `IOwnerQuestions` the *run* supplies for the call.
- `PageToolExecutor.ExecuteAsync` takes that seam and hands it to the tool; a tool that does not wait
  never sees it used.
- `RunProcessor` implements `IOwnerQuestions` once per call as a private adapter over the run, the
  stream and the step. It creates the record, stores `awaiting_answer`, publishes `question.required`,
  waits on `OwnerQuestionCoordinator`, restores `running`, reads the stored answer and publishes
  `question.answered`.

`ExecuteToolAsync` skips its `ToolTimeoutSeconds` deadline for a call whose spec waits for the owner
(the run's own token still cancels it, so Stop and a restart work). Everything else about the call is
the tool's: it validates its arguments, refuses a bad set, and formats the result.

The three places that fail a pending approval closed today — `FailAsync`, the cancellation and restart
path in `RunProcessor.RunAsync`, and `RecoverInterruptedRunsAsync` — fail a pending question the same
way, so no record outlives the run that asked it and every unanswered call gets the synthetic tool
result the stop path already writes.

The coordinator is `ToolApprovalCoordinator`'s sibling with no payload: the endpoint writes the answer
and then signals, and the worker reads the store, so the answer's authority is the row and not a
message between two objects. A duplicate record is a refusal ("this call already has a question
record"), exactly as a duplicate approval is.

### D4 — The answer is the tool's result, in the order it was asked

```json
{"ok": true, "data": {"answers": [
  {"question": "Which page should the guide live on?", "header": "Page", "selected": ["/about"], "text": ""},
  {"question": "What should the heading say?", "selected": [], "text": "About us"}
]}, "error": null}
```

One entry per question, in the order asked, with the question echoed so a reader of the transcript
after a long run needs no count-back; `selected` carries the option labels chosen; `text` carries the
owner's own words, empty when they picked instead. The envelope, its bound and its truncation are
`ToolResults.Success`'s, so the answer cannot bypass the one result budget.

An answer that gives nothing for a question is refused at the endpoint rather than sent as an empty
string: an empty answer is indistinguishable from the owner never having been asked.

The answer is stored as a `tool` message with this call's id, so it is what the provider sees, and the
planner may mask it like any old tool result (`assistant-old-tool-results-are-masked`). It is not
singled out.

### D5 — A card the owner can answer, rendered from the record

Pending, on the conversation page and live from `question.required`:

- One `<section class="assistant-question">` per pending record, with a heading and the run's Stop
  button already in reach.
- One `<fieldset>` per question with a `<legend>` carrying `header` when given and the question text;
  each option a `<label>` with the label text and, when given, its description; radio buttons for a
  single choice and checkboxes for `multiSelect`.
- A text input per question, labelled "Your own answer", always present.
- The form's fields are `answer{n}` (repeated for checkboxes) and `other{n}`, so the endpoint needs no
  JSON from the browser to parse.

The live card is keyed by `data-tool-call-id` and **replaces** the stored one when the journal replays
`question.required`, the way the continuation card replaces its stored copy. This is the rule the
replay makes necessary, and the plan fixes it for the new card whether or not the approval card has the
same latent fault.

Answered, in the finished turn's block beside the changed-pages card, rendered from the records for
that run: each question with the answer beneath it (`selected` labels joined, or the owner's text),
escaped. The raw tool card stays where it is, collapsed, as for every tool. Nothing is rendered for a
`failed` record: the run's own error and the synthetic tool result already say what happened.

The page needs one new read — every question for the conversation — because a turn's card must come
from the record rather than the tool result: the result can be truncated at the cap, and an answer cut
in half is worse than no card. That read is not gated on the conversation's approval mode, as the
approvals' is, because a question is asked in both modes.

### D6 — The answer endpoint is an approval's sibling

`POST /assistant/{id:long}/runs/{runId}/tool-calls/{toolCallId}/answer`, behind the same owner filter
and antiforgery token as the approval route:

- Bound the submission first: the whole answer's text against `ContextBudget.MessageChars`, the same
  eighth of the prompt the compose box uses. An answer is an owner message by another route, so it
  gets the message's share rather than a setting of its own.
- Then validate against the **stored** `QuestionsJson`: a selection must be an option of that question,
  a question must be answered, and a repeated or absent field is refused with the accepted shape.
- The store is the arbiter, as it is for approvals: only a `pending` record on an `awaiting_answer` run
  of a conversation this owner holds can be answered. It returns answered, not found or conflict, and
  the endpoint answers `204`, `404` or `409` with the same words the approval route uses.
- On a written answer the endpoint signals the coordinator and returns `204`.

The endpoint returns JSON errors and takes a scripted submit, exactly as the approval card does; the
page's script is already required for that card, so a no-JavaScript fallback is not invented for this
one.

### D7 — The caps are configuration, and each one names itself

| Key | Shipped | Range | Why it is a policy value |
| --- | --- | --- | --- |
| `Assistant:MaxQuestionsPerCall` | 4 | 1–8 | how many the owner is asked at once, and how long the card may be |
| `Assistant:MaxQuestionOptions` | 5 | 2–10 | how many choices one question may offer before it is a menu |
| `Assistant:MaxQuestionChars` | 500 | 20–4,000 | how long one question, header, label or option description may be |

`FromConfiguration`, `appsettings.json`, the shipped/override/refusal tests and the refusal sentences
are the same shape as the other limits. The answer's length is the one bound that is not new: it is
`ContextBudget.MessageChars`, because an answer is the owner's message.

`MaxQuestionsPerCall × MaxQuestionChars` plus the option envelopes stays far inside
`MaxToolArgumentChars` for the shipped values; `AssistantOptionsValidator` gains no cross-field rule
unless the shipped values are ever set near each other.

### D8 — A parameter may describe itself, and this tool's parameters do

`ToolArgumentSchema` gains an optional `Description`; `ParametersJson` emits `"description"` only when
it is set, so no other tool's request body changes by a byte and the provider's cached prefix is
untouched for them. The `ask_owner` arguments all carry one, because the word `description` is both a
JSON-Schema keyword and an option's second line, and because every vendor's guidance says a
parameter's purpose is the model's to read.

`ToolSpecsTests` gains the invariant that a waiting tool's arguments are all described, so the next
tool of this shape cannot ship with a bare schema.

### D9 — The prompt says when, and who is speaking

`PageAuthorPrompt` gains an "Asking the owner" section and moves to `page-author-v39`:

- Ask when the decision is the owner's and the answer changes what happens next, or when they hold a
  fact no tool can read. Do not ask for permission, for anything `read_page`, the shell or `fetch`
  can answer, or for a secret.
- Ask everything in one call, one sentence per question, with options only when the choices are real
  ones; the owner can always answer in their own words.
- The run stops while the card waits; make the call alone in its step.
- **An answer is the owner speaking.** The standing rule is that tool results are untrusted data; this
  result is the exception, and it is only an answer to the question that was asked.

The prompt has no test that reads prose (a scan would match "owner" in a dozen places); its version
and the tool's presence are what the tests pin.

### D10 — What the run says about itself

- Events: `question.required` carries `{step, toolCallId, runId, questions, summary}` and
  `question.answered` carries `{step, toolCallId}`. The journal replays them in order like every other
  event, which is why the page's card is keyed and replaces rather than joins.
- Logs: one line when a question is asked (the run, the count, the characters) and one when it is
  answered (the count, the characters, the outcome). **The question's text and the answer are never
  logged and never a metric tag**, for the reason memory is not: the owner types into that box.
- Metrics: nothing new. The call is a tool call and `assistant.tool.duration` already tags it by name;
  the wait is excluded from that histogram by restarting the call's stopwatch when the answer arrives,
  so a histogram of tool time is not a histogram of how long the owner was away. `assistant.run.duration`
  stays wall-clock and includes the wait, as it already includes an approval's
  ([metrics](../metrics.md)).
- `docs/metrics.md`'s sentence about what a run's duration includes gains the answer.

### D11 — What does not change

- **No page capability and no `/help` edit.** A page author learns nothing new; the assistant's tools
  are not page capabilities, and this one is explicitly not a candidate to become one.
- **No provider change.** `tools` and `tool_choice: "auto"` are sent as today; strict mode is not
  turned on, because it would demand every argument and reject the caps.
- **No change to approvals, continuation or the budgets.** The approval path keeps its existing
  behaviour (the wait already counts as tool time there); this plan does not quietly fix that.
- **The shell route catalogue** gains the words "and the owner's answers" in the `/assistant` row's
  purpose, so the assistant's own view of the site's routes stays true.

## What it touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Core/Assistant/OwnerQuestions.cs` | **new** — `OwnerQuestion`, `OwnerOption`, `OwnerAnswer`, the validation and the wire shape, so the tool, the store, the endpoint and the page cannot drift |
| `src/uk.osric.asp.Core/Assistant/ToolArguments.cs` | **changed** — `ToolArgumentSchema.Description` |
| `src/uk.osric.asp.Web/Assistant/Tools/AskOwnerTool.cs` | **new** — the tool, its description and its arguments |
| `src/uk.osric.asp.Web/Assistant/Tools/ToolSpec.cs` | **changed** — `WaitsForOwner` |
| `src/uk.osric.asp.Web/Assistant/Tools/ToolRegistry.cs` | **changed** — emit a description when set; register the tool last |
| `src/uk.osric.asp.Web/Assistant/Tools/ToolServices.cs` | **changed** — the `Questions` seam |
| `src/uk.osric.asp.Web/Assistant/PageToolExecutor.cs` | **changed** — pass the seam to the tool |
| `src/uk.osric.asp.Web/Assistant/IOwnerQuestions.cs` | **new** — the seam the run supplies |
| `src/uk.osric.asp.Web/Assistant/OwnerQuestionCoordinator.cs` | **new** — waits and signals, with no payload |
| `src/uk.osric.asp.Web/Assistant/OwnerQuestion.cs` | **new** — the stored record |
| `src/uk.osric.asp.Web/Assistant/RunProcessor.cs` | **changed** — the adapter, the status changes, the two events, fail-closed, the timeout exemption and the tool clock |
| `src/uk.osric.asp.Web/Assistant/IConversationStore.cs`, `SqliteConversationStore.cs` | **changed** — ask, answer, read one, read a conversation's, fail pending; the active statuses; recovery |
| `src/uk.osric.asp.Web/Storage/StorageDbContext.cs`, `Migrations/*_AddAssistantQuestions.cs` | **changed, new** — the entity and the widened active-run index |
| `src/uk.osric.asp.Web/Assistant/AssistantEndpoint.cs`, `Program.cs` | **changed** — the answer route; the coordinator registered beside the approval one |
| `src/uk.osric.asp.Web/Assistant/AssistantConversationPage.cs` | **changed** — the pending card and the answered card |
| `src/uk.osric.asp.Web/Pages/Seeds/assistant.js`, `assistant.css` | **changed** — the live card, its removal, its style |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | **changed** — the section and `page-author-v39` |
| `src/uk.osric.asp.Web/Assistant/AssistantOptions.cs`, `appsettings.json` | **changed** — three limits |
| `src/uk.osric.asp.Web/Shell/ShellCatalogue.cs` | **changed** — one row's purpose |
| `test/…` | **new/changed** — the suites below |
| `docs/assistant.md`, `docs/metrics.md`, `docs/testing.md`, `docs/spikes/README.md`, `docs/jobs/README.md` | **changed** — the feature, the duration's meaning, the suites, the spike row, the job row |
| `docs/decisions.md` | **changed at the merge** — the two names written and `assistant-deadlines-bound-silence` edited |

## Phases

1. **The values.** `OwnerQuestions` in `Core` with its validation and wire shape, and `Core.Tests`
   proving the caps, the refusals and the round trip. No Web code yet.
2. **The argument description.** `ToolArgumentSchema.Description`, the registry emitting it only when
   set, and the test that another tool's schema is byte-identical.
3. **The tool.** `AskOwnerTool`, `ToolSpec.WaitsForOwner`, the `Questions` seam and a fake asker in
   `Web.Tests`; the three configuration values, the shipped file and the options tests.
4. **The record.** The entity, the migration, the store methods and the statuses, with the store and
   migration suites. Nothing asks a question yet.
5. **The wait.** The coordinator, the processor's adapter, the events, fail-closed and the timeout and
   metric rules, proved in `RunProcessorTests` with a scripted provider.
6. **The route.** The endpoint and its four answers, proved in `Web.IntegrationTests`.
7. **The card.** The server's two renderers, the script's live card and the stylesheet; transcript
   tests, then the browser suite.
8. **The words.** The prompt and its version, `assistant.md`, `metrics.md`, `testing.md`, the shell
   catalogue and the two decision entries at the merge.

## Tests

**`Core.Tests` — `OwnerQuestionsTests`** (new)

- `AQuestionSetOverTheQuestionLimitIsRefusedAndNamesTheLimit`
- `AnOptionListOverTheLimitIsRefusedAndNamesTheLimit`
- `AQuestionOverTheCharacterLimitIsRefusedRatherThanCut`
- `AnOptionWithNoLabelIsRefused`
- `TwoOptionsOfOneQuestionWithTheSameLabelAreRefused`
- `TheAnswersAreReadBackInTheOrderTheyWereAsked`
- `AnAnswerThatChoosesNothingAndSaysNothingIsRefused`
- `TheWireShapeRoundTripsThroughItsOwnParser`

**`Web.Tests` — `ToolSpecsTests`** (changed)

- `TheRegistryHoldsEveryTool` — twenty-one.
- `OnlyTheQuestionToolWaitsForTheOwner`
- `AWaitingToolIsNotApprovalGated`
- `EveryArgumentOfAWaitingToolIsDescribed`
- `AToolWithNoArgumentDescriptionsIsUnchangedOnTheWire` — the schema of, say, `read_page` is the same
  JSON before and after the feature.

**`Web.Tests` — `AskOwnerToolTests`** (new)

- `TheAnswerIsTheToolResult`
- `AQuestionSetWithNoQuestionsIsRefused`
- `TheQuestionsTheToolAsksAreTheOnesItValidated`

**`Web.Tests` — `OwnerQuestionCoordinatorTests`** (new)

- `SignalWakesTheMatchingWaiter`
- `SignalForAnUnknownCallWakesNothing`
- `CancellationStopsWaitingAndRemovesThePendingSignal`

**`Web.Tests` — `RunProcessorTests`** (changed)

- `AQuestionWaitsForTheOwnerAndItsAnswerIsTheToolResult` — the status is `awaiting_answer` between the
  two events, and the stored tool message carries the answer.
- `AQuestionIsNotCutOffByTheToolTimeout` — a wait longer than the configured tool timeout still ends
  with the answer.
- `AStoppedQuestionLeavesARefusalForItsCall` — cancelling while the card waits writes the synthetic
  tool result and fails the record.
- `TheOwnersWaitIsNotCountedAsToolTime` — `assistant.tool.duration` for the call is the tool's own
  work, while the run's duration includes the wait.

**`Web.Tests` — `ConversationStoreTests`** (changed)

- `AQuestionIsAnsweredOnlyByAnOwnerWhoseRunIsWaiting`
- `AnAnsweredQuestionCannotBeAnsweredTwice`
- `ARunAwaitingAnAnswerHoldsTheConversation` — a second run is refused.
- `RecoveringARunFailsItsPendingQuestion`

**`Web.Tests` — `AssistantQuestionMigrationTests`** (new)

- `TheMigrationAddsTheTableAndAdmitsTheNewStatusToTheActiveRunIndex`
- `TheDownMigrationDropsTheTableAndRestoresTheIndex`

**`Web.Tests` — `AssistantOptionsTests`** (changed)

- `AQuestionLimitOutsideItsRangeStopsTheSite`

**`Web.Tests` — `AssistantTranscriptTests`** (changed)

- `APendingQuestionRendersOneFieldsetPerQuestionWithALabelForEveryOption`
- `AnAnsweredQuestionReadsAsTheQuestionAndTheOwnersAnswer`
- `TheOwnersAnswerIsEscaped`

**`Web.IntegrationTests` — `AssistantQuestionFlowTests`** (new)

- `AQuestionPausesTheRunUntilTheOwnerAnswers` — the event, the card on a reload, the answer, the next
  request carrying the answer, `run.completed`.
- `TheQuestionCardSurvivesAReloadWhileTheRunWaits`
- `AnAnswerToASettledQuestionIsRefused`
- `AnotherAccountsAnswerIsNotFound`
- `AnAnswerOverTheMessageBudgetIsRefused`

**`Web.BrowserTests` — `AssistantQuestionTests`** (new). These are browser tests because the card's
live behaviour lives only in the page.

- `AQuestionCardIsAnsweredAndTheRunContinues` — the card appears, a radio and some typed text are
  chosen, and the run's answer follows.
- `TheCardIsRebuiltByAReloadAndOnlyOnce` — reload mid-wait; one card, one form.
- `TheAnsweredQuestionReadsBackAfterTheRun`

**`Web.BrowserTests` — `AssistantLiveProviderTests`** (changed, explicit)

- `ARealModelAsksAQuestionAndUsesTheAnswer` — the request asks the model to ask which of two page names
  to use, the test answers, and the page is written with that name. This is the one test that proves
  DeepSeek fills a two-level schema, which is the survey's remaining uncertainty.

## Risks

- **R1 — DeepSeek and two levels of nesting.** `edit_page` proves one level at this provider; nothing
  proves `questions[].options[]` until the live test runs. The mitigations are the parameter
  descriptions, the refusal that names the accepted shape, and the fallback of a flat `options` array
  per question if it fails. The live test is the measurement, not an assumption.
- **R2 — The twenty-first tool.** OpenAI's guidance is to keep the initial set under twenty; the
  registry crosses that line. The description and the prompt's section are the mitigation, and the
  cost of getting it wrong is visible: `ask_owner` called where a tool should have read, or a tool
  call that the model no longer chooses. Worth watching in the transcript before the next tool is
  added.
- **R3 — A run that waits forever.** Deliberate, and the same as an approval: Stop, delete the
  conversation, or restart. The card says the run is stopped by the Stop button, and the page's status
  line says it is waiting.
- **R4 — Duplicate cards on replay.** The card replaces rather than joins, and the browser test asserts
  one. If the approval card's older behaviour turns out to duplicate, that is a separate defect with
  the same one-line fix, not a reason to copy it here.
- **R5 — The answer is not a secret.** It is stored and sent to the provider like a message. Nothing
  scans it; the description and the prompt forbid asking for one, and the docs say the transcript
  carries the answer.
- **R6 — A long answer is masked later.** It is a tool result, so the planner may elide it in an old
  turn (`assistant-old-tool-results-are-masked`). The record keeps it for the page; the model may have
  to rely on what it wrote down.

## The questions this plan asks the owner

1. **Is `ask_owner` the right name?** Recommended yes: "owner" is the word the prompt already uses for
   the human. `ask` is shorter and `ask_user` is the name most other frameworks use; if either is
   preferred, now is the time, because a tool name is in the transcript, the prompt and the docs.
2. **Batch or one question at a time?** Recommended: up to four in one call, with the card asking them
   together (Claude Code and VS Code both do; Cline insists on one). Batching saves a provider round
   trip per question but makes a four-question card.
3. **Should an unanswered question be skippable?** Recommended no: every question must get a selection
   or text, and the owner can stop the run or type "I don't know". A `skipped` flag (VS Code's) is a
   second state the model must interpret.
4. **Should the wait be excluded from `assistant.tool.duration`?** Recommended yes, for this tool; and
   the approval path is left alone in this change even though it has the same fault, so the ledger
   stays honest about what changed.
5. **Should parameter descriptions be added to every tool?** Recommended not in this change: it
   rewrites the provider's cached prefix and deserves its own measurement. The capability lands now so
   the follow-up is a data change.
6. **Should the run fail both records through one store call?** The processor fails pending
   approvals in three places (`FailAsync`, the stop path, recovery); the plan adds the same three for
   questions, which is six call sites that must never drift apart. One `FailPendingOwnerAsksAsync(runId, code)`
   that fails both tables would be a genuine reduction with no discriminator, at the cost of a store
   method that knows there are two kinds. Recommended yes, and it is the part of the "one kind of
   thing" idea that pays.

## Considered and rejected

- **Reusing `awaiting_approval`.** Saves a migration and makes a log line, a metric and the page lie
  about what the run waits for. The migration is a table and an index filter.
- **Widening `ToolApproval` into one "owner interrupt" with a kind.** The nearest miss, and worth
  stating at length because the duplication is real. What the two paths share is the *wait*: a record,
  a coordinator, a status the run sits in, and a fail-closed path on a restart. What they do not share
  is the *consequence*: an approval decides whether a tool runs and a denial is a completed
  interaction, while an answer is the result a tool produces and nothing was gated. A unified record
  is `{kind, request, resolution}` with one status (`awaiting_owner`) and one pending query; that is
  LangGraph's `interrupt(value, response_schema)` and it is coherent. But here it costs more than it
  saves: the migration alters a live table rather than adding one; every reader branches anyway,
  because the record's payload and the page's card and the endpoint's form are all different shapes;
  one status means renaming `awaiting_approval`, which is enumerated in five code sites and pinned by
  tests, docs and the page — a bigger diff than adding `awaiting_answer`; and it puts ~150 lines of
  working, tested approval code in front of a second caller. Measured, the shared plumbing is about
  120–150 lines beside a feature of roughly 650–700 including tests, so the saving is a fifth of the
  feature's plumbing and none of its semantics. The framework survey is the tie-breaker: MCP keeps
  elicitation out of tool-call approval, and Claude Code, VS Code, Cline and Roo Code all ship
  separate tools. Revisit if a third kind arrives — a confirmation carrying text, say — because that
  is the evidence the generalisation is real rather than anticipated.
- **Passing the answer through the coordinator.** The endpoint's signal would carry the words and the
  store would hold a copy. Reading the row makes the record the authority and survives a restart of
  the HTTP request's task.
- **Asking from the tool itself, with the wait in `ToolServices` as a service.** It puts the run's
  lifecycle in the tool and makes the tool timeout a rule the tool has to know about. The seam keeps
  the wait in the run and the schema in the tool.
- **A flat MCP-shaped form.** It solves a problem this site does not have — an unknown client — at the
  cost of the card.
- **One question per call.** Three unknowns would cost three steps and three waits.
- **An answer endpoint that takes the answer as JSON from the page.** The page would own a second
  serializer for a shape the server validates; named form fields with indices need none.
- **Rendering the answered card from the tool result.** The result is cut at the tool-result cap, so a
  long answer would read as a half answer. The record is complete.
- **A secret scan over the answer.** The compose box has none, and a false positive has no override.
- **A timeout, an auto-answer, or an "away" reply.** Reported by the survey as the one thing every
  framework adds and every framework regrets: they answer for the owner.

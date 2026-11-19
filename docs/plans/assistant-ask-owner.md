<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The assistant asks the owner a question and waits for the answer

| | |
| --- | --- |
| Status | Built 2026-11-19 — the owner's yes was given and the work is on its branch, awaiting the merge. The decision entry is written at the merge |
| Size | Medium — one tool, one interrupt kind, a card in two renderers, three configuration values, a prompt version and five suites. The wait, the record, the status and the route are [the owner interrupt's](archive/assistant-owner-interrupt.md) |
| Area | Core (Assistant), Web (assistant), docs |
| Spike | [The ask tool's shape](../spikes/ask-tool-shape.md) read MCP elicitation, Claude Code, VS Code, Cline, Roo Code, OpenAI, DeepSeek, LangGraph and AutoGen; it recommends the schema and the caps this plan adopts, and names the two levers — the description and the parameter descriptions — that the evidence says matter most. The plan adds one thing it would not: a question the owner may skip (D4) |
| Backlog | [`cannot-ask-the-owner`](../backlog/cannot-ask-the-owner.md) |
| Depends on | [The owner interrupt](archive/assistant-owner-interrupt.md) (ARCH-17), landed 2026-11-18 |
| Decisions | New name proposed: **assistant-a-question-is-a-tool-call**. Relies on `assistant-a-run-waits-on-one-owner-interrupt` and `assistant-a-tool-is-one-type`; edits nothing |

## The short version

The assistant can read, write, fetch and remember, and it can hear the owner at the start of a task and
at a gate. A run that needs one fact only the owner holds — which of two page names, what a heading
should say, whether a section is still wanted — has no way to ask. It guesses, or it ends its turn
with the question in prose and loses the work it had queued behind the answer.

This adds the missing turn: a tool, `ask_user`, whose **result is the owner's answer**. It is the third
kind of [owner interrupt](archive/assistant-owner-interrupt.md), so the wait already exists: the run opens a
question interrupt, the owner's card is rendered from the stored request, their answer is the stored
resolution, and the run carries on in the same step. What this plan adds is the kind, the tool, the
card, the prompt and three limits. The owner chose the name `ask_user` and no alias for it (D1).

The survey found no cross-vendor standard, but a strong convergence — one tool, one to four question
objects, options of a label and a description, free text always allowed, hard caps — and two levers
the vendor documentation agrees on more strongly than any schema: a description that says *when to use
it and when not*, and a description on each parameter. This site's tool schema emits no parameter
descriptions at all, so the plan adds the first one, for this tool's arguments only.

Four choices are load-bearing:

1. **A question is not an approval.** It is not gated by `RequireApproval`, it has no yes/no, and its
   resolution is content rather than a decision. Only the wait is shared.
2. **The answer is the tool's result**, read from the interrupt record after the run wakes — so a
   disconnected browser, a reload or a second device cannot change the outcome, and a restart fails the
   question closed like any other interrupt.
3. **The wait is the owner's, so no clock judges it.** The tool timeout does not apply, the silence
   clock is already stopped by the shared wait, and the question's wait is not counted as tool time.
4. **A question may be left blank.** The card is one form holding every question, so the owner can read
   the whole set before answering any of it, and a question they skip is carried to the model as an
   empty answer rather than refused or flagged (D4).

## What exists when this starts

- **The interrupt.** `AssistantInterrupts` stores `{kind, request, resolution}` with at most one pending
  per run; `awaiting_owner` is the run's status; `OwnerInterruptCoordinator` and one `WaitAsync` in
  `RunProcessor` serve every kind; `POST …/runs/{runId}/interrupt` resolves the run's pending one.
- **The tool surface.** Twenty tools in one registry; each carries its name, description, schema and
  flags, and `ToolSpecsTests` pins the count and the invariants.
- **The bounded schema.** `ToolArgumentSchema.Items` recurses and `ToolArguments.Parse` validates
  nested arrays of objects — `edit_page`'s `edits` proves it at this provider. `ParametersJson` emits
  `type`, `properties`, `required` and `additionalProperties: false`, and nothing else.
- **The page.** One read of the run's pending interrupt, rendered by kind; the turn's cards are read
  from stored rows (changed pages) and records.

## Goals

1. `ask_user` answers the owner's reply as this call's result, in the order asked, so the run continues
   in the same step with nothing to reconstruct.
2. The card asks every question the model sent, with the options it offered, and always offers the
   owner's own words. It is usable from the keyboard and readable by a screen reader.
3. The owner reads the whole card before answering any of it and may leave any question blank; a blank
   answer is carried to the model as the answer it is, with no second state to interpret.
4. The question, its options and the answer are bounded by configuration, and every cap names itself in
   its refusal.
5. The model is told when to ask, when not to, what the result means, and that the answer is the owner
   speaking. The prompt says so too, and its version moves.
6. Nothing else moves: no page capability, no `/help` edit, no provider change, no new status, route
   family, coordinator or table.

## Non-goals

- **A page capability.** `ctx.AskAsync(...)` would be a page that blocks its render on a visitor. If
  that is ever wanted it is a decision and an `IDEA-` of its own.
- **An approval that carries text.** Approvals stay yes/no.
- **A timeout, an auto-answer or an "away" mode.** An interrupt waits until the owner stops the run,
  deletes the conversation or restarts the host.
- **A secret scanner over the answer.** The compose box has none, the answer is the owner's own typing,
  and a false positive would block a legitimate reply with no override. The description and the prompt
  say not to ask for secrets instead.
- **Per-parameter descriptions for the other twenty tools.** The capability lands in `Core`; using it
  everywhere rewrites the provider's cached prefix and is a separate job with a measurement —
  [`tool-arguments-say-nothing`](../backlog/tool-arguments-say-nothing.md).

## The design

### D1 — One tool, named the way the other frameworks name it

`ask_user`, described in full, with this schema:

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

- **Options are optional**, so one tool covers a free-text question, a single choice and a multi-select.
  VS Code's tool makes the same choice and it is the one that removes a second tool.
- **The owner's own words are always allowed.** Free text is a card affordance in Claude Code and VS
  Code, not a schema field; a `freeText` argument is one more thing to get wrong for no gain.
- **`header` is optional** and is presentation plus a stable key, as against Claude Code's requirement
  and its answer keyed by the question's exact text.
- **Two levels of nesting**, `questions[].options[]`. `edit_page` already proves one level at this
  provider; two is the risk this plan measures (R1), and the alternative — a separate `ask_one` tool —
  doubles the tool count to solve a problem the description can prevent.

The name is `ask_user`, the owner's choice on 2026-11-18 against the survey's recommendation of
`ask_owner` (which matched the word the prompt, the memory section and the interrupt prose all use for
the human). It is the name most other frameworks give this tool, and so the one the owner has already
answered under. The description's first sentence still calls the human *the owner*, so the model reads
one word for the person and the other appears only in the name. **There is no alias.** A second name is
either a second tool — twenty-two, for a choice the model makes once — or an indirection that hides
which name the model was actually offered; nothing on this side reads the name, and the description is
where the model is told who it is asking. `ask_user` is legal under DeepSeek's name rule (`a-z`, `_`,
at most 128 characters).

The description is a paragraph, not a phrase: Anthropic's guidance is at least three or four sentences,
and this tool needs every one. It says what the tool does, **when to use it** (a decision that is the
owner's and changes what happens next, or a fact only they hold), **when not to** (permission, which
the approval setting already covers; anything a tool can read), that the run stops until they answer so
the call belongs alone in its step, and what the result is — their answer, in the order asked, in their
words, and the one tool result that is *not* untrusted data because it is the owner speaking. It says
that a question left blank is the owner leaving it to the model, so the model decides and says what it
assumed rather than asking again. It ends by forbidding the one thing this tool must never collect: a
password, key or token, because the answer is stored and sent to the provider.

### D2 — The question kind

The interrupt gains one kind, with no change to the record, the status, the coordinator or the wait:

| | |
| --- | --- |
| Kind | `question` |
| `RequestJson` | `{questions: [{question, header?, options?: [{label, description?}], multiSelect?}]}` — exactly what was validated |
| `ResolutionJson` | `{answers: [{question, header?, selected: [label], text}]}` — one entry per question, in the order asked, empty where the owner skipped it |
| The run does | nothing beyond the call: the answer *is* the tool's result |

The answer endpoint is the interrupt route with no kind in the path: the record says the run waits on
a `question`, and the handler parses the form against it. The form's fields are `answer{n}` (repeated for
checkboxes) and `other{n}`, so the endpoint needs no JSON from the browser, and the tool call id travels
in the form as a stale-form guard. A question with neither field set is stored as an empty answer rather
than refused, so a form with no field set at all is a form in which every question was skipped.

### D3 — The tool is given the wait, not the run's business

- `ToolSpec` gains `WaitsForOwner`, a fact about the tool beside `NeedsApproval` and `Writes`.
- `ToolServices` gains a `Questions` seam — an `IOwnerQuestions` the run supplies for the call — and
  `PageToolExecutor.ExecuteAsync` passes it through.
- `RunProcessor` implements the seam as a thin adapter over the `WaitAsync` the interrupt plan built.
  A tool that does not wait never sees it used.
- `ExecuteToolAsync` skips its `ToolTimeoutSeconds` deadline for a call whose spec waits for the owner.
  The run's own token still cancels it, so Stop and a restart work.

Naming `ask_user` in `RunProcessor` would put a tool's name in a second place, which is the fault
`assistant-a-tool-is-one-type` was written to end. Everything else about the call is the tool's: it
validates its arguments, refuses a bad set and formats the result.

### D4 — The answer is the tool's result, in the order it was asked

```json
{"ok": true, "data": {"answers": [
  {"question": "Which page should the guide live on?", "header": "Page", "selected": ["/about"], "text": ""},
  {"question": "What should the heading say?", "selected": [], "text": "About us"}
]}, "error": null}
```

One entry per question, in the order asked, with the question echoed so a reader of the transcript after
a long run needs no count-back; `selected` carries the option labels chosen; `text` carries the owner's
own words, empty when they picked instead. The envelope, its bound and its truncation are
`ToolResults.Success`'s, so the answer cannot bypass the one result budget.

**The owner may skip.** A question with nothing selected and nothing typed is stored as an empty answer
— `selected: []`, `text: ""` — and a submission in which every question is empty is an answer like any
other. There is no `skipped` flag (the owner's answer, 2026-11-18): the empty answer *is* the answer,
it is what the owner did, and a second state would be one more thing the model has to interpret and the
record has to keep in step. What an empty answer means is said in the description and the prompt — the
owner left the decision with you, so decide, say what you assumed, and do not ask it again — because
that is a reading of the answer, not a field of it.

This is the one place the plan departs from the survey, which would have refused the empty answer because
it could not tell it from a question never asked. It can: the run waited, the card was shown, and the
entry exists.

The answer is stored as a `tool` message with this call's id, so it is what the provider sees, and the
planner may mask it like any old tool result (`assistant-old-tool-results-are-masked`). It is not
singled out.

### D5 — Two cards, from the record

**Pending**, rendered by the interrupt's existing renderer:

- One `<section class="assistant-question">`, one `<fieldset>` per question with a `<legend>` carrying
  `header` when given and the question text; each option a `<label>` with its label text and, when
  given, its description; radio buttons for a single choice and checkboxes for `multiSelect`.
- A text input per question, labelled "Your own answer", always present.
- Nothing in the card is required. The form holds every question at once, so the owner can read the
  whole set before answering any of it, and submit with any or all of them left as they are. No
  `required` attribute, no client-side check, no "answer every question" line: a blank is an answer.
- The live card is built by the one `interrupt.required` listener and keyed by
  `data-interrupt-kind`, so a replayed event replaces the server's card rather than joining it.

**Answered**, in the finished turn's block beside the changed-pages card, from the resolved records for
that run: each question with the answer beneath it (the chosen labels, or the owner's text), escaped. A
question the owner skipped reads *No answer* beneath it, so the card says the question was put to them
rather than leaving a gap that reads as a rendering fault.
The raw tool card stays where it is, collapsed, as for every tool. Nothing renders for a `failed`
record: the run's own error and the synthetic tool result say what happened.

The page's one read of pending interrupts is joined by one read of the conversation's resolved question
interrupts, because a turn's card must come from the record rather than the tool result: the result can
be truncated at the cap, and an answer cut in half is worse than no card.

### D6 — The answer's bounds

- The **questions** are bounded by `Core`'s `OwnerQuestions` against the configured caps: at most
  `MaxQuestionsPerCall`, at most `MaxQuestionOptions` per question, at most `MaxQuestionChars` for any
  question, header, label or option description. A refusal names the cap and the accepted shape.
- The **answer** is bounded by `ContextBudget.MessageChars` — an answer is an owner message by another
  route, so it gets the message's share rather than a setting of its own — and the endpoint refuses an
  over-long submission with the figure.
- The **tool result** is bounded by the one result cap like every other.

| Key | Shipped | Range | Why it is a policy value |
| --- | --- | --- | --- |
| `Assistant:MaxQuestionsPerCall` | 4 | 1–8 | how many the owner is asked at once, and how long the card may be |
| `Assistant:MaxQuestionOptions` | 5 | 2–10 | how many choices one question may offer before it is a menu |
| `Assistant:MaxQuestionChars` | 500 | 20–4,000 | how long one question, header, label or option description may be |

`FromConfiguration`, `appsettings.json`, the shipped/override/refusal tests and the refusal sentences are
the same shape as the other limits. The shipped product stays far inside `MaxToolArgumentChars`.

### D7 — A parameter may describe itself, and this tool's parameters do

`ToolArgumentSchema` gains an optional `Description`; `ParametersJson` emits `"description"` only when it
is set, so no other tool's request body changes by a byte and the provider's cached prefix is untouched
for them. The `ask_user` arguments all carry one, because the word `description` is both a JSON-Schema
keyword and an option's second line, and because every vendor's guidance says a parameter's purpose is
the model's to read. `ToolSpecsTests` pins that a waiting tool's arguments are all described.

### D8 — The prompt says when, and who is speaking

`PageAuthorPrompt` gains an "Asking the owner" section and moves to `page-author-v44`:

- Ask when the decision is the owner's and the answer changes what happens next, or when they hold a
  fact no tool can read. Do not ask for permission, for anything `read_page`, the shell or `fetch` can
  answer, or for a secret.
- Ask everything in one call, one sentence per question, with options only when the choices are real
  ones; the owner can always answer in their own words.
- The run stops while the card waits; make the call alone in its step.
- **An answer is the owner speaking.** The standing rule is that tool results are untrusted data; this
  result is the exception, and it is only an answer to the question that was asked.
- **A skipped question is an answer too.** If a question comes back with nothing chosen and nothing
  typed, the owner left it to you: decide, say what you assumed in your next message, and do not ask it
  a second time in the same run.

The prompt has no test that reads prose (a scan would match "owner" in a dozen places); its version and
the tool's presence are what the tests pin.

### D9 — What the run says about itself

- **Events** are the interrupt's; nothing new. The page's one listener dispatches `question` to the card
  builder.
- **Logs**: one line when a question is asked (the run, the count, the characters) and one when it is
  answered (the count, the characters, how many were left blank and the outcome). **The question's text
  and the answer are never logged and never a metric tag**, for the reason memory is not: the owner
  types into that box.
- **Metrics**: nothing new. The call is a tool call tagged by name; the owner's wait is excluded from
  `assistant.tool.duration` by restarting the call's stopwatch when the answer arrives, so that
  histogram is tool time and not how long the owner was away. `assistant.run.duration` stays
  wall-clock and already includes the wait.
- `docs/metrics.md`'s sentence about what a run's duration includes already names the interrupt; it
  needs no second edit.

## What it touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Core/Assistant/OwnerQuestions.cs` | **new** — the question, option and answer records, their validation and their wire shape, so the tool, the store, the endpoint and the page cannot drift |
| `src/uk.osric.asp.Core/Assistant/ToolArguments.cs` | **changed** — `ToolArgumentSchema.Description` |
| `src/uk.osric.asp.Web/Assistant/Tools/AskUserTool.cs` | **new** — the tool, its description and its arguments |
| `src/uk.osric.asp.Web/Assistant/Tools/ToolSpec.cs`, `ToolRegistry.cs`, `ToolServices.cs` | **changed** — `WaitsForOwner`, the emit-when-set description, the registration, the seam |
| `src/uk.osric.asp.Web/Assistant/PageToolExecutor.cs` | **changed** — pass the seam |
| `src/uk.osric.asp.Web/Assistant/IOwnerQuestions.cs` | **new** — the seam the run supplies |
| `src/uk.osric.asp.Web/Assistant/OwnerInterrupt.cs`, `RunProcessor.cs` | **changed** — the `question` kind and its adapter; the timeout exemption |
| `src/uk.osric.asp.Web/Assistant/IConversationStore.cs`, `SqliteConversationStore.cs` | **changed** — the answer resolver and the resolved-question read |
| `src/uk.osric.asp.Web/Assistant/AssistantEndpoint.cs` | **changed** — the `question` kind's fields and validation |
| `src/uk.osric.asp.Web/Assistant/AssistantConversationPage.cs` | **changed** — the question renderer and the answered card |
| `src/uk.osric.asp.Web/Pages/Seeds/assistant.js`, `assistant.css` | **changed** — the card builder and its style |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | **changed** — the section and `page-author-v44` |
| `src/uk.osric.asp.Web/Assistant/AssistantOptions.cs`, `appsettings.json` | **changed** — three limits |
| `src/uk.osric.asp.Web/Shell/ShellCatalogue.cs` | **changed** — the `/assistant` row's purpose gains the answers |
| `test/…` | **new/changed** — the suites below |
| `docs/assistant.md`, `docs/glossary.md`, `docs/testing.md` | **changed** — the tool, the glossary's "Question", the suites |
| `docs/decisions.md` | **changed at the merge** — **assistant-a-question-is-a-tool-call** written |
| `docs/jobs/`tool-arguments-say-nothing`.md`, `docs/jobs/README.md` | **new/changed** — the per-argument descriptions the survey wants and this change does not make |

## Phases

1. **The values.** `OwnerQuestions` in `Core` with its validation and wire shape, and `Core.Tests`
   proving the caps, the refusals and the round trip.
2. **The argument description.** `ToolArgumentSchema.Description`, the registry emitting it only when
   set, and the test that another tool's schema is byte-identical.
3. **The tool.** `AskUserTool`, `WaitsForOwner`, the `Questions` seam and a fake asker in `Web.Tests`;
   the three configuration values, the shipped file and the options tests.
4. **The kind.** The `question` value, its request and resolution, the answer resolver's validation and
   the store read for the page, with the store's cases.
5. **The route.** The `question` kind's fields and refusals in the shared handler, proved in
   `Web.IntegrationTests`.
6. **The card.** The server's renderer, the answered card, the script's builder and the stylesheet;
   transcript tests, then the browser suite.
7. **The words.** The prompt and its version, `assistant.md`, the glossary, `testing.md`, the shell
   catalogue, the `tool-arguments-say-nothing` job and the decision entry at the merge.

## Tests

**`Core.Tests` — `OwnerQuestionsTests`** (new)

- `AQuestionSetOverTheQuestionLimitIsRefusedAndNamesTheLimit`
- `AnOptionListOverTheLimitIsRefusedAndNamesTheLimit`
- `AQuestionOverTheCharacterLimitIsRefusedRatherThanCut`
- `AnOptionWithNoLabelIsRefused`
- `TwoOptionsOfOneQuestionWithTheSameLabelAreRefused`
- `TheAnswersAreReadBackInTheOrderTheyWereAsked`
- `AQuestionLeftBlankIsCarriedAsAnEmptyAnswer`
- `AnAnswerSetInWhichEveryQuestionWasSkippedIsCarried`
- `TheWireShapeRoundTripsThroughItsOwnParser`

**`Web.Tests` — `ToolSpecsTests`** (changed)

- `TheRegistryHoldsEveryTool` — twenty-one.
- `OnlyTheQuestionToolWaitsForTheOwner`
- `AWaitingToolIsNotApprovalGated`
- `EveryArgumentOfAWaitingToolIsDescribed`
- `AToolWithNoArgumentDescriptionsIsUnchangedOnTheWire` — `read_page`'s schema is the same JSON.

**`Web.Tests` — `AskUserToolTests`** (new)

- `TheAnswerIsTheToolResult`
- `AQuestionSetWithNoQuestionsIsRefused`
- `TheQuestionsTheToolAsksAreTheOnesItValidated`
- `ASkippedQuestionIsAnEmptyEntryInTheResult`

**`Web.Tests` — `ConversationStoreTests`** (changed)

- `AQuestionIsAnsweredOnlyByAnOwnerWhoseRunIsWaiting`
- `AnAnsweredQuestionCannotBeAnsweredTwice`
- `RecoveringARunFailsItsPendingQuestion`

**`Web.Tests` — `RunProcessorTests`** (changed)

- `AQuestionWaitsForTheOwnerAndItsAnswerIsTheToolResult`
- `AQuestionIsNotCutOffByTheToolTimeout`
- `AStoppedQuestionLeavesARefusalForItsCall`
- `TheOwnersWaitIsNotCountedAsToolTime`

**`Web.Tests` — `AssistantOptionsTests`** (changed)

- `AQuestionLimitOutsideItsRangeStopsTheSite`

**`Web.Tests` — `AssistantTranscriptTests`** (changed)

- `APendingQuestionRendersOneFieldsetPerQuestionWithALabelForEveryOption`
- `AnAnsweredQuestionReadsAsTheQuestionAndTheOwnersAnswer`
- `ASkippedQuestionReadsAsNoAnswer`
- `TheOwnersAnswerIsEscaped`

**`Web.IntegrationTests` — `AssistantQuestionFlowTests`** (new)

- `AQuestionPausesTheRunUntilTheOwnerAnswers` — the event, the card on a reload, the answer, the next
  request carrying it, `run.completed`.
- `TheQuestionCardSurvivesAReloadWhileTheRunWaits`
- `AnAnswerToASettledQuestionIsRefused`
- `AnOwnerMaySkipEveryQuestionAndTheRunContinues`
- `AnotherAccountsAnswerIsNotFound`
- `AnAnswerOverTheMessageBudgetIsRefused`

**`Web.BrowserTests` — `AssistantQuestionTests`** (new). These are browser tests because the card's live
behaviour lives only in the page.

- `AQuestionCardIsAnsweredAndTheRunContinues`
- `TheCardIsRebuiltByAReloadAndOnlyOnce` — one card, one form.
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
- **R2 — The twenty-first tool.** OpenAI's guidance is to keep the initial set under twenty; the registry
  crosses that line. The description and the prompt's section are the mitigation, and the cost of
  getting it wrong is visible: `ask_user` called where a tool should have read, or a tool call the
  model no longer chooses.
- **R3 — A run that waits forever.** Deliberate, and the same as every interrupt: Stop, delete the
  conversation, or restart. The card sits beside the Stop button and the status line says it is waiting.
- **R4 — The answer is not a secret.** It is stored and sent to the provider like a message. Nothing
  scans it; the description and the prompt forbid asking for one, and the docs say the transcript
  carries the answer.
- **R5 — A long answer is masked later.** It is a tool result, so the planner may elide it in an old turn.
  The record keeps it for the page; the model may have to rely on what it wrote down.
- **R6 — A skipped answer reads as an answer the model ignores.** The contract is prose, not schema: a
  model that treats `selected: []` and `text: ""` as "no answer yet" may ask again, and the run pays a
  second wait. The description, the prompt's bullet and the live test are the measures; the alternative
  was a flag, which is the state the owner ruled out.

## The owner's answers

The plan asked five questions on 2026-11-18; these are the answers, and this revision carries them.

| # | Question | Answer |
| --- | --- | --- |
| 1 | Is `ask_owner` the right name? | `ask_user`, and no alias. The owner prefers the name the other frameworks use; the alias was weighed and dropped, for the reasons D1 gives. |
| 2 | Batch or one at a time? | Batch, up to four in one call. The owner has met seven or eight questions at a time before, so four is not a long card; the accepted range reaches eight. |
| 3 | Should an unanswered question be skippable? | Yes, without a flag: the owner wants to read the whole set before answering any of it, and to be able to leave a question. An empty answer is an answer (D4). |
| 4 | Should the wait be excluded from `assistant.tool.duration`? | Yes — "waiting for the user is not the assistant's budget" (D9). |
| 5 | Should parameter descriptions be added to every tool? | Not in this change, and it is written down: [`tool-arguments-say-nothing`](../backlog/tool-arguments-say-nothing.md). |

## Considered and rejected

- **An alias for `ask_owner`.** A second name is either a second tool — twenty-two, for one choice the
  model makes once — or a translation step that hides which name the model was offered. Nothing here
  reads the name, and the description is where the model is told who it is asking.

- **A second wait mechanism for questions.** That is what this plan would have been before the interrupt;
  the third copy of a coordinator, a status, a table and a recovery.
- **Passing the answer through the coordinator.** The row is the authority; a signal with a payload is a
  second copy that can disagree.
- **Asking from the tool with the wait as a service.** It puts the run's lifecycle in the tool and makes
  the tool timeout a rule the tool has to know about. The seam keeps the wait in the run and the schema
  in the tool.
- **A flat MCP-shaped form.** It solves a problem this site does not have — an unknown client — at the
  cost of the card.
- **One question per call.** Three unknowns would cost three steps and three waits.
- **An answer endpoint that takes the answer as JSON from the page.** The page would own a second
  serializer for a shape the server validates; named form fields with indices need none.
- **Rendering the answered card from the tool result.** The result is cut at the tool-result cap, so a
  long answer would read as a half answer. The record is complete.
- **A secret scan over the answer.** The compose box has none, and a false positive has no override.
- **A timeout, an auto-answer or an "away" reply.** The one thing every framework adds and every
  framework regrets: they answer for the owner.

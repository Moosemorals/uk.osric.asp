<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The budget panel keeps up while a step runs

| | |
| --- | --- |
| Status | Merged 2026-11-18 — built on *assistant-live-figures* and merged into trunk with `make check` and `make test-browser` green |
| Size | Medium — the page's projection and its mark, a small server change to what the Tool calls row counts and when it is published, the store method behind it, five tests and the words in three docs. No event type, no migration, no configuration |
| Area | Web (assistant, storage), docs |
| Spike | [DeepSeek token counting](../spikes/deepseek-token-counting.md) measured the prose ratio the projection leans on; nothing new is measured |
| Backlog | [IDEA-051](../jobs/IDEA-051.md) |
| Decisions | Recorded at the merge: `assistant-tool-calls-count-attempts` and `assistant-live-figures-are-marked`. Edits `assistant-budgets-are-one-snapshot`. Relies on `assistant-the-request-budget-is-tokens` |

## The short version

The panel falls behind for two different reasons, and they need two different fixes.

**The token rows wait for a plan or a report.** Completion, Context and Window cannot move while an
answer streams, because nothing on the server has learned anything. But the page is receiving the
text the next request will carry, so it projects the three from the deltas, marks them, and the next
snapshot replaces them. That is an estimate, and it says so.

**Tool calls is a different fault: the row counts the wrong quantity.** The cap pauses on the calls a
run has *attempted* (`toolCallCount`), while the row and the store count the calls the model *asked
for* (`toolCallsRecorded`), and publish that figure before any of them run. With more than one call in
a step the row can read past its limit while the run is still working, and a step that pauses or stops
early reloads to a count it never reached. So the row is made the same quantity the cap enforces and
published as each call starts: an exact tick, not an estimate, and one the page only has to apply.

That is the answer to "the page should reconcile with the end-of-turn data, but keep the display
current". The page does reconcile — every snapshot wins, and the reload at the end of a run is the
store's own figures — but a number the server already knows is the server's to send. A client tick
would be added to a figure that already contains the call, which is a double count; subtracting the
step's calls first needs a per-step base the page does not have, and would make the row fall back at
every snapshot and climb again.

## Goals

1. The Tool calls row counts the calls the run has attempted and ticks as each one starts, so the
   panel and the pause that stops the run name the same figure.
2. The run's stored `ToolCallCount` is that same attempted count, so the reload after a step agrees
   with the tick. A reload taken while a call is in flight lags by that one call, exactly as Steps
   already lags by the step in flight.
3. Completion, Context and Window move while an answer streams, marked as projections until the
   server's next snapshot replaces them.
4. A reconnect or a mid-run reload rebuilds the same figures from the replayed journal: it loses
   nothing and doubles nothing.
5. The mark is a word as well as a glyph, and nothing about the panel becomes a live region.

## Non-goals

- Changing what a budget means or where it pauses, beyond making the row show the count the pause
  already spends.
- Projecting the Message, Reply, Estimate, Steps, Tool arguments, Tool result or usage figures.
- A tokenizer in the page. The projection is a projection, and the provider corrects it at the end of
  every step.

## What exists today

- **The panel is ten rows**, each a `data-budget` name with a count span and, where there is a
  denominator, a `<meter>` (`AssistantConversationPage.WriteBudgetAsync`). The unit is in the markup:
  chars, tokens, steps, calls.
- **One snapshot feeds both renderers.** `PublishBudgets` sends `budgets`, `limits`, `estimate` and
  `usage`; the keys are the `data-budget` names, and `message` is absent because the textarea is its
  only source. It is published as a step starts, after that step's usage is stored, when a tool call's
  arguments or its result beat the stored maximum, and once more before `run.completed`.
- **Two counters, one budget.** `toolCallsRecorded` is `response.ToolCalls.Count`, stored by
  `RecordStepAsync` and published before the tool loop runs. `toolCallCount` advances as each call is
  attempted, and is the number the tool-call cap compares — the per-step cap counts the same way with
  its own counter. They diverge for the whole of a step, and only made calls can pause the run.
- **The page applies, it does not decide.** `applyBudgets` walks the payload, writes the count with
  the mirrored `formatAmount`, sets `meter.value` clamped to the maximum, rewrites the limit when a
  continuation has widened it, and toggles `assistant-budget-exceeded` by `used > maximum`.
- **The deltas are on the same stream.** `content.delta` and `reasoning.delta` carry `{ step, text }`;
  `tool.started` carries `{ step, toolCallId, name, arguments, summary }`; `tool.finished` adds
  `content` and `durationMs`. The journal replays from sequence zero on a reload (`data-replay-from`
  is `0` whenever the page is replaying), and `after`/`Last-Event-ID` covers a reconnect.
- **The streamed text is what the next request carries.** Reasoning is omitted from subsequent
  requests, and masked or trimmed content is the planner's business.
- **The browser suite already holds a step open.** `AToolCallRaisesTheToolCallBudgetBeforeTheRunCompletes`
  and `TheStepBudgetCountsUpWhileTheAnswerStreams` script a delayed response and assert while the run
  is busy, so a moving figure is observable the same way.

## The design

### D1 — Tool calls counts attempts, and the server publishes the tick

The row and the cap are made to count the same thing, and the tick is published when the call starts:

- **Publish `toolCallCount`, not `toolCallsRecorded`.** Every snapshot — step start, usage stored, a
  finished call, completion — names the calls attempted so far. `toolCallsRecorded` disappears, and
  nothing else reads it.
- **Tick at `tool.started`.** One snapshot is published per call, carrying `toolCallCount + 1` and the
  arguments maximum when this call sets it, so the row moves with the card that prompted it. It is
  exact, so it carries no mark.
- **Store attempts.** `RecordStepAsync` loses the count it adds today; `RecordToolCallAsync(runId)`
  adds one when the attempt resolves, so the run's `ToolCallCount` is the figure the row shows. A
  denied or refused call counts once, because the cap counts it once (`toolCallCount++` happens for
  every attempt).
- **The page applies it.** No accumulator, no ratio: `applyBudgets` already paints whatever the
  snapshot says, so the tick reaches the row with no new page rule.

The one visible seam is a reload taken while a call is in flight: the store has not counted it yet, so
the reloaded figure is one lower than the row the reader just watched. That is the same shape the
Steps row already has — it is published as the step starts and stored when the step's response is
recorded, and the live-budgets plan says so — and it is recorded here so it is not "fixed" by publishing
a call before it has run.

### D2 — Three token rows can be projected, and only three

| Row | What it is | Projection |
| --- | --- | --- |
| `output` (Completion) | the last step's completion tokens against the allowance that step was given | the characters of `content.delta` and `reasoning.delta` for this step, divided by the ratio |
| `context` | the estimate for the request the next step would send | the reported figure plus the characters appended since the snapshot — the step's content and its tool-call arguments — divided by the ratio; reasoning is omitted from later requests, so it is not counted here |
| `window` | Context and Reply together | the projected Context plus the reported Reply, because the panel's note defines it as the pair and it cannot be left behind |

- **Reply** is the `max_tokens` the planned request left. Projecting it needs the provider's published
  maximum and the configured floor, neither of which is on the page; it stays the reported figure.
- **Estimate** is the signed error of the last request, and there is nothing to sign until the
  provider has reported.
- **Steps** is live and exact (D1's sibling). **Tool arguments** and **Tool result** move on the events
  that carry them and are exact, because the DOM's `.length` is the same UTF-16 count the server
  measures. **Message** is the textarea, and the **usage list** is documented as the provider's count,
  so neither is projected.
- **A tool result does not feed the Context projection.** It is stored after the call and the next
  request is planned moments later, so the window in which a projection would show it is too short to
  see, and the Tool result row already carries the fact exactly.

### D3 — The ratio is prose, and it has a measurement behind it

`3.8` characters per token, from [the spike](../spikes/deepseek-token-counting.md): 2,316 tokens for
8,817 characters of English. The unit is UTF-16 characters, which is what JavaScript's `.length`
counts, so the page divides the text it already has and no encoding step exists.

It is deliberately not `TokenEstimator.BytesPerToken`. That ratio is anchored on a serialized
request — tools, JSON, escaping — and the spike measures that content at 1.61 bytes per token. Using
it on a prose answer would multiply the estimate by about 2.4, which does not merely look alarming:
it would push a comfortable answer over its meter and make the panel say something false at the exact
moment a reader is watching it.

The constant is one line in the page with the spike and the ratio in its comment. It is not policy:
nothing an operator can tune changes it, and the provider's own count replaces the projection at the
end of every step. A calibration from the last step's `output` and the characters it streamed is
possible and is written down below as rejected for now, not as never.

### D4 — The accumulator is what the last snapshot has not seen

Per row the map keeps two values: `reported`, the figure the last `budget.updated` supplied, and
`pending`, the characters seen since:

- `content.delta` adds `text.length` to both `output.pending` and `context.pending`;
- `reasoning.delta` adds `text.length` to `output.pending` only;
- `tool.started` adds `arguments.length` to `context.pending` and to `output.pending`, because the
  arguments are completion tokens too and the report that supersedes them comes after the call;
- a `budget.updated` sets `reported` for every row it names, zeroes `pending`, and paints the reported
  figure unmarked.

The projected figure is `reported + ceil(pending / 3.8)`, painted only when `pending` is non-zero, so
a row never falls below what the server last said. The projection is monotone within a snapshot's
lifetime: content only arrives, and the accumulator only resets.

A step-end snapshot zeroes the accumulator, so Context and Window fall back to the figure the snapshot
represents and then rise again when the next step's plan is published. That is the truthful sequence —
the reported figure is what the store holds at that instant — and it is recorded here so it is not
"fixed" later by keeping bytes across a snapshot, which would double them.

### D5 — A projection is marked, and the mark is more than a glyph

The count span keeps its identity and its clean text, so `applyBudgets`, the existing tests and the
element ids are unchanged. Each estimable row's `.assistant-budget-count` gains, once, two empty
siblings after the number: a `<span class="assistant-budget-mark" aria-hidden="true">~</span>` for the
eye and a `<span class="visually-hidden">estimated</span>` for a screen reader, both `hidden` until a
projection is painted. The row carries `data-estimated="true"` while it is showing one, which is what
the stylesheet reads.

A stylesheet rule makes the state visible without borrowing the exceeded colour, which belongs to a
fact: the mark is shown and the figure is drawn in the panel's ordinary weight, italic rather than
bold. The panel stays out of `aria-live` — the existing refusal stands, and a figure that changes
several times a step is louder still when it is a guess.

The mark is cleared for a row the moment a snapshot supplies that row, and the reload at the end of a
run renders only the server's figures, so nothing has to be cleaned up after a run.

### D6 — A projection never claims a budget is exceeded; the tick may

`assistant-budget-exceeded` is the one signal on the panel that says "read this twice". It should mean
a figure the server measured past its limit. A projection sets the number and the meter's `value`
(clamped to the maximum) but not the class: the meter already shows the approach, and the class arrives
with the report that confirms it. The tool-call tick is exact and does take the class — it is the count
the pause will use, so the moment it passes the limit is the moment the run will stop.

### D7 — Replay is the reason this is one opinion and not two

Every projected value is a function of the last snapshot in the journal and the deltas after it, and
the tick is the server's figure applied like any other:

- a reload replays from sequence zero, so the accumulator is reset by each replayed snapshot and ends
  up holding exactly the deltas the live page had — the projection is rebuilt, not doubled;
- a reconnect resumes after the last event id and applies the same rule to the tail;
- `stream.reset` reloads the page, so a projection never outlives the stream that produced it.

No event is ever added to a displayed value, which is the bug a client-side count would have had when
the live-budgets plan rejected one.

## What it touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Web/Assistant/RunProcessor.cs` | **changed** — publish `toolCallCount`, tick at `tool.started`, record each attempt; `toolCallsRecorded` goes |
| `src/uk.osric.asp.Web/Assistant/IConversationStore.cs` | **changed** — `RecordStepAsync` loses its count, `RecordToolCallAsync` added |
| `src/uk.osric.asp.Web/Assistant/SqliteConversationStore.cs` | **changed** — the increment moves to the new method |
| `test/uk.osric.asp.Web.Tests/ShellConversationStore.cs` | **changed** — the fake implements the new method |
| `src/uk.osric.asp.Web/Pages/Seeds/assistant.js` | **changed** — the two values per estimable row, the listeners that advance them, the projection and its marker, the reset in `applyBudgets` |
| `src/uk.osric.asp.Web/Pages/Seeds/assistant.css` | **changed** — the `[data-estimated]` rule and the mark |
| `test/uk.osric.asp.Web.Tests/ConversationStoreTests.cs` | **changed** — the count moves per attempt, not per step |
| `test/uk.osric.asp.Web.Tests/RunProcessorTests.cs` | **changed** — the tick's sequence in the event stream |
| `test/uk.osric.asp.Web.BrowserTests/AssistantChatTests.cs` | **changed** — the four projection tests, below |
| `docs/assistant.md`, `docs/testing.md` | **changed** — the row's meaning, the projection, and what each suite proves |
| `docs/decisions.md` | **changed at the merge** — the two names written, `assistant-budgets-are-one-snapshot` edited |
| `src/uk.osric.asp.Core/**`, `AssistantConversationPage.cs`, `BudgetSnapshot.cs` | **unchanged** |

## Phases

1. **The counter split.** Publish attempts, tick at `tool.started`, move the store increment; the
   event-stream test and the store test prove the row and the pause now name the same count.
2. **The projection's arithmetic.** Completion painted and marked, with a slow answer to watch.
3. **Context and Window.** The arguments listener, the context accumulator, Window following the
   projection, and the panel's note rewritten.
4. **The mark.** The two spans, the stylesheet rule and the accessibility word.
5. **Replay.** The reload-mid-run test, and the reset rule asserted rather than assumed.
6. **The words.** The assistant doc, the testing doc, the panel note, and the two decision entries at
   the merge.

## Tests

**`Web.Tests` — `RunProcessorTests`** (changed)

- `TheToolCallsFigureTicksAsEachCallStarts` — a step scripted with two calls; the `budget.updated`
  payloads carry `tool-calls` 0 before the calls, 1 after the first `tool.started` and before its
  `tool.finished`, and 2 before `run.completed`.

**`Web.Tests` — `ConversationStoreTests`** (changed)

- `AToolCallCountsWhenItIsAttempted` — two recorded attempts are 2, and `RecordStepAsync` leaves the
  count where it was, so the stored figure cannot run ahead of the row.

**`Web.BrowserTests` — `AssistantChatTests`** (changed). These are browser tests because the
projection's rule lives only in the page, exactly as `formatAmount`'s does; there is no JS test runner
and the project does not want one.

- `TheCompletionRowRisesWhileTheAnswerStreams` — a long, slow answer; `[data-budget="output"]` has
  `data-estimated="true"` and its figure is above zero while the page is `aria-busy`.
- `TheContextRowRisesWithTheAnswerItWillCarry` — the same run; Context is marked and above its
  load-time figure before the answer completes.
- `TheProjectionIsReplacedByTheReportedFigureWhenTheRunEnds` — after the run, the reload has no
  `[data-estimated]` row anywhere in the panel and Completion names the provider's
  `completion_tokens`.
- `AReloadMidRunRebuildsTheProjectionRatherThanDoublingIt` — reload during the stream; the row is
  still marked and its figure is at least the pre-reload figure and less than twice it.

A projected figure moves while it is being read, so each of these reads the count span's text with
`EvaluateAsync` and compares it as a number, rather than waiting for an exact string the way a
reported figure's test can. `AToolCallRaisesTheToolCallBudgetBeforeTheRunCompletes` stays as it is; it
now passes on the tick rather than on the step-end snapshot.

**`Web.IntegrationTests` — `AssistantChatLayoutTests`** (unchanged and still green)

- `TheBudgetsAfterTheReloadMatchTheLastLiveFigure` — the last live payload and the reload agree, and
  with both counting attempts that remains true.

## Risks

- **The stored count changes meaning.** `Run.ToolCallCount` becomes the attempted count, so the run
  metrics and the final log count attempts; only a run stopped between a response and its calls
  differs. Two store tests move with it.
- **A reload during a call is one behind.** The accepted shape of the Steps row, documented in
  `docs/assistant.md` beside it.
- **The ratio is wrong for dense text.** A tool result or a source listing carries fewer characters
  per token than prose, so the Context projection can be low where the next request holds code. The
  next step's plan corrects it; the direction is recorded rather than hidden.
- **A moving number invites a second opinion.** The mark, the monotone rule and the reload are what
  keep it one figure with one owner; a change that paints without the mark is a defect, not a tweak.
- **Cost per delta.** Two additions and a division, beside a `textContent` write the page already
  makes. No timer, no layout read. The tick adds one small snapshot per call, and one store update.

## The questions this plan asks the owner

1. **Should a projected figure wear the exceeded colour?** Recommended no: the class is the panel's
   one "read this twice" signal and should mean a measured figure (D6).
2. **Should Window follow the projected Context?** Recommended yes, because the panel's note defines
   Window as Context and Reply together; the alternative is to leave Window at the last plan and say
   so in the note.
3. **Does a call that is denied or refused count against the budget?** Recommended yes. The cap
   already counts the attempt (`toolCallCount++` runs for a denial too), so excluding it from the row
   would put the row and the pause out of step again — which is the fault this plan is fixing.
4. **Should the ratio calibrate from the last step's report?** Recommended not yet: the constant has a
   measurement behind it, and a step that answers with a large tool call would calibrate prose from
   JSON. Revisit if `3.8` proves coarse in use.

## Considered and rejected

- **Ticking Tool calls in the browser.** The snapshot's figure already includes every call the step
  will make, so an increment doubles it; replacing it with a base plus started calls would need a
  per-step base the page does not keep, and would fall back at each snapshot and climb again.
- **Ticking at `tool.finished`.** The row would lag the card, and a slow call would show nothing for
  as long as it runs, which is the stretch a reader is watching.
- **Keeping the row at the requested count and documenting the cap mismatch.** The panel's one alarm
  would be describing calls that were never made.
- **Carrying `TokenEstimator.BytesPerToken` on `budget.updated`.** Rejected in D3: it is a JSON
  request ratio and the streamed text is prose.
- **Calibrating from each step's `output` and the characters it streamed.** More accurate for prose
  and wrong for a step whose output is mostly a tool call's JSON; a later idea, if the constant bites.
- **Publishing an estimate from the server per delta, or on a timer.** A second copy of text already
  on the wire, and the live-budgets plan's refusal of a timer still holds.
- **A tokenizer in the page.** The spike's exact counter is a megabyte of merge table; an estimate
  that the provider corrects at the end of the step does not need one.
- **`aria-live` on the panel.** Six counts, now some of them guesses, announced repeatedly.

## Not in scope

- The per-step tool-call cap as a row of its own; only the run's cap is on the panel.
- Moving where any cap pauses, or what a continuation widens.
- Any other server figure, event, payload key or store column.
- Projecting the Reply, Estimate, Steps, Tool arguments, Tool result or usage figures.
- A JavaScript test runner or a build step; the browser suite proves the page's rule, as it does for
  the formatter.

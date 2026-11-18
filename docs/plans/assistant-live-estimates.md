<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The budget panel estimates while a step streams

| | |
| --- | --- |
| Status | Proposed 2026-11-18 — waits for the owner. Nothing below is built |
| Size | Medium — one page script, one stylesheet rule, four browser tests and the words in the assistant doc and the testing doc. No server change, no event, no store, no configuration |
| Area | Web (assistant), docs |
| Spike | [DeepSeek token counting](../spikes/deepseek-token-counting.md) measured the prose ratio this leans on; nothing new is measured |
| Backlog | [IDEA-051](../jobs/IDEA-051.md) |
| Decisions | New name proposed: **assistant-live-estimates-are-marked**. Edits `assistant-budgets-are-one-snapshot`. Relies on `assistant-the-request-budget-is-tokens` |

## The short version

The live budgets move the panel only when the run loop publishes. Between those publishes — the
stretch where an answer is streaming — the token rows sit still, because Context, Completion and
Window need a request plan or a provider report and neither exists yet. The page is already receiving
the text those figures are made of, so it can project the three of them from the deltas, mark them
as projections, and let the next `budget.updated` replace them.

Three things fall out of doing it this way:

1. **The server does not change.** The idea proposed carrying the estimator's ratio on
   `budget.updated`; it should not. `TokenEstimator.BytesPerToken` is UTF-8 bytes of a JSON request
   body per *prompt* token, and streamed text is prose: the spike measured 1.61 bytes per token for
   JSON and 3.81 characters per token for English. Dividing a prose answer by the request ratio
   would read it as more than twice its size. The page divides by a prose ratio instead, which it
   needs no new field to know.
2. **Counting is not projecting.** Tool calls, tool arguments and tool results already move — exactly,
   on the events that carry them. A column of counts added again in the browser would be wrong twice.
3. **A projection is marked.** The panel's promise is that a figure a reader watches is the figure a
   reload shows. A projection is not that figure, so the row says so until a snapshot supersedes it,
   and the panel is never an unmarked guess. That is the companion decision this plan proposes.

## Goals

1. While a step streams, the Completion row rises with the answer, marked as a projection.
2. While a step streams, the Context row rises with what the next request will carry, marked the
   same way, and Window — the two together — rises with it.
3. The next `budget.updated` replaces each projection with the server's figure and removes the mark;
   the reload after a run shows only the server's figures.
4. A reconnect or a mid-run reload rebuilds the same projection from the replayed journal rather than
   losing it or counting it twice.
5. Nothing about the panel becomes a live region, and an estimated figure is legible as estimated to
   a reader who cannot see the glyph.

## Non-goals

- Any change to `RunProcessor`, `PublishBudgets`, `BudgetSnapshot`, the events, the store or the
  configuration. The page has every byte this needs.
- Projecting the Message, Reply, Estimate, Steps, Tool calls, Tool arguments, Tool result or usage
  figures. Each is either the reader's own input, a reported figure, or already live.
- A tokenizer in the page. The projection is a projection; the provider's count corrects it at the
  end of the step.

## What exists today

- **The panel is ten rows**, each a `data-budget` name with a count span and, where there is a
  denominator, a `<meter>` (`AssistantConversationPage.WriteBudgetAsync`). The unit is in the markup:
  chars, tokens, steps, calls.
- **One snapshot feeds both renderers.** `PublishBudgets` sends `budgets`, `limits`, `estimate` and
  `usage`; the keys are the `data-budget` names, and `message` is absent because the textarea is its
  only source. It is published as a step starts, after that step's usage is stored, when a tool call's
  arguments or result beats the stored maximum, and once more before `run.completed`.
- **The page applies, it does not decide.** `applyBudgets` walks the payload, writes the count with
  the mirrored `formatAmount`, sets `meter.value` clamped to the maximum, rewrites the limit when a
  continuation has widened it, and toggles `assistant-budget-exceeded` by `used > maximum`.
- **The deltas are on the same stream.** `content.delta` and `reasoning.delta` carry `{ step, text }`;
  `tool.started` carries `{ step, toolCallId, name, arguments, summary }`; `tool.finished` adds
  `content` and `durationMs`. The journal replays from sequence zero on a reload (`data-replay-from`
  is `0` whenever the page is replaying), and `after`/`Last-Event-ID` covers a reconnect.
- **The streamed text is what the next request carries.** The docs already say reasoning is omitted
  from subsequent requests, and that masked or trimmed content is the planner's business.
- **The browser suite already holds a step open.** `AToolCallRaisesTheToolCallBudgetBeforeTheRunCompletes`
  and `TheStepBudgetCountsUpWhileTheAnswerStreams` script a delayed response and assert while the
  run is busy, so a projection is observable the same way.

## The design

### D1 — Three rows can be projected, and only three

| Row | What it is | Projection |
| --- | --- | --- |
| `output` (Completion) | the last step's completion tokens against the allowance that step was given | the characters of `content.delta` and `reasoning.delta` for this step, divided by the ratio |
| `context` | the estimate for the request the next step would send | the reported figure plus the characters appended since the snapshot — the step's content and its tool-call arguments — divided by the ratio; reasoning is omitted from later requests, so it is not counted here |
| `window` | Context and Reply together | the projected Context plus the reported Reply, because the panel's note defines it as the pair and it cannot be left behind |

- **Reply** is the `max_tokens` the planned request left. Projecting it needs the provider's published
  maximum and the configured floor, neither of which is on the page; it stays the reported figure.
- **Estimate** is the signed error of the last request, and there is nothing to sign until the
  provider has reported.
- **Steps** moves at a step's start already. **Tool calls** moves from the response's own count before
  any of its calls run, so a browser count would double it; **Tool arguments** and **Tool result**
  move on the events that carry them and are exact, because the DOM's `.length` is the same UTF-16
  count the server measures. **Message** is the textarea, and the **usage list** is documented as the
  provider's count, so neither is projected.
- **A tool result does not feed the Context projection.** It is stored after the call and the next
  request is planned moments later, so the window in which a projection would show it is too short
  to see, and the tool result row already carries the fact exactly.

### D2 — The ratio is prose, and it has a measurement behind it

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

### D3 — The accumulator is what the last snapshot has not seen

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

The one visible seam is that a step-end snapshot zeroes the accumulator, so Context and Window fall
back to the figure the snapshot represents and then rise again when the next step's plan is published.
That is the truthful sequence — the reported figure is what the store holds at that instant — and it is
recorded here so it is not "fixed" later by keeping bytes across a snapshot, which would double them.

### D4 — A projection is marked, and the mark is more than a glyph

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

### D5 — A projection never claims a budget is exceeded

`assistant-budget-exceeded` is the one signal on the panel that says "read this twice", and today it
means the server measured a figure past its limit. A projection sets the number and the meter's
`value` (clamped to the maximum) but not the class: the meter already shows the approach, and the class
arrives with the report that confirms it. A guess that cries wolf on the panel's one alarm is worse
than a guess that waits a step.

### D6 — Replay is the reason this is one opinion and not two

Every painted value is a function of the last snapshot in the journal and the deltas after it:

- a reload replays from sequence zero, so the accumulator is reset by each replayed snapshot and ends
  up holding exactly the deltas the live page had — the projection is rebuilt, not doubled;
- a reconnect resumes after the last event id and applies the same rule to the tail;
- `stream.reset` reloads the page, so a projection never outlives the stream that produced it.

No event is ever added to a displayed value, which is the bug a client-side count would have had when
the live-budgets plan rejected one.

## What it touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Web/Pages/Seeds/assistant.js` | **changed** — the two values per estimable row, the three listeners that advance them, the projection and its marker, the reset in `applyBudgets` |
| `src/uk.osric.asp.Web/Pages/Seeds/assistant.css` | **changed** — the `[data-estimated]` rule and the mark |
| `test/uk.osric.asp.Web.BrowserTests/AssistantChatTests.cs` | **changed** — three tests, below |
| `docs/assistant.md` | **changed** — the budget paragraph and the panel's note gain the projection and the mark |
| `docs/testing.md` | **changed** — the browser suite's sentence says a projection is watched as well as a reported figure |
| `docs/decisions.md` | **changed at the merge** — **assistant-live-estimates-are-marked** written, `assistant-budgets-are-one-snapshot` edited |
| `src/uk.osric.asp.Web/Assistant/RunProcessor.cs`, `AssistantConversationPage.cs`, `BudgetSnapshot.cs` | **unchanged** |

## Phases

1. **The page's projection.** The accumulator, the two delta listeners and `output` painted and
   marked; the first browser test proves it moves while the step is busy.
2. **Context and Window.** The arguments listener, the context accumulator, Window following the
   projection, and the API schema in `docs/assistant.md` unchanged but the note rewritten.
3. **The mark.** The two spans, the stylesheet rule, the `data-estimated` attribute and the
   accessibility word.
4. **Replay.** The reload-mid-run test, and the reset rule asserted rather than assumed.
5. **The words.** The assistant doc, the testing doc, the panel note, and the decision entry at the
   merge.

## Tests

**`Web.BrowserTests` — `AssistantChatTests`** (changed). These are browser tests because the rule
being proved lives only in the page, exactly as `formatAmount`'s does; there is no JS test runner and
the project does not want one.

- `TheCompletionRowRisesWhileTheAnswerStreams` — a long, slow answer; `[data-budget="output"]` has
  `data-estimated="true"` and its figure is above zero while the page is `aria-busy`, which the row
  does not do today.
- `TheContextRowRisesWithTheAnswerItWillCarry` — the same run; Context is marked and above its
  load-time figure before the answer completes.
- `TheProjectionIsReplacedByTheReportedFigureWhenTheRunEnds` — after the run, the reload has no
  `[data-estimated]` row anywhere in the panel and Completion names the provider's
  `completion_tokens`, so the mark cannot outlive the stream.
- `AReloadMidRunRebuildsTheProjectionRatherThanDoublingIt` — reload during the stream; the row is
  still marked and its figure is at least the pre-reload figure and less than twice it.

A projected figure moves while it is being read, so each of these reads the count span's text with
`EvaluateAsync` and compares it as a number, rather than waiting for an exact string the way a
reported figure's test can.

**Unchanged and still green:** `TheBudgetsAfterTheReloadMatchTheLastLiveFigure` and
`ARunPublishesItsBudgetsAsItSpendsThem` — the server's figures and the reload are untouched, which is
the point of keeping the projection on the client.

## Risks

- **The ratio is wrong for dense text.** A tool result or a source listing carries fewer characters
  per token than prose, so the Context projection can be low where the next request holds code. The
  next step's plan corrects it; the direction is recorded in `docs/assistant.md` rather than hidden.
- **A moving number invites a second opinion.** The mark, the monotone rule and the reload are what
  keep it one figure with one owner; a change that paints without the mark is a defect, not a tweak.
- **Cost per delta.** Two additions and a division, beside a `textContent` write the page already
  makes. No timer, no layout read.

## The questions this plan asks the owner

1. **Should a projected figure wear the exceeded colour?** Recommended no: the class is the panel's
   one "read this twice" signal and should mean a measured figure (D5).
2. **Should Window follow the projected Context?** Recommended yes, because the panel's note defines
   Window as Context and Reply together; the alternative is to leave Window at the last plan and say
   so in the note.
3. **Should the ratio calibrate from the last step's report?** Recommended not yet: the constant has a
   measurement behind it, and a step that answers with a large tool call would calibrate prose from
   JSON. Revisit if `3.8` proves coarse in use.

## Considered and rejected

- **Carrying `TokenEstimator.BytesPerToken` on `budget.updated`.** The idea's sketch, rejected in D2:
  it is a JSON request ratio and the streamed text is prose.
- **Calibrating from each step's `output` and the characters it streamed.** More accurate for prose and
  wrong for a step whose output is mostly a tool call's JSON; a later idea, if the constant bites.
- **Counting tool calls, arguments or results in the browser.** The calls are counted from the
  response before any of them runs, so a client count is double; the arguments and results are already
  published on the events that carry them, in the same UTF-16 units the row measures.
- **Publishing an estimate from the server per delta, or on a timer.** A second copy of text already
  on the wire, and the live-budgets plan's refusal of a timer still holds.
- **A tokenizer in the page.** The spike's exact counter is a megabyte of merge table; an estimate
  that the provider corrects at the end of the step does not need one.
- **`aria-live` on the panel.** Six counts, now some of them guesses, announced repeatedly.

## Not in scope

- Any server figure, event, payload key or store column.
- The editor, the page list and every other view.
- Projecting the Reply, Estimate, Steps, Tool calls, Tool arguments, Tool result or usage figures.
- A JavaScript test runner or a build step; the browser suite proves the page's rule, as it does for
  the formatter.

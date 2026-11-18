<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-051 — The budget panel's token figures only move at a step's edge

| | |
| --- | --- |
| Size | M — the page's script, one number on the snapshot, the marker and its style, the panel's note and the assistant doc, and the browser tests that prove the mirror |
| Area | Web (assistant), Core, docs |
| Location | `src/uk.osric.asp.Web/Assistant/RunProcessor.cs`, `src/uk.osric.asp.Web/Pages/Seeds/assistant.js`, `src/uk.osric.asp.Web/Pages/Seeds/assistant.css`, `docs/assistant.md` |

## Idea

The live budgets landed in [the assistant live budgets plan](../plans/archive/assistant-live-budgets.md)
and the panel moves with the run: the loop publishes `budget.updated` as a step starts, when that
step's usage is stored, and around a tool call when the call's arguments or its result beat the
longest already stored. Between those points the panel is still, and it is still for the stretch the
reader is most likely to be watching — a step streaming an answer. Context, Completion and Window are
token figures, and none of them can move until a provider count or a new request plan exists, so a
minute of writing shows the previous step's number.

The page is not blind during that minute. `content.delta` and `reasoning.delta` carry the text that
will be in the next request, and `tool.started`/`tool.finished` carry a call's arguments and a result's
content. With the run's bytes-per-token ratio — the one input it lacks — the page can project what the
answer is about to spend and what the next request is about to cost, and let the next snapshot replace
the projection. That is the estimate worth making.

Counting tool calls on `tool.started` would be the wrong tick. The Tool calls row is already one step
ahead rather than behind: `RunProcessor` adds the response's own call count to `toolCallsRecorded`
before any of those calls runs, publishes it, and `RecordStepAsync` stores the same figure, so a
browser count on top of the snapshot would double every call. Tool arguments and Tool result already
move on those same events, and they are exact rather than estimated: the server measures `string.Length`
of the JSON the event carries, and the DOM's `.length` is the same UTF-16 count. A browser copy would
be a second rule for a figure the server has already sent.

Two rows can be projected, and both are tokens:

- **Completion** is the answer being streamed. Its estimate is the UTF-8 bytes of the content and
  reasoning deltas divided by the ratio the run's own `TokenEstimator` is using — its
  `BytesPerToken`, clamped 1.0 to 6.0, and 1.6 before the first report. The provider's
  `completion_tokens` in the step-end snapshot replaces it.
- **Context** is the next request. The same ratio applied to everything appended since the snapshot —
  the streamed answer, the tool-call arguments and the stored results the events carry — added to the
  published figure. The planner may elide part of that when the next step is planned, so the
  projection leans high; `over` is the safe direction, the one the Estimate row already prefers.

Reply, Window, Estimate, Steps, the tool rows and the usage list stay the server's figures: Reply
needs the provider's maximum and the configured floor, Window is that pair, Estimate needs the
provider's report, and the usage list is documented as the provider's count rather than a limit's.

The panel makes one promise today — a live figure is the reloaded figure
(`assistant-budgets-are-one-snapshot`) — and a projection breaks it, so a row has to say which kind of
figure it is showing. A `~` beside the number, a spelling `BudgetAmount.Format` never produces, plus a
`data-estimated` attribute the stylesheet can read, cleared by the next snapshot that supplies that
row. A reload renders only the server's figures, so no projection outlives the run that made it. The
plan proposes the companion decision by the name it will carry: **assistant-live-estimates-are-marked**.

## Sketch

1. `budget.updated` carries the estimator's ratio beside `limits` — `TokenEstimator.For(calibration).BytesPerToken` — so the page divides by the number the server would have used rather than a
   constant of its own. Before the first report that is 1.6, which is what the server estimates with.
2. `assistant.js` keeps, per row, the figure the last snapshot reported and the bytes seen since:
   the two delta listeners add the UTF-8 length of their text to the Completion and Context
   accumulators, `tool.started`'s arguments and `tool.finished`'s content add to the Context
   accumulator only, and a `budget.updated` zeroes both and paints the reported figure unmarked.
3. Paint through the existing row map, so the meter, its clamp and the exceeded class keep their one
   rule; a projection changes the count text, the marker and the meter's `value`, and nothing else.
4. The projection is one function in the script — bytes and ratio in, a figure out — beside
   `formatAmount`, which is already the one rule mirrored into the page. The browser suite is what
   proves it, as `TypingALongMessageShowsTheFigureInThousands` proves the formatter.
5. Replay is what keeps this one opinion rather than two: the journal replays snapshots and deltas in
   the order the run produced them, so an accumulator that each snapshot resets rebuilds the same
   projection after a reconnect. Nothing is added to a displayed value, so no event can be counted
   twice.
6. Tests: a browser test with a slow streamed answer shows the Completion row moving and marked before
   `run.completed`; a test shows the marker gone after the step-end snapshot and the figure equal to
   the provider's; a reconnect mid-stream rebuilds the same projection rather than doubling it; a
   `Web.Tests` or integration test shows `budget.updated` carrying a positive ratio, and 1.6 before a
   report.
7. Docs: the budget paragraph in [assistant.md](../assistant.md) and the panel's own note say that
   Context and Completion may be a marked projection between snapshots, and when the figure becomes
   the reported one.

The plan that answers this is [assistant live estimates](../plans/assistant-live-estimates.md). It
takes its ratio from the token-counting spike's prose measurement rather than from the run — the
request's bytes per prompt token are a JSON body's, not an answer's — so the server does not change
at all.

## Not in scope

- Publishing on a timer or per delta from the server. The text is already on the wire, and a second
  copy of it per chunk is the traffic the live-budgets plan refused.
- A tokenizer in the page. The ratio is the model of one the server already keeps, and the provider's
  report corrects it at the end of every step.
- Projecting the reply, window, estimate, step, tool or usage figures, for the reasons above.
- The Message row, whose only source is the textarea, and a budgets endpoint or polling.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-051 — The budget panel's figures only move at a step's edge

| | |
| --- | --- |
| Size | M — the page's projection and its mark, a server tick for Tool calls with the store count behind it, five tests and the words in three docs |
| Area | Web (assistant, storage), docs |
| Location | `src/uk.osric.asp.Web/Assistant/RunProcessor.cs`, `src/uk.osric.asp.Web/Assistant/IConversationStore.cs`, `src/uk.osric.asp.Web/Assistant/SqliteConversationStore.cs`, `src/uk.osric.asp.Web/Pages/Seeds/assistant.js`, `src/uk.osric.asp.Web/Pages/Seeds/assistant.css`, `docs/assistant.md` |

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
content. With a characters-per-token ratio — the one input it lacks, and one the token-counting spike
has already measured — the page can project what the answer is about to spend and what the next
request is about to cost, and let the next snapshot replace the projection. That is the estimate
worth making.

Tool calls are a second, different fault. The row should tick as each call starts, and the instinct
to watch `tool.started` is right; the place is wrong. The snapshot is already a step ahead: the
response's own call count is added to `toolCallsRecorded` and published, and `RecordStepAsync` stores
the same figure, all before any of those calls runs — so a browser count added to it would double
every call. And `toolCallsRecorded` is not the number the cap pauses on: the tool-call budget is spent
on attempts (`toolCallCount`), so with more than one call in a step the row can read past its limit
while the run is still working, and a step that pauses or stops early reloads to a count it never
reached. The tick belongs to the server, at `tool.started`, and the row and the store should count
the same attempts the cap does. Tool arguments and Tool result already move on those events and are
exact: the server measures `string.Length` of the JSON the event carries, and the DOM's `.length` is
the same UTF-16 count.

Two figures are projected from the deltas, and Window follows them:

- **Completion** is the answer being streamed. Its estimate is the characters of the content and
  reasoning deltas divided by `3.8`, the token-counting spike's English-prose ratio. The provider's
  `completion_tokens` in the step-end snapshot replaces it.
- **Context** is the next request. The same division applied to what has been appended since the
  snapshot — the streamed answer and the tool-call arguments — added to the published figure. The
  planner may elide part of that when the next step is planned, so the projection leans high; `over`
  is the safe direction, the one the Estimate row already prefers.

Reply, Estimate, Steps, the tool arguments and result rows and the usage list stay the server's
figures: Reply needs the provider's maximum and the configured floor, Estimate needs the provider's
report, and the usage list is documented as the provider's count rather than a limit's. Window is the
projected Context plus the reported Reply, because the panel's note defines it as the pair.

The panel makes one promise today — a live figure is the reloaded figure
(`assistant-budgets-are-one-snapshot`) — and a projection breaks it, so a row has to say which kind of
figure it is showing. A `~` beside the number, a spelling `BudgetAmount.Format` never produces, plus a
`data-estimated` attribute the stylesheet can read, cleared by the next snapshot that supplies that
row. A reload renders only the server's figures, so no projection outlives the run that made it. The
plan proposes the companion decision by the name it will carry: **assistant-live-figures-are-marked**,
and the tool-call change by **assistant-tool-calls-count-attempts**.

## Sketch

1. The page divides by `3.8` characters per token — the token-counting spike's English-prose figure,
   in the UTF-16 units `.length` counts — and not by the run's `TokenEstimator.BytesPerToken`, which
   is UTF-8 bytes of a JSON request body per prompt token and would read a prose answer at more than
   twice its size.
2. `assistant.js` keeps, per row, the figure the last snapshot reported and the characters seen
   since: `content.delta` adds its length to Completion and Context, `reasoning.delta` to Completion
   only, `tool.started`'s arguments to both, and a `budget.updated` zeroes them and paints the
   reported figure unmarked.
3. Paint through the existing row map, so the meter, its clamp and the exceeded class keep their one
   rule; a projection changes the count text, the marker and the meter's `value`, and nothing else.
4. The projection is one function in the script — characters and ratio in, a figure out — beside
   `formatAmount`, which is already the one rule mirrored into the page. The browser suite is what
   proves it, as `TypingALongMessageShowsTheFigureInThousands` proves the formatter.
5. Replay is what keeps this one opinion rather than two: the journal replays snapshots and deltas in
   the order the run produced them, so an accumulator that each snapshot resets rebuilds the same
   projection after a reconnect. Nothing is added to a displayed value, so no event can be counted
   twice.
6. Tests: a browser test with a slow streamed answer shows the Completion row moving and marked before
   `run.completed`; a test shows the marker gone after the reload and the figure equal to the
   provider's; a reload mid-stream rebuilds the same projection rather than doubling it; a
   `Web.Tests` event-stream test shows `tool-calls` ticking as each call starts, and a store test
   shows the stored count moving with the attempts.
7. Docs: the budget paragraph in [assistant.md](../assistant.md) and the panel's own note say that
   Context and Completion may be a marked projection between snapshots, and when the figure becomes
   the reported one.

The plan that answers this is [assistant live figures](../plans/assistant-live-figures.md). It
projects the three token rows on the page, and moves the Tool calls row and the stored count to the
attempts the cap spends and publishes the tick from the server — the page keeps current without a
second rule for a number the server already knows.

## Not in scope

- Publishing on a timer or per delta from the server. The text is already on the wire, and a second
  copy of it per chunk is the traffic the live-budgets plan refused.
- A tokenizer in the page. The ratio is one the spike already measured, and the provider's report
  corrects it at the end of every step.
- Projecting the reply, estimate, step, tool or usage figures, for the reasons above.
- The Message row, whose only source is the textarea, and a budgets endpoint or polling.

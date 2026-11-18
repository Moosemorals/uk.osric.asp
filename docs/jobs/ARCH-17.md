<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-17 — The assistant waits on its owner three ways

| | |
| --- | --- |
| Severity | Low — nothing misbehaves; every wait works. The cost is that the third one has to be built from parts that already exist twice |
| Area | Web (assistant, storage) |
| Location | `src/uk.osric.asp.Web/Assistant/ToolApproval.cs`, `ToolApprovalCoordinator.cs`, `RunContinuationCoordinator.cs`, `SqliteConversationStore.cs`, `RunProcessor.cs`, `RunBudget.cs`, `Run.cs` |

## Problem

A run stops and hands control to its owner two times today, and a third is planned:

- **An approval** — a flagged tool call waits before it runs. `AssistantToolApprovals` is a row per
  `(run, tool call)` with its own statuses, `ToolApprovalCoordinator` waits on the pair and carries the
  decision, and the run stores `awaiting_approval`.
- **A continuation** — a run that spent a budget waits to be told to carry on. The record is the run row
  itself: `Status = awaiting_continuation`, `PausedBudget`, and two limit columns. `RunContinuationCoordinator`
  waits on the run id and carries nothing, and the endpoint widens the limits through `ContinueRunAsync`.
- **A question** — [IDEA-052](IDEA-052.md) plans a tool whose result is the owner's answer. On today's
  shape it needs a third table, a third status, a third coordinator, a third fail-pending method and a
  third route, none of which would share a line with the other two.

The three are one idea: *the run has stopped, the owner is shown a request, and their resolution is
recorded and resumes it.* The differences are the payload of the request, the payload of the
resolution, and what the run does with it — all of which a kind can name.

The same fault is visible in the statuses. `awaiting_approval` and `awaiting_continuation` say the same
thing about the run's availability ("the owner's turn") and differ only in why, and both are enumerated
in the same five places — two active-run queries, `IsRunning`, the recovery sweep and the partial unique
index — which is the shape of a fact that has been written down twice.

## Shape of the change

1. One record, `AssistantInterrupt` — `{RunId (key), Kind, Status, ToolCallId?, RequestJson,
   ResolutionJson, CreatedAt, ResolvedAt, ResolvedBy, FailureCode}` — with at most one pending per run,
   because a run waits on one thing at a time by construction.
2. Two kinds now (`approval`, `continuation`) and a third when the question lands. The store never
   parses a request or a resolution, so a new kind is a payload, not a schema change.
3. One status, `awaiting_owner`, and one home for the run's seven status names in code.
4. One coordinator keyed by the run, one `WaitAsync` on the processor, one route family
   (`…/runs/{runId}/interrupt/{kind}`), one recovery and one fail-closed path.
5. `Run.PausedBudget` and `RunBudget.Awaiting` go: the continuation's budget is in its request, and the
   store widens the limit from what the owner was shown.

[The plan](../plans/assistant-owner-interrupt.md) carries the design, the migration, the phases and the
tests. It changes no behaviour: the same cards, sentences, limits, refusals and restart handling, with
only the status, route and event names moving.

## Not in scope

- The question tool itself, which is [IDEA-052](IDEA-052.md) and depends on this.
- Resuming a run across a restart. The request becomes durable; the run still does not
  (`assistant-a-run-is-not-resumed`).
- Any owner interaction outside a run.

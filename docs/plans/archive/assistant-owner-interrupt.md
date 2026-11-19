<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The run waits on the owner one way, not three

| | |
| --- | --- |
| Status | Merged 2026-11-18 — built on *assistant-owner-interrupt* and merged into trunk with `make check` and `make test-browser` green |
| Size | Large — one record and its table, one migration, one run status, one coordinator, one route, one wait, and the two existing cards rendered from the record. No new behaviour |
| Area | Web (assistant, storage), docs |
| Spike | None. The shape is LangGraph's `interrupt(value, response_schema)` applied to three waits this codebase already implements; [the ask tool's survey](../../spikes/ask-tool-shape.md) is where the shape was met |
| Backlog | ARCH-17 |
| Decisions | Recorded at the merge: `assistant-a-run-waits-on-one-owner-interrupt`. Edited `assistant-a-run-pauses-on-budget` and `assistant-deadlines-bound-silence`. Relies on `assistant-a-run-is-not-resumed` |

## The short version

The assistant already stops and asks the owner three times: before a flagged tool call runs, when a run
budget is spent, and — from the next step — a question the run needs answered. Each is implemented as
its own thing:

| | Approval | Continuation | Question (planned) |
| --- | --- | --- | --- |
| Record | `AssistantToolApprovals` | the run row itself | a third table |
| Status | `awaiting_approval` | `awaiting_continuation` | `awaiting_answer` |
| Coordinator | `ToolApprovalCoordinator` | `RunContinuationCoordinator` | `OwnerQuestionCoordinator` |
| Page card | from the transcript | from `Run.PausedBudget` | from the new table |
| Events | `approval.required/resolved` | `run.paused/resumed` | `question.required/answered` |
| Route | `…/tool-calls/{id}/approval` | `…/continue` | `…/answer` |
| Fail closed | `FailPendingApprovalsAsync` | status rewrite | a third method |

That is one idea written three ways, and the third one does not exist yet. This plan writes it once:
**`OwnerInterrupt` — a stored request from a run to its owner, and the resolution they give it.** The
approval and the continuation move onto it now, because they are two real call sites that prove the
shape; the question is then the first *new* kind, added by the next plan without touching the record,
the status, the coordinator, the route or the wait.

Behaviour does not change. An approval is still a gate that runs or refuses its call, a spent budget
still pauses with the same sentence and the same window, a restart still fails the wait closed, and
the transcript still reads as it does. What changes is the plumbing's names — the status a reader sees,
the route a form posts to, the events the page listens to — and those are the suites' job to hold.

## What exists today, measured

- **Two records for one idea.** `ToolApproval` is a row per `(run, tool call)` with its own status
  vocabulary; a continuation is `Run.Status == "awaiting_continuation"` plus `Run.PausedBudget` plus
  two limit columns. Neither can describe the other.
- **Two coordinators.** `ToolApprovalCoordinator` waits on `(runId, toolCallId)` and carries the
  decision; `RunContinuationCoordinator` waits on `runId` and carries nothing. A third would be the
  question's.
- **Two statuses for one fact.** `awaiting_approval` and `awaiting_continuation` are enumerated in the
  same five places — the two active-run queries, `IsRunning`, `RecoverInterruptedRunsAsync` and the
  partial unique index — and `RunBudget.Awaiting` is the only half of that pair with a constant.
- **Two recoveries.** A restart fails pending approvals with `FailPendingApprovalsAsync` and interrupts
  any run in either awaiting status; a question would add its own fail-pending call beside them.
- **Two page paths.** The approval card is rebuilt from the transcript's tool-call arguments; the
  continuation card from `PausedBudget`; a question would be a third read.
- **The wait itself is already one thing.** Every tool call runs inside `WhilePaused`, and the approval
  and continuation both pause the silence clock before waiting, so the *timing* rule is already shared
  and only the bookkeeping around it is not.

## Goals

1. One record — kind, request, resolution, who resolved it and when — for every time a run waits on
   its owner, with at most one pending per run by construction.
2. One run status, `awaiting_owner`, so a log line, a metric, a query and a reader all say the run is
   the owner's turn without pretending to know what for.
3. One coordinator, one wait, one route and one fail-closed path for every kind.
4. The approval and the continuation behave exactly as they do: same cards, same sentences, same
   limits, same refusals, same restart behaviour.
5. A third kind is added by adding a kind — its payload, its card and its form — and nothing else.
6. The status names get one home in code, so the five enumerations cannot drift again.

## Non-goals

- **The question tool.** That is [the next plan](assistant-ask-owner.md); this one only makes room for
  it. Nothing here knows what a question's request or resolution contains.
- **Changing when the owner is asked.** `ApprovalMode`, the budgets and their windows are untouched.
- **Resuming a run across a restart.** The record makes the *request* durable; the run is still not
  resumed (`assistant-a-run-is-not-resumed`). A restart marks the run interrupted and fails the record
  with a reason, as it does today.
- **An owner interrupt outside a run.** No notifications, no inbox, no interrupt without a conversation.
- **Rendering a resolved approval or continuation.** Their cards remain transient; only a question
  earns a durable card, and that is the next plan's.

## The design

### D1 — One record, keyed by the run

`AssistantInterrupts`:

| Column | Type | Why |
| --- | --- | --- |
| `RunId` | TEXT, primary key, cascade on the run | the run is sequential, so it waits on one thing at a time — the key makes that an invariant rather than a habit |
| `Kind` | TEXT | `approval` or `continuation` now; the next plan adds `question` |
| `Status` | TEXT | `pending`, `resolved`, `failed` |
| `ToolCallId` | TEXT NULL | the call the wait belongs to, or null for a budget pause |
| `RequestJson` | TEXT | what the owner is shown, as the host wrote it |
| `ResolutionJson` | TEXT NULL | what they chose or typed |
| `CreatedAt`, `ResolvedAt`, `ResolvedBy`, `FailureCode` | | who answered, when, and why it failed |

`Status` is narrower than the approvals table's, on purpose. `applied`/`denied` were never statuses:
they were the resolution stored in the status column. One status plus a resolution payload means one
vocabulary for every kind, and the run's *consequence* is the processor's branch, not a stored word.

Opaque JSON on both sides is what makes the record general: the store never parses a request or a
resolution, so a new kind needs no store change and no migration.

### D2 — The two kinds that exist, and what each carries

| Kind | `RequestJson` | `ResolutionJson` | The run does |
| --- | --- | --- | --- |
| `approval` | `{name, description, arguments}` — the tool call the owner is gating | `{decision: "approve"\|"deny"}` | runs the call, or answers it with a refusal |
| `continuation` | `{budget, limit, window, message}` — which budget ran out and what continuing buys | `{decision: "continue"}` | widens that budget by the window it was shown and carries on |

The approval's request is new: today its arguments are read back out of the transcript's tool-call
JSON, which is a second parser for a shape the host itself wrote and leaves the card at the mercy of
the transcript's shape. Storing it means the page renders a pending approval from one row, the same
way it will render a question.

The continuation's request replaces `Run.PausedBudget`, and the message is the sentence
`RunBudget.Sentence` already builds, so the live card and the reloaded one still cannot disagree.

### D3 — One status, and the seven names get one home

`awaiting_owner` replaces `awaiting_approval` and `awaiting_continuation`. The run's statuses become
`queued`, `running`, `awaiting_owner`, `completed`, `failed`, `cancelled` and `interrupted`, and they
are declared once in a `RunStatus` static beside `RunBudget`, used by the queries, the processor, the
store and the page. The partial index's filter stays a literal, because it is a schema definition, not
a query.

The migration:

1. rewrites any run still in `awaiting_approval` or `awaiting_continuation` to `interrupted`, with
   `ErrorCode = "restart"` and the message recovery would have written — a deploy restarts the host, so
   this is what those rows mean anyway;
2. creates `AssistantInterrupts`, copies every `AssistantToolApprovals` row into it as kind `approval`
   with `status` resolved or failed and the decision in the resolution, and drops the old table;
3. drops `Run.PausedBudget` and recreates the active-run index around the one status.

The down migration puts the approvals table back, copies the approval rows back, restores the two old
statuses for runs in `awaiting_owner` (by the interrupt's kind) and restores `PausedBudget`. It is
lossless today because no question rows exist yet; after the next plan a down migration cannot restore
a question, and the migration test says so rather than pretending otherwise.

### D4 — One wait

The processor keeps one method:

```csharp
Task<OwnerInterrupt> WaitAsync(OwnerInterruptRequest request, CancellationToken cancellationToken)
```

It opens the record and the status in one transaction (`pending` + `awaiting_owner`), publishes
`interrupt.required`, waits on the coordinator, reads the record back and publishes
`interrupt.resolved`. It runs inside `WhilePaused`, so the silence clock is stopped for every kind as
it is today. The store is the authority for the resolution; the coordinator carries nothing, so a
signal can never disagree with the row.

Three callers, one method:

- `ResolveApprovalAsync` becomes a branch around it: a denied approval answers the call with a
  refusal; an approved one executes it.
- `PauseAsync` calls it with a continuation request and takes the next limit from the resolution.
- The next plan's tool calls the same method through a seam, because it is the one caller that is not
  the loop.

### D5 — One coordinator, keyed by the run

`OwnerInterruptCoordinator` replaces both existing coordinators: `WaitAsync(runId, token)` and
`Signal(runId)`, one pending signal per run, removed in `finally` and refused when a second wait
exists. `ToolApprovalCoordinator` and `RunContinuationCoordinator` are deleted. Its tests are the old
approval coordinator's three cases, unchanged in meaning.

### D6 — One route, and the record says what is being answered

```
POST /assistant/{id:long}/runs/{runId}/interrupt
```

One route and one handler. **It does not carry the kind.** The run has one pending interrupt, and the
record is the authority for what kind it is, so a path segment would be a second copy of a fact the row
already holds — and the copy is what can disagree. The page builds the form's action from the record it
just rendered, so nothing in a URL can drift from it.

The handler's order is what makes one route safe, and it replaces the check a kind in the path would
have made:

1. The antiforgery token, then the owner.
2. Read the run's pending interrupt. Absent, or belonging to another owner, is `404` with the sentence
   the approval route uses; present but not `pending` is `409` for the same reason it is today.
3. Parse the body **against that record's kind, strictly**: a field the kind does not expect, a missing
   field it requires, or a tool call id that is not the record's is refused with `409` and the
   "the run moved on before this arrived" sentence. A field the kind does not expect is the rule that
   keeps a form from an earlier wait from resolving a later one.
4. Resolve the record, signal the run, `204`.

| Kind | Body, beyond the antiforgery token |
| --- | --- |
| `approval` | `decision=approve\|deny` and the tool call id, checked against the record |
| `continuation` | nothing |
| `question` (next plan) | `answer{n}`, `other{n}` and the tool call id, checked against the record |

So a stale approval form arriving during a continuation is refused because a continuation takes no
fields, and a stale continuation form arriving during an approval is refused because the decision is
missing. Both are the same refusal a kind in the path would have produced, made against the authority
rather than against a copy of it.

### D7 — The page reads a record, not three things

The conversation page reads the run's pending interrupt (at most one) and renders it by kind:

- `WriteApprovalAsync` — the call's name, description and arguments from `request`;
- `WriteContinuationAsync` — the sentence from `request`, the Continue form, and the note that
  stopping ends the run.

The approval card's current gate on `RequireApproval` goes with it: a pending interrupt is a fact
about the run, and a conversation whose mode was changed mid-wait should not hide a card the run is
blocked on. Nothing else about either card changes.

### D8 — One event pair

`interrupt.required` carries `{step, kind, toolCallId, request}` and `interrupt.resolved` carries
`{step, kind, toolCallId, resolution}`. `approval.required`, `approval.resolved`, `run.paused` and
`run.resumed` go. The page has one listener pair that dispatches by kind to the card builders it
already has, and keys the card by `data-interrupt-kind` so a replayed event replaces the server's card
rather than joining it — the rule the continuation card already follows and the approval card does
not. The continuation's resumed limit arrives in the resolution, and the panel's next
`budget.updated` remains the authority for the figure.

### D9 — One recovery

`RecoverInterruptedRunsAsync` filters on `queued`, `running` and `awaiting_owner`, fails the run's
pending interrupt with `restart`, and writes the synthetic tool result for the call that was waiting —
the same results it writes today, because it derives them from the transcript, not the record.

### D10 — What does not change

- **`ApprovalMode`.** `AlwaysApprove` still asks nothing; `RequireApproval` still asks before a flagged
  call. The policy is not the record.
- **The budgets.** The same windows, the same sentence, the same `StepLimit`/`ToolCallLimit` on the run.
- **The ask tool's design.** [The next plan](assistant-ask-owner.md) is unchanged by this one except
  that it is now small.

## What it touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Web/Assistant/OwnerInterrupt.cs` | **new** — the record and the kind |
| `src/uk.osric.asp.Web/Assistant/OwnerInterruptCoordinator.cs` | **new** — waits and signals |
| `src/uk.osric.asp.Web/Assistant/RunStatus.cs` | **new** — the seven status names, once |
| `src/uk.osric.asp.Web/Assistant/ToolApproval.cs`, `ToolApprovalCoordinator.cs` | **deleted** |
| `src/uk.osric.asp.Web/Assistant/RunContinuationCoordinator.cs` | **deleted** |
| `src/uk.osric.asp.Web/Assistant/IConversationStore.cs`, `SqliteConversationStore.cs` | **changed** — open, get, resolve, fail, continue; one status; one recovery |
| `src/uk.osric.asp.Web/Assistant/RunProcessor.cs` | **changed** — one wait, three callers, one event pair |
| `src/uk.osric.asp.Web/Assistant/Run.cs`, `RunBudget.cs` | **changed** — `PausedBudget` and `RunBudget.Awaiting` go |
| `src/uk.osric.asp.Web/Assistant/AssistantEndpoint.cs`, `Program.cs` | **changed** — the one route; two registrations become one |
| `src/uk.osric.asp.Web/Assistant/AssistantConversationPage.cs` | **changed** — one read, two renderers from the record |
| `src/uk.osric.asp.Web/Pages/Seeds/assistant.js` | **changed** — one event pair, keyed cards |
| `src/uk.osric.asp.Web/Storage/StorageDbContext.cs`, `Migrations/*_AddOwnerInterrupts.cs` | **changed, new** — the entity, the copy, the status rewrite, the dropped column, the index |
| `test/uk.osric.asp.Web.Tests/ConversationStoreTests.cs`, `RunProcessorTests.cs`, `ToolApprovalCoordinatorTests.cs` | **changed** — the same behaviours against the record; the coordinator suite is renamed |
| `test/uk.osric.asp.Web.IntegrationTests/AssistantApprovalFlowTests.cs`, `AssistantContinuationFlowTests.cs`, `AssistantMemoryApprovalTests.cs` | **changed** — the new route, the new event names |
| `test/uk.osric.asp.Web.BrowserTests/AssistantApprovalTests.cs`, `AssistantContinuationTests.cs` | **unchanged** — they click, and click the same buttons |
| `docs/assistant.md`, `docs/glossary.md`, `docs/metrics.md`, `docs/testing.md`, `docs/jobs/README.md` | **changed** — the one wait, the glossary's "Tool approval" becomes a kind, the duration sentence, the suites, the job |
| `docs/decisions.md` | **changed at the merge** — **assistant-a-run-waits-on-one-owner-interrupt** written, two entries edited |

## Phases

1. **The record and its store.** `OwnerInterrupt`, the table and the migration, `RunStatus`, and the
   four store methods, with `ConversationStoreTests` and the migration suite proving the shape, the
   one-pending rule, the ownership rule and the continuation's widening. The old tables and statuses
   are gone from the schema at the end of this phase, so the build is green only once phase 2 lands:
   the branch's gate is the proof, not each commit.
2. **The wait.** `OwnerInterruptCoordinator`, `WaitAsync`, the approval and continuation callers, the
   event pair, and `RunProcessorTests` updated case by case. No behaviour may change except the event
   names.
3. **The route.** The one handler, the two integrations suites, and the removal of the old
   routes.
4. **The page.** One read, two renderers from `request`, the script's one listener pair and keyed cards,
   and the accessibility sweep's assistant fixture still green.
5. **Recovery and the failure paths.** One filter, one fail-pending call, and the stop path's synthetic
   result, proved by the store's recovery cases and the processor's stop case.
6. **The words.** `assistant.md`, the glossary, `metrics.md`, `testing.md`, the job row and the three
   decision entries at the merge.

## Tests

**`Web.Tests` — `ConversationStoreTests`** (changed)

- `AnInterruptIsOpenedOnlyWhileItsRunIsRunning`
- `ARunHasAtMostOnePendingInterrupt`
- `AnInterruptIsResolvedOnlyByTheOwnerOfItsConversation`
- `ResolvingAnInterruptReturnsTheRunToRunning`
- `AContinuationWidensOnlyTheBudgetThatRanOut` — the existing case, against the record.
- `AContinuationResolvesTheInterruptThatAskedIt`
- `RecoveringARunFailsItsPendingInterrupt`
- `AnApprovalIsCopiedFromTheOldTableWithItsDecision` — the migration suite, not this one; listed under
  `AssistantInterruptMigrationTests` below.

**`Web.Tests` — `AssistantInterruptMigrationTests`** (new)

- `TheMigrationCopiesApprovalsAndRetiresTheTwoOldStatuses`
- `TheDownMigrationRestoresTheApprovalsTableAndTheOldStatuses`
- `TheActiveRunIndexAdmitsTheOwnerStatusAndNothingElse`

**`Web.Tests` — `OwnerInterruptCoordinatorTests`** (renamed from `ToolApprovalCoordinatorTests`)

- `SignalWakesTheMatchingWaiter`
- `SignalForAnUnknownRunWakesNothing`
- `CancellationStopsWaitingAndRemovesThePendingSignal`

**`Web.Tests` — `RunProcessorTests`** (changed)

- `AnApprovalRunsOrRefusesItsCallAndPublishesOneInterrupt` — the existing approved and denied cases,
  asserting `interrupt.required`/`interrupt.resolved`.
- `ABudgetPauseIsAnInterruptThatWidensItsBudget` — the existing steps and tool-call pause cases.
- `TheOwnersWaitStopsTheSilenceClock` — a slow answer to an interrupt does not time the run out.

**`Web.IntegrationTests`** (changed)

- `AssistantApprovalFlowTests.RequiredApprovalPausesWritesAndAlwaysApproveExecutesWithoutPromptAsync`
  — the same assertions through the new route and the new event.
- `AssistantContinuationFlowTests` — the same, through `…/interrupt`.
- `AssistantMemoryApprovalTests` — the same, through the new route.
- `AFormForAKindTheRunIsNotWaitingOnIsRefused` (new) — a decision posted while the run waits on a
  continuation is `409`, and a continuation posted while it waits on an approval is `409`; neither
  resolves anything. This is the test the kind in the URL would otherwise have been.

**`Web.BrowserTests`** (unchanged)

- `AssistantApprovalTests` and `AssistantContinuationTests` are the proof that what a reader does with
  the cards is untouched. If one of them needs editing, the page changed in a way this plan said it
  would not.

## Risks

- **The rename is wide.** Two statuses, two route shapes and four event names move. The compiler finds
  the code; the suites find the literals; the migration suite finds the schema. This is the churn the
  owner has accepted, and the reason the approval and continuation suites are left as unchanged as
  possible.
- **The migration copies data.** Approvals are resolved or failed rows, all of them; the copy is
  mechanical and the down path is lossless while no question rows exist. The migration suite applies it
  to a database that already holds a conversation, a run and an approval.
- **A pending interrupt and a status could disagree** if they were written separately, so opening and
  resolving each write both in one transaction.
- **A stale form answers the wrong wait.** The body is parsed strictly against the pending record's
  kind — an unexpected field, a missing one, or a tool call id that is not the record's is `409` — so a
  form from an earlier wait cannot resolve a later one, and the kind needs no second home in the URL.
- **The approval card loses its mode gate.** That is deliberate: a run blocked on a card is a fact. The
  options form still changes the mode for the *next* call.
- **LangGraph's shape is not a specification.** It is the clearest statement of "a request and a
  resolution" in the survey; the local justification is the three call sites, not the precedent.

## The questions this plan asked the owner

Answered 2026-11-18:

1. **Does the continuation belong in the same record?** Yes. It is the third wait, it is the one with no
   record today, and leaving it out is what would keep two statuses for one fact. Its resolution
   changes run state as well as recording a decision, which is why the store owns the widening.
2. **Should the route be one path with a kind, or three paths?** One path, and **the kind stays out of
   the URL**. The run has one pending interrupt and the record says what kind it is, so a segment would
   be a second copy that can disagree; the body is parsed strictly against the record instead (D6).
3. **Should the status names get a `RunStatus` home?** Yes, because five enumerations drifting is the
   fault this plan is fixing.
4. **Should an approval's request be stored, or keep reading the transcript?** Stored: one row renders
   the card, the endpoint validates against it, and the page stops depending on the shape of a message
   it did not write.

## Considered and rejected

- **The kind in the route path.** Rejected by the owner: the run has one pending interrupt and the row
  already says which kind it is, so `…/interrupt/{kind}` is a second copy — and the copy is what can
  disagree. The mismatch check a path segment would give is replaced by parsing the body strictly
  against the record (D6), which refuses the same forms for the same reason.
- **Three routes, one per kind.** That is what exists now in two of the three cases, and it is three
  handlers repeating the same CSRF, owner and pending checks.
- **Two kinds (approval and question) and leaving the continuation alone.** That keeps two statuses for
  "the owner must act", which is the incoherence, and leaves the oldest of the three waits as the odd
  one out.
- **Keeping `awaiting_approval` and `awaiting_continuation` and adding the record underneath.** The
  record would then be a second place that says why the run waits; the status would say it a third
  time. One status plus the kind is the smallest honest pair.
- **A per-kind table.** That is what exists now, and it is why the third kind costs a table, a
  status, a coordinator, a route, a card path and a recovery.
- **A discriminator on the approvals table.** Cheaper to migrate than a new table, but the table's
  name, its status vocabulary and its partial index would all have to mean two things.
- **A generic `interrupt` outside the assistant's runs.** No second caller, and a page cannot block on
  a visitor.
- **Carrying the resolution through the coordinator.** The row is the authority; a signal with a
  payload is a second copy that can disagree.

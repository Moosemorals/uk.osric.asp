<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Page first

| | |
| --- | --- |
| Status | Done — DEC-291. The rule and the audit landed; the memory capability this plan first proposed was withdrawn before it was built, and the gap it was meant to fill is [IDEA-033](../jobs/IDEA-033.md) |
| Size | Small — a rule and its audit in two documents, one withdrawn decision, one new job and the cross-references that pointed at it. No product code |
| Spike | None. Nothing is built: the step records where a view lives and what a page still cannot do |
| Decisions | DEC-210 (the rule) stands; DEC-211 is withdrawn; DEC-291 records the re-scope |
| Backlog | [IDEA-025](../jobs/IDEA-025.md) (the assistant page, which now includes the memory view), [IDEA-024](../jobs/IDEA-024.md) (an archive is not a form), [IDEA-033](../jobs/IDEA-033.md) (a page has no persistent per-account store); streaming landed in [the streaming plan](page-streaming.md) |

## The short version

The site is a page that can edit itself, and its shell had been quietly growing views that are not the
page runtime at all. The rule that stops that:

> **Anything a page can serve is a page.** A route stays in the shell only for one of three reasons, and "we have not got to it" is not one of them: it is a job.

The first version of this plan applied it to the owner's memory page by adding `IMemoryCatalog` and
`IMemoryWriter` to the page contract. That was host support for one view, and it would not survive the
assistant becoming a page: once the conversation page exists, the memory view is one of its routes,
served by the same owner-bound capability, and a memory-shaped capability would be a second thing that
page has to be written against. Memory is the assistant's — its tools write it during a run, with FTS5
recall, versions, pins and provenance — so it does not become a page capability at all. DEC-211 is
withdrawn.

What a page actually lacks is **persistent per-account storage**: `IPageStorage` is shared by every
visitor and is the page's content, and `IPageSession` belongs to one browser and goes when it signs
out. A page that wants a personal list, a preference or a record per signed-in account has nowhere to
put it. That is a job of its own — SQL-shaped, one namespace per account, with the schema declared by
the page — and it needs a plan and a named first customer before it is built.

So this plan delivered three things and no code: the rule written into
[architecture](../architecture.md#where-a-view-lives) with the audit beside it, DEC-211 withdrawn and
the reasoning recorded below, and IDEA-033 written with every document that pointed at the old
capability re-pointed at the right one.

## Goals

1. One written rule for where code goes, applied to every route the shell answers today, with the
   exceptions named and a record behind each.
2. The audit in the document a contributor reads before adding a view, and expected to shrink.
3. The gap between what pages can keep and what an account needs named, shaped and costed, so the next
   step starts from a brief rather than from a blank page.

## What changed, and why

| Claim in the first version | What is true |
| --- | --- |
| "Memory becomes a product capability: a page written by the owner can read and change their own memory" | Memory is the assistant's. Its store, its FTS5 recall, its versions, pins and provenance are written by the tools inside a run and read back into the next prompt. A page capability would either duplicate that store or invert who owns the schema |
| "`IMemoryCatalog` and `IMemoryWriter` in `Sdk`, offered through `ctx.Capabilities`" | Those two interfaces are host support for one view. When the assistant becomes a page ([IDEA-025](../jobs/IDEA-025.md)), that page's capability answers for memory as it answers for conversations — one capability, owner-bound, and no memory-shaped type in `Sdk` |
| "The first customer is the owner's memory page" | The first thing a page wants that it cannot have is not memory and not a view. It is somewhere to keep per-account data |
| "`/assistant/memory` is deleted in phase 3" | It stays until the assistant page replaces it. Deleting it first would take away the owner's only view of their memory, and a page cannot replace it without the assistant's store |

What survives is what the plan was written for: the rule, the audit's shape, and the observation that
the list is expected to shrink.

## The rule

> **Anything a page can serve is a page.** A route stays in the shell only for one of three reasons, and "we have not got to it" is not one of them: it is a job.

### The audit

The list lives in [architecture](../architecture.md#where-a-view-lives), so a reader who has never
seen this plan can find it. The `/assistant/memory` row is folded into `/assistant`, and the two
recovery routes the first version left out are named.

| Route | Why it is not a page | Where that is recorded |
| --- | --- | --- |
| `/login`, `/logout` | **Bootstrap.** You cannot reach a page you must sign in for if signing in is a page you must sign in for | DEC-210 |
| `/status`, `/status/chart`, `/admin/changes` | **Bootstrap.** Diagnosing why a page did not load starts here, and the changelist is a file the image carries, which a page cannot read | DEC-210, DEC-255 |
| `/admin/restore`, `/admin/scrub` | **Bootstrap.** The restore is the way back for a page that no longer compiles, so it cannot itself be a page that might not compile; the scrub is the one irreversible delete and stays an explicit owner action | DEC-210, DEC-256 |
| `{**path}`, `/favicon.ico`, `/heath`, `/metrics` | **The runtime.** The page catch-all, the reserved key that stops a page claiming the icon, the probe and the scrape target are the thing pages run on | DEC-210 |
| `/assistant`, `/assistant/{id}`, its events, approvals, options, stop, delete and the memory view | **A page cannot, yet.** The live stream is now something a page can do ([the streaming plan](page-streaming.md)); the conversation's actions are host singletons; the memory view belongs to that page rather than to a capability of its own | [IDEA-025](../jobs/IDEA-025.md) |
| `/admin/import`, `/admin/export` | **A page cannot, yet.** A page can neither take an upload nor serve a file | [IDEA-024](../jobs/IDEA-024.md) |

Two sentences go with the table: the page column is the default, so a new route asks which column it
is in before it is written, and "it is easier in C#" is an IDEA file rather than an answer. The list
is expected to shrink — when IDEA-024 lands, import and export join the page column, and the rule is
what stays the same.

## The capability that is actually missing

| Store | Whose | How long | Where |
| --- | --- | --- | --- |
| `IPageStorage` | the page — every visitor shares it | until the key or the page goes | `PageState` |
| `IPageSession` | one browser | until sign-out, or 30 minutes idle (8 hours signed in) | `PageSessions` |
| `IPageCatalog` / `IPageWriter` | the page store itself | forever | `Pages`, `PageRevisions` |
| **missing** | **one signed-in account** | **until the account or the page goes** | — |

The gap is deliberate today: DEC-125's open-policy note says per-user state is "a later job with its
own migration and decision", and the identity hook it needed has existed since DEC-132–DEC-137. This
plan opens that job rather than building it.

[IDEA-033](../jobs/IDEA-033.md) — a page has no persistent per-account store — carries the analysis:

- **Per account, not per page.** The namespace belongs to the signed-in account, so a page names its
  own tables and can read what another page stored for the same account, which is the rule
  `IPageSession`'s reads already follow. The job records the consequence: a page the assistant edits
  can read another page's rows for the owner.
- **SQL, not JSON documents.** A per-account page wants a list, an order, a filter and a search; a
  document store would make every page load everything or grow a query language of its own. The schema
  is declared by the page, so the host can create the table and index it — including FTS5 over the
  columns the page names — which is the part page code cannot do for itself.
- **The cost is real**, and is why this is a job and not a line in this plan: a schema lifecycle, a
  quota per (account, page), a statement sandbox, a structural owner filter rather than a predicate a
  page remembers to write, and a privacy sentence for a store that outlives sign-out.
- **A customer first.** The owner names the page that will use it before it is built. A capability no
  page uses is speculative work (DEC-08), and this site takes the obvious thing at small `n` (DEC-14).

## Non-goals

- **Building per-account storage now.** It needs a plan of its own, a migration, a sandbox and a first
  customer. This step records it.
- **A memory capability in `Sdk`.** Withdrawn (DEC-211). Memory's view arrives with the assistant page.
- **Deleting `/assistant/memory`.** It is the owner's only view of their memory until the assistant
  page exists. IDEA-025 takes it, and DEC-205 stands until then.
- **Making any other view a page at once.** Each remaining shell route names its blocker; none of them
  needs this step.
- **A route policy for pages, team memory, or sharing.** Unchanged: the guard is the page's
  `ctx.User` check, and authority that lives in configuration is authority a page cannot see (DEC-02,
  DEC-169).

## What this touches

| File | Change |
| --- | --- |
| `docs/plans/page-first.md` | This plan, rewritten in place. The rule section and its `#the-rule` anchor are kept so DEC-210's citation still resolves; the memory capability, its phases and its tests are replaced by "what changed, and why", the audit and the pointer to IDEA-033 |
| `docs/architecture.md` | "Where a view lives" under "Where does code go?": the rule, the audit table and the sentence that the list is expected to shrink |
| `docs/decisions.md` | DEC-211 moved to Superseded, dated, marked never built, with IDEA-025 and IDEA-033 named; DEC-291 records the re-scope; DEC-125's open-policy note points at IDEA-033 |
| `docs/jobs/IDEA-033.md` | New: the per-account store, its shape, its open questions, its cost and the trigger that would make documents enough |
| `docs/jobs/IDEA-025.md` | The assistant page gains the memory view |
| `docs/jobs/IDEA-024.md` | The citation "exactly as the memory writer does (DEC-211's rule)" becomes DEC-190, the rule it meant |
| `docs/jobs/README.md` | IDEA-033 in the open table; programme step 9 re-worded to what this step is; the note under the programme corrected |
| `docs/glossary.md` | **Bootstrap route** added, because the audit uses the word; **Memory page** gains the sentence about where the view moves |
| `docs/assistant.md` | One sentence in the memory section: the view stays shell until the assistant is a page |
| `docs/plans/archive/assistant-memory-page.md` | Its **Superseded** row points at IDEA-025, not at a capability that does not exist |

## Design

### D1 — The rule stays; the audit moved to architecture

The audit is the durable half of the plan, and a reader should not have to find a plan to know which
column a route is in. It lives in the section a contributor already reads before adding a view, with
the two sentences that make it a rule rather than a list.

`/assistant/memory` is not a row of its own, because a memory view is not a capability the shell keeps;
it is part of the assistant, and the assistant is one row. That also keeps the table honest: its rows
are routes the shell answers, not views inside a route.

### D2 — DEC-211 is withdrawn, explicitly

DEC-211 was recorded with the first version and never built, so it is not a decision that drifted — it
is a decision that stopped being right before its code existed. It is in
[Superseded](../decisions.md#superseded) with the date, the words "never built", and where the question
went: IDEA-025 for the view, IDEA-033 for the capability a page actually lacks.

It is not deleted and it is not silently edited. A reader who finds `IMemoryCatalog` in a branch or a
note needs to find out why it is not in `Sdk`, and a Superseded row answers that. For the same reason
the first version's design sections are described in "What changed, and why" rather than erased:
DEC-09 makes a plan the record of the reasoning, including what was rejected.

### D3 — Memory's view belongs to the assistant page

[IDEA-025](../jobs/IDEA-025.md) already says the conversation list, the transcript and the approval
card are page-shaped, and that the two host things are a stream and the run actions. It now says the
memory view is page-shaped too:

- it is one of that page's routes rather than a page of its own;
- the capability the assistant page is offered answers for memory as it does for conversations — the
  owner comes from `ctx.User`, never from an argument, and an anonymous render is refused by the
  capability (DEC-190's shape);
- the pin stays the owner's alone (DEC-206), a stale version is still refused with the stored content
  (DEC-207), a refusal still keeps the text (DEC-208), and a forget is still a delete that asks first
  (DEC-209). DEC-205 is superseded when that route goes, not before.

IDEA-024's reference to "the memory writer (DEC-211's rule)" was corrected in the same change: the rule
it means is DEC-190, `IPageWriter`'s refusal of an anonymous render.

### D4 — IDEA-033 is written, not built

The job is the deliverable, and the shape recorded in it is the owner's: SQL, one namespace per
account, a schema the page declares. It says what it costs and what has to be decided, so the next plan
starts from a brief:

- **Where the schema is declared** — a declared type in the page's source, judged on save like a
  library's shape rule, with runtime DDL and a configuration section named as the worse alternatives.
- **How the owner filter becomes structural** — SQLite has no row-level security: a per-connection view
  over an `Owner` column with `INSTEAD OF` triggers, one table per account, and a query API over
  declared columns are the three shapes, each with its cost.
- **The sandbox** — `SELECT`/`INSERT`/`UPDATE`/`DELETE` against declared names only, one statement, a
  command timeout, a result cap, `trusted_schema = OFF`, and the page API's existing denial of
  `Microsoft.Data.Sqlite` unchanged. [SQLite's security notes](https://www.sqlite.org/security.html)
  are where the assumptions start: the library is not defended against hostile SQL, so the host has to
  be.
- **The quotas** — tables per page, columns per table, rows and bytes per (account, page), in a
  `Pages:UserStore` section with every value required (DEC-282).
- **The lifecycle** — a column added is fine, a column removed is refused or confirmed, a page deleted
  takes its tables with it, `users remove` clears the account's rows, and an archive never carries any
  of it, because it is personal data rather than content.
- **What would make it unnecessary** — a page that keeps fewer than a screenful of values and never
  orders or searches them: then JSON documents were enough, and the job says so.

### D5 — The housekeeping the re-scope owed

The documents that pointed at the withdrawn capability were re-pointed, because a plan that leaves
them pointing at nothing is how a rule erodes. The corrections are one line each and are listed in
"What this touches": IDEA-025 gains the memory view, IDEA-024's citation becomes DEC-190, the
memory-page plan's **Superseded** row points at IDEA-025, and [assistant](../assistant.md) says the
view stays shell until the assistant is a page. The [glossary](../glossary.md) gains **Bootstrap
route**, because the audit is unreadable without it, and **Memory page** gains the sentence about where
the view moves.

[Storage](../storage.md)'s sentence — "Nothing in `Sdk`, `Runtime` or the page contract reaches
memory" — is already true and stays; the withdrawal removes the exception that was going to be made to
it.

## Considered and rejected

- **Keeping the memory capability and moving the page.** It would work today and cost a capability in
  `Sdk` that the assistant page then has to coexist with, for one view of data the assistant already
  owns. Two owner-binding paths to the same rows is the drift this plan exists to avoid.
- **Moving memory onto the new per-account store.** The sharpest version of "no host support": memory
  becomes a page's tables and the tools read them. Rejected because it inverts the dependency — the
  assistant's tools would read a schema a page declared, and every rule of memory (versioning, pins,
  secrets, the cap, recall) would have to be enforced in page code the tools cannot rely on.
- **Building per-account storage now, since it is the real gap.** Rejected for this step: no page uses
  it yet, the SQL shape needs a sandbox and a schema lifecycle, and a capability arrives with the view
  that needs it. It is a job with the analysis in it, ready to be scheduled.
- **JSON documents now and SQL later.** Cheaper, but it is the shape that would have to be thrown away
  the first time a page wants an owner's rows sorted, searched or counted. The job records documents as
  the thing to revisit if the first customer turns out small.
- **One store per (account, page).** Isolation is tidier, but the data is the account's: a page that
  shows a profile another page keeps, and the assistant page reading what a memory view wrote, are one
  account's rows. Per account matches `IPageSession`'s read rule and is what the owner chose.
- **Deleting DEC-211.** Deleting a decision loses the record of a question someone will ask again. It
  moved to Superseded with the date and the destination.
- **Doing the audit's remaining routes now.** Each needs a capability the contract does not have, and
  each is a job. The rule is worth having before the work, not after it.
- **Making `/status` a page.** It is bootstrap: what the owner reads when a page did not load.

## Components

| Component | Responsibility | Where |
| --- | --- | --- |
| The rule and its audit | Where a view lives, and why each shell route is where it is | `docs/architecture.md` |
| This plan | The re-scope, the rejected options and the destination of the withdrawn capability | `docs/plans/page-first.md` |
| DEC-211, superseded | Why there is no memory capability in `Sdk` | `docs/decisions.md#superseded` |
| DEC-291 | Memory is the assistant's; the gap is per-account storage | `docs/decisions.md` |
| IDEA-033 | The per-account store: shape, questions, cost, trigger | `docs/jobs/IDEA-033.md` |
| IDEA-025 | The assistant page, memory view included | `docs/jobs/IDEA-025.md` |
| The programme's step 9 | What this step is, and what it is not | `docs/jobs/README.md` |

## Tests

None. This step changed documents only, and the repository has no documentation test: there is no
link-or-anchor suite to extend, and inventing one is a different job.

**The gate** was `make check` as for every step, with `license-check` the one that mattered, because
the new job file carries an SPDX header like every other file, plus a manual pass over the relative
links and anchors the change touched:

- `docs/plans/page-first.md#the-rule` still resolves from DEC-210;
- every new `../jobs/IDEA-033.md` link resolves from the plan and from the jobs table;
- the Superseded anchor from `docs/decisions.md` resolves;
- the audit table's citations all name records that exist (DEC-210, DEC-255, DEC-256, the streaming
  plan, IDEA-024, IDEA-025).

## Phases

Each phase ended with a green `make check`; there was nothing to test first.

### Phase 0 — the rule and the audit

- "Where a view lives" and the audit table in [architecture](../architecture.md#where-a-view-lives).
- **Bootstrap route** in the [glossary](../glossary.md), and the sentence about where the memory view
  moves in **Memory page**.

**Done when** a reader can tell, without opening a plan, why each shell route is in the shell and what
would move it.

### Phase 1 — decisions

- DEC-211 in Superseded: dated, "never built", with IDEA-025 and IDEA-033 named.
- DEC-291 recorded, and DEC-125's open-policy note pointed at IDEA-033.

**Done when** nothing in `docs/` describes a memory capability that does not exist, and the reason it
does not is one link away.

### Phase 2 — the plan and the jobs

- This plan, in place of the first version.
- [IDEA-033](../jobs/IDEA-033.md) written.
- The memory view in IDEA-025; IDEA-024's citation corrected to DEC-190.
- The jobs README: open table, programme step 9 and the note under the programme.
- The memory-page plan's **Superseded** row pointed at IDEA-025; the sentence added to
  [assistant](../assistant.md).

**Done when** the plan says what it delivers, the job says what the capability costs, and every
document that mentioned the withdrawn capability points at where the question went.

## Risks

| Risk | Mitigation |
| --- | --- |
| **A withdrawn decision is read as a deleted one.** Someone re-proposes `IMemoryCatalog` in three months. | The Superseded row carries the date, the reason and the destination, and this plan keeps "what changed, and why" including the option that was rejected. |
| **The audit rots.** A new shell route is added without a reason. | The rule and the table sit in the section a contributor reads before adding a view, and the table names the record for each row, so a missing row is visible. |
| **IDEA-033 becomes a licence to build a database engine.** A page-declared schema with SQL access is the largest thing the page contract would ever have. | The job says what it costs, names the sandbox and the caps, and requires a named first customer. It is a job, not a plan, until the owner schedules it. |
| **Per-account storage is never scheduled, so the gap stays.** | The gap is written down in three places — this plan, the job and DEC-125's note — and the trigger that would justify it early is in the job. |
| **The job number was claimed elsewhere.** IDEA-032 is on `origin/idea-032`, which is unmerged. | IDEA-033 was the next free number on trunk and on that branch; a later branch that takes it renumbers this one. |
| **The assistant page never happens, so `/assistant/memory` stays shell forever.** | That is an honest resting place: it is bootstrap-adjacent shell code with its own decision (DEC-205), and the audit names IDEA-025 as the way out. |

## Documentation and decisions

- **DEC-210** stands: the rule and its three reasons. Its list of examples loses `/edit`, which became
  a page when the editor did, and names the recovery routes the audit covers.
- **DEC-211** is in [Superseded](../decisions.md#superseded) — never built; the view is IDEA-025's and
  the capability is IDEA-033's.
- **DEC-291** records the re-scope: memory is the assistant's and its view arrives with the assistant
  page; the page contract's missing capability is per-account storage.
- **DEC-205–DEC-209** stand: the shell memory page is what exists today, and every rule it keeps moves
  with the view when IDEA-025 replaces it.
- Written by this step: [architecture](../architecture.md#where-a-view-lives) (the rule and the audit),
  this plan, [IDEA-033](../jobs/IDEA-033.md), [the jobs](../jobs/README.md), the
  [glossary](../glossary.md), [assistant](../assistant.md), and one line each in IDEA-024, IDEA-025 and
  the archived memory-page plan.

## Delivered when

- `docs/architecture.md` states the rule and carries the audit, and the glossary defines **Bootstrap
  route**.
- DEC-211 is in Superseded, dated and explained, and DEC-291 is recorded.
- This plan says what it did and did not build, and why the first version was wrong.
- `docs/jobs/IDEA-033.md` exists, with the per-account SQL shape, the open questions, the cost, the
  privacy work and the trigger that would make documents enough, and it is in the open jobs table.
- `docs/jobs/IDEA-025.md` carries the memory view, and IDEA-024 cites DEC-190 rather than DEC-211.
- Nothing in `docs/` describes `IMemoryCatalog`, `IMemoryWriter` or a `/memory` seed as work in hand.
- `make check` passes.

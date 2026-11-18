<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The job record

| | |
| --- | --- |
| Status | Proposed 2026-11-18 — waits for the owner. Nothing below is built |
| Size | M — a naming sweep over 23 open jobs and the live documents that cite them, one new checker, one Makefile target, and the two tables in the backlog index becoming one |
| Area | `docs/jobs`, `docs/plans`, the live docs that cite jobs, `CONTRIBUTING.md`, `Makefile`, `uk.osric.asp.slnx`, a new `tools/check-jobs` |
| Spike | None. Every claim about what exists was read off `trunk` on 2026-11-18: the 23 job files and the two tables in [the index](../jobs/README.md), [the closed list](../jobs/closed.md), [the whims](../jobs/WHIMS.md), the `Backlog` rows of the live plans, and the citations the live documents and comments carry |
| Decisions | At the merge: **product-a-job-is-a-name-not-a-number** and **code-the-gate-checks-the-backlog** |
| Backlog | This plan is the job. There is no `IDEA-` file because nothing about the site is wrong; the work is the record itself. [WHIM-03](../jobs/WHIMS.md) — the backlog is not on the site — is adjacent and stays a whim |

## The short version

The backlog is 23 open jobs, and the only thing that indexes it is a number. The relations that
matter — what blocks what, which plan a job belongs to, which jobs are the same work — exist as
sentences in prose, so nothing can check them and the owner has to hold them in his head.

This plan gives a job **a name** in place of a number, gives its header **the facts it is ordered
by**, reduces the index to **one table that is the order**, and adds **`make jobs`** to prove the
record is consistent. It is the move [the decision record](../decisions.md) already made, applied to
the other record in the repository.

- A job is `kind-subject`: `idea-check-without-storing`, `sec-page-runs-in-process`. The kind keeps
  what `IDEA-`/`SEC-` carried; the subject states the gap. The name is the id, cited in backticks in
  a job, a plan, a commit or a branch.
- The header carries **Size** (difficulty), **Value** (utility), **Blocked by** (dependency),
  **Status** and **Plan**. Kind comes from the file name.
- **Priority is the row's position** in the one order table, not a field. The table is a total order
  with a reason per row; `make jobs` refuses an order that puts a job above something that blocks it.
- `closed.md` is frozen: an `IDEA-` number written before the sweep keeps meaning what it meant, the
  way [`docs/archive/decisions.md`](../archive/decisions.md) does.

## What exists today

| | |
| --- | --- |
| The open jobs | 23 files, each with a header table of Size, Area, Location, Spike (and sometimes a Link or a Status) and an H1 title sentence |
| The index | Two tables in [the README](../jobs/README.md): an inventory of `ID`, Size, Severity, Plan and title, and an order of `#`, Job, Size and "Why here" |
| Duplication | Size is in the file header and in both tables, so one judgement is written in three places and can disagree with itself |
| Priority | The order table's `#`, which is a good total order with a written reason — and nothing else |
| Utility | Not recorded anywhere |
| Dependency | Prose only: IDEA-047 calls its batch "IDEA-037's work, borrowed", IDEA-025 "needs the account store beneath it, and that is IDEA-033", and IDEA-047 "follows IDEA-042 and IDEA-038" |
| Status | Implied by where a thing is: a file here is open, a row in `closed.md` is closed, a branch elsewhere is being worked. Nothing says "blocked" or "parked" except the prose |
| Links, job → plan | The header sometimes carries a `Plan` row; the IDE plan's `Backlog` row points IDEA-041 at `closed.md` |
| Links, plan → job | The `Backlog` row of a live plan, sometimes as links and sometimes as plain text: `assistant-owner-interrupt.md` says ARCH-17, and `page-response.md` says (IDEA-050) with no link |
| Citations that do not resolve | `docs/testing.md` cites `BUG-11`, which is a closed row; two code comments cite IDEA-023 and BUG-17, and neither is in the record at all |
| The working precedent | [The decision record](../decisions.md) is named, cited in backticks, and checked by [check-decisions](../../tools/check-decisions/Program.cs) — 281 lines that print `file:line` problems and run in `make check` |

The numbers fail for the reason the decision record already wrote down: a number tells a reader
nothing, and two people counting alone can take the same one. One owner counting alone still cannot
see the graph, and the graph is the part that is getting unwieldy.

## What the owner asked for, and where it lives

| Want | Field or place | What proves it |
| --- | --- | --- |
| Priority | the row's `#` in the one order table | `make jobs` refuses an order that breaks a dependency, and a job missing from the order |
| Difficulty | `Size` in the header and the row | one of `S`, `M`, `L`, and the two agree |
| Utility | `Value` in the header and the row | one of `low`, `medium`, `high`, and the two agree |
| Dependency | `Blocked by` in the header | every target resolves, no cycle, every open target is earlier in the order |
| Status | `Status` in the header; `closed.md` for closed | `blocked` exactly when an open blocker exists; `open` exactly when none does; `parked` is the owner's choice |
| Ideas and plans linked | `Plan` in the header, `Backlog` in the plan | both directions resolve |
| Names for ideas | the file name | unique, kind-prefixed, and every citation resolves |

## The design

### 1. A name, not a number

A job's file name is its id and the name is cited in backticks wherever the id would have been:

```
docs/jobs/idea-check-without-storing.md
docs/jobs/sec-page-runs-in-process.md
```

The shape is `^(idea|bug|sec|ops|arch)-[a-z0-9]+(-[a-z0-9]+)*$`.

- **The kind stays in the name.** It is the old `IDEA-`/`SEC-`/`OPS-`/`ARCH-`/`BUG-` prefix, kept
  because a bug is not an idea and the reader should see that before opening the file.
- **The subject states the gap**, in two to five words, the way a decision states the rule:
  `idea-editor-is-a-textarea`, not just *idea-editor*. A name that would fit two jobs is too broad, and
  the second job is what finds out.
- **The kind prefix keeps the two records apart.** The decision areas are fixed as `product`,
  `code`, `pages`, `shell`, `auth`, `assistant`, `deploy` and `metrics`; the job kinds are none of
  them. `check-decisions` already ignores a backticked token whose first segment is not an area, so a
  job name is never read as a decision and the two namespaces cannot collide. `make jobs` proves the
  guard by refusing a job name that starts with a decision area.
- **The H1 keeps the sentence.** `# A page cannot check a template without saving it` stays; the
  name is the short id, the sentence is the description.
- **A name is permanent once it is on trunk.** A name that was wrong before the merge is fixed with
  `git mv` and every citation in the same change, exactly as the decision record narrows a name
  before it lands. After the merge, `closed.md` is where the number lives.

### 2. The header carries the facts

The existing header table gains four rows and keeps three. Nothing new is invented: the header is
already the block a job read alone carries.

| | |
| --- | --- |
| Size | `S` — a sitting, `M` — a day or so, `L` — big enough to want a plan of its own |
| Value | `low` / `medium` / `high` — what the owner gets when it is done |
| Status | `open` / `blocked` / `parked` |
| Blocked by | `` `idea-check-without-storing` ``, comma-separated, or `—` |
| Plan | [ide-host](ide-host.md), or `—` |
| Area | as today |
| Location | as today |
| Spike | as today |

- **Kind is not a row.** It is the file name's first segment, so it cannot disagree with itself.
- **Dependency names an open job, or a closed row by its frozen number.** A closed blocker is
  satisfied and does not constrain the order; an open one must come earlier.
- **Status is derived and then checked.** `blocked` means at least one blocker is still open; `open`
  means none is; `parked` is the one state the graph cannot see, so it is the one the owner writes.
  A stored `blocked` with no open blocker is a problem, and so is an `open` job with one — that is
  what stops the field becoming a second, staler opinion.
- **There is no `Priority` row.** A priority field competes with the order table and loses, because
  the order is a total order and a label is not. The row's number is the priority.

### 3. One table is the index and the order

The inventory table goes. The order table already lists every open job, and a job that is not in it
is a job nobody will pick up; a second table that lists the same jobs with the same sizes is a second
place to be wrong.

| # | Job | Size | Value | Status | Blocked by | Why here |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | idea-message-does-not-survive-a-redirect | S | high | open | — | The smallest job on the list that unblocks an ordinary page: a message that survives a redirect. |
| … | | | | | | |

- The `Job` cell is a link whose text is the name, so the table is an index of ids rather than of
  sentences. The sentence is the file's H1 and one click away. (If the owner prefers the title in the
  row, see [Open questions](#open-questions).)
- `Size` and `Value` appear in the row and in the header. That duplication is deliberate and caught:
  the table is what a reader scans, the header is what a job carries when it is read alone, and
  `make jobs` fails when the two disagree.
- The rows are numbered `1..n` in order. A gap or a repeat is a problem.
- The order remains the owner's judgement, moved as steps are worked. Nothing computes it.

### 4. What `make jobs` proves

A new tool, `tools/check-jobs`, in the shape of `check-decisions`: an `Exe` project, SPDX headers,
`make jobs`, and a line in `make check` beside `decisions`. It reads the repository and prints
`file:line` problems, and exits non-zero on any.

1. Every file under `docs/jobs` other than `README.md`, `closed.md` and `WHIMS.md` is a job with a
   name of the right shape and a kind that is not a decision area.
2. No name is used twice, across open jobs and the names in `closed.md`.
3. Every header carries `Size`, `Value`, `Status`, `Blocked by` and `Plan`, each from its allowed
   set.
4. Every open job appears exactly once in the order table; every row names an open job; the numbers
   run `1..n`; the row's `Size` and `Value` equal the header's.
5. Every `Blocked by` target resolves — an open name or a closed row — and no open blocker sits at or
   below its dependent. An open blocker must be strictly earlier. Every open blocker is `blocked`;
   no open blockers is `open`, unless the job is `parked`.
6. The graph has no cycle through open jobs.
7. A `Plan` resolves to a live plan, and that plan's `Backlog` row links back to the job. A plan may
   name several jobs; every one of them links back.
8. A backticked job name cited in any `.md` or `.cs` resolves to an open job or a name in
   `closed.md`. A backticked `IDEA-`/`SEC-`/`OPS-`/`ARCH-`/`BUG-` number in a live document is
   allowed only when `closed.md` has that row; a number in plain text is prose, not a citation, and
   the map below is the case that needs one. In `docs/jobs/closed.md`, `docs/archive/`,
   `docs/plans/archive/` and `docs/jobs/WHIMS.md` the old numbers are history and are not policed.
9. `closed.md` is not rewritten except to append. A new row's first column is a job name, and its
   "Fix" cell says what closed it and which decisions it recorded, as the rows do today.

The checker does not derive what git already knows: a closed job is a row, and "in progress" is a
branch. Nothing is written about either.

### 5. Whims and closed jobs

- **Whims** become `whim-subject` (`whim-lsp-for-the-editor`, `whim-backlog-on-the-site`), keeping
  the one-line shape and no file. A whim is promoted to a job by writing the file and removing the
  line, and the name it carries is the one it had. The checker enforces the shape and uniqueness.
  If the owner would rather leave the four numbers alone, nothing else in this plan depends on it.
- **Closed jobs stay numbered.** `closed.md` is frozen in the sense that
  [`docs/archive/decisions.md`](../archive/decisions.md) is: a reader, a plan or a commit written
  before the sweep still means what it meant. New rows carry names; old rows do not move.

### 6. The sweep

The names are mechanical and finite. A `git mv` per job, then the citations:

- `docs/jobs/README.md` becomes the one table and the scheme's prose;
- the live plans' `Backlog` rows name open jobs;
- the live docs that cite an open job retarget: `architecture.md`, `assistant.md`, `pages.md`,
  `testing.md`, `glossary.md`, `decisions.md`, `deploy/README.md`, `SECURITY-REVIEW.md` and
  `.github/copilot-instructions.md`;
- the archived plans keep their prose but their links to the 23 renamed jobs are retargeted, so a
  reader of an old plan lands on the job that file became rather than on a `404`. A link that already
  points at a deleted job file is left as it is: that debt predates this change. The retarget is
  mechanical — the map below is the whole of it — and `docs/jobs/closed.md` is not touched.

The proof that the sweep is complete is a `git grep` for the old numbers outside the frozen files,
and the checker's rule 8 for a backticked citation that survives.

## The name map

The titles are unchanged; only the file name and the citation change.

| Was | Name | The gap it records |
| --- | --- | --- |
| SEC-03 | `sec-page-runs-in-process` | Page code has no execution budget and no process boundary |
| OPS-03 | `ops-no-ci` | There is no CI pipeline |
| OPS-05 | `ops-no-outage-notification` | No outbound notification when the site breaks |
| IDEA-001 | `idea-editor-is-a-textarea` | The editor is a plain textarea |
| IDEA-020 | `idea-import-cannot-reset-a-site` | Import only adds and overwrites |
| IDEA-024 | `idea-archive-is-not-a-form` | Import and export are shell code |
| IDEA-025 | `idea-conversation-is-not-a-page` | The conversation is a stream and host actions |
| IDEA-027 | `idea-message-does-not-survive-a-redirect` | A page cannot leave a message for the page it sends a visitor to |
| IDEA-029 | `idea-vscode-cannot-check-a-page` | VS Code cannot check a page as it is typed |
| IDEA-031 | `idea-policy-is-a-constant` | Some policy is still a constant in code |
| IDEA-033 | `idea-no-account-store` | A page has no persistent per-account store |
| IDEA-037 | `idea-check-without-storing` | A page cannot check a template without saving it |
| IDEA-038 | `idea-cannot-search-the-store` | A page cannot search the store's source |
| IDEA-039 | `idea-listing-has-no-cursor` | A page cannot ask a listing for the next page |
| IDEA-040 | `idea-library-edit-cost-is-invisible` | Nothing says which pages a library edit recompiles |
| IDEA-042 | `idea-page-cannot-move` | A page cannot move a page |
| IDEA-044 | `idea-no-browser-render` | A page cannot render a page in a browser |
| IDEA-047 | `idea-one-write-per-render` | A render may write a batch, and the caller names the policy |
| IDEA-050 | `idea-old-response-spelling` | The old response spelling is still on `IContext` |
| IDEA-052 | `idea-assistant-cannot-ask` | The assistant cannot ask the owner a question |
| IDEA-053 | `idea-tool-arguments-say-nothing` | Twenty tools' arguments say nothing about themselves |
| IDEA-054 | `idea-assistant-cannot-check-a-page` | The assistant can check a script but not a page |
| IDEA-055 | `idea-nothing-checks-page-assets` | Nothing checks a page's CSS, HTML or SVG |

A job that lands before the sweep — `IDEA-056` is sitting on the main worktree as this is written — is
named by the same rule when it lands: `idea-job-writes-as-its-starter`.

## What it touches

| | |
| --- | --- |
| Renamed | 23 job files, and the four whim lines if the owner takes that slice |
| Rewritten | `docs/jobs/README.md` — the scheme and one table |
| Retargeted | The `Backlog` rows of the live plans; the nine live documents above; the archived plans' links to the 23 renamed jobs |
| Added | `tools/check-jobs/check-jobs.csproj`, `Program.cs`; a `jobs` target in the `Makefile`; a line in `uk.osric.asp.slnx` and in `make check` |
| Updated | `CONTRIBUTING.md` (a branch is named for the job's name, not its number) and `.github/copilot-instructions.md` (the citation rule) |
| Untouched | `docs/jobs/closed.md` beyond a freeze note, `docs/decisions.md`'s entries (only their job links move), the product assemblies |

## Tests

The checker is a tool, and `check-decisions` has no suite; this follows that precedent rather than
inventing a test project for `tools/`. The proof is the record plus a deliberate break, run on a copy
in a scratch directory and recorded as evidence on the branch:

1. The real tree passes: `make jobs` prints the job count, the citation count and no problems.
2. Remove a job's row from the table — a problem naming the job.
3. Put a job above its open blocker — a problem on the row and on the blocker.
4. Make two jobs block each other — a cycle problem on both.
5. Point a `Plan` at a plan whose `Backlog` does not link back — a problem on the header and the plan.
6. Cite a job name that is not in the record in a live document — a problem on the citation.
7. Leave an open job cited by its old number in backticks in a live document — a problem on the
   citation.
8. Write `Status: blocked` on a job with no open blocker — a problem on the field.
9. The full gate: `make format-check license-check audit decisions jobs check-pages test publish`,
   or `make check`.

No product test changes: nothing under `src/` or `test/` changes behaviour. If the owner wants the
checker itself guarded by a suite, that is a small NUnit project and a fourth `tools/` entry — see
[Open questions](#open-questions).

## Documentation

- [The jobs index](../jobs/README.md) states the scheme once: names, the header's fields, the one
  table, and what `make jobs` proves.
- [CONTRIBUTING.md](../../CONTRIBUTING.md) says a branch is named for the job's name; the job-id
  examples become names, and the old IDs stay in the history the page already points at.
- [.github/copilot-instructions.md](../../.github/copilot-instructions.md) cites the policy job by its
  new name and says a job is named and checked.
- [decisions.md](../decisions.md) has its job links retargeted; no entry changes meaning.
- **`/help` is unchanged.** This plan adds no context member, capability, layout slot, editor action,
  content type or response rule, so there is nothing a page author does differently.

## The record at the merge

- **product-a-job-is-a-name-not-a-number** — a job is identified by a `kind-subject` name, cited in
  backticks; the header carries Size, Value, Status, Blocked by and Plan; the order table is the
  priority; `closed.md` is frozen. Recorded in this plan and
  [the index](../jobs/README.md).
- **code-the-gate-checks-the-backlog** — `make check` runs `make jobs`, which proves the names,
  the fields, the order against the dependency graph and both directions of the plan link. Recorded
  in this plan, [the index](../jobs/README.md) and the `Makefile`.

## Slices

1. **The names.** Rename the 23 jobs, retarget every citation and link, rewrite the index's prose.
   No new fields and no tool yet; the tree is consistent by hand and `make check` still passes.
2. **The facts.** Add Size, Value, Status, Blocked by and Plan to each header, fold the two tables
   into the one order table, fill Value and the blockers from the order's own prose.
3. **The proof.** `tools/check-jobs`, `make jobs`, the `slnx` and `make check` entries, the
   `CONTRIBUTING.md` and instruction-file updates, and the nine broken-copy proofs.

Slices 1 and 2 are the same branch; slice 2 is what makes slice 3 possible, because the checker
reads the shape slice 2 creates. If the owner would rather merge the names first and the checker
after, slice 1 stands alone and the other two wait.

## Risks

- **A live document cites a job in plain text and the sweep misses it.** The checker's rule 8 is the
  answer, and it is why the legacy-number ban is part of the tool rather than a note in this plan.
- **Archived plans lose their links.** Retargeting them is the one edit the archive takes; leaving
  them 404s is worse than touching a link, and the change to each is mechanical and reviewable.
- **The table gets wide.** Names are the ids, and a wide row is the cost of one index instead of
  three. The owner can drop the title from the row (it is the H1) — see below.
- **A stored status goes stale.** `blocked` and `open` are checked against the graph, so only
  `parked` can drift, and it is the state that exists precisely to be a human decision.
- **A name outgrows its kind.** A job that turns out to be a defect is renamed `idea-…` → `bug-…`
  before it merges, with its citations; after the merge it is history and the number in `closed.md`
  is what it was.
- **`make check` gets slower.** Two checkers over the tree, both linear scans; `check-decisions`
  already does the same walk.

## Open questions

1. **`Value` or `Utility`?** The plan uses `Value` because that is the word a reader of the table
   understands; the owner asked for "utility".
2. **Does the row carry the title?** The plan makes the `Job` cell the name alone. Adding the H1
   sentence after the link is possible, at the cost of a wider table.
3. **A suite for the checker?** Following `check-decisions` means none; a small NUnit project would
   guard the parser and the graph rules, at the cost of a fourth `tools/` project.
4. **Name the whims too?** Recommended, and independent of the rest.
5. **Branch name.** The full name (`idea-check-without-storing`) or the subject alone
   (`check-without-storing`)? The first is unambiguous and greppable; the second is shorter to type.

## Not in scope

- **GitHub Issues, Projects or Azure DevOps.** They model every field above and do it well; they move
  the record out of the repository, out of the diff and out of the gate, and that is the property
  this plan is keeping. A board is a view, and a view can be generated later ([WHIM-03](../jobs/WHIMS.md)).
- **A tracker tool.** A checker is a day; a tracker with transitions, filters and a UI is a second
  product.
- **A computed priority score.** The order with a reason per row is the decision; a score is a
  second opinion that can disagree with it.
- **Status from git.** "In progress" is a branch and stays one; the record does not repeat what
  `git branch` already answers.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The backlog record

| | |
| --- | --- |
| Status | Built 2026-11-19 — waits for the merge. The folder, the 26 slugs, the fields, the one table and `make backlog` are all in place; the eight completed plans are archived and the merge step now says so. The gate is green |
| Difficulty | Determined Mountain Goat 🐐💪 — a folder rename and a slug sweep over 26 open items, one new checker, one Makefile target, and the index's two tables becoming one, with care needed where a frozen document's links move |
| Area | `docs/jobs` → `docs/backlog`, `docs/plans`, the live documents that cite an item, `CONTRIBUTING.md`, `.github/copilot-instructions.md`, `Makefile`, `uk.osric.asp.slnx`, a new `tools/check-backlog` |
| Spike | None. Every claim about what exists was read off `trunk` on 2026-11-18: the 26 item files and the two tables in [the index](../backlog/README.md), [the closed list](../backlog/closed.md), [the whims](../backlog/WHIMS.md), the `Backlog` rows of the live plans, and the citations the live documents and comments carry |
| Decisions | At the merge: **product-backlog-items-are-slugs**, **product-a-difficulty-is-not-a-duration**, **product-a-merged-plan-is-archived** and **code-the-gate-checks-the-backlog** |
| Backlog | This plan is the whole of the work. Nothing about the site is wrong, so there is no item file: the record is what changes. [`whim-backlog-on-the-site`](../backlog/WHIMS.md) is adjacent and stays a whim |

## The short version

The backlog is 26 open items, and the only thing that indexes it is a number. The relations that
matter — what blocks what, which plan an item belongs to, which items are one step — exist as
sentences in prose, so nothing can check them and the owner has to hold them in his head.

This plan gives the record **its own word**, gives an item **a slug** in place of a number, gives its
header **the facts it is ordered by**, reduces the index to **one table that is the order**, and adds
**`make backlog`** to prove the record is consistent. It is the move [the decision
record](../decisions.md) already made, applied to the other record in the repository.

- **The folder is `docs/backlog`, the entries are items, and `make backlog` is the check.** `job` is
  the host's word — `PageJob`, `PageJobRunner`, `PageJobsScope`, the assistant's runs — and the
  record stops using it.
- An item is **a slug and a title**: `idea-check-without-storing` in the file name, *A page cannot
  check a template without saving it* in the H1.
- The header carries **Difficulty** on an animal scale, **Utility**, **Status**, **Blocked by**
  (dependency) and **Plan**. Nothing in the record is a duration.
- **Priority is the row's position** in the one order table, not a field. The table is a total order
  with a reason per row; `make backlog` refuses an order that puts an item above something that
  blocks it.
- `closed.md` is frozen: an `IDEA-` number written before the sweep keeps meaning what it meant, the
  way [`docs/archive/decisions.md`](../archive/decisions.md) does.

## What exists today

| | |
| --- | --- |
| The open items | 26 files, each with a header table of Size, Area, Location, Spike (and sometimes a Link or a Status) and an H1 of `IDEA-nnn — title` |
| The index | Two tables in [the README](../backlog/README.md): an inventory of `ID`, Size, Severity, Plan and title, and an order of `#`, item, Size and "Why here" |
| Duplication | Size is in the file header and in both tables, so one judgement is written in three places and can disagree with itself |
| What Size measures | Duration: `S` is "a sitting", `M` "a day or so", `L` "big enough to want a plan of its own". That is a guess about the calendar dressed as a judgement about the work, and the owner asked for difficulty |
| Priority | The order table's `#`, which is a good total order with a written reason — and nothing else |
| Utility | Not recorded anywhere |
| Dependency | Prose only: IDEA-047 calls its batch "IDEA-037's work, borrowed", IDEA-025 "needs the account store beneath it, and that is IDEA-033", and IDEA-056 waits on IDEA-047 |
| Status | Implied by where a thing is: a file here is open, a row in `closed.md` is closed, a branch elsewhere is being worked. Nothing says "blocked" or "parked" except the prose |
| Links, item → plan | The header sometimes carries a `Plan` row; the IDE plan's `Backlog` row points IDEA-041 at `closed.md` |
| Links, plan → item | The `Backlog` row of a live plan, sometimes as links and sometimes as plain text: `assistant-owner-interrupt.md` says ARCH-17, and `page-response.md` says (IDEA-050) with no link |
| Two things called a job | The record's unit of work, and the host's `PageJob`/`PageJobRunner`/`PageJobsScope`/`IWorkerContext` and the assistant's runs. IDEA-056 is about the host's job and sits in a folder whose README calls everything a job |
| Citations that do not resolve | `docs/testing.md` cites `BUG-11`, which is a closed row; two code comments cite IDEA-023 and BUG-17, and neither is in the record at all |
| The working precedent | [The decision record](../decisions.md) is identified by a slug, cited in backticks, and checked by [check-decisions](../../tools/check-decisions/Program.cs) — 281 lines that print `file:line` problems and run in `make check` |

The numbers fail for the reason the decision record already wrote down: a number tells a reader
nothing, and two people counting alone can take the same one. One owner counting alone still cannot
see the graph, and the graph is the part that is getting unwieldy.

## What the owner asked for, and where it lives

| Want | Field or place | What proves it |
| --- | --- | --- |
| Priority | the row's `#` in the one order table | `make backlog` refuses an order that breaks a dependency, and an item missing from the order |
| Difficulty | `Difficulty` in the header and the row | one of the ten animals, and the two agree; never a duration |
| Utility | `Utility` in the header and the row | one of `low`, `medium`, `high`, and the two agree |
| Dependency | `Blocked by` in the header | every target resolves, no cycle, every open target is earlier in the order |
| Status | `Status` in the header; `closed.md` for closed | `blocked` exactly when an open blocker exists; `open` exactly when none does; `parked` is the owner's choice |
| Ideas and plans linked | `Plan` in the header, `Backlog` in the plan | both directions resolve |
| A slug for each idea | the file name | unique, kind-prefixed, and every citation resolves |
| One word for the record | `docs/backlog`, `make backlog`, the noun *item* | the gate, and the host keeps *job* |

## Settled by the owner

| Question | Answer, 2026-11-18 |
| --- | --- |
| `Value` or `Utility` for the column? | **`Utility`** |
| Does the order table carry the title? | **Yes** — the row carries the slug and the title |
| A suite for the checker? | **No** — `check-decisions` has none and this follows it |
| Are the whims given slugs too? | **Yes** |
| What is a branch called? | **`kind/filename`** — `idea/check-without-storing`, `sec/page-runs-in-process`; a plan's branch is `plan/<plan file name>`, which is `plan/backlog-record` for this one |
| Is the record still called *jobs*? | **No** — the folder, the make target and the noun all become **backlog** and **item** |
| Is difficulty a size or a level? | **A level, on the animal scale** the `animal-difficulty-scale` skill sets out — never a duration |

## The design

### 1. A slug and a title

An item has two strings, and the plan uses one word for each:

- **slug** — the short, kind-prefixed file stem that identifies the item: `idea-check-without-storing`.
- **title** — the sentence a reader reads: *A page cannot check a template without saving it*. The
  title is the file's H1, and the H1 is nothing else.

*Name* is the word the decision record uses for its ids, and this plan does not use it for an item.

```
docs/backlog/idea-check-without-storing.md
docs/backlog/sec-page-runs-in-process.md
```

The shape is `^(idea|bug|sec|ops|arch)-[a-z0-9]+(-[a-z0-9]+)*$`.

- **The kind stays in the slug.** It is the old `IDEA-`/`SEC-`/`OPS-`/`ARCH-`/`BUG-` prefix, kept
  because a bug is not an idea and the reader should see that before opening the file.
- **The subject states the gap**, in two to five words, the way a decision states the rule:
  `idea-editor-is-a-textarea`, not just *idea-editor*. A slug that would fit two items is too broad,
  and the second item is what finds out.
- **The kind keeps the two records apart.** The decision areas are fixed as `product`, `code`,
  `pages`, `shell`, `auth`, `assistant`, `deploy` and `metrics`; the item kinds are none of them.
  `check-decisions` already ignores a backticked token whose first segment is not an area, so an item
  slug is never read as a decision and the two namespaces cannot collide. `make backlog` proves the
  guard by refusing a slug that starts with a decision area.
- **The H1 becomes the title**, with the number dropped: `# A page cannot check a template without
  saving it`. The slug needs no repeat in the body — it is the file name, and every citation of it is
  a backticked slug.
- **A slug is permanent once it is on trunk.** A slug that was wrong before the merge is fixed with
  `git mv` and every citation in the same change, exactly as the decision record narrows a decision
  name before it lands. After the merge, `closed.md` is where the number lives.
- **A branch is `kind/filename`**: `idea/check-without-storing` for that item, and
  `plan/backlog-record` for this plan. `make worktree BRANCH=idea/check-without-storing` already
  makes the nested folder and the branch; nothing in the Makefile changes for it.

### 2. The header carries the facts

The existing header table renames one row and gains four. Nothing is invented: the header is
already the block an item read alone carries.

| | |
| --- | --- |
| Difficulty | one of the ten levels below, written in the header — `caffeinated-squirrel` |
| Utility | `low` / `medium` / `high` — what the owner gets when it is done |
| Severity | a defect only, and kept as it was: the reviewer's risk assessment |
| Status | `open` / `blocked` / `parked` |
| Blocked by | `` `idea-check-without-storing` ``, comma-separated, or `—` |
| Plan | [ide-host](ide-host.md), or `—` |
| Area | as today |
| Location | as today |
| Spike | as today |

**The difficulty scale is the animal one.** Ten levels, each an animal and a condition, and the
level is a judgement about the work rather than a promise about the calendar. The backlog README
carries the list so `make backlog` has one place to read it, and it is the scale the
`animal-difficulty-scale` skill uses.

| # | Level | Emoji | What it says |
| --- | --- | --- | --- |
| 1 | `sleepy-hamster` | 😴🐹 | Trivial — autopilot |
| 2 | `contented-capybara` | 😌🦫 | Easy — straightforward |
| 3 | `eager-beaver` | 🦫⚙️ | Moderate — some problem-solving |
| 4 | `caffeinated-squirrel` | ☕🐿️ | Tricky — attention to detail, several considerations |
| 5 | `determined-mountain-goat` | 🐐💪 | Challenging — careful planning, several steps |
| 6 | `persistent-salmon` | 🐟⬆️ | Difficult — real obstacles, fought against |
| 7 | `angry-honey-badger` | 🦡😠 | Very difficult — determined effort, will not back down |
| 8 | `protective-dragon` | 🐉🔥 | Formidable — a major undertaking with real risk |
| 9 | `cornered-porcupine` | 🦔📍 | Hardcore — everything can go wrong, sharp edges everywhere |
| 10 | `tiger-with-a-toothache` | 🐯🦷 | Extreme — complexity and pain together |

- **The level is not a duration.** The table carries the animal and its condition, never "a day" or
  "a week". Two items a level apart may be a coin toss; the point is to rank them, not to date them.
- **The old `L` meant "wants a plan of its own".** That judgement is the `Plan` row's alone now:
  presence of a plan says it has one, and difficulty says how hard it is. A `protective-dragon` may
  need no plan and an `eager-beaver` may need one, because the plan is about coordination, not size.
- **Kind is not a row.** It is the slug's first segment, so it cannot disagree with itself.
- **There is no `Title` row and no `Slug` row.** The H1 is the title and the file name is the slug.
- **Dependency names an open item, or a closed row by its frozen number.** A closed blocker is
  satisfied and does not constrain the order; an open one must come earlier.
- **Status is derived and then checked.** `blocked` means at least one blocker is still open; `open`
  means none is; `parked` is the one state the graph cannot see, so it is the one the owner writes.
  A stored `blocked` with no open blocker is a problem, and so is an `open` item with one — that is
  what stops the field becoming a second, staler opinion.
- **There is no `Priority` row.** A priority field competes with the order table and loses, because
  the order is a total order and a label is not. The row's number is the priority.

### 3. One table is the index and the order

The inventory table goes. The order table already lists every open item, and an item that is not in
it is an item nobody will pick up; a second table that lists the same items with the same levels is a
second place to be wrong.

| # | Slug | Title | Difficulty | Utility | Status | Blocked by | Why here |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `idea-message-does-not-survive-a-redirect` | A page cannot leave a message for a page it sends a visitor to | contented-capybara 😌🦫 | high | open | — | The least difficult item on the list that unblocks an ordinary page: a message that survives a redirect. |
| … | | | | | | | |

- The `Slug` cell is a link to the item's file whose text is the backticked slug, so the table is an
  index of ids that is also the navigation. (The example above has no link because the files do not
  exist yet; the sweep makes it one.)
- The `Difficulty` cell is the level's slug followed by its emoji (`contented-capybara 😌🦫`); the
  checker reads the slug, and the emoji is there to be seen rather than parsed.
- `Difficulty` and `Utility` appear in the row and in the header. That duplication is deliberate and
  caught: the table is what a reader scans, the header is what an item carries when it is read
  alone, and `make backlog` fails when the two disagree.
- The rows are numbered `1..n` in order. A gap or a repeat is a problem.
- **A row normally names one item.** Where two items ship as one step — IDEA-054 with IDEA-053 as
  its first slice today — the row names both, its `Difficulty` is no easier than any of theirs and its
  `Utility` is each of theirs. Every item still appears exactly once.
- The order remains the owner's judgement, moved as steps are worked. Nothing computes it.

### 4. What `make backlog` proves

A new tool, `tools/check-backlog`, in the shape of `check-decisions`: an `Exe` project, SPDX
headers, `make backlog`, and a line in `make check` beside `decisions`. It reads the repository and
prints `file:line` problems, and exits non-zero on any.

1. Every file under `docs/backlog` other than `README.md`, `closed.md` and `WHIMS.md` is an item
   whose slug has the right shape and whose kind is not a decision area.
2. No slug is used twice, across open items and the slugs in `closed.md`.
3. Every H1 is the title and nothing else. Every header carries `Difficulty`, `Utility`, `Status`,
   `Blocked by` and `Plan`, each from its allowed set.
4. Every open item appears exactly once in the order table; every row names an open item; the numbers
   run `1..n`; the row's `Title` is the item's H1; a row that names one item has its `Difficulty`
   and `Utility` equal to the header's, and a row that names several is no easier than any of them.
   No `Difficulty` is a duration, and no cell in the table is one either.
5. Every `Blocked by` target resolves — an open slug or a closed row — and no open blocker sits at
   or below its dependent. An open blocker must be strictly earlier. Every item with an open blocker
   is `blocked`; an item with none is `open`, unless it is `parked`.
6. The graph has no cycle through open items.
7. A `Plan` resolves to a live plan, and that plan's `Backlog` row links back to the item. A plan may
   name several items; every one of them links back.
8. A backticked slug cited in any `.md` or `.cs` resolves to an open item or a slug in `closed.md`.
   A backticked `IDEA-`/`SEC-`/`OPS-`/`ARCH-`/`BUG-` number in a live document is allowed only when
   `closed.md` has that row; a number in plain text is prose, not a citation, and the slug map below
   is the case that needs one. In `docs/backlog/closed.md`, `docs/archive/`,
   `docs/plans/archive/` and `docs/backlog/WHIMS.md` the old numbers are history and are not policed.
9. `closed.md` is not rewritten except to append. A new row's first column is a slug, and its "Fix"
   cell says what closed it and which decisions it recorded, as the rows do today.

The checker does not derive what git already knows: a closed item is a row, and "in progress" is a
branch. Nothing is written about either.

### 5. Whims and closed items

- **Whims get slugs too**, in the same one-line table and with no file of their own:
  `whim-lsp-for-the-editor`, `whim-backlog-on-the-site`, `whim-build-timings`. A whim is promoted to
  an item by writing the file and removing its line, and it keeps the slug it had. The checker
  enforces the shape and the uniqueness.
- **Closed items stay numbered.** `closed.md` is frozen in the sense that
  [`docs/archive/decisions.md`](../archive/decisions.md) is: a reader, a plan or a commit written
  before the sweep still means what it meant. New rows carry slugs; old rows do not move.

### 6. The sweep

The changes are mechanical and finite.

- `docs/jobs` becomes `docs/backlog`, with `README.md`, `closed.md` and `WHIMS.md` moving with it.
- 26 files are renamed to their slugs, and the H1 of each loses its number.
- The noun goes with them: *job* becomes *item* everywhere the record means its own unit of work.
  *Job* then means only the host's job, which is what IDEA-056 is about.
- Every live document that cites an item or links to the folder is retargeted: `architecture.md`,
  `assistant.md`, `pages.md`, `testing.md`, `glossary.md`, `decisions.md`, `deploy/README.md`,
  `SECURITY-REVIEW.md`, `.github/copilot-instructions.md`, `CONTRIBUTING.md` and the live plans.
- The `Makefile` loses `jobs` and gains `backlog` (the target, `.PHONY`, the help line and the
  `check` recipe); `uk.osric.asp.slnx` gains `check-backlog` in place of `check-jobs`.
- `docs/archive/decisions.md` and the archived plans take **link retargets only**: their prose and
  their decisions are frozen, but a moved folder would 404 every link into it. A link that already
  points at an item file that no longer exists is left alone — that debt predates this change.
  This is the last time the archive is touched for this sweep.
- `docs/jobs/closed.md` keeps its rows and its numbers; only its own location and its links out move.

The proof that the sweep is complete is a `git grep` for the old numbers and for `docs/jobs` outside
the frozen files, and the checker's rule 8 for a backticked citation that survives.

### 7. A completed plan is archived

The owner asked for this beside the record: a plan whose work has merged leaves `docs/plans/` for
`docs/plans/archive/`, in the same merge that writes its decision. A finished plan that stays live asks
to be read and is not; eight had been merged for days and still sat beside the ones being worked.

- **The merge step does it.** [The instructions](../../.github/copilot-instructions.md), [the
  record](../decisions.md#taking-a-name) and [how to work](../../CONTRIBUTING.md#working-on-it) now say
  the merge writes the entry, points the plan at it, marks the plan's Status merged and moves it.
- **The evidence is the item's.** A plan is completed when the item that carries it is in
  [closed.md](../backlog/closed.md); three of the eight had a Status line that still said proposed or
  waiting for a merge, and the move is where that was found. Their Status now says merged and names
  the decision and the closed row.
- **Every link moves with it.** A link into a moved plan is recomputed against the plan's new
  directory, including the links inside the moved plan itself, which all need one more `../`. The
  backlog checker does not police this; a link that pointed at a plan which has moved is a reader sent
  nowhere, and the change that moves it is the change that fixes it.

## The slug map

The titles are unchanged apart from the dropped number.

| Was | Slug | Title |
| --- | --- | --- |
| SEC-03 | `sec-page-runs-in-process` | Page code has no execution budget and no process boundary |
| OPS-03 | `ops-no-ci` | There is no CI pipeline |
| OPS-05 | `ops-no-outage-notification` | No outbound notifications when the site breaks |
| IDEA-001 | `idea-editor-is-a-textarea` | The editor is a plain textarea |
| IDEA-020 | `idea-import-cannot-reset-a-site` | Import only adds and overwrites, so an archive cannot reset a site |
| IDEA-024 | `idea-archive-is-not-a-form` | `/admin/import` and `/admin/export` are shell code because an archive is not a form |
| IDEA-025 | `idea-conversation-is-not-a-page` | The assistant's conversation is a stream and a set of host actions, not a page |
| IDEA-027 | `idea-message-does-not-survive-a-redirect` | A page cannot leave a message for a page it sends a visitor to |
| IDEA-029 | `idea-vscode-cannot-check-a-page` | VS Code cannot check a page as it is typed |
| IDEA-031 | `idea-policy-is-a-constant` | Some policy is still a constant in code |
| IDEA-033 | `idea-no-account-store` | A page has no persistent per-account store |
| IDEA-037 | `idea-check-without-storing` | A page cannot check a template without saving it |
| IDEA-038 | `idea-cannot-search-the-store` | A page cannot search the store's source |
| IDEA-039 | `idea-listing-has-no-cursor` | A page cannot ask a listing for the next page |
| IDEA-040 | `idea-library-edit-cost-is-invisible` | Nothing says which pages a library edit will recompile |
| IDEA-042 | `idea-page-cannot-move` | A page cannot move a page |
| IDEA-044 | `idea-no-browser-render` | A page cannot render a page in a browser |
| IDEA-047 | `idea-one-write-per-render` | A render may write a batch of pages, and the caller names the policy |
| IDEA-050 | `idea-old-response-spelling` | The old response spelling is still on `IContext` |
| IDEA-052 | `idea-assistant-cannot-ask` | The assistant cannot ask the owner a question |
| IDEA-053 | `idea-tool-arguments-say-nothing` | Twenty tools' arguments say nothing about themselves |
| IDEA-054 | `idea-assistant-cannot-check-a-page` | The assistant can check a script but not a page |
| IDEA-055 | `idea-nothing-checks-page-assets` | Nothing checks a page's CSS, HTML or SVG |
| IDEA-056 | `idea-job-borrows-a-credential` | A job acts with its starter's credential, and loses it when that credential dies |
| IDEA-057 | `idea-block-that-is-not-code-compiles` | A block that is not code still compiles |
| IDEA-058 | `idea-declaration-does-not-name-its-close` | A declaration block does not name its close |

## What it touches

| | |
| --- | --- |
| Moved | `docs/jobs` → `docs/backlog`, with `README.md`, `closed.md` and `WHIMS.md` |
| Renamed | 26 item files to their slugs, and the three whims' first column |
| Rewritten | `docs/backlog/README.md` — the scheme and one table |
| Retargeted | The `Backlog` rows of the live plans; the ten live documents above; the archived plans' and `archive/decisions.md`'s links; this plan's own links |
| Added | `tools/check-backlog/check-backlog.csproj`, `Program.cs`; a `backlog` target in the `Makefile`; a line in `uk.osric.asp.slnx` and in `make check` |
| Updated | `CONTRIBUTING.md` (a branch is `kind/filename`; an item is a slug) and `.github/copilot-instructions.md` (the citation rule and `docs/backlog/`) |
| Archived | [assistant-live-figures](archive/assistant-live-figures.md), [assistant-owner-interrupt](archive/assistant-owner-interrupt.md), [page-composition](archive/page-composition.md), [page-response](archive/page-response.md), [seed-trimming](archive/seed-trimming.md), [shell-navigation](archive/shell-navigation.md), [shell-route-catalogue](archive/shell-route-catalogue.md) and [simple-context](archive/simple-context.md), with every link into them recomputed |
| Untouched | The rows and numbers in `closed.md`, the host's `PageJob` vocabulary, the product assemblies, `/help` |

## Tests

The checker is a tool and has no suite; the owner has answered that it does not want one, and
`check-decisions` is the precedent. The proof is the record, then a deliberate break run on a copy of
the tree in a scratch directory, one break per run:

| Break | What `make backlog` said |
| --- | --- |
| none — the real tree | `backlog: 26 items in 25 rows, 289 citations resolved`, exit 0 |
| a row deleted from the order | `the order runs 1..24 and this row is 15` and `` `ops-no-outage-notification` is not in the order ``, exit 1 |
| an item moved above its blocker | `` `idea-one-write-per-render` is blocked by `idea-check-without-storing`, which is at row 10 ``, exit 1 |
| two items made to block each other | a row problem, `says open and a blocker is still open`, and `` `idea-one-write-per-render` is in a cycle of blockers ``, exit 1 |
| a plan's `Backlog` row unlinked | `` `account-store.md` does not link back to `idea-no-account-store` ``, exit 1 |
| a citation of a slug that is not there | "cited but is not in the record", exit 1 |
| an open item cited by its old number | "cited by its old number and closed.md has no such row", exit 1 |
| `blocked` with no open blocker | `says blocked and no blocker is open`, exit 1 |
| the same item `parked` | no problem, exit 0 — `parked` is the owner's to write |
| a row whose utility disagrees with the header | `the row is high and … is low`, exit 1 |
| an item renamed and its row not | "is not in the order", exit 1 |

The gate itself is the last proof: `make check` runs `format-check license-check audit decisions
backlog check-pages test publish`, and `make format-check`, `make decisions` and `make license-check`
were run first, so a failure would have named the change that caused it.

The archive is proved by the links: every relative link in the tree is resolved against the file that
carries it, and 94 do not resolve on this branch — the same 94 as on trunk, once `jobs/` is read as
`backlog/`, and the same links. The move broke none and repaired none; the dead ones are links to
items and source files deleted long before this change.

Two rules the tool does not carry, deliberately:

- **`closed.md` is not rewritten except to append.** Only git can answer that, and a checker that
  guessed would be a second history.
- **Two items sharing a row.** The row's `Difficulty` is the step's, so it must be no easier than any
  item in it and its `Utility` must be theirs; that is what is checked, rather than an equality the
  combined step would always fail.

No product test changes: nothing under `src/` or `test/` changes behaviour.

## Documentation

- [The index](../backlog/README.md) states the scheme once: the slug and the title, the header's fields,
  the difficulty scale with its ten animals, the one table, and what `make backlog` proves.
- [CONTRIBUTING.md](../../CONTRIBUTING.md) says a branch is `kind/filename` and an item is a slug;
  the `arch-15`/`idea-026` examples become `arch/…` and `idea/…`.
- [.github/copilot-instructions.md](../../.github/copilot-instructions.md) cites the policy item by
  its slug, says a job is now the host's job, and sends a new idea to `docs/backlog/`.
- [decisions.md](../decisions.md) has its item links retargeted; no entry changes meaning.
- **`/help` is unchanged.** This plan adds no context member, capability, layout slot, editor action,
  content type or response rule, so there is nothing a page author does differently.

## The record at the merge

- **product-backlog-items-are-slugs** — the record is `docs/backlog`, its entries are items, an item
  is identified by a `kind-subject` slug and has a title as its H1, the header carries Difficulty,
  Utility, Status, Blocked by and Plan, the order table is the priority, and `closed.md` is frozen.
  Recorded
  in this plan and [the index](../backlog/README.md).
- **product-a-difficulty-is-not-a-duration** — difficulty is a level on the animal scale, never a
  guess at the calendar: the header and the row carry an animal and its condition, and no document
  in the record estimates hours or days. Recorded in this plan and [the index](../backlog/README.md).
- **product-a-merged-plan-is-archived** — the merge that writes a plan's decision also sets the
  plan's Status to merged and moves it to `docs/plans/archive/`, retargeting every link into it, so
  the live plans are the ones still being worked. Recorded in this plan,
  [the record](../decisions.md#taking-a-name) and [how to work](../../CONTRIBUTING.md#working-on-it).
- **code-the-gate-checks-the-backlog** — `make check` runs `make backlog`, which proves the slugs,
  the fields, the order against the dependency graph and both directions of the plan link. Recorded
  in this plan, [the index](../backlog/README.md) and the `Makefile`.

## Slices

1. **The record.** Caffeinated Squirrel ☕🐿️ — move the folder, rename the 26 files, give the whims slugs,
   retarget every citation and link, rewrite the index's prose. No new fields and no tool yet; the
   tree is consistent by hand and `make check` still passes.
2. **The facts.** Eager Beaver 🦫⚙️ — replace `Size` with `Difficulty` on the animal scale, add
   Utility, Status, Blocked by and Plan to each header, fold the two tables into the one order table,
   and fill Utility and the blockers from the order's own prose.
3. **The proof.** Determined Mountain Goat 🐐💪 — `tools/check-backlog`, `make backlog`, the `slnx`
   and `make check` entries, the `CONTRIBUTING.md` and instruction-file updates, and the eight
   deliberate breaks.
4. **The archive.** Caffeinated Squirrel ☕🐿️ — the eight completed plans moved to
   `docs/plans/archive/`, their Status lines corrected against `closed.md`, and every link into and
   inside them recomputed.

Slice 2 is what makes slice 3 possible, because the checker reads the shape slice 2 creates. If the
owner would rather merge the folder and the slugs first, slice 1 stands alone and the other two wait.

## Risks

- **A live document cites an item in plain text and the sweep misses it.** The `git grep` for the old
  numbers and for `docs/jobs` is the answer, and it is why the legacy-number rule is part of the tool
  rather than a note in this plan.
- **The archive takes link edits.** A moved folder 404s every link into it, including from documents
  that are otherwise frozen. Retargeting only the links is mechanical and reviewable, and leaving
  them broken is worse.
- **The table gets wide.** Eight columns is the cost of one index instead of three; the title is the
  column the owner asked for.
- **A stored status goes stale.** `blocked` and `open` are checked against the graph, so only
  `parked` can drift, and it is the state that exists precisely to be a human decision.
- **A difficulty is a judgement and two readers will disagree.** That is the point — it ranks work
  without pretending to date it — but it means `make backlog` can prove a level is *one of* the ten
  and cannot prove it is *right*. The order's "Why here" column is where the reasoning lives.
- **A slug outgrows its kind.** An item that turns out to be a defect is renamed `idea-…` → `bug-…`
  before it merges, with its citations; after the merge it is history and the number in `closed.md`
  is what it was.
- **Two words for one thing during the sweep.** The branch renames the folder and the noun at once,
  so a document that says "job" after it means the host's job. A document that meant the record and
  was missed reads as a host job — the one risk the checker cannot see, and the reason the sweep is
  a single reviewable change.
- **`make check` gets slower.** Two checkers over the tree, both linear scans; `check-decisions`
  already does the same walk.

## Not in scope

- **GitHub Issues, Projects or Azure DevOps.** They model every field above and do it well; they move
  the record out of the repository, out of the diff and out of the gate, and that is the property
  this plan is keeping. A board is a view, and a view can be generated later ([`whim-backlog-on-the-site`](../backlog/WHIMS.md)).
- **A tracker tool.** A checker is a pure function over a folder of text; a tracker with transitions,
  filters and a UI is a second product.
- **A computed priority score.** The order with a reason per row is the decision; a score is a
  second opinion that can disagree with it.
- **Status from git.** "In progress" is a branch and stays one; the record does not repeat what
  `git branch` already answers.
- **Renaming the host's job.** `PageJob`, `PageJobRunner`, `PageJobsScope`, the `jobs` table and the
  assistant's runs keep the word; this plan only stops the record borrowing it.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Backlog

The record of what the site should become. One file per item — something the site gets wrong
(`bug-`, `sec-`, `ops-`, `arch-`) or something it could usefully become (no kind at all) — named by
the item's **slug**, which is how it is cited in commits, branches, plans and discussion.

Nothing here is scheduled. The order in [the documentation index](../README.md#the-order) is the
owner's judgement of what comes next, and every step is proposed and waits for the owner
([a step stops](#a-step-stops-and-the-owner-answers)).

## A slug and a title

An item has two strings:

- its **slug** — `check-without-storing` — the file name and the id: lowercase and hyphenated, two
  to five words that state the gap. Cite it in backticks, and name the branch `idea/<slug>` —
  `idea/check-without-storing`.
- its **title** — *A page cannot check a template without saving it* — the H1, and the sentence a
  reader reads.

An idea's slug carries no kind, because an idea is what an item is when its slug names no defect. A
defect's begins with one — `bug`, `sec`, `ops` or `arch` — so the record still says at a glance what
has gone wrong. The branch carries the word the slug may not: `idea/<slug>` for an idea, and
`kind/<slug>` split at a defect's first hyphen. No kind is a [decision
area](../decisions.md#what-a-name-is), and no slug — kinded or not — may begin with one, so `make
backlog` refuses an item whose name would read as a decision.

A slug is permanent once it is on trunk, for the reason a decision name is: it is cited from
commits, plans and code, and a reader has to be able to find it. A slug that is wrong before the
merge is fixed with `git mv` and every citation in the same change.

`job` is the host's word — `PageJob`, `PageJobRunner`, `PageJobsScope`, the assistant's runs — and
this record does not use it for an item.

## What an item carries

Every item opens with a header table, so an item read alone carries the facts it is ordered by.

| Field | What it says |
| --- | --- |
| Difficulty | one of the ten levels below, and never a duration |
| Utility | `low`, `medium` or `high` — what the owner gets when it is done |
| Severity | a defect only: the reviewer's risk assessment. An idea is not something that has gone wrong, so it has none |
| Status | `open`, `planned`, `blocked` or `parked` — and only `parked` is the owner's to write (see below) |
| Blocked by | the slugs of the items that must land first, or `—` |
| Plan | the plan that carries the work, or `—` |
| Area | what the work touches |
| Location | where in the tree it starts |
| Spike | a spike that was written, or `—` |

### Difficulty is a level, not a duration

Ten levels, each an animal and a condition. A level ranks the work; it is not a promise about the
calendar, and no item estimates hours or days.

| # | Level | Emoji | What it says |
| --- | --- | --- | --- |
| 1 | `sleepy-hamster` | 😴🐹 | Trivial — autopilot |
| 2 | `contented-capybara` | 😌🦫 | Easy — straightforward |
| 3 | `eager-beaver` | ⚙️🦫 | Moderate — some problem-solving |
| 4 | `caffeinated-squirrel` | ☕🐿️ | Tricky — attention to detail, several considerations |
| 5 | `determined-mountain-goat` | 💪🐐 | Challenging — careful planning, several steps |
| 6 | `persistent-salmon` | ⬆️🐟 | Difficult — real obstacles, fought against |
| 7 | `angry-honey-badger` | 😠🦡 | Very difficult — determined effort, will not back down |
| 8 | `protective-dragon` | 🔥🐉 | Formidable — a major undertaking with real risk |
| 9 | `cornered-porcupine` | 📍🦔 | Hardcore — everything can go wrong, sharp edges everywhere |
| 10 | `tiger-with-a-toothache` | 🐯🦷 | Extreme — complexity and pain together |

A level's glyphs are in the order of its words — the condition first, the animal second — except
`tiger-with-a-toothache`, whose name puts the animal first and whose glyphs follow it.

Whether an item wants a plan is the `Plan` field's business, not difficulty's: a `protective-dragon`
may need none, and an `eager-beaver` may need one, because a plan is about coordination.

### Status is checked, not trusted

Three of the four states are the record's, not the author's:

- `blocked` — at least one item in `Blocked by` is still open;
- `planned` — nothing blocks it and its `Plan` names a plan that has not been archived, so the plan is
  written and waits for a yes. In the table the state is the link — `[planned](../plans/ide-host.md)`
  — so the cell answers both what the item waits for and where the answer is;
- `open` — nothing blocks it and there is no live plan.

`make backlog` fails on an item that says one and the record says another, and on a `Blocked by` that
names something which is not in the record — an open slug, or a closed item by the number it closed
with. An archived plan does not make an item `planned`: the work it carried has landed, and what is
left of the item is new.

`parked` is the one state the graph cannot see, so it is the one the owner writes.

## The order

The table lives in [the documentation index](../README.md#the-order), because the order is the site's
index as well as the backlog's. It is both the inventory and the order. **A row's place is its
priority** — the first row is next — and there is no number column, because a number that every close
has to renumber is churn with nothing to show for it. The line under a row in italics is the reason it
sits there: markdown has no merged cells, so the reason takes a row of its own under the title and the
data above stays narrow.

Below its head the order spends the easy utility first: a blocker sits above the item it blocks, and
everything else sorts by utility descending and then by difficulty ascending, so the most valuable work
that is cheapest to do comes first. The head is the owner's exception — the assistant's two steps and
the items in front of them, one programme rather than the next rows of that rule. A `parked` item sorts
last whatever its utility, because the owner has said it is not work. A `planned` item carries its plan
in its status, so the list says at a glance which items are a yes away — but a plan says an item is
ready, not that it is urgent, and a planned item sorts by its utility and difficulty like any other.

`make backlog` checks that every open item appears exactly once in that table, that a row's difficulty,
utility and status are the item's, that a `planned` row links the plan its item names, that every row is
followed by its reason, and that no item sits above something that blocks it.

Every step is one unit of work, on one branch in one worktree named `kind/filename`
([how](../../CONTRIBUTING.md#working-on-it)). Two independent steps may run at once, each in its own
worktree.

### A step stops, and the owner answers

Every step is proposed to the owner and waits. Nothing merges on its own evidence. There are three
stops, in this order, and a step makes the second only when it wants a plan:

1. **The questions.** An item or a plan that puts a choice to the owner — an `## Open questions`
   section, or the same thing under another heading: *what the owner decides*, a question left in the
   sketch, a value only they can pick — is asked before anything is planned or built from it. The
   questions go to the owner, and their answers are what the next step is written against. Asking
   after the plan or the code exists is the wrong order: an answer usually changes the shape of the
   work rather than qualifying it at the end. A question whose answer is not needed yet stays in the
   document with a line saying why it can wait.
2. **The plan.** A step that is large enough to want a plan writes it on the branch, commits it and
   stops. The answer is yes, or what to change; nothing is built until it is yes.
3. **The merge.** Every step stops here, with or without a plan. The branch is finished, its gate is
   run, and the step says what changed, what proved it and what it is unsure of. It stays on its
   branch until the owner says yes.

What a step proves is its own evidence and never its permission: a green `make check` does not merge,
and finishing a step is not a reason to start the next one, merge the branch, or read silence, a
promise or an unanswered question as approval.

## Whims

A whim is an idea that has not acquired a shape. It gets a slug (`whim-…`) and one or two lines in
[WHIMS.md](WHIMS.md), never a file of its own, and it is not counted, sized or ordered here.
Promoting a whim means writing an item file and removing its line; deleting one is free.

## Closed items

A resolved item moves to [closed.md](closed.md), where its row keeps the id it was cited by — a legacy
`IDEA-` number for an item old enough to have one, its slug for everything since — so a plan, a commit
or a decision written before the move still means what it meant.

## Help for authors

The author manual is the seeded page `/help`, linked from the editor and the page list. An item or
plan that adds or changes something a page author can do — an `IContext` member, a capability, a
layout slot, an editor action, a content type or a response rule — updates `/help` in the same
change. The update belongs beside the tests and the architecture notes in the plan, not in a
follow-up.

## The check

`make backlog` reads these rules, [the order](../README.md#the-order) and the items beside it, and
fails on a slug outside the shape, a slug used twice or one that begins with a decision area, a
missing or malformed field, a `Status` the
blockers and the plan do not say, an item missing from the order or listed twice, a row whose
difficulty, utility or status disagrees with the item, a `planned` row that links the wrong plan, an
order that puts an item above its blocker, a cycle, a plan that does not link back, a kinded
citation of an item that is not in the record, and a link into the record that names a file it does
not have. `make check` runs it, so the record cannot drift from itself.

An idea's slug has no kind for the citation check to recognise, so a link is how one is cited and how
the check finds it. A backticked mention still reads well in prose; the link is what is verified.

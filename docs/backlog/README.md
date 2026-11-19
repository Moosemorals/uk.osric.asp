<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Backlog

The record of what the site should become. One file per item — something the site gets wrong
(`bug-`, `sec-`, `ops-`, `arch-`) or something it could usefully become (no kind at all) — named by
the item's **slug**, which is how it is cited in commits, branches, plans and discussion.

Nothing here is scheduled. The order below is the owner's judgement of what comes next, and every
step is proposed and waits for the owner ([a step stops](#a-step-stops-and-the-owner-answers)).

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

The one table is both the inventory and the order. **A row's place is its priority** — the first row
is next — and there is no number column, because a number that every close has to renumber is churn with
nothing to show for it. The line under a row in italics is the reason it sits there: markdown has no
merged cells, so the reason takes a row of its own under the title and the data above stays narrow. The
order spends the easy utility first, and the owner moves an item above its difficulty when it is a rule
other work is written against rather than a tidy-up. A `planned` item carries its plan in its status,
so the list says at a glance which items are a yes away — but a plan says an item is ready, not that it
is urgent, and a planned item sits where its utility puts it.

`make backlog` checks that every open item appears exactly once, that a row's difficulty, utility and
status are the item's, that a `planned` row links the plan its item names, that every row is followed by
its reason, and that no item sits above something that blocks it.

| Slug | Title | Difficulty | Utility | Status | Blocked by |
| --- | --- | --- | --- | --- | --- |
| [`makefile-holds-the-deploy-recipe`](makefile-holds-the-deploy-recipe.md) | The Makefile holds the host deploy recipe | caffeinated-squirrel ☕🐿️ | high | open | — |
| | *The deploy target is 143 lines of host installation inside the task runner; moving it to deploy/deploy-host, as ci-install does, makes the Makefile readable and the host steps testable.* | | | | |
| [`cannot-ask-the-owner`](cannot-ask-the-owner.md) | The assistant cannot ask the owner a question | caffeinated-squirrel ☕🐿️ | high | [planned](../plans/assistant-ask-owner.md) | — |
| | *The wait it needs is built, so this is a tool, a card and three limits, and every assistant run that meets an unknown stops guessing.* | | | | |
| [`page-cannot-move`](page-cannot-move.md) | A page cannot move a page | eager-beaver ⚙️🦫 | high | [planned](../plans/page-move.md) | — |
| | *A rename is two histories and silent breakage today; a common act for moderate work.* | | | | |
| [`check-without-storing`](check-without-storing.md) | A page cannot check a template without saving it | caffeinated-squirrel ☕🐿️ | high | [planned](../plans/ide-host.md) | — |
| | *Stops "check my work" storing a revision, so no history carries a test save.* | | | | |
| [`one-write-per-render`](one-write-per-render.md) | A render may write a batch of pages, and the caller names the policy | protective-dragon 🔥🐉 | high | blocked | `check-without-storing` |
| | *The one-save rule is what the assistant hits on every multi-file edit, and the owner has decided it goes; it borrows [`check-without-storing`](check-without-storing.md)'s compile-without-store, so it follows that step.* | | | | |
| [`job-borrows-a-credential`](job-borrows-a-credential.md) | A job acts with its starter's credential, and loses it when that credential dies | persistent-salmon ⬆️🐟 | high | blocked | `one-write-per-render` |
| | *A job's writer is only safe once [`one-write-per-render`](one-write-per-render.md) has replaced the one-action rule with a policy the caller names; revocation is the other half of the same decision.* | | | | |
| [`cannot-check-a-page`](cannot-check-a-page.md), with [`tool-arguments-say-nothing`](tool-arguments-say-nothing.md) as its first slice | The assistant can check a script but not a page | determined-mountain-goat 💪🐐 | high | blocked | `check-without-storing` |
| | *The compiler [`check-without-storing`](check-without-storing.md) adds gives the editor answers the model's most-asked question; the same step retires two tools that are one tool twice, so the definitions it describes are described once.* | | | | |
| [`cannot-search-the-store`](cannot-search-the-store.md) | A page cannot search the store's source | eager-beaver ⚙️🦫 | medium | [planned](../plans/ide-host.md) | — |
| | *Lifts the assistant's own search into the contract rather than inventing anything.* | | | | |
| [`ops-no-outage-notification`](ops-no-outage-notification.md) | No outbound notifications when the site breaks | eager-beaver ⚙️🦫 | medium | open | — |
| | *Useful, but one owner and a unit that restarts already notices most outages.* | | | | |
| [`import-cannot-reset-a-site`](import-cannot-reset-a-site.md) | Import only adds and overwrites, so an archive cannot reset a site | caffeinated-squirrel ☕🐿️ | medium | open | — |
| | *A destructive reset has to be designed carefully; the additive import is the safe default until it is.* | | | | |
| [`no-account-store`](no-account-store.md) | A page has no persistent per-account store | protective-dragon 🔥🐉 | high | [planned](../plans/account-store.md) | — |
| | *The most useful large item, with a plan and a first customer, and a plan's yes is the only thing it waits on.* | | | | |
| [`editor-is-a-textarea`](editor-is-a-textarea.md) | The editor is a plain textarea | determined-mountain-goat 💪🐐 | high | open | — |
| | *High use and high cost; the gutter, the diagnostics and the row state already cover the worst of it.* | | | | |
| [`conversation-is-not-a-page`](conversation-is-not-a-page.md) | The assistant's conversation is a stream and a set of host actions, not a page | protective-dragon 🔥🐉 | medium | blocked | `no-account-store` |
| | *The assistant's conversation as a page needs the account store beneath it, and that is [`no-account-store`](no-account-store.md).* | | | | |
| [`archive-is-not-a-form`](archive-is-not-a-form.md) | `/admin/import` and `/admin/export` are shell code because an archive is not a form | caffeinated-squirrel ☕🐿️ | low | open | — |
| | *The two admin routes already work; making them pages buys consistency, not capability.* | | | | |
| [`sec-page-runs-in-process`](sec-page-runs-in-process.md) | Page code has no execution budget and no process boundary | cornered-porcupine 📍🦔 | high | open | — |
| | *Medium severity and a redesign of how a page runs; the trimmed API policy and the sign-in guard already carry much of it.* | | | | |
| [`no-browser-render`](no-browser-render.md) | A page cannot render a page in a browser | persistent-salmon ⬆️🐟 | low | open | — |
| | *A spike, a sidecar and a capability for a question the server's own answer often settles.* | | | | |
| [`nothing-checks-page-assets`](nothing-checks-page-assets.md) | Nothing checks a page's CSS, HTML or SVG | persistent-salmon ⬆️🐟 | low | open | — |
| | *Pays off once pages write their own CSS and SVG, and each checker is a parser we write, so it waits for that reason rather than a hunch.* | | | | |
| [`declaration-does-not-name-its-close`](declaration-does-not-name-its-close.md) | A declaration block does not name its close | protective-dragon 🔥🐉 | medium | open | — |
| | *A language change in two releases that rewrites every declaration in the store; the warning that carries release 1 has landed, so this now waits only on its own plan.* | | | | |
| [`vscode-cannot-check-a-page`](vscode-cannot-check-a-page.md) | VS Code cannot check a page as it is typed | determined-mountain-goat 💪🐐 | low | open | — |
| | *Developer-only, and `make check-pages` already compiles the seeds.* | | | | |
| [`old-response-spelling`](old-response-spelling.md) | The old response spelling is still on `IContext` | contented-capybara 😌🦫 | low | open | — |
| | *Waits for a release: the alias has to have been out long enough that nothing in the store reports `PAGE009` before it is deleted.* | | | | |
| [`message-does-not-survive-a-redirect`](message-does-not-survive-a-redirect.md) | A page cannot leave a message for a page it sends a visitor to | protective-dragon 🔥🐉 | high | [planned](../plans/page-inbox.md) | — |
| | *Planned, but a plan is not urgency: nothing blocks it and no page stops working without it, so it waits below everything that does.* | | | | |
| [`listing-does-not-date-itself`](listing-does-not-date-itself.md) | A listing cannot say the store moved under it | caffeinated-squirrel ☕🐿️ | medium | open | — |
| | *The cursor answered where a walk is; this answers whether it is still the same walk, and the write paths are the price.* | | | | |
| [`ship-the-tested-image`](ship-the-tested-image.md) | A release does not ship the image the tests passed | persistent-salmon ⬆️🐟 | high | open | — |
| | *The browser suite drives a source build while the deploy ships an image built later; this makes the promoted image the one the browser drove, and the host gate now keeps one.* | | | | |

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

`make backlog` reads this file and the items beside it and fails on a slug outside the shape, a slug
used twice or one that begins with a decision area, a missing or malformed field, a `Status` the
blockers and the plan do not say, an item missing from the order or listed twice, a row whose
difficulty, utility or status disagrees with the item, a `planned` row that links the wrong plan, an
order that puts an item above its blocker, a cycle, a plan that does not link back, a kinded
citation of an item that is not in the record, and a link into the record that names a file it does
not have. `make check` runs it, so the record cannot drift from itself.

An idea's slug has no kind for the citation check to recognise, so a link is how one is cited and how
the check finds it. A backticked mention still reads well in prose; the link is what is verified.

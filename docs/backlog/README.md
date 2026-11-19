<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Backlog

The record of what the site should become. One file per item — something the site gets wrong
(`bug-`, `sec-`, `ops-`, `arch-`) or something it could usefully become (`idea-`) — named by the
item's **slug**, which is how it is cited in commits, branches, plans and discussion.

Nothing here is scheduled. The order below is the owner's judgement of what comes next, and every
step is proposed and waits for the owner ([a step stops](#a-step-stops-and-the-owner-answers)).

## A slug and a title

An item has two strings:

- its **slug** — `idea-check-without-storing` — the file name and the id: lowercase and hyphenated,
  the kind first, then two to five words that state the gap. Cite it in backticks, and name the
  branch `kind/filename` — `idea/check-without-storing`.
- its **title** — *A page cannot check a template without saving it* — the H1, and the sentence a
  reader reads.

The kinds are `idea`, `bug`, `sec`, `ops` and `arch`. None of them is a [decision
area](../decisions.md#what-a-name-is), so an item slug is never read as a decision, and `make
backlog` refuses one that begins with an area.

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
| Status | `open`, `blocked` or `parked` — and only `parked` is free text for the owner to write (see below) |
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
| 3 | `eager-beaver` | 🦫⚙️ | Moderate — some problem-solving |
| 4 | `caffeinated-squirrel` | ☕🐿️ | Tricky — attention to detail, several considerations |
| 5 | `determined-mountain-goat` | 🐐💪 | Challenging — careful planning, several steps |
| 6 | `persistent-salmon` | 🐟⬆️ | Difficult — real obstacles, fought against |
| 7 | `angry-honey-badger` | 🦡😠 | Very difficult — determined effort, will not back down |
| 8 | `protective-dragon` | 🐉🔥 | Formidable — a major undertaking with real risk |
| 9 | `cornered-porcupine` | 🦔📍 | Hardcore — everything can go wrong, sharp edges everywhere |
| 10 | `tiger-with-a-toothache` | 🐯🦷 | Extreme — complexity and pain together |

Whether an item wants a plan is the `Plan` field's business, not difficulty's: a `protective-dragon`
may need none, and an `eager-beaver` may need one, because a plan is about coordination.

### Status is checked, not trusted

`blocked` means at least one item in `Blocked by` is still open. `open` means none is. `make backlog`
fails on an item that says one and the graph says the other, and on a `Blocked by` that names
something which is not in the record — an open slug, or a closed item by the number it closed with.

`parked` is the one state the graph cannot see, so it is the one the owner writes.

## The order

The one table is both the inventory and the order. A row's number is its priority, and "Why here" is
the reason it sits there: the order spends the easy utility first, and the owner moves an item above
its difficulty when it is a rule other work is written against rather than a tidy-up.

`make backlog` checks that every open item appears exactly once, that the numbers run `1..n`, that a
row's difficulty and utility are the item's, and that no item sits above something that blocks it.

| # | Slug | Title | Difficulty | Utility | Status | Blocked by | Why here |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | [`idea-message-does-not-survive-a-redirect`](idea-message-does-not-survive-a-redirect.md) | A page cannot leave a message for a page it sends a visitor to | protective-dragon 🐉🔥 | high | open | — | Every page that sends a visitor on wants a way to say something that survives the redirect, and its plan is written, so this is a yes away. |
| 2 | [`idea-policy-is-a-constant`](idea-policy-is-a-constant.md) | Some policy is still a constant in code | contented-capybara 😌🦫 | medium | open | — | Closes the policy exceptions the configuration rule still names, and tightening a guarantee always outranks tidying. |
| 3 | [`idea-listing-has-no-cursor`](idea-listing-has-no-cursor.md) | A page cannot ask a listing for the next page | eager-beaver 🦫⚙️ | medium | open | — | A listing that refuses past the cap is hit by every store that grows; a keyset cursor is small. |
| 4 | [`idea-library-edit-cost-is-invisible`](idea-library-edit-cost-is-invisible.md) | Nothing says which pages a library edit will recompile | contented-capybara 😌🦫 | medium | open | — | Says what a library edit recompiles; small, and it makes a cost the reader cannot see visible. |
| 5 | [`idea-block-that-is-not-code-compiles`](idea-block-that-is-not-code-compiles.md) | A block that is not code still compiles | eager-beaver 🦫⚙️ | medium | open | — | The same complaint as the diagnostics above one layer down: a shape the compiler cannot see, and the seeds are the proof the warnings stay quiet. |
| 6 | [`ops-no-ci`](ops-no-ci.md) | There is no CI pipeline | caffeinated-squirrel ☕🐿️ | high | open | — | Turns the gate from a habit into a rule, which makes every step below it cheaper. |
| 7 | [`idea-assistant-cannot-ask`](idea-assistant-cannot-ask.md) | The assistant cannot ask the owner a question | caffeinated-squirrel ☕🐿️ | high | open | — | The wait it needs is built, so this is a tool, a card and three limits, and every assistant run that meets an unknown stops guessing. |
| 8 | [`idea-page-cannot-move`](idea-page-cannot-move.md) | A page cannot move a page | eager-beaver 🦫⚙️ | high | open | — | A rename is two histories and silent breakage today; a common act for moderate work. |
| 9 | [`idea-check-without-storing`](idea-check-without-storing.md) | A page cannot check a template without saving it | caffeinated-squirrel ☕🐿️ | high | open | — | Stops "check my work" storing a revision, so no history carries a test save. |
| 10 | [`idea-one-write-per-render`](idea-one-write-per-render.md) | A render may write a batch of pages, and the caller names the policy | protective-dragon 🐉🔥 | high | blocked | `idea-check-without-storing` | The one-save rule is what the assistant hits on every multi-file edit, and the owner has decided it goes; it borrows 9's compile-without-store, so it follows that step. |
| 11 | [`idea-job-borrows-a-credential`](idea-job-borrows-a-credential.md) | A job acts with its starter's credential, and loses it when that credential dies | persistent-salmon 🐟⬆️ | high | blocked | `idea-one-write-per-render` | A job's writer is only safe once 10 has replaced the one-action rule with a policy the caller names; revocation is the other half of the same decision. |
| 12 | [`idea-assistant-cannot-check-a-page`](idea-assistant-cannot-check-a-page.md), with [`idea-tool-arguments-say-nothing`](idea-tool-arguments-say-nothing.md) as its first slice | The assistant can check a script but not a page | determined-mountain-goat 🐐💪 | high | blocked | `idea-check-without-storing` | The compiler step 9 gives the editor answers the model's most-asked question; the same step retires two tools that are one tool twice, so the definitions it describes are described once. |
| 13 | [`idea-cannot-search-the-store`](idea-cannot-search-the-store.md) | A page cannot search the store's source | eager-beaver 🦫⚙️ | medium | open | — | Lifts the assistant's own search into the contract rather than inventing anything. |
| 14 | [`ops-no-outage-notification`](ops-no-outage-notification.md) | No outbound notifications when the site breaks | eager-beaver 🦫⚙️ | medium | open | — | Useful, but one owner and a unit that restarts already notices most outages. |
| 15 | [`idea-import-cannot-reset-a-site`](idea-import-cannot-reset-a-site.md) | Import only adds and overwrites, so an archive cannot reset a site | caffeinated-squirrel ☕🐿️ | medium | open | — | A destructive reset has to be designed carefully; the additive import is the safe default until it is. |
| 16 | [`idea-no-account-store`](idea-no-account-store.md) | A page has no persistent per-account store | protective-dragon 🐉🔥 | high | open | — | The most useful large item, with a plan and a first customer, and a plan's yes is the only thing it waits on. |
| 17 | [`idea-editor-is-a-textarea`](idea-editor-is-a-textarea.md) | The editor is a plain textarea | determined-mountain-goat 🐐💪 | high | open | — | High use and high cost; the gutter, the diagnostics and the row state already cover the worst of it. |
| 18 | [`idea-conversation-is-not-a-page`](idea-conversation-is-not-a-page.md) | The assistant's conversation is a stream and a set of host actions, not a page | protective-dragon 🐉🔥 | medium | blocked | `idea-no-account-store` | The assistant's conversation as a page needs the account store beneath it, and that is 16. |
| 19 | [`idea-archive-is-not-a-form`](idea-archive-is-not-a-form.md) | `/admin/import` and `/admin/export` are shell code because an archive is not a form | caffeinated-squirrel ☕🐿️ | low | open | — | The two admin routes already work; making them pages buys consistency, not capability. |
| 20 | [`sec-page-runs-in-process`](sec-page-runs-in-process.md) | Page code has no execution budget and no process boundary | cornered-porcupine 🦔📍 | high | open | — | Medium severity and a redesign of how a page runs; the trimmed API policy and the sign-in guard already carry much of it. |
| 21 | [`idea-no-browser-render`](idea-no-browser-render.md) | A page cannot render a page in a browser | persistent-salmon 🐟⬆️ | low | open | — | A spike, a sidecar and a capability for a question the server's own answer often settles. |
| 22 | [`idea-nothing-checks-page-assets`](idea-nothing-checks-page-assets.md) | Nothing checks a page's CSS, HTML or SVG | persistent-salmon 🐟⬆️ | low | open | — | Pays off once pages write their own CSS and SVG, and each checker is a parser we write, so it waits for that reason rather than a hunch. |
| 23 | [`idea-declaration-does-not-name-its-close`](idea-declaration-does-not-name-its-close.md) | A declaration block does not name its close | protective-dragon 🐉🔥 | medium | blocked | `idea-block-that-is-not-code-compiles` | A language change in two releases that rewrites every declaration in the store; it waits for 5 to have proved the warning that carries release 1. |
| 24 | [`idea-vscode-cannot-check-a-page`](idea-vscode-cannot-check-a-page.md) | VS Code cannot check a page as it is typed | determined-mountain-goat 🐐💪 | low | open | — | Developer-only, and `make check-pages` already compiles the seeds. |
| 25 | [`idea-old-response-spelling`](idea-old-response-spelling.md) | The old response spelling is still on `IContext` | contented-capybara 😌🦫 | low | open | — | Waits for a release: the alias has to have been out long enough that nothing in the store reports `PAGE009` before it is deleted. |

Every step is one unit of work, on one branch in one worktree named `kind/filename`
([how](../../CONTRIBUTING.md#working-on-it)). Two independent steps may run at once, each in its own
worktree.

### A step stops, and the owner answers

Every step is proposed to the owner and waits. Nothing merges on its own evidence. There are two
stops, and a step makes the first only when it wants a plan:

1. **The plan.** A step that is large enough to want a plan writes it on the branch, commits it and
   stops. The answer is yes, or what to change; nothing is built until it is yes.
2. **The merge.** Every step stops here, with or without a plan. The branch is finished, its gate is
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

A resolved item moves to [closed.md](closed.md), where its row keeps the number it closed with: a
plan, a commit or a decision written before the move still means what it meant.

## Help for authors

The author manual is the seeded page `/help`, linked from the editor and the page list. An item or
plan that adds or changes something a page author can do — an `IContext` member, a capability, a
layout slot, an editor action, a content type or a response rule — updates `/help` in the same
change. The update belongs beside the tests and the architecture notes in the plan, not in a
follow-up.

## The check

`make backlog` reads this file and the items beside it and fails on a slug outside the shape, a slug
used twice, a missing or malformed field, an item missing from the order or listed twice, a row whose
difficulty or utility disagrees with the item, an order that puts an item above its blocker, a cycle,
a plan that does not link back, and a citation of an item that is not in the record. `make check`
runs it, so the record cannot drift from itself.

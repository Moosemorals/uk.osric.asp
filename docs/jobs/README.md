<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Jobs

One file per job — something the site gets wrong (`SEC-`, `BUG-`, `OPS-`, `ARCH-`) or something it
could usefully become (`IDEA-`) — named by the ID in commits, branches and discussion. **Severity**
is the reviewer's risk assessment, and is blank for an idea because an idea is not something that
has gone wrong. **Size** is how much work it looks like: `S` a sitting, `M` a day or so, `L` big
enough to want a plan of its own. A job whose work has a plan links it; a whim is smaller than a job
and shares one file, [WHIMS.md](WHIMS.md); a resolved job moves to [closed.md](closed.md).

| ID | Size | Severity | Plan | Job |
| --- | --- | --- | --- | --- |
| [SEC-03](SEC-03.md) | L | Medium | — | Page code has no execution budget and no process boundary |
| [OPS-03](OPS-03.md) | M | Medium | — | There is no CI pipeline |
| [OPS-05](OPS-05.md) | M | Medium | — | No outbound notifications when the site breaks |
| [IDEA-001](IDEA-001.md) | L | — | — | The editor is a plain textarea |
| [IDEA-020](IDEA-020.md) | M | — | — | Import only adds and overwrites, so an archive cannot reset a site |
| [IDEA-022](IDEA-022.md) | S | — | — | `SimpleContext` is one class doing five jobs |
| [IDEA-024](IDEA-024.md) | L | — | — | `/admin/import` and `/admin/export` are shell code because an archive is not a form |
| [IDEA-025](IDEA-025.md) | L | — | [page-apps](../plans/archive/page-apps.md) | The assistant's conversation is a stream and a set of host actions, not a page |
| [IDEA-027](IDEA-027.md) | S | — | — | A page cannot leave a message for a page it sends a visitor to |
| [IDEA-029](IDEA-029.md) | L | — | — | VS Code cannot check a page as it is typed |
| [IDEA-030](IDEA-030.md) | M | — | [shell-navigation](../plans/shell-navigation.md) | Every view writes its own navigation |
| [IDEA-031](IDEA-031.md) | S | — | — | Some policy is still a constant in code |
| [IDEA-033](IDEA-033.md) | L | — | [account-store](../plans/account-store.md) | A page has no persistent per-account store |
| [IDEA-037](IDEA-037.md) | M | — | [ide-host](../plans/ide-host.md) | A page cannot check a template without saving it |
| [IDEA-038](IDEA-038.md) | M | — | [ide-host](../plans/ide-host.md) | A page cannot search the store's source |
| [IDEA-039](IDEA-039.md) | S | — | [ide-host](../plans/ide-host.md) | A page cannot ask a listing for the next page |
| [IDEA-040](IDEA-040.md) | S | — | [ide-host](../plans/ide-host.md) | Nothing says which pages a library edit will recompile |
| [IDEA-041](IDEA-041.md) | S | — | [ide-host](../plans/ide-host.md) | A diagnostic is a point in an unnamed source |
| [IDEA-042](IDEA-042.md) | M | — | [page-move](../plans/page-move.md) | A page cannot move a page |
| [IDEA-044](IDEA-044.md) | L | — | — | A page cannot render a page in a browser |
| [IDEA-047](IDEA-047.md) | L | — | [ide-host](../plans/ide-host.md) | A render may write a batch of pages, and the caller names the policy |

## The order

The order spends the easy utility first: a small job that removes a daily annoyance comes before a
large one that mostly tidies the inside or serves a single caller. It is rough on purpose — a step
moves as it is worked — and each is one unit of work, on one branch in one worktree
([how](../../CONTRIBUTING.md#working-on-it)).

| # | Job | Size | Why here |
| --- | --- | --- | --- |
| 1 | [IDEA-027](IDEA-027.md) | S | The smallest job on the list that unblocks an ordinary page: a message that survives a redirect. |
| 2 | [IDEA-031](IDEA-031.md) | S | Closes the policy exceptions the configuration rule still names, and tightening a guarantee always outranks tidying. |
| 3 | [IDEA-039](IDEA-039.md) | S | A listing that refuses past the cap is hit by every store that grows; a keyset cursor is small. |
| 4 | [IDEA-041](IDEA-041.md) | S | Diagnostics point at half the right place today; one value widened fixes it for the author and the assistant together. |
| 5 | [IDEA-040](IDEA-040.md) | S | Says what a library edit recompiles; small, and it makes a cost the reader cannot see visible. |
| 6 | [OPS-03](OPS-03.md) | M | Turns the gate from a habit into a rule, which makes every step below it cheaper. |
| 7 | [IDEA-042](IDEA-042.md) | M | A rename is two histories and silent breakage today; medium work for a common act. |
| 8 | [IDEA-037](IDEA-037.md) | M | Stops "check my work" storing a revision, so no history carries a test save. |
| 9 | [IDEA-038](IDEA-038.md) | M | Lifts the assistant's own search into the contract rather than inventing anything. |
| 10 | [IDEA-022](IDEA-022.md) | S | Easy, but it buys tidiness rather than a capability, so it sits below the small jobs that buy something. |
| 11 | [OPS-05](OPS-05.md) | M | Useful, but one owner and a unit that restarts already notice most outages. |
| 12 | [IDEA-030](IDEA-030.md) | M | Removes duplication in views that several later jobs rewrite anyway. |
| 13 | [IDEA-020](IDEA-020.md) | M | A destructive reset has to be designed carefully; the additive import is the safe default until it is. |
| 14 | [IDEA-033](IDEA-033.md) | L | The most useful large job, with a plan and a first customer, and a plan's yes is the only thing it waits on. |
| 15 | [IDEA-001](IDEA-001.md) | L | High use and high cost; the gutter, the diagnostics and the row state already cover the worst of it. |
| 16 | [IDEA-025](IDEA-025.md) | L | The assistant's conversation as a page needs the account store beneath it, and that is IDEA-033. |
| 17 | [IDEA-024](IDEA-024.md) | L | The two admin routes already work; making them pages buys consistency, not capability. |
| 18 | [SEC-03](SEC-03.md) | L | Medium severity and a redesign of how a page runs; the trimmed API policy and the sign-in guard already carry much of it. |
| 19 | [IDEA-047](IDEA-047.md) | L | Only matters once something writes several pages at once, so it follows IDEA-042 and IDEA-038. |
| 20 | [IDEA-044](IDEA-044.md) | L | A spike, a sidecar and a capability for a question the server's own answer often settles. |
| 21 | [IDEA-029](IDEA-029.md) | L | Developer-only, and `make check-pages` already compiles the seeds. |

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

A whim is an idea that has not acquired a shape. It gets an ID (`WHIM-01`) and one or two lines in
[WHIMS.md](WHIMS.md), never a file of its own, and it is not counted, sized or ordered here.
Promoting a whim means writing an `IDEA-` file and removing its line; deleting one is free.

## Help for authors

The author manual is the seeded page `/help`, linked from the editor and the page list. A job or
plan that adds or changes something a page author can do — an `IContext` member, a capability, a
layout slot, an editor action, a content type or a response rule — updates `/help` in the same
change. The update belongs beside the tests and the architecture notes in the plan, not in a
follow-up.


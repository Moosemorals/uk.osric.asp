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
| [IDEA-024](IDEA-024.md) | L | — | — | `/admin/import` and `/admin/export` are shell code because an archive is not a form |
| [IDEA-025](IDEA-025.md) | L | — | [page-apps](../plans/archive/page-apps.md) | The assistant's conversation is a stream and a set of host actions, not a page |
| [IDEA-027](IDEA-027.md) | S | — | — | A page cannot leave a message for a page it sends a visitor to |
| [IDEA-029](IDEA-029.md) | L | — | — | VS Code cannot check a page as it is typed |
| [IDEA-031](IDEA-031.md) | S | — | — | Some policy is still a constant in code |
| [IDEA-033](IDEA-033.md) | L | — | [account-store](../plans/account-store.md) | A page has no persistent per-account store |
| [IDEA-037](IDEA-037.md) | M | — | [ide-host](../plans/ide-host.md) | A page cannot check a template without saving it |
| [IDEA-038](IDEA-038.md) | M | — | [ide-host](../plans/ide-host.md) | A page cannot search the store's source |
| [IDEA-039](IDEA-039.md) | S | — | [ide-host](../plans/ide-host.md) | A page cannot ask a listing for the next page |
| [IDEA-040](IDEA-040.md) | S | — | [ide-host](../plans/ide-host.md) | Nothing says which pages a library edit will recompile |
| [IDEA-042](IDEA-042.md) | M | — | [page-move](../plans/page-move.md) | A page cannot move a page |
| [IDEA-044](IDEA-044.md) | L | — | — | A page cannot render a page in a browser |
| [IDEA-047](IDEA-047.md) | L | — | [ide-host](../plans/ide-host.md) | A render may write a batch of pages, and the caller names the policy |
| [IDEA-050](IDEA-050.md) | S | — | [page-response](../plans/page-response.md) | The old response spelling is still on `IContext` |
| [IDEA-052](IDEA-052.md) | M | — | [assistant-ask-owner](../plans/assistant-ask-owner.md) | The assistant cannot ask the owner a question |
| [IDEA-053](IDEA-053.md) | S | — | — | Twenty tools' arguments say nothing about themselves |
| [IDEA-054](IDEA-054.md) | M | — | — | The assistant can check a script but not a page |
| [IDEA-055](IDEA-055.md) | L | — | — | Nothing checks a page's CSS, HTML or SVG |
| [IDEA-056](IDEA-056.md) | L | — | — | A job acts with its starter's name, not their credential, so it cannot write and cannot be revoked |
| [IDEA-057](IDEA-057.md) | S | — | — | A block that is not code still compiles |
| [IDEA-058](IDEA-058.md) | L | — | — | A declaration block does not name its close |

## The order

The order spends the easy utility first: a small job that removes a daily annoyance comes before a
large one that mostly tidies the inside or serves a single caller. It is rough on purpose — a step
moves as it is worked — and each is one unit of work, on one branch in one worktree
([how](../../CONTRIBUTING.md#working-on-it)).

The owner moves a step above its size when it is a rule other work is written against rather than a
tidy-up. [IDEA-047](IDEA-047.md) is one: the one-save rule is the friction the assistant meets on
every multi-file edit, and [IDEA-056](IDEA-056.md) cannot offer a job a writer until it is gone, so
they sit together just after [IDEA-037](IDEA-037.md), whose compile-without-store 047 borrows.

| # | Job | Size | Why here |
| --- | --- | --- | --- |
| 1 | [IDEA-027](IDEA-027.md) | S | The smallest job on the list that unblocks an ordinary page: a message that survives a redirect. |
| 2 | [IDEA-031](IDEA-031.md) | S | Closes the policy exceptions the configuration rule still names, and tightening a guarantee always outranks tidying. |
| 3 | [IDEA-039](IDEA-039.md) | S | A listing that refuses past the cap is hit by every store that grows; a keyset cursor is small. |
| 4 | [IDEA-040](IDEA-040.md) | S | Says what a library edit recompiles; small, and it makes a cost the reader cannot see visible. |
| 5 | [IDEA-057](IDEA-057.md) | S | The same complaint as the diagnostics above one layer down: a shape the compiler cannot see, and the seeds are the proof the warnings stay quiet. |
| 6 | [OPS-03](OPS-03.md) | M | Turns the gate from a habit into a rule, which makes every step below it cheaper. |
| 7 | [IDEA-052](IDEA-052.md) | M | The wait it needs is built, so this is a tool, a card and three limits, and every assistant run that meets an unknown stops guessing. |
| 8 | [IDEA-042](IDEA-042.md) | M | A rename is two histories and silent breakage today; medium work for a common act. |
| 9 | [IDEA-037](IDEA-037.md) | M | Stops "check my work" storing a revision, so no history carries a test save. |
| 10 | [IDEA-047](IDEA-047.md) | L | The one-save rule is what the assistant hits on every multi-file edit, and the owner has decided it goes; it borrows 037's compile-without-store, so it follows that step. |
| 11 | [IDEA-056](IDEA-056.md) | L | A job's writer is only safe once 047 has replaced the one-action rule with a policy the caller names; revocation is the other half of the same decision. |
| 12 | [IDEA-054](IDEA-054.md), with [IDEA-053](IDEA-053.md) as its first slice | M | The compiler step 9 gives the editor answers the model's most-asked question; the same step retires two tools that are one tool twice, so the definitions it describes are described once. |
| 13 | [IDEA-038](IDEA-038.md) | M | Lifts the assistant's own search into the contract rather than inventing anything. |
| 14 | [OPS-05](OPS-05.md) | M | Useful, but one owner and a unit that restarts already notices most outages. |
| 15 | [IDEA-020](IDEA-020.md) | M | A destructive reset has to be designed carefully; the additive import is the safe default until it is. |
| 16 | [IDEA-033](IDEA-033.md) | L | The most useful large job, with a plan and a first customer, and a plan's yes is the only thing it waits on. |
| 17 | [IDEA-001](IDEA-001.md) | L | High use and high cost; the gutter, the diagnostics and the row state already cover the worst of it. |
| 18 | [IDEA-025](IDEA-025.md) | L | The assistant's conversation as a page needs the account store beneath it, and that is IDEA-033. |
| 19 | [IDEA-024](IDEA-024.md) | L | The two admin routes already work; making them pages buys consistency, not capability. |
| 20 | [SEC-03](SEC-03.md) | L | Medium severity and a redesign of how a page runs; the trimmed API policy and the sign-in guard already carry much of it. |
| 21 | [IDEA-044](IDEA-044.md) | L | A spike, a sidecar and a capability for a question the server's own answer often settles. |
| 22 | [IDEA-055](IDEA-055.md) | L | Pays off once pages write their own CSS and SVG, and each checker is a parser we write, so it waits for that reason rather than a hunch. |
| 23 | [IDEA-058](IDEA-058.md) | L | A language change in two releases that rewrites every declaration in the store; it waits for IDEA-057 to have proved the warning that carries release 1. |
| 24 | [IDEA-029](IDEA-029.md) | L | Developer-only, and `make check-pages` already compiles the seeds. |
| 25 | [IDEA-050](IDEA-050.md) | S | Waits for a release: the alias has to have been out long enough that nothing in the store reports `PAGE009` before it is deleted. |

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


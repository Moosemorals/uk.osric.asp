<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

I'm writing this project for my own use, for fun. You're welcome to use it,
learn from it, adapt it to your needs, whatever (so long as you follow the
licence, obviously), but I'm not looking for contributions thanks. (Notice, in
fact, that the 'Issues' and 'Pull Requests' tabs are missing from the GitHub
interface).

Nothing personal, I'm sure you're lovely and kind, and I hope you get what
you're looking for from somewhere.

## Working on it

Trunk is for integration, not for work. Every job — a defect, an idea, a plan or a process change —
gets a branch and a worktree beside the repository, so two agents (or two sessions) never share a
checkout.

```bash
make worktree BRANCH=idea/check-without-storing   # ../uk.osric.asp.worktrees/idea/check-without-storing
make worktrees                                    # every checkout, the main one included
make worktree-remove BRANCH=idea/check-without-storing
```

The branch is `kind/filename`, and the name comes from the work:

- the item's slug, for shaping it, writing its plan and implementing it — an idea takes `idea/` in
  front of it, so [`nothing-checks-page-assets`](docs/backlog/nothing-checks-page-assets.md) is
  `idea/nothing-checks-page-assets`, and a defect's slug is split at its first hyphen, so
  [`ops-no-ci`](docs/backlog/closed.md) is `ops/no-ci`;
- `plan/<file>` only for one of the seven plans written before a plan became a section of its item —
  [email](docs/plans/email.md) is `plan/email`.

Work lands as a merge commit (`git merge --no-ff`) rather than a fast-forward, so the branch and the
work it carries stay one unit in the history. After the merge its worktree is removed and the branch
deleted; nothing is pushed to `origin` until asked. With the host as `origin`
([deploy](deploy/README.md#ci-on-the-host)), a push to `trunk` is what runs `make ci` in the host's
`ci` account; GitHub is a one-way mirror. The check is asynchronous, so
`https://moosemorals.com/ci/status.json` is the answer, and a red trunk is fixed forward. A decision
is named rather than numbered and its name is taken when the work merges
([the record](docs/decisions.md)), so a branch states the name it intends until then and the merge
writes the entry. The merge also closes the work's file: an item moves to
[docs/backlog/archive](docs/backlog/archive/README.md) with its plan section and writes the row in
[closed.md](docs/backlog/closed.md) that keeps its slug and links it, while one of the seven older
plans sets its Status merged and moves to [docs/plans/archive](docs/plans/archive). Every link into
the file is retargeted in the same change.

**The owner merges, not the agent.** A branch is a proposal. When its work is done and its gate is
run, the agent stops and says what changed, what proved it and what it is unsure of; the owner answers.
A green gate, a finished plan, an obvious next step or silence is not an answer. The same holds before
the work, in two stages: the questions an item or a plan puts to the owner are asked and answered
before anything is written from it, and then a plan is committed on its branch and waits for a yes
before anything is built ([the programme](docs/backlog/README.md#a-step-stops-and-the-owner-answers))

Two independent steps may run at once, each in its own worktree. No two agents share a worktree, and
`make deploy` and `make image` wait until nothing else is building. Each worktree builds into its own
`artifacts/`, so builds do not collide.

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
make worktree BRANCH=arch-15      # ../uk.osric.asp.worktrees/arch-15, branched from trunk
make worktrees                    # every checkout, the main one included
make worktree-remove BRANCH=arch-15
```

The branch is named for the unit of work:

- the job id in lower case, for planning work and for a job small enough to need no plan —
  `arch-15`, `idea-026`;
- the plan's file name once a plan is being implemented — [page-first](docs/plans/page-first.md)
  is `page-editor`. A job whose plan has a different name has two branches: `arch-13` writes the
  plan, `page-streaming` implements it.

Work lands as a merge commit (`git merge --no-ff`) rather than a fast-forward, so the branch and the
work it carries stay one unit in the history. After the merge its worktree is removed and the branch
deleted; nothing is pushed to `origin` until asked. Decision numbers are taken when the work merges
([DEC-09](docs/decisions.md)), so a branch cites a decision by name until then and every citation is
updated in the merge.

Two independent jobs may run at once, each in its own worktree. No two agents share a worktree, and
`make deploy` and `make image` wait until nothing else is building. Each worktree builds into its own
`artifacts/`, so builds do not collide.

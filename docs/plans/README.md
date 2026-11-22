<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Plans

A plan is written before a large change (`product-plan-before-building`), on its branch, and it
proposes the decisions it records by the name each will carry. It states what exists, what it will
change, what it proves, and what it is unsure of; then it stops. Nothing is built until the owner
answers the plan's open questions and says yes to the plan, and nothing merges until they say yes to
the merge (`product-steps-wait-for-the-owner`).

`docs/plans/` holds only work that is still proposed or in progress. A plan whose work has merged is
archived in [archive/](archive/README.md), where it is frozen: it records what was decided against the
code of the time and is not maintained. A later plan that changes the same code updates its own
document, not the archived one. `make backlog` checks that every item and its live plan link to each
other; `make decisions` checks the names.

## Contents

| Plan | Status | Backlog |
| --- | --- | --- |
| [account-store](account-store.md) | Superseded in shape 2026-11-21 by [storage-model](storage-model.md) | `no-account-store` |
| [assistant-shell-tool](assistant-shell-tool.md) | Proposed 2026-11-19, waits for the owner; nothing built | [`tool-surface-is-one-shell`](../backlog/tool-surface-is-one-shell.md) |
| [build-metrics](build-metrics.md) | Proposed 2026-11-15, waits for the owner; nothing built | [`whim-build-timings`](../backlog/WHIMS.md) |
| [email](email.md) | Proposed 2026-11-06, not implemented | — (operations, not the site) |
| [email-configuration](email-configuration.md) | Configuration excerpts for [email](email.md); nothing deployed | — |
| [ide-host](ide-host.md) | Proposed 2026-11-15; slices 0–6 merged, the search with its own archived plan; slice 7 waits on the owner | [`cannot-search-the-store`](../backlog/closed.md) |
| [page-inbox](page-inbox.md) | Superseded in shape 2026-11-21 by [storage-model](storage-model.md) | `message-does-not-survive-a-redirect` |
| [storage-model](storage-model.md) | Agreed 2026-11-21, revised against trunk 2026-11-22; slice 0, the [sandbox spike](../spikes/store-sandbox.md), and slice 1, values, lifetimes and identity, are written; slice 1 waits for the owner's review | [`storage-is-not-one-model`](../backlog/storage-is-not-one-model.md) |

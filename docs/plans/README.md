<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Plans

A plan is written before a large change (`product-plan-before-building`), and it proposes the
decisions it records by the name each will carry. It states what exists, what it will change, what it
proves, and what it is unsure of; then it stops. Nothing is built until the owner answers the plan's
open questions and says yes to the plan, and nothing merges until they say yes to the merge
(`product-steps-wait-for-the-owner`).

**A plan is a `## Plan` section of its item's file**, not a document here. The item is `planned` while
the section is there, and the merge archives the item, plan and all, in
[docs/backlog/archive](../backlog/archive/README.md), where it is frozen: it records what was decided
against the code of the time and is not maintained. A later change that alters the same code writes
its own item and its own plan, not an edit to the archived one. `make backlog` checks that every plan
section is what makes its item planned; `make decisions` checks the names.

## The seven live plans

Seven plans were written before the section rule and are still files here. Each folds into its
item's `## Plan` the next time the item is touched — shaped, revised or merged — and nothing new is
written in this folder. When the last one lands, the `Plan` row, its check and this table go with it.
Two of them, [email](email.md) and [email-configuration](email-configuration.md), have no item because
they were written before a plan belonged to one; each gets an item when it is next touched.

| Plan | Status | Backlog |
| --- | --- | --- |
| [assistant-shell-tool](assistant-shell-tool.md) | Proposed 2026-11-19, revised 2026-11-24, waits for the owner; nothing built | [`tool-surface-is-one-shell`](../backlog/tool-surface-is-one-shell.md) |
| [build-metrics](build-metrics.md) | Proposed 2026-11-15, waits for the owner; nothing built | [`build-reports-no-timings`](../backlog/build-reports-no-timings.md), whose shape is still the owner's |
| [email](email.md) | Proposed 2026-11-06, revised 2026-11-23 — questions answered, rootless, identity in the site's database; waits for a yes | — (operations and the site's service endpoints) |
| [email-configuration](email-configuration.md) | Configuration excerpts for [email](email.md); nothing deployed | — |
| [ide-host](ide-host.md) | Proposed 2026-11-15; slices 0–6 merged, the search with its own archived plan; slice 7 waits on the owner | [`cannot-search-the-store`](../backlog/closed.md) |
| [outbound-headers](outbound-headers.md) | Agreed 2026-11-22 — the owner said yes, and nothing is built | [`outbound-headers-are-an-allow-list`](../backlog/outbound-headers-are-an-allow-list.md) |

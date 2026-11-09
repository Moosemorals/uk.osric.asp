<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# AI page assistant

The optional `/assistant` subsystem is host code under a reserved prefix rather than a stored page,
is off by default, and filters every operation by the signed-in owner. [Pages](pages.md) describes
the page store its tools edit; [the plan](plans/archive/ai-page-assistant.md) has the reasoning.

When enabled it is an authenticated owner feature: the views are host code under the reserved
`/assistant` prefix, and `/assistant.js` plus `/assistant.css` are seeded, protected assets.

A submitted message and its queued run are committed together. A hosted worker owns the run after the
request returns; the browser is only a viewer and can disconnect without cancelling work. SQLite is
the transcript and lifecycle record. A bounded in-memory journal carries live deltas and reconnect
replay; on restart, active runs become interrupted, unresolved approvals fail closed, and dangling
tool calls receive synthetic results rather than being replayed to the provider.

The provider boundary is an explicitly configured OpenAI-compatible streaming chat-completions
endpoint. Enabling the feature sends the system prompt, conversation transcript, and page source read
by tools to that provider. No provider SDK is used. Assistant tools have access only to the page store
through `PageEditor` — list/read/search, bounded template-source line edits with an atomic revision-hash
check, whole-page writes, tombstone deletes, a read-only virtual shell, and two read-only script
checks that parse a stored page or a supplied complete template as an ES module and change nothing —
plus a record of the site's own routes, which they can read and never write.
There is no process, filesystem, git, package, or outbound-network tool. The per-conversation approval
mode defaults to `AlwaysApprove`; `RequireApproval` waits for an individual owner decision on each
flagged mutation. Unresolved, stale, stopped, or restarted approvals never execute.

The tools also see the shell's own files, which are the site's routes (DEC-198). Every key in
`Pages:ReservedPaths` — `/edit`, `/login`, `/logout`, `/assistant`, `/admin/import`,
`/admin/export`, `/metrics`, `/status`, `/heath` and `/favicon.ico` — is listed, searched and
readable: `cat` and `read_page` return a host-written record of the route's methods, access and
purpose rather than source, because the running image carries the assembly and not the source tree,
and `ShellRoutesTests` pins the records to `ReservedPaths.Default` in both directions. Every listing
row and `ls` line carries the assistant's own permission in the `ls -l` first column —
`-rw-r--r--` where a save would be accepted, `-r--r--r--` where it would not — and `list_pages` also
marks the row `kind: shell` or `kind: page`. A save to a shell file answers "Not permitted": only the
owner changes the shell, in code. The seeded assets are ordinary pages the assistant may edit;
`Assistant:ProtectedPaths` (empty unless an operator fills it) is a separate, extra lock over a single
page, which still reads as its own source. `RequireApproval` asks the path policy before it asks the
owner, so a save it has already refused never becomes an approval nobody can grant. `check_script`
stays a page-source check, so a shell file is refused there.

An assistant reply is Markdown and the transcript renders it. The restricted profile is the second
`MarkdownRenderer` profile (DEC-164): no raw HTML, images, autolinks or unsafe URL schemes;
site-relative or `http(s)` links with `rel="nofollow noopener noreferrer"`; pipe tables through the
site's table renderer; and every heading flattened to `h3`, because the transcript's own heading is
an `h2`. A step streams as plain text and becomes formatted when `message.completed` arrives: the
page asks `GET /assistant/{id}/messages/{messageId}/content`, which renders the stored message with
the same profile as the reload, so the live step and the page after the run cannot disagree. The
route is behind the same `Editor` policy and owner filter as every other conversation operation.

The feature bounds run duration, concurrency, queue depth, steps, tool calls, arguments, page size,
results, context and event replay, and bounds each provider request by silence rather than by duration:
the deadline is re-armed for every event the provider sends, so a model that reasons for minutes is not
cut off, while a stalled stream ends the step with `provider_timeout`. The whole run uses the same
two-minute silence rule, re-armed per streamed event; keep-alive comments are not events, and the
clock stops while a tool runs or an approval waits for its owner, because neither is the run going
quiet. An approval the owner never answers leaves the run `awaiting_approval` until they stop the run
or delete the conversation, or the host restarts. A run that stops leaves its reason in the
transcript, and a message that stops at the provider's output limit is stored as `truncated`, because
the reload after a terminal event is when a reader needs the reason, and a bare status word would
not give it. Provider
content and reasoning are never logged; reasoning is display-only and is omitted from subsequent
provider requests. Assistant metrics use only low-cardinality model, tool, outcome, and token-kind
tags.

The budgets the page shows are one `BudgetSnapshot` measured from the stored transcript: the page
renders it, and the run publishes it as `budget.updated` over the journal the page already listens
to, at the start of each step, when that step's usage is stored and around each tool call. A figure
a reader watches move is therefore the figure the reload will show. The context row counts the
conversation — the whole untrimmed transcript plus the system prompt — rather than the trimmed
request that was actually sent, which is what `ContextTrimmer.MeasureChars` measures for both. The
panel is read at a glance: each row is a label and a meter with its figure drawn over it, and
`BudgetAmount` keeps every figure to at most three digits and a unit letter (`16k`, `500k`), so the
seven rows fit two columns.

Each finished turn of the transcript ends with a changed-pages card: the live pages its `write_page`
and `edit_page` calls reported saving, each linked to the page and to its editor. It is read from the
stored tool results by `PageWrites` rather than published live, because the page reloads on the run's
terminal event and a result the run itself reported is what that reload would show. A refused write
changed nothing, so it is absent; the run still going has no card, so a reader never sees a page
listed before the turn that changed it has ended.

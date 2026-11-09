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

The assistant has a small memory of its own, one owner's and nobody else's (DEC-184–DEC-188). A memory
is a note — a preference, a fact about this site, a correction, a reference to where something lives —
stored in SQLite beside the transcript and read back into the system prompt at the start of a run. Four
tools reach it: `remember`, `update_memory` and `forget_memory` are approval-gated under
`RequireApproval` and run immediately under the default `AlwaysApprove`, and `search_memories` is a
read. The owner is taken from the signed-in conversation and never from the model's arguments, so
there is no path by which a tool call names somebody else's memory.

`/assistant/memory` is the owner's view of all of it (DEC-205–DEC-209): every memory the signed-in
account holds, grouped by kind with the pinned ones first inside a kind, each row a form that saves
the content, the kind and the pin, and a count against `Assistant:MaxMemoriesPerOwner` so "the
assistant is full" is visible before a run fails to remember something. The row carries the version it
was rendered from: a save from a stale page is refused with the content the assistant left behind
rather than overwriting it, and an invalid, too long or secret note re-renders the row with the text
the owner typed still in it. Forgetting asks on a page of its own first, and says what the delete
cannot reach. The page is server-rendered and script-free, it is a shell file the assistant may read
and never save (DEC-198), and it is the only place a memory can be pinned: no tool has an argument
for that (DEC-206).

Recall happens once per run, on the first step, and the block it builds is frozen for the rest of the
run: a memory the model writes mid-run reaches the next run, not the turn that wrote it. The newest
user message becomes quoted FTS5 prefix terms, the owner's pinned memories and the memories this
conversation has already used are mixed with the ranked matches by a pure rule, and the selection
stops at `Assistant:MaxMemoriesPerRun` and `Assistant:MaxMemoryCharsPerRun`. The block is appended to
the system prompt rather than sent as its own message, so the context row counts it with everything
else and the invariant part of the prompt stays the prefix the provider's cache keys on. It declares
itself data rather than instructions, its content is HTML-escaped inside a labelled element, and it
carries each memory's id, kind, written date and use count. A recall that finds nothing adds nothing.

Nothing is remembered unless a tool call says so: there is no extraction pass and no derived profile,
so every memory is attributable to a conversation and a run. A write is refused rather than truncated
when it is too long, when the owner holds `Assistant:MaxMemoriesPerOwner` memories already, when it
duplicates a note the owner already holds (the existing id is returned instead), and when it looks
like a secret — `MemorySecrets` refuses a private-key block, a password or key assignment, a
provider-shaped key and a long encoded value, and lets a sentence *about* a secret through. An update
carries the version it read, and a stale one is refused with the current content, the same rule
`edit_page` uses for a revision hash. Memory is an enhancement, so a store that throws costs the run
its memory and a `memory_unavailable` warning in the transcript, and nothing else.

The provider receives memory and page source alike; enabling the assistant sends both. Memory is never
in a log line and never a metric tag: a recall line has the run, the count and the characters, and a
write line has the operation, the kind and the outcome.

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
quiet. A budget is a different matter: running out of steps or tool calls pauses the run instead of
failing it. The run stops where the budget forbids, writes nothing to the transcript, and the page
asks "Do you want to continue?" in a card rendered from the run's stored state, so a reload or
another device still shows it. Continue gives that budget one more window of itself — another
`MaxSteps` steps, `MaxToolCalls` calls, or `MaxToolCallsPerStep` calls in one step — and the run
carries on with the request it was about to make, so the model never learns it was interrupted.
Stopping a paused run is the ordinary Stop. An approval or a continuation the owner never answers
leaves the run `awaiting_approval` or `awaiting_continuation` until they stop the run or delete the
conversation, or the host restarts. A run that stops leaves its reason in the
transcript, and a message that stops at the provider's output limit is stored as `truncated`, because
the reload after a terminal event is when a reader needs the reason, and a bare status word would
not give it. Provider
content and reasoning are never logged; reasoning is display-only and is omitted from subsequent
provider requests. Assistant metrics use only low-cardinality model, tool, outcome, and token-kind
tags.

The budgets the page shows are one `BudgetSnapshot`: the request plan and the stored transcript,
measured once. The page renders it, and the run publishes it as `budget.updated` over the journal the
page already listens to, at the start of each step, when that step's usage is stored and around each
tool call. A figure a reader watches move is therefore the figure the reload will show. The request
row is the provider request as sent: the system prompt, the tool definitions and the messages after
masking and trimming, measured in estimated tokens against `Assistant:MaxContextTokens`.
`ChatRequestJson` measures the exact UTF-8 body,
and `TokenEstimator` turns that into tokens with the bytes-per-token ratio the provider's last
`prompt_tokens` report calibrated, a ten-percent margin and a conservative default before the first
report. `ContextPlanner` elides old tool results and long old tool-call arguments first, and drops
whole older turns only when masking cannot free enough. The panel is read at a glance: each row is a
label and a meter with its figure drawn over it, and `BudgetAmount` keeps every figure to at most
three digits and a unit letter (`16k`, `90k`), so the seven rows fit two columns.

A second fieldset, "Tokens this run", shows what the provider reported rather than what a limit
allows: prompt, cached, completion and reasoning tokens, the cached share of the prompt, and the
prompt-plus-completion total. Cached is a subset of prompt and reasoning a subset of completion, so
the list says so rather than summing them twice. The run accumulates the figures across its steps;
the page reads the stored totals after a reload, and `budget.updated` carries the same object while
the run is live. The context row is the only estimate; the usage list is the provider's own count.

Each finished turn of the transcript ends with a changed-pages card: the live pages its `write_page`
and `edit_page` calls reported saving, each linked to the page and to its editor. It is read from the
stored tool results by `PageWrites` rather than published live, because the page reloads on the run's
terminal event and a result the run itself reported is what that reload would show. A refused write
changed nothing, so it is absent; the run still going has no card, so a reader never sees a page
listed before the turn that changed it has ended.

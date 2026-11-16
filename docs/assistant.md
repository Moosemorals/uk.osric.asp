<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# AI page assistant

The `/assistant` subsystem is host code under a reserved prefix rather than a stored page, and filters
every operation by the signed-in owner. [Pages](pages.md) describes the page store its tools edit;
[the plan](plans/archive/ai-page-assistant.md) has the reasoning.

It is an authenticated owner feature: the views are host code under the reserved `/assistant` prefix,
and `/assistant.js` plus `/assistant.css` are seeded, protected assets. It is not switchable: the
provider's key is the one value a deployment must supply, the host refuses to start without
`Assistant:ApiKey` unless `Assistant:RequireApiKey` says the provider needs none, and a host that
cannot answer for the assistant never comes up at all (`assistant-always-on`).

A submitted message and its queued run are committed together. A hosted worker owns the run after the
request returns; the browser is only a viewer and can disconnect without cancelling work. SQLite is
the transcript and lifecycle record. A bounded in-memory journal carries live deltas and reconnect
replay; on restart, active runs become interrupted, unresolved approvals fail closed, and dangling
tool calls receive synthetic results rather than being replayed to the provider.

The provider boundary is an explicitly configured OpenAI-compatible streaming chat-completions
endpoint. A run sends the system prompt, conversation transcript, and page source read
by tools to that provider. No provider SDK is used. The tools are nineteen types under
`Assistant/Tools`, each carrying its name, its description, its argument schema, its approval flag,
the verb its refusals use, whether it changes a page and its own run, beside the code that runs it;
`ToolRegistry` is the only list, so the model's definitions, the dispatcher and the approval gate
cannot drift from each other (`assistant-a-tool-is-one-type`). Assistant tools reach the page store
through one read seam, `PageStoreReader` — list/read/search, bounded template-source line edits with an
atomic revision-hash check, whole-page writes, tombstone deletes, a read-only virtual shell, and two
read-only script checks that parse a stored page or a supplied complete template as an ES module and
change nothing — while every write is still `PageEditor`'s. The seam also answers the applets, so the
tree, the reserved keys, the tombstones, the key order and what a scope means are decided once
(`pages-the-store-is-read-through-one-seam`, `pages-a-listing-carries-facts`, `pages-a-page-has-a-state`, `pages-the-store-is-a-tree`). It carries facts rather than words: a node says whether a save would be accepted and
what the page compiled to, and `PageMode` and `PageImageStatus` spell them, so `ls -l`, `stat`,
`list_pages` and `read_page` agree (`pages-a-listing-carries-facts`, `pages-a-page-has-a-state`). The tools also see a record of the site's own
routes, which they can read and never write, and three tools outside the store: `fetch`, `view` and
`diff_page`.
`fetch` GETs an absolute `http` or `https` URL through the same bounded
outbound HTTP a page uses and returns the status, content type and text body; every assistant
request, the provider call included, carries `moosemorals.com (assistant)` as its `User-Agent`. It is approval-gated under
`RequireApproval`, and like
page fetch it has no host allow-list (`pages-outbound-fetch`). A fetched body is a tool result, so it is stored in the
transcript and sent to the provider on the next step: treat it as untrusted data, not instructions.
`diff_page` compares two retained revisions of a page, defaulting to the newest two, and returns a
unified diff, the two hashes and the editor link; it is a read, changes nothing and needs no
approval (`pages-diff`).

`view` is the run's own view of the site: it GETs a path on `Site:Origin` as the owner the run
belongs to, so the editor, the conversation, the memory page and the status page render rather than
answering a sign-in redirect. It answers the response as the server produced it — status, **every**
response header, the body's own bytes read as UTF-8 (base64 when the body is not UTF-8), the
redirects it followed, and the true byte length when the body is cut — because a view that
summarised the response would be a second guess at what the page did. `fetch` keeps its own
flattening: nine allow-listed headers and text decoded by the declared charset, which is what a page
wants. A view needs no approval: it reaches nothing but this site and writes nothing, the standing
`read_page` has. It has no cookie jar, so page state starts empty on every call, and it costs a
render slot and a fetch budget, so a page that has to compile first, or a path that streams, can time
out ([the shell](shell.md#a-runs-view)).

The assistant also reads and restores history. `list_revisions` answers a page's retained revisions
newest first — hash, save time, creation runtime, compile state, whether it is the head and its
editor URL — including for a tombstoned path, and takes `limit` with a `before` cursor that names the
last hash given, so a long history is read a slice at a time; a refusal names the accepted range and
the default (`Assistant:MaxListLimit` and `Assistant:DefaultListLimit`);
`truncated` says there is more. `list_pages` takes the same `limit` with an `after` cursor, because
its listing is in key order rather than in time. `read_revision` answers one revision's stored
source through the same 1-based physical lines, line ranges and result budget `read_page` uses,
so the two cannot drift. Both are reads, change nothing and need no approval; both refuse a shell
file, a missing page and a non-canonical hash, and `read_revision` refuses a hash from another
path — the rules the editor's `cmd=revision` applies. Reading a revision sends its source to the
provider exactly as `read_page` does. Every source line the two tools answer with is prefixed by
its 1-based number and `| `, because counting lines in escaped JSON is guesswork; the prefix is not
part of the stored source (`assistant-edit-page-is-safe`).

`edit_page` answers with what it changed: the added and removed line counts and the unified diff,
so an edit that compiles but mangles a block is visible in the same turn. It takes either one line
range — `endLine` defaults to `startLine` — or an `edits` array applied against the original line
numbers in one conditional save, and optional `expectFirst`/`expectLast` exact-line assertions
refuse a range whose boundary is not the text the model read. An edit that would leave the source
byte-identical is not saved; it answers `changed: false` with a warning instead (`assistant-edit-page-is-safe`,
`assistant-edit-page-is-safe`).

`restore_revision` saves a revision's source as a new revision through the ordinary
compile-and-save path: nothing is rewound, and a deleted page comes back with its history. It is the
one write to history and the most constrained write the assistant has, because the source must
already be a revision of that path — the model cannot invent content with it. It is approval-gated
and path-policy-checked, it refuses a hash whose source the live page already holds, and it cannot
scrub. That supersedes the rule the assistant plan recorded, that restoring is the owner's alone:
`write_page` already restored, by retyping a source read through a bounded argument, so refusing the
hash-shaped form kept the lossy version and dropped the faithful one.
The page contract's own XML documentation is the `reference` tool's corpus. `reference
type="IPageStorage"` answers a type's summary, remarks and every member with its signature,
parameters and returns; `reference query="cookie"` searches names and summaries when the model cannot
name the type and answers the type that holds each match. It reads `uk.osric.asp.Sdk` only, because
that is what a page may use, and refuses a type from another assembly by name. It is a read, needs no
approval, and its matches are bounded by `Assistant:DefaultListLimit` and `MaxListLimit`; the whole
result is still under the tool result cap. The prompt names both forms (`page-author-v28`), and the
human page at `/admin/reference` covers all four assemblies.

There is no process, filesystem, git, package, or other outbound-network tool. The per-conversation
approval mode defaults to `AlwaysApprove`; `RequireApproval` waits for an individual owner decision on
each flagged call. Unresolved, stale, stopped, or restarted approvals never execute.

The tools also see the shell's own files, which are the site's routes (`assistant-reads-the-shell-files`).
Every route in the shell's table — `/login`, `/assistant`, `/admin/scrub`, `/metrics` and the rest,
including the files served from the web root — is listed, searched and readable: `cat` and
`read_page` return the record the table carries of the route's methods, access and purpose rather than
source, because the running image carries the assembly and not the source tree.
Every `list_pages` row and `ls -l` line carries the assistant's own permission in a long
listing's first column — `-rw-r--r--` where a save would be accepted, `-r--r--r--` where it
would not — and `list_pages` also marks the row `kind: shell` or `kind: page`. The shell
describes itself: `help` lists every command with its options and arguments, `help <command>`
prints one, and a refused call names `help`, so the accepted form is one call away rather than
in the prompt or here. `-n` numbers the lines `cat` prints, and `grep` accepts it and changes
nothing, because a match already carries its line (`assistant-the-shell-is-applets`). The line is lexed before it is
judged (`assistant-the-shell-lexes-before-it-judges`): a quoted or escaped character is data, so `grep '&mdash;'` searches for one
and an unquoted `&` is refused, and a first word that is not a command is refused as that word
rather than as the punctuation inside its quotes. The store is a tree, and the shell reads it as
one (`assistant-the-shell-is-posix`): `ls` lists what is at a path and `ls -R`/`find` list everything below it, `grep`
reads one level and `grep -r` descends, and `grep`'s query is a basic regular expression whose
`\|` is alternation while every other punctuation character is literal. A capped listing, a
capped match, or a match that lies below the level a search read, is said in the answer rather
than left to be misread. `diff` compares a pair of keys and writes POSIX `diff`'s own output
(`assistant-the-shell-is-posix`), because where the shell implements a command it implements the POSIX one (`assistant-the-shell-is-posix`).
The commands are applets and the kernel is one pipeline (`assistant-the-shell-is-applets`): a command is one type
carrying its usage, its syntax and its behaviour, help is rendered from them, and lexing,
parsing and pattern expansion happen once. `find` is POSIX's, with `-name`, `-path` and `-type`
(`assistant-the-shell-is-posix`), and operators are refused as not there yet rather than as forbidden. A save to a
shell file answers "Not permitted": only the owner changes the shell, in code. The seeded assets
are ordinary pages the assistant may edit, like every page the shell does not answer.
`RequireApproval` asks the path policy before it asks the owner, so a save it has already
refused never becomes an approval nobody can grant. `check_script` stays a page-source check, so
a shell file is refused there.

The assistant has a small memory of its own, one owner's and nobody else's (`assistant-memory-is-the-owners`, `assistant-memory-recall-is-fts5`). A memory
is a note — a preference, a fact about this site, a correction, a reference to where something lives —
stored in SQLite beside the transcript and read back into the system prompt at the start of a run. Four
tools reach it: `remember`, `update_memory` and `forget_memory` are approval-gated under
`RequireApproval` and run immediately under the default `AlwaysApprove`, and `search_memories` is a
read. The owner is taken from the signed-in conversation and never from the model's arguments, so
there is no path by which a tool call names somebody else's memory.

`/assistant/memory` is the owner's view of all of it (`assistant-memory-page`): every memory the signed-in
account holds, grouped by kind with the pinned ones first inside a kind, each row a form that saves
the content, the kind and the pin, and a count against `Assistant:MaxMemoriesPerOwner` so "the
assistant is full" is visible before a run fails to remember something. The row carries the version it
was rendered from: a save from a stale page is refused with the content the assistant left behind
rather than overwriting it, and an invalid, too long or secret note re-renders the row with the text
the owner typed still in it. Forgetting asks on a page of its own first, and says what the delete
cannot reach. The page is server-rendered and script-free, it is a shell file the assistant may read
and never save (`assistant-reads-the-shell-files`), and it is the only place a memory can be pinned: no tool has an argument
for that (`assistant-memory-page`). It stays shell code until the assistant itself is a page
([IDEA-025](jobs/IDEA-025.md)): the memory view is one of that page's routes, and the page capability
that would have replaced it was withdrawn (`assistant-memory-belongs-to-the-assistant`).

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
`MarkdownRenderer` profile (`pages-markdown`): no raw HTML, images, autolinks or unsafe URL schemes;
site-relative or `http(s)` links with `rel="nofollow noopener noreferrer"`; pipe tables through the
site's table renderer; and every heading flattened to `h3`, because the transcript's own heading is
an `h2`. A step streams as plain text and becomes formatted when `message.completed` arrives: the
page asks `GET /assistant/{id}/messages/{messageId}/content`, which renders the stored message with
the same profile as the reload, so the live step and the page after the run cannot disagree. The
route is behind the same `Editor` policy and owner filter as every other conversation operation.
A `diff_page` result carries `editorUrl`, which opens the same rendered diff the editor's history
shows, so a model can link the owner to the line changes it read (`pages-diff`).

The feature bounds run duration, concurrency, queue depth, steps, tool calls, arguments, page size,
results, context and event replay. The shipped caps are 160 steps and 128 tool calls per run, a
500,000 estimated-token request budget with a 393,216-token output limit, and 131,072 characters
for one tool result. `deepseek-flash` reports a 1,048,576-token context window and a 393,216-token
output maximum; the request budget stays below the window so the output reserve fits. Each cap is
configuration rather than a constant — the shipped `appsettings.json` is the whole list — and every
one of them is required, so a missing or out-of-range value stops the site at startup
([configuration](architecture.md#configuration)). Each provider request is bounded by silence rather
than duration: the deadline is re-armed for every event the provider sends, so a model that reasons
for minutes is not cut off, while a stalled stream ends the step with `provider_timeout`. The whole run uses the same
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

The conversation page also shows the provider account's balance. `AccountBalanceClient` GETs the
configured provider's `/user/balance` — DeepSeek's balance route — with the assistant's key and
the same `User-Agent` every assistant request carries, bounded by `Assistant:BalanceTimeoutSeconds`
and started while the transcript is read, so a slow provider costs the panel its figure rather than
the page its answer. The panel lists each currency's total with the granted and topped-up parts, and
says so when the provider reports the balance is not available for API calls. Every failure — an
error status, a timeout, an unreachable host, or a body that is not the balance JSON — is "The
provider did not report a balance.", and the provider's body is never logged. The page reloads when
a run ends, so the figure a reader sees after a turn is the one that turn spent.

The same route feeds the `assistant.balance` gauge on `/status`. `AccountBalanceSampler` reads it once
at startup and then every `Assistant:BalanceSampleSeconds`, so the chart is a history of the spend
rather than a diary of the owner's visits. The client returns the balance or a reason and logs nothing;
the sampler logs when the reason changes, so a provider with no balance route costs one line and an
empty panel rather than a warning a minute.

The transcript reads newest first: the prompt fieldset sits above it, so the answer a reader is waiting
for arrives directly under the box that asked for it, and the page's script inserts each new card at
the top rather than scrolling a growing column.

Each finished turn of the transcript leads with a changed-pages card: the live pages its `write_page`
and `edit_page` calls reported saving, each linked to the page and to its editor. It is read from the
stored tool results by `PageWrites`, told by the tool itself whether its call writes a page, rather
than published live, because the page reloads on the run's
terminal event and a result the run itself reported is what that reload would show. A refused write
changed nothing, so it is absent; a successful edit that changed nothing is absent for the same
reason, because nothing was saved. The run still going has no card, so a reader never sees a page
listed before the turn that changed it has ended.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Glossary

The site has a vocabulary of its own, and these are the words in it. Most of them live in three
vocabularies — the assistant, pages and observability — so each entry ends by naming the type, table,
configuration key or metric prefix that owns it. The code is authoritative; this page only explains
it. Where an entry would need to state a rule, it names the decision instead, and the record stays in
[decisions](decisions.md).

The entries are alphabetical across all three vocabularies. A term is defined once here, and
[architecture](architecture.md) explains how the words fit together. **Turn** is not one of them: it
was used for two different units, so **exchange** and **step** replace it.

## Asset
A revision with no code: it is a complete representation, the shell writes no chrome around it, and
a URL naming one revision may promise never to change.

**Not** every stored page with a file extension. A page with code is never an asset, even if it is a
stylesheet or writes its own whole document. **Owner:** pages — `PageAssetPolicy`,
`PageRevisions.TemplateHasCode` and `PageAssetMap`.

## Budget
The process-wide limit on how long a render or compile may take and how many may run at once,
shared by every request.

**Not** a per-page quota, and not a promise that running page code stops: it bounds the response and
releases the slot, while the abandoned render continues (DEC-42). Nor are the assistant's budgets
this: those are the `Assistant:Max*` limits on one run or conversation, drawn as used/available
pairs (DEC-141, DEC-159).

**Owner:** pages — `PageBudget`, `Pages:Render:TimeoutSeconds`, `Pages:Render:Slots`,
`Pages:Render:MaxBodyChars`, `Pages:Compile:TimeoutSeconds` and `Pages:Compile:Slots`.

## Capability
An ability the host grants one render and withholds from the next: storage, the visitor's session,
outbound fetch, the page store read, the page's own save, the layout's slots, the Markdown and diff
renderers, and the build the site is running. A page asks for one through `ctx.Capabilities`, read-only; the host decides what
the render offers, and a capability is registered or absent, never `null`.

**Not** a feature or an extension. A feature is something the product has — the assistant is one, and
ASP.NET Core's `HttpContext.Features` are another mechanism altogether — and an extension is
something a third party adds; a capability is something one render offers a page, and it may be
absent from the next. The accessor was called `ctx.Features` until DEC-228.

**Owner:** pages — `IPageCapabilities`, `IContext.Capabilities` and the nine `Sdk` interfaces a page
may ask for; `PageCapabilities` is the shell's registry (DEC-142, DEC-228).

## Conversation
One chat thread owned by one account, with a title, an approval mode and a message counter of its own.

**Not** a run. A conversation outlives every run it contains, and at most one run is active in it at a
time.

**Owner:** assistant — `Conversation`, the `AssistantConversations` table and `IConversationStore`.

## Dashboard
The owner-only `/status` view that queries Prometheus and draws the answer as charts.

**Not** `/metrics`. The dashboard reads Prometheus with `query_range`; `/metrics` is the exporter's
scrape surface.

**Owner:** observability — `StatusEndpoint`, `Dashboard`, `StatusPage`; route `/status`.

## Diff

The line-by-line comparison of two templates. **Not** a revision, a stored value or a second copy of
the source: a diff is computed from two retained templates when it is asked for.

**Owner:** pages and assistant — `IDiffRenderer`, `DiffRenderer` and `LineDiff`; the editor's
`cmd=diff` view and the assistant's `diff_page` tool.

## Document
The whole representation a page with no code serves: the stored source is the response, and the
author writes `<html>`, `<head>` and `<body>` because the shell injects nothing.

**Not** a separate kind declared by an extension list. A page with code may write a whole document
by clearing `IPageLayout.Layout`, but it is still a fragment the shell could have wrapped, so it is
not an asset. **Owner:** pages — `PageAssetPolicy`, `PageScriptPolicy` and `SecurityHeaders`.

## Exchange
One user message plus everything it sets off: the run it starts and the messages that run produces.

**Not** a step. An exchange may span many steps, and it is a word for prose only — no type, table or
column is called an exchange.

**Owner:** assistant — deliberately prose only; a user `Message` and the `Run` it starts, joined by
`Message.RunId`.

## Head
The storage row that points a page key at its current revision.

**Not** the revision itself. A head changes and can be a tombstone; the revision it points at is
immutable, and a page has many revisions.

**Owner:** pages — `PageHead`, the `PageHeads` table.

## Image
The assembly and portable PDB emitted by compiling a template, stored beside that template in its
revision.

**Not** the page or the template. It is the compiled form of one template, and it is current only
while its `TemplateHash` and `RuntimeId` match.

**Owner:** pages — `PageImage` (`TemplateHash`, `RuntimeId`, `Bytes`, `Symbols`, `Diagnostics`),
built by `PageImageBuilder` and stored in `PageRevisions`.

## Instrument
A recording handle created on a meter — a histogram, counter or observable gauge — that the
application records measurements into.

**Not** a metric. The instrument is the in-process API; the metric is the exported name and its
series. `page.render.duration` is the instrument, and `page_render_duration_seconds` is the metric.

**Owner:** observability — `PageMetrics` and `AssistantMetrics`; `Meter.CreateHistogram`,
`Meter.CreateCounter` and `Meter.CreateObservableGauge`.

## Journal
The bounded, in-memory record of a run's events that live viewers replay from and that is dropped
after its time to live.

**Not** the transcript. A restart, an expiry or truncation can lose the journal; the transcript is
the durable record.

**Owner:** assistant — `RunStream`, `RunEvent` and `RunRegistry`; `Assistant:JournalMaxEvents`,
`Assistant:JournalMaxBytes` and `Assistant:JournalTtlMinutes`.

## Key
The canonical identity a page is stored under: absolute, lowercase, NFC, with no dot segments and no
trailing slash except at the root.

**Not** the path a request carried. Many request paths can name one key (`/Hello/` and `/hello`), and
a key containing `?` or `#` is not addressable at all.

**Owner:** pages — `PagePath.Key` and `PagePath.Normalize`; the `Path` and `PagePath` columns in
`PageHeads`, `PageRevisions` and `PageState`; `Pages:ReservedPaths`.

## Markdown
The source format a page may render to HTML, and the format an assistant reply is written in.

**Not** the page template. A template is HTML and C# blocks; Markdown is one value a page can pass
to `IMarkdownRenderer`, and the assistant's reply is Markdown only in the transcript.

**Owner:** pages and assistant — `IMarkdownRenderer` and `MarkdownRenderer`; the restricted
assistant profile, `LinkPolicy` and `AccessibleTableRenderer` are the rules that differ from the
page profile (DEC-163, DEC-164).

## Masking
Replacing old observations in a provider request with deterministic placeholders while the stored
transcript keeps the whole value.

**Not** trimming. Masking keeps the message and its tool name, id and short arguments; trimming drops
whole old turns. **Not** summarisation: a placeholder only says how much was there, and costs no model
call.

**Owner:** assistant — `ObservationMasker` and `ContextPlanner` (DEC-201, DEC-202).

## Memory
One owner's note to the assistant: a preference, a fact about this site, a correction or a reference
to where something lives, stored in SQLite and read back into the system prompt at the start of a run.

**Not** page state and not a page. Page state is one key/value store per page path, shared by every
visitor; a memory belongs to one account, and the page store stays the authority on anything a page can
answer. It is written only by a `remember` call the transcript shows, never by a background pass
(DEC-185).

**Owner:** assistant — `MemoryEntry`, `MemoryCandidate`, the `AssistantMemories` table, the
`AssistantMemoryEdits` history, `SqliteMemoryStore` and `Assistant:MaxMemoriesPerOwner`.

## Memory page
The owner's own view of the assistant's memory: `/assistant/memory`, where every note is listed by
kind, corrected, pinned or forgotten.

**Not** a stored page. It is shell code under the reserved `/assistant` prefix, server-rendered like
the editor and `/status`, so a page cannot claim the key and the assistant reads it as a shell file it
may not save.

**Owner:** assistant — `AssistantMemoryEndpoint`, `/assistant.css` and DEC-205–DEC-209.

## Message
One row of a conversation's transcript, with role `user`, `assistant` or `tool`, a per-conversation
`Sequence`, and the `RunId` that produced it.

**Not** a step. A step ends with one assistant message, but a message can also be the user's or a tool
result, and the run's messages are the transcript rather than the provider round trip that made them.

**Owner:** assistant — `Message`, the `AssistantMessages` table and `Message.ToChatMessage()`.

## Meter
A named, versioned group that instruments are created on.

**Not** a metric. It is the container, and its name is what an exporter subscribes to.

**Owner:** observability — `PageMetrics` (`uk.osric.asp.Pages`) and `AssistantMetrics`
(`uk.osric.asp.Assistant`).

## Metric
The exported measurement family Prometheus stores under a name such as
`page_render_duration_seconds`, with its type and unit.

**Not** an instrument or a series. The instrument is what the app records; the metric is the family
Prometheus sees; a series is one metric at one set of label values. A histogram metric also produces
`_bucket`, `_sum` and `_count`.

**Owner:** observability — OpenTelemetry's Prometheus exporter; names and labels are fixed in
`PageMetrics` and `AssistantMetrics`, and a tag such as `page.path` becomes the label `page_path`.

## Page
The HTTP resource served at a key: the current revision's image run against a request.

**Not** a revision, a template, an image or a seed. The page is the addressable thing; the others are
the stored forms it is served from or restored from (DEC-114).

**Owner:** pages — `PageEndpoint`, `IPageStore` and the `PageHeads` table.

## Panel
One drawing on `/status`, its title and its legend inside the SVG, and exactly one instrument behind
it.

**Not** a metric or a series. A panel may draw several series — the label values of one instrument —
but two panels never draw the same instrument.

**Owner:** observability — `Panel`, `StatusCatalogue` and `Dashboard`.

## Path
The URL path a request carries, which the shell resolves to a page key.

**Not** a key. A path is what the visitor typed or linked to and may be non-canonical; the key is the
normalised identity the store knows. `PageRequest.Path` carries the key a page runs under, not the
raw request path.

**Owner:** pages — the `path` route value and `HttpRequest.Path` in `PageEndpoint`; the resolved
value is `PagePath.Key`.

## Recall
The one moment a run is given memory: its newest user message becomes FTS5 terms, the owner's pinned
and already-used memories are mixed with the ranked matches by a pure rule, and the chosen notes are
appended to the system prompt inside the character budget.

**Not** a search the model makes. `search_memories` is the model asking; recall is the run deciding
before the first step, once per run, with the block frozen for the rest of it (DEC-186).

**Owner:** assistant — `MemoryRecall`, `MemoryQuery`, `MemorySelection`, `IMemoryStore.RecallAsync`,
`Assistant:MaxMemoriesPerRun` and `Assistant:MaxMemoryCharsPerRun`.

## Pin
The owner's "always use this": a flag on a memory that puts it first in recall, whatever the turn
happens to be about.

**Not** something the assistant can set. No tool has an argument for it; only `EditAsync`, reached
from the memory page, changes it (DEC-206).

**Owner:** assistant — `MemoryCandidate.Pinned`, the `AssistantMemories.Pinned` column,
`IMemoryStore.PinnedAsync` and `MemoryRecall.Select`.

## Restore
Saving a retained revision's source as a new revision of the page, through the ordinary compile-and-save
path.

**Not** a rewind. No stored row changes, the page's head simply moves to the new revision, and a
deleted page comes back with its history. Nor is it an install: a seed is *installed* and a saved
revision is *restored*, and both refuse the change that would leave the live source as it is.

**Owner:** pages — the editor's `?cmd=restore&hash=`, `PageEditor.SaveAsync` and
`PageSaver.SaveAsync`; the assistant's `restore_revision` and `list_revisions` / `read_revision`.

## Revision
An immutable saved row holding one template, the image built from it, its hash and the runtime id
recorded when it was created.

**Not** a head. A head points at the current revision and changes; a revision never changes after it
is written.

**Owner:** pages — `PageRevision`, the `PageRevisions` table, `RevisionHash` and
`PageTemplate.NewRevision`.

## Run
One background execution of the assistant over a conversation, started by a user message and ending
completed, failed, cancelled or interrupted.

**Not** a step. A run makes one or more provider round trips, and it also owns the tool calls and
messages they produce.

**Owner:** assistant — `Run`, the `AssistantRuns` table, `RunProcessor`, `RunWorker`, `RunQueue` and
`RunRegistry`; `Assistant:MaxSteps`, `Assistant:MaxActiveRuns` and `Assistant:MaxQueuedRuns`.

## Runtime id
The compatibility stamp on a stored image: the compile format version, the page-policy version and
the SDK and Core module version ids.

**Not** a build number or a content hash. It says which contract produced the image, not which source
went in; `TemplateHash` carries the source identity (DEC-26).

**Owner:** pages — `PageImage.RuntimeId` and `PageCompiler`.

## Scrape
One Prometheus pull of the app's `/metrics` endpoint.

**Not** a dashboard query. Prometheus scrapes into its own store; `/status` then queries that store
with `query_range`.

**Owner:** observability — `app.MapPrometheusScrapingEndpoint("/metrics")` and
`deploy/prometheus.yml`; Caddy answers `404` for `/metrics` on the public origin.

## Scrub
The removal of a deleted page's head and all of its retained revisions, with best-effort SQLite
cleanup.

**Not** delete. Delete marks a tombstone and keeps the history; scrub removes that history and is
allowed only after the page has been deleted. Nor is scrubbing forensic erasure: it cannot reach
running code, browser copies, archives or backups.

**Owner:** pages — `IPageStore.ScrubPageAsync`, `PageEditor.ScrubAsync` and the editor's
`?cmd=scrub`.

## Seed
A page template bundled with the release, used to create a page that has never existed and to install
over a seeded one.

**Not** a page or a revision. A seed is a file under `Pages/Seeds`; installing it saves the bundled
source as a new revision through the ordinary revision path, and is refused while the page already
holds it.

**Owner:** pages — `src/uk.osric.asp.Web/Pages/Seeds`, `PageAssetSeeder` and `PageAssetSeeder.SeedPaths`.

## Sent bytes
The size of a provider request as sent: the UTF-8 byte count of the body `ChatRequestJson` builds
from the model, system prompt, tool definitions, messages and envelope.

**Not** the budget any more, and not a character count. Characters undercount the JSON envelope and
escaping, so bytes are the honest measure of the wire body; `TokenEstimator` converts that count to
estimated tokens, which is what `Assistant:MaxContextTokens` bounds (DEC-204).

**Owner:** assistant — `ChatRequestJson.MeasureBytes` and `ContextPlan.RequestBytes`; the token
estimate is `TokenEstimator` and `ContextPlan.EstimatedTokens` (DEC-204).

## Session
One visitor's key/value store, which a page reaches through `IPageSession`: JSON values under short
keys, bounded per visitor and addressed by the page that wrote them.

**Not** page state. `IPageStorage` is one store per page that every visitor shares, and it is content
— exported, imported and measured; a session value is a visitor's scratch space, lives in
`PageSessions` and `PageSessionValues`, and never leaves the site. Nor is it the browser's `ISession`:
it is not ASP.NET Core's session, has no in-process cache and survives a restart.

**Owner:** pages — `IPageSession`, `PageSessionScope`, the `PageSessions` tables, the
`Pages:Session` configuration and the `s` cookie.

## Shell file
A key the site's own routes answer — `/edit`, `/login`, `/assistant`, `/metrics` and the rest of
`Pages:ReservedPaths` — which the assistant may list and read but never save over.

**Not** a page or a seed. A shell file has no stored source, revision or compile state: the assistant
reads a host-written record of what the route does, because the running image has no source tree.

**Owner:** assistant — `ShellRoutes` for the records, `PagePathPolicy` for the permission, and
`ReservedPaths` for the list itself.

## Series
One metric at one set of label values, sampled over time.

**Not** a metric. A histogram metric has a series per bucket label plus `_sum` and `_count`, and an
unbounded tag value mints a series per value — which is why a miss is tagged `page.path="(not-found)"`
rather than with the requested path.

**Owner:** observability — Prometheus; label values come from instrument tags such as `page.path`,
`outcome` and `cache`.

## Slot
A permit to run one render or one compile, with requests waiting without a thread until one is free
or their deadline passes.

**Not** a thread or a budget. A slot bounds concurrency; the budget bounds time.

**Owner:** pages — `PageBudget.RenderSlots` and `PageBudget.CompileSlots`; `Pages:Render:Slots` and
`Pages:Compile:Slots`.

## Staleness
The state of a stored image that no longer matches the current template or runtime.

**Not** a compile failure. A stale image is valid code for an older template or contract; it is
recompiled rather than treated as broken.

**Owner:** pages — `PageImage.IsCurrent` and `PageImageStatus`; `page.cache.staleness` with the
reasons `never-compiled`, `template-changed` and `runtime-changed`.

## Step
One provider round trip inside a run, which ends with an assistant message; if that message carries
tool calls, the run executes them and takes another step.

**Not** an exchange or a run. A step is one request and response with the provider; a run contains
many steps, bounded by `Assistant:MaxSteps`.

**Owner:** assistant — no row; `Run.StepCount`, the `step` field on every run event and the
`assistant.run.steps` instrument; `Assistant:MaxSteps`.

## Token estimate
The assistant's estimate of a provider request in tokens: `ChatRequestJson` measures the exact UTF-8
body, and `TokenEstimator` divides it by the bytes-per-token ratio the provider's last `prompt_tokens`
report calibrated, with a ten-percent margin and a conservative default before the first report.

**Not** the provider's exact count. The usage list shows the exact prompt, completion, cache and
reasoning tokens the provider reported; the request row is an estimate, and the provider's own limit
is the backstop.

**Owner:** assistant — `TokenEstimator`, `ContextPlan.EstimatedTokens` and
`BudgetSnapshot.ContextTokens`; `Assistant:MaxContextTokens` (DEC-204).

## Template
The text an author edits and saves: markup plus C# in `<% %>` blocks and declarations in `<%! %>`
blocks.

**Not** generated source or an image. The template is what the revision stores; the generator turns
it into C# and the compiler turns that into an image (DEC-114).

**Owner:** pages — `PageTemplate.Template`, `PageRevision.Template` and `Pages/Seeds`.

## Tombstone
A deleted page's head, which stays in the store with `IsDeleted` set and its revision pointer intact.

**Not** a scrubbed page. A tombstone keeps the history and is cleared by saving again; scrub removes
the head and revisions.

**Owner:** pages — `PageHead.IsDeleted` and `IPageStore.DeletePageAsync`; startup seeding checks for
it.

## Tool approval
The durable owner decision that a flagged tool call waits for before it executes.

**Not** a tool call. The approval records whether one call may run; the call is the assistant's
request to run it.

**Owner:** assistant — `ToolApproval`, the `AssistantToolApprovals` table,
`ToolApprovalCoordinator` and the per-conversation `ApprovalMode`.

## Tool call
A call the assistant emits in a message and the run executes once, stored back as a `tool` message.

**Not** a tool approval. A call may need an approval first, but the call is the request and the
approval is the permission.

**Owner:** assistant — `ToolCall`, `Message.ToolCallsJson`, `Message.ToolCallId`,
`Message.ToolName` and `PageToolExecutor`; `Assistant:MaxToolCalls`, `Assistant:MaxToolCallsPerStep`
and `assistant.run.tool_calls`.

## Transcript
The durable, ordered messages of a conversation, which are rebuilt into each provider request.

**Not** the journal. The transcript is stored in `AssistantMessages`; the journal is live, in memory
and dropped after `Assistant:JournalTtlMinutes`.

**Owner:** assistant — `Message`, the `AssistantMessages` table and `IConversationStore`;
`ContextPlanner` selects what is sent, and `ObservationMasker` shortens old payloads in the request
without changing the stored rows.

## Usage
The prompt, completion, cache-hit and reasoning token counts a provider reports for a step,
accumulated by `RunUsage` and stored on the run.

**Not** a budget. Usage says what was spent; the budgets say what a run may spend, and the two are
shown separately. Cached is part of prompt and reasoning part of completion, so the figures overlap.

**Owner:** assistant — `TokenUsage`, `RunUsage`, the `AssistantRuns` totals and
`AssistantMetrics.RecordUsage` (DEC-203).

## Versioned URL
A page URL with `?v=<hash>`, where the value is one retained revision's 64-character lowercase
hexadecimal hash. It selects that exact revision — never the head — and an unknown, repeated,
wrong-case, malformed or other-path hash is a `404`. The revision's verdict decides whether the
answer may be cached: only an asset gets `immutable`.

**Not** a permanent link to the page. A new save keeps the old bytes under the old hash and moves the
head; the URL is immutable, not current. **Owner:** pages — `RevisionHash.IsCanonical`, the `v`
query in `PageEndpoint`, and `PageMetrics.Asset*`.

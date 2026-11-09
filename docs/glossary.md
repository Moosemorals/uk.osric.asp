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

## Budget
The process-wide limit on how long a render or compile may take and how many may run at once,
shared by every request.

**Not** a per-page quota, and not a promise that running page code stops: it bounds the response and
releases the slot, while the abandoned render continues (DEC-42). Nor are the assistant's budgets
this: those are the `Assistant:Max*` limits on one run or conversation, drawn as used/available
pairs (DEC-141, DEC-159).

**Owner:** pages — `PageBudget`, `Pages:Render:TimeoutSeconds`, `Pages:Render:Slots`,
`Pages:Render:MaxBodyChars`, `Pages:Compile:TimeoutSeconds` and `Pages:Compile:Slots`.

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
One titled section of `/status`, holding one chart or table and its legend.

**Not** a metric or a series. A panel is a view that may query several series, and one metric may
appear in several panels.

**Owner:** observability — `StatusPanel` and `Dashboard`.

## Path
The URL path a request carries, which the shell resolves to a page key.

**Not** a key. A path is what the visitor typed or linked to and may be non-canonical; the key is the
normalised identity the store knows. `PageRequest.Path` carries the key a page runs under, not the
raw request path.

**Owner:** pages — the `path` route value and `HttpRequest.Path` in `PageEndpoint`; the resolved
value is `PagePath.Key`.

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
A page template bundled with the release, used to create a page that has never existed and to restore
a seeded one.

**Not** a page or a revision. A seed is a file under `Pages/Seeds`; restoring it saves a new revision
through the ordinary revision path.

**Owner:** pages — `src/uk.osric.asp.Web/Pages/Seeds`, `PageAssetSeeder` and `PageAssetSeeder.SeedPaths`.

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
`ContextTrimmer` selects what is sent.

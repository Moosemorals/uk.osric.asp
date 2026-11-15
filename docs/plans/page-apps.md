<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Pages can be apps

| | |
| --- | --- |
| Status | Proposed 2026-11-14 — awaiting the owner's answer; nothing is built |
| Size | Large — three capabilities, a fourth page kind and a readiness pass, each phase merging on its own |
| Area | Core, Sdk, Runtime, Web (pages and work), deploy, docs |
| Location | `src/uk.osric.asp.Core/`, `src/uk.osric.asp.Sdk/`, `src/uk.osric.asp.Runtime/`, `src/uk.osric.asp.Web/Pages/`, `src/uk.osric.asp.Web/Work/`, `deploy/Caddyfile`, `src/uk.osric.asp.Web/Pages/Seeds/help` |
| Spike | None. The assistant proves the long response, the upstream stream and work that outlives a request; the worker kind is the compiler's existing page/library split with an entry point instead of a handler |
| Decisions | Named in prose; numbers are taken from `trunk` when the work merges (`DEC-09`, `DEC-232`, `DEC-281`) |
| Backlog | [IDEA-025](../jobs/IDEA-025.md) (the assistant as a page, re-scoped by this plan), [IDEA-033](../jobs/IDEA-033.md) (a page has no per-account store), [the streaming plan](page-streaming.md) (ARCH-13, where a page learned to stream but not to keep the stream) |

## The short version

The assistant stays in the shell because a page cannot hold a live connection, read an upstream stream,
or work after its request — and, for its tools, because a page may not save another page. The first
three are runtime-shaped and generic; this plan builds them. The fourth is authority and is a
question for the port.

Each of the three is a generic capability, not a feature of the assistant:

> **A committed stream is bounded by silence, not duration, and holds a stream slot rather than a
> render slot.** A page that keeps producing may hold the connection for minutes; a page that goes
> quiet is cut.

> **A page can read an outbound response as it arrives.** A page relays an upstream stream instead of
> waiting for a whole body it will never fit in its buffer.

> **A page can start work that outlives the request.** The program is a **worker**: a stored source of
> kind Worker, compiled to an entry-point assembly, never served and never wrapped. A page starts a
> **job** of it by key, **sends it messages**, and the job **receives** them and **publishes events**.
> The page can find the job again, follow its events through a bounded journal and cancel it. A job
> works for that page — its storage is the page's, its egress is the worker's — and has no response to
> write.

The page-side surface is six calls — start, send, read events, get, list, cancel — and the worker side
is two — receive and publish. The page side is a **capability**, reached as
`ctx.Capabilities.GetRequired<IPageJobs>()`; `IContext` gains no member and stays the request and the
response. Everything else (the queue, the slots, the ring, the sweeper) is the shell's. That is the
whole of the management noise.

Nothing here is shaped like a conversation. There is no `IAssistantConversations`, no memory
capability, no chat event bus and no specialist job type. The assistant is the worked example; the
only names in `Sdk` are the ones any app would need.

The plan deliberately does not port the assistant. It builds the shell an app like it needs, then
writes down what a port still lacks. The memory lesson is the reason: a capability invented for one
view outlived the view and had to be withdrawn ([DEC-291](../decisions.md)).

## What the assistant actually does, and where each part belongs

| The assistant does | What it needs | Where that belongs |
| --- | --- | --- |
| Lists conversations, reads a transcript, edits options, deletes, shows memory rows | Durable rows owned by one account | Page code over storage — but `IPageStorage` is shared by every visitor and capped at 256 KiB per page in the shipped configuration. The owner's store is [IDEA-033](../jobs/IDEA-033.md) |
| Starts a run from a message, bounds the queue and the concurrency | Work after the response has gone | **Workers and jobs** (this plan): a worker entry point, invoked by the job runner |
| Streams deltas, replays them after a reconnect, stops a run | A sequenced journal with live subscribers and a bounded replay | **Workers and jobs** — the journal belongs to the job, not to a standalone event bus |
| Asks for approval, asks to continue | A decision sent as a message to the running job, or a stored row that starts the next job | **Workers and jobs** — the message channel; the page chooses whether to wait or to end |
| Streams from the provider | An outbound body read as it arrives | **Streaming outbound HTTP** (this plan) |
| Renders deltas as they are written | A response written while the page runs | **The stream's own lifetime** (this plan) |
| Reads and edits pages through eighteen tools | Read every page; write any page the shell does not route | `IPageCatalog` already reads everything, but `IPageWriter` writes only the page's own key. Writing *another* page is the authority question in [phase 5](#phase-5--what-a-port-still-lacks-documents-only); the tools write every page the shell does not route (DEC-253) |
| Remembers, recalls, forgets | Owner-scoped rows with search and versions | Page code over the owner's store; memory is not a capability |
| Renders a model's Markdown | A renderer that is safe on untrusted text | `IMarkdownRenderer` exists, but only the permissive profile. The restricted profile (DEC-164) is a named gap for the port |
| Reads the provider's balance | One outbound GET | `IHttpFetch` already does it |
| Shows budgets, usage and timings | Instruments | Page code can log; instruments stay the shell's |

The table is the point of the exercise. The assistant is one page plus three runtime-shaped
primitives and one authority question, not one conversation-shaped capability. A page can already do
the rest: forms, identity, antiforgery, storage, fetch, Markdown, the page store read, the page's own
save, page libraries.

## The three gaps

### 1. A stream dies with the render budget

A page can commit its response with `ctx.FlushAsync()` ([the streaming plan](page-streaming.md)), but
the render deadline still measures the whole request: five seconds by default, sixty at most. A stream
also holds one of the two render slots for its entire life, so two live viewers stop the site serving
pages.

The `FlushAsync` commit is already the right seam. Before it, a page is compiling, reading and shaping
a response, and a tight bound is what stops a hung page answering late. After it, the response is on
the wire: the question is no longer "how long may this page run" but "is this stream still alive".

### 2. An outbound fetch returns a whole body

`IHttpFetch` returns `HttpFetchResponse` once the body has been read, capped at 1 MiB. A provider that
streams its answer over two minutes cannot be relayed: the page waits for the whole body, and the
render deadline ends it long before. The assistant reads the provider's stream with its own client,
which is why it can show an answer as it is written.

### 3. A page cannot work after its request

The assistant queues a run so a phone that loses signal does not lose the work. A page's work lives
exactly as long as its render; when the visitor leaves, the page is abandoned. Everything that makes a
run resumable — the queue, the journal, the stop and the approval — is a set of in-memory singletons
in `Assistant/`, reachable only by shell code.

That is the largest gap and the one most easily over-built. The generic version is not a conversation
service or a task framework: it is **a stored worker — a program with one entry point, compiled like a
page and never served like a library — started as a job by a page and owned by the visitor who started
it**. A job has no document, no visitor and no response; giving it a page's handler would mean carrying
a response-shaped context it can never use.

## Goals

1. A committed stream is bounded by silence between flushes plus a ceiling, and the render deadline
   before the first flush is unchanged.
2. A live stream holds a stream slot, not a render slot, so viewers cannot starve ordinary pages.
3. A stream reports how it ended — completed, silent, at its ceiling, the client left, or a page
   fault — and a stream is not recorded as a ten-minute page render.
4. A page can read an outbound response body as lines arrive, under the same lifetime rule, with byte,
   silence and ceiling bounds and the existing credential and header rules.
5. A page can start a **job** of a **worker** — a stored program of kind Worker with exactly one
   `IWorker` entry point — with a first message, owned by `ctx.User`, bounded by a queue, a concurrency
   limit, a silence clock re-armed by the job's own activity, and a ceiling.
6. A page can send a job further messages; the job can wait for one without being cut for silence; a
   worker's wait is bounded by the job's ceiling, not by the silence clock. The page can list its
   owner's jobs and find the one it started, so a reload can reconnect to a running job.
7. A running job can publish bounded, sequenced events; any request owned by the same visitor can
   read them after a sequence, wait briefly for the next one, and cancel the job.
8. A job has a context and the work capabilities, and no response: it cannot write, flush, set a
   status, use a layout or a session, or read an antiforgery field. Its storage is the starting page's,
   its egress is the worker's, and it reports progress through its own published events.
9. A worker is a fourth kind: compiled to an entry-point assembly when it is saved, never served,
   never wrapped, never an asset, and never a library another page or library may `using`.
10. `/help`, the page contract, configuration, [pages](../pages.md), [architecture](../architecture.md)
    and [the jobs](../jobs/README.md) say what a page author may rely on, in the same change.
11. The page side of a worker is a capability, not a new `IContext` member; a layout is not offered it
    and a `HEAD` is offered the reads and never the writes.
12. The assistant is never named in `Sdk`, and no new type exists only for it.

## Non-goals

- **Porting the assistant in this plan.** The plan ends with what a port still lacks. The port is its
  own step, with its own plan, after the authority question in phase 5 is answered.
- **A per-account store.** [IDEA-033](../jobs/IDEA-033.md) is the owner's rows, with SQL, quotas, a
  schema lifecycle and a privacy sentence. The job registry and journal are in memory and are not a
  store a page can put its data in.
- **A general page-store writer.** A page still writes only its own key and a worker writes no
  page at all. The assistant's tools write every page the shell does not route; granting that to page
  or job code is a decision about authority, not a connection primitive, and phase 5 records it rather
  than smuggling it in.
- **A durable transport.** The job registry, journal, queue and messages are in memory. A restart
  loses them, and the starting page's storage is the record; a durable queue or event log is a
  different job with a different cost.
- **Scheduling, retries, cron, dependencies, priorities.** A job runs because a page asked, once.
- **A standalone event bus or pub/sub capability.** Events belong to the job whose viewers read them;
  there is no free-floating channel between pages.
- **`Task.Delay` in page code.** A viewer waits for a job through the job capability, not by sleeping.
  The denial stays ([the API policy](../pages.md#page-api-policy)).
- **`IAsyncEnumerable` in page code.** `ValueTask` is denied and an enumerator is easy to leak. The
  outbound stream is a line at a time.
- **Streaming through a layout, or WebSockets.** A streaming page writes its own document, as it does
  today; HTTP streaming covers SSE, long-poll and progressive HTML.
- **A seeded app, demo or SSE library.** What a page writes is page code. The first app that wants an
  SSE formatter seeds a library; the shell does not ship one speculatively (`DEC-08`).
- **A restricted Markdown profile.** It is the port's gap, named in phase 5, not a connection
  capability. Adding it here would mix a renderer decision into a lifetime plan.
- **New assistant metrics.** The assistant's own instruments stay as they are; jobs are logged, not
  folded into its panels.

## Decisions this plan proposes

**D1 — the budget splits at the commit.** Before the first flush, `Pages:Render:TimeoutSeconds` is the
whole budget, queueing included, exactly as it is today. From the first flush, the deadline is
re-armed by every flush to `Pages:Stream:SilenceSeconds`, with an absolute `Pages:Stream:MaxSeconds`
measured from the commit. A page that flushes and then goes quiet is truncated by the silence rule; a
page that keeps flushing is stopped by the ceiling. Rejected: raising `Pages:Render:TimeoutSeconds`
and letting every page — including a hung one — run for minutes; a rule keyed on content type (a page
that merely answers `text/event-stream` is not necessarily live); trusting the page to bound itself.

**D2 — a live stream moves from a render slot to a stream slot.** The stream slot is taken before the
response is committed; on success the render slot is released. `Pages:Stream:Slots` is a separate,
larger pool (default 8) because a stream is mostly waiting, while a render is mostly CPU. Rejected:
keeping the render slot (two viewers would stop the site); no bound (a public page could hold
connections until the process dies); one pool sized for the worst case (it would over-commit either
CPU or connections).

**D3 — a refused stream is a distinct exception, thrown before the commit.** When every stream slot is
taken, the first flush throws `PageStreamRefusedException` (in `Sdk`, deriving from
`InvalidOperationException`), the response is still uncommitted, and the endpoint answers `503` with a
sentence that says the site is at its live-stream limit. A page may catch it and render its own
explanation. Rejected: returning `false` from `FlushAsync` (a page that ignores the result silently
falls back to a buffered response, which is a worse failure than an exception); waiting for a slot
(a request parked behind a stream is the starvation the pool exists to stop); a `500` (capacity is not
a page bug).

**D4 — a stream reports its own end.** A streamed render records `page.render.duration` at the commit,
with the outcome the page had when it committed. The lifetime after the commit is
`page.stream.duration`, tagged with `page.path` and `outcome` (`completed`, `silence`, `ceiling`,
`client-left`, `error`). The render histogram's comment says its top bucket is past the longest
deadline; a ten-minute stream would make that false. Rejected: letting the stream land in the render
histogram's `+Inf` bucket (the slow-pages panel would become meaningless); no instrument at all (an
operator cannot see live streams, and the assistant's existing panels are not about pages).

**D5 — `IHttpFetch` grows one method, and the stream is line-oriented text.** `StreamAsync(
HttpFetchRequest)` returns an `HttpFetchStream` with `Status`, `Headers`, `Failure`, `Truncated`,
`Task<string?> ReadLineAsync()` and `Task CloseAsync()`. A null line is the end; `Failure` and
`Truncated` say why. The buffered `SendAsync` is unchanged and remains the right call for a small
body. Rejected: `IAsyncEnumerable` (the page policy denies `ValueTask` and its disposal is easy to
forget); `Stream` or `byte[]` (the page contract is text); a second capability (`IHttpFetch` is
already the page's only egress, and its credential and header rules must not be duplicated).

**D6 — the outbound stream has its own silence and ceiling.** The request's headers are bounded by
`Pages:Fetch:TimeoutSeconds` as today. The body is then bounded by
`Pages:Fetch:StreamSilenceSeconds` between lines, `Pages:Fetch:StreamMaxSeconds` in total, and
`Pages:Fetch:MaxStreamBytes`; a stream that hits the byte cap ends with `Truncated`. Rejected: one
total timeout (an answer that thinks for two minutes is healthy); no ceiling (a provider can stream
forever); reusing `MaxResponseBytes` silently (a stream's cap and a fetch's cap answer different
questions).

**D7 — flush before the slow read.** The render deadline covers everything before the first flush,
including a fetch. A page that wants a long upstream read writes and commits its document first; the
commit is what moves it onto the stream and outbound-stream clocks. This is a consequence, not a
mechanism, and `/help` says it beside the streaming example.

**D8 — a worker is a fourth kind.** A stored source is **Text**, **Page**, **Library** or **Worker**. A
Worker is declaration-only like a library, but it declares exactly one public type with a parameterless
constructor implementing `IWorker`, and it is compiled to an entry-point assembly when it is saved. It
is never served, never wrapped, never an asset and never a library: the closure includes libraries and
excludes workers, so a page or library that names a worker gets a missing-namespace diagnostic. The
kind is the lifecycle, not a badge: it says what the runtime does with the source. Rejected: a library
with an `IWorker` and a per-start compile (no entry-point diagnostic when the author saves, a compile
on every job, and a page could `using` the program as if it were a module); a future render of the page
that started it (a page's handler writes a response that does not exist, and its layout, session and
antiforgery members would be dead weight); a `Func<IContext, Task>` (it closes over a dead request and
hands the shell an opaque lifetime); an assistant run type (the specialist capability this plan exists
to avoid).

**D9 — a job is one run of a worker, started by a page and owned by the visitor.** `IPageJobs.StartAsync(
workerKey, firstMessage)` starts it; the job runs as the signed-in owner. A missing key, a page, an
asset, a library, a reserved key, an anonymous render and a full queue are refused as values, because
the kind is known at save and the start needs no compile to say no. The job works **for the starting
page**: the page is the principal and the worker is the code. Rejected: a job that names another page
(a page can already link to one; widening the authority is a later decision); a job as a `Func` or a
delegate; an assistant run type.

**D10 — six page calls, two worker calls, and the owner is structural.** `IPageJobs` — `StartAsync`,
`SendAsync`, `GetAsync`, `ListAsync`, `ReadEventsAsync`, `CancelAsync` — is offered to every page. The
running job is handed an `IWorkerContext` — `Id`, `Path` (the starting page), `Worker` (the worker's
key), `User`, `Input` (the first message), `Aborted`, `Capabilities`, three log members,
`ReceiveAsync` and `Publish(type, dataJson)`. A message is a string (JSON or text) bounded by
`Pages:Jobs:MaxMessageBytes`; the pending queue is bounded by `MaxPendingMessages`. `ReceiveAsync`
takes an optional wait, capped by `MaxWaitSeconds`; it returns `null` when the wait elapses or the job
can no longer receive, and `Aborted` tells the two apart. `ListAsync` answers the owner's jobs with `Id`,
`Worker`, `Path`, `State`, `StartedAt`, `EndedAt` and `Failure`, where the state is `Queued`,
`Running`, `Completed`, `Failed` or `Cancelled`, so a page that lost its id can find the job it
started, and `ReadEventsAsync` replays from a sequence. `IPageJobs` is a **capability** in `Sdk`,
registered by `PageChain` beside storage and fetch and reached as
`ctx.Capabilities.GetRequired<IPageJobs>()`; `IContext` does not grow. A layout is not offered it at
all, and a `HEAD` is offered the reads and not `StartAsync`, `SendAsync` or `CancelAsync`. Neither
surface takes an owner: `IPageJobs` is bound to `ctx.User`, an anonymous render is refused, and every
read, send and cancel filters on the job's owner before it answers. Rejected: an owner argument
(`DEC-190`); one interface for both sides (a viewer would meet `ReceiveAsync`, and a worker would meet
`SendAsync` it has no reason to call); a typed message object (`JsonSerializer` is already the page's,
and a worker that wants a shape can parse one).

**D11 — the journal is in memory, bounded and replayable; the durable record is the page's.** A job's
events are `(sequence, type, json)` in a ring bounded by `Pages:Jobs:MaxEvents`, `MaxEventBytes` and
`RetentionMinutes`, with live subscribers and a replay window. A viewer that asks for a sequence the
ring no longer holds gets a reset event. There is no `PageJobs` table. A worker does not survive a
restart, so a transport row would only need tidying up after every crash, and the starting page's
storage is the record. A page that finds its own state says running while `GetAsync` returns null knows
the host restarted; the interrupted state is the page's, not the shell's. Rejected: a durable journal
or job table; a durable queue (re-running work with non-idempotent side effects is a different
feature); a durable message channel (a pending decision belongs in the page's storage).

**D12 — a job has its own lifetime and its own slot.** A job takes a job slot
(`Pages:Jobs:MaxConcurrent`), not a render slot, because it is not answering a visitor. Its deadline
is `Pages:Jobs:SilenceSeconds` re-armed by every `Publish` and by every return from `ReceiveAsync`,
plus `Pages:Jobs:MaxDurationSeconds` from the start. The shipped ceiling is nine hours (32,400 s), and
the range may go to seven days: a worker that runs for a working day is the point, and the silence
clock, not the ceiling, is what catches a stall. **A wait in `ReceiveAsync` pauses the silence
clock**: a job waiting for its owner is not a job that has gone quiet, and the ceiling is what bounds
the wait. A timed wait returns and the clock resumes, so a worker can do periodic work between
messages without a sleep primitive. A job that keeps working but never publishes or receives is cut, so a wedged job cannot burn
the ceiling. A job that waits holds its slot; a worker that would rather free it ends the job at the
question and a new job resumes, which is the page's choice. Rejected: the render deadline (a job
legitimately outlives a request); an absolute deadline only (a page that hits a synchronous loop would
run for the ceiling); sharing the render slots (a background job would pay for a visitor's slot);
cutting a job that is waiting on its owner (the approval case).

**D13 — a viewer waits, it does not sleep.** `ReadEventsAsync(jobId, after, wait)` returns when there
are events, when the job ends, or when the wait elapses — whichever is first — with `Events`, `Next`,
`Reset`, `State` and `Failure`. The wait is capped by `Pages:Jobs:MaxWaitSeconds` and by the
render's stream budget. A viewer streams the batches as SSE and writes a comment when one is empty.
Rejected: allowing `Task.Delay` (a general fairness hole, and the policy denies it); a poll per reload
(a whole document per event); a second pub/sub capability (the wait belongs to the journal).

**D14 — a job's capabilities split data from code.** `IWorkerContext` has no `Write`, `FlushAsync`,
`SetStatus`, `SetContentType`, `SetHeader`, `Redirect`, `AntiforgeryField` or `IPageLayout`; its
`Capabilities` offer `IPageStorage`, `IHttpFetch`, `IPageCatalog` and `IPageJobs`, with **storage bound
to the starting page** (the data) and **egress and catalog bound to the worker** (the code). A job and
its view therefore share storage: the job writes the transcript and the view reads it, while the
worker's credential stays the worker's. A worker is not a view: it publishes text or JSON and the page
renders it, so `IMarkdownRenderer` is the page's and not the worker's — a worker has no document to
render into, and whether text is rendered with the author or the restricted profile is the view's
decision. `IPageWriter` is not offered: a job keeps data, not code, and the authority to save a page is
the port's question. Rejected: one key for all three (either the worker owns the page's data or the
page owns the worker's credential); giving the worker a renderer (it would be formatting a document
nobody receives); an `IContext` (a job looks like a visitor's request and is handed a response nobody
receives); no capabilities at all.

**D15 — a worker's image is a page's mechanics without a handler.** The worker's source and its
`using` closure are policy-checked and emitted at save, and the image and symbols are stored on the
revision with the Worker kind, the `TemplateFingerprint`, the `RuntimeId` and the closure fingerprint.
A worker whose library moved is rebuilt lazily on its next start, the way a page is rebuilt on its next
request; the runner loads the image and finds the single `IWorker`, and a load or compile failure fails
the job with its reason on the journal. There is still no shared runtime assembly (`DEC-289`): each
worker has its own. Rejected: compiling per start (the save cannot say whether the entry point is
valid, and every job pays for it); caching a loaded assembly across jobs (statics would become a
channel between jobs); a generated handler (the worker has no response).

**D16 — the controls stay where they are.** `Pages:Render` keeps the pre-commit budget; a new
`Pages:Stream` section owns the stream slots, silence and ceiling; `Pages:Fetch` grows the outbound
stream values; a new `Pages:Jobs` section owns the job bounds. Every value is required, with a range
that stops a zero switching a guard off (`DEC-282`). Rejected: one section for all page work (the
three lifetimes are different questions and an operator should see them apart); fallbacks in code (a
missing value must stop the site, not run with a number nobody chose).

The shipped values, for the implementation to start from:

| Section | Values |
| --- | --- |
| `Pages:Stream` | `Slots` 8, `SilenceSeconds` 60, `MaxSeconds` 1800 |
| `Pages:Fetch` (new) | `StreamSilenceSeconds` 30, `StreamMaxSeconds` 600, `MaxStreamBytes` 4 MiB |
| `Pages:Jobs` | `QueueCapacity` 8, `MaxConcurrent` 2, `SilenceSeconds` 120, `MaxDurationSeconds` 32400, `MaxEvents` 512, `MaxEventBytes` 256 KiB, `RetentionMinutes` 30, `MaxWaitSeconds` 30, `MaxMessageBytes` 16 KiB, `MaxPendingMessages` 32 |

## Phases

Each phase ends with `make check` and stops for the owner's merge (DEC-281). The branch for this plan
is `idea-025`; the implementation branch is `page-apps`, one phase at a time.

### Phase 0 — the plan and the programme

- This plan, in place of the first version of IDEA-025's sketch.
- The programme's step 8 points at this plan and says the port is later.
- IDEA-025 records that the plan re-scopes it: the three capabilities this plan builds, and the gaps
  it leaves.

**Done when** a reader can tell what is built, what is deliberately not, and which gap decides the
port.

### Phase 1 — the stream's own lifetime

- A `RenderLifetime` (Web, internal) owns the linked source, the render deadline, the stream clocks and
  which slot is held. `RenderSession` delegates to it; `SimpleContext` attaches it instead of a bare
  `CancellationToken`.
- `FlushAsync` asks the lifetime to begin the stream on the first flush and to re-arm the silence clock
  on every flush. It throws `PageStreamRefusedException` when no stream slot is free, before anything
  is committed.
- `PageBudget` gains the stream semaphore; `PageEndpoint` answers a refusal with `503`, and keeps the
  existing truncation path for a stream that dies.
- `PageMetrics` records the render at the commit and the stream when it ends; `StatusCatalogue` and
  `PageMetricsTests` learn the new instrument.
- `Sdk`: `PageStreamRefusedException` in `PublicAPI.Unshipped.txt`.
- `deploy/Caddyfile`: nothing, after all. The pinned Caddy 2.11 flushes a `text/event-stream` response
  as it is written and `flush_interval -1` stops it waiting for the app, so a compressed page stream
  arrives one event at a time — measured against the pinned binary through this Caddyfile, six
  one-second events a second apart with `Content-Encoding: zstd`. A response matcher cannot express
  "not this content type", so the alternative was an explicit list of compressible types to maintain,
  for no gain. `deploy/README.md` records the measurement and why the assistant's path exclusion is
  that route's rather than streaming's.
- `/help`, [pages](../pages.md#streaming), [pages](../pages.md#page-execution-budget), the
  [glossary](../glossary.md) entries the budget and the slot already had, [metrics](../metrics.md),
  [testing](../testing.md) and [logging](../logging.md). [configuration](../architecture.md#configuration)
  needs no edit: it names the pattern and sends a reader to the subsystem, and the keys are documented
  where the budget is.

**Done when** a page can stream past `Pages:Render:TimeoutSeconds` while a page that stalls before its
first flush still answers `504`, and a page that flushes then goes quiet is truncated with its reason
in the log and the metric.

### Phase 2 — the outbound stream

- `Sdk`: `IHttpFetch.StreamAsync` and `HttpFetchStream` in `PublicAPI.Unshipped.txt`.
- `Web`: `OutboundFetcher` opens a response and hands back a line reader; `PageFetcher` shapes it and
  keeps the credential, header, redirect and origin rules; `PageHttpFetch` counts it against
  `MaxRequestsPerRender`, tracks it, and closes it when the render ends or is abandoned.
- `Pages:Fetch` grows `StreamSilenceSeconds`, `StreamMaxSeconds` and `MaxStreamBytes`.
- Tests: line splitting across chunk boundaries and multi-byte characters; a mid-stream failure; the
  silence and ceiling rules; the byte cap; a redirect that drops a credential; a page that relays a
  stream and flushes each line; a page that names the new types compiles under the API policy.
- `/help` and [pages](../pages.md#page-api-policy).

**Done when** a page can relay an upstream line stream to a visitor that outlives the render budget,
with every failure a value and the socket closed on every path.

### Phase 3 — the Worker kind

- `Core`: `PageKind` gains `Worker`; `PageRevisions.TemplateKind` stores it (3) beside text, page and
  library. A declaration-only source is generated as a library; the compiler refines the verdict to
  Worker when it declares exactly one `IWorker`, and reports two as a diagnostic. No data migration: the
  column is a nullable int and an existing source keeps its kind until it is saved again.
- `Sdk`: `IWorker` and `IWorkerContext` in `PublicAPI.Unshipped.txt`. `IWorker` is the one entry point a
  worker declares; `IWorkerContext` is its parameter, so the contract exists when the worker compiles.
  The runner that supplies the context arrives in phase 4, so this phase's `IWorkerContext` holds only
  the two members that are true without it — `Worker` and `User`. It is a contract that grows: phase 4
  adds `Id`, `Path`, `Input`, `Aborted`, the log members, `ReceiveAsync`, `Publish` and
  `Capabilities`.
- `Runtime`: compile a worker — its source and its `using` closure, policy-checked, emitted to an
  assembly — and load it so the entry point can be found. The entry point is exactly one public type
  with a parameterless constructor implementing `IWorker`, and anything else is the new `PAGE006`
  (`PAGE005` is retired, DEC-254). The declaration-only compile is the one entry point for both
  verdicts, so `CompileLibrary` becomes `CompileWorker`: it checks a library and emits a worker, and
  `CompileResult.Kind` carries the refined answer. `IPageCompiler` and the `Runtime` public surface
  grow the compile and load of a worker. The compile is recorded as `page.compile.duration` under the
  reason that caused it — `save`, `lazy` or `import` — because that is what the tag answers; the
  phase-4 runner's lazy rebuild is where a worker-specific reason would belong.
- `Web`: `PageImage` stores a worker's bytes and symbols; `PageImageBuilder` chooses the worker compile
  when the source declares the entry point; `PageAssetPolicy`, the endpoint, the renderer's
  `FromStored` and the versioned-asset path treat a worker as never served, never wrapped and never an
  asset; `PageLibraries`' closure candidates exclude workers, so a page or library naming one gets a
  missing-namespace diagnostic; the page list, the editor state, `PageMode`/`PageImageStatus` and the
  assistant's facts say "worker".
- Tests: a declaration-only source with one `IWorker` saves as a worker with an image; two entry points
  is a diagnostic; a source that does not compile is stored with its diagnostics; a worker key answers
  `404`; a worker is never an asset and a `?v=` naming one is `404`; a page or library naming a worker
  is a compile error; a worker whose library moved is rebuilt; the editor and the assistant see the
  kind.
- `/help`, [pages](../pages.md) and its kinds table, the [glossary](../glossary.md), the assistant's
  prompt vocabulary, [architecture](../architecture.md), and [storage](../storage.md) for the column.

**Done when** a stored declaration-only source with one entry point is a worker: compiled and checked
when it is saved, stored with its image, never served, and invisible as a library to pages.

### Phase 4 — jobs that outlive the request

- `Sdk`: `IPageJobs` and its values (`PageJobStart`, `PageJobSendResult`, `PageJobInfo`, `PageJobState`,
  `PageJobEvent`, `PageJobEvents`). `IWorker` and `IWorkerContext` exist from phase 3, and this phase
  **grows** `IWorkerContext` to what D10 and D14 describe: `Id`, `Path`, the first `Input`, `Aborted`,
  `ReceiveAsync`, `Publish`, the log members and `Capabilities` — the same `IPageCapabilities` seam a
  page reaches through `ctx.Capabilities`, so a worker asks for a capability exactly as a page does and
  the host decides which are there.
- `Web/Work`: the generic queue (the existing `BoundedWorkQueue`), an in-memory registry, a bounded
  journal with live subscribers and a wait, and a `BackgroundService` runner. The assistant's
  `RunStream` shape is the starting point, not a shared type: a job is not a run. The runner loads the
  worker's stored image — rebuilding it lazily when its library moved — finds the single `IWorker`,
  invokes it, and releases the assembly at the end.
- `PageChain` registers `IPageJobs` in the capability set — never on `IContext`, never for a layout,
  and read-only for a `HEAD`.
- The job's context: the owner as `PageUser`, the starting page as `Path`, the worker key as
  `Worker`, the first message as `Input`, a timed or indefinite `ReceiveAsync` for later messages,
  and the job's own deadline. Its `Capabilities` carry `IPageStorage` bound to the starting page and
  `IHttpFetch`, `IPageCatalog` and `IPageJobs` bound to the worker, with no renderer, no writer and no
  layout. `SendAsync` writes a bounded pending queue; `ReceiveAsync` pauses the silence clock while
  it waits; `Publish` writes the event and wakes a viewer.
- `Pages:Jobs` with `QueueCapacity`, `MaxConcurrent`, `SilenceSeconds`, `MaxDurationSeconds`,
  `MaxEvents`, `MaxEventBytes`, `RetentionMinutes`, `MaxWaitSeconds`, `MaxMessageBytes` and
  `MaxPendingMessages`.
- Tests, one behaviour each:
  - the capability: a page reaches it through `ctx.Capabilities.GetRequired<IPageJobs>()`, a layout has
    none, and a `HEAD` refuses a start, send and cancel;
  - a worker starts, writes the starting page's storage, and the view reads it;
  - a page starts it by key, sends it a message, and the worker receives it;
  - an indefinite wait in `ReceiveAsync` outlives the silence clock but not the ceiling; a timed wait
    returns and the worker publishes afterwards;
  - a full pending queue and a message after the job ends are refused as values;
  - a page lists its owner's jobs and reconnects to the running one; a viewer reads the journal and
    waits for the next event;
  - the ring drops events past `MaxEvents`, `MaxEventBytes` and `RetentionMinutes`, and a viewer that
    asks for a dropped sequence gets a reset; a restart loses the journal, and the page's own state is
    the record;
  - a worker has no `IMarkdownRenderer`; the page renders the text it published;
  - cancel ends the job at its next await;
  - a missing key, a page, a library, a reserved key, an anonymous render and a full queue are refused
    as values;
  - a worker whose image fails to load or compile is a failed job with its reason;
  - a job that works without publishing or receiving is cut;
  - a job's context has no response members; storage is the page's and the credential is the worker's;
  - another account cannot read, send to or cancel it, and the render slot count is unchanged while a
    job runs.
- `/help` (with `ctx.Capabilities.GetRequired<IPageJobs>()` beside the other capabilities), [pages](../pages.md),
  the assistant prompt's capability section with its `Version` bumped, and [logging](../logging.md) for
  the job's lines.

**Done when** a stored worker can be started as a job by a page, sent a message, found again by a
second request, streamed, stopped and read, with no assistant type in `Sdk`.

### Phase 5 — what a port still lacks (documents only)

- Re-scope [IDEA-025](../jobs/IDEA-025.md): the conversation view, the run as a worker and a job, the
  memory view, the approval as a stored decision and a new job, and the transcript on the owner's
  store.
- Name the remaining gaps honestly: [IDEA-033](../jobs/IDEA-033.md) for the transcript and memory, the
  page-store write authority for the tools, and the restricted Markdown profile.
- The authority question, written as a decision for the owner: does a worker get a page writer for the
  keys the assistant's tools may edit, so one configured worker (or one page) has the authority and
  nothing else does? A page writer is the largest authority in the port and is not a connection
  primitive; phase 5 records the choice, it does not make it.
- The audit in [architecture](../architecture.md#where-a-view-lives) and the programme.

**Done when** the port is either a step with a plan and every capability it needs, or the audit says
why the assistant stays shell and the capabilities stand on their own.

## Tests

The suites follow the phases; every behaviour is a NUnit test, named for the behaviour.

- `Web.Tests/PageStreamingTests` — the lifetime in isolation: the re-arm, the ceiling, a refusal, the
  slot release, the buffered path unchanged.
- `Web.IntegrationTests/PageStreamingTests` — the real host: a page that burns past the render timeout
  *after* a flush is served; one that burns past it *before* a flush answers `504`; a stream that goes
  quiet is truncated; `HEAD` still commits with no body; a client that leaves stops the page.
- `Web.Tests/PageFetchTests` (or a new `PageFetchStreamTests`) — the line reader, the caps, the
  credential and redirect rules.
- `Web.IntegrationTests/PageFetchTests` — a scripted `IPageFetcher` answers a stream, and a page relays
  it through a real flush.
- `Web.IntegrationTests/PageWorkerTests` — a worker is compiled at save, answers `404`, is never an
  asset and is not a closure candidate for a page; a worker whose library moved is rebuilt.
- `Runtime.Tests/PageCompilerTests` — a worker compiles with its closure, exactly one entry point, and
  its policy violations are its own lines.
- `Web.IntegrationTests/PageJobTests` — a worker starts, is sent a message, is found after a reload,
  observes, waits, cancels, refuses, isolates, writes the starting page's storage and has no response
  at all.
- `Web.Tests/PageApiPolicyTests` — a page that names the new Sdk types compiles; `Task.Delay`,
  `ValueTask` and `IAsyncEnumerable` stay denied until a decision says otherwise.

The browser suite gains nothing: every behaviour here is provable through the real host with an HTTP
client.

## Risks

| Risk | Mitigation |
| --- | --- |
| **Slot accounting double-releases or leaks.** The render slot is released at the commit and the stream slot at the end; an abandoned page keeps running. | One owner (`RenderLifetime`) with an idempotent release, and tests that assert the semaphore's count after each ending. |
| **A page uses the long stream to burn CPU.** | The stream slot, the silence rule and the ceiling; the residual is the same in-process trust SEC-03 records. |
| **The worker kind touches every `PageKind` switch.** A missed branch serves, wraps or closes a worker. | One predicate set on `PageAssetPolicy`/the endpoint, and a test for each of the four kinds at each branch. |
| **The journal grows without bound.** | `MaxEvents`, `MaxEventBytes` and `RetentionMinutes` bound the ring; a lagging viewer gets a reset and reads the page's storage. |
| **A restart loses the journal.** | The worker does not survive a restart either; the page's own state is the record, and a page that says running with no job knows it was interrupted. There is nothing in the shell to tidy up. |
| **A worker stays in the job slot while it waits for a message.** | A wait pauses the silence clock but not the ceiling; `MaxConcurrent` bounds the number of waits; a worker that would rather free the slot ends the job at the question and a new job resumes. |
| **A page floods a worker.** | `MaxPendingMessages` bounds the queue and `SendAsync` refuses rather than grows it. |
| **A worker becomes a module by accident.** A page names a worker and compiles it in. | The closure includes libraries and excludes workers; the diagnostic is a missing namespace. Code shared by a view and a worker is a library. |
| **A job becomes a scheduler or a task framework.** | The non-goals name scheduling, retries, durability and dependencies; a new need gets a job file, not a feature. |
| **A job survives the request but not the deploy.** | Jobs are in memory and the plan says so; the starting page's storage is the record, and a page detects an interrupted job by asking for it. |
| **The outbound stream is a second egress with different rules.** | One implementation of the credential, header, redirect and origin rules; the buffered and streaming paths call the same helpers. |
| **The page API policy makes the stream awkward.** | The line-at-a-time shape is chosen for `Task`, not `ValueTask`; the policy test pins it. |
| **The render histogram loses its meaning.** | The commit is the render's end for a streamed page, and `page.stream.duration` carries the rest. |
| **A viewer holds a stream open forever.** | The stream ceiling, the journal's retention, and `ReadEventsAsync`'s bounded wait. |
| **The port is assumed to be delivered.** | This plan says it is not, and phase 5 records the authority question rather than answering it by code. |

## Considered and rejected

- **`IAssistantConversations`, `IMemoryWriter`, `IMemoryCatalog`.** The specialist capabilities this
  plan exists to avoid. Memory is the assistant's own store (DEC-291); a conversation capability would
  encode one app's shape and leave the next app needing another.
- **One capability for "the assistant".** See above. The page contract names connections and work, not
  products.
- **Raising `Pages:Render:TimeoutSeconds`.** Cheap, and it makes every page host a hung page for
  minutes while holding a render slot. The problem is not the number; it is that one clock covers two
  different lifetimes.
- **Streaming through a layout.** A layout needs the whole body before it can start; a second protocol
  for one customer.
- **WebSockets.** HTTP streaming covers the shape, and the site already has `FlushAsync` and SSE.
- **A free-standing event bus.** Events without a job have no owner, no lifetime and no reason; the
  journal is a part of the job.
- **A durable journal or job table.** A worker does not survive a restart, so its transport rows would
  have to be tidied up after every crash, and the page's storage is the record. A durable **queue** is
  rejected for the same reason plus one more: a job's side effects are not idempotent.
- **A durable message channel.** A message is delivered to a live job; a restart loses the pending
  queue, and the page's storage is what a resumed job reads. A message bus is a different job.
- **A job as a page render.** A page's handler writes a response; a job has none, and its context
  would carry layout, session and antiforgery members it must not use. A worker entry point says what
  a job is without pretending it is a visit.
- **A library with an entry point, compiled per start.** It needs no kind, but the author gets no
  entry-point diagnostic when they save, every job pays for a compile, and a page could `using` the
  program as if it were a module. The worker kind is the same idea with the lifecycle made explicit.
- **A request/response RPC between a page and a worker.** Messages are one way in and events one way
  out; a worker that wants to answer publishes an event, and a page that wants a reply reads it.
- **A typed message object.** A message is a string; a worker that wants a shape parses it with
  `JsonSerializer`, which the page contract already offers.
- **A renderer in the worker.** A worker publishes text or JSON; the page renders it. A worker has no
  document, and the author/restricted profile choice belongs to the view.
- **A worker a page may `using`.** A worker is a program; its types are its own. Shared code is a
  library both the page and the worker name.
- **A general page writer now.** It is the largest authority in the port and is not a connection
  primitive. Phase 5 writes the question down.
- **`Task.Delay` and `IAsyncEnumerable` in pages.** The wait belongs to the journal; the stream is a
  line at a time. Both denials keep the render's bound meaningful.
- **A seeded SSE helper or a demo app.** Page code, not shell; the first app seeds a library
  (`DEC-08`).
- **New assistant metrics.** The assistant's panels answer the assistant's questions; a job's
  lifetime is a page instrument (`page.stream.duration`) or a log line, not an assistant one.

## Components

| Component | Responsibility | Where |
| --- | --- | --- |
| `RenderLifetime` | The two clocks, the slot, the refusal and the end reason for one render | `src/uk.osric.asp.Web/Pages/` |
| `PageBudget` and the three options records | The stream, job and fetch bounds | `src/uk.osric.asp.Web/Pages/` |
| `IHttpFetch.StreamAsync` and `HttpFetchStream` | The page's outbound body, a line at a time | `src/uk.osric.asp.Sdk/` |
| `OutboundFetcher` and `PageFetcher` | One implementation of the outbound rules for both paths | `src/uk.osric.asp.Web/Pages/` |
| `PageKind`, `PageImage` and `PageAssetPolicy` | The fourth kind, its stored image and the never-served, never-wrapped, never-asset rules | `src/uk.osric.asp.Core/` and `src/uk.osric.asp.Web/Pages/` |
| `Runtime` worker compile and load | A worker and its closure, policy-checked, emitted and loaded for the entry point | `src/uk.osric.asp.Runtime/` |
| `IPageJobs` | The page-side capability: start, send, observe, find, wait and cancel | `src/uk.osric.asp.Sdk/` |
| `IWorker` and `IWorkerContext` | The entry point a worker declares, and the work context a job is given: receive messages, publish events (no renderer) | `src/uk.osric.asp.Sdk/` |
| `Web/Work` | The queue, the in-memory registry and journal, the message channel, the live fan-out and the runner | `src/uk.osric.asp.Web/Work/` |
| The job runner | Loads the worker's image, finds the entry point, runs it under a job slot and releases it | `src/uk.osric.asp.Web/Work/` |
| `/help` | The author manual: the lifetime, the stream, the worker kind, the message protocol, what to publish and what to keep in storage | `src/uk.osric.asp.Web/Pages/Seeds/help` |

## Documentation and decisions

- `docs/pages.md` — the execution budget, streaming, the API policy, the four kinds with a worker
  section, and a new section each for the outbound stream and jobs.
- `docs/architecture.md` — configuration's new sections, and the audit if a route changes (it should
  not: this plan adds capabilities and a kind, not views).
- `docs/metrics.md` — `page.stream.duration`, the `worker` compile reason, and the correction to the
  render histogram's top-bucket sentence.
- `docs/logging.md` — a job's start, end and reason; a message is never logged; no prompt, transcript
  or memory in a log line.
- `docs/storage.md` — the fourth `TemplateKind` value, and that jobs and their events are not stored.
- `docs/glossary.md` — **Worker** and **Job**, and why a worker is not a thread.
- `docs/testing.md` — the suites and what belongs in the real host.
- `src/uk.osric.asp.Web/Pages/Seeds/help` — the author manual, updated with each phase (DEC-173).
- `docs/jobs/IDEA-025.md` and `docs/jobs/README.md` — the re-scope and the programme.
- Decisions proposed, numbered at merge: the split budget, the stream slot and its refusal, the stream
  instrument, the line-shaped outbound stream, the worker kind, the job as a run of it, the job surface
  (`IPageJobs`) and the worker contract (`IWorker`, `IWorkerContext`), the message channel, the worker
  image and staleness, the data/code capability split, the closure exclusion, the in-memory journal,
  the job's own lifetime and slot, the bounded wait, and the configuration sections.

## Delivered when

- A stored page can hold a live response for minutes, bounded by silence and a ceiling, without holding
  a render slot.
- A stored page can relay an outbound stream, line by line, under the same rules as a fetch.
- A declaration-only source with one entry point is a **worker**: compiled and checked when it is
  saved, stored with its image, answered `404`, never wrapped, never an asset and never a library a
  page may name.
- A stored page can start a worker as a job, send it messages, find it again after a reload, follow
  and cancel it. The job's durable state is the starting page's storage; the journal is live only, and
  a restart loses it.
- `/help` teaches all of it, and the page contract's XML documentation matches.
- `make check` passes after every phase, and each phase has merged on the owner's answer.
- IDEA-025 says what a port still lacks, and the authority question is written down for the owner.

## Open questions

1. **The page-store write authority.** The assistant's tools save other pages. A page cannot, and a
   job can only keep data. The port cannot happen without an answer. Does a worker get a page writer
   for another key, bound to one configured worker, or does `IPageWriter` grow a key for any caller?
   Phase 5 records the choice.
2. **The names.** `Worker` and `Job` are the working names for the kind and one run of it;
   `IPageJobs`, `IWorker`, `IWorkerContext`, `StreamAsync`, `HttpFetchStream` and
   `PageStreamRefusedException` are the surfaces. The implementation phase settles them beside the
   existing vocabulary in [the glossary](../glossary.md), and the glossary says why a worker is not a
   `System.Threading.Thread`.
3. **How long a viewer may replay.** The ring's `RetentionMinutes` is a policy value; the first
   customer should say what "long enough to reconnect" means.
4. **Whether an approval wait holds a job slot.** `ReceiveAsync` pauses the silence clock but not the
   ceiling, and the job keeps its slot while it waits. The channel allows the alternative — end the
   job at the question and start a new one with the answer — and the assistant's port chooses.

<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Pages can be apps

| | |
| --- | --- |
| Status | Proposed 2026-11-14 — awaiting the owner's answer; nothing is built |
| Size | Large — three capabilities and a readiness pass, each phase merging on its own |
| Area | Sdk, Web (pages and work), deploy, docs |
| Location | `src/uk.osric.asp.Sdk/`, `src/uk.osric.asp.Runtime/`, `src/uk.osric.asp.Web/Pages/`, `src/uk.osric.asp.Web/Work/`, `deploy/Caddyfile`, `src/uk.osric.asp.Web/Pages/Seeds/help` |
| Spike | None. The assistant proves every shape this plan generalises: a long response, an upstream stream, and work that outlives a request |
| Decisions | Named in prose; numbers are taken from `trunk` when the work merges (`DEC-09`, `DEC-232`, `DEC-281`) |
| Backlog | [IDEA-025](../jobs/IDEA-025.md) (the assistant as a page, re-scoped by this plan), [IDEA-033](../jobs/IDEA-033.md) (a page has no per-account store), [the streaming plan](page-streaming.md) (ARCH-13, where a page learned to stream but not to keep the stream) |

## The short version

The assistant stays in the shell because a page cannot hold a live connection, read an upstream stream,
or work after its request — and, for its tools, because a page may not save another page. The first
three are connection-shaped and generic; this plan builds them. The fourth is authority and is a
question for the port.

Each of the three is a generic capability, not a feature of the assistant:

> **A committed stream is bounded by silence, not duration, and holds a stream slot rather than a
> render slot.** A page that keeps producing may hold the connection for minutes; a page that goes
> quiet is cut.

> **A page can read an outbound response as it arrives.** A page relays an upstream stream instead of
> waiting for a whole body it will never fit in its buffer.

> **A page can start work that outlives the request.** The unit of work is a **job library**: a
> stored library that declares one type with a `RunAsync` entry point. A page starts it by key, and
> the job works for that page — its storage and egress are the page's — while the page follows its
> events through a bounded journal and may cancel it. The job has a context and capabilities, and no
> response to write.

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
| Starts a run from a message, bounds the queue and the concurrency | Work after the response has gone | **Background jobs** (this plan): a library entry point, invoked by the shell |
| Streams deltas, replays them after a reconnect, stops a run | A sequenced journal with live subscribers and a bounded replay | **Background jobs** — the journal belongs to the job, not to a standalone event bus |
| Asks for approval, asks to continue | A decision written by one request and read by another | Page code over storage: the job ends at the question and a new job resumes with the answer |
| Streams from the provider | An outbound body read as it arrives | **Streaming outbound HTTP** (this plan) |
| Renders deltas as they are written | A response written while the page runs | **The stream's own lifetime** (this plan) |
| Reads and edits pages through eighteen tools | Read every page; write any page the shell does not route | `IPageCatalog` already reads everything, but `IPageWriter` writes only the page's own key. Writing *another* page is the authority question in [phase 4](#phase-4--what-a-port-still-lacks-documents-only); the tools write every page the shell does not route (DEC-253) |
| Remembers, recalls, forgets | Owner-scoped rows with search and versions | Page code over the owner's store; memory is not a capability |
| Renders a model's Markdown | A renderer that is safe on untrusted text | `IMarkdownRenderer` exists, but only the permissive profile. The restricted profile (DEC-164) is a named gap for the port |
| Reads the provider's balance | One outbound GET | `IHttpFetch` already does it |
| Shows budgets, usage and timings | Instruments | Page code can log; instruments stay the shell's |

The table is the point of the exercise. The assistant is one page plus three connection-shaped
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
service or a task framework: it is **a type in a library, with one entry point, started by a page and
owned by the visitor who started it**. A job has no document, no visitor and no response; giving it a
page's handler would mean carrying a response-shaped context it can never use.

## Goals

1. A committed stream is bounded by silence between flushes plus a ceiling, and the render deadline
   before the first flush is unchanged.
2. A live stream holds a stream slot, not a render slot, so viewers cannot starve ordinary pages.
3. A stream reports how it ended — completed, silent, at its ceiling, the client left, or a page
   fault — and a stream is not recorded as a ten-minute page render.
4. A page can read an outbound response body as lines arrive, under the same lifetime rule, with byte,
   silence and ceiling bounds and the existing credential and header rules.
5. A page can start a **job library** — a stored library declaring one `IJob` type — with a
   form-shaped input, owned by `ctx.User`, bounded by a queue, a concurrency limit, a silence clock
   re-armed by the job's own published events, and a ceiling.
6. A running job can publish bounded, sequenced events; any request owned by the same visitor can
   read them after a sequence, wait briefly for the next one, and cancel the job.
7. A job has a context and the work capabilities, and no response: it cannot write, flush, set a
   status, use a layout or a session, or read an antiforgery field. It keeps its durable state in the
   starting page's storage and reports progress through its own published events.
8. `/help`, the page contract, configuration, [pages](../pages.md), [architecture](../architecture.md)
   and [the jobs](../jobs/README.md) say what a page author may rely on, in the same change.
9. The assistant is never named in `Sdk`, and no new type exists only for it.

## Non-goals

- **Porting the assistant in this plan.** The plan ends with what a port still lacks. The port is its
  own step, with its own plan, after the authority question in phase 4 is answered.
- **A per-account store.** [IDEA-033](../jobs/IDEA-033.md) is the owner's rows, with SQL, quotas, a
  schema lifecycle and a privacy sentence. Nothing here stores data for an account.
- **A general page-store writer.** A page still writes only its own key and a job library writes no
  page at all. The assistant's tools write every page the shell does not route; granting that to page
  or job code is a decision about authority, not a connection primitive, and phase 4 records it rather
  than smuggling it in.
- **A durable job queue.** Jobs are in memory. A restart loses a live job; the starting page's storage
  is what survives, and a page that finds its state says "running" with no job knows it was
  interrupted. A durable scheduler is a different job with a different cost.
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
- **A restricted Markdown profile.** It is the port's gap, named in phase 4, not a connection
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

**D8 — a job is a library with an entry point, not a page render.** A library declares one public type
with a parameterless constructor implementing `IJob`, and its `RunAsync(IJobContext)`. A page starts
it with `IPageJobs.StartAsync(libraryKey, input)`; the job runs as the signed-in owner and **for the
starting page**, because the page is the principal and the library is only the code. A job has no
document and no request, so it has no page handler and no response context. A missing key, a page, an
asset, a reserved key, an anonymous render and a full queue are refused as values. A library with no
`IJob`, two `IJob`s or one that does not compile is a **failed job** with its reason on the journal,
because finding and checking the entry point is the compiler's work and must not run inside the
starting request. Rejected: a future render of the page that started it (a page's handler writes to a
response that does not exist; its layout, session and antiforgery members would be dead weight, and
the view and the work would share one source for no reason); a `Func<IContext, Task>` (it closes over a
dead request and hands the shell an opaque lifetime); an assistant run type (the specialist capability
this plan exists to avoid).

**D9 — two surfaces, and the owner is structural.** `IPageJobs` — `StartAsync`, `GetAsync`,
`ListAsync`, `ReadEventsAsync`, `CancelAsync` — is offered to every page. The running job is handed an
`IJobContext` — `Id`, `Path` (the starting page), `Library` (the job's own key), `User`, `Input`,
`Aborted`, `Capabilities`, three log members and `Publish(type, dataJson)`. Neither takes an owner:
`IPageJobs` is bound to `ctx.User`, an anonymous render is refused, and every read and cancel filters
on the job's owner before it answers. Rejected: an owner argument (`DEC-190`); one interface for both
sides (a viewer would meet `Publish`, and a job would meet `StartAsync` it has no reason to call).

**D10 — the journal is in memory, bounded and replayable; the durable record is the page's.** A job's
events are `(sequence, type, json)` in a ring bounded by `Pages:Jobs:MaxEvents`,
`MaxEventBytes` and `RetentionMinutes`, with live subscribers and a replay window. A viewer that asks
for a sequence the ring no longer holds gets a reset event. There is no `PageJobs` table. Rejected: a
durable journal (events are for viewers, not history, and the starting page's storage is the record);
an unbounded ring (a job that publishes in a loop would grow the process); a table for job status (a
second source of truth the page's storage already answers).

**D11 — a job has its own lifetime and its own slot.** A job takes a job slot
(`Pages:Jobs:MaxConcurrent`), not a render slot, because it is not answering a visitor. Its deadline
is `Pages:Jobs:SilenceSeconds` re-armed by every `Publish`, plus `Pages:Jobs:MaxDurationSeconds` from
the start. A job that stops publishing is cut, so a wedged job cannot burn the ceiling. Rejected: the
render deadline (a job legitimately outlives a request); an absolute deadline only (a page that hits a
synchronous loop would run for the ceiling); sharing the render slots (a background job would pay for a
visitor's slot).

**D12 — a viewer waits, it does not sleep.** `ReadEventsAsync(jobId, after, wait)` returns when there
are events, when the job ends, or when the wait elapses — whichever is first — with `Events`, `Next`,
`Reset`, `State` and `Failure`. The wait is capped by `Pages:Jobs:MaxEventWaitSeconds` and by the
render's stream budget. A viewer streams the batches as SSE and writes a comment when one is empty.
Rejected: allowing `Task.Delay` (a general fairness hole, and the policy denies it); a poll per reload
(a whole document per event); a second pub/sub capability (the wait belongs to the journal).

**D13 — a job works for the page, so its capabilities are the page's.** `IJobContext` has no `Write`,
`FlushAsync`, `SetStatus`, `SetContentType`, `SetHeader`, `Redirect`, `AntiforgeryField` or
`IPageLayout`; its `Capabilities` offer `IPageStorage`, `IHttpFetch`, `IPageCatalog`,
`IMarkdownRenderer` and `IPageJobs`, with storage, credentials and the catalog scope bound to the
**starting page's** key. A job and its view therefore share storage: the job writes the transcript and
the view reads it. `IPageWriter` is not offered: a job keeps data, not code, and the authority to save
a page is the port's question. Rejected: an `IContext` (it makes a job look like a visitor's request
and hands it a response nobody receives); a full page capability set (a job that saved its library
would be writing the code it is running); storage bound to the library (the code would own the data,
and a view could not read what its job wrote); no capabilities at all (a job could reach nothing).

**D14 — a job library is compiled when it starts, not when it is saved.** The library's source and its
`using` closure are policy-checked and emitted as a job assembly by the compiler, then loaded for that
job and released when it ends. A library that pages compile into their own assemblies is unaffected:
there is still no shared runtime assembly (`DEC-289`), because each dependent and each job has its own
copy. Rejected: storing a job image on the revision (a library stores source, verdict and diagnostics,
and a job image would be a second kind of library storage for a rare operation); caching a loaded
assembly across jobs (statics would become a channel between jobs, and one job's code would outlive
its own start).

**D15 — the controls stay where they are.** `Pages:Render` keeps the pre-commit budget; a new
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
| `Pages:Jobs` | `QueueCapacity` 8, `MaxConcurrent` 2, `SilenceSeconds` 120, `MaxDurationSeconds` 1800, `MaxEvents` 512, `MaxEventBytes` 256 KiB, `RetentionMinutes` 30, `MaxEventWaitSeconds` 30, `MaxInputBytes` 16 KiB |

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
- `deploy/Caddyfile`: a response whose content type starts with `text/event-stream` is excluded from
  `encode`, so a page's SSE is not buffered by the proxy, and `deploy/README.md` says why. The
  assistant's path exclusion stays until its events route becomes the page's; a progressive-HTML
  stream through the proxy is a deployment choice the README names, because `text/html` is still
  compressed.
- `/help`, [pages](../pages.md#streaming), [pages](../pages.md#page-execution-budget),
  [configuration](../architecture.md#configuration) and the plan's own decision notes.

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

### Phase 3 — work that outlives the request

- `Sdk`: `IPageJobs`, `IJob`, `IJobContext` and their values (`PageJobStart`, `PageJobInfo`,
  `PageJobState`, `PageJobEvent`, `PageJobEvents`). `IJob` is the one method a job library declares;
  `IJobContext` is what that method is handed.
- `Runtime`: compile a job library — its source and its `using` closure, policy-checked, emitted as an
  assembly under the compile slot — and load it so the entry point can be found. The entry point must
  be exactly one public `IJob` with a parameterless constructor in the named library; the closure is
  compiled in and is not a source of entry points. The worker compiles, so a slow compile is the job's
  time and not the starting request's. `IPageCompiler` and the `Runtime` public surface grow the
  compile and load of a job, recorded as `page.compile.duration` with reason `job`.
- `Web/Work`: the generic queue (the existing `BoundedWorkQueue`), a job registry, a bounded journal
  with subscribers and a wait, and a `BackgroundService` worker. The assistant's `RunStream` shape is
  the starting point, not a shared type: a job is not a run. The worker holds the job's loaded library
  for the job's lifetime and releases it at the end.
- The job's context: the owner as `PageUser`, the starting page as `Path`, the library as `Library`,
  the input as `IReadOnlyDictionary<string, string>`, the work capability set bound to the starting
  page's key, and the job's own deadline.
- `Pages:Jobs` with `QueueCapacity`, `MaxConcurrent`, `SilenceSeconds`, `MaxDurationSeconds`,
  `MaxEvents`, `MaxEventBytes`, `RetentionMinutes`, `MaxEventWaitSeconds` and `MaxInputBytes`.
- Tests: a library with one `IJob` starts and writes the starting page's storage, which the view
  reads; a page starts it by key; a viewer reads the journal and waits for the next event; cancel ends
  the job at its next await; a missing key, a page key, a reserved key, an anonymous render and a full
  queue are refused as values; a library with no `IJob`, two `IJob`s and a compile failure are failed
  jobs with reasons; a job that goes silent is cut; a job's context has no response members; the
  library's closure compiles and the policy is applied to it; another account cannot read or cancel
  it; the render slot count is unchanged while a job runs; the library is never served.
- `/help`, [pages](../pages.md#page-api-policy), [architecture](../architecture.md#where-a-view-lives)
  if the audit changes, [logging](../logging.md) for the job's lines.

**Done when** a stored library can be started as a job by a page, a second request can stream its
events, stop it, and read what it wrote, with no assistant type in `Sdk`.

### Phase 4 — what a port still lacks (documents only)

- Re-scope [IDEA-025](../jobs/IDEA-025.md): the conversation view, the run as a job, the memory view,
  the approval as a stored decision and a new job, and the transcript on the owner's store.
- Name the remaining gaps honestly: [IDEA-033](../jobs/IDEA-033.md) for the transcript and memory, the
  page-store write authority for the tools, and the restricted Markdown profile.
- The authority question, written as a decision for the owner: does a job library get a page writer
  for the keys the assistant's tools may edit, so one configured job library (or one page) has the
  authority and nothing else does? A page writer is the largest authority in the port and is not a
  connection primitive; phase 4 records the choice, it does not make it.
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
- `Web.IntegrationTests/PageJobTests` — a library with one `IJob` starts, observes, waits, cancels,
  refuses, isolates, and has no response at all.
- `Runtime.Tests/PageCompilerTests` — a job library compiles with its closure, and its policy
  violations are its own lines.
- `Core.Tests`/`Runtime.Tests` — only if a pure helper (a journal sequence, an SSE line formatter in a
  page library) appears; the connection work is I/O and belongs in `Web`.
- `Web.Tests/PageApiPolicyTests` — a page that names the new Sdk types compiles; `Task.Delay`,
  `ValueTask` and `IAsyncEnumerable` stay denied until a decision says otherwise.

The browser suite gains nothing: every behaviour here is provable through the real host with an HTTP
client.

## Risks

| Risk | Mitigation |
| --- | --- |
| **Slot accounting double-releases or leaks.** The render slot is released at the commit and the stream slot at the end; an abandoned page keeps running. | One owner (`RenderLifetime`) with an idempotent release, and tests that assert the semaphore's count after each ending. |
| **A page uses the long stream to burn CPU.** | The stream slot, the silence rule and the ceiling; the residual is the same in-process trust SEC-03 records. |
| **A job becomes a scheduler or a task framework.** | The non-goals name scheduling, retries, durability and dependencies; a new need gets a job file, not a feature. |
| **A job survives the request but not the deploy.** | Jobs are in memory and the plan says so; the starting page's storage is the record, and a page detects an interrupted job by asking for it. |
| **The outbound stream is a second egress with different rules.** | One implementation of the credential, header, redirect and origin rules; the buffered and streaming paths call the same helpers. |
| **The page API policy makes the stream awkward.** | The line-at-a-time shape is chosen for `Task`, not `ValueTask`; the policy test pins it. |
| **The render histogram loses its meaning.** | The commit is the render's end for a streamed page, and `page.stream.duration` carries the rest. |
| **A viewer holds a stream open forever.** | The stream ceiling, the journal's retention, and `ReadEventsAsync`'s bounded wait. |
| **The port is assumed to be delivered.** | This plan says it is not, and phase 4 records the authority question rather than answering it by code. |

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
- **A durable job queue or a `PageJobs` table.** The starting page's storage is the record; a table would be a
  second source of truth and a migration for nothing at this size.
- **A job as a page render.** A page's handler writes a response; a job has none, and its context
  would carry layout, session and antiforgery members it must not use. A library entry point says
  what a job is without pretending it is a visit.
- **A general page writer now.** It is the largest authority in the port and is not a connection
  primitive. Phase 4 writes the question down.
- **`Task.Delay` and `IAsyncEnumerable` in pages.** The wait belongs to the journal; the stream is a
  line at a time. Both denials keep the render's bound meaningful.
- **A seeded SSE helper or a demo app.** Page code, not shell; the first app seeds a library
  (`DEC-08`).
- **New assistant metrics.** The assistant's panels answer the assistant's questions; a job's
  lifetime is a page instrument (`page.stream.*`) or a log line, not an assistant one.

## Components

| Component | Responsibility | Where |
| --- | --- | --- |
| `RenderLifetime` | The two clocks, the slot, the refusal and the end reason for one render | `src/uk.osric.asp.Web/Pages/` |
| `PageBudget` and the three options records | The stream, job and fetch bounds | `src/uk.osric.asp.Web/Pages/` |
| `IHttpFetch.StreamAsync` and `HttpFetchStream` | The page's outbound body, a line at a time | `src/uk.osric.asp.Sdk/` |
| `OutboundFetcher` and `PageFetcher` | One implementation of the outbound rules for both paths | `src/uk.osric.asp.Web/Pages/` |
| `IPageJobs` | Start, observe, wait and cancel, from a page | `src/uk.osric.asp.Sdk/` |
| `IJob` and `IJobContext` | The entry point a job library declares, and the work context it is given | `src/uk.osric.asp.Sdk/` |
| `Runtime` job compile | A library and its closure, policy-checked and emitted for one job | `src/uk.osric.asp.Runtime/` |
| `Web/Work` | The queue, the registry, the journal and the worker | `src/uk.osric.asp.Web/Work/` |
| The job host | Loads the job's library, finds the entry point, runs it under a job slot and releases it | `src/uk.osric.asp.Web/Work/` |
| `/help` | The author manual: the lifetime, the stream, the job and its capabilities | `src/uk.osric.asp.Web/Pages/Seeds/help` |

## Documentation and decisions

- `docs/pages.md` — the execution budget, streaming, the API policy, a job library under the library
  kind, and a new section each for the outbound stream and jobs.
- `docs/architecture.md` — configuration's new sections, and the audit if a route changes (it should
  not: this plan adds capabilities, not views).
- `docs/metrics.md` — `page.stream.duration`, the `job` compile reason, and the correction to the
  render histogram's top-bucket sentence.
- `docs/logging.md` — a job's start, end and reason; no prompt, transcript or memory in a log line.
- `docs/testing.md` — the suites and what belongs in the real host.
- `src/uk.osric.asp.Web/Pages/Seeds/help` — the author manual, updated with each phase (DEC-173).
- `docs/jobs/IDEA-025.md` and `docs/jobs/README.md` — the re-scope and the programme.
- Decisions proposed, numbered at merge: the split budget, the stream slot and its refusal, the stream
  instrument, the line-shaped outbound stream, the job as a library entry point, the three job
  surfaces (`IPageJobs`, `IJob`, `IJobContext`), the job compile, the in-memory journal, the job's own
  lifetime and slot, the bounded wait, the work-only capability set, and the configuration sections.

## Delivered when

- A stored page can hold a live response for minutes, bounded by silence and a ceiling, without holding
  a render slot.
- A stored page can relay an outbound stream, line by line, under the same rules as a fetch.
- A stored page can start a job library, another request can follow and cancel it, and the job's
  durable state is the starting page's storage.
- `/help` teaches all three, and the page contract's XML documentation matches.
- `make check` passes after every phase, and each phase has merged on the owner's answer.
- IDEA-025 says what a port still lacks, and the authority question is written down for the owner.

## Open questions

1. **The page-store write authority.** The assistant's tools save other pages. A page cannot, and a
   job can only keep data. The port cannot happen without an answer. Does a job library get a page
   writer for another key, bound to one configured job library, or does `IPageWriter` grow a key for
   any caller? Phase 4 records the choice.
2. **The names.** `IPageJobs`, `IJob`, `IJobContext`, `StreamAsync`, `HttpFetchStream` and
   `PageStreamRefusedException` are the working names; the implementation phase settles them beside
   the existing vocabulary in [the glossary](../glossary.md).
3. **How long a viewer may replay.** The ring's `RetentionMinutes` is a policy value; the first
   customer should say what "long enough to reconnect" means.

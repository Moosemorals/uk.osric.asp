<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The assistant's conversation is a stream and a set of host actions, not a page

| | |
| --- | --- |
| Difficulty | protective-dragon |
| Utility | medium |
| Status | blocked |
| Blocked by | `no-account-store` |
| Plan | — |
| Area | Sdk, Web, assistant |
| Location | `src/uk.osric.asp.Web/Assistant/AssistantEndpoint.cs`, `src/uk.osric.asp.Web/Assistant/RunQueue.cs`, `src/uk.osric.asp.Sdk/IContext.cs` |

> **Re-scoped 2026-11-14** by [the page-apps plan](../plans/archive/page-apps.md). The sketch below first
> proposed an `IAssistantConversations` capability; that is exactly the specialist capability the site
> must not build (`assistant-memory-belongs-to-the-assistant`). What a page actually lacks is three connection-shaped things — a stream
> that outlives the render budget, an outbound body read as it arrives, and work that outlives the
> request — and the plan builds those generically first. A worker is a fourth kind: a program with one
> entry point, compiled like a page and never served like a library. A job is one run of it, not a
> page render: it has no document to write. The sketch is kept as the record of what the port still
> has to answer, not as the shape of the work.
>
> **Built 2026-11-15.** The three connection primitives and the two kinds are in: a page can hold a
> live response, relay an outbound line stream, and start, message, follow and cancel a worker job.
> What is left is a port with its own plan and three gaps nothing in the page contract fills — see
> [what the port still lacks](#what-the-port-still-lacks). None of them is a connection primitive, and
> the largest is a question of authority rather than of capability.

## Idea

`/assistant` is the last view-shaped thing in the shell. The conversation list is a list and a form;
the conversation page is a transcript, a textarea and a button; the approval card is a form. All of
that is page-shaped. Two things are not:

1. **A live run is a stream.** The page subscribes to `GET /assistant/{id}/events` and renders deltas
   as they arrive, so a long answer appears while it is being written. A page can now stream its own
   response ([the streaming plan](../plans/archive/page-streaming.md)), so the response shape is no longer the
   blocker; the run's journal, its live subscribers and its lifetime are.
2. **Starting, stopping and approving are host actions.** A message queues a run (`RunQueue`), a stop
   signals the registry (`RunRegistry`), and an approval resolves a waiter in
   `OwnerInterruptCoordinator` — three in-memory singletons a page cannot reach, and three state changes
   that are not memory's simple row write.

`pages-page-first` puts `/assistant` in the third column: not
movable **yet**. This job is what would move it.

## Sketch

1. **The capability first, the stream second.** ~~A page needs
   `ctx.Capabilities.GetRequired<IAssistantConversations>()`, bound to `ctx.User` as `IPageWrites` is
   (`pages-a-page-may-save-itself`): list conversations, read a transcript, start a run with a message, stop one, and
   resolve a pending approval.~~ **Withdrawn.** A conversation capability encodes one app's shape.
   The generic pieces are the stream's lifetime, the outbound stream and a background job; the page
   owns the conversations, transcript and approval state over storage, and the run is a job. The
   owner still comes from the request and an anonymous render is still refused, but by the page's
   own guard and the job capability's structural owner filter, not by a conversation type.
2. **The memory view is one of its routes.** `/assistant/memory` is a list and a form already
   (`assistant-memory-page`), and it becomes a route of this page rather than a capability of its own: the
   same page answers for memory as it does for conversations, over the starting page's storage and
   the owner's rows, the pin stays the owner's alone (`assistant-memory-page`), a stale version is still refused with
   the stored content (`assistant-memory-page`), a refusal still keeps the text (`assistant-memory-page`) and a forget still asks
   first (`assistant-memory-page`). `assistant-memory-belongs-to-the-assistant`, which proposed `IMemoryCatalog` and `IMemoryWriter` in `Sdk`, is
   withdrawn (`assistant-memory-belongs-to-the-assistant`). Memory's store, its FTS5 recall, its versions, pins and provenance are the
   assistant's, written by its tools inside a run, and a page capability would either duplicate them
   or take ownership of the schema the tools depend on.
3. **A stream is not the only way to watch a run.** A page can already answer per request, and
   `/status` proves how far that goes without a script: a run view that reloads on its own — a
   `<meta http-equiv="refresh" content="2">`, or a `?run={id}` link the browser follows — would show
   the transcript growing without a stream. It is worse than a stream (a whole document every two
   seconds, and the textarea's draft would need care), and it is the fallback a page keeps when its
   script is not running, as the same site's "no JavaScript where markup will do" rule argues
   (`metrics-prometheus-only`). The long stream in [the page-apps plan](../plans/archive/page-apps.md) makes the live version
   possible; this job no longer has to choose.
4. **The chat input and the seed.** The input posts and the page reloads on the run, so
   `/assistant.js` stops being necessary; the seeded page could carry a tiny module later, for
   progressive rendering only, and that is the one place a page is allowed `ctx.Response.AllowScripts()`.
5. **Approvals.** The approval card is a form posting a decision; it is the same shape as the page's
   own saves, so once the job exists the card is markup. The decision is either a message to the
   running job — which pauses its silence clock while it waits, up to the job's ceiling — or a stored
   row that the next job reads. The page chooses; the assistant's port records which.
6. **What stays shell whatever happens.** `/metrics` and `/health` because they are the runtime, and
   `/status` because it is bootstrap (`pages-page-first`). The run loop is a worker under
   [the page-apps plan](../plans/archive/page-apps.md), so it is page code too; the question is only which
   route serves the view.

## What the port still lacks

Every item below is a fact about the page contract as it stands, not a preference. The first two
record what the page-apps plan closed, so a reader can see the port's shape without reading the plan;
the last three are what is still missing, and the fourth is the one that blocks the port.

### 1. A view is not the problem any more

The conversation list is a list and a form; the conversation page is a transcript, a textarea and a
button; the approval card is a form. A page can already write all of it, and the two things it could
not are now capabilities: `ctx.Response.FlushAsync()` holds a response open past the render budget
(`Pages:Stream` bounds it), and `IPageJobs` starts a worker, sends it a message, reads its events by
sequence and cancels it. A run's progress is published events, which the view streams; a visitor who
reloads finds the job again with `ListAsync`.

### 2. A run is a job of a worker, and the worker is page code

`RunWorker`, `RunProcessor`, `RunQueue` and `RunRegistry` are one worker: a stored source whose only
blocks are declarations, declaring one public type implementing `IWorker`. The run's provider call is
`IHttpFetch` — `StreamAsync` reads the response as decoded text lines, which is exactly what an SSE
provider sends — and its credential is an operator-configured credential bound to the worker's key,
so the provider token stops being an `Assistant:ApiKey` the shell holds and becomes a
`Pages:Fetch:Credentials` entry the worker names. A job has no document, no layout and no session, so
`IWorkerContext` is deliberately not an `IContext`; it has `ReceiveAsync` and `Publish`, which is the
message-and-event pair the run loop already speaks.

### 3. The transcript and memory need a store that belongs to an account

This is [`no-account-store`](no-account-store.md) and it blocks both the conversation view and the memory view. A page's
`IPageStorage` is shared by **every** visitor and `IPageSession` goes when the visitor signs out, so
neither can hold one account's conversation. Memory is the assistant's own rows with FTS5 recall,
versions, pins and provenance (`assistant-memory-belongs-to-the-assistant`); a view of it needs the same account-scoped store, and a
conversation needs it for its transcript. Until it exists, a ported page could show a transcript only
to everyone; that is not the feature.

The approval does **not** need it. A decision is a message to the running job, and a wait in
`ReceiveAsync` pauses the job's silence clock while the ceiling still bounds it — which is the shape
an approval needs, and it is available today. A decision that must survive a restart becomes a row in
the account store, and that is `no-account-store` again.

### 4. A worker cannot save a page, and the tools do

`IPageWrites` is offered to every page and names a key on every named call, so a page can already
save, delete and restore any page the shell does not route. What the assistant's page tools do beyond
that — the diff, the search, the batch — is the rest of this job.

**Split off 2026-11-18, and landed 2026-11-20.** The writer a worker needs was
[`job-borrows-a-credential`](../backlog/closed.md): it acts with the credential its starter presented,
so logging out or changing a password ends it, and it waits on the page transaction, which has landed
first (`pages-writes-are-transactions`). That is in (`pages-a-jobs-credential-is-borrowed`), so a worker
is now offered the store's writer and this job keeps the port and nothing more.

### 5. The restricted Markdown profile is shell-only

`IMarkdownRenderer.ToHtml` is the **author** profile: CommonMark plus pipe tables and auto-links, with
raw HTML passing through, because it renders the owner's own source. The assistant renders a model's
reply with `MarkdownRenderer.ToRestrictedHtml`, a static on the concrete renderer that no capability
exposes. A ported page that renders a reply through `IMarkdownRenderer` would pass a model's raw HTML
into the document. That is the one piece of the port that is a renderer decision rather than a
connection one, and it needs a decision of its own: a second capability, a profile argument on the
existing one, or a seeded library the page opts into.

## The authority question, answered

**Decided 2026-11-15.** The owner's answer is that any page may write any key the shell does not
route, and that the authority is the signed-in visitor's rather than the page's
(`pages-any-page-may-write-the-store`). So the port needs no worker writer, and the assistant page
would make its edits through `IPageWrites` the way its tools make them today — with a transaction
(`pages-writes-are-transactions`) making a multi-file edit one request rather than a sequence.

The shapes that were weighed, with what each cost:

| Shape | What it means | What it cost |
| --- | --- | --- |
| **A worker writer, bound to one worker** | The capability is granted to the worker key named in configuration, and it may write the keys the assistant's tools may write. One configured worker has the authority and nothing else does. | Rejected: the authority becomes a configuration value, and it is a second writer shape beside the page's own. |
| **The write capability is offered to every page** (chosen) | Any page may save any key the shell does not route. | Taken: the assistant page is no longer special and a mistake in any page is a site-wide rewrite. The signed-in visitor, the reserved keys and `/admin/seeds` are what is left of the guard. |
| **Keep the tools in the shell** | The port stops at the view and the run: the assistant keeps its endpoint for the edits, or the tools stay shell-only and a page calls a route. | Rejected once the first question was answered: the page contract would keep a hole that is now visible. |

## What would make it unnecessary

If the transcript were readable only as a document — no live updates at all — the conversation page
would be an ordinary page today. The live stream is the feature, so this job is about keeping it,
not about dropping it.

**Origin:** [the page-first plan](../plans/archive/page-first.md) and `pages-page-first`, which named this route as one of
the shell's three exceptions with a job behind it. The streaming plan closed ARCH-13; what is left is
this job's.

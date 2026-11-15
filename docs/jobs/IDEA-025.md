<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-025 — The assistant's conversation is a stream and a set of host actions, not a page

| | |
| --- | --- |
| Size | Large |
| Area | Sdk, Web, assistant |
| Location | `src/uk.osric.asp.Web/Assistant/AssistantEndpoint.cs`, `src/uk.osric.asp.Web/Assistant/RunQueue.cs`, `src/uk.osric.asp.Sdk/IContext.cs` |

> **Re-scoped 2026-11-14** by [the page-apps plan](../plans/page-apps.md). The sketch below first
> proposed an `IAssistantConversations` capability; that is exactly the specialist capability the site
> must not build (DEC-291). What a page actually lacks is three connection-shaped things — a stream
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
   response ([the streaming plan](../plans/page-streaming.md)), so the response shape is no longer the
   blocker; the run's journal, its live subscribers and its lifetime are.
2. **Starting, stopping and approving are host actions.** A message queues a run (`RunQueue`), a stop
   signals the registry (`RunRegistry`), and an approval resolves a waiter in
   `ToolApprovalCoordinator` — three in-memory singletons a page cannot reach, and three state changes
   that are not memory's simple row write.

[DEC-210](../decisions.md#architecture-and-code-placement) puts `/assistant` in the third column: not
movable **yet**. This job is what would move it.

## Sketch

1. **The capability first, the stream second.** ~~A page needs
   `ctx.Capabilities.GetRequired<IAssistantConversations>()`, bound to `ctx.User` as `IPageWriter` is
   (DEC-190): list conversations, read a transcript, start a run with a message, stop one, and
   resolve a pending approval.~~ **Withdrawn.** A conversation capability encodes one app's shape.
   The generic pieces are the stream's lifetime, the outbound stream and a background job; the page
   owns the conversations, transcript and approval state over storage, and the run is a job. The
   owner still comes from the request and an anonymous render is still refused, but by the page's
   own guard and the job capability's structural owner filter, not by a conversation type.
2. **The memory view is one of its routes.** `/assistant/memory` is a list and a form already
   (DEC-205–DEC-209), and it becomes a route of this page rather than a capability of its own: the
   same page answers for memory as it does for conversations, over the starting page's storage and
   the owner's rows, the pin stays the owner's alone (DEC-206), a stale version is still refused with
   the stored content (DEC-207), a refusal still keeps the text (DEC-208) and a forget still asks
   first (DEC-209). DEC-211, which proposed `IMemoryCatalog` and `IMemoryWriter` in `Sdk`, is
   withdrawn (DEC-291). Memory's store, its FTS5 recall, its versions, pins and provenance are the
   assistant's, written by its tools inside a run, and a page capability would either duplicate them
   or take ownership of the schema the tools depend on.
3. **A stream is not the only way to watch a run.** A page can already answer per request, and
   `/status` proves how far that goes without a script: a run view that reloads on its own — a
   `<meta http-equiv="refresh" content="2">`, or a `?run={id}` link the browser follows — would show
   the transcript growing without a stream. It is worse than a stream (a whole document every two
   seconds, and the textarea's draft would need care), and it is the fallback a page keeps when its
   script is not running, as the same site's "no JavaScript where markup will do" rule argues
   (DEC-81). The long stream in [the page-apps plan](../plans/page-apps.md) makes the live version
   possible; this job no longer has to choose.
4. **The chat input and the seed.** The input posts and the page reloads on the run, so
   `/assistant.js` stops being necessary; the seeded page could carry a tiny module later, for
   progressive rendering only, and that is the one place a page is allowed `ctx.AllowScripts()`.
5. **Approvals.** The approval card is a form posting a decision; it is the same shape as the page's
   own saves, so once the job exists the card is markup. The decision is either a message to the
   running job — which pauses its silence clock while it waits, up to the job's ceiling — or a stored
   row that the next job reads. The page chooses; the assistant's port records which.
6. **What stays shell whatever happens.** `/metrics` and `/heath` because they are the runtime, and
   `/status` because it is bootstrap (DEC-210). The run loop is a worker under
   [the page-apps plan](../plans/page-apps.md), so it is page code too; the question is only which
   route serves the view.

## What the port still lacks

Every item below is a fact about the page contract as it stands, not a preference. The first two
record what the page-apps plan closed, so a reader can see the port's shape without reading the plan;
the last three are what is still missing, and the fourth is the one that blocks the port.

### 1. A view is not the problem any more

The conversation list is a list and a form; the conversation page is a transcript, a textarea and a
button; the approval card is a form. A page can already write all of it, and the two things it could
not are now capabilities: `ctx.FlushAsync()` holds a response open past the render budget
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

This is [IDEA-033](IDEA-033.md) and it blocks both the conversation view and the memory view. A page's
`IPageStorage` is shared by **every** visitor and `IPageSession` goes when the visitor signs out, so
neither can hold one account's conversation. Memory is the assistant's own rows with FTS5 recall,
versions, pins and provenance (DEC-291); a view of it needs the same account-scoped store, and a
conversation needs it for its transcript. Until it exists, a ported page could show a transcript only
to everyone; that is not the feature.

The approval does **not** need it. A decision is a message to the running job, and a wait in
`ReceiveAsync` pauses the job's silence clock while the ceiling still bounds it — which is the shape
an approval needs, and it is available today. A decision that must survive a restart becomes a row in
the account store, and that is IDEA-033 again.

### 4. A worker cannot save a page, and the tools do

`IPageWriter` saves the calling page and nothing else, and `IPageEditor` is offered to one key. A
worker is offered neither (D14), so the assistant's page tools — which save, delete, restore and
import pages the shell does not route — have no equivalent in page or job code. This is the authority
question below.

### 5. The restricted Markdown profile is shell-only

`IMarkdownRenderer.ToHtml` is the **author** profile: CommonMark plus pipe tables and auto-links, with
raw HTML passing through, because it renders the owner's own source. The assistant renders a model's
reply with `MarkdownRenderer.ToRestrictedHtml`, a static on the concrete renderer that no capability
exposes. A ported page that renders a reply through `IMarkdownRenderer` would pass a model's raw HTML
into the document. That is the one piece of the port that is a renderer decision rather than a
connection one, and it needs a decision of its own: a second capability, a profile argument on the
existing one, or a seeded library the page opts into.

## The authority question, for the owner

**Not decided here.** This is the choice a port cannot be planned around until the owner makes it, and
it is the largest authority in the port: a page writer at another key can rewrite any page the site
serves, including the pages that describe the site.

Today's authority is structural, not checked: `IPageWriter` is bound to `ctx.Path` and `IPageEditor` is
registered for one key, so a page cannot name another page because the handle it is given is the only
one it can use. The assistant's tools reach further because they are shell code holding a scoped
`IPageStore`; DEC-198 removed the last allow-list and let them write every page the shell does not
route.

Three shapes, with what each costs:

| Shape | What it means | What it costs |
| --- | --- | --- |
| **A worker writer, bound to one worker** | The capability is granted to the worker key named in configuration, and it may write the keys the assistant's tools may write. One configured worker has the authority and nothing else does. | The authority becomes a configuration value, so an operator can grant it — which is also its risk. The capability is a second writer shape beside `IPageWriter`, and its key set has to be decided (`Pages:Reserved` is the list it would subtract from). |
| **`IPageWriter` grows a key** | Any page or worker may save any key the shell does not route. | Every page has the authority the assistant has, so the assistant page is no longer special and a mistake in any page is a site-wide rewrite. This is the shape the structural rule exists to prevent. |
| **Keep the tools in the shell** | The port stops at the view and the run: the assistant keeps its endpoint for the edits, or the tools stay shell-only and a page calls a route. | The port is partial, and the page contract keeps a hole that is now visible — the run is page code and the edits are not. |

The recommendation, if the owner wants one: **the first**. It is the only shape that keeps the
authority structural — a handle that can only be obtained for the configured key — and it mirrors
DEC-190's "no owner argument" rule rather than inventing a checked permission. It also means a worker
that streams a reply and a worker that edits a page are different workers, which is a fair thing to
make an operator say out loud.

Whichever shape is chosen, the decision is written down before the port is planned, because the port's
plan cannot be phased without knowing whether a job may write.

## What would make it unnecessary

If the transcript were readable only as a document — no live updates at all — the conversation page
would be an ordinary page today. The live stream is the feature, so this job is about keeping it,
not about dropping it.

**Origin:** [the page-first plan](../plans/page-first.md) and DEC-210, which named this route as one of
the shell's three exceptions with a job behind it. The streaming plan closed ARCH-13; what is left is
this job's.

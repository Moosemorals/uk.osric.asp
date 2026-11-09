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

## Idea

`/assistant` is the last view-shaped thing in the shell. The conversation list is a list and a form;
the conversation page is a transcript, a textarea and a button; the approval card is a form. All of
that is page-shaped. Two things are not:

1. **A live run is a stream.** The page subscribes to `GET /assistant/{id}/events` and renders deltas
   as they arrive, so a long answer appears while it is being written. A page's body is buffered and
   sent once, which is [ARCH-13](ARCH-13.md).
2. **Starting, stopping and approving are host actions.** A message queues a run (`RunQueue`), a stop
   signals the registry (`RunRegistry`), and an approval resolves a waiter in
   `ToolApprovalCoordinator` — three in-memory singletons a page cannot reach, and three state changes
   that are not memory's simple row write.

[DEC-210](../decisions.md#architecture-and-code-placement) puts `/assistant` in the third column: not
movable **yet**. This job is what would move it.

## Sketch

1. **The capability first, the stream second.** A page needs
   `ctx.Features.GetRequired<IAssistantConversations>()`, bound to `ctx.User` like the memory
   capability (DEC-211): list conversations, read a transcript, start a run with a message, stop one,
   and resolve a pending approval. The owner comes from the request, never from an argument, and an
   anonymous render is refused by the capability rather than by the page.
2. **A stream is not the only way to watch a run.** A page can already answer per request, and
   `/status` proves how far that goes without a script: a run view that reloads on its own — a
   `<meta http-equiv="refresh" content="2">`, or a `?run={id}` link the browser follows — would show
   the transcript growing without any of ARCH-13's work. It is worse than a stream (a whole document
   every two seconds, and the textarea's draft would need care), and it is the cheap version worth
   having before the expensive one, as the same site's "no JavaScript where markup will do" rule
   argues (DEC-81).
3. **The chat input and the seed.** The input posts and the page reloads on the run, so
   `/assistant.js` stops being necessary; the seeded page could carry a tiny module later, for
   progressive rendering only, and that is the one place a page is allowed `ctx.AllowScripts()`.
4. **Approvals.** The approval card is a form posting a decision; it is the same shape as the page's
   own saves, so once the capability exists the card is markup.
5. **What stays shell whatever happens.** `/metrics` and `/heath` because they are the runtime, and
   `/status` because it is bootstrap (DEC-210). The assistant's own run loop is host code either way;
   the question is only which route serves the view.

## What would make it unnecessary

If the transcript were readable only as a document — no live updates at all — the conversation page
would be an ordinary page today. The live stream is the feature, so this job is about keeping it,
not about dropping it.

**Origin:** [the page-first plan](../plans/page-first.md) and DEC-210, which named this route as one of
the shell's three exceptions with a job behind it. ARCH-13 is the same constraint from the contract's
side.

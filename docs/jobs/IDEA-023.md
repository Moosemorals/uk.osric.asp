<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-023 — The assistant remembers, but only the transcript shows it

| | |
| --- | --- |
| Size | Medium |
| Area | Web, assistant, storage |
| Location | `src/uk.osric.asp.Web/Assistant/`, `src/uk.osric.asp.Web/Pages/Seeds/assistant.css` |

## Idea

[The assistant-memory plan](../plans/assistant-memory.md) gives the assistant a small, owner-scoped
memory in SQLite: it remembers a preference or a fact about this site, and reads it back into the
system prompt at the start of a later run. What it does not give the owner is a way to look at it.

Today the only view of a memory is the transcript of the conversation that wrote it, somewhere
between a tool call and its result, and the only ways to change one are to ask the assistant (which
calls `update_memory`) or to ask it to forget (which calls `forget_memory`). That is a poor fit for
the kind of data it is. It is the owner's own words about themselves, it outlives the conversation
that recorded it, and a wrong or stale one quietly colours every later answer until someone
notices. The evidence in [the spike](../spikes/assistant-memory.md) is blunt about this being the
failure that erodes trust: most memories are created by the system rather than the user, and the
products that made the store visible are the ones that gave users a chance to correct it.

The plan stores a `Pinned` flag and honours it in the recall selector, but nothing sets it. Pinning
is the owner's way to say "always use this", and it belongs on the same page as the list. So this
job is the owner's interface: see every memory, correct it, pin it, forget it.

## Sketch

1. **Routes, all owner-filtered and behind the `Editor` policy.** `GET /assistant/memory` lists;
   `POST /assistant/memory/{id}` saves content, kind and pinned; `GET` then `POST
   /assistant/memory/{id}/forget` confirms, as conversation deletion already does. Antiforgery on
   every POST, `303` after a save, the same shapes the editor uses.
2. **The page is a list and a form.** Memories grouped by kind, newest change first: content, kind,
   written date, the conversation it came from (a link while that conversation exists, "from a
   deleted conversation" after), how many times it has been used and when it last was. Each row is a
   form with the version in a hidden field, so a save from a stale page is refused rather than
   overwriting an edit the assistant made meanwhile — the same `expectedVersion` rule the tool uses.
3. **A count against the cap.** `MaxMemoriesPerOwner` is the ceiling the tool refuses at. The page
   shows the current count against it, so "the assistant is full" is visible before a run fails to
   remember something.
4. **Pinning is the owner's, and only the owner's.** The toggle sets `Pinned`, which the recall
   selector already reads; nothing in the assistant may set it. That needs a sentence in the prompt
   or the tool table saying so, and a store test that a tool write cannot change it.
5. **Forget is immediate and says so.** Confirmation on its own page, with the caveat the plan
   already states: the row, its history and its index entry are gone, and the rest is the same
   best-effort erasure as a page scrub — WAL frames, backups and snapshots are the operator's.
   A "forget everything" action is not part of this job; if it is ever wanted it needs a typed
   confirmation and its own decision, like a site reset would.
6. **No JavaScript, no JSON.** Server-rendered like `/status` and the editor's own pages; a memory is
   not edited often enough to earn a script. `assistant.css` gains a few rules for the rows and the
   kind headings, following the existing assistant page.
7. **Tests.** Endpoint tests through the hand-built `HttpContext` the editor tests use: a second
   owner's id is not found, a stale version is refused, a pin survives a reload, a forget removes
   the row. One integration test with two accounts against the real host, and one browser test of the
   round trip — the assistant already has a browser suite, and this is a view an owner uses with a
   mouse.
8. **What would make it unnecessary.** If memory turned out to be small and self-correcting enough
   that the transcript were the natural place to read it, this would be dropped and the `Pinned`
   column with it. The plan's cap is 200 memories and the evidence says wrong memories are the
   failure mode, so that is unlikely.

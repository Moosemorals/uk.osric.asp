<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-052 — The assistant cannot ask the owner a question

| | |
| --- | --- |
| Size | M — [ARCH-17](../plans/assistant-owner-interrupt.md) has landed, so this is one tool, one interrupt kind, the card in two renderers, three limits, a prompt version and five suites |
| Area | Core (Assistant), Web (assistant) |
| Location | `src/uk.osric.asp.Core/Assistant/`, `src/uk.osric.asp.Web/Assistant/Tools/`, `src/uk.osric.asp.Web/Pages/Seeds/assistant.js`, `docs/assistant.md` |

## Idea

The assistant can read, write, fetch and remember, but it can only hear the owner at the start of a
task and at a yes/no gate. A run that needs one fact the page store does not hold — which of two names
the owner prefers, what a heading should say, whether a section is still wanted — has three bad
options today: guess and be corrected by a wasted edit, write the question into a reply and end the
run, or remember it for another conversation and hope. None of them is a conversation.

The missing thing is small and ordinary: a tool whose result is the owner's answer, and a run that
waits for it the way it already waits for an approval. Nothing about it is a page capability — a stored
page cannot ask its visitor a question and wait, and this is the assistant's own tool — so it belongs
to the shell's assistant.

[The survey](../spikes/ask-tool-shape.md) found no cross-vendor standard, but a strong convergence on
one tool, an array of one to four question objects, optional options of a label and a description, free
text always allowed, and hard caps. [The plan](../plans/assistant-ask-owner.md) adopts that shape; the
wait it needs is [the owner interrupt](../plans/assistant-owner-interrupt.md), which landed on
2026-11-18.

## Sketch

1. One tool, `ask_user`, carrying its schema beside its code like every other tool
   (`assistant-a-tool-is-one-type`): `questions[]` of `{question, header?, options[]{label,
   description?}, multiSelect?}`, capped at four questions and five options by configuration.
2. The wait is the owner interrupt's third kind, so there is no new status, record, coordinator or route: the run opens a `question` interrupt, the card is rendered from its request, and the answer
   is its resolution.
3. The answer is the tool's result, read from the record after the run wakes, so a disconnected browser,
   a second device or a reload changes nothing about the outcome. A question the owner leaves blank is
   carried as an empty answer; they can read the whole card before answering any of it, and there is no
   `skipped` flag to interpret.
4. The card is rendered twice from one record — pending with a form, answered with the question and the
   answer — and the live one is keyed so a replayed event replaces the server's copy.
5. The prompt gains an "Asking the owner" section and the version moves; the tool description says when
   to use it, when not to, and that it is alone in its step. Never ask for a secret: the answer is
   stored and sent to the provider exactly as a memory or a page is.
6. `ToolArgumentSchema` gains an optional description, emitted only when it is set, so this tool's
   arguments explain themselves without rewriting twenty other tools' request bodies.

## Not in scope

- A page capability that asks a visitor a question and waits. A page is rendered and returned; a
  question is the assistant's conversation. If that ever changes it is an `IDEA-` of its own.
- Per-argument descriptions for the other twenty tools. It invalidates the provider's cached prefix and
  deserves its own measurement: [IDEA-053](IDEA-053.md).
- A timeout for the answer. An interrupt waits until the owner answers, stops it, deletes the
  conversation or restarts the host.
- Any way for a question to carry a file, an image or a link the owner clicks.

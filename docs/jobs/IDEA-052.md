<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-052 — The assistant cannot ask the owner a question

| | |
| --- | --- |
| Size | L — a tool, a stored question and its migration, a coordinator, a route, the card in two renderers, the prompt and the configuration, six suites and four documents |
| Area | Web (assistant, storage), docs |
| Location | `src/uk.osric.asp.Web/Assistant/`, `src/uk.osric.asp.Web/Storage/`, `src/uk.osric.asp.Web/Pages/Seeds/assistant.js`, `docs/assistant.md` |

## Idea

The assistant can read, write, fetch and remember, but it can only hear the owner at the start of a
task and at a yes/no gate. A run that needs one fact the page store does not hold — which of two names
the owner prefers, what a heading should say, whether a section is still wanted — has three bad
options today: guess and be corrected by a wasted edit, write the question into a reply and end the
run, or remember it for another conversation and hope. None of them is a conversation.

The missing thing is small and ordinary: a tool whose result is the owner's answer, and a run that
waits for it the way it already waits for an approval. The owner sees a card in the transcript with
the questions, any options the model offered, and a box for their own words; the answer becomes the
tool's result and the run carries on in the same step. Nothing about it is a page capability — a
stored page cannot ask its visitor a question and wait, and this is the assistant's own tool — so it
belongs to the shell's assistant, beside approvals and continuation.

[The survey](../spikes/ask-tool-shape.md) found no cross-vendor standard, but a strong convergence on
one tool, an array of one to four question objects, optional options of a label and a description,
free text always allowed, and hard caps. [The plan](../plans/assistant-ask-owner.md) adopts that shape
and the lifecycle approval already has.

## Sketch

1. One tool, `ask_owner`, carrying its schema beside its code like every other tool
   (`assistant-a-tool-is-one-type`): `questions[]` of `{question, header?, options[]{label,
   description?}, multiSelect?}`, capped at four questions and five options by configuration.
2. The wait is the approval's, not a new mechanism: a stored record, a run status `awaiting_answer`
   that the active-run, recovery and index rules learn, a coordinator that only wakes the worker, and
   a restart that fails the record closed and writes the synthetic tool result the stop path already
   writes.
3. The answer is the tool's result, read from the store after the coordinator wakes the run, so a
   disconnected browser, a second device or a reload changes nothing about the outcome.
4. The card is rendered twice from one record — pending with a form, answered with the question and
   the answer — and the live page builds the pending one from `question.required` and removes it on
   `question.answered`.
5. The prompt gains an "Asking the owner" section and the version moves; the tool description says
   when to use it, when not to, and that it is alone in its step. Never ask for a secret: the answer
   is stored and sent to the provider exactly as a memory or a page is.
6. `ToolArgumentSchema` gains an optional description, emitted only when it is set, so the ask tool's
   arguments explain themselves without rewriting twenty other tools' request bodies.

## Not in scope

- A page capability that asks a visitor a question and waits. A page is rendered and returned; a
  question is the assistant's conversation. If that ever changes it is an `IDEA-` of its own.
- Per-argument descriptions for the other twenty tools. It invalidates the provider's cached prefix
  and deserves its own measurement.
- A timeout for the answer. Approvals and continuations have none: the run waits until the owner
  answers, stops it, deletes the conversation or restarts the host.
- Any way for a question to carry a file, an image or a link the owner clicks.

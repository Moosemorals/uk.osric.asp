<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Spike — how agent frameworks shape the "ask the user and wait" tool

| | |
| --- | --- |
| Question | Is there a standard shape for a tool that asks the owner a question and waits for the answer, and which parts of the state of the art make the tool easiest for DeepSeek to call correctly? |
| Answered | 2026-11-18 |
| Sources | Vendor documentation read on 2026-11-18: MCP specification (`2025-06-18`, `2025-11-25`, `2026-07-28`), Anthropic's Claude Agent SDK and tools reference, OpenAI's function-calling and Responses documentation, DeepSeek's tool-call guide and API reference, VS Code's `askQuestions` source, Cline's and Roo Code's tool schemas, LangGraph's interrupts, AutoGen's user proxy. No provider was called and no token was spent |
| Verdict | There is no cross-vendor standard, but every large vendor independently converged on the same shape: **one tool, an array of 1–4 question objects, options of `{label, description}` that are optional, free text always allowed, and hard caps**. MCP's deliberately flat schema exists because it must run against unknown clients; Claude Code's nested schema exists because it owns the client. DeepSeek is a known provider and this site's `edit_page` already fills a nested array of objects, so the bounded nested shape is the one to copy. The two things the evidence says matter more than the schema are a description that says *when to use it and when not*, and a per-argument description — which this site's tool schema does not emit at all |
| Recorded | `assistant-a-run-waits-on-one-owner-interrupt` and `assistant-a-question-is-a-tool-call`: [the owner interrupt plan](../plans/archive/assistant-owner-interrupt.md) and [the ask-owner plan](../plans/archive/assistant-ask-owner.md) |

## The answer

A blocking question is not a protocol feature anywhere. It is an ordinary function call whose result
the host fills in with the human's words, and the design question is only what shape the model has to
fill and how the host survives the wait.

Three designs are in production, and they differ by who owns both ends of the wire:

1. **A protocol-level request with a deliberately crippled schema** — MCP's `elicitation/create`. The
   server names a `message` and a `requestedSchema`, and the client answers with `action` one of
   `accept`, `decline` or `cancel` and, for an accept, a `content` object matching the schema. The
   schema is "flat objects with primitive properties only": no nested objects, no arrays of objects,
   `enum`/`oneOf` for choices, and an array of an enum when several may be chosen. Nesting is excluded
   "to simplify client user experience"
   ([elicitation](https://modelcontextprotocol.io/specification/2026-07-28/client/elicitation)).
   The latest revision (`2026-07-28`) also removed server-initiated requests entirely: a server now
   returns `inputRequests` on the original call and is retried with `inputResponses` — a stateless
   "multi round-trip" pattern, stated as a breaking change
   ([MRTR](https://modelcontextprotocol.io/specification/2026-07-28/basic/patterns/mrtr.md)). That is
   the strongest evidence that *waiting* is the hard part and that statelessness is the price of not
   holding a connection open.
2. **A rich, vendor-owned built-in tool** — Claude Code's `AskUserQuestion` and VS Code's
   `vscode_askQuestions`. Both take `questions[]` of objects, each with a short header, the question,
   options of `{label, description}` and a `multiSelect` flag, and both cap what the model may send:
   Claude Code is "1–4 questions with 2–4 options each", the header is at most 12 characters, and free
   text is the client's `Other` row rather than a schema field
   ([user input](https://code.claude.com/docs/en/agent-sdk/user-input#question-format),
   [tools reference](https://code.claude.com/docs/en/tools-reference#askuserquestion-tool-behavior)).
   VS Code's version makes `options` optional — so one tool covers free text, a single choice and a
   multi-select — keys its result by `header`, and distinguishes "soft" limits in the schema from
   "hard" limits it truncates to, because it does not trust the model
   ([`askQuestionsTool.ts`](https://github.com/microsoft/vscode/blob/main/src/vs/workbench/contrib/chat/common/tools/builtinTools/askQuestionsTool.ts)).
3. **A minimal, third-party-facing tool** — Cline's `ask_question` takes `{question, options: string[]}`
   with 2–5 options and says "you should only ask one question", and Roo Code's
   `ask_followup_question` takes `{question, follow_up[]}`; both tell the model that the options are a
   hint and the user may answer freely
   ([Cline tools](https://docs.cline.bot/tools-reference/all-cline-tools),
   [Roo Code](https://roocodeinc.github.io/Roo-Code/features/tools/ask-followup-question/)).

OpenAI has no first-class ask-user tool at all: the Assistants API that used to model this as
`requires_action` plus `submit_tool_outputs` was retired on 2026-08-26, and the current Responses API
models a question as a `function_call` item and an answer as a `function_call_output` item correlated
by `call_id`
([migration](https://developers.openai.com/api/docs/guides/migrate-to-responses)). Its Agents SDK has
a first-class *approval* pause (`needs_approval`, `result.interruptions`) but nothing for a free-form
answer ([approvals](https://developers.openai.com/api/docs/guides/agents/guardrails-approvals)).

The two framing decisions that follow are:

- **A question is not an approval.** Every framework keeps them apart. Approvals are gated actions
  with a fixed yes/no; questions collect content. A site that models one as the other ends up asking
  the owner to approve a question before answering it.
- **A wait has to be durable.** AutoGen's user proxy is the cautionary example — it blocks the team
  and leaves it "in an unstable state that cannot be saved or resumed"
  ([human-in-the-loop](https://microsoft.github.io/autogen/stable/user-guide/agentchat-user-guide/tutorial/human-in-the-loop.html)).
  LangGraph's `interrupt()` is the opposite answer: the value shown to the human is separate from the
  schema the answer is validated against, and a resume re-executes the node
  ([interrupts](https://docs.langchain.com/oss/python/langgraph/interrupts)). This site already has the
  durable shape for approvals — a stored record, a stored run status, a coordinator that only wakes the
  worker, and a restart that fails the wait closed — and a question should borrow it exactly.

## What makes it easy for DeepSeek

DeepSeek speaks OpenAI's function-calling dialect and its current guide is
[Tool Calls](https://api-docs.deepseek.com/guides/tool_calls). The constraints that bear on this design:

| Fact | Consequence |
| --- | --- |
| `tools: [{type: "function", function: {name, description, parameters}}]`, `tool_choice` of `none`, `auto` or `required` (never set here: this site sends `auto`) | Nothing new is needed on the wire; a tool is a tool |
| Names are `a-z`, `A-Z`, `0-9`, `_`, `-`, at most 128 characters, and unique | `ask_owner` is legal; the registry already guarantees uniqueness |
| Optional strict mode (beta) requires **every** property of every object to be `required` and rejects `minLength`/`maxItems` | Do not opt in: this tool wants optional arguments, and its caps belong in refusals and in the description |
| Strict and non-strict both reject an unknown property only when `additionalProperties: false` is sent — which this site always sends | The schema is already the strictest shape available without `strict` |
| Tool results are `{"role": "tool", "tool_call_id": …, "content": …}` appended after the assistant message that asked | The answer is the call's result; no second message is needed |
| Parallel tool calls are not documented either way, and the guide's example reads `tool_calls[0]` | Do not rely on them. A call that blocks should be alone in its step, and the description says so |
| Thinking mode supports tools from V3.2 on, but `required` and named `tool_choice` return HTTP 400 there | Another reason to send `auto` and express "call this now" in prose |

The vendor guidance on *shape* is unusually consistent:

- **OpenAI** asks for clear names and descriptions, "describe the purpose of the function and each
  parameter (and its format), and what the output represents", use enums and object structure to make
  invalid states impossible, and keep the initially available set of functions small — "aim for fewer
  than 20"
  ([function calling](https://platform.openai.com/docs/guides/function-calling#best-practices-for-defining-functions)).
  Strict mode's all-required rule is the opposite of what a question wants, so this tool stays
  non-strict on purpose.
- **Anthropic** says a detailed description is "by far the most important factor in tool
  performance", at least three or four sentences, stating what it does, when it should be used and how
  it behaves
  ([define tools](https://platform.claude.com/docs/en/agents-and-tools/tool-use/define-tools#best-practices-for-tool-definitions)).
- **Cline** adds a `successContext` string to its tool — "The user has answered the question. Continue
  working on their task using that answer." — so the model is told what the result *means* rather than
  only given data.

Two further findings matter here:

- **Nothing caps question counts in the schema that this site can emit.** `maxItems` is a JSON-Schema
  keyword the registry does not produce and DeepSeek's strict mode rejects anyway. The caps have to be
  refusals with the accepted range in the sentence, which is what every other tool here already does.
- **This site emits no per-argument descriptions.** `ToolRegistry.ParametersJson` writes
  `{"type": "string"}` per property and nothing else, so the only prose the model gets is the tool's
  own description. Every vendor above recommends parameter-level descriptions, and this tool needs
  them more than most: `questions` contains questions, and each question's `description` field (an
  option's second line) shares a word with the schema keyword. The cheapest honest fix is to let an
  argument carry a description and to write one for this tool's arguments; doing it for all twenty
  tools is a separate change, because it rewrites the provider's cached prefix.

## What this codebase already proves

The site's tool machinery already supports the shape the evidence recommends, and it has evidence of
its own that DeepSeek fills it:

- **Two levels of structure work.** `ToolArgumentSchema.Items` recurses, `ToolArguments.Parse`
  validates nested arrays of objects, and `edit_page`'s `edits` array —
  `[{startLine, endLine, replacement, expectFirst, expectLast}]` — is a nested array of objects in daily
  use. A question's `options` is the same depth again (`questions[].options[]`), which is the part the
  plan must prove rather than assume.
- **The description is the model's only prose.** `ToolSpec.Description` is sent verbatim beside the
  schema, and the prompt adds a section of its own. Both are where "when to use it and when not" has
  to live.
- **The wait already exists, once.** `RunProcessor.ResolveApprovalAsync` stores a record, sets the run
  to `awaiting_approval`, publishes an event, pauses the run's silence clock and waits on
  `ToolApprovalCoordinator`; a restart fails pending records closed and writes a synthetic tool result
  for the unanswered call. A question needs the same lifecycle with a different record and answer.
- **The tool surface is already over OpenAI's soft limit.** The registry holds twenty tools; a
  twenty-first is one more. The description and the prompt are the mitigation, and the risk is
  recorded in the plan rather than hidden.

## What was rejected

- **MCP's flat form.** It is a client-neutral compromise. The owner is known, the client is this
  site's own page, and the card can render options and a text box better than a flat property bag.
  Copying it would trade a better card for a constraint that protects nobody here.
- **One question per call, Cline-style.** The run pays a provider round trip per step, so a model with
  three unknowns would spend three steps and three waits. Batching up to four is what both rich
  clients do, and the card asks them together.
- **Options as plain strings.** An option's description is the model's chance to say *why* a choice
  exists ("short and formal", "keeps the URL"), and the anthropic guidance is explicit that
  descriptions carry the meaning.
- **A schema field for the free-text answer.** VS Code and Claude Code both make free text a client
  affordance, always available; a `freeText: boolean` argument is one more thing for the model to get
  wrong for no gain.
- **`strict: true`.** It would force every argument to be present and reject the caps, and this site
  has never sent it.

## The recommendation

One tool named `ask_owner`, one call per step, with this shape:

```
questions: [
  {
    question: string          // one sentence, what you need to know
    header: string?           // a few words, the card's label and the answer's key
    options: [                // 2–5, optional: omit for a free-text answer
      { label: string, description: string? }
    ]
    multiSelect: boolean?     // default false
  }
]
```

bounded to four questions per call and five options per question by configuration, with the owner
always able to answer in their own words, and the result returned in the order asked:

```json
{"ok": true, "data": {"answers": [
  {"question": "Which page?", "selected": ["/about"], "text": ""},
  {"question": "What should the heading say?", "selected": [], "text": "About us"}
]}, "error": null}
```

The description carries four sentences the model needs: when to use it, when not to (permission,
anything a tool can read), that the run stops until the owner answers so the call is alone in its
step, and that the answer is the owner speaking — not the untrusted tool output the other tools
return. The prompt repeats the last point, because the transcript's standing rule is that tool
results are data.

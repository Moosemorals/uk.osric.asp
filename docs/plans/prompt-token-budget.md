<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The provider's own count

| | |
| --- | --- |
| Status | Proposed 2026-11-17, held by the owner the same day and rewritten the same evening: an anchored estimate replaces the vendored tokenizer this plan first proposed (`product-steps-wait-for-the-owner`). Nothing below is built. |
| Size | Medium — one estimate in `Core`, the planner's arithmetic, the masker, six option changes, the words, and the docs that explain each |
| Area | Core (assistant), Web (options, tools, run loop), docs |
| Spike | [Counting DeepSeek tokens in C#](../spikes/deepseek-token-counting.md) — the exact counter is *not* adopted, but its measurements are the evidence for the ratios here and it remains the fallback |
| Decisions | Five names intended and two entries edited in place, below; taken at the merge (`product-plan-before-building`) |

## The short version

Every step already learns the exact size of the request it sent. This plan spends it:

- **The budget is anchored.** After each step we know the prompt's true token count, because the
  provider reports it. The next step's request is that request plus the messages appended since, so
  its size is `reported + estimate(the new messages)` rather than `estimate(the whole conversation)`.
  The error shrinks from the request to the delta, and it is corrected from the provider's own reply
  on every single step, for ever.
- **No tokenizer, no data, no licence.** The exact counter the spike measured is still the fallback
  if 3% proves too loose, but nothing here vendors 1 MB of DeepSeek vocabulary, tracks their chat
  template, or regenerates a table when they bump a version. The only thing to keep is a ratio, and
  the provider recalibrates it for us every step.
- **The budget is the model's, read from the provider.** `GET /models` publishes each model's
  `context_window` and `max_output_tokens`, so the request budget is that window less a small floor
  for the reply — 917,504 today — and neither `MaxContextTokens` nor `ModelContextTokens` is
  configuration any more. The provider is the only place a model's limits are written down.
- **`max_tokens` follows the prompt.** The window is shared between input and output, so the reply's
  allowance is computed per request: whatever the prompt leaves, capped at the published maximum and
  floored at the largest reply a step must still be able to make. A small conversation gets the
  model's full output room; a full one still gets a usable reply.
- **The masker loses its window.** `ObservationMasker` stops protecting "the newest 8 tool results
  and the last 2 tool calls". Oldest goes first, as it already does, and nothing is exempt.
- **The invented limits go with it.** Every cap that decides what reaches the model is audited below
  against one test — does it stop something the provider would have accepted, and can the model see
  what it lost — and the ones that fail it are cut, refused less, or tied to the one budget.
- **The words catch up**, and **usage stops being a setting**: a request that does not ask for its
  own usage cannot be checked, so `Assistant:IncludeUsage` goes.

## Goals

1. The budget — the window the provider reports for the configured model, less the reply we reserve
   — is compared against an estimate anchored on the provider's own `prompt_tokens`, not a ratio
   applied to the whole request.
2. The estimate is reproducible and its error is measured: every step records how far it was from
   the count the provider returned, so "the estimate is good enough" is a figure, not a claim.
3. A step pays for the messages it has not sized before, not for the whole transcript per candidate.
4. Masking is decided by cost alone: nothing is exempt, oldest goes first, and the stop is the fit.
5. Every content cap either comes from the model, is a fraction of the one budget, or has a note and
   a way to ask again. No free-standing character count decides what the model may read.
6. A request that fits the budget is still returned untouched, so the common step keeps its prefix
   byte-identical for the provider's prompt cache.
7. Nothing a page author can do changes: no `IContext` member, no capability, no editor action, so
   `/help` is untouched.

## Non-goals

- **Exact counting.** The spike measured it and it works; this plan does not adopt it. Its trigger
  to come back is the error gauge in Goal 2 rising above the margin, or a figure on the page that
  has to be true rather than close.
- **Changing the drop order.** Turns are still dropped oldest first after masking.
- **Summarising or compacting history.** Old turns are masked or dropped, never rewritten.
- **Removing the work limits.** Steps, tool calls, retries, timeouts, concurrency and the journal
  bound time and cost rather than context, and nothing they stop is lost — the run pauses and the
  owner decides.
- **A second provider's ratios.** One model family, one learned ratio; a second provider reports its
  own usage and is calibrated the same way.

## What exists today

`ContextPlanner.Build` masks old payloads one at a time and then hands the request to
`ContextTrimmer.Trim`, which keeps the newest whole turns that fit. Every candidate is measured by
serializing the whole body, dividing its byte count by a ratio calibrated from the last report, and
adding a ten-percent margin. Three things are wrong with it:

- **The estimate is applied to the whole request.** The one number we can be sure of — the size of
  the request the provider just counted for us — is used to derive a *ratio* and then thrown away.
- **The margin is a permanent tax.** Ten percent of the 655,360-token budget the fixed reserve
  allowed is 65,536 tokens — between one and two full-size tool results — given up on every step to
  cover an error that is mostly the ratio's, not the estimate's.
- **The reserve is a guess with a cost.** Asking for the model's whole output ceiling on every
  request takes 37.5% of the window away from the prompt whether the reply needs it or not.
- **The protected window can exceed the budget on its own.** `ObservationMasker` keeps the newest
  eight tool results, and `MaxToolResultChars` is 131,072 characters — about 81,000 tokens of JSON —
  so eight protected results are up to 655,000 tokens against a budget of that order. Where that happens
  no drop order helps: the request fails with `context_too_long` and the eight it protected are why.

## How the count works now

One rule: **the provider counts the last request, so we count the difference.**

```
step N        request  = messages 0..k             sent, reported as prompt_tokens = T
step N+1      request  = messages 0..k+m           estimate = T + tokens(messages k+1..k+m)
```

- **The anchor.** `RunProcessor` keeps, for the life of the run, the provider's `prompt_tokens` for
  the request it sent and the messages that request carried. The next request starts from the same
  stored transcript, so it *extends* that request rather than replacing it, and the part the provider
  already counted keeps its exact count.
- **The delta.** Only the messages appended since are estimated, and they are byte-measured exactly:
  their serialized length divided by the bytes-per-token ratio the last report implies. A tool result
  of 4,000 characters is a delta of about 2,500 tokens with an error of tens, not thousands.
- **Masking and trimming break the anchor only where they touch it.** A masked payload and a dropped
  turn are known byte amounts, so they are subtracted from the anchor as estimates; everything the
  planner did not change keeps its exact count. When the planner changes nothing — the common step —
  the estimate is `anchor + delta` and nothing else.
- **Per message, once.** `ContextPlanner` and `ContextTrimmer` never re-serialize a whole candidate:
  each message is measured as bytes once, and masking a message or dropping a turn is arithmetic
  over those numbers.
- **The margin comes down to 3%.** It covers the delta's ratio error and the one rendering we do not
  control (how the API injects `tools`), not an estimate of the whole conversation. 3% of 917,504 is
  27,525 tokens — a fraction of one full-size tool result, against the 65,536 the ten-percent margin
  used to cost — and it is applied *before* `max_tokens` is worked out, so it protects the sum of
  prompt and reply rather than being a haircut on the prompt alone.
- **The reply is sized to fit.** `max_tokens` is `context_window − estimate`, capped at the
  published `max_output_tokens` and floored at `Assistant:MinOutputTokens`. With the estimate
  carrying the margin, prompt plus `max_tokens` can never exceed the window even when the estimate
  is low: the margin is spent on the reply's allowance instead of on trimming.

- **The error is measured every step.** After each step we have the estimate and the truth, and the
  difference is the number that says whether 3% is right. If it settles below the margin the margin
  can fall; if it climbs, the fallback in the spike is waiting. What is watched is
  [below](#what-is-watched).

### D1 — **assistant-tokens-anchor-on-the-last-report**

Every step's estimate starts from the provider's `prompt_tokens` for the previous request and adds
only what has been appended since. The margin covers the delta, not the conversation, so it falls
from ten percent to three.

Rejected: the exact tokenizer. The spike proved it correct and cheap to run, but it costs 1 MB of
DeepSeek-acquired vocabulary under the DeepSeek License Agreement, a chat-template renderer that must
track their template, a 567-case fixture to regenerate on every vocabulary bump — and after all that,
the one rendering it still cannot see is how the API injects `tools`. The anchor has the same blind
spot and needs none of the rest.

Rejected: bytes with a fixed ratio, which is what the request row does today. It throws away the one
exact figure the system receives on every step.

## What is watched

The anchor only works while the estimate is close, and the only way to know is to compare it with
what the provider returns. That comparison is the first of four instruments. Each is one panel,
because the catalogue maps one instrument to one panel and a test holds the two lists together
(`metrics-one-panel-per-instrument`).

| Instrument | Kind | Unit | Labels | The question it answers |
| --- | --- | --- | --- | --- |
| `assistant.context.estimate.error` | histogram | `{token}` | `direction` (`over`, `under`) | How far the estimate was from the truth, and which way. `under` is the one that can breach the budget |
| `assistant.context.shaped` | counter | `{step}` | `kind` (`masked`, `trimmed`) | How often the planner has to change the request at all. This plan claims it is rare; this is the claim |
| `assistant.budget.exhausted` | counter | `{run}` | `cause` (`context`, `provider`, `output`) | How often a budget stops work, and which one: our context estimate before sending, the provider's window, or the reply reaching its computed `max_tokens` |
| `assistant.tool.truncated` | counter | `{result}` | `tool` (a registered name, or `(unknown)`) | How often a cap actually bites. If it is every step, the cap is wrong |

The error's buckets carry the margin as a boundary — `0, 64, 256, 1024, 4096, 16384, 27525, 65536,
262144` — so whatever lands above `le="27525"` is a step that spent more than the three percent the
estimate allows. That is the same trick the step and tool-call buckets already use to draw their cap
rather than hide it under `+Inf`.

The error is also one log line per step, because a figure on a chart wants an explanation beside it:
`Step {Step}: estimated {Estimate} tokens, the provider counted {Actual} ({Delta} {Direction}); the
request was {shaped|untouched}`. The other line is the cache warning, said once per run.

What is deliberately **not** instrumented:

- **How much of the prompt came from the anchor.** The error already answers it — if the anchor
  breaks every step, the error is a whole request wide — and a share metric would be a second way to
  say one thing.
- **A ratio per model or per tool.** One model, one conversation mixture; a label per tool would
  multiply series to say what `assistant.tool.duration` already says.
- **Anything tagged by conversation, run or message id.** The same cardinality trap the tool name
  avoids by recording `(unknown)` instead of a model-supplied string.

No new instrument watches the window itself: it is the denominator of the request meter, so a change
is visible there, and the sampler's log line says when it moved. What a *missing* window costs is
visible as `assistant.budget.exhausted{cause="context"}` — a refused run, counted where a budget
death is counted.

Two figures already collected get more useful without changing: `assistant.tokens{kind="cached"}`
over `kind="prompt"` is the prefix's stability, and it falls exactly when
`assistant.context.shaped` rises — which is the mask-versus-trim cost being measured rather than
argued about.

## The budget panel

The budgets fieldset at the foot of the conversation page is the human view of the same three numbers.
It grows from seven rows to ten, and every one of them is a `[data-budget]` row the existing script
already updates from `budget.updated`, so a figure that moves live is still the figure a reload shows
(`assistant-budgets-are-one-snapshot`).

| Row | Figure | Against | Why it is there |
| --- | --- | --- | --- |
| Context | the estimate for the **next** request | the prompt budget, window less the floor | The amount of the window the conversation is using |
| Reply | the `max_tokens` that request would be sent with | nothing — a figure, not a share | The output half of the split, which is now computed rather than configured |
| Window | context plus reply | `context_window` | The split in one pair; the note says the remainder is spare |
| Estimate | the last step's error, signed | the 3% margin | The current accuracy, and which way it missed |
| Completion | the last step's completion tokens | the `max_tokens` that step was sent with | Not the model's ceiling: what the step actually had |

The five rows that stay as they are — Message, Steps, Tool calls, Tool arguments, Tool result — keep
their meanings and their keys, and the two that are renamed keep their DOM ids. The note under the list is rewritten in the same change, because it
currently explains the request as "estimated in tokens from the exact body and the last provider
report", which is exactly the sentence this plan makes false.

Two details the shape forces:

- **The Estimate row is the one row that is signed.** It cannot reuse the used/available pair as a
  plain number, so it carries the magnitude against the margin and a direction word beside it — `over`
  when the estimate was high, which is safe, and `under` when it was low, which is the direction that
  can breach the budget. The row takes the `assistant-budget-exceeded` class when the magnitude passes
  the margin, so the eye finds it the way it finds every other exceeded budget.
- **The denominators move.** `budget.updated` already carries a `limits` map beside its figures, and
  the reply allowance belongs in it: it changes with the estimate, and a meter whose `max` did not
  move would draw a full budget as a quarter full.

The page and the metrics therefore report one thing twice, from one source: the estimate error is the
`assistant.context.estimate.error` histogram in aggregate and the Estimate row for this conversation.
Neither computes it independently.

## The masker's window

`ObservationMasker.MaskableMessages` protects the newest eight results and the two newest call
messages today, and `ContextPlanner` masks the rest one payload at a time, oldest first, stopping as
soon as the request fits. The count is the part that is wrong, and the audit found why:

- **It is not the protection it looks like.** Eight protected results are up to about 655,000 tokens
  of JSON against a 500,000-token budget, so the window can be the reason a request cannot fit.
- **Any protective boundary has the same flaw.** The obvious replacement — "never mask what the step
  in progress is answering" — protects up to `MaxToolCallsPerStep` results, so a 64-call burst would
  leave nothing maskable at all, and the newest turn cannot be dropped either: the step would fail
  rather than degrade.
- **The order already says what matters.** Masking walks oldest first, so the newest payloads are the
  last to be masked. A count adds nothing except an exemption that can bite back.

So: nothing is exempt. Everything except the system prompt and the tool definitions is maskable,
oldest first, and masking stops the moment the request fits. The model reasons over a placeholder
for something it just read only when every older payload has already been given up — which is the
case where the alternative is not sending the request at all.

`KeptResults` and `KeptCalls` both go; they are the same rule expressed twice. `MaximumArgumentLength`
is a different rule — the *shape* of a masked call's arguments, not which calls are exempt — and
stays.

## The invented limits

Only one number in the assistant comes from the model, and after this plan it is read from the
provider rather than typed into configuration: the context window. Every other number that decides
what the model reads is ours, including how much of that window to hold back for the reply. Each one
below is asked the same two questions: **does it stop something the provider would have accepted**,
and **can the model see what it lost and ask for it another way**? A limit with a note and a way to
ask again is a paging rule and stays. A limit that replaces data with an error, or cuts silently, is
a limit that throws context away.

| Limit | Shipped | When it bites | Verdict |
| --- | --- | --- | --- |
| `MaxContextTokens` | 500,000 | trims the request at 48% of a window the model would fill | **Gone.** The budget is the provider's `context_window` less a floor for the reply: 917,504 today, and no key to disagree |
| `ModelContextTokens` | 1,048,576 | a figure an operator has to keep in step with the model | **Gone.** `GET /models` reports it, and the same answer carries `max_output_tokens` |
| `MaxOutputTokens` | 393,216 | reserves the model's whole output ceiling on every request, taking it from the prompt | **Gone.** The ceiling comes from `/models` and the per-request allowance is computed; only a floor, `Assistant:MinOutputTokens`, is configured |
| `MaxToolResultChars` | 131,072 | the whole result is *replaced by a refusal*, so the model learns nothing but that the answer was big | **Cut, don't refuse.** The tail is lost either way; the head is worth keeping |
| `ToolResultBudget` | 65,536 (half) | every tool cuts its own answer before the envelope is measured | **One budget.** The half was a guess at escaping overhead |
| `BoundedDiff`, `ProjectSource` | 26,214 (a fifth) | a diff is cut; a page read is windowed to head and tail | **One budget.** Two more divisors with no story |
| search row text | 240 | each matching line is cut | **One budget** |
| `MaxToolArgumentChars` | 131,072 | the call is refused before it runs | **Above `MaxPageChars`.** A page the configuration lets the model store cannot be written, because its argument is half the page size (bug, below) |
| `MaxMessageChars` | 16,384 | the owner's message is refused with a 400 | **Raise.** An owner pasting a specification is not an attack; the newest turn is the one thing trimming cannot touch |
| `MaxPageChars` | 262,144 | a page write is refused | Keep, and say what it is: the page store's bound, not a context rule |
| `MaxMemoriesPerRun` | 12 | the memory block is cut by rank | **Drop.** Relevance already ranks; the budget bounds |
| `MaxMemoryCharsPerRun` | 4,000 | the memory block never exceeds 0.8% of the context | **One budget.** 4,000 characters beside a 917,504-token request is a rounding error |
| `MaxMemoryChars` | 1,000 | a memory write is refused | Keep: one memory is an entry, not a document |
| `MaxSearchMatches` | 100 | the hundred-and-first match is unreachable: no cursor, only a narrower query | **Raise** to the range's own maximum, as a stopgap: [IDEA-038](../jobs/IDEA-038.md) replaces the number with a key-then-line cursor and moves it onto the page contract as `Pages:Search:MaxMatches` |
| `MaxListLimit`, `DefaultListLimit` | 500, 50 | the assistant's listings stop and say so | Keep: `list_pages` already has an `after` cursor, and [IDEA-039](../jobs/IDEA-039.md) gives the page contract the same one |
| `ShellMatchLimit`, `ShellDefaultLines` | 100, 10 | `grep` stops and says "narrow the query"; `head` and `tail` default to ten | Keep for now. Narrowing is the only recourse today; the cursor arrives with [IDEA-038](../jobs/IDEA-038.md), which names this cap beside the search one |
| `ObservationMasker` | 8 results, 2 calls, 1,024 chars | old payloads become placeholders | **No window**, above |
| `TokenEstimator` | 10% margin on the whole request | the budget is a guess with a tax on it | Replaced above by the anchor and a 3% delta margin |
| `MaxSteps`, `MaxToolCalls`, `MaxToolCallsPerStep` | 160, 128, 64 | the run pauses and asks whether to continue | Keep: they bound work, not context, and the owner can grant another window |
| timeouts, `MaxProviderRetries`, `MaxActiveRuns`, `MaxQueuedRuns`, journal | | a request or a run stops | Keep: time and concurrency, not context |
| `MaxMemorySearchResults`, `MaxMemoryQueryTerms` | 50, 12 | recall ranks fifty candidates, a query carries twelve terms | Keep: they bound work and shape the query, and a miss costs a second recall |

Three of these are bugs rather than taste, and would be wrong under any policy:

1. **The argument cap is below the page cap.** `MaxToolArgumentChars` (131,072) is half
   `MaxPageChars` (262,144), and a page's content travels inside a tool call's arguments. A page the
   configuration says may be stored cannot be written through `write_page` at the size it allows.
2. **An oversized result is destroyed, not shortened.** `ToolResults.Bounded` replaces the whole
   envelope with `{"ok":false,…,"error":"Tool result exceeds the configured size limit."}`. The model
   is not told what the answer began with, and usually calls the tool again.
3. **The memory block is capped at 4,000 characters** — under 0.4% of the new budget.

The owner settled the open calls on 2026-11-17: no masking exemption, the budget at the window less
a reply floor, the reply allowance sized per request, and all five changes on this page — cutting instead of refusing, the message and the
memory block bounded by the budget, and the search-match cap raised.

### D2 — **assistant-one-budget-decides-what-fits**

There is one content budget, derived from the model's window. Every other content cap is either a
fraction of it or gone; none is a free-standing character count:

| Rule | Fraction | Shipped |
| --- | ---: | ---: |
| one tool result (a tool's answer, a fetch body, a diff, a projected read) | 1/8 of the budget | 183,500 bytes |
| one message | 1/8 of the budget | 183,500 bytes |
| the memory block a run is given | 1/32 of the budget | 45,875 bytes |
| the per-tool answer, before the envelope | the tool-result fraction | 183,500 bytes |
| one tool call's arguments | not a fraction: `MaxPageChars` plus 8,192 bytes | 270,336 bytes |

The fractions are invariants in code, not configuration: they exist so no single payload can fill
the budget a guard is protecting, and a configuration that could raise one to 1/1 would switch the
guard off. The caps are bytes because that is the unit a tool holds, converted with the spike's
conservative 1.6 bytes per token; 1/8 of 917,504 tokens at that ratio is 183,500 bytes, half as
large again as the 131,072 the result row allows today. That growth is the point — the budget grew,
and the cap is a fraction of the budget rather than a number standing beside it.

The argument cap is the one rule that is not a fraction, because what it must carry is a page:
`MaxPageChars` plus room for the envelope and the other fields. Deriving it from the page rather than
from the context is what stops the bug below recurring — a page the store accepts must be writable
by the tool that writes pages.

### D3 — **assistant-tool-results-are-cut-not-refused**

At a cap, a tool result is truncated, marked `truncated: true` and named in the note, and the head is
kept. It is never replaced by a refusal, because a refusal loses the information *and* the fact that
there was information. Refusals stay for what they are for: an invalid argument, a stale revision
hash, a path outside the writable prefix, a missing page.

Where a partial answer would be actively misleading — a diff whose cut line is wrong, a compile
result that looks complete — the existing suffix (`[truncated]`) and the `truncated` flag carry it,
so the shape does not change; only the size that triggers it does.

### D4 — **assistant-the-budget-is-window-minus-reply**

`Assistant:MaxContextTokens` goes, and so does `Assistant:ModelContextTokens`: the window is read from
the provider, and the only number left to configure is a floor under the reply.

**The window is shared.** The API describes `context_window` as "the total token capacity of the
context window, counting both input and output tokens", and `max_output_tokens` as "the maximum
accepted value of `max_tokens`". So the prompt and the reply draw on one pool, and every token named
in `max_tokens` is context the prompt cannot use on that request.

**Where the window comes from.** `GET /models` answers with one entry per model the key may use, and
each entry carries `context_window` and `max_output_tokens`, either of which "may be omitted when the
information is not available for that model". The provider's own example is `deepseek-flash`,
`DeepSeek-V4.1-Flash`, 1,048,576 and 393,216 — the two numbers the first draft of this plan asked an
operator to keep in step by hand.

- **The read.** `ProviderModelsClient` makes the request, and `ModelWindowSampler` reads it once at
  startup in the shape `AccountBalanceSampler` already has: same key, same `User-Agent`, bounded by
  `Assistant:ModelsTimeoutSeconds`, and one log line when the answer changes. It never stops the
  site — the assistant is one feature, and a provider that is down at startup must not take the page
  server with it.
- **The cache.** The answer is one immutable value for the process, so a step never waits on it.
- **No fallback.** A provider that cannot be reached, or an entry that omits `context_window`, leaves
  the assistant with no budget: a run refuses with `model_window_unknown`, naming the model and what
  was missing. A configured number beside a fetched one is two answers, which is the thing this
  decision exists to stop.
- **A definite error is different from an unavailable one.** If the provider answers and the
  configured `Assistant:Model` is not in the list, the site stops at startup with the same
  `ConfigurationException` a missing key gets, naming the key — the answer is authoritative and the
  owner should hear it at deploy time. If the provider does not answer, the site starts and the
  assistant says why it cannot run. The asymmetry is deliberate: the two must not look alike.
- **The refresh.** A request rejected for length is the one signal that the cached window is stale,
  so that failure re-reads `/models`; if the window it finds differs, the step is re-planned and sent
  once more. Nothing was generated and no tool ran, so the retry is free. A window that grows between
  deploys is picked up at the next restart, which is when a model change is expected anyway.

**The request budget is the window less a floor.** `Assistant:MinOutputTokens` ships at 131,072 — the
largest reply a step must still be able to make, which is one page at `MaxPageChars` plus its JSON
envelope, about 82,000 tokens, and room for the reasoning beside it. Everything else, 917,504 tokens,
is the prompt's. That is 40% more context than the fixed reserve allowed, and it is context the model
can actually be sent rather than tokens parked for a reply nobody writes.

**The reply's allowance follows the prompt.** `max_tokens` is computed per request as
`context_window − estimate`, capped at the published `max_output_tokens` and floored at
`MinOutputTokens`. Three things fall out of that:

- A short conversation gets the model's whole output ceiling, because there is nothing else to spend
  the window on.
- A full one still gets the floor, so the request is never rejected for reserving output it did not
  use.
- The margin is applied to the estimate *before* the subtraction, so prompt plus `max_tokens` cannot
  exceed the window even when the estimate is low: an estimate 3% high shortens the reply's allowance
  by 3% of the prompt, and the sum stays inside the window by exactly that much.

**The panel's denominator moves with it.** The output row shows what the last step was allowed, not
the model's ceiling, so the run stores the `max_tokens` it sent beside the completion tokens it got
back — one nullable column and one migration, and the figure on the page is the one the step lived
with.

**A cut reply is a known failure, not a surprise.** Once the allowance is whatever is left, a reply
can reach it — and a tool call cut through its arguments is unparsable JSON. So the cut is named: the
provider's `finish_reason` of `length` on a message that carried tool calls fails the step with
`reply_truncated` rather than surfacing as "invalid arguments", and it is counted as
`assistant.budget.exhausted{cause="output"}`. The fix is the owner's: a shorter page, or a new
conversation, and the message says which.

`AssistantOptions` therefore loses both context figures and gains `MinOutputTokens`, and the budget
becomes a value the run resolves: the window it holds, less the configured floor.

Rejected: keeping the key with the derived value as its default. A default is a second answer, and
the two drift. Rejected too: a startup read that fails the site when the provider is unreachable —
that turns a provider outage into an outage of every page. Rejected: reserving the whole output
ceiling on every request, which spends a third of the window on a reply that a prompt-heavy step
cannot make anyway.

It is an 84% larger worst-case prompt than the 500,000 the key shipped with, and with the provider's
prompt cache most of it is usually cached.

## The words

A change that rewrites every sentence about the budget should leave the vocabulary straight behind
it. The provider-facing half already matches the API word for word — `prompt_tokens`,
`completion_tokens`, `tool_calls`, `tool_call_id`, `reasoning_content`, `delta.content` — and the
mismatch is one layer out, where one concept picked up a name in code, another in configuration, a
third on the page and a fourth in prose.

The rule: **where the provider has a word, use it; where it does not, one concept has one word.**

| The concept | Today | Becomes | Why |
| --- | --- | --- | --- |
| A tool's returned content | *observation* (`ObservationMasker`), *tool result* (`MaxToolResultChars`, `ToolResults`, the decision), *tool output* (DeepSeek's marker), *answer* (the shell) | **tool result**, everywhere | `ObservationMasker` is the only place in the assistant that says "observation"; the decision that records it already says *tool results*, and MCP and Anthropic say *tool result*. DeepSeek's `<｜tool▁output▁begin｜>` is a token, not a name for the thing |
| The owner's message | *Prompt*, on the compose box | **Message** | Matches `MaxMessageChars` and the "Message" budget row beside it, and removes one of the three meanings of *prompt* |
| The whole input to one step | *Prompt*, in the usage list | **Prompt tokens** | The provider's own word, so the usage row stops reading as a synonym for the compose box |
| The reply | *Output* (budget row, `OutputTokens`), *Completion* (`CompletionTokens`) | **Completion** for the quantity; the last step's becomes `LastCompletionTokens` | `RunUsage` carries `CompletionTokens` (summed over the run) and `OutputTokens` (the last step only), which are two names for overlapping numbers |

What stays, deliberately:

- **`max_tokens` as the request's word.** `ChatRequest.MaxOutputTokens` becomes `MaxTokens`, the
  provider's own field name, because the value is now computed per request rather than configured.
  The only configured figure left is `Assistant:MinOutputTokens`, which says what the floor is.
- **The assistant-chat DOM id** and its class. The id names a region of the page, not a datum: the
  type is `Conversation` and stays that.
- **The two code families.** HTTP errors carry `assistant.` (`assistant.message_invalid`); run codes
  are bare snake case (`context_too_long`, `context_masked`, `memory_unavailable`). Each is internally
  consistent, and the page switches on neither, so the only rule worth writing down is that a run code
  never takes the `assistant.` prefix.
- **Masking, trimming and truncating.** All three are defined terms with behaviour behind them and
  stay; a shortened payload is `truncated` in JSON and in prose, not *cut* and not *shortened*, so the
  shell's "[the answer was cut at N characters]" and the tool descriptions' "is cut" say truncated.

### D5 — **assistant-the-vocabulary-is-the-providers**

Provider words where the provider has one, one word per concept where it does not. The renames that
follow are the table above: `ObservationMasker` → `ToolResultMasker`, the compose legend "Prompt" →
"Message", the usage row "Prompt" → "Prompt tokens", and `OutputTokens` → `LastCompletionTokens` on
`RunUsage` and `BudgetSnapshot`.

Rejected: leaving it alone because the code works. The three meanings of *prompt* are what made this
plan's first draft wrong about what the budget measures, and a reader who confuses the compose box
with `prompt_tokens` will make the same mistake.

## Usage is not a setting

`Assistant:IncludeUsage` goes, and with it `ChatRequest.IncludeUsage`: the request always sends
`stream_options.include_usage`. Off means no `prompt_tokens`, so no anchor, no error gauge and no
cache check — a key that can silently disable everything this plan depends on is not configuration,
it is a trap with a default.

The provider's usage also answers a question we were assuming: the system prompt is a constant
(`PageAuthorPrompt.Version` and fixed text, no clock) and the memory block is recalled once, so from
a run's second step the provider has seen every byte before and should report most of the prompt as
cached. A step whose request the planner returned untouched — `IsMasked` and `IsTrimmed` both false —
that reports no cached tokens means something changed that should not have, and the run says so once.
On a step that *was* masked or trimmed the cached share falls instead, which is the policy's cost
being measured rather than argued about.

## Components

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Core/Assistant/TokenEstimator.cs` | Anchored: an exact base, a byte delta, a 3% delta margin, and the error against the last report |
| `src/uk.osric.asp.Core/Assistant/PromptSizes.cs` | New: each message's serialized byte size, computed once, and the arithmetic masking and dropping apply |
| `src/uk.osric.asp.Core/Assistant/ContextPlanner.cs` | Sizes once, then adds and subtracts; carries the anchor forward |
| `src/uk.osric.asp.Core/Assistant/ContextTrimmer.cs` | Fits by byte sums converted once; the whole-turn rules are unchanged |
| `src/uk.osric.asp.Core/Assistant/ToolResultMasker.cs` | Renamed from `ObservationMasker`; `KeptResults` and `KeptCalls` go; every payload is maskable, oldest first |
| `src/uk.osric.asp.Core/Assistant/RunUsage.cs`, `BudgetSnapshot.cs` | `OutputTokens` → `LastCompletionTokens`; the snapshot gains `Window`, the next request's `MaxTokens`, and the last step's estimate error |
| `src/uk.osric.asp.Core/Assistant/ChatRequest.cs`, `ChatRequestJson.cs` | `IncludeUsage` goes; the body always carries `stream_options.include_usage`. `PublicAPI.Unshipped.txt` gains the `*REMOVED*` pair and the shorter constructor |
| `src/uk.osric.asp.Web/Assistant/RunProcessor.cs` | Holds the anchor per run; computes `max_tokens` per request and stores it; records the estimate's error; warns once when an untouched request reports no cache hit; counts the message against the budget |
| `src/uk.osric.asp.Web/Assistant/Run.cs`, the storage context and one migration | The step's `MaxTokens` and `EstimatedTokens`, so the reply allowance and the estimate error survive a reload |
| `src/uk.osric.asp.Web/Assistant/AssistantEndpoint.cs` | Nine budget rows: Context, Reply, Window and Estimate beside the five that stay; the rewritten note; the compose legend "Prompt" → "Message", the usage row → "Prompt tokens", the budget row "Output" → "Completion" (the DOM ids stay) |
| `src/uk.osric.asp.Web/Pages/Seeds/assistant.js` | The Estimate row's signed figure and direction word, and the moving reply allowance in the `limits` map |
| `src/uk.osric.asp.Web/Assistant/AssistantOptions.cs`, `appsettings.json` | `MaxContextTokens`, `ModelContextTokens`, `MaxOutputTokens` and `IncludeUsage` go, `MinOutputTokens` (131,072) and `ModelsPath` arrive; `MaxToolResultChars`, `MaxMessageChars` and `MaxMemoryCharsPerRun` go; `MaxToolArgumentChars` and `MaxSearchMatches` move |
| `src/uk.osric.asp.Web/Assistant/AssistantOptionsValidator.cs` | The argument cap must exceed the page cap; a floor that leaves less than 16,384 tokens of the reported window for the prompt is refused |
| `src/uk.osric.asp.Web/Assistant/ProviderModelsClient.cs` | New: `GET {BaseUrl}{ModelsPath}` with the assistant's key, bounded by `Assistant:ModelsTimeoutSeconds`; answers with the configured model's `context_window` and `max_output_tokens`, or the reason there is none |
| `src/uk.osric.asp.Web/Assistant/ModelWindowSampler.cs` | New: the startup read, in the shape `AccountBalanceSampler` already has — never fatal, one log line when the answer changes |
| `src/uk.osric.asp.Core/Assistant/ContextBudget.cs` | New: a reported window and a configured reply, resolved once per run into the request budget the planner is given and the byte caps a fraction of it allows |
| `src/uk.osric.asp.Web/Assistant/OpenAiChatCompletionClient.cs` | On a `400`, read the provider's error code: a context-length failure becomes `context_window_mismatch`, anything else stays `provider_error`. The body's message is parsed for the code and never logged |
| `src/uk.osric.asp.Web/Assistant/AssistantMetrics.cs`, `src/uk.osric.asp.Web/Metrics/StatusCatalogue.cs` | Four instruments and their buckets, and the four panels the catalogue maps them to |
| `src/uk.osric.asp.Web/Assistant/Tools/ToolResults.cs` | Truncate and flag, never replace a result with a refusal |
| `src/uk.osric.asp.Web/Assistant/Tools/PageTools.cs`, `ViewTool.cs`, `FetchTool.cs`, `DiffTool.cs`, `ShellRunner.cs`, `ReferenceTool.cs`, `HistoryTools.cs` | One answer budget from `ContextBudget`, not a half or a fifth of a character cap; the prose says "truncated" where it says "cut" |
| `src/uk.osric.asp.Core/Assistant/Data/`, `tools/tokenizer-data/`, `LICENSES/`, `THIRD-PARTY-NOTICES.md` | **Not needed.** The first draft of this plan would have added them; the anchor does not |

## Tests

- **The anchor.** A second step whose transcript only grew is estimated as the first step's reported
  count plus the delta; a step that masks one old payload subtracts that payload and nothing else.
- **The instruments.** The estimate error recorded `over` and `under`; a run stopped by our context
  estimate, by the provider's window and by the output limit, each counted under its cause; a shaped
  step counted under its kind; a truncated result counted under its tool, with an unknown tool named
  `(unknown)`. `AssistantMetricsTests` already holds the catalogue and the registrations to the same
  list, so the four new names fail the build until both agree.
- **The provider's arithmetic.** A recorded stream body where `prompt_tokens + completion_tokens`
  equals `total_tokens`, so the total the page shows is the provider's own and not ours summed.
- **The cache check.** A second step whose request was untouched and which reports no cached tokens
  warns once; a step that masked does not, because its cache miss is expected.
- **The planner's arithmetic.** The existing behaviour tests kept, with budgets expressed as a
  request's estimate rather than its bytes; plus a masking delta and a dropping delta.
- **The masker's order.** Nothing is exempt, so a 64-call burst of large results is maskable oldest
  first and the step degrades instead of failing; the two newest call messages are maskable too.
- **The provider's own limit.** A recorded `400` whose body carries a context-length error becomes
  `context_window_mismatch`; a `400` with any other code stays `provider_error`; a body with no code
  falls back without throwing.
- **The models answer.** The provider's own documented example — `deepseek-flash` with
  `context_window` 1,048,576 and `max_output_tokens` 393,216 — parses to those two figures; an entry
  with the field omitted yields no window; a model absent from the list stops startup naming
  `Assistant:Model`.
- **The sizing.** A 1,048,576-token window with a 131,072 floor is a 917,504 budget, and the
  arithmetic follows the reported value rather than a stored one.
- **`max_tokens` follows the prompt.** An empty conversation asks for the published ceiling; a prompt
  estimated at 900,000 asks for 148,576; a prompt at the budget asks for the floor and not less. The
  margin is applied before the subtraction, so prompt plus `max_tokens` never exceeds the window even
  when the estimate is low.
- **The panel.** Ten rows render; the Context row is the next request's estimate against the prompt
  budget; Reply is the allowance that estimate buys; Window is the two together; Estimate shows the
  last step's signed error and takes the exceeded class past the margin; Completion is the last step's
  tokens against the allowance that step had.
- **The live update agrees with the reload.** `budget.updated` carries every figure the panel draws,
  including the moving reply allowance, so a live row and a reloaded one read alike.
- **A cut reply.** A `length` finish on a message carrying tool calls fails the step with
  `reply_truncated`, not as invalid arguments.
- **A length rejection.** The step re-reads `/models` and, when the window differs, is re-planned and
  sent once more; when it does not differ, the step fails without a second request.
- **No window.** A run on a provider that never answered refuses with `model_window_unknown` instead
  of guessing.
- **The content budgets.** A 200 KB tool result arrives truncated with its head intact and
  `truncated: true`; a page at `MaxPageChars` can be written, so its argument fits; the message the
  owner sends is bounded by the context budget rather than 16,384 characters; the memory block is
  bounded by its fraction of the budget and not by 4,000 characters.
- **The validator** refuses an argument cap below the page cap, so the half-page bug cannot return,
  and refuses a floor that leaves less than 16,384 tokens of the window for the prompt.
- **The words.** `AssistantChatLayoutTests` asserts the compose legend and the usage row by their
  literal text, so it moves with them: "Message", "Prompt tokens", "Completion".
- **Deleted:** the byte-based budget expectations in `ContextTrimmerTests`, `ContextPlannerTests`,
  `BudgetSnapshotTests`, `AssistantBudgetTests`, `RunProcessorTests` and `AssistantChatLayoutTests`;
  `ObservationMaskerTests` becomes `ToolResultMaskerTests`; and the test that asserted the body omits
  the usage chunk goes with the flag.

## Phases

- **Phase 1 — the anchor.** `PromptSizes`, the anchored `TokenEstimator`, and the planner's
  arithmetic over sizes, with `assistant.context.estimate.error` and `assistant.budget.exhausted`
  as they become true.
- **Phase 2 — the masker.** The window goes, `ObservationMasker` becomes `ToolResultMasker` while the
  file is open, `assistant.context.shaped` counts it, and the tests follow.
- **Phase 3 — the budgets.** `ContextBudget`, the models read, the option changes, the truncate-don't-refuse rule,
  `assistant.tool.truncated`, and the validator rule. It comes after Phase 1 because the caps are
  fractions of the budget.
- **Phase 4 — the words.** `LastCompletionTokens`, the three page labels, and the glossary entries for
  Turn, Request, System prompt and Tool result. Independent of Phases 1-3 and could go first.
- **Phase 5 — the record.** `docs/assistant.md`, `docs/metrics.md`, `docs/testing.md`, the spike's
  verdict line (measured, not adopted, with its trigger), the two edited decisions and the new ones,
  and `make check`.

The run's two columns — `EstimatedTokens` for the estimate error, `MaxTokens` for the reply
allowance — and their one migration land in the phase that first needs to read the figure back:
Phase 1 and Phase 3 respectively. Nothing is deployed between phases: the owner merges the work
complete, so a schema change mid-programme costs no release, and the host carries no `Assistant__`
override of either removed key to clear.

## Documentation and decisions

- `docs/assistant.md` — the budget paragraph: the anchor, the delta margin, the error gauge, the
  cache check, the masker's order and the one-budget fractions, replacing the ratio, the eight-result
  window and the character caps.
- `docs/metrics.md` — the context figure's meaning and four new Assistant rows: the estimate error,
  shaped steps, exhausted runs and truncated results; the panel count per level; and the cached
  share read beside them.
- `docs/testing.md` — the anchor's tests and the suites' counts.
- `docs/spikes/deepseek-token-counting.md` — the verdict line: exact counting is measured and stands,
  not adopted, with the gauge as the trigger to revisit. The spike keeps its numbers; it loses its
  expected role.
- `docs/glossary.md` — four new entries: **Turn** (the trimmer's unit, with Exchange pointing at it),
  **Request** (the whole input to one step), **System prompt** and **Tool result**; the **Token
  estimate** entry rewritten for the anchor; and Masking re-pointed at `ToolResultMasker`.
- `docs/decisions.md` — five entries at the merge:
  **assistant-tokens-anchor-on-the-last-report**, **assistant-one-budget-decides-what-fits**,
  **assistant-tool-results-are-cut-not-refused**, **assistant-the-budget-is-window-minus-reply** and
  **assistant-the-vocabulary-is-the-providers**; and two edited in place,
  [`assistant-the-request-budget-is-tokens`](../decisions.md#assistant-the-request-budget-is-tokens)
  and [`assistant-old-tool-results-are-masked`](../decisions.md#assistant-old-tool-results-are-masked).
  That entry's *Recorded in* line names `ObservationMasker`, so it moves to `ToolResultMasker` in the
  same merge.
- `/help` is unchanged: this adds no page-authoring capability.

## Risks

| Risk | Answer |
| --- | --- |
| The delta's ratio is wrong for new content | The error is confined to the delta, so a base64 result that tokenizes denser than the ratio shifts the estimate by a fraction of that result, not of the conversation; the next report corrects it. The gauge is what turns "a fraction" into a figure |
| 3% is too tight or too loose | It is one constant with a gauge beside it. Too tight shows as `context_too_long`, too loose as an error figure well under the margin; either way the next change is a one-line margin, and the exact counter is the fallback the spike already measured |
| Masking breaks the anchor | It breaks it by exactly the payloads it changes, which are known byte amounts. Everything it did not touch keeps its exact count |
| The provider is unreachable when the site starts | The window is unknown, so the assistant refuses runs with `model_window_unknown` and every page still serves. The startup read is deliberately not fatal; the sampler logs one line |
| An entry omits `context_window` | The provider says the information is not available, and guessing it is the thing this decision refuses. The assistant is unusable for that model until the provider publishes it |
| The window changes under a running site | It grows: the extra is unused until the next restart, which is when a model change is expected. It shrinks: the first rejected request re-reads `/models` and retries the step once, so the cost is one request, not a failed run |
| The startup read is a new network dependency | Bounded by `Assistant:ModelsTimeoutSeconds`, logged, and non-fatal; the balance sampler beside it has been living with the same shape |
| The provider changes what `prompt_tokens` includes | The anchor *is* the provider's number, so a change in what it counts moves the estimate with it rather than against it |
| A larger prompt costs more per step | The budget moves to window minus reply, so a conversation that used to trim now pays for what it sends. Most of it is a cached prefix. To spend less, raise `MinOutputTokens` or use a model with a smaller window; there is no key that silently caps the prompt below the window the provider reports |
| A reply is cut at its allowance | The allowance is whatever the prompt left, so a prompt-heavy step has a small one. The cut is named: `reply_truncated` when the cut message carried tool calls, `budget.exhausted{cause="output"}` in the metrics, and the message tells the owner a shorter page or a new conversation is the fix |
| A huge single message fills the newest turn | One message may take a fraction of the budget, so a turn of maximum-size parts can still exceed it; the planner says `context_too_long` with the estimate named |
| Masking the step's own results | It happens only after every older payload is masked, which is the point where the alternative is not sending the request. The placeholder says how much was there, and the tool can be called again |
| The fractions are misjudged | They are four lines in one place with a test each, and every one is a code invariant rather than a configuration value, so changing one is a code review and not a deployment |
| A rename lands without its citations | Type names are not what `make decisions` checks, so the plan lists each one: the masker class, its tests, and the *Recorded in* line of the decision that names it. The public-API files fail the build if `LastCompletionTokens` is added without them |
| The page's figure is close rather than true | It is, by design. It is within the margin of the last report plus the delta, and the usage list beside it shows the provider's own count for what was actually sent |

## Delivered when

`make check` is green; a step's estimate is the previous step's reported `prompt_tokens` plus the
delta, and every step records how far it was from the truth; the estimate's error is recorded `over` and `under`, shaped steps, exhausted runs and truncated
results are counted; the conversation page shows the window split into context, reply and spare, the
last step's signed estimate error against the margin, and the last step's completion against the
allowance it was given, all of which move live and survive a reload; a step whose request was returned untouched reports its cached share, and one that
reports none says so once; `Assistant:IncludeUsage` no longer exists and every body asks for the usage
chunk; the masker has no window and masks oldest first; every content cap is a fraction of the budget
or has a note and a way to ask again; an oversized tool result arrives truncated
with its head intact instead of as a refusal; a page at `MaxPageChars` can be written; there is neither a `MaxContextTokens` nor a `ModelContextTokens` key — the budget is the provider's
reported window less the configured floor, and
a reply that leaves less than 16,384 tokens for the prompt is refused at startup; a provider that
rejects a request for length fails as `context_window_mismatch` rather than as a generic provider
fault; one word names one thing — a tool result is a tool result in the
class, the config key and the record, *prompt* names only the system prompt unless it is the provider's
`prompt_tokens`, and `RunUsage` says `LastCompletionTokens` for the last step and `CompletionTokens`
for the run; the five new decisions and the two edits are in `docs/decisions.md`; and no conversation
that fits the budget it fits today is refused or trimmed sooner than it is today.

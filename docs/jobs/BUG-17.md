<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-17 — A long think loses the answer

| | |
| --- | --- |
| Severity | Medium |
| Area | Assistant, provider client, conversation page |
| Location | `src/uk.osric.asp.Web/Assistant/OpenAiChatCompletionClient.cs`, `AssistantRunProcessor.cs`, `AssistantEndpoint.cs`, `Pages/Seeds/assistant.js` |

The file and suite names predate DEC-122: `AssistantRunProcessor.cs` is now `RunProcessor.cs`, and
`AssistantRunProcessorTests` is now `RunProcessorTests`. The mapping in
[naming consistency](../plans/naming-consistency.md#the-mapping) is the translation.

## Problem

A conversation worked for a few turns and then, whenever the model needed to think for a while, the
answer stopped mid-sentence. The conversation page made it worse than it needed to: the partial
message was stored and rendered, but the only trace of the cause was the bare word `failed` under it.

Two independent causes, both reproduced by the browser suite:

1. **The provider deadline measured duration, not silence.** `Assistant:RequestTimeoutSeconds` was a
   single `CancelAfter` for the whole streamed completion, so a model that kept sending reasoning for
   longer than five minutes was cut off mid-thought — while it was healthy and still talking. The
   cancellation surfaced as `run_timeout`, the same code a whole-run timeout uses, so even the stored
   run said the wrong thing.
2. **A truncated answer forgot why it was truncated.** `MaxOutputTokens` counts reasoning tokens, so a
   long think can spend most of the budget before the answer starts. The provider then reports
   `finish_reason: "length"`, the run published a `run.warning` — and the page reloaded one event
   later, taking the warning with it.

A third, smaller defect surfaced while proving the first: a provider payload whose `delta` was not an
object threw `InvalidOperationException` from the property read, which the run reported as
`internal_error` rather than `provider_invalid_response`.

## Fix

- The request deadline is re-armed for every event the provider sends. Only a stalled stream ends a
  step, with `provider_timeout` and its own sentence; `Assistant:RunTimeoutSeconds` is still the hard
  ceiling for the whole run.
- A message that stops at the output limit is stored with status `truncated`, and the conversation
  view renders a sentence per status instead of the raw word — `truncated`, `failed`, `cancelled` and
  `interrupted` all read as English.
- The run's own error message is rendered in the transcript (`p.assistant-run-error`), so the reason
  survives the reload that follows every terminal event.
- The status line says why before the reload instead of "Response complete".
- A provider payload that is not the expected shape fails as `provider_invalid_response`.

Tests: `AssistantProviderClientTests` proves a stream that keeps sending outlives the deadline and one
that stalls does not; `AssistantRunProcessorTests` proves the `truncated` status and the
`provider_timeout` code; `AssistantCutOffTests` proves both end to end in Firefox, and the two
`[Explicit]` live fixtures prove them against the configured provider.

## Not fixed here

- The event-stream endpoint logs a failed request when the browser reloads mid-run
  (`NotSupportedException` from writing to a response that is already gone). It is noise from the
  framework's writer on a closed socket, not a user-visible fault; see ARCH-13 for streaming work.
- The sign-in throttle is not configurable from configuration, so the browser suite signs in once and
  reuses the browser state rather than raising the limit.

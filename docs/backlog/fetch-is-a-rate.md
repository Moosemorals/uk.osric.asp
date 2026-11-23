<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A fetch allowance is a total, and a job gets one for its whole life

| | |
| --- | --- |
| Difficulty | determined-mountain-goat |
| Utility | high |
| Status | planned |
| Blocked by | — |
| Plan | [fetch-is-a-rate](../plans/fetch-is-a-rate.md) |
| Area | Core (pages), Web (pages, work, configuration), docs |
| Location | `src/uk.osric.asp.Core/Pages/Allowance.cs`, `src/uk.osric.asp.Web/Pages/RenderFetchBudget.cs`, `ReadBudget.cs`, `PageHttpFetch.cs`, `PageFetchOptions.cs`, `PageReadOptions.cs`, `PageCatalogScope.cs`, `PageFetcher.cs`, `OutboundFetcher.cs`, `PageEndpoint.cs`, `src/uk.osric.asp.Web/Work/PageJobOptions.cs`, `PageJobRunner.cs`, `src/uk.osric.asp.Web/appsettings.json`, `Program.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `docs/pages.md`, `deploy/README.md` |
| Spike | — |

## Idea

`Pages:Fetch:MaxRequestsPerRender` is a count, not a rate. A render spends at most ten requests from
one shared counter, and a job is given that same ten when it starts and may make no more until it
ends — a worker that lives nine hours has the same allowance as a page that lives five seconds. A
timeline fan-out cannot be sized against either number, and neither can be read by the page that is
spending it.

Two smaller things are wrong in the same counter:

- **A refused request spends it.** `PageHttpFetch` takes the allowance before the request is
  validated, so a request the policy refuses costs one of the ten. A page that experiments with a
  header has less left for the requests it meant to make.
- **A stream costs one.** That is right, and it should stay: the token pays for opening the request,
  not for the lines that arrive.

The owner's rule replaces the total with a rate, and the rate is chosen so nothing gets worse: a
render keeps the count it has today when it runs its whole deadline, and a fast page keeps today's
burst. That is a token bucket whose capacity is `rate × Pages:Render:TimeoutSeconds`. A job gets its
own rate, and its duration is the only ceiling — the literal reading of "a rate instead of a limit".

The same review found the read counter has the defect too. `Pages:Read:MaxSourceCharsPerRender` is
built once per job as well, so a worker that reads a template per tick is starved after its first
half megabyte, and a listing or a search never paid it. On 2026-11-23 the owner extended this item to
convert it in the same change: one rule over requests and characters, one pure bucket, one derivation
of a burst.

## Sketch

1. **The bucket is pure.** An `Allowance` in `Core` holds a capacity, a rate and the last refill,
   and answers "may I take one now" and "how many are left" for a given instant. It is a function of
   the clock the caller passes, so a test drives it with a fake clock rather than a sleep
   (`code-fixtures-hold-no-deadline`). One type serves both allowances, so it is named for what it
   is rather than for fetch.
2. **The configured value is the rate.** `Pages:Fetch:FetchesPerSecond` (fractional) replaces
   `MaxRequestsPerRender`; the capacity is derived from it and `Pages:Render:TimeoutSeconds` rather
   than being a second key that could disagree. `Pages:Jobs:FetchesPerSecond` is the worker's. Both
   are required, ranged, and named in a refusal.
3. **The charge moves after the validation.** A request that would be `InvalidRequest` takes no
   token; one that is attempted takes exactly one, whatever its outcome.
4. **The numbers are readable.** The budget answers its capacity, the tokens left and the rate; the
   capability that publishes them to a page is
   [`the-site-is-one-type`](the-site-is-one-type.md), so a fan-out can size itself instead of
   guessing.
5. **The refusal keeps its word.** `TooManyRequests` stays the value, and the message names the rate
   and the key that sets it, as the limit's message names the limit today.
6. **The read allowance is the same rule.** `Pages:Read:SourceCharsPerSecond` replaces
   `MaxSourceCharsPerRender`, with the same derived capacity, and `Pages:Jobs:SourceCharsPerSecond`
   is the worker's. A read is whole, so the burst is also the largest single template that can be
   read — which is true of the count today.
7. **The manual and the record.** `/help` and [pages](../pages.md#page-contract) carry the rate and
   the job's own value; the merge records the name the plan intends.

## Not in scope

- **Publishing it to a page.** The live capability is
  [`the-site-is-one-type`](the-site-is-one-type.md); this item makes the figures readable inside the
  host and leaves the door to that item.
- **The headers.** What a page may send is
  [`outbound-headers-are-an-allow-list`](outbound-headers-are-an-allow-list.md).
- **The job's other clocks and limits.** `Pages:Jobs` already bounds a job's silence, duration,
  messages and journal; these add two rates beside them.
- **A local or internal host.** The status page keeps its shell route and its own client, and the
  assistant's `fetch` tool keeps `MaxToolCalls`; neither is exempted here, and no loopback or
  private-address rule arrives with the rate.

## Open questions

None. The owner settled the shape on 2026-11-22: a token bucket with today's capacity, a rate derived
from the render's deadline, and a per-job rate with the job's duration as its only ceiling. On
2026-11-23 they extended it to the read allowance and set the status page aside as a special case.

**Origin:** the owner's ask 6 of 2026-11-22, after measuring a render refused its sixth request and
asking what a worker gets, and their answer that the allowance should be a rate rather than a limit;
extended on 2026-11-23 by their review of the other budgets.

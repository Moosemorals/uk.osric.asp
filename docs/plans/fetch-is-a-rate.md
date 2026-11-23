<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# An allowance is a rate, and a job gets its own

| | |
| --- | --- |
| Status | Agreed 2026-11-23 — the owner said yes; built on `plan/fetch-is-a-rate`, and the merge it waits on is theirs |
| Size | Medium-large — one pure Core type, two budgets rebuilt around it, four keys out and four in, one validation seam in the fetch edge, and the tests that pin the clock |
| Area | Core (pages), Web (pages, work, configuration), docs, build (public API) |
| Spike | None. Every rule is arithmetic on an instant the caller passes, and the existing fetch tests already drive the refusal path with a fake fetcher |
| Backlog | [`fetch-is-a-rate`](../backlog/fetch-is-a-rate.md) is this job, extended on 2026-11-23 to the read allowance |
| Decisions | One name intended, below; taken at the merge (`product-plan-before-building`) |

## The short version

- **An allowance is a rate with a burst.** `Pages:Fetch:FetchesPerSecond` and
  `Pages:Read:SourceCharsPerSecond` replace `MaxRequestsPerRender` and `MaxSourceCharsPerRender`.
- **The burst is what the rate spends in a whole render**: `Pages:Render:TimeoutSeconds` × the rate,
  in one derivation, so no second key can disagree and nothing that is allowed today is refused.
- **A job gets its own rate**: `Pages:Jobs:FetchesPerSecond` and `Pages:Jobs:SourceCharsPerSecond`.
  A job is one lifetime, so its burst is refilled for as long as it runs; its duration is the only
  ceiling.
- **A request that would be `InvalidRequest` takes no unit**; one that is attempted takes exactly
  one, whatever its outcome. A stream still costs one at open, which is right and stays.
- **The budget answers its capacity, what is left and its rate**, so `the-site-is-one-type` can
  publish the figures without guessing.
- **The refusal keeps its word.** `TooManyRequests` stays the value, and the line names the rate and
  the key.

## The problem

### Fetch

`Pages:Fetch:MaxRequestsPerRender` is a count, not a rate. A render spends at most ten requests
from one shared counter, and a job is given that same ten when it starts and may make no more until
it ends — a worker that lives nine hours has the same allowance as a page that lives five seconds.
A timeline fan-out cannot be sized against either number, and neither can be read by the page that
is spending it.

Two smaller things are wrong in the same counter:

- **A refused request spends it.** `PageHttpFetch` takes the allowance before the request is
  validated, so a request the policy refuses costs one of the ten. A page that experiments with a
  header has less left for the requests it meant to make.
- **A stream costs one.** That is right, and it should stay: the token pays for opening the request,
  not for the lines that arrive.

### Read

The same defect sits beside it, found by the owner's review of the budgets on 2026-11-23. A
`ReadBudget` is built once per job ([`PageJobRunner`](../../src/uk.osric.asp.Web/Work/PageJobRunner.cs))
with `Pages:Read:MaxSourceCharsPerRender` (512 KB) for the job's whole life, and a job that reads a
template per tick is starved after the first half megabyte. Only source reads charge it — a listing
or a search never has — so the defect is exactly the fetch one over characters instead of requests.

Converting both at once is deliberate: one rule, one pure type, one derivation of a burst, and one
place that says why a job is different from a render. Converting fetch alone would leave the next
reader to rediscover that the read counter has the same shape.

## The shape

### 1. The allowance is pure, and lives in `Core`

A new `src/uk.osric.asp.Core/Pages/Allowance.cs`:

```csharp
public readonly record struct Allowance {
    public Allowance(int capacity, double perSecond, DateTimeOffset fullAt);
    public int Capacity { get; }
    public double PerSecond { get; }
    public bool TryTake(int units, DateTimeOffset now, out Allowance next);
    public int Available(DateTimeOffset now);
}
```

- It starts **full** at `fullAt`, because the burst is what a page may spend before anything is
  refilled.
- A refill is `perSecond × elapsed`, clamped at `Capacity`; a refusal answers `false` and returns
  the state unchanged, so a refusal never costs.
- `units` larger than `Capacity` can never be taken, however long it idles — a read is whole, and
  half a template is a misleading answer. That is the same property the count had.
- Time going backwards refills nothing and does not move the last instant, so a clock correction
  cannot mint a burst.
- `Capacity` must be at least one and `PerSecond` must be positive: both are constructor guards.
- It reads no clock, holds no lock and calls nothing, so a test drives it with two instants rather
  than a sleep (`code-fixtures-hold-no-deadline`). XML-documented, and added to
  `src/uk.osric.asp.Core/PublicAPI.Unshipped.txt`.

### 2. The Web budgets hold one under a lock

- `RenderFetchBudget` becomes `FetchBudget` (the file and the type), because it is now a render's
  allowance or a job's and the old name claimed the first only. `ReadBudget` keeps its name.
- Both are constructed from `(int capacity, double perSecond, TimeProvider clock)`, hold an
  `Allowance`, and answer `TryTake` / `TryCharge` by taking a `Lock`, asking the allowance at
  `clock.GetUtcNow()` and storing the answer. Two fields and a short section do not need anything
  cleverer (`Simplicity`).
- Both expose `Capacity`, `Available` and `PerSecond`; `Available` is a snapshot read under the same
  lock.
- One chain still shares one instance, one job holds one for its life (`pages-one-request-one-chain`).

### 3. The keys, and the burst

| Key | Shipped | Burst at `Pages:Render:TimeoutSeconds` (5) | Replaces |
| --- | --- | --- | --- |
| `Pages:Fetch:FetchesPerSecond` | 2 | 10 | `Pages:Fetch:MaxRequestsPerRender` (10) |
| `Pages:Read:SourceCharsPerSecond` | 104858 | 524290 | `Pages:Read:MaxSourceCharsPerRender` (524288) |
| `Pages:Jobs:FetchesPerSecond` | 2 | 10 | the job borrowing the render's 10 |
| `Pages:Jobs:SourceCharsPerSecond` | 104858 | 524290 | the job borrowing the render's 524288 |

- **One derivation.** `RenderOptions.BurstFor(double perSecond)` answers
  `(int)Math.Floor(perSecond × TimeoutSeconds)`, documented as "the count a rate spends in a whole
  render". The endpoint and the runner both call it; nothing else computes a capacity.
- **A burst below one stops the site.** A rate whose derived burst is zero would refuse every
  request forever rather than tighten a guard, so startup refuses it and names both keys and the
  smallest rate that works. It is checked for both fetch rates and both read rates, even though the
  read floor (1024 × 0.05 s = 51) cannot reach it, so a later change to either floor cannot make it
  silent.
- **Ranges are safety nets**: `FetchesPerSecond` 0.01–100 and `SourceCharsPerSecond` 1024–16,777,216.
  The high ends exist to refuse a typo, not to ration a deployment.
- **Retired keys are refused, not ignored.** A configuration that still carries
  `MaxRequestsPerRender` or `MaxSourceCharsPerRender` fails at startup naming the replacement, the
  way `Program` already refuses a retired `Pages:Storage` section: an operator who kept the key
  believes it still bounds something, and it does not.
- **One report.** The cross-key burst check records into the root reader with `Fail`, before
  `settings.Complete()`, so a bad rate is listed beside any other problem in the same start.

What a page author notices: today's ten is now a burst, and the same render may refill at 2/s while
it still runs, so a page that spends its whole deadline can attempt up to twenty rather than ten.
The deadline is unchanged and still ends the render. This is the one place the item's sentence
("a render keeps the count it has today") cannot be exact — keeping the burst and keeping the total
are different numbers once the allowance refills — and it should be said in `/help` rather than
discovered.

### 4. The charge moves after the validation

- `OutboundFetcher` grows `TryValidate(request, caller, credential, out reason)`, the shape checks
  `TryPrepareRequest` already performs, extracted so there is one copy.
- `IPageFetcher` grows `TryValidate(request, pagePath, out reason)`, implemented by `PageFetcher` as
  that check plus the credential rules it already owns: unknown name, not bound to this page, and a
  header the credential also sets.
- `PageHttpFetch.SendAsync` and `StreamAsync` ask it first. A request it refuses answers
  `InvalidRequest`, records `PageMetrics.FetchInvalid`, costs nothing, and never reaches the
  transport. A request it accepts takes one unit and is attempted, whatever the outcome: a network
  failure or a timeout still cost one, because the request was made.
- `FetchAsync` and `StreamAsync` keep validating as they do today. The second check is the same pure
  code, so the two cannot drift, and the second run is what produces the value the page reads.
- The `HEAD`-cannot-POST rule stays in `PageHttpFetch` and stays before the charge: it is a fact
  about the render, not about the request.

### 5. The read side

- `ReadBudget` charges a source read's length exactly as today, whole or not at all; the read either
  fits or is refused, so a page never renders half a template.
- Only source reads charge — a listing, a search and a body write never did, and this item does not
  add a meter where none existed.
- The refusal message names the rate, the burst and `Pages:Read:SourceCharsPerSecond`, as the count's
  message names the count today.
- The budget is created once per render and once per job. A job's read rate is its own.

### 6. The job's own allowance

- `PageJobOptions` gains `FetchesPerSecond` and `SourceCharsPerSecond`, read from `Pages:Jobs` under
  the same rules as everything else there.
- `PageJobRunner` builds both budgets from those rates, with the burst from
  `RenderOptions.BurstFor`, and takes `RenderOptions` and `TimeProvider` from the container beside
  the environment it already receives.
- Nothing else about a job changes: its silence clock, its ceiling, its journal and its queue are
  untouched.

### 7. The numbers are readable

`Capacity`, `Available` and `PerSecond` are documented on both budgets with what each means — the
most one burst may spend, what is left at this instant, and what is refilled each second. No page
capability is added: [`the-site-is-one-type`](../backlog/the-site-is-one-type.md) is the door, and
it can only publish these figures if they come from the object that enforces them, which is why they
are public on the budget rather than formatted in the refusal.

### 8. The refusal keeps its word

- The value stays `HttpFetchFailure.TooManyRequests`; the outcome metric stays `PageMetrics.FetchLimit`.
- The log line names the rate, the burst and the key: "more than 2 fetches per second
  (`Pages:Fetch:FetchesPerSecond`), 10 at most in one burst".
- The read's exception names the rate, the burst and `Pages:Read:SourceCharsPerSecond`, and keeps
  telling the author to raise the rate rather than read a smaller revision.

## What changes

| Where | Change |
| --- | --- |
| `src/uk.osric.asp.Core/Pages/Allowance.cs` | New: the pure bucket above |
| `src/uk.osric.asp.Core/PublicAPI.Unshipped.txt` | The new public API, documented |
| `src/uk.osric.asp.Web/Pages/RenderFetchBudget.cs` → `FetchBudget.cs` | Renamed and rebuilt over `Allowance`; exposes capacity, available and rate |
| `src/uk.osric.asp.Web/Pages/ReadBudget.cs` | Rebuilt over `Allowance` of characters; same surface, plus the three figures |
| `src/uk.osric.asp.Web/Pages/PageFetchOptions.cs` | `FetchesPerSecond` in, `MaxRequestsPerRender` out and refused |
| `src/uk.osric.asp.Web/Pages/PageReadOptions.cs` | `SourceCharsPerSecond` in, `MaxSourceCharsPerRender` out and refused |
| `src/uk.osric.asp.Web/Pages/RenderOptions.cs` | `BurstFor(double)`, the one derivation |
| `src/uk.osric.asp.Web/Pages/PageAllowanceRules.cs` | New: the burst-below-one check, recorded in the one startup report |
| `src/uk.osric.asp.Web/Pages/PageHttpFetch.cs` | Validate, then charge; the rate's message |
| `src/uk.osric.asp.Web/Pages/IPageFetcher.cs`, `PageFetcher.cs` | `TryValidate`, one seam a page's charge can ask |
| `src/uk.osric.asp.Web/Pages/OutboundFetcher.cs` | `TryValidate` extracted from `TryPrepareRequest` |
| `src/uk.osric.asp.Web/Pages/PageEndpoint.cs` | Builds both budgets from the rates, the burst and a `TimeProvider` |
| `src/uk.osric.asp.Web/Pages/PageCatalogScope.cs` | The read refusal names the rate and the key |
| `src/uk.osric.asp.Web/Pages/PageJobOptions.cs` | Both job rates |
| `src/uk.osric.asp.Web/Work/PageJobRunner.cs` | Builds the job's two budgets from its own rates |
| `src/uk.osric.asp.Web/appsettings.json` | Four rates in, two counts out |
| `src/uk.osric.asp.Web/Program.cs` | Reads the rates, runs the burst check into the one report |
| `src/uk.osric.asp.Web/Pages/Seeds/help` | The rate, the burst, the job's own rate and the read allowance |
| `docs/pages.md`, `deploy/README.md` | The same facts, and the deployment key names |
| `docs/decisions.md` | The entry, written by the merge |

## Tests

Written first, each failing before the change (`code-fixtures-hold-no-deadline` for the clock).

**`test/uk.osric.asp.Core.Tests/AllowanceTests.cs`** — the bucket, with two instants:

- **A fresh allowance is full.** Ten units at the start instant, and the eleventh is refused.
- **It refills at its rate.** Two per second: refused a whole unit a quarter second later, accepted
  half a second later.
- **A refusal costs nothing.** A refused take, then the same instant again, leaves the same units.
- **It does not bank.** An hour idle holds the capacity, not 7,200 units.
- **It never takes more than its capacity.** A take larger than the capacity is refused however full
  the bucket is.
- **It starts full, not empty.** A render's first request is never refused for want of a refill.
- **A clock that goes backwards mints nothing.** An earlier instant refills nothing and is not
  recorded.
- **Nothing invalid is constructed.** A capacity below one and a rate of zero are refused.

**Options** — in the existing `PageFetchOptions`/`PageReadOptionsTests`/`PageJobOptionsTests`:

- **The shipped values are the documented ones**, including the two job rates.
- **Configured rates are read**, fractional fetch and whole-character read.
- **A retired key stops the site** naming its replacement, each of the two.
- **A burst below one stops the site**, naming the rate, `Pages:Render:TimeoutSeconds` and the
  smallest rate that works.
- **A rate outside its range stops the site**, below the floor and above the ceiling.
- **A burst below one is caught across the two sections**, naming both keys and the smallest rate that
  works (`PageAllowanceRulesTests`).
- **`BurstFor` is the rate times the render's deadline**, floored — the one test of the derivation,
  on the shipped 5 seconds. A test that needs a capacity of one builds the budget directly rather
  than raising `TestOptions.FixtureRender`, which is 60 seconds for a different reason.

**The fetch edge** — `PageFetchRenderTests` and `PageFetcherTests`:

- **A refused request takes no unit.** An unknown credential, a URL with credentials, and a header
  the deny list refuses each answer `InvalidRequest` and leave the budget's available count
  untouched; the third asks the transport for nothing.
- **An attempted request costs one whatever happens.** A fetch that fails in the network costs one.
- **A stream costs one.** Opening a stream spends one unit, whether or not a line is read.
- **A stream refused before the transport costs none.**
- **Sibling layers share one bucket.** The existing two-layer test, rebuilt on a capacity of one.
- **A refused request's message names the rate and the key.** The log line asserts both, and the
  page still reads `TooManyRequests`.

**The read edge** — `PageFetchRenderTests` or a small read test beside it:

- **A read that fits charges its length.** Two reads that together exceed the capacity refuse the
  second whole, and the first is not rolled back.
- **A refused read costs nothing.**
- **The refusal names the rate, the burst and the key.**

**Wiring** — `PageHarness` and the job tests:

- **A job gets its own rates and refills.** A job whose rate allows one fetch, advanced by the
  harness's clock, fetches again; a render's bucket is untouched by it.
- **A job's read budget refills the same way.**
- `PageHarness` builds its budgets through one helper, so the fetch tests state a capacity rather
  than an option value that no longer exists.

## Docs

- `/help`'s fetch section: the rate and the burst, that a job has its own rate, that a refused
  request costs nothing and a stream costs one, and that a page spending its whole deadline can
  refill while it runs.
- `/help`'s catalog section: the read allowance is a rate over source characters, shared by the
  chain, and a job has its own.
- [pages](../pages.md#page-contract): the same in the outbound-fetch bullet and the budget sentence.
- [deployment](../../deploy/README.md): `Pages__Fetch__FetchesPerSecond` and the read key replace the
  two count names in the host's notes.
- The merge writes **pages-allowances-are-a-rate** and cites it from the plan, the two options
  records and the help page.

## Not in scope

- **Publishing the numbers to a page.** The live capability is
  [`the-site-is-one-type`](../backlog/the-site-is-one-type.md); this item makes the figures readable
  inside the host and leaves the door open.
- **The status page.** `/admin/status` is a shell endpoint with its own client,
  `Metrics:QueryTimeoutSeconds` and a 15-second cache; it is not a page and this plan does not
  special-case it — no exempt host, no page-scoped rate, no loopback rule. When `pages-page-first`
  makes it a page, that step decides what a page with a fan-out of forty is given, and it will need
  a configured allowance rather than a hole in this one.
- **The assistant's `fetch` tool.** It draws no bucket today and this change leaves it that way: it
  is bounded by `Assistant:MaxToolCalls` and the run's own deadline. It is named here so the next
  reader knows it was considered.
- **The headers.** What a page may send is
  [`outbound-headers-are-an-allow-list`](../backlog/outbound-headers-are-an-allow-list.md), which
  leaves the charge to this item.
- **The job's other clocks and limits.** `Pages:Jobs` already bounds a job's silence, duration,
  messages and journal; this adds two rates beside them.
- **Store writes, record statements and catalogue listings.** Only their size and per-call row
  limits exist; there is no lifetime count to convert. If `Bounds` is to promise a fan-out can be
  sized, the listing count is the number it cannot read — an idea for the backlog, not this plan.

## Risks and what I am unsure of

1. **Ten becomes twenty for a page that runs its whole deadline.** Keeping the burst and keeping the
   total are different numbers. The plan keeps the burst (nothing that works today is refused) and
   lets the rate refill while the render runs. The owner should see this before saying yes.
2. **The job's shipped fetch rate is two per second**, which is up to 64,800 requests across the
   nine-hour ceiling. That is parity with today's burst, which is what "nothing gets worse" asks
   for; the quieter alternative is 0.2/s with a burst of one, which refuses a worker's first fan-out
   of ten. The key exists so a deployment can choose; the plan ships parity.
3. **A burst is also the largest single read.** A read is whole, so a template larger than the
   capacity can never be read at any rate. Today's 524,288 has the same property; nothing here makes
   it worse, and nothing here fixes it either. If the store can hold a page larger than that, that
   is its own item.
4. **The fetch charge now asks the fetcher before taking a unit.** That is one extra validation pass
   per request, which is pure and cheap, and it is the only way to keep "a refused request costs
   nothing" without moving the credential rules into the budget.

## Open questions

None. The owner settled the fetch shape on 2026-11-22 — a token bucket with today's capacity, a rate
derived from the render's deadline, and a per-job rate with the job's duration as its only ceiling —
and directed on 2026-11-23 that the read allowance be converted in the same plan. The shipped job
rates and the ten-become-twenty trade are stated above for the owner's yes.

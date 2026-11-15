<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Assistant balance metric

| | |
| --- | --- |
| Status | Planned, not started — waiting on the owner's yes (DEC-281) |
| Size | Small — one pure parse, one value on the client's answer, one sampler, one gauge, one panel and one key, with the tests for each |
| Area | Core (assistant), Web (assistant, metrics, configuration) |
| Spike | None. `/user/balance` is already read on every conversation page view, its JSON is already parsed, and the observability shape is the one `PageStorageSampler` already uses |
| Backlog | [IDEA-032](../jobs/IDEA-032.md) |
| Decisions | D1–D6 below; numbered from [decisions](../decisions.md) when the work merges |

## The short version

The conversation page shows the provider account's balance, and that is the only place it exists.
This change gives it history:

- **One instrument.** `assistant.balance`, a gauge in `uk.osric.asp.Assistant`, unit `{credit}`, tagged
  `currency`. One line per currency on `/status`, and the panel is a line chart because a gauge is a
  level already (DEC-180, DEC-183).
- **One sample on a clock.** A sampler reads the balance at startup and then every
  `Assistant:BalanceSampleSeconds`; the gauge reports the last snapshot, so a scrape never causes an
  outbound call and a slow provider never holds one.
- **One reason, logged once.** The client already knows why it has no balance; it starts saying so as
  a value instead of a warning per attempt, so a provider with no balance route costs one log line
  and not one a minute.
- **Nothing changes on the page.** The figure it shows, the sentence for its absence and the moment
  it is fetched are all as they are.

What the chart answers is not "what is the number" — the page does that — but *how fast is it going
down*, *what did this week cost*, and *did the price move*, because credit per second beside
`assistant.tokens` per second is the effective price.

## Goals

1. The provider's credit is a level on `/status`, with a history and a slope.
2. A provider that cannot answer costs one log line and one empty panel, not a warning a minute and
   not a stopped host.
3. The sample is a clock's, not a page view's, so the history is the spend rather than a diary of the
   owner's visits.
4. Nothing about the conversation page changes: same figure, same wording, same deadline.
5. Every new value is configuration, every new rule is a test, and the drift tests that hold the
   catalogue to the meters keep holding it.

## Non-goals

- **Money derived from tokens.** A per-token price is the provider's and may change without notice;
  the balance is the provider's own figure.
- **The granted and topped-up split as series.** The page shows it; the chart shows the total, which
  is the one that runs out.
- **Alerting.** A low balance is [OPS-05](../jobs/OPS-05.md)'s question and needs a notification
  channel, not a chart.
- **Per-run cost attribution.** The balance is an account's, not a run's; `assistant.tokens` already
  attributes text to a run, and dividing the two is a reader's arithmetic.
- **Sampling on demand.** There is no route, no button and no page that asks for a fresh sample. The
  page keeps its own fetch, and it is not the sampler's.

## What exists today

| Piece | Where | What it does |
| --- | --- | --- |
| `AccountBalance`, `CurrencyBalance` | `Core/Assistant/AccountBalance.cs` | The provider's figures, as the text it sent: a currency, a total, granted and topped up |
| `AccountBalanceJson.Parse` | `Core/Assistant/AccountBalanceJson.cs` | Reads the body without any dependency but the text |
| `IAccountBalanceClient` / `AccountBalanceClient` | `Web/Assistant/` | GETs `/user/balance` with the assistant's key and user agent, bounded by `Assistant:BalanceTimeoutSeconds`; every failure is a null and a warning |
| `AssistantEndpoint.WriteAccountBalanceAsync` | `Web/Assistant/AssistantEndpoint.cs` | Renders the one figure the page shows, or the sentence for its absence |
| `AssistantMetrics` | `Web/Assistant/AssistantMetrics.cs` | The meter, its ten instruments and the inventory the catalogue is checked against |
| `PageStorageSampler` | `Web/Storage/PageStorageSampler.cs` | The precedent: a background sample into a cached snapshot that an observable gauge reports, with the previous value kept when a sample fails |
| `StatusCatalogue`, `PrometheusNames` | `Web/Metrics/` | One panel per instrument; a gauge is queried with `max(...)` and drawn as a line |
| `AssistantOptions` | `Web/Assistant/AssistantOptions.cs` | Every assistant policy value, read through `ConfigurationReader` with no fallback |

The one thing this change cannot take from the sampler precedent is its quietness: the storage
sampler decides what to log, while the balance client logs a warning for every failure itself. On a
clock, that is the difference between a log line and a log flood.

## Design

### D1 — A gauge, because a balance is a level

One instrument, `assistant.balance`, in `AssistantMetrics.Instruments`, kind `Gauge`, unit `{credit}`.
`PrometheusNames.Family` adds no suffix for an annotated unit, so the family is `assistant_balance`,
and the existing drift tests take the rest from the inventory: one panel per instrument, the kind
decides the chart, the query names a family in the inventory, and a gauge is read as a level.

The panel is a line chart, titled "Account credit", its value axis `credit`, and it splits by
currency: `max by (currency) (assistant_balance)`. `StatusCatalogue.Line` grows a `by:` argument,
used only when the instrument is a gauge, so `max by (currency)` replaces the bare `max` that would
otherwise merge two currencies into one line. Each currency is then a series, labelled at the end of
its line, which is how every other chart in the catalogue names its series without colour (DEC-182).

Why a label rather than a currency in the name: the provider decides which currencies it reports, and
it reports the ones the account is held in — one or two, never visitor input — so the label is
bounded by the provider's own list, the same way `model` is bounded by configuration.

### D2 — A sample on a clock, not on a page view

`AccountBalanceSampler : BackgroundService`, registered beside `RunWorker` and
`RunJournalCleanupService`, inside the `if (assistantEnabled)` block: an assistant that is off has no
client, no sampler and no instrument, and its panel draws "No data" like every other Assistant panel.

It samples once at startup and then on a `PeriodicTimer` of `Assistant:BalanceSampleSeconds`, and it
takes the client from a scope each time, because `AddHttpClient<IAccountBalanceClient, …>` registers a
typed client and the handler pool is what makes a per-sample client cheap.

The gauge reads a snapshot the sampler wrote, the way `ObserveStorage` reads `_storage`: a field
written under `Volatile.Write` and read under `Volatile.Read`, with the callback doing no I/O at all.
A scrape that arrives between samples reports the last sample, and a scrape that arrives before the
first reports nothing, so the panel says "No data" rather than drawing a zero.

Sampling at startup matters for the panel: a deploy shows a level immediately, rather than after the
first interval, which is the difference between a chart and a mystery.

### D3 — Failure is a value, and the sampler logs the change

`IAccountBalanceClient.GetAsync` returns `AccountBalanceRead`: the balance, or nothing and why. The
reasons are the ones the client already distinguishes, and they are a closed set:

| Reason | When |
| --- | --- |
| `ok` | A balance was parsed |
| `unsupported` | The provider answered `404`, so it has no balance route — a local model, or a provider that bills differently |
| `error` | Any other non-success status |
| `timeout` | `Assistant:BalanceTimeoutSeconds` expired before the provider answered |
| `unreachable` | The socket failed |
| `invalid` | The body was not the balance JSON |

The client stops logging entirely; the reason is the diagnosis and the caller decides what it is worth.
The conversation page ignores it and keeps its one sentence for "no balance", so nothing a reader sees
changes. The sampler logs when the reason changes and stays quiet while it repeats, so a provider that
has no balance route writes one line at startup and nothing until it changes; `unsupported` is logged
at information, because a provider without the route is a fact rather than a fault, and the rest at
warning.

This is the same shape as `PageMetrics`' fetch outcomes and as the house rule that every failure is a
value: a client that returns a status can be used twice without one caller deciding the other's
logging.

### D4 — The last good sample is kept, and a stale one is the log's problem

A failed sample keeps the previous value, exactly as the storage gauges keep theirs, and the sampler
logs it once per change of reason (D3). A dead sampler therefore looks like a flat line, which is the
same hole the storage gauges have and accept.

The alternative is an `assistant.balance.sample.age` gauge: a level that climbs when sampling stops,
one panel more, and an answer to "is the flat line real". It is rejected for now because one rule for
every level on the page is worth more than one honest one, and because the sampler's own log line is
where a stuck sample is diagnosed. If a second level ever needs the same guard, it becomes a panel for
all of them rather than one for each.

### D5 — The parse belongs to `Core`

`CurrencyBalance.Total` is the provider's text — `"110.00"` — and a gauge takes a number. The parse is
an invariant-culture decimal in `Core`, beside the JSON reader that produced the text:

```csharp
public static class AccountBalanceAmounts {
    public static bool TryRead(string total, out decimal amount);
}
```

Two refusals worth stating: a figure that is not a number is skipped rather than recorded as zero,
because a zero on this chart reads as "the credit is gone"; and a negative figure is recorded as it
came, because a provider that owes the account money is telling the truth and the chart should show
it. The sampler records only what it can read; a currency it cannot read is a provider's change and
the log line from D3 is not the place for it — the sampler logs a warning naming only the currency
code, never the body.

### D6 — Configuration: one key

`Assistant:BalanceSampleSeconds`, read with `reader.Number("BalanceSampleSeconds", recommended: 60,
minimum: 30, maximum: 3600)`, added to `appsettings.json` beside `BalanceTimeoutSeconds`.

It is policy and not an invariant: a cadence for an outbound call is a quota (DEC-282), it is what
decides how much history a day has and how much the provider is asked, and the owner may reasonably
want a quieter site. The range is a safety net rather than a ration: thirty seconds is as often as is
polite to a provider, an hour is the widest interval that still shows a day's shape, and the shipped
minute matches the storage sampler's own rhythm so the page's two kinds of level are sampled alike.
Nothing in the code has a fallback, and a missing key stops the host with the usual report.

## Considered and rejected

| Alternative | Why not |
| --- | --- |
| An observable gauge that fetches when Prometheus scrapes | A gauge callback does no I/O, here as for storage: a scrape would wait on the provider, and a scrape timeout would be recorded as "no data" rather than as a slow provider |
| Recording the balance only when the conversation page is opened | The page's fetch is free to reuse, but the history becomes a diary of visits: a batch of runs between two visits would draw as one step at the second |
| Three gauges — total, granted, topped up | Three instruments are three panels, and the only figure that runs out is the total |
| A counter of credit spent, derived from runs | It needs a price list, and the provider publishes none that is stable; the balance needs no assumption |
| An age-of-sample gauge (D4) | One panel more for one figure; the storage gauges would then want the same, which is a different job |

## Components

| File | Change |
| --- | --- |
| `Core/Assistant/AccountBalanceAmounts.cs` | New: the pure parse (D5) |
| `Core/Assistant/AccountBalanceRead.cs` | New: a balance or nothing, and the reason (D3) |
| `Core/PublicAPI.Unshipped.txt` | The two new surfaces |
| `Web/Assistant/AccountBalanceClient.cs`, `IAccountBalanceClient.cs` | Return `AccountBalanceRead`; the client stops logging |
| `Web/Assistant/AccountBalanceSampler.cs` | New: the timed sample and its snapshot (D2) |
| `Web/Assistant/AssistantMetrics.cs` | The instrument, its observable gauge and `RecordBalance` |
| `Web/Assistant/AssistantOptions.cs` | `BalanceSampleSeconds` (D6) |
| `Web/Assistant/AssistantEndpoint.cs` | Reads `.Balance` from the answer; nothing else moves |
| `Web/Metrics/StatusCatalogue.cs` | The panel, and `by:` on `Line` for a gauge (D1) |
| `Web/Program.cs` | Registers the sampler with the other assistant hosted services |
| `Web/appsettings.json` | `Assistant:BalanceSampleSeconds` |
| `test/uk.osric.asp.Core.Tests/AccountBalanceAmountTests.cs` | New: a figure read, a figure refused |
| `test/uk.osric.asp.Web.Tests/AccountBalanceClientTests.cs`, `StubAccountBalanceClient.cs` | One case per reason (D3) |
| `test/uk.osric.asp.Web.Tests/AssistantMetricsTests.cs` | The inventory grows; a balance records one measurement per currency |
| `test/uk.osric.asp.Web.Tests/AssistantOptionsTests.cs` | Shipped value, override, refusal (D6) |
| `test/uk.osric.asp.Web.IntegrationTests/MetricNamesTests.cs`, `StubAccountBalanceClient.cs` | Records the new instrument; the live body carries `assistant_balance` |
| `test/uk.osric.asp.Web.IntegrationTests/AssistantBalanceTests.cs` | The page's two cases are unchanged, and prove D3 changed no sentence |
| `docs/metrics.md`, `docs/assistant.md`, `docs/jobs/README.md` | The instrument, the panel count, the sampler, the job |

## Tests

Written first, one behaviour each:

- **`AccountBalanceAmountTests`** — `"110.00"` reads as 110.00; a trailing figure reads; `"abc"` and an
  empty string are refused; a negative figure reads.
- **`AccountBalanceClientTests`** — a 200 with the balance is `ok` with the figures; a 404 is
  `unsupported`; a 500 is `error`; a timeout is `timeout`; a socket failure is `unreachable`; a body
  that is not the balance is `invalid`; and no case writes a log entry (the reason is the record).
- **`AccountBalanceSamplerTests`** — a sample records one measurement per readable currency; a failed
  sample changes no value; a changed reason is logged once and a repeated one is not.
- **`AssistantMetricsTests`** — the inventory and the meter still agree; `RecordBalance` emits one
  measurement per currency, tagged `currency`, and none before a sample.
- **`StatusCatalogueTests`** — unchanged, and passing: the kind decides the chart, the query names a
  family in the inventory, and a gauge is read as a level.
- **`AssistantOptionsTests`** — the shipped 60, an override, and a refusal naming the key, the range
  and the recommendation.
- **`MetricNamesTests`** — records the new instrument and reads the live `/metrics` body for
  `assistant_balance`.
- **`AssistantBalanceTests`** — the page's figure and its absence, unchanged.

## Phases

Each phase ends with a green `make check`.

### Phase 0 — the reason and the parse (half a day)

- `AccountBalanceAmounts`, `AccountBalanceRead`, the client's return type, the two stubs and the
  endpoint's `.Balance`.
- `AccountBalanceAmountTests`, the six client cases, and the page's two integration cases, unchanged.

**Done when** the client answers a reason, the page's tests pass untouched, and nothing is logged by
the client.

### Phase 1 — the sample and the panel (half a day)

- `AssistantMetrics`' gauge and `RecordBalance`, `AccountBalanceSampler`, its registration, the key and
  its `appsettings.json` entry.
- The panel, and `by:` on `Line`.
- `AccountBalanceSamplerTests`, `AssistantMetricsTests`, `AssistantOptionsTests`, `MetricNamesTests`.

**Done when** `/status` draws a line per currency after a startup sample, and the catalogue and name
tests are green.

### Phase 2 — documents (an hour)

- [metrics](../metrics.md): the instrument row, the panel count sentence, and what the level answers.
- [assistant](../assistant.md): the balance paragraph gains the sample.
- [jobs](../jobs/README.md): IDEA-032 in the table and in "not in this programme".
- `/help` is unchanged: this adds no page-authoring capability.

## Documentation and decisions

- `docs/metrics.md` — an `assistant.balance` row in the Assistant instrument table, `ten Assistant
  panels` becoming eleven with the reason, and a paragraph on why a level that only falls is worth a
  panel: the slope is the spend, a step up is a top-up, and it is read beside `assistant.tokens` as the
  effective price.
- `docs/assistant.md` — one sentence where the balance panel is described: the conversation page
  reads it afresh for its own display, and the sampler reads it on its own clock for the chart.
- `docs/decisions.md` — D1–D6 numbered on merge (DEC-281).

## Risks

| Risk | Answer |
| --- | --- |
| An outbound call every minute, forever | One GET with no tokens; the interval is a key; the reason is logged once per change; a provider without the route answers a 404 and costs one line |
| The provider meters the balance call | Confirm before shipping. If it is metered the key is the knob, and the shipped minute is the only value that matters |
| A dead sampler looks like a flat line | The sampler's log line, and the same hole the storage gauges have; D4 records why an age gauge is not built |
| The chart is nearly flat and therefore dull | That is the answer: a flat line is "no spend", and the slope over a day or a week is the question. The window tokens are the page's, not this panel's |
| The provider reports a currency the chart has never seen | It becomes a new line, which is the truth; the label is the provider's own list, not visitor input |
| The page and the sampler disagree by a minute | They are the same measurement taken at different times, and the page states its own figure as it always has |
| A price change is invisible until the balance drops faster | That is exactly what the panel is for: credit per second against tokens per second is the price, and it moves on the day the provider's does |

## Delivered when

`/status` draws the provider's credit as a line per currency for an enabled assistant, sampled at
startup and on the configured clock; the conversation page's figure, wording and fetch are unchanged;
a provider with no balance route logs once and draws "No data"; `make check` is green; and the
catalogue's drift tests hold the new instrument to its panel.

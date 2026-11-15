<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-032 — The provider's credit is on a page, not on a chart

| | |
| --- | --- |
| Size | S |
| Area | Core, Web (assistant, metrics, configuration) |
| Location | `src/uk.osric.asp.Web/Assistant/AccountBalanceClient.cs`, `src/uk.osric.asp.Web/Assistant/AssistantMetrics.cs`, `src/uk.osric.asp.Web/Metrics/StatusCatalogue.cs`, `src/uk.osric.asp.Web/appsettings.json` |

## Idea

`AccountBalanceClient` already GETs the provider's `/user/balance`, and `AccountBalanceJson` already
parses it, but the figure it returns exists only inside one page. The site spends money on every
assistant run and measures everything about that run except its cost: `assistant.tokens` says how
much text was bought, and nothing says what it cost or what is left.

A level on `/status` answers three things the page cannot:

- **How long the credit lasts.** The slope is the spend rate, so a steep slope against a low level is
  a deadline rather than a surprise.
- **What a period cost**, without opening the conversation page to look.
- **Whether the price moved.** Credit per second beside tokens per second is the effective price; a
  provider that changed its prices, or a switch to a dearer model, shows up as the two lines drifting
  apart, which no token count on its own can show.

The work is small because the hard half is already done and proved: the client, its timeout, the
response parser and the failure rules. What is missing is a sample on a clock, one gauge, one panel
and one configuration value. [The plan](../plans/assistant-balance-metric.md) is written.

## Sketch

- `assistant.balance`, a gauge on `uk.osric.asp.Assistant`, unit `{credit}`, tagged `currency`. One
  line per currency, and a line chart because a gauge is a level (DEC-180, DEC-183).
- A background sampler reads it at `Assistant:BalanceSampleSeconds` — a cadence for an outbound call
  is a policy value, so a key with no fallback (DEC-282) — and the gauge reports the last snapshot,
  the way `PageStorageSampler` reports the database's size. The gauge's callback does no I/O.
- Failure stays a value. The sampler logs when the answer changes rather than once per interval, so a
  provider with no balance route costs one line and not one a minute.
- Reading it: the level is the credit, a step up is a top-up, and `-deriv(assistant_balance[1d])` is
  the last day's spend.

## Not in scope

- **Money derived from tokens.** The price list is the provider's and can change unannounced; the
  balance is the provider's own figure and needs no assumption about how a run was charged.
- **The granted and topped-up split as series.** The conversation page already shows it; three
  gauges would be three panels for one answer, and the total is what a chart is for.
- **Alerting on a low balance.** That is [OPS-05](OPS-05.md)'s question, and it wants a notification
  channel, not another panel.

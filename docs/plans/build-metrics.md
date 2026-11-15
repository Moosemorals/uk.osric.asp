<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Build timings

| | |
| --- | --- |
| Status | Proposed 2026-11-15 — waits for the owner (`product-steps-wait-for-the-owner`). Nothing below is built. |
| Size | Medium — one pure parse and its target list in `Core`, one meter, one guarded endpoint, one Makefile wrapper and its records, and the panel that draws them |
| Area | Core (metrics), Web (metrics, configuration, shell), Makefile, deploy (one secret file and its seeding) |
| Spike | None. The scrape is a pull from the app's existing exporter, `PageStorageSampler` is the precedent for an in-process level, and `ViewTokenStore` is the precedent for a fixed-time secret comparison |
| Backlog | [WHIM-04](../jobs/WHIMS.md) — promoted here; its line goes when this merges |
| Decisions | Five names intended, below; taken at the merge (`product-plan-before-building`) |

## The short version

The build knows how long it took, and the only record is a terminal someone was watching. This change
gives those durations a history:

- **Measure the make targets.** A small wrapper times a target, appends one JSON record to
  `artifacts/build-metrics/records.jsonl`, and — when the build is configured to push — posts it. A
  failed target is recorded locally but not posted.
- **Push to the app, not to Prometheus.** The app already owns the `/metrics` export Prometheus
  scrapes. The push route validates a record and sets an in-process gauge, and Prometheus picks it up
  on its next scrape. There is no Pushgateway and no remote-write receiver: one scrape target, one
  exporter, one place where the shape is checked.
- **Authorise it with one token.** `BuildMetrics:Token` is an optional configuration value presented
  in `X-Build-Metrics-Token` and compared in fixed time. Absent means the route is not mapped at all;
  it is not a session, not the assistant's view token, and not network position.
- **Draw one line per target.** `build.duration` is a gauge tagged `target`, and the Build panel is a
  line chart of `max by (target)`. It answers "is `make check` getting slower" rather than "what did
  this one run take", which is the question a terminal already answers.

What a scrape sees is the last **successful** run of each target. A failure is a fact for the
journal and the local spool, not a shorter-looking line on the chart.

## Goals

1. `make check`, `make deploy`, `dotnet format`, every test suite, `make image` and the ssh phase of a
   deploy each leave a duration that outlives the terminal.
2. A target is measured once, by one wrapper, so the numbers cannot be assembled differently in two
   places.
3. The push is safe to leave on a build machine: it needs no browser, no owner session and no ssh
   connection; a leaked token can pollute one chart and nothing else.
4. A push that cannot happen (no URL, no token, no network) never fails a build.
5. Every new value is configuration, every new rule is a test, and the catalogue's drift tests keep
   holding the instruments to their panels.
6. Nothing a page author can do changes: no `IContext` member, no capability, no editor action, so
   `/help` is untouched.

## Non-goals

- **A build dashboard page of its own.** The Build group is one panel on `/status`, where every other
  instrument already is.
- **Per-machine, per-branch or per-commit labels.** There is one builder. A `host` label would
  separate machines that should not be compared and multiply the series; the branch and commit are
  already in the changelist and the log.
- **A CI runner.** Running the gate unasked is [OPS-03](../jobs/OPS-03.md); this plan only makes the
  push reachable from whatever eventually runs it.
- **Alerting on a slow build.** A notification channel is [OPS-05](../jobs/OPS-05.md).
- **Retrying or failing a build when the push fails.** The build's exit code is the command's.
- **Prometheus remote-write or a Pushgateway.** See "Where the numbers go".
- **Timing `make build`, `make restore` or the mutation targets.** They are reachable with one more
  wrapper line and one name in the list, but the user's list does not ask for them, and a stamp that
  does nothing (an up-to-date `build`) is a record that says nothing.

## What exists today

| Piece | Where | What it does |
| --- | --- | --- |
| Make targets | `Makefile` | `check`, `deploy`, `format`, `format-check`, the `test-*` suites, `publish`, `image`, `image-check`, the gate's own parts, and the ssh recipe inside `deploy` |
| `PageMetrics`, `AssistantMetrics` | `Web/Pages`, `Web/Assistant` | A `Meter`, an inventory of instruments, and the observable-gauge pattern for a level already in `PageStorageSampler` |
| `/metrics` | `Program` | OpenTelemetry's Prometheus exporter, scraped by `prometheus` over the rootless network; Caddy answers 404 on the public origin |
| `StatusCatalogue` | `Web/Metrics` | One panel per instrument, the kind decides the chart, and the drift tests prove the inventory and the panels agree |
| `ViewTokenStore`, `ViewAuthenticationHandler` | `Web/Authentication` | Fixed-time secret comparison, and a bearer scheme whose guard refuses POST |
| `ConfigurationReader` | `Web/Configuration` | Every policy value read once, every problem collected, no fallback |
| `deploy/uk.osric.asp.container` | `deploy` | The app quadlet; `EnvironmentFile=%h/.config/uk.osric.asp/assistant.env` is the precedent for a secret the image does not carry |
| `MAKE_TOOL`-free recipes | `Makefile` | `image_command`'s comment records why `$(MAKE)` must not appear in a recipe: make runs such a line even under `-n` |

## Where the numbers go

The first question is not how to authenticate the push but what the push talks to.

| Option | What it costs | Why not |
| --- | --- | --- |
| **Push to the app (chosen)** | One route, one meter, one token | The app is already the exporter Prometheus scrapes; the record is checked and bounded once, and the panel is built from the same inventory as every other |
| Prometheus remote-write receiver | `--web.enable-remote-write-receiver`, a snappy-compressed protobuf payload, and no authentication on the receiver | A new wire format and a new flag for one producer; the app's validation and the catalogue's inventory are bypassed; the receiver would accept any series from anything on the network |
| Pushgateway | A third container, a volume, a retention rule and the pushgateway's last-push-per-group semantics | The deployment deliberately has two rootless containers; a store whose semantics differ from Prometheus's is a new thing to operate for one gauge |
| A text file plus the node exporter's textfile collector | `node_exporter` is not deployed | A fourth service for a format Prometheus already gets from the app |
| A page that writes the metric | A page cannot reach `PageMetrics`, and a page cannot hold a secret | The shell owns both, and the route must answer a machine with no browser |

The app is a relay, not the store: it holds the last value per target, Prometheus keeps the history,
and a restart loses the level until the next successful build. That is the same lifetime the storage
gauges have, and it is why the chart is read from Prometheus rather than from the app.

## Authorising the push

The build has no browser, no owner session and no assistant run. The options, in the order they were
considered:

| Option | Authenticates by | Why not |
| --- | --- | --- |
| The owner's cookie | A signed-in session | A build machine has no login form and must not keep a password; a CI has no owner |
| `Authorization: Bearer` and the `View` scheme | A token minted in memory per run | A build is not a run; `ViewGuard` refuses every view `POST` and an unknown bearer is a `401` before the endpoint; reusing the scheme would widen what a view token can do |
| Network position: loopback only, Caddy `404`, push over ssh | The host's ssh key | Genuinely simpler, and the runner-up. Rejected because every measured target would need a host (or a separate push step), a local `make check` would depend on the deploy host being reachable, and a future CI would need the key. It is one Caddy line and one `ssh` if the token ever proves wrong |
| A shared token in a dedicated header (chosen) | `BuildMetrics:Token` | One optional secret, fixed-time comparison, no identity and no session. The route is not mapped when the token is absent, so a site that has not opted in answers 404 rather than 401 |
| A per-request signature (HMAC over the body) | The same secret plus a clock | A replay of a timing record is harmless; the extra clock and nonce are not worth it for one gauge |
| No authentication, rely on the origin being hard to find | Obscurity | The origin is public and the path is in the repository |

The token is presented as `X-Build-Metrics-Token`, not as `Authorization: Bearer`, for the reason in
the table: the default authentication scheme forwards every bearer header to the view handler, and
`ViewGuard` makes a bearer `POST` a `405`. A second header leaves the identity pipeline exactly as it
is and makes the route's authority visible in one place.

The route is `POST /admin/build-metrics`. It is under the admin prefix because it is shell
administration, and it joins `Pages:ReservedPaths` so no stored page can claim it. It is anonymous at
the routing layer and checks its header before it reads a body; the anti-forgery middleware has no
metadata for it, which is correct because it is never authenticated by a cookie.

The token rules:

- **Absent means off.** `BuildMetrics:Token` is optional and declared so. A site without it does not
  map the route: a push gets `404`, which is indistinguishable from a path that never existed.
- **Blank is not absent.** A blank value is a configuration error, as everywhere else.
- **At least 32 characters.** A floor is a safety net, not a ration: a short secret is a mistake and
  should stop the start rather than be accepted. `openssl rand -base64 32` is the documented way to
  make one.
- **Compared in fixed time.** The route compares UTF-8 bytes with
  `CryptographicOperations.FixedTimeEquals`, exactly as `ViewTokenStore` compares a view token.
- **Never logged.** The journal line names the target and the duration, never the header.

There is deliberately no rate limit. The token is the gate, the body is bounded, the target set is
closed, and the only thing a holder can do is set a duration on a chart they could already see. If
that changes, a fixed-window policy is one line beside the login's.

## Design

### D1 — **metrics-a-build-timing-is-pushed-to-the-app**

A successful measured target posts one JSON record to the app. The app parses it, refuses anything
outside the contract, and records the duration on a `build.duration` gauge tagged `target`. The
existing exporter writes `build_duration_seconds{target="..."}` and Prometheus scrapes it on its next
interval.

The app validates because it is the one component that knows the dashboard's inventory, and because a
push that bypasses validation could mint a series the catalogue never draws. The gauge is in a new
meter, `uk.osric.asp.Build`, registered with `AddMeter` beside the other two.

### D2 — **metrics-a-build-push-carries-a-token**

The route is authorised by the token in "Authorising the push". Configuration is a new
`BuildMetrics` section:

| Key | Shipped | Range | Why |
| --- | --- | --- | --- |
| `Token` | *(absent)* | at least 32 characters when present | Optional: absent disables the route; blank or short is refused |
| `MaxBodyBytes` | 65536 | 1024–1048576 | The body cap, applied to `ContentLength` and to Kestrel's request size |
| `MaxSeconds` | 86400 | 1–604800 | A duration longer than a day is a mistake; a week is the widest a mutated or wedged target could honestly take |

`BuildMetricsOptions.FromConfiguration` reads all three through `ConfigurationReader`, collects every
problem, and has the same three tests as every other options record: the shipped value, an override,
and a refusal that names the key, the range and the recommendation. The target list is **not**
configuration: it is the build's own set of names, an invariant in code (D3).

### D3 — **metrics-a-build-target-is-a-fixed-name**

The wire record is:

```json
{"target":"check","seconds":412.531}
```

`Core.Metrics.BuildTargets` holds the closed list of names:

`format`, `format-check`, `check`, `test`, `test-core`, `test-runtime`, `test-web`,
`test-integration`, `test-browser`, `test-browser-live`, `license-check`, `audit`, `decisions`,
`check-pages`, `publish`, `image`, `image-check`, `deploy`, `deploy-ssh`.

`Core.Metrics.BuildDurationJson.Parse(json, maxSeconds)` returns a `BuildDurationRead`: one
`BuildDurationRecord`, or a refusal sentence. It refuses: not JSON, not an object, a missing or
non-string `target`, a target not in the list, a missing or non-numeric `seconds`, a non-finite or
non-positive `seconds`, and a `seconds` above `MaxSeconds`. Unknown fields are ignored, so the spool
can carry `outcome` and `finishedAt` beside the two the endpoint reads.

The list is closed so a push cannot mint a series, and so the chart's labels are the build's targets
rather than whatever a caller typed. It is an invariant rather than configuration for the same
reason: it names the targets this repository measures, and a new target is a code change in the
branch that adds the wrapper line. `BuildTargetsTests` reads the `Makefile` through the `RepoRoot`
metadata `ShippedConfiguration` already uses and proves the list and the wrapper lines agree in both
directions: a measured target missing from the list fails, and a name in the list that nothing
measures fails too.

One record per request is enough because the wrapper posts as each target finishes; a batch shape
would be a second contract with no caller, and the spool's `push` mode re-sends one record at a time.

### D4 — **metrics-a-build-chart-shows-the-last-good-run**

The gauge is set only for a successful run. A failed target writes its record to the local spool with
`"outcome":"failed"` and does not post it; the wrapper warns on stderr and exits with the command's
status. The panel's sentence says "the last successful run", so a failed `check` cannot be read as a
sudden improvement.

The alternatives are rejected for concrete reasons:

- **A histogram with an `outcome` tag.** A heatmap aggregates every tag but `le`, so the targets would
  merge; splitting it would be a grid of small multiples (see [metrics](../metrics.md)).
- **A counter of runs by outcome.** A counter would need the outcome label, would reset on every
  restart, and would double-count a retried push. A gauge is idempotent: pushing the same record
  twice changes nothing.
- **A gauge with an `outcome` tag.** The stale series from the other outcome would keep reporting, and
  `max by (target)` would pick the larger of two runs rather than the latest.

The panel is one line chart of `max by (target) (build_duration_seconds)`. `StatusCatalogue.Line`
gains a `by:` argument for a labelled gauge; [assistant-balance-metric](assistant-balance-metric.md)
proposes the same argument, and whichever merges second reuses it. The targets share an axis and
`deploy` will dominate it; that is accepted for a first panel because the hover title and the
end-axis label give each value, and the question is the trend of each line rather than the comparison
between a two-second suite and a fifteen-minute deploy. If the scale proves unreadable, a logarithmic
axis is a chart question for every panel, not a second instrument here.

The panel is in a new `Build` group, between `Assistant` and `Host`. `StatusCatalogue.Groups` and
`GroupOf` learn the prefix; the catalogue's drift tests then hold the instrument to its panel.

### D5 — **metrics-a-build-measures-a-make-target**

The unit of measurement is one make target, plus the deploy's ssh phase as `deploy-ssh`. An aggregate
is measured as well as its parts:

- `check` records the whole gate and every part keeps its own record, so the gate's total can be read
  beside the part that grew.
- `test` records the four suites together, and each suite keeps its own record.
- `deploy` records the whole deploy; `deploy-ssh` records the recipe that does the pushing,
  installing and restarting. The gate and the image are separately measured, so the ssh phase is the
  part that is not one of them.

A target is measured by one wrapper. A one-line recipe is wrapped in place; a multi-line recipe or an
aggregate moves its work to an inner `_name` target, and the wrapper times a sub-make of that. The
sub-make is invoked as `$(MAKE_TOOL)` (`MAKE_TOOL ?= make`), not `$(MAKE)`, because a recipe line
containing `$(MAKE)` runs even under `make -n` (the `image_command` comment records the same rule).

A prerequisite that is only setup stays outside the timer; a prerequisite that is the measured work
stays inside it. `test-core: build` keeps `build` outside, so the suite's number is the suite, not a
stale build. `_check` keeps the gate's parts inside, so `check`'s number is the gate.

## The wrapper

`tools/build-metrics/record.sh` (one file, an SPDX header, executable):

```
record.sh <target> -- <command> [args...]   # run, record, maybe push
record.sh push                              # re-send the spool's successful records
```

It measures with `date +%s%3N`, appends to `artifacts/build-metrics/records.jsonl`, and returns the
command's exit status. The record line is the wire object plus the two facts the endpoint ignores:

```json
{"target":"check","seconds":412.531,"outcome":"ok","finishedAt":"2026-11-15T20:14:03Z"}
```

When the command succeeded, `BUILD_METRICS_URL` is set and a token is found — `BUILD_METRICS_TOKEN`,
or `deploy/build-metrics.token`, which `*.token` already ignores — it posts the record. The token is
put in a private curl config file (`mktemp`, mode 0600, removed afterwards) rather than on the command
line, so it never appears in `ps`. The request is capped (`--connect-timeout 2 --max-time 5`), a
failure is a warning on stderr, and neither changes the command's status. No configuration at all
means no push, which is the default on a fresh checkout.

The URL is the public origin (`https://…/admin/build-metrics`). A plain HTTP URL is for a local host
only: on any other host the token would travel in clear.

## The Makefile

The shape, on one aggregate and one leaf:

```make
BUILD_METRICS := tools/build-metrics/record.sh
MAKE_TOOL ?= make

check:
	@$(BUILD_METRICS) check -- $(MAKE_TOOL) --no-print-directory _check

_check: format-check license-check audit decisions check-pages test publish

format-check:
	@$(BUILD_METRICS) format-check -- $(MAKE_TOOL) --no-print-directory _format-check

_format-check:
	$(DOTNET) format whitespace $(SOLUTION) --verify-no-changes
	$(DOTNET) format style $(SOLUTION) --verify-no-changes --diagnostics IMPORTS
```

The deploy gains one more level:

```make
deploy:
	@$(BUILD_METRICS) deploy -- $(MAKE_TOOL) --no-print-directory _deploy

_deploy: deploy-guard check test-browser image | $(SSH_CONTROL_DIR)
	@$(BUILD_METRICS) deploy-ssh -- $(MAKE_TOOL) --no-print-directory _deploy-ssh

_deploy-ssh: | $(SSH_CONTROL_DIR)
	# the existing ssh and podman recipe, unchanged
```

`make help` gains one line for `build-metrics-push`; the measured targets keep their descriptions. The
inner `_` targets are phony and unlisted. `make -n` still prints the wrapper lines without running
them, and the gate is not run with `-j` (the existing comment on `deploy` already says the order is
the point).

## Components

| File | Change |
| --- | --- |
| `Core/Metrics/BuildTargets.cs` | New: the closed list of measured names (D3) |
| `Core/Metrics/BuildDurationRecord.cs`, `BuildDurationJson.cs`, `BuildDurationRead.cs` | New: the immutable record and its pure parse (D3) |
| `Core/PublicAPI.Unshipped.txt` | The new public surface |
| `Web/Metrics/BuildMetrics.cs` | New: the meter, its inventory, the gauge and `Record` (D1) |
| `Web/Metrics/BuildMetricsOptions.cs` | New: the token and the two caps (D2) |
| `Web/Metrics/BuildMetricsEndpoint.cs` | New: the guarded POST, the bounded body read, the refusal documents |
| `Web/Metrics/StatusCatalogue.cs` | The `Build` group, the panel, and `by:` on `Line` for a labelled gauge (D4) |
| `Web/Program.cs` | The options, the meter, `AddMeter`, and the route that exists only when the token does |
| `Web/appsettings.json` | `BuildMetrics:MaxBodyBytes`, `BuildMetrics:MaxSeconds`; no token; `/admin/build-metrics` in `Pages:ReservedPaths` |
| `Makefile` | The wrapper, the inner targets, `build-metrics-push`, the help line, `.PHONY` |
| `deploy/uk.osric.asp.container` | A second `EnvironmentFile` for the token |
| `deploy/operations.md`, `deploy/README.md` | Generate, place and rotate the token; set the build machine's URL and token; what 404 and 401 mean |
| `docs/metrics.md`, `docs/shell.md` | The instrument, the panel, the route, the token rule, the relay lifetime |
| `docs/jobs/WHIMS.md` | WHIM-04's line goes at the merge |
| `test/uk.osric.asp.Core.Tests/BuildDurationJsonTests.cs` | New: one case per refusal, and a reading |
| `test/uk.osric.asp.Web.Tests/BuildMetricsTests.cs` | New: the inventory and the meter agree; a record emits one measurement per target; none before |
| `test/uk.osric.asp.Web.Tests/BuildMetricsOptionsTests.cs` | New: shipped, override, refusal, token absent/blank/short |
| `test/uk.osric.asp.Web.Tests/StatusCatalogueTests.cs` | The inventory list grows; the Build panel is in a listed group and is a level |
| `test/uk.osric.asp.Web.Tests/BuildTargetsTests.cs` | New: the Makefile's wrapper lines and the list agree (D3) |
| `test/uk.osric.asp.Web.IntegrationTests/BuildMetricsEndpointTests.cs` | New: the route's answers and the meter it sets |
| `test/uk.osric.asp.Web.IntegrationTests/MetricNamesTests.cs` | Records a build; the live body carries `build_duration_seconds` |

## Tests

Written first, one behaviour each:

- **`BuildDurationJsonTests`** — a good record reads; an unknown target, a missing target, a negative
  zero-length or non-finite duration, a duration over the cap, a non-object and malformed JSON are
  each refused with a sentence; unknown fields are ignored.
- **`BuildMetricsTests`** — `Instruments` and the meter agree; `Record` for two targets emits one
  measurement each, tagged `target`; nothing is emitted before the first record.
- **`BuildMetricsOptionsTests`** — the shipped caps; an override; a refusal naming the key, range and
  recommendation; the token absent is `null`; a blank token and a 31-character token are refused; 32
  characters are accepted.
- **`StatusCatalogueTests`** — every instrument still has one panel; the Build panel is a line; its
  query names `build_duration_seconds` and `by (target)`; `Build` is in `Groups`.
- **`BuildTargetsTests`** — the Makefile's `$(BUILD_METRICS) <name> --` lines and `BuildTargets.Names`
  are the same set.
- **`BuildMetricsEndpointTests`** — with the token configured: a good record is `204` and the meter
  reports it; no token and a wrong token are `401`; a signed-in cookie without the header is `401`;
  malformed JSON, an unknown target and a duration over the cap are `400` with a problem document; a
  non-JSON content type is `415`; an oversized body is `413`; a `GET` is `405`. With no token
  configured, the route is `404` even for a caller that presents one.
- **`MetricNamesTests`** — records a build and reads the live `/metrics` body for
  `build_duration_seconds`.

The wrapper itself is shell, and NUnit does not drive it: Phase 2 proves it by running the targets and
reading the spool, and the endpoint's contract is proved by the integration fixture. Its JSON is the
only glue, and the spool is the record if a number is ever questioned.

## Phases

Each phase ends with a green `make check`.

### Phase 0 — the contract (half a day)

- `BuildTargets`, `BuildDurationRecord`, `BuildDurationJson`, `BuildDurationRead`, the public API
  entries and `BuildDurationJsonTests`.

**Done when** every refusal is a sentence, the good record reads, and nothing is wired to a route.

### Phase 1 — the site (a day)

- `BuildMetrics`, `BuildMetricsOptions`, `BuildMetricsEndpoint`, the `Build` group and panel, `by:`
  on `Line`, the appsettings keys and the reserved path.
- `BuildMetricsTests`, `BuildMetricsOptionsTests`, the catalogue test updates, the endpoint fixture
  and `MetricNamesTests`.
- `BuildTargetsTests` waits for Phase 2, because the Makefile it reads has no wrapper lines yet.

**Done when** a `curl` with the token and a record makes `build_duration_seconds` appear on
`/metrics`, the panel draws it, and every refusal is tested.

### Phase 2 — the build (half a day)

- `tools/build-metrics/record.sh`, the Makefile wrappers and inner targets, `build-metrics-push`, the
  help line, and `BuildTargetsTests`.
- A local run of `make check` and `make test-core` proves the spool; a run with the URL and token set
  proves the push; a run with neither proves the build is unchanged.

**Done when** `make check` is green, the spool names every target once per run, and a configured push
shows the lines on `/status`.

### Phase 3 — the deployment and the record (an hour)

- The quadlet's second `EnvironmentFile` and the deploy recipe's seeding line.
- `deploy/operations.md` and `deploy/README.md`; the metrics and shell docs; the decision entries at
  merge.
- WHIM-04's line is removed when this merges, as a promoted whim.

**Done when** a host with no token answers 404, a host with the token accepts a push, and the next
deploy needs nothing beyond filling one file.

## Documentation and decisions

- `docs/metrics.md` — a `build.duration` row in a new Build instrument table, the four-group panel
  count (nineteen Pages, ten Assistant, one Build, five Host), the push route and its token, the
  relay lifetime, and why the chart counts successful runs.
- `docs/shell.md` — `POST /admin/build-metrics` beside the other administration routes, the
  `X-Build-Metrics-Token` rule, why it is not Bearer and not a page, and the 404 when absent.
- `deploy/operations.md` — the token's generation, placement in
  `~/.config/uk.osric.asp/build-metrics.env` as `BuildMetrics__Token=…`, rotation by restart, the
  build machine's `BUILD_METRICS_URL` and token file, and the 404/401 reading.
- `docs/jobs/WHIMS.md` — WHIM-04 promoted; the line goes at the merge.
- `docs/decisions.md` — five entries at the merge:
  **metrics-a-build-timing-is-pushed-to-the-app**,
  **metrics-a-build-push-carries-a-token**,
  **metrics-a-build-target-is-a-fixed-name**,
  **metrics-a-build-chart-shows-the-last-good-run**,
  **metrics-a-build-measures-a-make-target**. If the merge judges the last two one rule, it folds
  them and updates the citations in this plan.
- `/help` is unchanged: this adds no page-authoring capability.

## Risks

| Risk | Answer |
| --- | --- |
| The route is on the public origin | A 256-bit token, fixed-time comparison, a closed target list, a body cap and a duration cap. A leaked token can set a duration on a chart the owner could already see; rotation is a restart. If that is ever too much, a Caddy `respond` rule and an ssh push are one line and one wrapper change |
| `deploy` dominates the shared axis | The panel's sentence and hover names each value; the trend of each line is the question. A log axis, if wanted, is a change to every chart, not a second instrument |
| A restart clears the gauge | Prometheus keeps the history; the next successful build restores the level. The same hole the storage gauges have |
| Two builds at once overwrite each other's last value | One builder by design; the spool has both records, and a later push sends the latest. A host label is the answer if machines ever multiply |
| The wrapper adds a sub-make to every measured target | The gate already runs sequentially; `$(MAKE_TOOL)` keeps `-n` printing rather than running. `-j` behaves as it does today |
| A push timeout slows a build | Two-second connect and five-second total caps; a failure is a warning; the command's status is untouched |
| The Makefile and the target list drift | `BuildTargetsTests` reads both directions, and a new wrapper line without a name fails the gate |
| A timing from a slow machine looks like a regression | The chart has no host label; the owner knows which machine builds. If a second machine appears, the label comes with it |
| The spool is in `artifacts/`, so `make clean` forgets it | That is the build output's contract; the numbers that matter are already in Prometheus |

## Delivered when

`make format-check`, `make check`, each test suite, `make image` and `make deploy` each append a
record to `artifacts/build-metrics/records.jsonl`; with `BUILD_METRICS_URL` and the token set, a
successful run sets `build_duration_seconds{target}` and `/status` draws one line per target in a
Build group; a failed run is in the spool and the journal but not on the chart; a site without the
token answers 404 and a wrong token 401; a push that cannot happen never fails a build; `make check`
is green and the catalogue's drift tests hold the instrument to its panel.

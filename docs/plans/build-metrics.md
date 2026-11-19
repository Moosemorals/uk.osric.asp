<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Build timings

| | |
| --- | --- |
| Status | Proposed 2026-11-15 — waits for the owner (`product-steps-wait-for-the-owner`). Nothing below is built. |
| Size | Large — one pure contract in `Core`, one new deployable (project, image, quadlet, DNS, scrape job), one Makefile wrapper, and the panel that draws the family |
| Area | Core (metrics), a new `Collector` service, Web (metrics catalogue only), Makefile, deploy (image, quadlet, Caddy, Prometheus, one secret) |
| Spike | None. The collector is a minimal API over one dictionary; the app's meter/exporter is the pattern it does *not* need, and `ViewTokenStore` is the precedent for a fixed-time secret comparison |
| Backlog | [WHIM-04](../backlog/WHIMS.md) — promoted here; its line goes when this merges |
| Decisions | Six names intended, below; taken at the merge (`product-plan-before-building`) |

## The short version

The build knows how long it took, and the only record is a terminal someone was watching. This change
gives those durations a history **in a service that is not the site**:

- **A tiny collector.** A minimal ASP.NET Core app, `uk.osric.asp.Collector`, accepts one JSON record
  per request, keeps the last value per make target in memory, and serves them as Prometheus text on
  `/metrics`. It has its own image, quadlet, DNS name and Prometheus job.
- **The app is not in the push path.** The site is the thing being deployed, and it is most likely to
  be down exactly when a deploy's gate runs. The collector is a separate unit, so `make check`,
  `make test` and `make image` still land while the site is broken, and a successful deploy's timing
  lands after it comes back.
- **The site draws the family it does not produce.** `/status` gains a Build panel reading
  `build_duration_seconds` from Prometheus, the same way the Host panels read the framework's meters.
  The site's own `/metrics` is unchanged.
- **Authorise it with one token.** The collector requires `BuildMetrics:Token`, presented in
  `X-Build-Metrics-Token` and compared in fixed time. It is not a session, not the assistant's view
  token, and not network position.
- **Count the last good run.** The build wrapper posts only a successful run; a failure is in the
  local spool and the journal, so a fast failure cannot look like an improvement.

## Goals

1. `make check`, `make deploy`, `dotnet format`, every test suite, `make image` and the ssh phase of a
   deploy each leave a duration that outlives the terminal **and survives the site being down**.
2. A target is measured once, by one wrapper, so the numbers cannot be assembled differently in two
   places.
3. The collector is a separate failure domain: an app deploy, a failed app start or a broken page
   cannot take the gate's or the image's timings with it. A failed deploy's own record is spooled
   locally, because the last-good-run rule means it is not posted.
4. The push is safe to leave on a build machine: no browser, no owner session and no ssh connection;
   a leaked token can pollute one chart and nothing else.
5. A push that cannot happen (collector down, no URL, no token) never fails a build.
6. Every new value is configuration, every new rule is a test, and the catalogue's drift tests keep
   holding its panels to the families they read.
7. Nothing a page author can do changes: no `IContext` member, no capability, no editor action, so
   `/help` is untouched.

## Non-goals

- **A general metrics receiver.** The collector accepts one record shape and stores one gauge. It is
  not a Pushgateway and not a remote-write endpoint.
- **Host resource metrics.** node_exporter is installed, but the quadlet runs with
  `--collector.disable-defaults`, so it serves only the textfile directory and reports no CPU,
  memory or disk. Turning those on is a separate decision; the collector's only numbers are the
  build's.
- **Per-machine, per-branch or per-commit labels.** There is one builder. A `host` label would
  separate machines that should not be compared and multiply the series. A branch or commit label
  would make every build a new series.
- **Alerting on a slow build.** A notification channel is [`ops-no-outage-notification`](../backlog/ops-no-outage-notification.md).
- **Retrying or failing a build when the push fails.** The build's exit code is the command's.
- **A CI runner.** Running the gate unasked is [`ops-no-ci`](../backlog/ops-no-ci.md); this only makes the push
  reachable from whatever eventually runs it.
- **Timing `make build`, `make restore` or the mutation targets.** They are one wrapper line and one
  name away, but the user's list does not ask for them, and a stamp that does nothing (an up-to-date
  `build`) is a record that says nothing.

## What exists today

| Piece | Where | What it does |
| --- | --- | --- |
| The site | `src/uk.osric.asp.Web` | Pages, endpoints, the `/metrics` exporter, the status catalogue; the thing a deploy restarts |
| Make targets | `Makefile` | `check`, `deploy`, `format`, `format-check`, the `test-*` suites, `publish`, `image`, `image-check`, the gate's own parts, and the ssh recipe inside `deploy` |
| The page meter | `Web/Pages/PageMetrics` | A `Meter`, an inventory and the observable-gauge shape |
| The status catalogue | `Web/Metrics/StatusCatalogue` | One panel per instrument, Host panels for families this repository does not register, and the drift tests |
| `/metrics` | `Web/Program` | OpenTelemetry's Prometheus exporter, scraped at `app:8080`; Caddy answers 404 on the public origin |
| Prometheus | `deploy/prometheus.yml` | Two jobs on the rootless network: the app at `app:8080`, and `node-exporter:9100` as `host` |
| node_exporter | `deploy/node-exporter.container` | Rootless beside the app and Prometheus; publishes nothing; every default collector off, so it serves only `/var/lib/node-exporter/textfile`, which the root `fail2ban-metrics.sh` timer fills |
| The web quadlet | `deploy/uk.osric.asp.container` | Rootless; publishes `127.0.0.1:8080`; `EnvironmentFile` for the assistant's key; the pattern a collector quadlet copies |
| The image build | `Makefile` | One project, one `dotnet publish -t:PublishContainer`, one tag per commit, a reproducibility check |
| `ConfigurationReader` | `Web/Configuration` | The house shape for options; in `Web`, so the collector cannot reference it without dragging the site in |
| `ViewTokenStore` | `Web/Authentication` | Fixed-time secret comparison |
| `Core` | `src/uk.osric.asp.Core` | Pure values and functions, no references; where the wire contract belongs |

## Where the numbers go

The owner's note decides it: the site is most likely to be down when a deploy runs, because the only
deploys outside this early work are the ones a breakage causes. The app cannot be the receiver.

| Option | What it costs | Why |
| --- | --- | --- |
| **A separate collector (chosen)** | One tiny app, one image, one quadlet, one DNS name, one Caddy block, one scrape job | It is up when the site is down, which is exactly when the gate's numbers are wanted. The site is not in the push path at all |
| The site's own route | One route, one meter, one token | Rejected by the failure mode: the gate and the image are measured while the site is broken, so those pushes would be lost, and a failed deploy leaves nothing to push to |
| Prometheus remote-write receiver | `--web.enable-remote-write-receiver`, a snappy-compressed protobuf payload and no authentication on the receiver | Prometheus is up when the site is down, so the failure domain is right, but the wire format needs a new encoder and the receiver would accept any series from anything on the network; a token proxy in front of it is the collector again |
| Pushgateway | A fourth container, a volume, a retention rule and last-push-per-group semantics | The deployment deliberately has three rootless containers; a store whose semantics differ from Prometheus's is a new thing to operate for one gauge |
| node_exporter's textfile collector | A `.prom` file in a directory node_exporter already serves | Rejected by the clock, not by the service. node_exporter drops a textfile line's timestamp — `Ignoring unsupported custom timestamp on textfile collector metric` — so a sample is dated at the scrape that reads the file, and `node_textfile_mtime_seconds` is when the file was written. A record that waited for a deploy or a reachable host would land as a point on the day it was sent. This build pushes at the moment it measures, so its receiver has to be up then |
| Network position: loopback only, Caddy `404`, push over ssh | The host's ssh key | Genuinely simple, but every measured target would need a host (or a separate push step), a local `make check` would depend on the deploy host, and a future CI would need the key |

The collector is the smallest thing that keeps the failure domain right: a process with no storage, no
pages and no database, whose entire state is the last duration per target. Prometheus is still the
store; the collector is a relay that does not share the site's lifecycle.

## Authorising the push

The build has no browser, no owner session and no assistant run. The options, in the order they were
considered:

| Option | Authenticates by | Why |
| --- | --- | --- |
| The owner's cookie | A signed-in session | A build machine has no login form and must not keep a password; a CI has no owner |
| `Authorization: Bearer` and the `View` scheme | A token minted in memory per run | A build is not a run. On the site, `ViewGuard` refuses every view `POST` and an unknown bearer is a `401`; the collector has no identity pipeline to reuse, and widening the view token's authority to accept writes is the wrong direction |
| Network position: loopback only, Caddy `404`, push over ssh | The host's ssh key | Genuinely simpler, but every measured target would need a host (or a separate push step), a local `make check` would depend on the deploy host being reachable, and a future CI would need the key. The collector's own DNS makes it public anyway |
| A shared token in a dedicated header (chosen) | `BuildMetrics:Token` | One required secret, fixed-time comparison, no identity and no session. The collector's only authority is a caller that knows the token |
| A per-request signature (HMAC over the body) | The same secret plus a clock | A replay of a timing record is harmless; the extra clock and nonce are not worth it for one gauge |
| No authentication, rely on the origin being hard to find | Obscurity | The name is in the repository, and Caddy publishes it |

The token is presented as `X-Build-Metrics-Token`, not as `Authorization: Bearer`, for a smaller
reason than on the site: the collector has no view scheme to collide with, but a dedicated header
keeps the route's authority in one place and survives the day the collector ever sits behind the
site's authentication. It is compared as UTF-8 bytes with `CryptographicOperations.FixedTimeEquals`,
exactly as `ViewTokenStore` compares a view token.

The token rules, and the two sides are deliberately different:

- **Required on the collector.** The collector exists to accept pushes, so a missing or short token
  stops it at startup with the usual list, naming the key and the range. The first deploy needs
  `collector.env` filled before the unit starts.
- **Optional in the build.** A build with no token records every duration and skips the push; the
  outermost make invocation prints **one warning at the end**, where someone will see it, and the
  build's exit code is the command's. The warning names the missing value and the spool, and
  `make build-metrics-push` re-sends once the token exists. A missing token is never a build failure.
- **At least 32 characters.** A floor is a safety net, not a ration: a short secret is a mistake and
  should stop the collector rather than be accepted. `openssl rand -base64 32` is the documented way
  to make one.
- **Never logged.** The journal line names the target and the duration, never the header.
- **No rate limit.** The token is the gate, the body is bounded, the target set is closed, and the
  only thing a holder can do is set a duration on a chart the owner could already see. If that
  changes, a fixed-window policy is one line beside it.

`POST /build` checks the token before it reads a body. A missing or wrong header is `401`; the request
never reaches the parser. The route has no anti-forgery token because it is not a browser route and is
never authenticated by a cookie.

## The collector

A new web project, `src/uk.osric.asp.Collector`, with a new test project beside it. It is a minimal
API with three routes:

| Route | Purpose |
| --- | --- |
| `POST /build` | The one data endpoint: a JSON record, or a refusal |
| `GET /metrics` | The Prometheus exposition, scraped at `collector:8080` and refused publicly by Caddy |
| `GET /health` | `200 ok`, for the quadlet's health check |

It holds a `ConcurrentDictionary<string, double>` of target to seconds. A push replaces the target's
value; a scrape reads a snapshot. There is no storage, no database, no page pipeline and no clock: a
restart forgets the levels and Prometheus keeps the history, which is the same lifetime the storage
gauges already have.

The image is the same pinned Alpine base as the site, the same non-root user, and a `make image`
target that builds both projects. The collector image is tiny; the release pushes one more small
layer.

### D1 — **metrics-a-build-timing-is-collected-beside-the-app**

The collector is its own quadlet, image and DNS name, on the static `uk.osric.asp.network` beside the
app and Prometheus. It publishes on the host loopback for Caddy (rootful Caddy cannot join a rootless
network) and carries `NetworkAlias=collector` for the scrape. The deploy restarts the collector
**first**, waits for its health, and only then restarts the app. The gate runs before any of that, so
the collector's availability during the gate does not depend on the app: it is the unit that was
already up, and the only thing the deploy changes after the gate is the collector's own restart, long
after its records were pushed.

The site's `/metrics` is untouched. Prometheus gains one job:

```yaml
  - job_name: build
    metrics_path: /metrics
    static_configs:
      - targets: ["collector:8080"]
```

The build's URL is the collector's public name, `https://collector.moosemorals.com/build`.

### D2 — **metrics-a-build-push-carries-a-token**

The collector requires a token; unlike the site it has no other reason to start. `BuildMetrics:Token`
is a required configuration value, presented as `X-Build-Metrics-Token` and compared in fixed time
with `CryptographicOperations.FixedTimeEquals`. A missing or wrong token is `401` before the body is
read; the token is never logged.

The section is the collector's whole configuration:

| Key | Shipped | Range | Why |
| --- | --- | --- | --- |
| `Token` | *(absent — supplied in the host's env file)* | at least 32 characters | Required: the collector exists to accept pushes. A missing value stops it at startup, as the assistant's key stops the site |
| `MaxBodyBytes` | 65536 | 1024–1048576 | The body cap, checked on `ContentLength` and enforced by Kestrel's request size |
| `MaxSeconds` | 86400 | 1–604800 | A duration longer than a day is a mistake; a week is the widest a wedged target could honestly take |

`openssl rand -base64 32` is the documented way to make one. The host's
`~/.config/uk.osric.asp/collector.env` holds `BuildMetrics__Token=…`, mode 0600, seeded once by
`make deploy` like `assistant.env`; the build machine holds the same value in
`BUILD_METRICS_TOKEN` or `deploy/build-metrics.token`, which `*.token` already ignores.

The collector carries a small copy of the configuration reader's shape (collect every problem, one
report, no fallback) because `Web` is not a reference it may take; referencing the site for one
helper would put the site's assembly and packages in the collector's image. The copy is tested with
the same three cases as every options record: the shipped caps, an override and a refusal naming the
key, the range and the recommendation.

### D3 — **metrics-a-build-target-is-a-fixed-name**

The wire record is:

```json
{"target":"check","seconds":412.531}
```

`Core.Metrics.BuildTargets` holds the closed list of names:

`format`, `format-check`, `check`, `ci`, `test`, `test-core`, `test-runtime`, `test-web`,
`test-collector`, `test-integration`, `test-browser`, `test-browser-live`, `license-check`, `audit`,
`decisions`, `check-pages`, `publish`, `image`, `image-check`, `deploy`, `deploy-ssh`.

`Core.Metrics.BuildDurationJson.Parse(json, maxSeconds)` returns a `BuildDurationRead`: one
`BuildDurationRecord`, or a refusal sentence. It refuses: not JSON, not an object, a missing or
non-string `target`, a target not in the list, a missing or non-numeric `seconds`, a non-finite or
non-positive `seconds`, and a `seconds` above `MaxSeconds`. Unknown fields are ignored, so the spool
can carry `outcome` and `finishedAt` beside the two the collector reads.

The list is closed so a push cannot mint a series, and so the chart's labels are the build's targets
rather than whatever a caller typed. It is an invariant rather than configuration for the same
reason: it names the targets this repository measures, and a new target is a code change in the
branch that adds the wrapper line. `BuildTargetsTests` reads the `Makefile` through the `RepoRoot`
metadata every test project already has and proves the list and the wrapper lines agree in both
directions.

`Collector` references `Core`, so the parse and the list are the same code the tests already cover;
the collector adds only the route and the exposition. The site does not reference them at all — its
Build panel knows one family name, not the target list.

### D4 — **metrics-a-build-chart-shows-the-last-good-run**

The collector stores whatever it is given; the build wrapper decides what is worth giving. A failed
target writes its record to the local spool with `"outcome":"failed"` and does not post it; the
wrapper warns on stderr and exits with the command's status. The panel's sentence says "the last
successful run", so a failed `check` cannot be read as a sudden improvement.

The alternatives are rejected for concrete reasons:

- **A histogram with an `outcome` tag.** A heatmap aggregates every tag but `le`, so targets would
  merge; splitting it would be a grid of small multiples (see [metrics](../metrics.md)).
- **A counter of runs by outcome.** It would reset on every collector or Prometheus restart and double
  count a retried push. A gauge is idempotent: pushing the same record twice changes nothing.
- **A gauge with an `outcome` tag.** The stale series from the other outcome would keep reporting, and
  `max by (target)` would pick the larger of two runs rather than the latest.

The site's panel is one line chart of `max by (target) (build_duration_seconds)`. It is an **external
family** panel, like the Host panels: the property `Panel.HostFamilies` becomes
`Panel.ExternalFamilies`, its doc says "a family this repository does not register, from the
framework's meters or another scrape target", and the Build panel names
`build_duration_seconds`. The drift tests keep holding it: a panel with no instrument names its
external family, and a family a query names is in an inventory or an external list.

The panel is in a new `Build` group between `Assistant` and `Host`. The targets share an axis and
`deploy` will dominate it; that is accepted for a first panel because the hover title and the
end-axis label give each value, and the question is the trend of each line rather than the comparison
between a two-second suite and a fifteen-minute deploy. A logarithmic axis, if wanted, is a change to
every chart rather than a second family.

### D5 — **metrics-a-build-measures-a-make-target**

The unit of measurement is one make target, plus the deploy's ssh phase as `deploy-ssh`. An aggregate
is measured as well as its parts:

- `check` records the whole gate and every part keeps its own record, so the gate's total can be read
  beside the part that grew.
- `test` records the five non-browser suites together, and each keeps its own record. `test-collector`
  is one of them.
- `deploy` records the whole deploy; `deploy-ssh` records the recipe that does the pushing,
  installing and restarting. The gate and the image are separately measured, so the ssh phase is the
  part that is not one of them.
- `ci` is measured as well, because it reaches `check` through its own sub-make; without a wrapper at
  the top, the one missing-token warning would be written by a child invocation that is not the end of
  the build the owner is watching.

A target is measured by one wrapper. A one-line recipe is wrapped in place; a multi-line recipe or an
aggregate moves its work to an inner `_name` target, and the wrapper times a sub-make of that. The
sub-make is invoked as `$(MAKE_TOOL)` (`MAKE_TOOL ?= make`), not `$(MAKE)`, because a recipe line
containing `$(MAKE)` runs even under `make -n` (the `image_command` comment records the same rule).

A prerequisite that is only setup stays outside the timer; a prerequisite that is the measured work
stays inside it. `test-core: build` keeps `build` outside, so the suite's number is the suite, not a
stale build. `_check` keeps the gate's parts inside, so `check`'s number is the gate.

### D6 — **metrics-the-collector-uses-the-prometheus-exporter**

The collector uses the site's own metrics stack: `System.Diagnostics.Metrics` for the instrument and
`OpenTelemetry.Exporter.Prometheus.AspNetCore` plus `OpenTelemetry.Extensions.Hosting` for the
exposition. A `BuildMetrics` class owns a meter named `uk.osric.asp.Build` and one observable gauge,
`build.duration`, unit `s`, tagged `target`; `Program` calls `AddMeter`, `AddPrometheusExporter` and
`MapPrometheusScrapingEndpoint("/metrics")`, so the family the site queries is the one the exporter
writes, `build_duration_seconds`.

The two packages are already pinned in `Directory.Packages.props` and already used by the site, so
the collector adds two references and no new package or version to the repository. It gains the
exporter's naming, escaping and label handling, which are exactly the boring, easy-to-get-wrong
parts; a collector integration test reads the live `/metrics` body for `build_duration_seconds`, so a
package upgrade that changes the family fails a test rather than quietly emptying the panel.

The rejected alternative is rendering the exposition by hand. For one gauge it is about forty lines
and no packages, and it was the first draft of this plan; it is rejected because the owner would
rather take the dependency than own the escaping, the label quoting and the content negotiation, and
because two implementations of one family name is one more place to get the contract wrong.
`Microsoft.Diagnostics.Monitoring` (`dotnet-monitor`) is not the alternative it sounds like: it
monitors a .NET process rather than accepting a blob.

## The collector's endpoint

`POST /build`:

1. The token is checked first, before any body is read. A missing or wrong header is `401`.
2. The content type must be `application/json`; anything else is `415`.
3. `ContentLength` above `MaxBodyBytes` is `413`; Kestrel's request size is set to the same cap so a
   chunked body cannot bypass it.
4. `BuildDurationJson.Parse` either returns the record or a refusal. A refusal is `400` with the
   sentence as plain text.
5. The record replaces the target's value and the response is `204`.

The endpoint logs one line per accepted record at information — target and seconds, never the token —
and a refusal at debug. It carries no anti-forgery token: it is not a browser route and is never
authenticated by a cookie; the token is its only authority.

`GET /metrics` is the exposition; Caddy answers `404` for it on the public name, and Prometheus
scrapes it over the rootless network. `GET /health` is `200 ok`, so the quadlet's own probe and the
deploy's wait have an answer that does not depend on the site's storage or page pipeline.

## The deployment

| Piece | Change |
| --- | --- |
| Project | `src/uk.osric.asp.Collector`, a `Microsoft.NET.Sdk.Web` app; `ContainerRepository=uk.osric.asp.collector`, the same non-root user and base image |
| Test project | `test/uk.osric.asp.Collector.Tests`, NUnit and `WebApplicationFactory<Program>` |
| Image | `make image` builds the site and the collector; `make image-check` proves both reproducible; `make deploy` tags and pushes both commit tags |
| Quadlet | `deploy/uk.osric.asp.collector.container`: rootless, `Network=uk.osric.asp.network`, `NetworkAlias=collector`, `PublishPort=127.0.0.1:8081:8080`, `EnvironmentFile` for the token, a `wget` health check and a small memory/pids cap |
| Caddy | A `collector.moosemorals.com` block: `respond @metrics 404`, then `reverse_proxy 127.0.0.1:8081` |
| Prometheus | The `build` job above |
| Deploy | Seed `collector.env` once, install both quadlets, restart the collector first and wait for its health, then restart the app and Prometheus; push both images and pull both commit tags |
| The site | No route, no meter, no build token. Only the Build panel and the external-family rename in the catalogue |

On the first deploy the collector's env file is empty, and a collector that requires its token refuses
to start; fill `collector.env` before that deploy, as the assistant's key already has to be filled
before the site's first start. The deploy's health wait then proves the token was read, rather than
failing later at the first push.

The collector's image is a framework-dependent app on the same pinned base; its own layer is small.
The one extra publish is cheap; the one extra push is a few seconds.

### Why not native AOT

AOT is possible. The collector's own code is already friendly — `JsonDocument` rather than
reflection serialization, an endpoint over `HttpContext` and services, a configuration reader rather
than a binder, no dynamic registration — and the OpenTelemetry packages declare `IsAotCompatible` for
net8.0 and later, so the exporter is not the blocker it might have been. Check the pinned version's
annotations before relying on that. It is still not worth it here:

- **Startup is not the bottleneck.** The unit restarts rarely, and `RestartSec=5` plus the health
  period dwarfs the few hundred milliseconds AOT saves.
- **Memory is not the bottleneck.** The collector is capped at 128 MB on an 8 GB host and uses far
  less; AOT's saving is tens of megabytes nobody is short of.
- **The image would probably get bigger.** The framework-dependent layer sits on the ASP.NET base the
  site already pulls. A native binary is 10–30 MB of its own layer, and a `runtime-deps` base is
  another pin and another pull.
- **The build gets harder.** `linux-musl-arm64` from an x64 builder needs a cross toolchain and
  sysroot the SDK does not carry, and `image-check` would have to prove the AOT image reproducible
  before the second build in a deploy is trustworthy.
- **The attack surface is already small.** One token-checked JSON route, no page pipeline, and no
  JIT of anything a visitor wrote.

If startup ever matters, ReadyToRun is the cheaper middle ground: no cross toolchain, the shared
base, and a bigger layer for a faster start. If AOT is wanted anyway, the choices above are what keep
it easy — `JsonDocument` or a source-generated JSON context, the `HttpContext` endpoint, the reader
instead of `IOptions` binding, `InvariantGlobalization`, `PublishAot` with `StripSymbols` and
`OptimizationPreference=Size`, a pinned `runtime-deps` base, the target toolchain, and trim/AOT
warnings as errors. The plan keeps the framework-dependent collector.

## The build wrapper

`tools/build-metrics/record.sh` (one file, an SPDX header, executable):

```
record.sh <target> -- <command> [args...]   # run, record, maybe push
record.sh push                              # re-send the spool's successful records
```

It measures with `date +%s%3N`, appends to `artifacts/build-metrics/records.jsonl`, and returns the
command's exit status. The record line is the wire object plus the two facts the collector ignores:

```json
{"target":"check","seconds":412.531,"outcome":"ok","finishedAt":"2026-11-15T20:14:03Z"}
```

When the command succeeded, `BUILD_METRICS_URL` is set and a token is found — `BUILD_METRICS_TOKEN`,
or `deploy/build-metrics.token` — it posts the record to `POST /build`. The token is put in a private
curl config file (`mktemp`, mode 0600, removed afterwards) rather than on the command line, so it
never appears in `ps`. The request is capped (`--connect-timeout 2 --max-time 5`) and never changes
the command's status.

A push that is skipped (no URL or token) or that fails writes one line to
`artifacts/build-metrics/push-problems` and stays quiet. The warning is printed once, at the end: a
wrapper invoked by the outermost make (`MAKELEVEL` is zero) reads the marker, prints one line naming
the reason and the spool, and clears it. `make test-core` run on its own is the outermost invocation
and warns; the same target inside `make check` does not, and `check`'s own wrapper does at the end.
Nothing about a missing token or an unreachable collector fails a build; the spool is the record, and
`make build-metrics-push` re-sends it once the collector or the token is there.

The `push` mode is the exception: a manual `make build-metrics-push` reports a missing URL or token as
an error, because it was asked to push and cannot.

The URL is the collector's public name. A plain HTTP URL is for a local collector only: on any other
host the token would travel in clear.

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
	# the existing ssh and podman recipe, extended for the collector
```

`test` gains `test-collector`; `ci` becomes a wrapper around its existing recipe, so its sub-make
chain still ends with the one missing-token warning; the help gains `test-collector` and
`build-metrics-push`; the inner `_` targets are phony and unlisted. `make -n` still prints the wrapper
lines without running them, and the gate is not run with `-j` (the existing comment on `deploy`
already says the order is the point).

## Components

| File | Change |
| --- | --- |
| `Core/Metrics/BuildTargets.cs` | New: the closed list of measured names (D3) |
| `Core/Metrics/BuildDurationRecord.cs`, `BuildDurationJson.cs`, `BuildDurationRead.cs` | New: the immutable record and its pure parse (D3) |
| `Core/PublicAPI.Unshipped.txt` | The new public surface |
| `src/uk.osric.asp.Collector/Program.cs` | New: the minimal API, options, routes, health, and `AddMeter`/`AddPrometheusExporter`/`MapPrometheusScrapingEndpoint` (D6) |
| `src/uk.osric.asp.Collector/BuildMetrics.cs` | New: the meter, the `build.duration` gauge, the last-value store and `Record` (D6) |
| `src/uk.osric.asp.Collector/BuildMetricsEndpoint.cs` | New: the token, the bounded body, the parse and the `204` |
| `src/uk.osric.asp.Collector/CollectorOptions.cs` | New: the token and the two caps, with its own small reader (D2) |
| `src/uk.osric.asp.Collector/appsettings.json` | The two caps; no token |
| `src/uk.osric.asp.Collector/uk.osric.asp.Collector.csproj` | The web SDK, the container metadata, the two OpenTelemetry packages (already pinned), the `Core` reference and `InternalsVisibleTo` |
| `uk.osric.asp.slnx` | The two new projects |
| `test/uk.osric.asp.Collector.Tests/…` | New: options, endpoint, metrics and the Makefile/target drift |
| `Web/Metrics/StatusCatalogue.cs` | The `Build` group, the panel, and `HostFamilies` → `ExternalFamilies` (D4) |
| `Web/Metrics/StatusCatalogueTests.cs` | The group and the external family assertions |
| `Makefile` | The wrapper and inner targets, `test-collector`, `build-metrics-push`, the collector image/tag/publish variables, both images, both pushes, the deploy order |
| `deploy/uk.osric.asp.collector.container` | New: the collector quadlet |
| `deploy/Caddyfile` | The collector's public block and its `/metrics` 404 |
| `deploy/prometheus.yml` | The `build` job |
| `deploy/README.md`, `deploy/operations.md` | The collector's image, token, DNS, health, restart and what 404/401 mean |
| `docs/collector.md` | New: what the collector is, why it is separate, its routes, token and deployment |
| `docs/architecture.md`, `docs/metrics.md`, `docs/testing.md`, `docs/glossary.md` | The new project and its dependency; the Build panel and the third scrape; the new suite; the term |
| `README.md` | One line naming the collector beside the site, and the link to its document |
| `tools/check-decisions/ignore.txt` | `deploy-ssh` beside `deploy-guard`, both make targets shaped like decision names |
| `docs/jobs/WHIMS.md` | WHIM-04's line goes at the merge |

## Tests

Written first, one behaviour each:

- **`BuildDurationJsonTests`** — a good record reads; an unknown target, a missing target, a negative,
  zero, non-finite or over-cap duration, a non-object and malformed JSON are each refused with a
  sentence; unknown fields are ignored.
- **`CollectorOptionsTests`** — the shipped caps; an override; a refusal naming the key, range and
  recommendation; the token missing is refused; a blank and a 31-character token are refused; 32
  characters are accepted.
- **`CollectorMetricsTests`** — the meter registers `build.duration`; `Record` emits one measurement
  per target, tagged `target`; nothing is emitted before the first record; a repeated target replaces
  its value; the live `/metrics` body carries `build_duration_seconds` with the target label, which
  is the exporter's family and the panel's contract.
- **`CollectorEndpointTests`** — a good record is `204` and the live `/metrics` body carries it; the
  token missing and wrong are `401`; an unknown target, malformed JSON and an over-cap duration are
  `400`; a non-JSON content type is `415`; an oversized body is `413`; `/health` is `200`.
- **`BuildTargetsTests`** — the Makefile's `$(BUILD_METRICS) <name> --` lines and `BuildTargets.Names`
  are the same set.
- **`StatusCatalogueTests`** — every instrument still has one panel; the Build panel names its
  external family and is a line; `Build` is in `Groups`; the Host panels still name theirs.
- **`MetricNamesTests`** — unchanged in the app: it records every app instrument and the live
  `/metrics` body carries them. The Build family is the collector's, not the app's, so it is not in
  this test; the collector's own live-body test is the proof.

The wrapper itself is shell, and NUnit does not drive it: the build phase proves it by running the
targets and reading the spool, including the one missing-token warning at the end, and the
collector's endpoint fixture proves the contract. Its JSON is the only glue, and the spool is the
record if a number is ever questioned.

## Phases

Each phase ends with a green `make check`.

### Phase 0 — the contract (half a day)

- `BuildTargets`, `BuildDurationRecord`, `BuildDurationJson`, `BuildDurationRead`, the public API
  entries and `BuildDurationJsonTests`.

**Done when** every refusal is a sentence, the good record reads, and nothing is wired to a route.

### Phase 1 — the collector (a day and a half)

- The project, its options and small reader, the store, the endpoint, the exposition, `/health`, the
  `appsettings.json` and the solution entries.
- `CollectorOptionsTests`, `CollectorEndpointTests`, `CollectorMetricsTests`.
- `test-collector` in the Makefile, the `test` aggregate and the suite list.

**Done when** `curl` with the token and a record makes `build_duration_seconds` appear in the
collector's `/metrics`, the refusals are tested, and `make test-collector` is green.

### Phase 2 — the panel (a few hours)

- `ExternalFamilies` on `Panel`, the Host panels' rename, the `Build` group and panel, and the
  catalogue tests.
- `StatusPageSizeTests` if the extra panel moves its pinned size.

**Done when** `/status` draws the Build panel from a Prometheus that has scraped the collector, and
the catalogue tests are green.

### Phase 3 — the build (half a day)

- `tools/build-metrics/record.sh`, the Makefile wrappers and inner targets, `build-metrics-push`, the
  help lines, and `BuildTargetsTests`.
- A local run of `make check` and `make test-core` proves the spool; a run with the URL and token set
  proves the push; a run with neither proves the build is unchanged.

**Done when** `make check` is green, the spool names every target once per run, and a configured push
shows the lines on `/status`.

### Phase 4 — the deployment (half a day)

- The collector's image variables and commands in `make image`, `image-check` and `deploy`; the
  quadlet, the Caddy block, the Prometheus job and the token seeding.
- `deploy/README.md`, `deploy/operations.md`, `docs/collector.md`, the architecture, metrics and
  testing docs, and the decision entries at merge.
- WHIM-04's line is removed when this merges, as a promoted whim.

**Done when** the collector is its own unit at its own name, Prometheus scrapes it, a host with no
token fails the collector at startup with a list naming the key, a wrong token is `401`, and `make
deploy` brings both units up with the collector first.

## Documentation and decisions

- `docs/collector.md` — the new service: why it is not the site, the three routes, the token (required
  there, optional in the build), the two caps, the store's lifetime, the meter and exporter, the image
  and quadlet, and the build wrapper's push and end warning.
- `docs/metrics.md` — the Build panel and its external family, the third scrape target, the two
  internal `/metrics` paths, the exporter's family name, the relay lifetime and the last-good-run
  rule, and the panel count (nineteen Pages, ten Assistant, one Build, five Host).
- `docs/architecture.md` — `Collector` in the project list, `Collector → Core` in the dependency
  direction, and why the collector is not part of the page pipeline.
- `docs/testing.md` — the collector suite in the suite list and the counts.
- `docs/glossary.md` — the collector as a term: the build's relay, not the site and not the store.
- `README.md` — the collector beside the site's one-paragraph shape.
- `deploy/operations.md` — the token's generation and placement, the collector's health and restart,
  the DNS and certificate, and the 404/401 reading.
- `docs/jobs/WHIMS.md` — WHIM-04 promoted; the line goes at the merge.
- `docs/decisions.md` — six entries at the merge:
  **metrics-a-build-timing-is-collected-beside-the-app**,
  **metrics-a-build-push-carries-a-token**,
  **metrics-a-build-target-is-a-fixed-name**,
  **metrics-a-build-chart-shows-the-last-good-run**,
  **metrics-a-build-measures-a-make-target**,
  **metrics-the-collector-uses-the-prometheus-exporter**.
  If the merge judges two of them one rule, it folds them and updates the citations in this plan.
- `/help` is unchanged: this adds no page-authoring capability.

## Risks

| Risk | Answer |
| --- | --- |
| A fourth container to run, update and roll back | It is a single process with no storage. Its own image and quadlet are small; the deploy restarts it first and waits for its health, and an app-only change never touches it |
| The collector is down when a build runs | The wrapper spools the record and prints one warning at the end; `make build-metrics-push` re-sends. The failure is a line lost from a chart, never a failed build |
| The build has no token | Every duration is spooled and one warning is printed at the end; nothing fails. `make build-metrics-push` re-sends once the token is set |
| The collector is down when Prometheus scrapes | The series goes stale; the previous samples stay in Prometheus. The next scrape after a restart resumes |
| A leaked token pollutes the chart | A 256-bit token, fixed-time comparison, a closed target list, a body cap and a duration cap. The only thing a holder can do is set a duration on a chart the owner could already see; rotation is a restart |
| The exporter's family changes on a package upgrade | The collector's live `/metrics` test fails; the panel is never silently empty. If the change is wanted, the panel's query moves with it |
| The collector's DNS or certificate is not ready | The build's push warns once at the end and spools; the app and its own metrics are unaffected. The deploy's Caddyfile is installed as one file, so the block is inert until the name resolves |
| The release now builds and pushes two images | The collector layer is small; `image-check` adds two tiny builds. The site image is unchanged |
| `deploy` dominates the shared axis | The panel's sentence and hover names each value; the trend of each line is the question. A log axis, if wanted, is a change to every chart |
| The site's panel is empty until the first push | It is one "No data" panel on an owner-only page, the same as any instrument before its first sample |
| The collector's small config reader drifts from the site's | The same three tests hold both; the copy is deliberately small, and a shared project is a later decision if a third service needs it |
| The site is down while the collector keeps its numbers | The site's `/status` is served by the app, so while it is down no panel is reachable. Prometheus still has the data, and the collector's `/metrics` and Prometheus's own UI are the answer until the site returns; the next `/status` shows the completed history |

One thing the separate collector cannot fix, and the plan says so plainly: `/status` is the site's
page. When the site is down, the Build panel is unreachable with every other panel. What the collector
buys is that the numbers still reach Prometheus during that time, so the history is complete when the
site returns; reading them while the site is down is Prometheus's own UI on the loopback, or a later
`/status` window.

## Delivered when

The collector is its own rootless unit at its own name, with its own image, token and Prometheus job;
`make format-check`, `make check`, each suite, `make image` and `make deploy` each append a record to
`artifacts/build-metrics/records.jsonl`; with the URL and token set, a successful run sets
`build_duration_seconds{target}` and `/status` draws one line per target in a Build group; the site is
not in the push path, so the gate's numbers land while the site is broken; a failed run is in the
spool and the journal but not on the chart; a missing collector token stops the collector with a list
naming the key, while a build with no token prints one warning at the end and exits with the
command's status; a wrong token is 401; a push that cannot happen never fails a build; `make check`
is green, the collector's live-body test holds the family the panel queries, and the catalogue's
drift tests hold the Build panel to its external family.

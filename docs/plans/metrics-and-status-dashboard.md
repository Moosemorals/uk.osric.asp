<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Metrics, Prometheus and a status dashboard

Implementation plan for the monitoring half of [OPS-02](../issues/OPS-02.md): export the instruments
`PageMetrics` already records, run Prometheus beside the app, and render a status page whose charts
are our own SVG drawn from pure functions in `Core`. Metrics only — no traces, no logs, no OTLP.

## Status

Proposed, not started. Baseline on 2026-11-01: 102 core, 39 runtime, 235 web, 42 integration tests.

## What already exists

The hard half is done. `PageMetrics` records everything on a `System.Diagnostics.Metrics` meter named
`uk.osric.asp`; recording is nearly free until something listens, so the instruments are always on and
this work never touches the render path.

| Instrument | Type | Unit | Becomes, in Prometheus |
| --- | --- | --- | --- |
| `page.render.duration` | histogram | `s` | `page_render_duration_seconds_bucket` / `_sum` / `_count` |
| `page.compile.duration` | histogram | `s` | `page_compile_duration_seconds_*` |
| `page.handler.load.duration` | histogram | `s` | `page_handler_load_duration_seconds_*` |
| `page.handler.invoke.duration` | histogram | `s` | `page_handler_invoke_duration_seconds_*` |
| `page.save.duration` | histogram | `s` | `page_save_duration_seconds_*` |
| `page.image.size` | histogram | `By` | `page_image_size_bytes_*` |
| `page.cache.lookups` | counter | — | `page_cache_lookups_total` |
| `page.cache.staleness` | counter | — | `page_cache_staleness_total` |

Tags are a contract already documented as such in `PageMetrics`: `page.path`, `outcome`, `cache`,
`result`, `reason`. The dashboard queries depend on these names, so they join the contract.

`/heath` exists but nothing watches it: that is OPS-02. This plan supplies the monitor; alerting
stays with [OPS-05](../issues/OPS-05.md).

## Decisions

### 1. OpenTelemetry SDK, metrics only, no instrumentation packages

Two third-party packages, both pinned in `Directory.Packages.props`:

| Package | Version on 2026-11-01 |
| --- | --- |
| `OpenTelemetry.Extensions.Hosting` | `1.20.1` |
| `OpenTelemetry.Exporter.Prometheus.AspNetCore` | `1.20.1-beta.1` |

The exporter is still prerelease. Hand-rolling the exposition format instead means implementing
aggregation temporality, `_bucket`/`_sum`/`_count` composition, label escaping and staleness — a
spec detail that is easy to get subtly wrong and hard to test cheaply. The official SDK is the
smaller risk, and the package is pinned so an upgrade is a deliberate commit.

Metrics only falls out of the API: registering `.WithMetrics(...)` and nothing else means there is no
`TracerProvider` and no `LoggerProvider`. Deliberately **absent** is
`AddAspNetCoreInstrumentation()`, which is the tracing package.

No instrumentation package is needed for the framework's own numbers, because .NET 10 ships them as
meters:

```csharp
builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics
    .AddMeter(PageMetrics.MeterName)
    .AddMeter("Microsoft.AspNetCore.Hosting")        // http.server.request.duration
    .AddMeter("Microsoft.AspNetCore.Server.Kestrel") // connection and TLS metrics
    .AddMeter("System.Runtime")                      // GC, threadpool, working set
    .AddView("page.render.duration", new ExplicitBucketHistogramConfiguration { Boundaries = RenderBuckets })
    .AddView("page.image.size", new ExplicitBucketHistogramConfiguration { Boundaries = ImageBuckets })
    .AddPrometheusExporter());
```

### 2. OTel's default buckets are wrong for this meter, so the views are not optional

OpenTelemetry .NET defaults an explicit-bucket histogram to
`[0, 5, 10, 25, 50, 75, 100, 250, 500, 750, 1000, 2500, 5000, 7500, 10000]` **in instrument units**.
For an instrument measured in seconds that means a first bucket of 0–5 s, and every page render lands
in it: the heatmap in Phase 5 would be a single column, and the line chart's percentiles would be
worthless upstream of Prometheus too.

The SDK special-cases only its own instruments (`DefaultHistogramBoundShortSeconds`, used for
`http.server.request.duration` and friends); our meter gets the generic bounds unless a view says
otherwise. `page.render.duration` needs boundaries where renders actually live:

```csharp
private static readonly double[] RenderBuckets =
    [0.0005, 0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5];
```

`page.image.size` is bytes and gets its own scale, since bundling assemblies are tens of kilobytes:
`[1024, 4096, 16384, 32768, 65536, 131072, 262144, 524288, 1048576]`.

This is the sort of detail that looks like tuning and is really a correctness requirement: change the
boundaries and every stored dashboard query changes meaning, so they belong in `PageMetrics` as
named constants next to the instruments they describe.

### 3. The scrape endpoint is a route on the existing listener

`app.MapPrometheusScrapingEndpoint("/metrics")`, mapped next to `/heath` and before the page
catch-all, with `/metrics` added to `Pages:ReservedPaths`.

The consequence to own: the app's port is the public origin behind Caddy, so this route is reachable
from the internet unless the edge refuses it. Caddy gets an explicit rule, and it is the load-bearing
control:

```caddyfile
	# Prometheus scrapes inside the podman network. The public origin must not serve metrics.
	@metrics path /metrics
	respond @metrics 404
```

Caddy orders `respond` before `reverse_proxy`, so this needs no `handle` block; if that ordering ever
becomes unclear, `handle /metrics { respond 404 }` plus a `handle` for the proxy makes it explicit.

The alternative that removes the question — a second listener that the quadlet never publishes, via
`OpenTelemetry.Exporter.Prometheus.HttpListener` — was considered and deferred: it cannot be driven
through `WebApplicationFactory`, so the exposition format would have no in-process test.

### 4. Prometheus shares a rootless network with the app

The current topology gives Caddy the host network and publishes the rootless app on
`127.0.0.1:8080`. A second rootless container's `127.0.0.1` is its own namespace, not the host, so
neither direction works by loopback:

- Prometheus cannot scrape `127.0.0.1:8080`.
- The dashboard cannot query `127.0.0.1:9090`.

The fix is one user-defined network for both rootless units, with the aliases used in configuration:

```ini
# deploy/uk.osric.asp.network
[Network]
NetworkName=uk.osric.asp
```

Both containers add `Network=uk.osric.asp.network`; the app adds `NetworkAlias=app`, Prometheus keeps
`PublishPort=127.0.0.1:9090:9090` for local debugging only. Scrape target becomes `app:8080`, the
dashboard's base address `http://prometheus:9090`. Caddy keeps proxying the published loopback port
and needs no change beyond the metrics 404.

Prometheus is never routed through Caddy: it has no authentication and nothing public needs it.
`ssh -L 9090:127.0.0.1:9090 deploy@host` is the access story, as with any other internal service.

### 5. The dashboard is a shell endpoint, not a stored page

A stored page cannot make this request: `PageApiPolicy` is default-deny with no `System.Net` and no
`System.Threading.Tasks`, and the compiler's reference set excludes the shell. Pages are a runtime of
plain values, deliberately without I/O.

So `/status` is a minimal endpoint beside `/edit` and `/heath`:

- It can be async, which the page contract (`void Handle(IContext)`) cannot.
- It is exercised in-process by `Web.Tests` with a stubbed `HttpMessageHandler` and by
  `Web.IntegrationTests` through the real host.
- The page policy, `IContext` and the compiled-page runtime are untouched.

A dashboard is code, not content: it is a query set plus chart geometry, and neither belongs in a
text box. If editing it from the site ever matters, the follow-up is a narrow
`IContext` capability (`QueryMetrics(query, from, to, steps)` returning a `Core` value) — recorded
here as the considered alternative, not done now.

### 6. Charts are pure functions in `Core`

Geometry is arithmetic over values: it is the functional half of the repository and gets unit tests.

```csharp
public readonly record struct ChartSize(double Width, double Height);
public readonly record struct ChartPoint(DateTimeOffset At, double? Value); // null is a gap
public sealed record ChartSeries(string Label, IReadOnlyList<ChartPoint> Points);
public sealed record ChartTick(double Position, string Label);
public sealed record ChartAxis(string Title, IReadOnlyList<ChartTick> Ticks);

public static class LineChart {
    public static string Render(IReadOnlyList<ChartSeries> series, ChartAxis x, ChartAxis y, ChartSize size);
}
```

- Deterministic: same input, same string, so tests assert on the generated markup.
- Output is one `<svg>` with `viewBox="0 0 w h"`, `role="img"`, a `<title>`, gridlines, a polyline per
  series and text ticks. CSS sets the width; `vector-effect="non-scaling-stroke"` keeps strokes honest
  when it scales.
- Gaps split the polyline rather than drawing through them.
- Every label goes through `WebUtility.HtmlEncode`.
- Colors come from CSS custom properties, not from chart code, so the chart inherits the site's palette.

Tick selection is two small pure functions, each with a table of steps rather than arithmetic on the
span: times use `1s, 5s, 15s, 30s, 1m, 5m, 15m, 30m, 1h, 6h, 12h, 1d` and values use `1/2/5 × 10^n`.
Arithmetic steps produce axis labels like `7m34s`, which is how charts announce that nobody looked at
them.

### 7. Failure is a value, and there is a snapshot cache

The status page is what gets opened when things are broken, so it must survive Prometheus being down:

- `IMetricsSource.QueryAsync(...)` returns `MetricsSnapshot` or `MetricsUnavailable(reason)` — never a
  thrown exception for a timeout or a non-200. The endpoint renders the frame, the legend and a
  banner; only a bug in our own code is a 500.
- Both charts and banners render at any data quantity, including none.
- Responses are cached for about 15 seconds (`TimeProvider` for the test clock), so a refresh loop
  cannot turn one page view into four panels of `query_range` calls. The exporter's own 500 ms
  snapshot cache does not help with that: it is keyed on scrapes, not on our queries.
- Queries run with a 2 s timeout and `Accept: application/json`.

### 8. `/status` is owner-only for the MVP

`RequireAuthorization(EditorPolicy.Name)`, the same cookie as the editor. The charts key on
`page.path`, which enumerates the site's pages and their traffic; that is not a secret, but it is
internal, and the existing sign-in already exists. Opening it later is one line plus a decision about
how much the page shows.

The Prometheus API stays unreachable from the browser entirely: the page is server-rendered, there is
no JavaScript and no `/api` proxy.

### 9. Axis labels are Europe/London, which the base image cannot currently provide

Only the labels are local. Positions, queries and stored samples stay UTC: `query_range` takes unix
seconds, Prometheus stores UTC, and every internal timestamp is a `DateTimeOffset`. The zone exists so
a tick reads `14:05` instead of `13:05` in summer, and for nothing else.

That costs a deployment change, verified on 2026-11-01 against the images themselves:

- `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` has no `/usr/share/zoneinfo` at all, and
  `TimeZoneInfo.FindSystemTimeZoneById("Europe/London")` throws `TimeZoneNotFoundException` inside it.
- `mcr.microsoft.com/dotnet/aspnet:10.0-alpine-extra` resolves the zone (July `+01:00`, January
  `+00:00`) and is about 38 MB larger, 122 MB → 160 MB. Base layers are shared and cached, so a
  release still pushes only the ~180 kB app layer.

So `CONTAINER_BASE_IMAGE` in the Makefile moves to `:10.0-alpine-extra`, and `-extra` joins "do not
trim" as a deliberate base-image choice. The alternative — generating UK local time from the DST rule
in `Core` so no tzdata is needed — was rejected: it is a second, silent copy of a rule that
legislation can change, and it would only be right for the years it was written for.

`Core` never resolves a zone ID: `TimeZoneInfo` is a parameter, so the tick labeller stays pure and
the tests pin a zone explicitly. The shell resolves `Metrics:TimeZone` (`Europe/London`) once at
startup and, if the ID is unknown, falls back to UTC with a warning — a cosmetic axis detail must not
take the site down, and the validation step below catches the case that matters.

Daylight saving needs saying out loud: the axis is positioned by instant and labelled by clock time,
so the hour that repeats in October yields two ticks reading `01:00` and the hour that never happens
in March yields none. Spans over a day carry the date (`ddd d MMM HH:mm`), shorter spans just the
clock (`HH:mm`), and a span crossing a year boundary carries the year. Labels use invariant formatting
so the tests assert text, not culture data.

## Phases

### Phase 1 — exporter

`Directory.Packages.props` and `uk.osric.asp.Web.csproj` gain the two packages; `Program.cs` registers
the meters, the two views and the exporter; `/metrics` is mapped before the catch-all and added to
`Pages:ReservedPaths`; the Caddyfile gains the 404. Verify with `make run` and
`curl -s localhost:8080/metrics | head`.

The bucket boundaries land in `PageMetrics` as constants here, not in Phase 5, because stored data
gathered with the generic bounds cannot be reinterpreted later.

### Phase 2 — chart primitives, test-first

`Core`: `ChartSize`, `ChartPoint`, `ChartSeries`, `ChartTick`, `ChartAxis`, `NiceTicks` (time and
value), `LineChart.Render`, and a `SvgText.Escape`. Tests assert exact markup for: one series, several
series, a series with a gap, an empty series, a series whose every value is equal (flat line, nonzero
axis span), and a single point.

The tick labeller takes the `TimeZoneInfo` as an argument (Decision 9) and is tested against a fixed
zone in both winter and summer, plus a span crossing the October change.

Add `SvgText.Escape`: `WebUtility.HtmlEncode` already lives in the base class library, so `Core` needs
no package for it, but a named helper keeps escaping in one place and makes the rule visible next to
the markup it guards.

### Phase 3 — Prometheus client and `/status`

`Web/Metrics/` holds `IMetricsSource`, `PrometheusMetricsSource` (an `HttpClient` against
`/api/v1/query_range`) and the small DTOs `System.Text.Json` deserialises into.

Parsing rules, each with a test over a recorded body:

- `values` is `[[<unix seconds>, "<value>"], ...]`; the value is a **string** and is parsed with
  `CultureInfo.InvariantCulture`.
- `NaN`, `+Inf` and `-Inf` are gaps, not values.
- An empty `result` is an empty series, not an error.
- `status: "error"` carries `errorType` and `error`; surface the message in the banner.

`/status` queries four panels: render rate by outcome, render duration p50/p95, cache lookups, and
requests by `page.path` (top n). Configuration is `Metrics:PrometheusUrl`, defaulting to
`http://prometheus:9090`, and `Metrics:TimeZone`, defaulting to `Europe/London` (Decision 9); a
Development override points at `http://localhost:9090`, and an unreachable URL is a banner, not a
startup failure.

### Phase 4 — deployment

- `CONTAINER_BASE_IMAGE` becomes `mcr.microsoft.com/dotnet/aspnet:10.0-alpine-extra`, for the tzdata
  that `Europe/London` labels need (Decision 9).
- `deploy/uk.osric.asp.network`, plus `Network=`/`NetworkAlias=` in `deploy/uk.osric.asp.container`.
- `deploy/prometheus.container`: rootless, `Image=docker.io/prom/prometheus:v3.15.1` (pinned to an
  exact patch, like Caddy), `Network=uk.osric.asp.network`, `PublishPort=127.0.0.1:9090:9090`,
  `Volume=prometheus-data:/prometheus:U`, `Volume=…/prometheus.yml:/etc/prometheus/prometheus.yml:ro`,
  `PodmanArgs=--memory=256m`, `--storage.tsdb.retention.time=30d`,
  `--storage.tsdb.retention.size=2GB` — 10% of the VM's 20 GB disk.
- `deploy/prometheus.yml`: one static target, `app:8080`, `metrics_path: /metrics`, 15 s interval.
- `make deploy` installs the network unit first, then the app unit (so it restarts onto the network),
  then the config and Prometheus unit; `deploy/README.md` describes the new unit and the
  `ssh -L` story.
- The config is owned by `deploy`, unlike Caddy's root-owned file, because the container is rootless.

### Phase 5 — heatmap

Histogram buckets are cumulative counters, so a heatmap is per-bucket rates:

```promql
sum by (le) (rate(page_render_duration_seconds_bucket{page_path="/"}[5m]))
```

`le` is a string label; sort by its numeric value with `+Inf` last. Colour is a log-ish scale over the
cell rate, drawn as SVG `<rect>`s. `Core` gains `Heatmap.Render` with the same value types, and the
same rules: pure, deterministic, escaping, no JavaScript. This is also where the Phase 1 boundaries
earn their keep.

### Phase 6 — documentation and bookkeeping

- `docs/architecture.md`: a short "Metrics" section — the meter, the instrument-to-Prometheus naming
  contract, the two histogram views, the network topology, the Caddy 404 as a load-bearing rule, and
  why the base image is `-extra` (tzdata for the London axis labels).
- `docs/testing.md`: chart math and parsing are unit-level; `/status` and `/metrics` are integration
  level; no browser tests.
- `deploy/README.md`: the Prometheus unit, the network, retention and the loopback story.
- `docs/issues/README.md`: OPS-02's monitoring half is answered here; alerting stays with OPS-05.
- Mark this plan Implemented with test counts when it lands.

## Test matrix

| Level | Test | Proves |
| --- | --- | --- |
| Core | `LineChart` golden markup | geometry, ticks, gaps, escaping |
| Core | `NiceTicks` tables | labels stay readable at every span |
| Core | tick labels in a fixed zone | local clock text, date on long spans, the October DST repeat |
| Web | request with a query string | the page key and metric tag are the path, never the query |
| Web | `query_range` parsing fixtures | strings, `NaN`/`±Inf`, empty results, error payloads |
| Web | `/status` with a stubbed source | 200 with charts; 200 with a banner when unavailable |
| Integration | render a page, then `GET /metrics` | the exposition contains `page_render_duration_seconds_bucket` |
| Integration | `GET /status` anonymous | redirects to `/login` |
| Integration | `/metrics` reserved | a page stored at `/metrics` is never served |

## Edge cases and risks

- **Cardinality.** `page.path` is user-controlled and unbounded in principle; a very large site makes
  every histogram series multiply by page count. Only the owner can create pages, so this is a
  self-inflicted risk, but it is the first thing to trim with a `View` if the series count grows.
- **Naming skew.** The dashboard hard-codes instrument names converted by the exporter. Renaming an
  instrument or changing its unit silently empties a panel; the architecture note is the mitigation.
- **Prerelease exporter.** Pinned; upgrades are deliberate commits, and `/metrics` is covered by an
  integration test so a breaking change surfaces there.
- **Self-observation.** Scrapes are HTTP requests, so `http.server.request.duration` includes
  `/metrics` every 15 s. Harmless, worth knowing when reading QPS.
- **Prometheus at boot.** After a host reboot the two units start concurrently and Prometheus may
  record a failed scrape before the app is listening; the target recovers on the next interval with
  no intervention.
- **Retention.** 30 days and 2 GB against a 20 GB disk; the container is separately capped at 256 MB of
  RAM, so runaway cardinality fails a scrape rather than the host. Metrics are the cheapest data in
  the system and the easiest to let grow silently.
- **Clock.** Everything is UTC except tick text: Prometheus timestamps, stored samples and every
  `DateTimeOffset`; only axis labels are Europe/London (Decision 9), and only in the shell.
- **Page paths in labels.** A page key is a URL and already appears in access logs. No query string
  becomes a tag because the key comes from the route's `path` value, which is taken from the request
  path — `PagePath.Normalize` does not strip one, since `?` is just a character in a path. The
  protection is the router, and `AQueryStringDoesNotChangeThePageKeyAsync` pins it.
- **`/metrics` exposes page names.** The same information as the public site's own URLs, but it is
  unauthenticated; the Caddy rule and the loopback publish are the only controls, so any future change
  to either must keep both.

## Deliberately not doing

- Tracing, logs, OTLP, exemplars, auto-instrumentation packages.
- Grafana: the dashboard is the point of the exercise, and a second UI is a second thing to keep alive.
- Alertmanager and notifications: that is OPS-05, and it needs a channel that is not this page.
- Storing metrics in SQLite: Prometheus already is a time-series database, and duplicating it puts
  write pressure on the database that serves pages.
- JavaScript charts: no client-side code, no third-party chart bundle, no CDN.
- A public dashboard: owner-only for now, see Decision 8.

## Validation

1. `make test` — all four suites green, with the new Core and Web tests.
2. `make run`, then `curl -s localhost:8080/metrics | grep page_render_duration_seconds_bucket` after
   loading a page; `RenderBuckets` boundaries must appear as `le` values.
3. `make deploy`, then on the host: `systemctl --user status prometheus`, and the Prometheus targets
   page shows `app:8080` as `UP`.
4. On the deployed host, `curl -si https://moosemorals.com/metrics` must answer 404 from Caddy while
   the scrape target on the podman network still answers 200.
5. On the deployed host, the image must resolve the zone:
   `podman run --rm --entrypoint=/bin/sh <image> -c 'test -e /usr/share/zoneinfo/Europe/London'`.
6. Sign in, open `/status`, confirm four panels render with data and the time axis reads local clock
   time, then `systemctl --user stop prometheus` and confirm the page still renders with a banner
   instead of an error.

## Open questions

- **Retention window.** 30 days and the 2 GB cap are guesses; the window should be whatever makes "what
  happened last month" answerable without holding a year of nothing.
- **`page.path` on histograms.** Per-page latency is genuinely useful and multiplies series. If the
  heatmap only ever looks at one page at a time, a copy of the instruments without the tag may be
  better than trimming it everywhere.
- **Second account.** `/status` inherits the editor policy, so every editor sees it. That is fine
  while the account table is the owner list.

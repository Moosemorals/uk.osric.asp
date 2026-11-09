// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

using uk.osric.asp.Core.Charting;

/// <summary>Runs the dashboard's queries and turns the answers into charts.</summary>
/// <remarks>
/// The whole page is cached for a few seconds, because the page is refreshed by a person and every
/// view runs about twenty queries; a refresh loop must not become a load test. A remote failure is
/// a banner,
/// never an exception, so the page still renders when Prometheus does not.
/// </remarks>
internal sealed class Dashboard {

    private const int PanelHeight = 200;
    // Fifteen `le` rows need more height than a line chart, or 11px labels overlap.
    private const int HeatmapHeight = 320;

    private const string HeatmapTitle = "Render duration buckets";
    // Each cell is a per-second rate of the bucket's counter, not a raw count.
    private const string HeatmapValueUnit = "renders/s";

    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(15);

    // Cumulative counters, one series per le boundary; the difference between neighbours is the
    // interesting number, which is what Heatmap.FromCumulative computes.
    private const string HeatmapQuery = "sum by (le) (rate(page_render_duration_seconds_bucket[5m]))";

    private static readonly PanelDefinition[] Definitions = [
        new("Requests by page", "per second", UnitScale.Plain, [
            new("topk(5, sum by (page_path) (rate(page_render_duration_seconds_count[5m])))", "requests"),
        ]),
        // Attribution for the heatmap's rare slow rows: total render rate minus the rate that
        // finished within half a second, per page.
        new("Pages slower than 0.5 s", "per second", UnitScale.Plain, [
            new("topk(5, sum by (page_path) (rate(page_render_duration_seconds_count[5m])) - sum by (page_path) (rate(page_render_duration_seconds_bucket{le=\"0.5\"}[5m])))", "slow pages"),
        ]),
        new("Render rate by outcome", "per second", UnitScale.Plain, [
            new("sum by (outcome) (rate(page_render_duration_seconds_count[5m]))", "renders"),
            new("sum(rate(page_render_abandoned_total[5m]))", "abandoned"),
        ]),
        new("Render duration p50/p95", "seconds", UnitScale.Duration, [
            new("histogram_quantile(0.5, sum by (le) (rate(page_render_duration_seconds_bucket[5m])))", "p50"),
            new("histogram_quantile(0.95, sum by (le) (rate(page_render_duration_seconds_bucket[5m])))", "p95"),
        ]),
        new("Render slot wait p50/p95", "seconds", UnitScale.Duration, [
            new("histogram_quantile(0.5, sum by (le) (rate(page_render_wait_seconds_bucket[5m])))", "p50"),
            new("histogram_quantile(0.95, sum by (le) (rate(page_render_wait_seconds_bucket[5m])))", "p95"),
        ]),
        new("Compiles by reason", "per second", UnitScale.Plain, [
            new("sum by (reason) (rate(page_compile_duration_seconds_count[5m]))", "compiles"),
        ]),
        new("Compile duration p95", "seconds", UnitScale.Duration, [
            new("histogram_quantile(0.95, sum by (le) (rate(page_compile_duration_seconds_bucket[5m])))", "p95"),
        ]),
        new("Stale images by reason", "per second", UnitScale.Plain, [
            new("sum by (reason) (rate(page_cache_staleness_total[5m]))", "stale"),
        ]),
        new("Cache lookups by result", "per second", UnitScale.Plain, [
            new("sum by (result) (rate(page_cache_lookups_total[5m]))", "lookups"),
        ]),
        new("Process memory", "bytes", UnitScale.Bytes, [
            new("dotnet_process_memory_working_set_bytes", "working set"),
        ]),
        new("Process CPU", "cores", UnitScale.Plain, [
            new("sum(rate(dotnet_process_cpu_time_seconds_total[5m]))", "cpu"),
        ]),
        new("Connections and active requests", "count", UnitScale.Plain, [
            new("sum(http_server_active_requests)", "active requests"),
            new("sum(kestrel_active_connections)", "open connections"),
            new("sum(kestrel_queued_connections)", "queued connections"),
        ]),
        new("HTTP status mix", "per second", UnitScale.Plain, [
            new("sum by (http_response_status_code) (rate(http_server_request_duration_seconds_count[5m]))", "requests"),
        ]),
        new("Average assembly size", "bytes", UnitScale.Bytes, [
            new("sum(rate(page_image_size_bytes_sum[5m])) / sum(rate(page_image_size_bytes_count[5m]))", "average"),
        ]),
    ];

    /// <summary>Every PromQL expression the dashboard runs, so a test can pin their shape.</summary>
    internal static IReadOnlyList<string> Queries { get; } =
        [.. Definitions.SelectMany(definition => definition.Queries).Select(query => query.PromQL)];

    private readonly IMetricsSource _source;
    private readonly TimeZoneInfo _zone;
    private readonly TimeProvider _time;
    private readonly ILogger<Dashboard> _logger;
    private readonly Lock _gate = new();

    // One page per window: switching ranges must not throw away the page for the range behind it.
    private readonly Dictionary<string, CachedPage> _cached = [];

    public Dashboard(
        IMetricsSource source,
        MetricsOptions options,
        TimeProvider time,
        ILogger<Dashboard> logger) {

        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _source = source;
        _zone = options.TimeZone;
        _time = time;
        _logger = logger;
    }

    /// <summary>Builds the page for one window, reusing the last one while it is still fresh.</summary>
    /// <param name="duration">The window the nav selected.</param>
    /// <param name="cancellationToken">Aborted when the request is abandoned.</param>
    public async Task<StatusPage> GetAsync(StatusDuration duration, CancellationToken cancellationToken) {
        DateTimeOffset now = _time.GetUtcNow();

        lock (_gate) {
            if (_cached.TryGetValue(duration.Token, out CachedPage? cached) && now - cached.At < CacheFor) {
                return cached.Page;
            }
        }

        StatusPage page = await BuildAsync(duration, now, cancellationToken);

        lock (_gate) {
            _cached[duration.Token] = new CachedPage(page, now);
        }

        return page;
    }

    private async Task<StatusPage> BuildAsync(StatusDuration duration, DateTimeOffset now, CancellationToken cancellationToken) {
        DateTimeOffset from = now - duration.Window;
        TimeSpan step = duration.Step;
        List<StatusPanel> panels = [];
        string? banner = null;

        foreach (PanelDefinition definition in Definitions) {
            List<ChartSeries> series = [];
            bool failed = false;

            foreach (PanelQuery query in definition.Queries) {
                QueryResult result = await _source.QueryAsync(query.PromQL, from, now, step, cancellationToken);
                switch (result) {
                    case MetricsUnavailable unavailable:
                        failed = true;
                        banner ??= unavailable.Reason;
                        break;
                    case MetricsMatrix matrix:
                        // Prometheus names a series only when the expression leaves a label on it;
                        // a percentile is one anonymous line, so the query's own name labels it.
                        series.AddRange(matrix.Series.Select(line =>
                            line.Label.Length == 0 ? line with { Label = query.Label } : line));
                        break;
                }
            }

            if (!failed) {
                panels.Add(Render(definition, series, duration.ChartWidth));
            }
        }

        QueryResult buckets = await _source.QueryAsync(HeatmapQuery, from, now, step, cancellationToken);
        switch (buckets) {
            case MetricsUnavailable unavailable:
                banner ??= unavailable.Reason;
                break;
            case MetricsMatrix matrix:
                panels.Add(RenderHeatmap(matrix.Series, duration.ChartWidth));
                break;
        }

        if (banner is not null) {
            // The page shows the banner; the journal keeps the reason for the person with a shell.
            _logger.LogWarning("The metrics dashboard is incomplete: {Reason}", banner);
        }

        return new StatusPage(panels, banner);
    }

    private StatusPanel RenderHeatmap(IReadOnlyList<ChartSeries> series, int width) {
        List<ChartPoint> samples = [.. series
            .SelectMany(line => line.Points)
            .Where(point => point.Value is not null)];

        string heatmapTitle = $"{HeatmapTitle} ({HeatmapValueUnit})";

        if (samples.Count == 0) {
            return new StatusPanel(heatmapTitle, null, []);
        }

        IReadOnlyList<HeatmapRow> rows = Heatmap.FromCumulative(
            [.. series.Select(line => new HeatmapRow(line.Label, line.Points))]);

        DateTimeOffset earliest = samples.Min(point => point.At);
        DateTimeOffset latest = samples.Max(point => point.At);
        ChartAxis x = new("time", NiceTicks.ForTime(earliest, latest, _zone));
        string svg = Heatmap.Render(
            rows, x, new ChartSize(width, HeatmapHeight), heatmapTitle, "seconds", HeatmapValueUnit);

        return new StatusPanel(heatmapTitle, svg, []);
    }

    private StatusPanel Render(PanelDefinition definition, IReadOnlyList<ChartSeries> series, int width) {
        List<ChartPoint> samples = [.. series
            .SelectMany(line => line.Points)
            .Where(point => point.Value is not null)];

        string title = $"{definition.Title} ({definition.Unit})";

        if (samples.Count == 0) {
            return new StatusPanel(title, null, []);
        }

        DateTimeOffset earliest = samples.Min(point => point.At);
        DateTimeOffset latest = samples.Max(point => point.At);
        double min = samples.Min(point => point.Value!.Value);
        double max = samples.Max(point => point.Value!.Value);

        string unit = definition.Unit;
        if (definition.Scale != UnitScale.Plain) {
            ChartScale scale = definition.Scale == UnitScale.Duration
                ? ValueUnit.ForSeconds(min, max)
                : ValueUnit.ForBytes(min, max);
            (min, max, unit) = (scale.Min, scale.Max, scale.Unit);
        }

        ChartAxis x = new("time", NiceTicks.ForTime(earliest, latest, _zone));
        ChartAxis y = new(unit, NiceTicks.ForValue(min, max));
        string svg = LineChart.Render(series, x, y, new ChartSize(width, PanelHeight));

        return new StatusPanel($"{definition.Title} ({unit})", svg, [.. series.Select(line => line.Label)]);
    }

    private sealed record CachedPage(StatusPage Page, DateTimeOffset At);

    private sealed record PanelDefinition(
        string Title,
        string Unit,
        UnitScale Scale,
        IReadOnlyList<PanelQuery> Queries);

    private enum UnitScale { Plain, Duration, Bytes }

    private sealed record PanelQuery(string PromQL, string Label);
}

// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

using uk.osric.asp.Core;

/// <summary>Runs the dashboard's queries and turns the answers into charts.</summary>
/// <remarks>
/// The whole page is cached for a few seconds, because the page is refreshed by a person and every
/// view is eight queries; a refresh loop must not become a load test. A remote failure is a banner,
/// never an exception, so the page still renders when Prometheus does not.
/// </remarks>
internal sealed class MetricsDashboard {

    private const int PanelWidth = 640;
    private const int PanelHeight = 200;
    // Fifteen `le` rows need more height than a line chart, or 11px labels overlap.
    private const int HeatmapHeight = 320;

    private const string HeatmapTitle = "Render duration buckets";

    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(15);

    // Cumulative counters, one series per le boundary; the difference between neighbours is the
    // interesting number, which is what Heatmap.FromCumulative computes.
    private const string HeatmapQuery = "sum by (le) (rate(page_render_duration_seconds_bucket[5m]))";

    private static readonly PanelDefinition[] Definitions = [
        new("Requests by page", "per second", [
            new("topk(5, sum by (page_path) (rate(page_render_duration_seconds_count[5m])))", "requests"),
        ]),
        new("Render rate by outcome", "per second", [
            new("sum by (outcome) (rate(page_render_duration_seconds_count[5m]))", "renders"),
            new("sum(rate(page_render_abandoned_total[5m]))", "abandoned"),
        ]),
        new("Render duration p50/p95", "seconds", [
            new("histogram_quantile(0.5, sum by (le) (rate(page_render_duration_seconds_bucket[5m])))", "p50"),
            new("histogram_quantile(0.95, sum by (le) (rate(page_render_duration_seconds_bucket[5m])))", "p95"),
        ]),
        new("Render slot wait p50/p95", "seconds", [
            new("histogram_quantile(0.5, sum by (le) (rate(page_render_wait_seconds_bucket[5m])))", "p50"),
            new("histogram_quantile(0.95, sum by (le) (rate(page_render_wait_seconds_bucket[5m])))", "p95"),
        ]),
        new("Cache lookups by result", "per second", [
            new("sum by (result) (rate(page_cache_lookups_total[5m]))", "lookups"),
        ]),
    ];

    /// <summary>Every PromQL expression the dashboard runs, so a test can pin their shape.</summary>
    internal static IReadOnlyList<string> Queries { get; } =
        [.. Definitions.SelectMany(definition => definition.Queries).Select(query => query.PromQL)];

    private readonly IMetricsSource _source;
    private readonly TimeZoneInfo _zone;
    private readonly TimeProvider _time;
    private readonly ILogger<MetricsDashboard> _logger;
    private readonly Lock _gate = new();

    private StatusPage? _cached;
    private DateTimeOffset _cachedAt;

    public MetricsDashboard(
        IMetricsSource source,
        MetricsOptions options,
        TimeProvider time,
        ILogger<MetricsDashboard> logger) {

        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _source = source;
        _zone = options.TimeZone;
        _time = time;
        _logger = logger;
    }

    /// <summary>Builds the page, reusing the last one while it is still fresh.</summary>
    /// <param name="cancellationToken">Aborted when the request is abandoned.</param>
    public async Task<StatusPage> GetAsync(CancellationToken cancellationToken) {
        DateTimeOffset now = _time.GetUtcNow();

        lock (_gate) {
            if (_cached is not null && now - _cachedAt < CacheFor) {
                return _cached;
            }
        }

        StatusPage page = await BuildAsync(now, cancellationToken);

        lock (_gate) {
            _cached = page;
            _cachedAt = now;
        }

        return page;
    }

    private async Task<StatusPage> BuildAsync(DateTimeOffset now, CancellationToken cancellationToken) {
        DateTimeOffset from = now - Window;
        List<StatusPanel> panels = [];
        string? banner = null;

        foreach (PanelDefinition definition in Definitions) {
            List<ChartSeries> series = [];
            bool failed = false;

            foreach (PanelQuery query in definition.Queries) {
                MetricsQueryResult result = await _source.QueryAsync(query.PromQL, from, now, Step, cancellationToken);
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
                panels.Add(Render(definition, series));
            }
        }

        MetricsQueryResult buckets = await _source.QueryAsync(HeatmapQuery, from, now, Step, cancellationToken);
        switch (buckets) {
            case MetricsUnavailable unavailable:
                banner ??= unavailable.Reason;
                break;
            case MetricsMatrix matrix:
                panels.Add(RenderHeatmap(matrix.Series));
                break;
        }

        if (banner is not null) {
            // The page shows the banner; the journal keeps the reason for the person with a shell.
            _logger.LogWarning("The metrics dashboard is incomplete: {Reason}", banner);
        }

        return new StatusPage(panels, banner);
    }

    private StatusPanel RenderHeatmap(IReadOnlyList<ChartSeries> series) {
        List<ChartPoint> samples = [.. series
            .SelectMany(line => line.Points)
            .Where(point => point.Value is not null)];

        if (samples.Count == 0) {
            return new StatusPanel(HeatmapTitle, null, []);
        }

        IReadOnlyList<HeatmapRow> rows = Heatmap.FromCumulative(
            [.. series.Select(line => new HeatmapRow(line.Label, line.Points))]);

        DateTimeOffset earliest = samples.Min(point => point.At);
        DateTimeOffset latest = samples.Max(point => point.At);
        ChartAxis x = new("time", NiceTicks.Time(earliest, latest, _zone));
        string svg = Heatmap.Render(rows, x, new ChartSize(PanelWidth, HeatmapHeight), HeatmapTitle, "seconds");

        return new StatusPanel(HeatmapTitle, svg, []);
    }

    private StatusPanel Render(PanelDefinition definition, IReadOnlyList<ChartSeries> series) {
        List<ChartPoint> samples = [.. series
            .SelectMany(line => line.Points)
            .Where(point => point.Value is not null)];

        if (samples.Count == 0) {
            return new StatusPanel(definition.Title, null, []);
        }

        DateTimeOffset earliest = samples.Min(point => point.At);
        DateTimeOffset latest = samples.Max(point => point.At);
        double min = samples.Min(point => point.Value!.Value);
        double max = samples.Max(point => point.Value!.Value);

        ChartAxis x = new("time", NiceTicks.Time(earliest, latest, _zone));
        ChartAxis y = new(definition.Unit, NiceTicks.Value(min, max));
        string svg = LineChart.Render(series, x, y, new ChartSize(PanelWidth, PanelHeight));

        return new StatusPanel(definition.Title, svg, [.. series.Select(line => line.Label)]);
    }

    private sealed record PanelDefinition(string Title, string Unit, IReadOnlyList<PanelQuery> Queries);

    private sealed record PanelQuery(string PromQL, string Label);
}

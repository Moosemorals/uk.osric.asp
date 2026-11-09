// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

using uk.osric.asp.Core.Charting;

/// <summary>Turns one panel and one window into one drawing.</summary>
/// <remarks>
/// This is the only code that runs a panel's queries and turns the answers into SVG, which is what lets
/// the page embed a chart and the chart endpoint serve one: both call this and get the same bytes.
/// </remarks>
/// <param name="source">Where the answers come from.</param>
/// <param name="options">The zone the time labels are written in.</param>
internal sealed class ChartRenderer(IMetricsSource source, MetricsOptions options) {

    // Twenty per cent larger than the first draft: twenty-eight small multiples is dense enough
    // without the plots themselves being cramped.
    private static readonly ChartSize LineSize = new(504, 216);
    private static readonly ChartSize StackedSize = new(504, 240);
    private static readonly ChartSize HeatmapSize = new(504, 336);
    private const string HeatmapValueUnit = "per second";
    private const string EmptyText = "No data in this window.";

    /// <summary>Draws one panel for one window.</summary>
    /// <param name="panel">The panel, whose chart kind decides the drawing.</param>
    /// <param name="window">The start and length every query is run over.</param>
    /// <param name="titleHref">
    /// Where the drawing's title links, or null when the drawing is already the page that link would
    /// open — which is what the chart endpoint passes.
    /// </param>
    /// <param name="cancellationToken">Aborted when the request is abandoned.</param>
    /// <returns>The drawing, or why it could not be drawn; never throws for a remote failure.</returns>
    public async Task<ChartResult> RenderAsync(
        Panel panel,
        ChartWindow window,
        string? titleHref,
        CancellationToken cancellationToken) {

        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(window);

        List<ChartSeries> series = [];

        foreach (PanelQuery query in panel.Queries) {
            QueryResult result = await source.QueryAsync(query.Query, window.Start, window.End, window.Step, cancellationToken);

            if (result is MetricsUnavailable unavailable) {
                return new ChartResult(null, unavailable.Reason);
            }

            foreach (ChartSeries line in ((MetricsMatrix)result).Series) {
                series.Add(line.Label.Length == 0 && query.Label.Length > 0
                    ? line with { Label = query.Label }
                    : line);
            }
        }

        ChartFrame frame = new(panel.Id, panel.Title, panel.YTitle, EmptyText, options.TimeZone, titleHref);
        ChartAxis time = new("time", NiceTicks.ForTime(window.Start, window.End, options.TimeZone));

        return panel.Chart switch {
            PanelChart.Heatmap => new ChartResult(
                Heatmap.Render(ToRows(series), time, HeatmapSize, frame, HeatmapValueUnit),
                null),
            PanelChart.StackedArea => new ChartResult(
                StackedAreaChart.Render(series, time, ValueAxis(panel, series), StackedSize, frame),
                null),
            _ => new ChartResult(
                LineChart.Render(series, time, ValueAxis(panel, series), LineSize, frame),
                null),
        };
    }

    // The differencing has to happen before a chart folds or merges anything, so it is the first thing
    // that happens to a heatmap's answer.
    private static IReadOnlyList<HeatmapRow> ToRows(IReadOnlyList<ChartSeries> series) =>
        [.. Heatmap.FromCumulative([.. series.Select(line => new HeatmapRow(line.Label, line.Points))])];

    private static ChartAxis ValueAxis(Panel panel, IReadOnlyList<ChartSeries> series) {
        (double min, double max) = Extremes(series);

        if (panel.Unit == PanelUnit.Bytes) {
            ChartScale scale = ValueUnit.ForBytes(min, max);

            return new ChartAxis(scale.Unit, NiceTicks.ForValue(scale.Min, scale.Max));
        }

        return new ChartAxis(panel.YTitle, NiceTicks.ForValue(min, max));
    }

    private static (double Min, double Max) Extremes(IReadOnlyList<ChartSeries> series) {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;

        foreach (ChartSeries line in series) {
            foreach (ChartPoint point in line.Points) {
                if (point.Value is not double value || !double.IsFinite(value)) {
                    continue;
                }

                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
        }

        return min > max ? (0, 1) : (min, max);
    }
}

/// <summary>One panel's drawing, or the reason there is none.</summary>
/// <param name="Svg">The drawing, or null when a query failed.</param>
/// <param name="Reason">Why a query failed, or null when the drawing was made.</param>
internal sealed record ChartResult(string? Svg, string? Reason);

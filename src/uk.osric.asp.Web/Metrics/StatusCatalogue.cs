// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Pages;

/// <summary>The panels the status page draws, one per instrument and one per host question.</summary>
/// <remarks>
/// <para>
/// The catalogue is the only source of panels: <see cref="Dashboard"/> holds no queries. Each entry
/// names an instrument (or, in the Host group, the framework families it reads), and the chart kind
/// follows the instrument's kind — a histogram is a heatmap, a counter is a line chart, a gauge is a
/// line chart, and a counter whose labels partition one total is a stacked area.
/// </para>
/// <para>
/// <c>StatusCatalogueTests</c> asserts that mapping is a bijection over the meters' inventories and
/// that every family a query names is accounted for, which is what stops the page drifting from the
/// instruments it is built from.
/// </para>
/// </remarks>
internal static class StatusCatalogue {

    /// <summary>The groups, in the order the page lists them.</summary>
    public static IReadOnlyList<string> Groups { get; } = ["Pages", "Assistant", "Host"];

    /// <summary>Every panel, in the order the page draws them.</summary>
    public static IReadOnlyList<Panel> Panels { get; } = [
        // Pages: nine histograms, four counters, three gauges.
        Heatmap(Page("page.render.duration"), "Render duration", "seconds"),
        Heatmap(Page("page.render.wait"), "Render slot wait", "seconds"),
        Heatmap(Page("page.compile.duration"), "Compile duration", "seconds"),
        Heatmap(Page("page.load.duration"), "Loaded image duration", "seconds"),
        Heatmap(Page("page.invoke.duration"), "Page code duration", "seconds"),
        Heatmap(Page("page.save.duration"), "Save duration", "seconds"),
        Heatmap(Page("page.fetch.duration"), "Fetch duration", "seconds"),
        Heatmap(Page("page.import.duration"), "Import duration", "seconds"),
        Heatmap(Page("page.image.size"), "Assembly size", "bytes"),
        Line(Page("page.render.abandoned"), "Abandoned renders", "renders/s", topk: "page_path"),
        Stacked(Page("page.cache.lookups"), "Cache lookups", "lookups/s", split: "result"),
        Stacked(Page("page.cache.staleness"), "Stale images", "images/s", split: "reason"),
        Line(Page("page.layout.fallback"), "Layout fallbacks", "fallbacks/s", topk: "page_path"),
        Line(Page("page.storage.payload.size"), "Stored payload", "bytes", unit: PanelUnit.Bytes),
        Line(Page("page.storage.database.size"), "Database size", "bytes", unit: PanelUnit.Bytes),
        Line(Page("page.storage.volume.capacity"), "Volume capacity", "bytes", unit: PanelUnit.Bytes),

        // Assistant: six histograms and the token counter.
        Heatmap(Assistant("assistant.run.duration"), "Run duration", "seconds"),
        Heatmap(Assistant("assistant.run.steps"), "Steps per run", "steps"),
        Heatmap(Assistant("assistant.run.tool_calls"), "Tool calls per run", "calls"),
        Heatmap(Assistant("assistant.provider.first_token.duration"), "First token latency", "seconds"),
        Heatmap(Assistant("assistant.provider.request.duration"), "Provider request duration", "seconds"),
        Heatmap(Assistant("assistant.tool.duration"), "Tool duration", "seconds"),
        Stacked(Assistant("assistant.tokens"), "Tokens", "tokens/s", split: "kind"),

        // Host: the framework's meters, which are not instruments this repository registers.
        new Panel {
            Id = "host.memory",
            Group = "Host",
            Title = "Process memory",
            YTitle = "bytes",
            Chart = PanelChart.Line,
            Queries = [new PanelQuery("dotnet_process_memory_working_set_bytes", "working set")],
            Unit = PanelUnit.Bytes,
            HostFamilies = ["dotnet_process_memory_working_set_bytes"],
        },
        new Panel {
            Id = "host.cpu",
            Group = "Host",
            Title = "Process CPU",
            YTitle = "cores",
            Chart = PanelChart.Line,
            Queries = [new PanelQuery("sum(rate(dotnet_process_cpu_time_seconds_total[5m]))", "cpu")],
            Unit = PanelUnit.Plain,
            HostFamilies = ["dotnet_process_cpu_time_seconds_total"],
        },
        new Panel {
            Id = "host.connections",
            Group = "Host",
            Title = "Connections and active requests",
            YTitle = "count",
            Chart = PanelChart.Line,
            Queries = [
                new PanelQuery("sum(http_server_active_requests)", "active requests"),
                new PanelQuery("sum(kestrel_active_connections)", "open connections"),
                new PanelQuery("sum(kestrel_queued_connections)", "queued connections"),
            ],
            Unit = PanelUnit.Plain,
            HostFamilies = ["http_server_active_requests", "kestrel_active_connections", "kestrel_queued_connections"],
        },
        new Panel {
            Id = "host.request.duration",
            Group = "Host",
            Title = "Request duration",
            YTitle = "seconds",
            Chart = PanelChart.Heatmap,
            Queries = [new PanelQuery("sum by (le) (rate(http_server_request_duration_seconds_bucket[5m]))", string.Empty)],
            Unit = PanelUnit.Plain,
            HostFamilies = ["http_server_request_duration_seconds"],
        },
        new Panel {
            Id = "host.status.mix",
            Group = "Host",
            Title = "HTTP status mix",
            YTitle = "requests/s",
            Chart = PanelChart.StackedArea,
            Queries = [new PanelQuery("sum by (http_response_status_code) (rate(http_server_request_duration_seconds_count[5m]))", string.Empty)],
            Unit = PanelUnit.Plain,
            HostFamilies = ["http_server_request_duration_seconds"],
            PartsOfAWhole = "http_response_status_code",
        },
    ];

    // The inventory is the one place a name is written; the catalogue asks for it by name so a reorder
    // cannot quietly swap two panels.
    private static MetricInstrument Page(string name) =>
        PageMetrics.Instruments.Single(instrument => instrument.Name == name);

    private static MetricInstrument Assistant(string name) =>
        AssistantMetrics.Instruments.Single(instrument => instrument.Name == name);

    /// <summary>Finds a panel by the id a link names.</summary>
    /// <param name="id">The panel id, which is the instrument name for an instrument panel.</param>
    /// <returns>The panel, or null when nothing has that id.</returns>
    public static Panel? Find(string? id) =>
        Panels.FirstOrDefault(panel => string.Equals(panel.Id, id, StringComparison.Ordinal));

    private static Panel Heatmap(MetricInstrument instrument, string title, string yTitle) => new() {
        Id = instrument.Name,
        Group = GroupOf(instrument.Name),
        Title = title,
        YTitle = yTitle,
        Chart = PanelChart.Heatmap,
        Queries = [new PanelQuery($"sum by (le) (rate({PrometheusNames.Buckets(instrument)}[5m]))", string.Empty)],
        Unit = PanelUnit.Plain,
        InstrumentName = instrument.Name,
    };

    private static Panel Line(MetricInstrument instrument, string title, string yTitle, string? topk = null, PanelUnit unit = PanelUnit.Plain) {
        string query = topk is null
            ? $"sum(rate({PrometheusNames.Total(instrument)}[5m]))"
            : $"topk(8, sum by ({topk}) (rate({PrometheusNames.Total(instrument)}[5m])))";

        return new Panel {
            Id = instrument.Name,
            Group = GroupOf(instrument.Name),
            Title = title,
            YTitle = yTitle,
            Chart = PanelChart.Line,
            Queries = [new PanelQuery(query, string.Empty)],
            Unit = unit,
            InstrumentName = instrument.Name,
        };
    }

    private static Panel Stacked(MetricInstrument instrument, string title, string yTitle, string split) => new() {
        Id = instrument.Name,
        Group = GroupOf(instrument.Name),
        Title = title,
        YTitle = yTitle,
        Chart = PanelChart.StackedArea,
        Queries = [new PanelQuery($"sum by ({split}) (rate({PrometheusNames.Total(instrument)}[5m]))", string.Empty)],
        Unit = PanelUnit.Plain,
        InstrumentName = instrument.Name,
        PartsOfAWhole = split,
    };

    private static string GroupOf(string instrumentName) =>
        instrumentName.StartsWith("page.", StringComparison.Ordinal) ? "Pages"
        : instrumentName.StartsWith("assistant.", StringComparison.Ordinal) ? "Assistant"
        : "Host";
}

/// <summary>One chart on the status page.</summary>
internal sealed record Panel {

    /// <summary>The panel's id: the instrument's name, or a <c>host.</c> name for a framework panel.</summary>
    public required string Id { get; init; }

    /// <summary>The group the panel is listed under.</summary>
    public required string Group { get; init; }

    /// <summary>The heading the drawing carries.</summary>
    public required string Title { get; init; }

    /// <summary>What the value axis measures. A heatmap's is its bucket unit.</summary>
    public required string YTitle { get; init; }

    /// <summary>How the panel is drawn.</summary>
    public required PanelChart Chart { get; init; }

    /// <summary>The queries whose answers the drawing is made from.</summary>
    public required IReadOnlyList<PanelQuery> Queries { get; init; }

    /// <summary>How a line chart's value axis is rescaled.</summary>
    public PanelUnit Unit { get; init; } = PanelUnit.Plain;

    /// <summary>The instrument the panel draws, or null for a Host panel.</summary>
    public string? InstrumentName { get; init; }

    /// <summary>The framework families a Host panel reads.</summary>
    public IReadOnlyList<string> HostFamilies { get; init; } = [];

    /// <summary>The label whose values partition one total, when the panel is a stacked area.</summary>
    public string? PartsOfAWhole { get; init; }
}

/// <summary>One PromQL expression a panel runs, and the name to give an anonymous series.</summary>
/// <param name="Query">The expression.</param>
/// <param name="Label">The label a series without one takes.</param>
internal sealed record PanelQuery(string Query, string Label);

/// <summary>How a line chart's value axis is rescaled.</summary>
internal enum PanelUnit {

    /// <summary>Counts and rates, as they are recorded.</summary>
    Plain,

    /// <summary>Bytes, in the largest unit that keeps the labels short.</summary>
    Bytes,
}

/// <summary>How a panel is drawn.</summary>
internal enum PanelChart {

    /// <summary>A distribution over buckets, drawn as a heatmap.</summary>
    Heatmap,

    /// <summary>A value over time, one line per label.</summary>
    Line,

    /// <summary>Parts of one total, stacked.</summary>
    StackedArea,
}

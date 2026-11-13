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
/// line chart, and a counter whose labels partition one total is a stacked area. A panel may instead
/// compose existing instruments when one question spans them, as the render chain does.
/// </para>
/// <para>
/// <c>StatusCatalogueTests</c> asserts every instrument has exactly one owning panel, that a composed
/// panel names its sources, and that every family a query names is accounted for, which is what stops
/// the page drifting from the instruments it is built from.
/// </para>
/// </remarks>
internal static class StatusCatalogue {

    /// <summary>The groups, in the order the page lists them.</summary>
    public static IReadOnlyList<string> Groups { get; } = ["Pages", "Assistant", "Host"];

    /// <summary>Every panel, in the order the page draws them.</summary>
    public static IReadOnlyList<Panel> Panels { get; } = [
        // Pages: nine histograms, four counters, three gauges, and one panel that composes instruments.
        Heatmap(Page("page.render.duration"), "Render duration",
            "How long a render takes from request to response, split into duration buckets so slow renders stand out.",
            "seconds", "renders/s"),
        Heatmap(Page("page.render.wait"), "Render slot wait",
            "How long a render waits for a free render slot before it starts; a growing wait means renders are queuing.",
            "seconds", "renders/s"),
        RenderChain(),
        Heatmap(Page("page.compile.duration"), "Compile duration",
            "How long Roslyn spends compiling one page, split into duration buckets.",
            "seconds", "compiles/s"),
        Heatmap(Page("page.load.duration"), "Loaded image duration",
            "How long loading a stored compiled image into its assembly load context takes.",
            "seconds", "loads/s"),
        Heatmap(Page("page.invoke.duration"), "Page code duration",
            "How long a page's own code runs, separate from compiling and loading it.",
            "seconds", "invokes/s"),
        Heatmap(Page("page.save.duration"), "Save duration",
            "How long saving an edited page takes, including compiling and storing it.",
            "seconds", "saves/s"),
        Heatmap(Page("page.fetch.duration"), "Fetch duration",
            "How long one outbound fetch takes, redirects included.",
            "seconds", "fetches/s"),
        Heatmap(Page("page.import.duration"), "Import duration",
            "How long compiling and committing one imported archive takes.",
            "seconds", "imports/s"),
        Heatmap(Page("page.image.size"), "Assembly size",
            "The size of each emitted page assembly, split into byte buckets so an oversized image stands out.",
            "bytes", "compiles/s"),
        Line(Page("page.render.abandoned"), "Abandoned renders",
            "Renders given up on because they outran their execution budget, per second, by page.",
            "renders/s", topk: "page_path"),
        Stacked(Page("page.cache.lookups"), "Cache lookups",
            "Compiled-image cache lookups per second, split by whether each lookup was usable, stale or absent.",
            "lookups/s", split: "result"),
        Stacked(Page("page.cache.staleness"), "Stale images",
            "Why a stored compiled image could not be reused, per second.",
            "images/s", split: "reason"),
        Stacked(Page("page.asset.requests"), "Asset requests",
            "Requests for an asset per second, split by whether it was served, revalidated, unknown or not an asset.",
            "requests/s", split: "outcome"),
        Line(Page("page.layout.fallback"), "Layout fallbacks",
            "Pages served without the default layout because it could not be served, per second, by page.",
            "fallbacks/s", topk: "page_path"),
        Line(Page("page.storage.payload.size"), "Stored payload",
            "Logical bytes held in page revisions and compiled images, excluding indexes and SQLite overhead.",
            "bytes"),
        Line(Page("page.storage.database.size"), "Database size",
            "The whole SQLite database, including indexes and free space, but not the separate write-ahead log.",
            "bytes"),
        Line(Page("page.storage.volume.capacity"), "Volume capacity",
            "Total capacity of the filesystem holding the database; read it against the database size.",
            "bytes"),

        // Assistant: eight histograms and the two counters.
        Heatmap(Assistant("assistant.run.duration"), "Run duration",
            "How long one assistant run takes, from the request to the final answer.",
            "seconds", "runs/s"),
        Heatmap(Assistant("assistant.run.steps"), "Steps per run",
            "How many steps one assistant run used, split into step-count buckets.",
            "steps", "runs/s"),
        Heatmap(Assistant("assistant.run.tool_calls"), "Tool calls per run",
            "How many tool calls one assistant run made, split into call-count buckets.",
            "calls", "runs/s"),
        Heatmap(Assistant("assistant.provider.first_token.duration"), "First token latency",
            "How long the provider takes to send the first token of a response.",
            "seconds", "requests/s"),
        Heatmap(Assistant("assistant.provider.request.duration"), "Provider request duration",
            "How long a provider request takes, from sending it to the last token.",
            "seconds", "requests/s"),
        Heatmap(Assistant("assistant.tool.duration"), "Tool duration",
            "How long one assistant tool call takes to run.",
            "seconds", "calls/s"),
        Line(Assistant("assistant.tokens"), "Tokens",
            "Tokens the provider reported per second, split by kind; the kinds overlap and do not stack.",
            "tokens/s", topk: "kind"),
        Heatmap(Assistant("assistant.memory.recalled"), "Memories per run",
            "How many stored memories each assistant run recalled.",
            "memories", "runs/s"),
        Heatmap(Assistant("assistant.memory.chars"), "Memory characters per run",
            "How many characters of remembered text each assistant run recalled.",
            "chars", "runs/s"),
        Stacked(Assistant("assistant.memory.writes"), "Memory writes",
            "Memory writes per second, split by outcome.",
            "writes/s", split: "outcome"),

        // Host: the framework's meters, which are not instruments this repository registers.
        new Panel {
            Id = "host.memory",
            Group = "Host",
            Title = "Process memory",
            Description = "The process's working set: the memory the runtime currently has resident.",
            YTitle = "bytes",
            Chart = PanelChart.Line,
            Queries = [new PanelQuery("dotnet_process_memory_working_set_bytes", "working set")],
            HostFamilies = ["dotnet_process_memory_working_set_bytes"],
        },
        new Panel {
            Id = "host.cpu",
            Group = "Host",
            Title = "Process CPU",
            Description = "CPU time the process is using, expressed as a number of cores.",
            YTitle = "cores",
            Chart = PanelChart.Line,
            Queries = [new PanelQuery("sum(rate(dotnet_process_cpu_time_seconds_total[5m]))", "cpu")],
            HostFamilies = ["dotnet_process_cpu_time_seconds_total"],
        },
        new Panel {
            Id = "host.connections",
            Group = "Host",
            Title = "Connections and active requests",
            Description = "Kestrel's open and queued connections alongside the requests being handled right now.",
            YTitle = "count",
            Chart = PanelChart.Line,
            Queries = [
                new PanelQuery("sum(http_server_active_requests)", "active requests"),
                new PanelQuery("sum(kestrel_active_connections)", "open connections"),
                new PanelQuery("sum(kestrel_queued_connections)", "queued connections"),
            ],
            HostFamilies = ["http_server_active_requests", "kestrel_active_connections", "kestrel_queued_connections"],
        },
        new Panel {
            Id = "host.request.duration",
            Group = "Host",
            Title = "Request duration",
            Description = "How long HTTP requests take, split into duration buckets.",
            YTitle = "seconds",
            CellUnit = "requests/s",
            Chart = PanelChart.Heatmap,
            Queries = [new PanelQuery("sum by (le) (rate(http_server_request_duration_seconds_bucket[5m]))", string.Empty)],
            HostFamilies = ["http_server_request_duration_seconds"],
        },
        new Panel {
            Id = "host.status.mix",
            Group = "Host",
            Title = "HTTP status mix",
            Description = "Requests per second, stacked by response status code.",
            YTitle = "requests/s",
            Chart = PanelChart.StackedArea,
            Queries = [new PanelQuery("sum by (http_response_status_code) (rate(http_server_request_duration_seconds_count[5m]))", string.Empty)],
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

    private static Panel Heatmap(MetricInstrument instrument, string title, string description, string yTitle, string cellUnit) => new() {
        Id = instrument.Name,
        Group = GroupOf(instrument.Name),
        Title = title,
        Description = description,
        YTitle = yTitle,
        Chart = PanelChart.Heatmap,
        Queries = [new PanelQuery($"sum by (le) (rate({PrometheusNames.Buckets(instrument)}[5m]))", string.Empty)],
        InstrumentName = instrument.Name,
        CellUnit = cellUnit,
    };

    private static Panel Line(MetricInstrument instrument, string title, string description, string yTitle, string? topk = null) {
        // A gauge is a level, not a counter: rate() over it asks for a _total family the exporter never
        // writes, which empties the panel rather than drawing it.
        string query = instrument.Kind == InstrumentKind.Gauge
            ? $"max({PrometheusNames.Family(instrument)})"
            : topk is null
                ? $"sum(rate({PrometheusNames.Total(instrument)}[5m]))"
                : $"topk(8, sum by ({topk}) (rate({PrometheusNames.Total(instrument)}[5m])))";

        return new Panel {
            Id = instrument.Name,
            Group = GroupOf(instrument.Name),
            Title = title,
            Description = description,
            YTitle = yTitle,
            Chart = PanelChart.Line,
            Queries = [new PanelQuery(query, string.Empty)],
            InstrumentName = instrument.Name,
        };
    }

    private static Panel Stacked(MetricInstrument instrument, string title, string description, string yTitle, string split) => new() {
        Id = instrument.Name,
        Group = GroupOf(instrument.Name),
        Title = title,
        Description = description,
        YTitle = yTitle,
        Chart = PanelChart.StackedArea,
        Queries = [new PanelQuery($"sum by ({split}) (rate({PrometheusNames.Total(instrument)}[5m]))", string.Empty)],
        InstrumentName = instrument.Name,
        PartsOfAWhole = split,
    };

    // One request's stages partition its time, so their rates stack. Each band is the rate of a
    // histogram's observations sum: its buckets answer "how many were slow", not "how long it took",
    // so a bucket cannot be the stack's value. label_replace returns the series with a stage label
    // even after rate() has dropped __name__, because ".*" matches the missing label's empty value;
    // `or` keeps whichever instruments have samples, so a warm site with no lazy compiles still
    // draws the load half of load/compile.
    private static Panel RenderChain() {
        MetricInstrument wait = Page("page.render.wait");
        MetricInstrument compile = Page("page.compile.duration");
        MetricInstrument load = Page("page.load.duration");
        MetricInstrument invoke = Page("page.invoke.duration");

        string query =
            "sum by (stage) (" +
            $"label_replace(rate({PrometheusNames.Sum(wait)}[5m]), \"stage\", \"wait\", \"__name__\", \".*\")" +
            $" or label_replace(rate({PrometheusNames.Sum(compile)}{{reason=\"lazy\"}}[5m]), \"stage\", \"load/compile\", \"__name__\", \".*\")" +
            $" or label_replace(rate({PrometheusNames.Sum(load)}[5m]), \"stage\", \"load/compile\", \"__name__\", \".*\")" +
            $" or label_replace(rate({PrometheusNames.Sum(invoke)}[5m]), \"stage\", \"render\", \"__name__\", \".*\")" +
            ")";

        return new Panel {
            Id = "page.render.chain",
            Group = "Pages",
            Title = "Render chain",
            Description = "How a request's time is spent, split into waiting for a slot, loading or compiling the page's image, and running the page's code.",
            YTitle = "seconds/s",
            Chart = PanelChart.StackedArea,
            Queries = [new PanelQuery(query, string.Empty)],
            SourceInstruments = [wait.Name, compile.Name, load.Name, invoke.Name],
            PartsOfAWhole = "stage",
        };
    }

    private static string GroupOf(string instrumentName) =>
        instrumentName.StartsWith("page.", StringComparison.Ordinal) ? "Pages"
        : instrumentName.StartsWith("assistant.", StringComparison.Ordinal) ? "Assistant"
        : "Host";
}

/// <summary>One chart on the status page.</summary>
internal sealed record Panel {

    /// <summary>The panel's id: the instrument's name, or a named id for a composed or Host panel.</summary>
    public required string Id { get; init; }

    /// <summary>The group the panel is listed under.</summary>
    public required string Group { get; init; }

    /// <summary>The heading the drawing carries.</summary>
    public required string Title { get; init; }

    /// <summary>A sentence saying what the chart shows, for the title's hover text and the drawing's description.</summary>
    public required string Description { get; init; }

    /// <summary>What the value axis measures. A heatmap's is its bucket unit.</summary>
    public required string YTitle { get; init; }

    /// <summary>How the panel is drawn.</summary>
    public required PanelChart Chart { get; init; }

    /// <summary>The queries whose answers the drawing is made from.</summary>
    public required IReadOnlyList<PanelQuery> Queries { get; init; }

    /// <summary>What a heatmap's cells count, per second, for its legend and its hover text.</summary>
    public string CellUnit { get; init; } = string.Empty;

    /// <summary>The instrument the panel draws, or null for a panel that composes instruments or reads the framework's.</summary>
    public string? InstrumentName { get; init; }

    /// <summary>The instruments a composed panel reads, when it owns none.</summary>
    public IReadOnlyList<string> SourceInstruments { get; init; } = [];

    /// <summary>The framework families a Host panel reads.</summary>
    public IReadOnlyList<string> HostFamilies { get; init; } = [];

    /// <summary>The label whose values partition one total, when the panel is a stacked area.</summary>
    public string? PartsOfAWhole { get; init; }
}

/// <summary>One PromQL expression a panel runs, and the name to give an anonymous series.</summary>
/// <param name="Query">The expression.</param>
/// <param name="Label">The label a series without one takes.</param>
internal sealed record PanelQuery(string Query, string Label);

/// <summary>How a panel is drawn.</summary>
internal enum PanelChart {

    /// <summary>A distribution over buckets, drawn as a heatmap.</summary>
    Heatmap,

    /// <summary>A value over time, one line per label.</summary>
    Line,

    /// <summary>Parts of one total, stacked.</summary>
    StackedArea,
}

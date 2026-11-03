// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

internal static class PageAssetSeeder {

    internal const string StylesheetBody = """
        /* SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com> */
        /* SPDX-License-Identifier: MIT */

        body {
            margin: 0 auto;
            max-width: 40rem;
            padding: 2rem;
            font-family: system-ui, sans-serif;
            line-height: 1.6;
        }

        .diagnostics {
            padding: 1rem;
            overflow-x: auto;
            background-color: #f5f5f5;
            border-left: 0.25rem solid #999;
        }

        .diagnostics .diagnostic-error {
            color: #c00;
        }

        .diagnostics .diagnostic-warning {
            color: #8a5a00;
        }

        :root {
            --series-1: #1f77b4;
            --series-2: #ff7f0e;
            --series-3: #2ca02c;
            --series-4: #d62728;
            --series-5: #9467bd;
        }

        .banner {
            padding: 0.75rem 1rem;
            background-color: #fdf0f0;
            border-left: 0.25rem solid #c00;
        }

        .panel {
            margin-block: 2rem;
        }

        .status-nav {
            display: flex;
            flex-wrap: wrap;
            gap: 1rem;
            font-size: 0.875rem;
        }

        .status-nav .current {
            font-weight: 600;
        }

        .chart {
            display: block;
            width: 100%;
            height: auto;
        }

        .chart .chart-grid {
            stroke: #ddd;
            stroke-width: 1;
            vector-effect: non-scaling-stroke;
        }

        .chart-label,
        .chart-axis-title {
            fill: #444;
            font-size: 11px;
        }

        .chart-axis-title {
            fill: #666;
        }

        .series-1 { --series: var(--series-1); }
        .series-2 { --series: var(--series-2); }
        .series-3 { --series: var(--series-3); }
        .series-4 { --series: var(--series-4); }
        .series-5 { --series: var(--series-5); }

        .chart .series {
            fill: none;
            stroke: var(--series, currentColor);
            stroke-width: 2;
            vector-effect: non-scaling-stroke;
        }

        .chart circle.series {
            fill: var(--series, currentColor);
            stroke: none;
        }

        .chart-legend {
            display: flex;
            flex-wrap: wrap;
            gap: 1rem;
            margin: 0.5rem 0 0;
            padding: 0;
            list-style: none;
            font-size: 0.875rem;
        }

        .chart-legend .swatch {
            display: inline-block;
            width: 0.75rem;
            height: 0.75rem;
            margin-right: 0.35rem;
            background-color: var(--series, currentColor);
        }

        .heatmap {
            display: block;
            width: 100%;
            height: auto;
        }

        .heat-1 { fill: #dbeafe; }
        .heat-2 { fill: #93c5fd; }
        .heat-3 { fill: #3b82f6; }
        .heat-4 { fill: #1d4ed8; }
        .heat-5 { fill: #1e3a8a; }
        """;

    internal const string IconBody = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" role="img" aria-labelledby="title">
          <title id="title">Rocket</title>
          <path fill="#17324d" d="M46.8 7.2c-10.6 1.1-20.2 7.6-25.3 17.1l-9.2 1.5-6.1 8.6 14.1 1.4 8.5 8.5 1.4 14.1 8.6-6.1 1.5-9.2c9.5-5.1 16-14.7 17.1-25.3l.9-10.5-11.5-.1z"/>
          <path fill="#f07845" d="m18.7 38.8-8.8 8.8 6.5 1.1 1.1 6.5 8.8-8.8z"/>
          <circle cx="43.2" cy="20.8" r="5.1" fill="#8ed8ca"/>
          <path fill="#f5c85b" d="m13.9 26.4-5.1 7.2 7.9.8 3.8-6.6z"/>
        </svg>
        """;

    internal const string StylesheetTemplate = StylesheetBody
        + "<% ctx.SetContentType(\"text/css; charset=utf-8\"); ctx.SetHeader(\"Cache-Control\", \"no-cache\"); %>";

    internal const string IconTemplate = IconBody
        + "<% ctx.SetContentType(\"image/svg+xml\"); ctx.SetHeader(\"Cache-Control\", \"no-cache\"); %>";

    public static async Task SeedAsync(IPageStore pages, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(pages);

        (string Path, string Content)[] assets = [
            ("/site.css", StylesheetTemplate),
            ("/rocket.svg", IconTemplate),
        ];

        foreach ((string path, string content) in assets) {
            PagePath key = PagePath.Normalize(path);
            if (await pages.GetPageAsync(key, cancellationToken) is not null) {
                continue;
            }

            await pages.SavePageAsync(new StoredPage(key, content), cancellationToken);
        }
    }
}

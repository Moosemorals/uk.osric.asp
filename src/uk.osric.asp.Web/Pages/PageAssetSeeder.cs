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
        <svg version="1.1" baseProfile="full" xmlns="http://www.w3.org/2000/svg"
            viewBox="0 0 32 32"
        >
            <!--
        Copyright (c) 2020 Osric Wilkinson (osric@fluffypeople.com)

        Permission to use, copy, modify, and/or distribute this software for any
        purpose with or without fee is hereby granted, provided that the above
        copyright notice and this permission notice appear in all copies.

        THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH
        REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND
        FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
        INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM
        LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR
        OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR
        PERFORMANCE OF THIS SOFTWARE.

            -->
            <style type="text/css">
                .r {
                fill: white;
                stroke: black;
                stroke-width: 0.2px;
                stroke-linejoin: round;
                stroke-linecap: round;
                }
                .fin.right {fill: url('#rightWing');}
                .fin.left {fill: url('#leftWing')}
                .fin.top {fill: url('#topWing')}
                .hull{fill: url('#hullPaint');}
                .nose, .aerial {fill: url('#noseCone')}
                .engine {fill: url('#engineCone')}
                .exhaust {stroke: none; fill: url('#thrust');}
                .cabin {fill: url('#window');}
            </style>
            <defs>
                <linearGradient id="thrust" x1="1" y1="0.5" x2="0" y2="0.5">
                    <!-- Engine exhaust cone, red to orange to yellow, fading out further from the ship  -->
                    <stop style="stop-color:hsl(0,100%,50%)" offset="0%"/>
                    <stop style="stop-color:hsl(30,100%,50%);stop-opacity:0.5" offset="50%"/>
                    <stop style="stop-color:hsl(60,100%,50%);stop-opacity:0" offset="100%"/>
                </linearGradient>
                <linearGradient id="hullPaint" x1="0.5" y1="0" x2="0.5" y2="1">
                    <!-- Along the axis of the hull to make it look rounded-->
                    <stop style="stop-color:hsl(200,100%,30%)" offset="0%"/>
                    <stop style="stop-color:hsl(200,100%,75%)" offset="45%"/>
                    <stop style="stop-color:hsl(200,100%,75%)" offset="55%"/>
                    <stop style="stop-color:hsl(200,100%,30%)" offset="100%"/>
                </linearGradient>
                <linearGradient id="noseCone" x1="0.5" y1="0" x2="0.5" y2="1">
                    <!-- A tighter version of hull for a smaller shape -->
                    <stop style="stop-color:hsl(200,100%,30%)" offset="0%"/>
                    <stop style="stop-color:hsl(200,100%,75%)" offset="50%"/>
                    <stop style="stop-color:hsl(200,100%,30%)" offset="100%"/>
                </linearGradient>
                <linearGradient id="engineCone" x1="0.5" y1="0" x2="0.5" y2="1">
                    <!-- Same as noseCone, but in grey -->
                    <stop style="stop-color:hsl(0,0%,30%)" offset="0%"/>
                    <stop style="stop-color:hsl(0,0%,75%)" offset="50%"/>
                    <stop style="stop-color:hsl(0,0%,30%)" offset="100%"/>
                </linearGradient>
                <linearGradient id="leftWing" x1="1.05" y1="0" x2="-0.25" y2="1">
                    <!--
                    Left/Right wings are the same - the axis of the gradient
                    should run along the center of the triangle from the tip
                    to the hull. (Actually, it's guesswork).
                    Top wing same colors but inverted.
                    -->
                    <stop style="stop-color:hsl(230,60%,30%)" offset="30%"/>
                    <stop style="stop-color:hsl(230,60%,60%)" offset="50%"/>
                    <stop style="stop-color:hsl(230,60%,30%)" offset="70%"/>
                </linearGradient>
                <linearGradient id="rightWing" x1="1.05" y1="1" x2="-0.25" y2="0">
                    <stop style="stop-color:hsl(230,60%,30%)" offset="30%"/>
                    <stop style="stop-color:hsl(230,60%,60%)" offset="50%"/>
                    <stop style="stop-color:hsl(230,60%,30%)" offset="70%"/>
                </linearGradient>
                <linearGradient id="topWing" x1="0.5" y1="0" x2="0.5" y2="1">
                    <stop style="stop-color:hsl(230,60%,60%)" offset="10%"/>
                    <stop style="stop-color:hsl(230,60%,30%)" offset="50%"/>
                    <stop style="stop-color:hsl(230,60%,60%)" offset="90%"/>
                </linearGradient>
                <linearGradient id="sky" x1="0" y1="1" x2="0" y2="0">
                    <!-- Simple blue to black graidient -->
                    <stop style="stop-color:hsl(200, 100%, 50%)" offset="20%"/>
                    <stop style="stop-color:hsl(200, 10%, 10%)" offset="90%"/>
                </linearGradient>
                <radialGradient id="window" cx="0.7" cy="0.3">
                    <!-- Rough specular highlight -->
                    <stop style="stop-color:#fff" offset="20%"/>
                    <stop style="stop-color:hsl(180,90%,50%)" offset="100%"/>
                </radialGradient>
            </defs>
            <!--
            <rect id="skyBox" x="0" y="0" width="100%" height="100%" fill="url('#sky')" />
            -->
            <svg viewBox="-16 -16 32 32" x="10%" y="10%" height="80%" width="80%" id="rocket">
                <g transform="rotate(-30) translate(3, 0)">
                    <!-- Fins are triangles with slightly curved edges -->
                    <path class="fin left r" d="M-6 -1 C-6 -2 -9 -5 -10 -6C-11 -7 -1 -2 0 -1Z"/>
                    <path class="fin right r" d="M-6 1 C-6 2 -9 5 -10 6C-11 7 -1 2 0 1Z"/>
                    <!-- The engine. A truncated cone -->
                    <path class="exhaust r" d="M-9 -1L-15.5 -3L-15.5 3L-9 1Z"/>
                    <!-- Another truncated cone -->
                    <path class="engine r" d="M-8 -0.5 L-9.5 -1.25L-9.5 1.25L-8 0.5Z"/>
                    <!-- An oblong with slightly bulging long edges -->
                    <path class="hull r" d="M-8 -1C-6 -3 6 -3 8 -1L8 1C6 3 -6 3 -8 1L-8 -1Z"/>
                    <!--
                    Top view of a fin. An aerofoil (a very narrow bulging rectangle)
                    Located after the hull because it's drawn on top
                    -->
                    <path class="fin top r" d="M0 0C-2 -1 -8 -1 -11 0C-8 1 -2 1 0 0L-11 0Z"/>

                    <!-- A triangle with concave edges -->
                    <path class="nose r" d="M8 -1Q9 -0.3 10 0Q9 0.3 8 1M8 -1Z"/>

                    <!-- A line with a circle on the end -->
                    <path class="aerial r" d=" M10 0 L10.5 0 A0.25 0.25 180 1 1 11 0 A0.25 0.25 180 1 1 10.5 0 Z"/>

                    <!-- Three cabin windows -->
                    <circle cx="6" cy="0" r="0.75" class="cabin r"/>
                    <circle cx="3.9" cy="0" r="0.75" class="cabin r"/>
                    <circle cx="1.8" cy="0" r="0.75" class="cabin r"/>
                </g>
            </svg>
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
            if (await pages.GetHeadAsync(key, cancellationToken) is not null) {
                continue;
            }

            await pages.SavePageAsync(new StoredPage(key, content), cancellationToken);
        }
    }
}

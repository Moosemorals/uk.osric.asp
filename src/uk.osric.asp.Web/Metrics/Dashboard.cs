// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

using System.Globalization;

/// <summary>Runs the catalogue's queries for one window and remembers the page for a few seconds.</summary>
/// <remarks>
/// The page is refreshed by a person and every view runs twenty-eight range queries, so a refresh loop
/// must not become a load test. Windows are cached by their snapped start and length, in a small
/// bounded map: a link may name any start, and a route the owner can call must not grow a dictionary
/// without a limit. A remote failure is a banner, never an exception, so the page still renders.
/// </remarks>
/// <param name="renderer">Draws one panel at a time.</param>
/// <param name="time">The clock a cache entry's age is measured against.</param>
/// <param name="logger">Where an incomplete page is explained.</param>
internal sealed class Dashboard(ChartRenderer renderer, TimeProvider time, ILogger<Dashboard> logger) {

    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(15);
    private const int MaxCachedWindows = 16;

    private readonly Lock _gate = new();
    private readonly Dictionary<(long Start, long Length), CachedPage> _cache = [];

    /// <summary>Builds the page for one window, reusing the last one while it is still fresh.</summary>
    /// <param name="window">The start and length to draw.</param>
    /// <param name="cancellationToken">Aborted when the request is abandoned.</param>
    public async Task<StatusPage> GetAsync(ChartWindow window, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(window);

        DateTimeOffset now = time.GetUtcNow();
        (long, long) key = (window.Start.UtcTicks, window.Length.Ticks);

        lock (_gate) {
            if (_cache.TryGetValue(key, out CachedPage? cached) && now - cached.At < CacheFor) {
                return cached.Page;
            }
        }

        StatusPage page = await BuildAsync(window, cancellationToken);

        lock (_gate) {
            _cache[key] = new CachedPage(page, now);
            while (_cache.Count > MaxCachedWindows) {
                (long, long) oldest = _cache.MinBy(entry => entry.Value.At).Key;
                _cache.Remove(oldest);
            }
        }

        return page;
    }

    /// <summary>The URL that draws one panel for the same window, for the figure's caption link.</summary>
    /// <param name="panel">The panel.</param>
    /// <param name="window">The window.</param>
    internal static string Href(Panel panel, ChartWindow window) {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(window);

        string start = window.Start.ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        string duration = ((long)window.Length.TotalMinutes).ToString(CultureInfo.InvariantCulture);

        return $"/status/chart?panel={Uri.EscapeDataString(panel.Id)}&start={Uri.EscapeDataString(start)}&duration={duration}";
    }

    private async Task<StatusPage> BuildAsync(ChartWindow window, CancellationToken cancellationToken) {
        List<StatusPanel> panels = [];
        string? banner = null;

        foreach (Panel panel in StatusCatalogue.Panels) {
            ChartResult result = await renderer.RenderAsync(panel, window, cancellationToken);

            if (result.Svg is null) {
                banner ??= result.Reason;
                continue;
            }

            panels.Add(new StatusPanel(panel, result.Svg, Href(panel, window)));
        }

        if (banner is not null) {
            // The page shows the banner; the journal keeps the reason for the person with a shell.
            logger.LogWarning("The metrics dashboard is incomplete: {Reason}", banner);
        }

        return new StatusPage(panels, banner);
    }

    private sealed record CachedPage(StatusPage Page, DateTimeOffset At);
}

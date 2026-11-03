// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;
/// <summary>One window the status page can show, and the chart it draws.</summary>
/// <remarks>
/// The token is matched, never parsed: <c>1h</c> and <c>3h</c> are two windows to choose between, not
/// a syntax for any length of time. An unknown token is the default window rather than an error,
/// because the page is opened by hand and a stale bookmark should still draw.
/// </remarks>
/// <param name="Token">The value carried in the <c>duration</c> query string, and the nav's label.</param>
/// <param name="Window">How far back the Prometheus queries reach.</param>
/// <param name="ChartWidth">The drawing width in SVG user units, wider for a longer window.</param>
internal sealed record StatusDuration(string Token, TimeSpan Window, int ChartWidth) {

    // Prometheus refuses a range query over 11,000 points and the SVG is heaviest at one column per
    // sample, so a long window samples more sparsely instead of sending 43,200 points for 30 days.
    private const int TargetSamples = 500;

    /// <summary>The window a request without a usable <c>duration</c> gets.</summary>
    public static StatusDuration Default { get; } = new("1h", TimeSpan.FromHours(1), 640);

    /// <summary>Every window, in the order the nav lists them.</summary>
    public static IReadOnlyList<StatusDuration> All { get; } = [
        Default,
        new("3h", TimeSpan.FromHours(3), 720),
        new("8h", TimeSpan.FromHours(8), 800),
        new("1d", TimeSpan.FromDays(1), 880),
        new("7d", TimeSpan.FromDays(7), 1040),
        new("30d", TimeSpan.FromDays(30), 1200),
    ];

    /// <summary>The sample spacing: a minute, widened in whole minutes so a long window stays small.</summary>
    public TimeSpan Step {
        get {
            double minutes = Math.Max(1, Math.Ceiling(Window.TotalSeconds / TargetSamples / 60));
            return TimeSpan.FromMinutes(minutes);
        }
    }

    /// <summary>Finds the window a token names.</summary>
    /// <param name="token">The raw query string value; null when the parameter is absent.</param>
    /// <returns>The matching window, or <see cref="Default"/> when nothing matches.</returns>
    public static StatusDuration FromToken(string? token) {
        return All.FirstOrDefault(candidate => string.Equals(candidate.Token, token, StringComparison.Ordinal))
            ?? Default;
    }
}

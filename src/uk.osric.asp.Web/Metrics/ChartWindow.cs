// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

/// <summary>One period a chart is drawn over: an explicit start and a length.</summary>
/// <remarks>
/// A window is a value, not "the last hour": the start is snapped down to the sample step, so the same
/// start and length are the same query. A page's window ends at the current step, and a link's window
/// is whatever it named — which is what lets a chart URL reproduce the picture it showed.
/// </remarks>
/// <param name="Start">The left edge, snapped to the step.</param>
/// <param name="Length">How far the window reaches.</param>
internal sealed record ChartWindow(DateTimeOffset Start, TimeSpan Length) {

    // Prometheus refuses a range query over 11,000 points and the SVG is heaviest at one column per
    // sample, so a long window samples more sparsely instead of sending 43,200 points for 30 days.
    private const int TargetSamples = 500;

    /// <summary>The length a request that names no window gets.</summary>
    public static TimeSpan DefaultLength { get; } = TimeSpan.FromHours(1);

    /// <summary>The windows the nav offers, in the order it lists them.</summary>
    /// <remarks>
    /// A token is matched, never parsed: <c>1h</c> and <c>3h</c> are two windows to choose between, not
    /// a syntax for any length of time. A stale bookmark that names an unknown token gets the default
    /// rather than an error, because the page is opened by hand.
    /// </remarks>
    public static IReadOnlyList<WindowToken> Tokens { get; } = [
        new("1h", DefaultLength),
        new("3h", TimeSpan.FromHours(3)),
        new("8h", TimeSpan.FromHours(8)),
        new("1d", TimeSpan.FromDays(1)),
        new("7d", TimeSpan.FromDays(7)),
        new("30d", TimeSpan.FromDays(30)),
    ];

    /// <summary>The right edge of the window.</summary>
    public DateTimeOffset End => Start + Length;

    /// <summary>The spacing of the samples the window asks for.</summary>
    public TimeSpan Step => StepFor(Length);

    /// <summary>Builds a window from an explicit start and length, snapping the start to the step.</summary>
    /// <param name="start">The left edge the caller asked for.</param>
    /// <param name="length">How far the window reaches; it must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">The length is zero or negative.</exception>
    public static ChartWindow At(DateTimeOffset start, TimeSpan length) {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero);

        return new ChartWindow(Snap(start, StepFor(length)), length);
    }

    /// <summary>Builds the window a duration token names, ending at the current step.</summary>
    /// <param name="token">The token from the query string; null or unknown means the default.</param>
    /// <param name="now">The instant the window ends at, before snapping.</param>
    public static ChartWindow FromToken(string? token, DateTimeOffset now) {
        TimeSpan length = LengthForToken(token);

        return At(now - length, length);
    }

    /// <summary>The token that names the window being shown.</summary>
    /// <param name="token">The token from the query string; null or unknown means the default.</param>
    public static string TokenFor(string? token) =>
        Tokens.Any(candidate => string.Equals(candidate.Token, token, StringComparison.Ordinal))
            ? token!
            : Tokens[0].Token;

    /// <summary>The length a token names, or the default when it names none.</summary>
    /// <param name="token">The token from the query string; null or unknown means the default.</param>
    public static TimeSpan LengthForToken(string? token) =>
        Tokens.FirstOrDefault(candidate => string.Equals(candidate.Token, token, StringComparison.Ordinal))?.Length
            ?? DefaultLength;

    /// <summary>The sample spacing a length needs to stay under the sample target.</summary>
    /// <param name="length">How far the window reaches.</param>
    public static TimeSpan StepFor(TimeSpan length) {
        double minutes = Math.Max(1, Math.Ceiling(length.TotalSeconds / TargetSamples / 60));

        return TimeSpan.FromMinutes(minutes);
    }

    // Snapping keeps a window a stable thing: two requests a few seconds apart are the same query, and
    // a bookmark reproduces the chart it showed.
    private static DateTimeOffset Snap(DateTimeOffset start, TimeSpan step) {
        long ticks = step.Ticks;

        return new DateTimeOffset(start.UtcTicks / ticks * ticks, TimeSpan.Zero);
    }

    /// <summary>One window the nav offers.</summary>
    /// <param name="Token">The value carried in the <c>duration</c> query string, and the nav's label.</param>
    /// <param name="Length">How far back the queries reach.</param>
    public sealed record WindowToken(string Token, TimeSpan Length);
}

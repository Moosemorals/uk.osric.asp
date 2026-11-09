// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

using System.Globalization;
using uk.osric.asp.Web.Http;

/// <summary>Serves one panel's drawing on its own, for a link or a bookmark.</summary>
/// <remarks>
/// <para>
/// The addon, not the page's assembly line: the page embeds the same renderer's output and never asks
/// this route for it. A URL names a <c>panel</c>, a <c>start</c> and a <c>duration</c>, and the
/// drawing is a function of those three, so a link reproduces the chart it showed.
/// </para>
/// <para>
/// A malformed window is refused rather than defaulted: a hotlink is a claim about a period, and one
/// that silently drew a different period would be a lie. The page's own <c>?duration=</c> stays
/// lenient, because that is navigation typed by hand.
/// </para>
/// </remarks>
internal static class ChartEndpoint {

    /// <summary>Answers <c>GET /status/chart</c> for the owner.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="dashboard">Builds the drawings, and holds the cache the page shares.</param>
    /// <param name="time">The clock a window without a start ends at.</param>
    /// <param name="cancellationToken">Aborted when the request is abandoned.</param>
    public static async Task HandleAsync(
        HttpContext context,
        Dashboard dashboard,
        TimeProvider time,
        CancellationToken cancellationToken) {

        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(time);

        Panel? panel = StatusCatalogue.Find(context.Request.Query["panel"]);
        if (panel is null) {
            await RefuseAsync(context, StatusCodes.Status404NotFound, "No panel has that id.", cancellationToken);
            return;
        }

        if (!TryWindow(context.Request.Query, time.GetUtcNow(), out ChartWindow window, out string? problem)) {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, problem!, cancellationToken);
            return;
        }

        StatusPage page = await dashboard.GetAsync(window, cancellationToken);
        StatusPanel? drawing = page.Panels.FirstOrDefault(candidate => candidate.Panel.Id == panel.Id);

        if (drawing is null) {
            // A broken image is the honest failure for a pasted link; the page is where the reason is read.
            await RefuseAsync(context, StatusCodes.Status503ServiceUnavailable, page.Banner ?? "That chart could not be drawn.", cancellationToken);
            return;
        }

        context.Response.ContentType = "image/svg+xml; charset=utf-8";
        // The owner's data, and the fifteen-second cache belongs to this process rather than to a proxy.
        SecurityHeaders.NoStore(context);
        await context.Response.WriteAsync(drawing.Svg, cancellationToken);
    }

    private static bool TryWindow(IQueryCollection query, DateTimeOffset now, out ChartWindow window, out string? problem) {
        window = ChartWindow.At(now, ChartWindow.DefaultLength);
        problem = null;

        DateTimeOffset start = now;
        if (query["start"] is { Count: > 0 } value) {
            if (!DateTimeOffset.TryParse(value.ToString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out start)) {
                problem = "start must be an ISO-8601 instant, such as 2026-11-09T12:00:00Z.";
                return false;
            }
        }

        TimeSpan length = ChartWindow.DefaultLength;
        if (query["duration"] is { Count: > 0 } duration && !TryLength(duration.ToString(), out length)) {
            problem = "duration must be a window token (1h, 3h, 8h, 1d, 7d, 30d) or a whole number of minutes.";
            return false;
        }

        window = ChartWindow.At(start, length);

        return true;
    }

    private static bool TryLength(string value, out TimeSpan length) {
        length = ChartWindow.LengthForToken(value);

        if (ChartWindow.Tokens.Any(token => string.Equals(token.Token, value, StringComparison.Ordinal))) {
            return true;
        }

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) && minutes > 0) {
            length = TimeSpan.FromMinutes(minutes);
            return true;
        }

        return false;
    }

    private static Task RefuseAsync(HttpContext context, int status, string reason, CancellationToken cancellationToken) {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";
        SecurityHeaders.NoStore(context);

        return context.Response.WriteAsync(reason, cancellationToken);
    }
}
